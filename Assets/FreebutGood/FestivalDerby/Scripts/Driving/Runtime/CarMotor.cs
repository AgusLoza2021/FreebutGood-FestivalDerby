using System;
using FestivalDerby.Driving;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Arcade raycast-suspension handling model. Four spring-damper raycasts carry the chassis,
    /// per-axle lateral grip scrubs sideways velocity, the rear axle drives and the front axle
    /// steers. It is deliberately loose: a car can launch, spin and land on its roof, then
    /// self-right after a moment. Every force is bounded by a cap or by the tick's remaining
    /// headroom, the whole step allocates nothing, and tuning is copied once at construction so a
    /// mutated asset can never change a running car.
    ///
    /// It implements <see cref="ICarMotor"/> so the driver can hold either handling model without a call
    /// site knowing which one it has. The behaviour of this class is unchanged by that: the interface was
    /// written to the surface this motor already published.
    /// </summary>
    public sealed class CarMotor : ICarMotor
    {
        // The grip fractions are authored for a 50 Hz tick; converting them to a rate with
        // k = -ln(1 - f) / ReferenceStep keeps the feel identical at any other fixed step.
        private const float ReferenceStep = 0.02f;
        private const int WheelCount = 4;
        private const int WheelBufferSize = 8;
        private const float TiltThresholdDegrees = 50f;
        private const float SelfRightDelaySeconds = 1f;
        private const float SelfRightSpeedLimit = 1.5f;
        private const float YawEngageSpeed = 3f;
        // Extra ray past the suspension's rest length, so a wheel keeps sensing while compressed.
        private const float MaxSuspensionTravel = 0.35f;
        private const int FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3;


        private readonly Rigidbody _body;

        private readonly float _driveForce;
        private readonly float _maxForwardSpeed;
        private readonly float _maxReverseSpeed;
        private readonly float _reverseEngageSpeed;
        private readonly float _brakeDeceleration;
        private readonly float _coastDrag;
        private readonly float _maxSteerAngleDegrees;
        private readonly float _highSpeedSteerAngleDegrees;
        private readonly float _steeringSmoothing;
        private readonly float _maxYawRateRad;
        private readonly float _yawAssist;
        private readonly float _steeringDeadZone;
        private readonly float _frontGripRate;
        private readonly float _rearGripRate;
        private readonly float _rearDriftGripRate;
        private readonly float _maxLateralForce;
        private readonly float _suspensionRestLength;
        private readonly float _suspensionSpring;
        private readonly float _suspensionDamper;
        private readonly float _maxSuspensionForce;
        private readonly float _antiRoll;
        private readonly float _downforceCoefficient;
        private readonly float _airControlTorque;
        private readonly float _uprightTorque;
        private readonly float _groundAngularDamping;
        private readonly float _airAngularDamping;
        private readonly float _nitroDuration;
        private readonly float _nitroAccelerationMultiplier;
        private readonly float _nitroTopSpeedMultiplier;
        private readonly float _nitroCooldown;

        // Pre-allocated per-wheel state: the hot step must never allocate.
        private readonly Vector3[] _anchors = new Vector3[WheelCount];
        private readonly bool[] _grounded = new bool[WheelCount];
        private readonly float[] _compression = new float[WheelCount];
        private readonly RaycastHit[] _hits = new RaycastHit[WheelBufferSize];
        private readonly float _anchorRayLength;

        private float _steer;
        private float _flipSeconds;
        private bool _airDampingActive;
        private bool _nitroReady = true;
        private float _boostRemaining;
        private float _nitroCooldownRemaining;


        // Terrain grip the surface under the car resolved to. It is not damage: it survives a respawn
        // and never touches the damage-zone scales, so the two multipliers compose instead of
        // overwriting each other. Neutral is 1, which is the value on asphalt and before any surface
        // is sampled, so a run with surfaces off steps exactly as it did before this existed.
        private float _surfaceGripScale = 1f;
        private const float MinSurfaceGripScale = 0.2f;
        private const float MaxSurfaceGripScale = 2f;

        /// <summary>Last mode resolved by a step that actually ran; Forward until the first such step.</summary>
        public CarControlMode ResolvedMode { get; private set; }

        /// <summary>
        /// Any wheel touching a surface in the last step. Read-only presentation view, so the world
        /// VFX can refuse to draw skid marks or drift smoke while the car is airborne.
        /// </summary>
        public bool IsGrounded
        {
            get { return _grounded[FrontLeft] || _grounded[FrontRight] || _grounded[RearLeft] || _grounded[RearRight]; }
        }

        /// <summary>True while the nitro boost still has time left. Read-only presentation view.</summary>
        public bool IsBoosting
        {
            get { return _boostRemaining > 0f; }
        }

        /// <summary>
        /// Absolute lateral speed of the rear axle in m/s: the slip the drift smoke and skid marks are
        /// tuned against. Zero when the rear axle is airborne or the chassis basis degenerates, so the
        /// caller never sees a stale value, and it reads the same basis grip uses (body up, forward on
        /// the ground plane) so the presentation and the physics agree on what sliding means.
        /// </summary>
        public float RearSlipSpeed
        {
            get
            {
                if (_body == null || (!_grounded[RearLeft] && !_grounded[RearRight]))
                {
                    return 0f;
                }

                Vector3 up = _body.transform.up;
                Vector3 forward = Vector3.ProjectOnPlane(_body.transform.forward, up);
                if (!IsFinite(up) || !IsFinite(forward) || up.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f)
                {
                    return 0f;
                }

                up.Normalize();
                forward.Normalize();
                Vector3 right = Vector3.Cross(up, forward);
                if (right.sqrMagnitude < 1e-6f)
                {
                    return 0f;
                }

                Vector3 rearAxle = _body.transform.TransformPoint((_anchors[RearLeft] + _anchors[RearRight]) * 0.5f);
                float lateral = Vector3.Dot(_body.GetPointVelocity(rearAxle), right);
                return IsFinite(lateral) ? Mathf.Abs(lateral) : 0f;
            }
        }

        public CarMotor(Rigidbody body, CarDefinition definition)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            _body = body;

            // Copy the tuning once with the same safe-minima semantics as CarDefinition.Sanitize;
            // the asset itself must never be read or written after construction.
            _driveForce = FiniteMinimum(definition.driveForce, 0f);
            _maxForwardSpeed = FiniteMinimum(definition.maxForwardSpeed, 0f);
            _maxReverseSpeed = FiniteMinimum(definition.maxReverseSpeed, 0f);
            _reverseEngageSpeed = FiniteMinimum(definition.reverseEngageSpeed, 0f);
            _brakeDeceleration = FiniteMinimum(definition.brakeDeceleration, 0f);
            _coastDrag = FiniteMinimum(definition.coastDrag, 0f);
            _maxSteerAngleDegrees = FiniteMinimum(definition.maxSteerAngleDegrees, 0f);
            _highSpeedSteerAngleDegrees = FiniteMinimum(definition.highSpeedSteerAngleDegrees, 0f);
            _steeringSmoothing = FiniteMinimum(definition.steeringSmoothing, 0f);
            _maxYawRateRad = FiniteMinimum(definition.maxYawRateDegrees, 0f) * Mathf.Deg2Rad;
            _yawAssist = FiniteMinimum(definition.yawAssist, 0f);
            _steeringDeadZone = Mathf.Clamp01(FiniteMinimum(definition.steeringDeadZone, 0f));
            _frontGripRate = LateralRate(definition.frontGrip);
            _rearGripRate = LateralRate(definition.rearGrip);
            _rearDriftGripRate = LateralRate(definition.rearDriftGrip);
            _maxLateralForce = FiniteMinimum(definition.maxLateralForce, 0f);
            _suspensionRestLength = FiniteMinimum(definition.suspensionRestLength, 0f);
            _suspensionSpring = FiniteMinimum(definition.suspensionSpring, 0f);
            _suspensionDamper = FiniteMinimum(definition.suspensionDamper, 0f);
            _maxSuspensionForce = FiniteMinimum(definition.maxSuspensionForce, 0f);
            _antiRoll = FiniteMinimum(definition.antiRoll, 0f);
            _downforceCoefficient = FiniteMinimum(definition.downforceCoefficient, 0f);
            _airControlTorque = FiniteMinimum(definition.airControlTorque, 0f);
            _uprightTorque = FiniteMinimum(definition.uprightTorque, 0f);
            _groundAngularDamping = FiniteMinimum(definition.angularDamping, 0f);
            _airAngularDamping = FiniteMinimum(definition.airAngularDamping, 0f);
            _nitroDuration = FiniteMinimum(definition.nitroDuration, 0f);
            _nitroAccelerationMultiplier = FiniteMinimum(definition.nitroAccelerationMultiplier, 1f);
            _nitroTopSpeedMultiplier = FiniteMinimum(definition.nitroTopSpeedMultiplier, 1f);
            _nitroCooldown = FiniteMinimum(definition.nitroCooldown, 0f);

            // Raycast anchors sit just inside the chassis floor, so a ray never starts on a surface
            // it is about to hit, and the spring is measured against the rest length rather than the
            // whole ray: a wheel at rest carries the chassis with a small static compression.
            float sizeX = FiniteMinimum(definition.bodySize.x, 0.1f);
            float sizeY = FiniteMinimum(definition.bodySize.y, 0.1f);
            float sizeZ = FiniteMinimum(definition.bodySize.z, 0.1f);
            float halfHeight = sizeY * 0.5f;
            float anchorY = -halfHeight + 0.02f;
            _anchorRayLength = _suspensionRestLength + MaxSuspensionTravel;
            float halfWidth = sizeX * 0.5f * 0.8f;
            float halfLength = sizeZ * 0.5f * 0.8f;
            _anchors[FrontLeft] = new Vector3(-halfWidth, anchorY, halfLength);
            _anchors[FrontRight] = new Vector3(halfWidth, anchorY, halfLength);
            _anchors[RearLeft] = new Vector3(-halfWidth, anchorY, -halfLength);
            _anchors[RearRight] = new Vector3(halfWidth, anchorY, -halfLength);
        }

        /// <summary>
        /// The grip multiplier the surface under the car resolved to. Only the authority that owns the
        /// surface query may write it: a client that predicted terrain grip would drift from the tick
        /// that decides the match, exactly like zone handling. The value is clamped to a sane band so a
        /// bad surface table can neither freeze the car nor make it infinitely sticky, and it is
        /// idempotent, so setting it every tick with the same number costs nothing.
        /// </summary>
        public void ApplySurfaceGrip(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f)
                return;
            _surfaceGripScale = Mathf.Clamp(multiplier, MinSurfaceGripScale, MaxSurfaceGripScale);
        }

        /// <summary>
        /// World position of one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR), so a debris burst can be placed
        /// where the wheel actually left the car. False when there is no body or the index is unknown.
        /// </summary>
        public bool TryGetWheelWorldPosition(int wheel, out Vector3 position)
        {
            position = Vector3.zero;
            if (_body == null || wheel < 0 || wheel >= _anchors.Length)
                return false;

            position = _body.transform.TransformPoint(_anchors[wheel]);
            return true;
        }

        /// <summary>Applies one tick of arcade handling forces; invalid state is a silent no-op.</summary>
        public void Step(CarInput input, float deltaTime)
        {
            if (_body == null || _body.isKinematic || !IsFinite(deltaTime) || deltaTime <= 0f)
                return;

            Vector3 up = _body.transform.up;
            if (!IsFinite(up) || up.sqrMagnitude < 1e-6f)
                up = Vector3.up;
            else
                up.Normalize();
            Vector3 down = -up;

            Vector3 forward = _body.transform.forward;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (!IsFinite(forward) || forward.sqrMagnitude < 1e-6f)
                return; // Degenerate projection: no safe longitudinal axis to push along.
            forward.Normalize();

            float signedForwardSpeed = Vector3.Dot(_body.linearVelocity, forward);
            var decision = CarControlRules.Resolve(
                input, signedForwardSpeed, _reverseEngageSpeed, _steeringDeadZone);
            ResolvedMode = decision.Mode;

            // A pulse is accepted only where it can be used: brake and reverse reject it without
            // consuming or queuing the charge. The economy is one charge that recharges after a
            // cooldown measured from activation, so a spent boost is available again once the timer runs out.
            if (decision.NitroPulse && decision.Mode == CarControlMode.Forward && _nitroReady)
            {
                _nitroReady = false;
                _boostRemaining = _nitroDuration;
                _nitroCooldownRemaining = _nitroDuration + _nitroCooldown;
            }

            bool boosting = _boostRemaining > 0f;

            // Steering input is smoothed, then scaled down with speed so a full stick at top speed
            // is a gentle angle. The front axle turns by this angle; there is no direct yaw target.
            float smoothing = 1f - Mathf.Exp(-_steeringSmoothing * deltaTime);
            _steer = Mathf.Lerp(_steer, decision.Steer, smoothing);
            float speedFraction = Mathf.Clamp01(Mathf.Abs(signedForwardSpeed) / Mathf.Max(_maxForwardSpeed, 1e-3f));
            float steerLimit = Mathf.Min(_maxSteerAngleDegrees,
                Mathf.Lerp(_maxSteerAngleDegrees, _highSpeedSteerAngleDegrees, speedFraction));
            float steerRad = _steer * steerLimit * Mathf.Deg2Rad;


            ApplySuspension(up, down);
            bool grounded = _grounded[0] || _grounded[1] || _grounded[2] || _grounded[3];
            bool rearGrounded = _grounded[2] || _grounded[3];

            Vector3 rearAxlePoint = (_anchors[2] + _anchors[3]) * 0.5f;
            rearAxlePoint = _body.transform.TransformPoint(rearAxlePoint);

            ApplyPropulsion(decision, forward, signedForwardSpeed, boosting, rearGrounded, deltaTime, rearAxlePoint);
            ApplyGrip(up, forward, steerRad, decision.Mode, deltaTime);
            ApplyYawAssist(grounded, decision.Steer, signedForwardSpeed, up);

            if (grounded && _downforceCoefficient > 0f)
            {
                // Downforce grows with v squared and presses the car onto the surface, hardening
                // grip at speed without ever being a hard speed clamp.
                float speedSquared = _body.linearVelocity.sqrMagnitude;
                if (speedSquared > 0f)
                    _body.AddForce(-up * (_downforceCoefficient * speedSquared), ForceMode.Force);
            }

            ApplyAirAndRecovery(decision, grounded, up, signedForwardSpeed, deltaTime);
            ApplyAngularDamping(grounded);
            // Airborne spin belongs to the launch or the ram that caused it; the yaw cap is a
            // ground-handling limit and would otherwise cut a tumble short.
            if (grounded)
                ClampYaw(up);

            // The clock runs in every mode, so a boost spent while braking cannot be resumed later,
            // and it never advances outside a step, so a cooperative pause holds it still.
            if (_boostRemaining > 0f)
                _boostRemaining = Mathf.Max(0f, _boostRemaining - deltaTime);
            if (_nitroCooldownRemaining > 0f)
            {
                _nitroCooldownRemaining = Mathf.Max(0f, _nitroCooldownRemaining - deltaTime);
                if (_nitroCooldownRemaining <= 0f && _boostRemaining <= 0f)
                    _nitroReady = true;
            }
        }

        private void ApplySuspension(Vector3 up, Vector3 down)
        {
            for (int wheel = 0; wheel < WheelCount; wheel++)
            {
                _grounded[wheel] = false;
                _compression[wheel] = 0f;
                Vector3 origin = _body.transform.TransformPoint(_anchors[wheel]);
                if (!IsFinite(origin))
                    continue;

                int count = Physics.RaycastNonAlloc(origin, down, _hits, _anchorRayLength,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                for (int hit = 0; hit < count; hit++)
                {
                    // The car's own chassis is under the ray origin; ignore it so only the ground
                    // (or a rival) can carry the car.
                    if (ReferenceEquals(_hits[hit].rigidbody, _body))
                        continue;
                    if (_hits[hit].distance < nearest)
                        nearest = _hits[hit].distance;
                }
                if (nearest == float.PositiveInfinity)
                    continue;

                // The only way a hit occurs inside the ray is compression of the spring; the rest
                // length, not the whole ray, is the spring's free length.
                float compression = _suspensionRestLength - nearest;
                if (compression <= 0f)
                    continue;

                _grounded[wheel] = true;
                _compression[wheel] = compression;

                // Compression velocity is how fast the chassis point moves along the ray: positive
                // while the suspension is being squeezed, so the damper adds to the spring force.
                float compressionVelocity = Vector3.Dot(_body.GetPointVelocity(origin), down);
                float force = _suspensionSpring * compression + _suspensionDamper * compressionVelocity;
                if (!IsFinite(force))
                    continue;
                force = Mathf.Clamp(force, 0f, _maxSuspensionForce);
                if (force > 0f)
                    _body.AddForceAtPosition(up * force, origin, ForceMode.Force);
            }

            if (_antiRoll <= 0f)
                return;
            // Anti-roll transfers load from the compressed side to the extended side, which keeps
            // the body flat through a corner and stops a soft setup from rolling onto its roof.
            ApplyAntiRoll(FrontLeft, FrontRight, up);
            ApplyAntiRoll(RearLeft, RearRight, up);
        }

        private void ApplyAntiRoll(int leftIndex, int rightIndex, Vector3 up)
        {
            if (!_grounded[leftIndex] || !_grounded[rightIndex])
                return;
            float force = _antiRoll * (_compression[leftIndex] - _compression[rightIndex]);
            if (force == 0f || !IsFinite(force))
                return;
            _body.AddForceAtPosition(up * force, _body.transform.TransformPoint(_anchors[leftIndex]), ForceMode.Force);
            _body.AddForceAtPosition(-up * force, _body.transform.TransformPoint(_anchors[rightIndex]), ForceMode.Force);
        }

        private void ApplyPropulsion(CarControlDecision decision, Vector3 forward,
            float signedForwardSpeed, bool boosting, bool rearGrounded, float deltaTime, Vector3 rearAxlePoint)
        {
            if (decision.Mode == CarControlMode.Forward)
            {
                float cap = _maxForwardSpeed * (boosting ? _nitroTopSpeedMultiplier : 1f);
                if (rearGrounded && _driveForce > 0f)
                {
                    float headroom = cap - signedForwardSpeed;
                    if (headroom > 0f)
                    {
                        // The curve is full force at rest and falls to nothing at the cap, so the
                        // car approaches top speed asymptotically instead of slamming into a limit.
                        // The stick's own depth scales it, so half stick is half the force.
                        float curve = Mathf.Clamp01(headroom / Mathf.Max(cap, 1e-3f));
                        float force = _driveForce * decision.DriveFraction
                            * (boosting ? _nitroAccelerationMultiplier : 1f) * curve;
                        _body.AddForceAtPosition(forward * force, rearAxlePoint, ForceMode.Force);
                    }
                }
                if (signedForwardSpeed > cap && _coastDrag > 0f)
                {
                    // Above the cap only a gentle bleed applies: existing momentum decays, it is
                    // never erased in one tick.
                    float excess = signedForwardSpeed - cap;
                    float bleed = Mathf.Min(_coastDrag * excess, excess / deltaTime);
                    _body.AddForceAtPosition(forward * (-bleed * _body.mass), rearAxlePoint, ForceMode.Force);
                }
            }
            else if (decision.Mode == CarControlMode.Brake)
            {
                if (IsFinite(signedForwardSpeed) && signedForwardSpeed != 0f && _brakeDeceleration > 0f)
                {
                    // Bounded by the remaining speed divided by the step so one tick may stop the
                    // body but never push it past zero into the opposite direction.
                    // The 0.99 keeps float rounding from pushing a creeping body a hair past zero.
                    float magnitude = Mathf.Min(_brakeDeceleration * decision.DriveFraction,
                        Mathf.Abs(signedForwardSpeed) / deltaTime * 0.99f);
                    _body.AddForce(forward * (-Mathf.Sign(signedForwardSpeed) * magnitude * _body.mass),
                        ForceMode.Force);
                }
            }
            else if (decision.Mode == CarControlMode.Reverse)
            {
                if (rearGrounded && _driveForce > 0f && decision.DriveFraction > 0f)
                {
                    float backwardSpeed = -signedForwardSpeed;
                    float headroom = _maxReverseSpeed - backwardSpeed;
                    if (headroom > 0f)
                    {
                        float curve = Mathf.Clamp01(headroom / Mathf.Max(_maxReverseSpeed, 1e-3f));
                        _body.AddForceAtPosition(
                            forward * (-_driveForce * decision.DriveFraction * curve), rearAxlePoint, ForceMode.Force);
                    }
                }
            }
            else if (_coastDrag > 0f && IsFinite(signedForwardSpeed) && signedForwardSpeed != 0f)
            {
                // Released throttle: the only longitudinal force left is the same gentle drag that
                // bleeds speed above the cap, applied against the car's own direction of travel, so
                // it rolls to a stop instead of holding speed forever. It never reverses the motion
                // in one tick, for the same reason braking does not.
                float magnitude = Mathf.Min(_coastDrag * Mathf.Abs(signedForwardSpeed),
                    Mathf.Abs(signedForwardSpeed) / deltaTime * 0.99f);
                _body.AddForce(forward * (-Mathf.Sign(signedForwardSpeed) * magnitude * _body.mass),
                    ForceMode.Force);
            }
        }

        private void ApplyGrip(Vector3 up, Vector3 forward, float steerRad, CarControlMode mode, float deltaTime)
        {
            // Terrain scales the whole axle, rate and friction cap alike. Scaling only the rate would
            // be invisible: at racing slip both axles already sit on the cap, so the cap is where a
            // slippery surface actually gives way, and it is the same tyre limit the cap models.
            float terrain = _surfaceGripScale;
            float maxForce = _maxLateralForce * terrain;
            if (_frontGripRate > 0f)
                ApplyAxleGrip(FrontLeft, FrontRight, up, forward, steerRad, _frontGripRate * terrain,
                    maxForce, deltaTime);
            float rearRate = mode == CarControlMode.Brake ? _rearDriftGripRate : _rearGripRate;
            if (rearRate > 0f)
                ApplyAxleGrip(RearLeft, RearRight, up, forward, 0f, rearRate * terrain, maxForce, deltaTime);
        }

        private void ApplyAxleGrip(int leftIndex, int rightIndex, Vector3 up, Vector3 forward, float steerRad,
            float rate, float maxForce, float deltaTime)
        {
            if (!_grounded[leftIndex] && !_grounded[rightIndex])
                return;

            Vector3 wheelForward = steerRad == 0f
                ? forward
                : Quaternion.AngleAxis(steerRad * Mathf.Rad2Deg, up) * forward;
            Vector3 wheelRight = Vector3.Cross(up, wheelForward);
            if (wheelRight.sqrMagnitude < 1e-6f)
                return;
            wheelRight.Normalize();

            Vector3 point = (_body.transform.TransformPoint(_anchors[leftIndex])
                + _body.transform.TransformPoint(_anchors[rightIndex])) * 0.5f;
            float lateralSpeed = Vector3.Dot(_body.GetPointVelocity(point), wheelRight);
            if (lateralSpeed == 0f || !IsFinite(lateralSpeed))
                return;

            // Cancel the share of lateral velocity the tick's surface contact may absorb, with the
            // axle carrying half the chassis mass; the force cap then models tire saturation, which
            // is what makes a car understeer instead of snapping to full grip.
            float factor = 1f - Mathf.Exp(-rate * deltaTime);
            float force = -lateralSpeed * factor * (_body.mass * 0.5f) / deltaTime;
            force = Mathf.Clamp(force, -maxForce, maxForce);
            if (force != 0f)
                _body.AddForceAtPosition(wheelRight * force, point, ForceMode.Force);
        }

        /// <summary>
        /// One wheel's share of the axle grip, used only once a wheel is damaged enough to need its own
        /// scale. It carries a quarter of the chassis mass, so a pair of healthy wheels sums to the same
        /// half the axle path applies.
        /// </summary>
        private void ApplyYawAssist(bool grounded, float steer, float signedForwardSpeed, Vector3 up)
        {
            if (!grounded || steer == 0f || _yawAssist <= 0f || _maxYawRateRad <= 0f)
                return;
            // The assist engages only above walking pace, so a stationary car with full stick never
            // spins in place; the tire forces above still do the turning.
            float engage = Mathf.Clamp01(Mathf.Abs(signedForwardSpeed) / YawEngageSpeed);
            float targetYaw = _steer * _maxYawRateRad * engage;
            float currentYaw = Vector3.Dot(_body.angularVelocity, up);
            float error = targetYaw - currentYaw;
            if (!IsFinite(error))
                return;
            _body.AddTorque(up * (error * _yawAssist * _body.mass), ForceMode.Force);
        }

        private void ApplyAirAndRecovery(CarControlDecision decision, bool grounded, Vector3 up,
            float signedForwardSpeed, float deltaTime)
        {
            if (!grounded && _airControlTorque > 0f && decision.Steer != 0f)
            {
                // A small yaw ability in the air, so a jump can be aimed but never fully spun.
                _body.AddTorque(up * (_steer * _airControlTorque), ForceMode.Force);
            }

            // The clock only runs while the car is tilted and nearly at rest, i.e. lying on its side or
            // roof. A car still tumbling through the air or sliding keeps its spin until it settles,
            // so self-righting can never cancel a flip that is still in progress.
            float tilt = Vector3.Angle(up, Vector3.up);
            bool resting = _body.linearVelocity.sqrMagnitude <= SelfRightSpeedLimit * SelfRightSpeedLimit;
            _flipSeconds = tilt > TiltThresholdDegrees && resting ? _flipSeconds + deltaTime : 0f;
            if (_flipSeconds < SelfRightDelaySeconds || _uprightTorque <= 0f)
                return;

            // Rotate the shortest way back to world up. Exactly upside down has no cross product,
            // so roll around the car's own forward axis instead of dividing by zero.
            Vector3 axis = Vector3.Cross(up, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f)
                axis = _body.transform.forward;
            if (axis.sqrMagnitude < 1e-6f)
                return;
            axis.Normalize();
            if (IsFinite(axis))
                _body.AddTorque(axis * _uprightTorque, ForceMode.Force);
        }

        // Ground damping calms suspension wobble; in the air it would bleed the spin of a launched car
        // within a fraction of a second, so the body gets a far lighter value until a wheel lands.
        // The property is written only on a transition to keep the hot step free of engine calls.
        private void ApplyAngularDamping(bool grounded)
        {
            bool air = !grounded;
            if (air == _airDampingActive)
                return;
            _airDampingActive = air;
            _body.angularDamping = air ? _airAngularDamping : _groundAngularDamping;
        }

        private void ClampYaw(Vector3 up)
        {
            if (_maxYawRateRad <= 0f)
                return;
            Vector3 angular = _body.angularVelocity;
            float yaw = Vector3.Dot(angular, up);
            float clamped = Mathf.Clamp(yaw, -_maxYawRateRad, _maxYawRateRad);
            if (clamped != yaw)
                _body.angularVelocity = angular + up * (clamped - yaw);
        }

        private static float LateralRate(float fraction)
        {
            float clamped = Mathf.Clamp(fraction, 0f, 0.999f);
            return clamped <= 0f ? 0f : -Mathf.Log(1f - clamped) / ReferenceStep;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static float FiniteMinimum(float value, float minimum)
        {
            return IsFinite(value) ? Mathf.Max(value, minimum) : minimum;
        }
    }
}
