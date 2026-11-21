using System;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Handling;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// What the V2 debug line prints, captured in one struct so a HUD can read it without touching the
    /// model. Presentation only: nothing here feeds back into a step.
    /// </summary>
    public readonly struct CarHandlingReadout
    {
        public readonly float SpeedMetersPerSecond;
        public readonly float ForwardSpeedMetersPerSecond;

        /// <summary>The pedal the mapper resolved, so "the throttle never went negative" is measurable.</summary>
        public readonly float Throttle;

        public readonly float BetaRadians;

        /// <summary>
        /// Whether the model has committed to one turn direction, and which. Published because the commit is
        /// what keeps a stick held past 150 degrees from oscillating, and a caller cannot see it in the yaw.
        /// </summary>
        public readonly bool TurnCommitted;

        public readonly sbyte TurnSign;

        /// <summary>
        /// How far the car still is from the heading it is steering at, in the core's sign. It is the number
        /// the acceptance criteria call "settled" (below 20 degrees), so it is published rather than left to
        /// a caller to reconstruct from the stick and the yaw.
        /// </summary>
        public readonly float HeadingErrorRadians;

        public readonly float YawRateRadiansPerSecond;
        public readonly float YawRateTargetRadiansPerSecond;
        public readonly float SteerAngleDegrees;
        public readonly int GroundedWheels;
        public readonly HandlingPreset Preset;

        /// <summary>
        /// Per-axle slip ratio the core computed this tick. Added for the DriveLab telemetry, which needs
        /// the front/rear split to tell understeer from oversteer; appended last with defaults so every
        /// existing positional call site keeps compiling.
        /// </summary>
        public readonly float SlipRatioFront;

        public readonly float SlipRatioRear;

        /// <summary>
        /// How far past its grip budget the rear axle is, 0 to 1. Published for the tyre smoke, which needs
        /// "the rear tyres are spinning" rather than "the rear axle is moving sideways"; appended after the
        /// slip ratios with defaults so every existing positional call site keeps compiling.
        /// </summary>
        public readonly float RearWheelspin01;

        /// <summary>
        /// The core's brake-to-rotate factor, 1 when it is not helping the car turn on the brakes. Below one
        /// it is a locked rear tyre by the core's own definition, which is smoke the wheel slip ratio does not
        /// report because the tyre is not sliding faster than the road, it is just stopped.
        /// </summary>
        public readonly float BrakeRotateFactor;

        /// <summary>
        /// The terrain grip multiplier the surface sampler last resolved, 1 for asphalt. The driving layer has
        /// no surface ids at runtime, so this is the only signal the tyre smoke has for "loose ground, tint the
        /// puff as dust".
        /// </summary>
        public readonly float SurfaceGripScale;

        /// <summary>Whether the driver is on the brake this step.</summary>
        public readonly bool Braking;

        public CarHandlingReadout(
            float speedMetersPerSecond,
            float forwardSpeedMetersPerSecond,
            float throttle,
            float betaRadians,
            bool turnCommitted,
            sbyte turnSign,
            float headingErrorRadians,
            float yawRateRadiansPerSecond,
            float yawRateTargetRadiansPerSecond,
            float steerAngleDegrees,
            int groundedWheels,
            HandlingPreset preset,
            float slipRatioFront = 0f,
            float slipRatioRear = 0f,
            float rearWheelspin01 = 0f,
            float brakeRotateFactor = 1f,
            float surfaceGripScale = 1f,
            bool braking = false)
        {
            SpeedMetersPerSecond = speedMetersPerSecond;
            ForwardSpeedMetersPerSecond = forwardSpeedMetersPerSecond;
            Throttle = throttle;
            BetaRadians = betaRadians;
            TurnCommitted = turnCommitted;
            TurnSign = turnSign;
            HeadingErrorRadians = headingErrorRadians;
            YawRateRadiansPerSecond = yawRateRadiansPerSecond;
            YawRateTargetRadiansPerSecond = yawRateTargetRadiansPerSecond;
            SteerAngleDegrees = steerAngleDegrees;
            GroundedWheels = groundedWheels;
            Preset = preset;
            SlipRatioFront = slipRatioFront;
            SlipRatioRear = slipRatioRear;
            RearWheelspin01 = rearWheelspin01;
            BrakeRotateFactor = brakeRotateFactor;
            SurfaceGripScale = surfaceGripScale;
            Braking = braking;
        }
    }

    /// <summary>
    /// Driving Model V2 in the car: the Unity adapter over the engine-free <see cref="HandlingStep"/>.
    ///
    /// The split of work is the whole design. The engine-free core decides forces; this class owns only
    /// what needs an engine — the raycast suspension that carries the chassis and produces the axle loads,
    /// the surface and damage-zone multipliers, the worlds-to-body frame conversion, and the application of
    /// each force at its own axle point. The core is handed plain floats and hands back plain floats, so the
    /// same numbers can run on a server tick.
    ///
    /// Three conventions have to be translated, and every one of them is a place a silent sign error
    /// would hide:
    ///
    /// 1. Body axes. The core uses +x forward and +y LEFT; Unity uses +z forward and +x right. So the
    ///    core's lateral axis is <c>-Vector3.Cross(up, forward)</c> and its yaw rate is
    ///    <c>-dot(angularVelocity, up)</c>: a positive core yaw turns LEFT, which is a negative Unity yaw.
    /// 2. Axle loads. <see cref="LateralModel"/> converts a load in newtons into a corner mass with the
    ///    reference gravity (9.80665), because the V2 presets are written in real lateral-acceleration
    ///    terms. The project runs gravity at -30 m/s^2, so an unscaled load would make every car scrub
    ///    lateral velocity three times faster than the acceptance numbers say. The adapter therefore
    ///    reports the load in reference-gravity terms, which is what makes the PlayMode car match the
    ///    planar reference sim instead of being a different game with the same constants.
    /// 3. Application points. Longitudinal force goes on the drive axle and each axle's lateral force on
    ///    its own axle point, both at centre-of-mass height, so PhysX computes the yaw moment exactly the
    ///    way the planar sim did (a horizontal force at a horizontal lever gives a pure yaw moment) while
    ///    the dyadic terms stay the engine's job.
    ///
    /// Everything is pre-allocated. The step is allocation free: no LINQ, no closures, no strings, and the
    /// only delegate-free calls are engine ones.
    /// </summary>
    public sealed class ArcadeCarMotor : ICarMotor, IRamRollMute
    {
        private const int WheelCount = 4;
        private const int FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3;
        // A ray longer than the spring's free length, so a wheel keeps sensing while compressed. Same
        // value the legacy motor uses; the chassis geometry is shared, so the probe must be too.
        private const float MaxSuspensionTravel = 0.35f;
        private const int WheelBufferSize = 8;

        // The prototypes the sim is measured against are planar, so the lateral force is applied at the
        // axle line and at centre-of-mass height: a horizontal force at a horizontal lever is a pure yaw
        // moment, and any vertical offset would add a roll moment the reference sim never had.
        private const float TiltThresholdDegrees = 50f;
        private const float SelfRightDelaySeconds = 1f;
        private const float SelfRightSpeedLimit = 1.5f;

        /// <summary>How long a ram keeps the roll assist out of the way of the flip it started.</summary>
        public const float RamRollMuteSeconds = 0.6f;

        private readonly Rigidbody _body;
        // Not readonly: ApplyParams swaps the table for live feel tuning. Nothing else caches a value
        // derived from it except the two axle points, which ApplyParams refreshes.
        private HandlingParams _params;
        private readonly HandlingPreset _preset;
        private readonly JoystickDriveMode _driveMode;
        private readonly Transform _camera;
        private readonly float _reverseEngageSpeed;
        private readonly float _steeringDeadZone;
        private readonly float _yawInertia;

        // Suspension and body tuning, copied once from the definition exactly like the legacy motor.
        private readonly float _suspensionRestLength;
        private readonly float _suspensionSpring;
        private readonly float _suspensionDamper;
        private readonly float _maxSuspensionForce;
        private readonly float _antiRoll;
        private readonly float _downforceCoefficient;
        private readonly float _uprightTorque;
        private readonly float _groundAngularDamping;
        private readonly float _airAngularDamping;

        // Nitro economy, identical to the legacy motor's so IsBoosting and the HUD reads are unchanged.
        private readonly float _nitroDuration;
        private readonly float _nitroAccelerationMultiplier;
        private readonly float _nitroTopSpeedMultiplier;
        private readonly float _nitroCooldown;

        // Pre-allocated per-wheel state: the hot step must never allocate.
        private readonly Vector3[] _anchors = new Vector3[WheelCount];
        private readonly bool[] _grounded = new bool[WheelCount];
        // How far below its anchor each wheel's ray hit something on the last suspension pass. Kept as a
        // distance, not a point, so a reader can rebuild the contact from the anchor's CURRENT pose: the
        // suspension pass runs before the physics step moves the car, and a stored point would be a step
        // stale against a carrier that is itself moving. The CATCH bed test reads it; handling does not.
        private readonly float[] _contactDepths = new float[WheelCount];
        private readonly float[] _compression = new float[WheelCount];
        private readonly float[] _wheelLoads = new float[WheelCount];
        // The ground under each wheel, so a car can ride a moving carrier. The body a wheel last stood on and
        // the motion source found on it are a one-entry cache per wheel: the component lookup runs when a
        // wheel reaches a new body, never every step.
        private readonly Rigidbody[] _groundBodies = new Rigidbody[WheelCount];
        private readonly IGroundVelocitySource[] _groundSources = new IGroundVelocitySource[WheelCount];
        private readonly Vector3[] _groundPointVelocities = new Vector3[WheelCount];
        private readonly Vector3[] _groundAngularVelocities = new Vector3[WheelCount];
        // Mean ground motion under the grounded wheels of the last step; zero on static ground and in the air.
        private Vector3 _groundVelocity;
        private Vector3 _groundAngularVelocity;
        // Any grounded wheel stood on a body that states its own motion this step. Recomputed every pass,
        // because the per-wheel source cache above survives a wheel leaving the carrier.
        private bool _onMovingGround;
        // Tilt from world up, in degrees, of the last step. Shared by the drive gate and the self-right.
        private float _lastTiltDegrees;
        // The roof's tip lever: half the chassis width, the edge a car on its roof has to roll over.
        private readonly float _bodyHalfWidth;
        private readonly RaycastHit[] _hits = new RaycastHit[WheelBufferSize];
        private readonly float _anchorRayLength;
        private float _axleFrontX;
        private float _axleRearX;


        // Terrain grip the surface under the car resolved to. Neutral is 1, which is asphalt and also the
        // value before any surface is sampled.
        private float _surfaceGripScale = 1f;
        private const float MinSurfaceGripScale = 0.2f;
        private const float MaxSurfaceGripScale = 2f;

        private HandlingState _state;
        private HandlingOutput _output;
        // Captured on every step so the debug line can print the model's own numbers without re-deriving
        // them from the body, which would not be the same numbers the model acted on.
        private float _lastYawRate;
        private float _lastForwardSpeed;
        private float _lastSpeed;
        private bool _lastGrounded;
        private int _lastGroundedWheels;
        // The brake pedal of the last step. Captured because the tyre smoke asks "is the driver braking"
        // between steps, and the input is gone by then.
        private bool _lastBrake;
        private float _rollMuteRemaining;
        private float _flipSeconds;
        private bool _airDampingActive;
        private bool _nitroReady = true;
        private float _boostRemaining;
        private float _nitroCooldownRemaining;
        private float _steerRequestAxis;

        /// <summary>Last control mode resolved by a step that actually ran; Forward until the first such step.</summary>
        public CarControlMode ResolvedMode { get; private set; }

        /// <summary>The preset this car is running; the debug line prints it.</summary>
        public HandlingPreset Preset
        {
            get { return _preset; }
        }

        /// <summary>Front wheel angle of the last step, in degrees, for the visual wheels.</summary>
        public float SteerAngleDegrees
        {
            get { return _output.SteerAngleRadians * HandlingParams.DegreesPerRadian; }
        }

        /// <summary>Any wheel touching a surface in the last step.</summary>
        public bool IsGrounded
        {
            get { return _grounded[FrontLeft] || _grounded[FrontRight] || _grounded[RearLeft] || _grounded[RearRight]; }
        }

        /// <summary>True while the nitro boost still has time left.</summary>
        public bool IsBoosting
        {
            get { return _boostRemaining > 0f; }
        }

        /// <summary>Absolute lateral speed of the rear axle in m/s, on the same basis the grip uses.</summary>
        public float RearSlipSpeed
        {
            get
            {
                if (_body == null || (!_grounded[RearLeft] && !_grounded[RearRight]))
                    return 0f;

                Vector3 up = _body.transform.up;
                Vector3 forward = Vector3.ProjectOnPlane(_body.transform.forward, up);
                if (!IsFinite(up) || !IsFinite(forward) || up.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f)
                    return 0f;

                up.Normalize();
                forward.Normalize();
                Vector3 right = Vector3.Cross(up, forward);
                if (right.sqrMagnitude < 1e-6f)
                    return 0f;

                Vector3 rearAxle = _body.transform.TransformPoint((_anchors[RearLeft] + _anchors[RearRight]) * 0.5f);
                // Against the ground the rear tyres stand on, like the grip: a car parked on a turning carrier
                // is not sliding, and must not smoke, rut or rumble as if it were.
                float lateral = Vector3.Dot(_body.GetPointVelocity(rearAxle) - RearGroundVelocity(), right);
                return IsFinite(lateral) ? Mathf.Abs(lateral) : 0f;
            }
        }

        public ArcadeCarMotor(
            Rigidbody body,
            CarDefinition definition,
            in HandlingParams handling,
            HandlingPreset preset,
            JoystickDriveMode driveMode,
            Transform camera)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            _body = body;
            _params = handling;
            _preset = preset;
            _driveMode = driveMode;
            _camera = camera;
            _reverseEngageSpeed = FiniteMinimum(definition.reverseEngageSpeed, 0f);
            _steeringDeadZone = Mathf.Clamp01(FiniteMinimum(definition.steeringDeadZone, 0f));

            _suspensionRestLength = FiniteMinimum(definition.suspensionRestLength, 0f);
            _suspensionSpring = FiniteMinimum(definition.suspensionSpring, 0f);
            _suspensionDamper = FiniteMinimum(definition.suspensionDamper, 0f);
            _maxSuspensionForce = FiniteMinimum(definition.maxSuspensionForce, 0f);
            _antiRoll = FiniteMinimum(definition.antiRoll, 0f);
            _downforceCoefficient = FiniteMinimum(definition.downforceCoefficient, 0f);
            _uprightTorque = FiniteMinimum(definition.uprightTorque, 0f);
            _groundAngularDamping = FiniteMinimum(definition.angularDamping, 0f);
            _airAngularDamping = FiniteMinimum(definition.airAngularDamping, 0f);

            _nitroDuration = FiniteMinimum(definition.nitroDuration, 0f);
            _nitroAccelerationMultiplier = FiniteMinimum(definition.nitroAccelerationMultiplier, 1f);
            _nitroTopSpeedMultiplier = FiniteMinimum(definition.nitroTopSpeedMultiplier, 1f);
            _nitroCooldown = FiniteMinimum(definition.nitroCooldown, 0f);

            // The wheel anchors are the same chassis geometry the legacy motor probes, because the visual
            // wheels are placed from the same definition and a second anchor set would put the raycasts and
            // the wheels in different places.
            float sizeX = FiniteMinimum(definition.bodySize.x, 0.1f);
            float sizeY = FiniteMinimum(definition.bodySize.y, 0.1f);
            float sizeZ = FiniteMinimum(definition.bodySize.z, 0.1f);
            float anchorY = -sizeY * 0.5f + 0.02f;
            _anchorRayLength = _suspensionRestLength + MaxSuspensionTravel;
            _bodyHalfWidth = sizeX * 0.5f;
            float halfWidth = sizeX * 0.5f * 0.8f;
            float halfLength = sizeZ * 0.5f * 0.8f;
            _anchors[FrontLeft] = new Vector3(-halfWidth, anchorY, halfLength);
            _anchors[FrontRight] = new Vector3(halfWidth, anchorY, halfLength);
            _anchors[RearLeft] = new Vector3(-halfWidth, anchorY, -halfLength);
            _anchors[RearRight] = new Vector3(halfWidth, anchorY, -halfLength);

            // The force application points come from the model's own wheelbase, not from the visual anchors:
            // the handling core's yaw balance is written against that number, and a helper geometry that
            // disagreed with it would silently change the balance the acceptance numbers describe.
            _axleFrontX = _params.WheelbaseMeters * 0.5f;
            _axleRearX = -_axleFrontX;

            // Yaw inertia is the body's own, so the model's inertia-scaled torques (assist, damping,
            // sideslip guard) produce the angular accelerations the preset intends on this particular car
            // instead of on an assumed one. body.inertiaTensor is already in kg m^2 — it is the tensor PhysX
            // built from the colliders and the mass — so it is used as-is and never multiplied by mass again.
            // A body whose tensor is not resolved yet (no collider, no mass) falls back to the same box
            // formula PhysX itself would apply, so a car can never launch with a near-zero yaw inertia.
            float yawInertia = body.inertiaTensor.y;
            if (!IsFinite(yawInertia) || yawInertia <= 0f)
                yawInertia = body.mass * (sizeX * sizeX + sizeZ * sizeZ) / 12f;
            _yawInertia = FiniteMinimum(yawInertia, 1f);
        }

        /// <summary>
        /// The grip multiplier the surface under the car resolved to. Clamped to a sane band so a bad
        /// surface table can neither freeze the car nor make it infinitely sticky.
        /// </summary>
        public void ApplySurfaceGrip(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f)
                return;
            _surfaceGripScale = Mathf.Clamp(multiplier, MinSurfaceGripScale, MaxSurfaceGripScale);
        }

        /// <summary>World pose of one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR), for placing debris.</summary>
        public bool TryGetWheelWorldPosition(int wheel, out Vector3 position)
        {
            position = Vector3.zero;
            if (_body == null || wheel < 0 || wheel >= _anchors.Length)
                return false;

            position = _body.transform.TransformPoint(_anchors[wheel]);
            return true;
        }

        /// <summary>
        /// Whether one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR) is touching the ground this step.
        ///
        /// Read-only, and it reads the flag the step already wrote — the suspension pass sets it, so the
        /// answer is exactly the one the grip model acted on and no ray is cast. Per wheel and not per
        /// car because a car on two wheels must not press ruts with the other two.
        /// </summary>
        public bool TryGetWheelGrounded(int wheel, out bool grounded)
        {
            grounded = false;
            if (wheel < 0 || wheel >= WheelCount)
                return false;

            grounded = _grounded[wheel];
            return true;
        }

        /// <summary>
        /// Where one wheel (0=FL, 1=FR, 2=RL, 3=RR) touches the world, with its grounded flag. A grounded wheel
        /// reports its anchor pushed down the suspension ray by the distance the last pass measured; a wheel in
        /// the air reports its own anchor, the honest "not touching anything" position. Read-only: no ray is
        /// cast and nothing is allocated.
        /// </summary>
        public bool TryGetWheelContactPoint(int wheel, out Vector3 point, out bool grounded)
        {
            point = Vector3.zero;
            grounded = false;
            if (_body == null || wheel < 0 || wheel >= WheelCount)
                return false;

            grounded = _grounded[wheel];
            point = _body.transform.TransformPoint(_anchors[wheel]);
            if (grounded)
                point -= _body.transform.up * _contactDepths[wheel];
            return true;
        }

        /// <summary>
        /// Keeps the roll assist away from a flip a ram just started: the assist exists to settle a car that
        /// has come to rest on its roof, and a ram is neither. Without the mute the levelling torque fights
        /// the tumble the ram applied and a hard hit stops flipping cars.
        /// </summary>
        /// <summary>
        /// FEEL2 catch-up torque: a heavy contact arms the handling core's short post-impact window, so a
        /// car that lost its speed to a crash gets it back quickly. Severity is 0..1; the core clamps it
        /// and turns the flag into its timer on the next step, so this costs nothing and allocates nothing.
        /// </summary>
        public void NotifyImpact(float severity)
        {
            _state.NotifyImpact(severity);
        }

        public void MuteAutoRoll(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
                return;
            _rollMuteRemaining = Mathf.Max(_rollMuteRemaining, seconds);
        }

        /// <summary>
        /// The numbers the V2 debug line prints. Everything here is a value the last step already
        /// computed, so reading it costs nothing and cannot disagree with what the model acted on.
        /// </summary>
        public CarHandlingReadout ReadDebug()
        {
            return new CarHandlingReadout(
                speedMetersPerSecond: _lastSpeed,
                forwardSpeedMetersPerSecond: _lastForwardSpeed,
                throttle: _output.Throttle,
                betaRadians: _output.BetaRadians,
                turnCommitted: _output.TurnCommitted,
                turnSign: _output.TurnSign,
                headingErrorRadians: _output.HeadingErrorRadians,
                yawRateRadiansPerSecond: _lastYawRate,
                yawRateTargetRadiansPerSecond: _output.YawRateTargetRadiansPerSecond,
                steerAngleDegrees: SteerAngleDegrees,
                groundedWheels: _lastGroundedWheels,
                preset: _preset,
                slipRatioFront: _output.SlipRatioFront,
                slipRatioRear: _output.SlipRatioRear,
                rearWheelspin01: _output.RearWheelspin01,
                brakeRotateFactor: _output.BrakeRotateFactor,
                surfaceGripScale: _surfaceGripScale,
                braking: _lastBrake);
        }

        /// <summary>The handling table this motor is stepping with.</summary>
        public HandlingParams Parameters
        {
            get { return _params; }
        }

        /// <summary>
        /// Swaps the handling table in place, for live feel tuning (DriveLab). It exists so a tuning
        /// session can change a knob without a rebind: <see cref="LocalCarFixedStepDriver.Bind"/> drops
        /// every callback and the caller has to re-join them, and it also resets the core's state
        /// (steering ramp, nitro, handling state), which makes an A/B of one field unreadable.
        ///
        /// Nothing else is cached from the table: the two axle points derived from the wheelbase are
        /// refreshed here, and every other field is read by the step. A caller that hands in a bogus
        /// value gets the same clamp the presets ship with, because the core validates its own inputs.
        /// </summary>
        public void ApplyParams(in HandlingParams handling)
        {
            _params = handling;
            _axleFrontX = handling.WheelbaseMeters * 0.5f;
            _axleRearX = -_axleFrontX;
        }

        /// <summary>Any wheel touching a surface in the last step.</summary>
        public bool LastGrounded
        {
            get { return _lastGrounded; }
        }

        /// <summary>
        /// A grounded wheel stood on a body that reports its own motion (the CATCH carrier) in the last step.
        /// Speeds there are carrier-relative, so the stuck escapes must not read "slow" as "stuck".
        /// </summary>
        public bool OnMovingGround
        {
            get { return _onMovingGround; }
        }

        /// <summary>Angle between the car's up and world up in the last step, in degrees.</summary>
        public float TiltDegrees
        {
            get { return _lastTiltDegrees; }
        }

        /// <summary>Applies one tick of V2 handling; invalid state is a silent no-op.</summary>
        public void Step(CarInput input, float deltaTime)
        {
            if (_body == null || _body.isKinematic || !IsFinite(deltaTime) || deltaTime <= 0f)
                return;

            Vector3 up = _body.transform.up;
            if (!IsFinite(up) || up.sqrMagnitude < 1e-6f)
                up = Vector3.up;
            else
                up.Normalize();
            _lastTiltDegrees = Vector3.Angle(up, Vector3.up);

            Vector3 forward = Vector3.ProjectOnPlane(_body.transform.forward, up);
            if (!IsFinite(forward) || forward.sqrMagnitude < 1e-6f)
                return; // Degenerate projection: no safe longitudinal axis to push along.
            forward.Normalize();

            // The core's left. Vector3.Cross(up, forward) is the engine's right, and the core's +y is left.
            Vector3 coreLeft = Vector3.Cross(forward, up);
            if (coreLeft.sqrMagnitude < 1e-6f)
                return;
            coreLeft.Normalize();

            // The suspension runs before the speed reads because its rays find the ground the speeds are
            // measured against. Its forces only accumulate until the physics step, so the reads are unchanged.
            ApplySuspension(up, -up);
            ResolveGroundMotion();

            // Every tyre quantity is measured against the ground under the wheels, so drag, rolling
            // resistance, grip and yaw damping hold a car still on a moving carrier instead of braking it
            // toward world rest. Static ground moves at exactly zero, which leaves these the world reads.
            Vector3 groundRelativeVelocity = _body.linearVelocity - _groundVelocity;
            float forwardSpeed = Vector3.Dot(groundRelativeVelocity, forward);
            float lateralSpeed = Vector3.Dot(groundRelativeVelocity, coreLeft);
            float yawRate = -Vector3.Dot(_body.angularVelocity - _groundAngularVelocity, up);
            _lastYawRate = yawRate;

            // Legacy control rules still resolve the mode and the nitro pulse: the mode drives the HUD and
            // the reverse-engage hysteresis, and the nitro economy must not fork per model.
            var decision = CarControlRules.Resolve(
                input, forwardSpeed, _reverseEngageSpeed, _steeringDeadZone);
            ResolvedMode = decision.Mode;

            if (decision.NitroPulse && decision.Mode == CarControlMode.Forward && _nitroReady)
            {
                _nitroReady = false;
                _boostRemaining = _nitroDuration;
                _nitroCooldownRemaining = _nitroDuration + _nitroCooldown;
            }

            bool boosting = _boostRemaining > 0f;

            _lastGroundedWheels = 0;
            for (int wheel = 0; wheel < WheelCount; wheel++)
            {
                if (_grounded[wheel])
                    _lastGroundedWheels++;
            }

            _lastGrounded = _lastGroundedWheels > 0;
            _lastBrake = input.brake;
            _lastSpeed = IsFinite(groundRelativeVelocity) ? groundRelativeVelocity.magnitude : 0f;
            _lastForwardSpeed = forwardSpeed;
            float axleLoadFront = (_wheelLoads[FrontLeft] + _wheelLoads[FrontRight]) * ReferenceGravityScale;
            float axleLoadRear = (_wheelLoads[RearLeft] + _wheelLoads[RearRight]) * ReferenceGravityScale;

            float carYaw = -WorldYaw(forward);


            ResolveIntent(input, decision.Steer, carYaw, forwardSpeed, out float stickX, out float stickY,
                out float cameraYaw, out float throttleOverride);

            var handlingInput = new HandlingInput(
                stickX: stickX,
                stickY: stickY,
                cameraYawRadians: cameraYaw,
                carYawRadians: carYaw,
                throttleOverride: throttleOverride,
                brake: input.brake,
                reverse: input.reverse,
                nitroScale: boosting ? _nitroAccelerationMultiplier : 1f,
                forwardSpeed: forwardSpeed,
                lateralSpeed: lateralSpeed,
                yawRateRadiansPerSecond: yawRate,
                massKilograms: _body.mass,
                yawInertiaKilogramMetersSquared: _yawInertia,
                axleFrontX: _axleFrontX,
                axleRearX: _axleRearX,
                axleLoadFrontNewton: axleLoadFront,
                axleLoadRearNewton: axleLoadRear,
                surfaceGripScale: _surfaceGripScale,
                zoneWheelGripScale: 1f,
                // Nitro raises the drive force rather than the modelled top speed, so the boost is a
                // multiplier here; it is folded into the top speed so a charged car still pulls at the
                // top of the curve instead of hitting the same wall.
                zoneTopSpeedScale: boosting ? _nitroTopSpeedMultiplier : 1f,
                groundedWheels: _lastGroundedWheels,
                deltaTime: deltaTime,
                parameters: _params,
                onMovingGround: _onMovingGround);

            _output = HandlingStep.Step(in handlingInput, ref _state);
            _steerRequestAxis = _output.SteerAngleRadians / Max(_output.SteerMaxRadians, 1e-4f);

            Vector3 comHeight = _body.worldCenterOfMass;
            Vector3 frontAxlePoint = comHeight + forward * _axleFrontX;
            Vector3 rearAxlePoint = comHeight + forward * _axleRearX;

            // Past the cutoff tilt the tyres are not on the road, whatever the chassis-down rays say (on its
            // side they hit the floor and count as grounded): no drive, brake, reverse or tyre grip, so a car
            // on its roof stops sliding under the pedal and comes to the rest the self-right waits for.
            if (FlipRecoveryRules.DriveAllowed(_lastTiltDegrees))
            {
                ApplyAxleForces(forward, coreLeft, frontAxlePoint, rearAxlePoint);
                ApplyYawTorque(up, forward);
            }

            bool grounded = _lastGrounded;
            if (grounded && _downforceCoefficient > 0f)
            {
                // Air, not tyres: the air stands still in the world, so downforce keeps the world speed.
                float speedSquared = _body.linearVelocity.sqrMagnitude;
                if (speedSquared > 0f)
                    _body.AddForce(-up * (_downforceCoefficient * speedSquared), ForceMode.Force);
            }

            ApplyAir(up, forward, coreLeft, grounded, deltaTime);
            ApplyAngularDamping(grounded);
            ApplyRecovery(up, forward, grounded, deltaTime);

            if (_boostRemaining > 0f)
                _boostRemaining = Mathf.Max(0f, _boostRemaining - deltaTime);
            if (_nitroCooldownRemaining > 0f)
            {
                _nitroCooldownRemaining = Mathf.Max(0f, _nitroCooldownRemaining - deltaTime);
                if (_nitroCooldownRemaining <= 0f && _boostRemaining <= 0f)
                    _nitroReady = true;
            }
        }

        /// <summary>
        /// Turns the tick's command into what <see cref="IntentMapper"/> reads.
        ///
        /// Direction mode hands the raw stick and the camera's bearing straight through: the mapper owns the
        /// dead zone, the low-pass and the turn commitment, and resolving the stick anywhere else would give
        /// the client and the server two different filters.
        ///
        /// CarRelative mode (bots, and any car the config keeps on the legacy reading) has no stick at all,
        /// only a signed steer axis and a pedal, so the axis is converted into the heading error that asks
        /// for the same share of the available yaw rate. That keeps one steering law for both modes instead
        /// of a second, separately tuned path, and it is what makes a bot's steering countersteer on its own
        /// once its tail steps out.
        /// </summary>
        private void ResolveIntent(CarInput input, float steerAxis, float carYaw, float forwardSpeed,
            out float stickX, out float stickY, out float cameraYaw, out float throttleOverride)
        {
            if (_driveMode == JoystickDriveMode.CarRelative)
            {
                float referenceSpeed = Max(forwardSpeed, _params.MinReferenceSpeedMetersPerSecond);
                float yawMax = HandlingMath.Min(
                    _params.MaxYawRateRadiansPerSecond,
                    _params.LateralAccelBudgetMetersPerSecondSquared / referenceSpeed);
                float error = HandlingMath.WrapPi(
                    Clamp(steerAxis, -1f, 1f) * yawMax / Max(_params.HeadingGain, 1e-4f));
                // The axis keeps the legacy reading: positive means right, and right is clockwise, which
                // is negative in the core's counter-clockwise-positive heading frame. The mapper reads a
                // stick as targetHeading = cameraYaw - stickAngle, so handing it the axis itself (and not
                // its negative) is what makes a bot turn the way its own steering asked for.
                stickX = (float)Math.Sin(error);
                stickY = (float)Math.Cos(error);
                cameraYaw = carYaw;
                throttleOverride = input.throttle < 0f ? 0f : input.throttle;
                return;
            }

            stickX = input.stickX;
            stickY = input.stickY;
            cameraYaw = _camera != null ? -WorldYaw(_camera.forward) : carYaw;
            // Negative means "no pedal of its own": the mapper's shaped stick magnitude is the throttle,
            // which is the shipped reading and the one the owner's "stick distance is throttle" rule wants.
            throttleOverride = -1f;
        }

        /// <summary>
        /// Each axle's force at its own axle point. The core's longitudinal force is applied on the drive
        /// axle, which is yaw-neutral by construction (a force along the lever is no moment) but keeps the
        /// acceleration where a rear-drive car's is; the lateral forces are what actually turn the car.
        /// </summary>
        private void ApplyAxleForces(Vector3 forward, Vector3 coreLeft, Vector3 frontAxlePoint, Vector3 rearAxlePoint)
        {
            if (_output.LongitudinalForceNewton != 0f)
                _body.AddForceAtPosition(forward * _output.LongitudinalForceNewton, rearAxlePoint, ForceMode.Force);

            Vector3 frontForce = forward * _output.FrontForceXNewton + coreLeft * _output.FrontForceYNewton;
            if (frontForce.sqrMagnitude > 0f)
                _body.AddForceAtPosition(frontForce, frontAxlePoint, ForceMode.Force);

            Vector3 rearForce = forward * _output.RearForceXNewton + coreLeft * _output.RearForceYNewton;
            if (rearForce.sqrMagnitude > 0f)
                _body.AddForceAtPosition(rearForce, rearAxlePoint, ForceMode.Force);
        }

        /// <summary>
        /// The core's yaw torque (assist + damping + sideslip guard). It is a pure left-handed yaw, so it is
        /// applied about the car's own up axis with the sign flipped: the core's positive yaw is a left turn,
        /// and Unity's positive rotation about up is a right one.
        /// </summary>
        private void ApplyYawTorque(Vector3 up, Vector3 forward)
        {
            float torque = _output.TotalYawTorqueNewtonMeters;
            if (torque == 0f || !IsFinite(torque))
                return;

            // Ground-only: the air model owns the airborne yaw, and adding both would let a jump steer with
            // the grip assist the wheels are not providing.
            if (!_lastGrounded)
                return;

            _body.AddTorque(up * -torque, ForceMode.Force);
        }

        /// <summary>
        /// The airborne half of the model. <see cref="AirModel"/> is engine-free, so the adapter only has to
        /// hand it the angles and apply what it returns: extra gravity on the way down, a stick-driven yaw
        /// the ground assist would otherwise own, and a roll levelling the ground cannot do.
        /// </summary>
        private void ApplyAir(Vector3 up, Vector3 forward, Vector3 coreLeft, bool grounded, float deltaTime)
        {
            if (grounded)
                return;

            Vector3 worldUp = Vector3.up;
            // Signed roll about the body's forward axis: positive means the roof is tipped toward the car's
            // left, which is a positive rotation about the core's own left-handed frame and therefore a
            // negative Unity rotation.
            float rollAngle = (float)Math.Atan2(Vector3.Dot(up, coreLeft), Vector3.Dot(up, worldUp));
            float rollRate = -Vector3.Dot(_body.angularVelocity, forward);

            AirModel.Evaluate(
                airborne: true,
                descending: Vector3.Dot(_body.linearVelocity, worldUp) < 0f,
                yawInput: _steerRequestAxis,
                rollAngleRadians: rollAngle,
                yawRateRadiansPerSecond: -Vector3.Dot(_body.angularVelocity, up),
                rollRateRadiansPerSecond: rollRate,
                yawInertiaKilogramMetersSquared: _yawInertia,
                rollInertiaKilogramMetersSquared: _yawInertia,
                p: _params,
                out float gravityScale,
                out float yawTorque,
                out float rollTorque);

            if (gravityScale != 1f)
            {
                // The engine already applied one gravity this tick; this adds the remainder the model asked
                // for, so a descent reads as weight instead of as a float.
                _body.AddForce(Physics.gravity * ((gravityScale - 1f) * _body.mass), ForceMode.Force);
            }

            if (yawTorque != 0f && IsFinite(yawTorque))
                _body.AddTorque(up * -yawTorque, ForceMode.Force);

            if (rollTorque != 0f && _rollMuteRemaining <= 0f && IsFinite(rollTorque))
                _body.AddTorque(forward * -rollTorque, ForceMode.Force);
        }

        /// <summary>
        /// Raycast suspension, identical in intent to the legacy motor's: four spring-dampers carry the
        /// chassis, anti-roll keeps it flat, and the same ray reads the load each axle hands the tyre model.
        /// The load is what makes the V2 model load-sensitive, so it has to come from the contact rather than
        /// from a static weight split.
        /// </summary>
        private void ApplySuspension(Vector3 up, Vector3 down)
        {
            _onMovingGround = false;
            for (int wheel = 0; wheel < WheelCount; wheel++)
            {
                _grounded[wheel] = false;
                _compression[wheel] = 0f;
                _wheelLoads[wheel] = 0f;
                _groundPointVelocities[wheel] = Vector3.zero;
                _groundAngularVelocities[wheel] = Vector3.zero;
                Vector3 origin = _body.transform.TransformPoint(_anchors[wheel]);
                if (!IsFinite(origin))
                    continue;

                int count = Physics.RaycastNonAlloc(origin, down, _hits, _anchorRayLength,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                int nearestHit = -1;
                for (int hit = 0; hit < count; hit++)
                {
                    // The car's own chassis is under the ray origin; ignore it so only the ground (or a
                    // rival) can carry the car.
                    if (ReferenceEquals(_hits[hit].rigidbody, _body))
                        continue;
                    if (_hits[hit].distance < nearest)
                    {
                        nearest = _hits[hit].distance;
                        nearestHit = hit;
                    }
                }

                if (nearest == float.PositiveInfinity)
                    continue;

                float compression = _suspensionRestLength - nearest;
                if (compression <= 0f)
                    continue;

                _grounded[wheel] = true;
                _compression[wheel] = compression;
                _contactDepths[wheel] = nearest;

                // The damper reads the closing speed against the ground, so a carrier moving under the car
                // is not mistaken for the car diving into it.
                Vector3 groundVelocity = SampleGround(wheel, _hits[nearestHit].rigidbody, _hits[nearestHit].point);
                float compressionVelocity = Vector3.Dot(_body.GetPointVelocity(origin) - groundVelocity, down);
                float force = _suspensionSpring * compression + _suspensionDamper * compressionVelocity;
                if (!IsFinite(force))
                    continue;
                force = Mathf.Clamp(force, 0f, _maxSuspensionForce);
                _wheelLoads[wheel] = force;
                if (force > 0f)
                    _body.AddForceAtPosition(up * force, origin, ForceMode.Force);
            }

            if (_antiRoll <= 0f)
                return;
            // Anti-roll transfers load from the compressed side to the extended side, which keeps the body
            // flat through a corner and stops a soft setup from rolling onto its roof.
            ApplyAntiRoll(FrontLeft, FrontRight, up);
            ApplyAntiRoll(RearLeft, RearRight, up);
        }

        /// <summary>
        /// The ground's velocity at one wheel's contact, recorded for the step. Static ground has no body and
        /// moves at zero. A body that states its own motion is asked, because a kinematic carrier moved with
        /// MovePosition reads back zero; any other body (a rival's roof) reports its own point velocity.
        /// </summary>
        private Vector3 SampleGround(int wheel, Rigidbody ground, Vector3 contact)
        {
            if (ground == null)
                return Vector3.zero;

            if (!ReferenceEquals(ground, _groundBodies[wheel]))
            {
                _groundBodies[wheel] = ground;
                _groundSources[wheel] = ground.TryGetComponent(out IGroundVelocitySource source) ? source : null;
            }

            IGroundVelocitySource stated = _groundSources[wheel];
            Vector3 pointVelocity;
            Vector3 angularVelocity;
            if (stated != null)
            {
                _onMovingGround = true;
                pointVelocity = stated.GetPointVelocity(contact);
                angularVelocity = stated.AngularVelocity;
            }
            else
            {
                pointVelocity = ground.GetPointVelocity(contact);
                angularVelocity = ground.angularVelocity;
            }

            // A bad source must not poison the tyre model; standing on still ground is the safe reading.
            if (!IsFinite(pointVelocity) || !IsFinite(angularVelocity))
                return Vector3.zero;

            _groundPointVelocities[wheel] = pointVelocity;
            _groundAngularVelocities[wheel] = angularVelocity;
            return pointVelocity;
        }

        /// <summary>
        /// The mean over every grounded wheel, a still contact counting as zero, so a car climbing onto the
        /// carrier blends from road speed to bed speed wheel by wheel instead of jumping the instant one tyre
        /// touches the ramp.
        /// </summary>
        private void ResolveGroundMotion()
        {
            Vector3 velocity = Vector3.zero;
            Vector3 angularVelocity = Vector3.zero;
            int grounded = 0;
            for (int wheel = 0; wheel < WheelCount; wheel++)
            {
                if (!_grounded[wheel])
                    continue;
                velocity += _groundPointVelocities[wheel];
                angularVelocity += _groundAngularVelocities[wheel];
                grounded++;
            }

            if (grounded > 1)
            {
                velocity /= grounded;
                angularVelocity /= grounded;
            }

            _groundVelocity = velocity;
            _groundAngularVelocity = angularVelocity;
        }

        private Vector3 RearGroundVelocity()
        {
            bool left = _grounded[RearLeft];
            bool right = _grounded[RearRight];
            if (left && right)
                return (_groundPointVelocities[RearLeft] + _groundPointVelocities[RearRight]) * 0.5f;
            if (left)
                return _groundPointVelocities[RearLeft];
            return right ? _groundPointVelocities[RearRight] : Vector3.zero;
        }

        private void ApplyAntiRoll(int leftIndex, int rightIndex, Vector3 up)
        {
            if (!_grounded[leftIndex] || !_grounded[rightIndex])
                return;
            float force = _antiRoll * (_compression[leftIndex] - _compression[rightIndex]);
            if (force == 0f || !IsFinite(force))
                return;
            // Anti-roll is a chassis force, not a tyre load: it must not enter the axle load the tyre model
            // reads, or a cornering car would appear to gain grip from the roll it is resisting.
            _body.AddForceAtPosition(up * force, _body.transform.TransformPoint(_anchors[leftIndex]), ForceMode.Force);
            _body.AddForceAtPosition(-up * force, _body.transform.TransformPoint(_anchors[rightIndex]), ForceMode.Force);
        }

        // Ground damping calms suspension wobble; in the air it would bleed the spin of a launched car
        // within a fraction of a second, so the body gets a far lighter value until a wheel lands. Written
        // only on a transition, to keep the hot step free of engine calls.
        private void ApplyAngularDamping(bool grounded)
        {
            bool air = !grounded;
            if (air == _airDampingActive)
                return;
            _airDampingActive = air;
            _body.angularDamping = air ? _airAngularDamping : _groundAngularDamping;
        }

        /// <summary>
        /// Self-righting, as a last resort only: the clock runs while the car is tilted AND nearly at rest,
        /// so a car still tumbling through the air or sliding keeps its spin and this can never cancel a flip
        /// in progress. A ram holds it off for its own short window on top of that.
        ///
        /// The gate is the car's own ORIENTATION, not wheel contact. A tipped car resting on its side aims
        /// its chassis-down rays sideways, so they hit the floor or a barrier and still count as grounded
        /// while carrying none of the load the tyres need. Gating on contact kept the assist silent at
        /// exactly the moment it was the only way back, and the same flat contact read cut the drive force
        /// to the airborne share on top of it: the car was stuck on its side with a dead pedal and, in a
        /// mode with no respawn, no route to recover. Detecting the tilt first makes that a recoverable
        /// state instead. A car square on its wheels keeps the old rule.
        /// </summary>
        private void ApplyRecovery(Vector3 up, Vector3 forward, bool grounded, float deltaTime)
        {
            if (_rollMuteRemaining > 0f)
                _rollMuteRemaining = Mathf.Max(0f, _rollMuteRemaining - deltaTime);

            bool tilted = _lastTiltDegrees > TiltThresholdDegrees;

            if (grounded && !tilted)
            {
                // The wheels are carrying the car, so a roll the tyres can still resolve is theirs to
                // resolve: no levelling torque on the ground, only the anti-roll above.
                _flipSeconds = 0f;
                return;
            }

            bool resting = _body.linearVelocity.sqrMagnitude <= SelfRightSpeedLimit * SelfRightSpeedLimit;
            _flipSeconds = tilted && resting ? _flipSeconds + deltaTime : 0f;
            if (_flipSeconds < SelfRightDelaySeconds || _uprightTorque <= 0f || _rollMuteRemaining > 0f)
                return;

            // Rotate the shortest way back to world up. Exactly upside down has no cross product, so roll
            // around the car's own forward axis instead of dividing by zero.
            Vector3 axis = Vector3.Cross(up, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f)
                axis = forward;
            if (axis.sqrMagnitude < 1e-6f)
                return;
            axis.Normalize();
            // The configured torque is raised to clear the roof's own tip moment with margin: 23 kN·m
            // against 1100 kg × 30 m/s² × 0.8 m = 26.4 kN·m left a car on its roof rocking forever.
            float torque = FlipRecoveryRules.SelfRightTorque(
                _uprightTorque, _body.mass, Physics.gravity.magnitude, _bodyHalfWidth);
            if (IsFinite(axis) && IsFinite(torque))
                _body.AddTorque(axis * torque, ForceMode.Force);
        }

        /// <summary>
        /// How much of a newton is a newton under the model's reference gravity. See the class comment: the
        /// V2 presets are written for real lateral accelerations, so the contact load has to be reported in
        /// the same units the presets assume, or this project's heavier gravity would silently retune them.
        /// </summary>
        private float ReferenceGravityScale
        {
            get
            {
                float gravity = Physics.gravity.magnitude;
                if (!IsFinite(gravity) || gravity <= 1e-3f)
                    return 1f;
                return HandlingParams.StandardGravity / gravity;
            }
        }

        /// <summary>World heading of a direction in radians, in the core's counter-clockwise frame.</summary>
        private static float WorldYaw(Vector3 direction)
        {
            return (float)Math.Atan2(direction.x, direction.z);
        }

        private static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            return value > max ? max : value;
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
