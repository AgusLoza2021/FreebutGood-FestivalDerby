using System;
using System.Collections.Generic;
using FestivalDerby.Driving;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Plain snapshot of one step's applied input, owned by this assembly. Scene code reads what a
    /// driver actually did without referencing the engine-free rules assembly that owns CarInput,
    /// which is invisible to Assembly-CSharp.
    /// </summary>
    public readonly struct CarInputSnapshot
    {
        public readonly float Steer;
        public readonly float Throttle;
        public readonly bool Brake;
        public readonly bool Reverse;
        public readonly bool Nitro;

        /// <summary>
        /// The raw stick this step was given, unshaped. Carried alongside steer/throttle rather than instead
        /// of them: the legacy model reads the axes and V2 reads the stick, and one snapshot has to serve a
        /// consumer that does not know which motor produced it.
        /// </summary>
        public readonly float StickX;

        /// <inheritdoc cref="StickX"/>
        public readonly float StickY;

        public CarInputSnapshot(float steer, float throttle, bool brake, bool reverse, bool nitro)
            : this(steer, throttle, brake, reverse, nitro, 0f, 0f)
        {
        }

        public CarInputSnapshot(float steer, float throttle, bool brake, bool reverse, bool nitro,
            float stickX, float stickY)
        {
            Steer = steer;
            Throttle = throttle;
            Brake = brake;
            Reverse = reverse;
            Nitro = nitro;
            StickX = stickX;
            StickY = stickY;
        }
    }

    /// <summary>
    /// Car-vs-car collision probe, added beside the driver on a car's body. It belongs to this
    /// assembly because a collision callback needs the physics module and this is the assembly that
    /// already owns every car's Rigidbody.
    ///
    /// A pair is reported exactly once, from the lower car index, so a head-on ram does not consume
    /// two damage-ledger slots. Static geometry carries no attached Rigidbody, so the floor and the
    /// walls are ignored here and never reach the damage point.
    ///
    /// WALL1: the same callback is where a barrier contact becomes a glance. Static geometry never reaches
    /// the damage, VFX or hit-stop paths (see <see cref="Report"/>), so a wall was pure physics: the solver
    /// removed the normal component, the car kept pointing into the barrier, and the tyre model then spent
    /// the wall-tangential speed as a brake, every tick, until the car crawled. <see cref="Glance"/> hands
    /// the contact to the engine-free <see cref="WallContactRules"/> instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarContactProbe : MonoBehaviour
    {
        private int _carIndex = -1;

        // The owned driving config is read once, lazily, and never inside a collision callback: a
        // Resources.Load on the first wall the player touches is a hitch. A missing asset resolves the
        // switch off, which is the rollback the project rule asks for.
        private const string WallGlanceConfigResourcePath = "DrivingFeatureConfig";
        private bool _wallGlanceResolved;
        private bool _wallGlance;
        private Rigidbody _wallBody;

        // Velocity at the start of the current fixed tick. OnCollision* runs AFTER the solver, so the body's
        // velocity there has already lost the normal component the glance has to reason about; the previous
        // tick's value is the pre-impact one (the same trick CarRamLift uses for the ram lift).
        private Vector3 _preStepVelocity;

        // Fixed time of the last barrier contact of any kind; negative infinity until the first, so the first
        // hit of a run is always fresh.
        private float _lastWallContactTime = float.NegativeInfinity;

        // Every live probe by its body's instance id. A pack of cars fires several collision callbacks per
        // car per physics step, and each one used to GetComponent the other body's probe (twice on an
        // enter); a dictionary read is the same answer without the native lookup (FIELD_24.md perf pass).
        private static readonly Dictionary<int, CarContactProbe> s_ByBody = new Dictionary<int, CarContactProbe>(32);
        private int _registeredBody;
        private bool _registered;

        /// <summary>The probe on <paramref name="body"/>'s car, or null for a body that is not a car.</summary>
        public static CarContactProbe ForBody(Rigidbody body)
        {
            if (body == null)
            {
                return null;
            }

            return s_ByBody.TryGetValue(body.GetInstanceID(), out CarContactProbe probe) ? probe : null;
        }

        private void Awake()
        {
            Register();
            ResolveWallGlance();
        }

        /// <summary>
        /// WALL1 kill switch. Off, a barrier contact is the plain contact response the game shipped with.
        /// </summary>
        public bool WallGlanceEnabled
        {
            get
            {
                ResolveWallGlance();
                return _wallGlance;
            }
        }

        private void ResolveWallGlance()
        {
            if (_wallGlanceResolved)
            {
                return;
            }

            _wallGlanceResolved = true;
            _wallGlance = DrivingFeatureConfig.ResolveWallGlanceEnabled(
                Resources.Load<DrivingFeatureConfig>(WallGlanceConfigResourcePath));
        }

        private void FixedUpdate()
        {
            // Read before the physics step of this tick, so a contact resolved later in the tick sees the
            // velocity the car actually arrived with.
            if (_wallBody != null)
            {
                _preStepVelocity = _wallBody.linearVelocity;
            }
        }

        private void OnDestroy()
        {
            if (_registered && s_ByBody.TryGetValue(_registeredBody, out CarContactProbe current) && current == this)
            {
                s_ByBody.Remove(_registeredBody);
            }

            _registered = false;
        }

        private void Register()
        {
            if (_registered || !TryGetComponent(out Rigidbody body))
            {
                return;
            }

            _registeredBody = body.GetInstanceID();
            s_ByBody[_registeredBody] = this;
            _registered = true;
            // Cached here rather than resolved per contact: the wall glance writes to the body and reads the
            // pre-step velocity, and a GetComponent in a collision callback is a native lookup per contact.
            _wallBody = body;
        }

        // Pre-allocated contact buffer: GetContacts writes into it, so the physics callback never
        // allocates. 16 covers Unity's usual contact count; a larger collision simply truncates, and the
        // strongest of the contacts that fit is still the one reported.
        private readonly ContactPoint[] _contactBuffer = new ContactPoint[16];

        /// <summary>Car slot of this probe, or -1 while unbound. Set once, before the first physics step.</summary>
        public int CarIndex
        {
            get { return _carIndex; }
        }

        /// <summary>(other car slot, closing speed in m/s). Subscribed once per run by the scene bridge.</summary>
        public event Action<int, float> Impact;

        /// <summary>
        /// Same single report per pair as <see cref="Impact"/>, plus the world contact point and the
        /// contact normal. Both are what the damage point needs to project the hit onto a car zone, so
        /// the netcode side listens to this one and the world VFX spawns exactly where the cars met.
        /// </summary>
        public event Action<int, float, Vector3, Vector3> PositionalImpact;

        /// <summary>Binds the probe to one car slot. Called once per run, before any collision.</summary>
        public void Bind(int carIndex)
        {
            _carIndex = carIndex;
            // A probe added before its body existed could not register in Awake.
            Register();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Report(collision);
            NotifyHandlingOfImpact(collision);
            Glance(collision, true);
        }

        // FEEL2: a car-vs-car hit hard enough to cost speed arms the catch-up torque on both cars. Only
        // on enter, never on stay: a sustained push would otherwise keep re-arming the window. Below the
        // threshold a scrape is not a crash; the severity ramps to 1 at a head-on ram speed.
        private const float ImpactNotifyMinClosing = 4f;
        private const float ImpactNotifyFullClosing = 16f;
        private LocalCarFixedStepDriver _driver;
        private bool _driverResolved;

        private void NotifyHandlingOfImpact(Collision collision)
        {
            if (_carIndex < 0)
                return;

            Rigidbody otherBody = collision.collider.attachedRigidbody;
            if (otherBody == null || ForBody(otherBody) == null)
                return;

            float closing = collision.relativeVelocity.magnitude;
            if (closing < ImpactNotifyMinClosing)
                return;

            if (!_driverResolved)
            {
                _driver = GetComponent<LocalCarFixedStepDriver>();
                _driverResolved = true;
            }

            // Each car's own probe hears its own enter, so each notifies only itself.
            if (_driver != null)
                _driver.NotifyImpact(Mathf.Clamp01(
                    (closing - ImpactNotifyMinClosing) / (ImpactNotifyFullClosing - ImpactNotifyMinClosing)));
        }

        private void OnCollisionStay(Collision collision)
        {
            // A sustained ram must keep reporting: the contact source keeps only the worst closing
            // speed of the tick, so a weak repeat costs nothing and a fresh hard hit is never missed.
            Report(collision);
            // Not a glance: staying on the barrier only keeps the scrape's clock alive (see Glance).
            Glance(collision, false);
        }

        /// <summary>
        /// WALL1: turns a barrier contact into a glance — the wall-tangential direction, a planned share of
        /// the speed, and a small outward bounce — and then nudges the yaw toward the barrier's tangent so
        /// the car slides along it instead of pressing its nose into it.
        ///
        /// Static geometry only. A contact with a Rigidbody is car-on-car, which keeps its own path (ram
        /// lift, zone damage, the FEEL2 catch-up window), and the ground is excluded by its normal: the floor,
        /// the road chunks and every ramp report a mostly-vertical normal, and treating one as a wall would
        /// stop the car dead on every landing.
        /// </summary>
        private void Glance(Collision collision, bool contactBegan)
        {
            if (!WallGlanceEnabled || _wallBody == null || _wallBody.isKinematic || collision.contactCount == 0)
            {
                return;
            }

            bool otherHasRigidbody = collision.collider.attachedRigidbody != null;
            if (otherHasRigidbody)
            {
                return;
            }

            int contactCount = collision.GetContacts(_contactBuffer);
            if (contactCount <= 0)
            {
                return;
            }

            // The strongest barrier contact of the pair decides the glance, the same way Report picks the
            // hardest contact: two barriers can land in one physics frame and only one velocity may win.
            Vector3 velocity = _preStepVelocity;
            int wall = -1;
            float deepest = 0f;
            bool touchingWall = false;
            for (int i = 0; i < contactCount; i++)
            {
                Vector3 normal = _contactBuffer[i].normal;
                if (!WallContactRules.IsWallContact(otherHasRigidbody, normal.x, normal.y, normal.z))
                {
                    continue;
                }

                touchingWall = true;
                float intoWall = -Vector3.Dot(velocity, normal);
                if (intoWall > deepest)
                {
                    deepest = intoWall;
                    wall = i;
                }
            }

            if (!touchingWall)
            {
                return;
            }

            // Catch playtest 1: the glance used to run on every step of a contact and its retention cost
            // compounded into a brake. Only a fresh hit glances; every barrier contact, fresh or not, stamps
            // the clock that decides what "fresh" is, so a grinding scrape stays one hit.
            float now = Time.fixedTime;
            bool fresh = WallContactRules.IsFreshHit(contactBegan, now - _lastWallContactTime);
            _lastWallContactTime = now;
            if (!fresh || wall < 0)
            {
                return;
            }

            Vector3 hitNormal = _contactBuffer[wall].normal;
            if (!WallContactRules.TryResolve(velocity.x, velocity.y, velocity.z,
                    hitNormal.x, hitNormal.y, hitNormal.z, out WallGlanceResult glance))
            {
                return;
            }

            _wallBody.linearVelocity = new Vector3(glance.VelocityX, glance.VelocityY, glance.VelocityZ);

            // Every contact aligns, not just a graze. Without it the glance only hands the tangential speed
            // back so the tyre model can spend it again on the next tick, because the nose still points into
            // the barrier — that is the "the wall brakes me" the owner keeps feeling.
            AlignToWall(velocity, hitNormal, glance.NormalSpeedMetersPerSecond);
        }

        /// <summary>
        /// Nudges the yaw toward the wall tangent. Without this the glance alone would restore the speed and
        /// the tyres would spend it again on the next tick: the car keeps pointing at the barrier, so the
        /// wall-tangential velocity is lateral to its own heading and the grip model scrubs it.
        ///
        /// Only the yaw rate about the car's up axis is touched, and only by one tick's step, so even a
        /// head-on blow can never spin the car.
        /// </summary>
        private void AlignToWall(Vector3 velocity, Vector3 normal, float normalSpeed)
        {
            Vector3 up = _wallBody.transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(_wallBody.transform.forward, up);
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, normal);
            if (forward.sqrMagnitude < 1e-4f || tangent.sqrMagnitude < 1e-4f)
            {
                return;
            }

            forward.Normalize();
            tangent.Normalize();
            float error = Vector3.SignedAngle(forward, tangent, up) * Mathf.Deg2Rad;
            if (!WallContactRules.ShouldAlignToWall(normalSpeed, error))
            {
                // A car travelling backwards along the barrier: the tangent disagrees with the nose by more
                // than any slide into it could explain, and turning the car would be a spin, not a glance.
                return;
            }

            float current = Vector3.Dot(_wallBody.angularVelocity, up);
            if (!WallContactRules.AlignYawRateRadiansPerSecond(error, current,
                    WallContactRules.AlignRateStepRadiansPerSecond, out float yawRate))
            {
                return;
            }

            _wallBody.angularVelocity += up * (yawRate - current);
        }

        private void Report(Collision collision)
        {
            if (_carIndex < 0 || collision.contactCount == 0 || (Impact == null && PositionalImpact == null))
            {
                return;
            }

            Rigidbody otherBody = collision.collider.attachedRigidbody;
            if (otherBody == null)
            {
                // Floor, wall or any static prop: not a rival, not damage.
                return;
            }

            CarContactProbe other = ForBody(otherBody);
            // ONE report per pair: the lower index side speaks for both. Equal indices cannot happen
            // (a body never hits itself), and the guard keeps the rule total.
            if (other == null || other._carIndex < 0 || _carIndex >= other._carIndex)
            {
                return;
            }

            // The strongest contact of the pair is the one that decides the hit: a scrape and a ram can
            // land in the same physics frame, and the damage curve and the zone projection must read the
            // hard one. GetContacts writes into the pre-allocated buffer, never collision.contacts.
            int contactCount = collision.GetContacts(_contactBuffer);
            if (contactCount <= 0)
            {
                return;
            }

            int strongest = 0;
            float bestClosing = -1f;
            for (int i = 0; i < contactCount; i++)
            {
                float closing = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, _contactBuffer[i].normal));
                if (closing > bestClosing)
                {
                    bestClosing = closing;
                    strongest = i;
                }
            }

            ContactPoint contact = _contactBuffer[strongest];
            float closingSpeed = bestClosing < 0f ? 0f : bestClosing;
            if (Impact != null)
            {
                Impact.Invoke(other._carIndex, closingSpeed);
            }

            if (PositionalImpact != null)
            {
                PositionalImpact.Invoke(other._carIndex, closingSpeed, contact.point, contact.normal);
            }
        }
    }

    /// <summary>
    /// Owns the sole local fixed-step driving loop: it binds one caller-owned Rigidbody with a
    /// CarDefinition, caches the latest CarInput for the next tick, and steps the CarMotor inside
    /// FixedUpdate on real fixed ticks. Rotation is deliberately never constrained: the arcade model
    /// lets a car launch, roll and land on its roof, so a tilt lock would fight the whole design.
    /// It also owns a cooperative pause that stops only its own step, and a read-only snapshot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalCarFixedStepDriver : MonoBehaviour
    {
        private Rigidbody _body;
        // Held as the interface so the driver is model-agnostic: the legacy motor and V2 are
        // interchangeable behind it, and the only place that decides which one a car gets is Bind.
        private ICarMotor _motor;
        private CarInput _input;
        private Func<float, CarInput> _inputSampler;
        private Func<Vector3, float> _surfaceGrip;
        private int _fixedStepCount;

        public void Bind(Rigidbody body, CarDefinition definition)
        {
            // The two-argument shape is the legacy one, kept byte for byte: every existing caller that does
            // not know about the model switch keeps getting the model it always got.
            Bind(body, definition, MotorSetup.Legacy());
        }

        /// <summary>
        /// Binds the body with an explicit handling model. The setup is copied, so a mutated config cannot
        /// change a car that is already running.
        /// </summary>
        public void Bind(Rigidbody body, CarDefinition definition, in MotorSetup setup)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (body.gameObject != gameObject)
                throw new ArgumentException("The body must live on this driver's GameObject.", nameof(body));

            // A rebinding steals ownership cleanly: the previous body is dropped before this one is
            // touched, so the motor never steps two bodies with one definition.
            if (_motor != null)
                Unbind();

            _body = body;
            _motor = setup.Model == HandlingModel.V2
                ? (ICarMotor)new ArcadeCarMotor(body, definition, setup.Handling, setup.Preset,
                    setup.DriveMode, setup.Camera)
                : new CarMotor(body, definition);
            BoundModel = setup.Model;
        }

        /// <summary>The model this driver is actually stepping, or Legacy while unbound.</summary>
        public HandlingModel BoundModel { get; private set; }

        /// <summary>
        /// V2 numbers for the debug line, or a zeroed readout while the legacy model is bound. Never null
        /// and never allocates, so a HUD may poll it every frame.
        /// </summary>
        public CarHandlingReadout ReadHandlingDebug()
        {
            var arcade = _motor as ArcadeCarMotor;
            return arcade != null ? arcade.ReadDebug() : default;
        }

        /// <summary>
        /// The V2 handling table this car is stepping right now — the preset with whatever profile overrides
        /// it was bound with — or false on the legacy model. A bot plans with the car it actually drives, so
        /// reading the bare preset back from <see cref="Preset"/> would undersell a profiled car.
        /// </summary>
        public bool TryGetBoundHandling(out FestivalDerby.Driving.Handling.HandlingParams handling)
        {
            var arcade = _motor as ArcadeCarMotor;
            if (arcade == null)
            {
                handling = default;
                return false;
            }

            handling = arcade.Parameters;
            return true;
        }

        /// <summary>The V2 preset this car is running, or Balanced while the legacy model is bound.</summary>
        public HandlingPreset Preset
        {
            get
            {
                var arcade = _motor as ArcadeCarMotor;
                return arcade != null ? arcade.Preset : HandlingPreset.Balanced;
            }
        }

        /// <summary>
        /// Tells the motor a ram just landed on this car, so its auto-roll stands down for the length of the
        /// flip. A no-op on the legacy model, which has no such assist; the cast keeps that check free.
        /// </summary>
        /// <summary>
        /// Reports a heavy contact to the V2 handling core (FEEL2 catch-up torque). A no-op on the legacy
        /// model. Only the car's own physics owner calls it (the host or the offline run): a client's car
        /// is a kinematic proxy whose motor never moves it.
        /// </summary>
        public void NotifyImpact(float severity)
        {
            var arcade = _motor as ArcadeCarMotor;
            if (arcade != null)
                arcade.NotifyImpact(severity);
        }

        public void NotifyRam(float seconds)
        {
            var mute = _motor as IRamRollMute;
            if (mute != null)
                mute.MuteAutoRoll(seconds);
        }

        public void SetInput(CarInput input)
        {
            // Latest-wins storage only; the body is touched exclusively by the fixed step.
            // While a sampler is installed this cache is ignored temporarily, then falls back
            // to it again once the sampler is cleared.
            _input = input;
        }

        /// <summary>
        /// Optional per-tick input provider for later runtime bots; null clears it and restores
        /// human latest-wins. Physics must own the bot route, so a provider is sampled inside
        /// the existing fixed step rather than polling from its own Update/second FixedUpdate.
        /// Install only after Bind: Bind calls Unbind, which clears any stale callback.
        /// </summary>
        public void SetInputSampler(Func<float, CarInput> sampler)
        {
            // Replace-or-clear is idempotent; the delegate itself is caller-owned and cached,
            // allocated outside the hot tick, so the fixed step only invokes it.
            _inputSampler = sampler;
        }

        /// <summary>
        /// Optional per-tick terrain grip provider, fed the body's world position. Null means asphalt:
        /// the motor keeps its neutral surface scale. The provider lives here rather than in the scene
        /// because physics must own the surface read the same way it owns the step: sampling it outside
        /// the fixed tick would let a car feel a grip the tick before it actually crossed the seam. The
        /// delegate is cached and returns a float, so the hot tick does not allocate.
        /// </summary>
        public void SetSurfaceGripProvider(Func<Vector3, float> provider)
        {
            _surfaceGrip = provider;
        }

        /// <summary>
        /// Hands the V2 motor a new handling table without a rebind, so live feel tuning does not reset the
        /// car's state or drop the callbacks a Bind-installed driver holds. A no-op on the legacy motor,
        /// which resolves its numbers from the definition and has no table to swap.
        /// </summary>
        public void ApplyHandlingParams(in FestivalDerby.Driving.Handling.HandlingParams handling)
        {
            var arcade = _motor as ArcadeCarMotor;
            if (arcade != null)
                arcade.ApplyParams(handling);
        }

        /// <summary>Cooperative pause flag: true stops only this driver's fixed step, nothing global.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Input the last unpaused fixed step actually applied, or the default before any step.
        /// Read-only so a match host can mirror each car's real input without touching the motor.</summary>
        public CarInputSnapshot LastAppliedInput { get; private set; }

        /// <summary>Read-only presentation view of the motor's grounded state; false while unbound.</summary>
        public bool IsGrounded
        {
            get { return _motor != null && _motor.IsGrounded; }
        }

        /// <summary>
        /// A wheel stood on a moving body (the CATCH carrier) in the last step. The V2 motor only: the legacy
        /// motor never rides a carrier, so it reads false and the stuck escapes behave as they always have.
        /// </summary>
        public bool IsOnMovingGround
        {
            get
            {
                var arcade = _motor as ArcadeCarMotor;
                return arcade != null && arcade.OnMovingGround;
            }
        }

        /// <summary>True while this car's nitro is burning; drives the exhaust flame.</summary>
        public bool IsBoosting
        {
            get { return _motor != null && _motor.IsBoosting; }
        }

        /// <summary>Rear axle lateral speed in m/s; drives the drift smoke and skid marks.</summary>
        public float RearSlipSpeed
        {
            get { return _motor != null ? _motor.RearSlipSpeed : 0f; }
        }

        public void SetPaused(bool value)
        {
            // The body, its constraints, the motor and the global time scale are never touched:
            // each system owns its pause, so a paused car keeps whatever momentum physics gives it.
            if (IsPaused == value)
                return;
            IsPaused = value;
        }

        /// <summary>World pose of one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR), for placing debris.</summary>
        public bool TryGetWheelWorldPosition(int wheel, out Vector3 position)
        {
            if (_motor != null)
                return _motor.TryGetWheelWorldPosition(wheel, out position);

            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// World pose plus ground contact for one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR), read-only.
        ///
        /// The cosmetic consumers (tyre smoke, ruts) need the two together and per wheel: a car
        /// balancing on two wheels must not press ruts with the two in the air. The legacy model has no
        /// per-wheel contact, so it reports the car-level answer for all four — worse, but never wrong
        /// in the direction that matters (a car that is airborne reports nothing).
        /// </summary>
        public bool TryGetWheelContact(int wheel, out Vector3 position, out bool grounded)
        {
            grounded = IsGrounded;

            var arcade = _motor as ArcadeCarMotor;
            if (arcade != null)
                arcade.TryGetWheelGrounded(wheel, out grounded);

            return TryGetWheelWorldPosition(wheel, out position);
        }

        /// <summary>
        /// Where one wheel touches the world and whether it does, for the CATCH bed test. Only the arcade
        /// model measures a contact; the legacy model has no per-wheel ray, so it reports false and a car
        /// on it can never count as settled on a bed, which fails safe instead of awarding a win.
        /// </summary>
        public bool TryGetWheelContactPoint(int wheel, out Vector3 point, out bool grounded)
        {
            var arcade = _motor as ArcadeCarMotor;
            if (arcade != null)
                return arcade.TryGetWheelContactPoint(wheel, out point, out grounded);

            point = Vector3.zero;
            grounded = false;
            return false;
        }

        public CarSnapshot Snapshot()
        {
            // Read-only capture of live state: never mutates, caches, allocates or steps.
            if (_body == null)
                return default;
            return new CarSnapshot(
                hasBody: true,
                position: _body.position,
                rotation: _body.rotation,
                linearVelocity: _body.linearVelocity,
                angularVelocity: _body.angularVelocity,
                resolvedMode: _motor != null ? _motor.ResolvedMode : default,
                isPaused: IsPaused,
                fixedStepCount: _fixedStepCount,
                yawRate: YawRate);
        }

        /// <summary>
        /// Turn rate about the world up axis, in rad/s, positive to the car's left. Matches the sign the V2
        /// core uses, so a HUD can compare it with the model's target without a second conversion.
        /// </summary>
        public float YawRate
        {
            get
            {
                if (_body == null)
                    return 0f;
                Vector3 angular = _body.angularVelocity;
                if (float.IsNaN(angular.x) || float.IsInfinity(angular.x))
                    return 0f;
                return -Vector3.Dot(angular, _body.transform.up);
            }
        }

        public void Unbind()
        {
            _body = null;
            _motor = null;
            BoundModel = HandlingModel.Legacy;
            _input = default;
            LastAppliedInput = default;
            // Cleared unconditionally (this method also runs when already unbound, since the
            // body guard only skips the constraint restore) so no dangling callback survives
            // disable or rebinding.
            _inputSampler = null;
            _surfaceGrip = null;
            _fixedStepCount = 0;
            IsPaused = false;
        }

        private void OnDisable()
        {
            // Disabling must hand the body back untouched; a disabled component must not keep driving it.
            Unbind();
        }

        private void FixedUpdate()
        {
            if (_motor == null || IsPaused)
                return;
            // One sample per unpaused fixed tick, with the same dt handed to the motor; a null
            // sampler keeps the human latest-wins cache. No allocation here: the delegate is
            // cached and its struct return never boxes.
            float dt = Time.fixedDeltaTime;
            CarInput input = _inputSampler != null ? _inputSampler(dt) : _input;
            if (_surfaceGrip != null)
            {
                // Terrain grip is read from the body's own pose inside the tick it is about to move, so
                // the surface a car feels is the one it is standing on. One cached delegate call.
                _motor.ApplySurfaceGrip(_surfaceGrip(_body.position));
            }
            _motor.Step(input, dt);
            LastAppliedInput = new CarInputSnapshot(
                input.steer, input.throttle, input.brake, input.reverse, input.nitroPressed,
                input.stickX, input.stickY);
            _fixedStepCount++; // Applied steps only: paused ticks neither attempt nor count.
        }
    }
}
