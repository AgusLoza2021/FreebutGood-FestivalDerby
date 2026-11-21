using System;
using System.Collections;
using FestivalDerby.Driving.Handling;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Driving Model V2 in the car, measured on a real Rigidbody through the real player path.
    ///
    /// The engine-free acceptance numbers live in Tests/Driving/Handling/HandlingAcceptanceTests.cs and run on
    /// <see cref="PlanarCarSim"/>, which fakes the suspension, the gravity axis and the ground contact. These
    /// cases run the same scenarios through <see cref="ArcadeCarMotor"/>, which owns real raycast suspension,
    /// real contact loads and the engine's own inertia tensor, because the point of taking V2 into the car is
    /// that the reference numbers survive on a car that can also flip.
    ///
    /// The car is the reference car of Documentation/Design/DRIVING_MODEL_V2.md, matched by geometry rather
    /// than by a tuned override: at 1200 kg in a 1.8 x 0.7 x 4.2 box Unity's collider-derived yaw inertia is
    /// the same 2088 kg m^2 the planar sim computes by hand. The criteria are the planar ones with the +/-5
    /// degrees of beta slack a real load-transferring suspension buys.
    ///
    /// Input goes through <see cref="LocalCarFixedStepDriver"/> and <see cref="MotorSetupFactory"/>, which is
    /// the path a player's joystick takes, so a case here also pins the config switch and the fixed-step
    /// wiring rather than only the model.
    /// </summary>
    public sealed class ArcadeCarMotorAcceptancePlayModeTests
    {
        private const float BodyMass = 1200f;
        private const float BodyWidth = 1.8f;
        private const float BodyHeight = 0.7f;
        private const float BodyLength = 4.2f;

        private static readonly Vector3 BodySize = new Vector3(BodyWidth, BodyHeight, BodyLength);

        private const float DegreesPerRadian = 57.29578f;
        private const float TwoPi = 6.28318530718f;

        /// <summary>A yaw rate below this is the car sitting still, not a turn direction.</summary>
        private const float YawRateSignEpsilon = 0.05f;

        private const float SettleDegrees = 20f;
        private const float SettleSecondsLimit = 3.5f;
        private const float BetaSlackDegrees = 5f;

        private const float SweepSeconds = 4f;
        private const float StartSpeed = 20f;
        private const int IdleTicksBeforeScenario = 25;

        private const float SpawnHeight = 0.43f;
        private const float RamAggressorX = -2.1f;
        private const float RamAggressorZ = -1.2f;
        private const float RamApproachSpeed = 25f;
        private const int RamTicks = 26;
        private const int RamIdleTicks = 12;

        /// <summary>
        /// A joystick held still with the brake down. The mapper floors a centred stick at 35% throttle, so a
        /// car left on a neutral stick creeps; the cases that need a car to sit still hold it here.
        /// </summary>
        private static readonly CarInput HeldStill = new CarInput(0f, 0f, true, false, false, 0f, 0f);

        private const int MeasuredTicks = 20000;
        private const int WarmupTicks = 2000;

        /// <summary>
        /// This thread's counter still has a floor, so the bound is one page rather than zero. Every per-tick
        /// allocation is multiplied by 20000 ticks, so a single byte per tick lands orders of magnitude above
        /// this bound.
        /// </summary>
        private const long AllocationBoundBytes = 4096L;

        private CarMotorTestRig _rig;
        private GameObject _bearing;
        private Rigidbody _body;
        private LocalCarFixedStepDriver _driver;
        private CarDefinition _definition;
        private DrivingFeatureConfig _config;
        private HandlingParams _params;
        private HeldStickMetrics _held;

        [SetUp]
        public void SetUp()
        {
            _rig = new CarMotorTestRig();
            _params = HandlingParams.Balanced;
            // The stick's bearing is read off the render camera's yaw, and the shipped chase camera keeps a
            // constant world offset, so its bearing is a fixed world direction. A transform that never moves
            // reproduces exactly that: the top of the screen is world +z for the whole case, and the bearings
            // below are measured from it just as the planar cases measure them.
            _bearing = new GameObject("V2 acceptance bearing");
            _bearing.transform.rotation = Quaternion.identity;
            _config = ScriptableObject.CreateInstance<DrivingFeatureConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            // The driver drops its body on unbind; pausing first keeps a stray fixed step off a rig that is
            // already being torn down.
            if (_driver != null)
            {
                _driver.SetPaused(true);
                _driver.Unbind();
            }

            _rig.Dispose();
            if (_bearing != null)
                Object.Destroy(_bearing);
            if (_config != null)
                Object.Destroy(_config);

            _driver = null;
            _body = null;
            _definition = null;
            _config = null;
        }

        // ------------------------------------------------------------------ 1. the 360 degree sweep

        [UnityTest]
        public IEnumerator Acceptance1_FullRingSweep_At20_PedalNeverReversesAndSpeedHolds()
        {
            CreateCar();
            yield return SettleAt(StartSpeed);

            int ticks = (int)(SweepSeconds / Time.fixedDeltaTime);
            float minThrottle = 1f;
            float maxBeta = 0f;
            float maxYawRatio = 0f;
            float minSpeed = ForwardSpeed;
            float maxSpeed = minSpeed;

            for (int i = 0; i < ticks; i++)
            {
                float phase = TwoPi * (i * Time.fixedDeltaTime) / SweepSeconds;
                ApplyStick((float)Math.Sin(phase), (float)Math.Cos(phase));
                yield return new WaitForFixedUpdate();

                CarHandlingReadout readout = ReadAndAssertFinite();
                if (readout.Throttle < minThrottle)
                    minThrottle = readout.Throttle;

                float beta = Math.Abs(readout.BetaRadians) * DegreesPerRadian;
                if (beta > maxBeta)
                    maxBeta = beta;

                float yawMax = YawMaxRadiansPerSecond(ForwardSpeed);
                if (yawMax > 0f)
                {
                    float ratio = Math.Abs(_driver.YawRate) / yawMax;
                    if (ratio > maxYawRatio)
                        maxYawRatio = ratio;
                }

                float speed = ForwardSpeed;
                if (speed < minSpeed)
                    minSpeed = speed;
                if (speed > maxSpeed)
                    maxSpeed = speed;
            }

            float betaLimit = _params.BetaMaxRadians * DegreesPerRadian + BetaSlackDegrees;
            Debug.Log(
                "metric v2b test1 sweep: minThrottle=" + minThrottle.ToString("F3")
                + " max|beta|=" + maxBeta.ToString("F1") + "deg (limit " + betaLimit.ToString("F1") + ")"
                + " max|w|/yawMax=" + maxYawRatio.ToString("F3")
                + " speed=" + minSpeed.ToString("F1") + ".." + maxSpeed.ToString("F1") + " m/s");

            Assert.That(minThrottle, Is.GreaterThanOrEqualTo(0f), "the pedal went negative while the stick swept");
            Assert.That(maxBeta, Is.LessThanOrEqualTo(betaLimit), "sideslip past betaMax + 5 deg");
            Assert.That(maxYawRatio, Is.LessThanOrEqualTo(1.2f), "yaw rate past 1.2 * yawMax");
            Assert.That(minSpeed, Is.GreaterThanOrEqualTo(6f), "speed fell below 6 m/s during the sweep");
        }

        // ---------------------------------------------- 2. stick dead astern with +/-3 degrees of jitter

        [UnityTest]
        public IEnumerator Acceptance2_StickDeadAsternWithJitter_CommitHoldsAndUTurnCompletes()
        {
            yield return RunHeldStick(180f, 3f, 4f, 20261013);

            float betaLimit = _params.BetaMaxRadians * DegreesPerRadian + BetaSlackDegrees;
            Debug.Log(
                "metric v2b test2 jitter: committed=" + _held.Committed + " turnSign=" + _held.CommitSign
                + " settle(|e|<20deg)=" + _held.SettleTime.ToString("F2") + "s"
                + " worstOppositeYaw=" + (_held.WorstOppositeYaw * DegreesPerRadian).ToString("F2") + "deg/s"
                + " max|beta|=" + (_held.MaxBeta * DegreesPerRadian).ToString("F1") + "deg (limit " + betaLimit.ToString("F1") + ")"
                + " speed=" + _held.MinSpeed.ToString("F1") + ".." + _held.MaxSpeed.ToString("F1") + " m/s");

            Assert.That(_held.Committed, Is.True, "the turn never committed past 150 deg");
            Assert.That(_held.WorstOppositeYaw, Is.LessThanOrEqualTo(YawRateSignEpsilon),
                "the yaw rate flipped sign after the commit");
            Assert.That(_held.SettleTime, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(SettleSecondsLimit),
                "the U-turn did not complete (|e| < 20 deg) inside the settled window");
            Assert.That(_held.MaxBeta * DegreesPerRadian, Is.LessThanOrEqualTo(betaLimit),
                "sideslip past betaMax + 5 deg");
        }

        // ------------------------------------------------------------- 3. the owner's down-left case

        [UnityTest]
        public IEnumerator Acceptance3_OwnerDownLeftStick_HoldsOneDirectionAndCompletes()
        {
            yield return RunHeldStick(225f, 0f, 4f, 1);

            float betaLimit = _params.BetaMaxRadians * DegreesPerRadian + BetaSlackDegrees;
            Debug.Log(
                "metric v2b test3 down-left: settle(|e|<20deg)=" + _held.SettleTime.ToString("F2") + "s"
                + " worstOppositeYaw=" + (_held.WorstOppositeYaw * DegreesPerRadian).ToString("F2") + "deg/s"
                + " max|beta|=" + (_held.MaxBeta * DegreesPerRadian).ToString("F1") + "deg (limit " + betaLimit.ToString("F1") + ")"
                + " speed=" + _held.MinSpeed.ToString("F1") + ".." + _held.MaxSpeed.ToString("F1") + " m/s");

            Assert.That(_held.WorstOppositeYaw, Is.LessThanOrEqualTo(YawRateSignEpsilon),
                "the yaw rate flipped sign while the stick was held down-left");
            Assert.That(_held.SettleTime, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(SettleSecondsLimit),
                "the turn did not complete (|e| < 20 deg) inside the settled window");
            Assert.That(_held.MaxBeta * DegreesPerRadian, Is.LessThanOrEqualTo(betaLimit),
                "sideslip past betaMax + 5 deg");
        }

        // --------------------------------------------------------------------------------- 5. launch

        [UnityTest]
        public IEnumerator Acceptance5_Launch_ZeroToTwentyInTheSpecWindow()
        {
            CreateCar();
            yield return SettleAt(0f);

            float launchTime = -1f;
            int ticks = (int)(6f / Time.fixedDeltaTime);
            for (int i = 0; i < ticks; i++)
            {
                ApplyStick(0f, 1f);
                yield return new WaitForFixedUpdate();
                ReadAndAssertFinite();

                if (launchTime < 0f && ForwardSpeed >= 20f)
                    launchTime = i * Time.fixedDeltaTime;
            }

            Debug.Log("metric v2b test5 launch: t(0->20)=" + launchTime.ToString("F2")
                + "s terminal=" + ForwardSpeed.ToString("F1") + " m/s");

            // FEEL2 (owner playtest round 3) moved the launch window from 2.3-3.3 s to a punchy one:
            // the planar sim does it in 1.06 s and the real Rigidbody, with suspension and load
            // transfer, measured 1.48 s.
            Assert.That(launchTime, Is.GreaterThanOrEqualTo(0.9f), "0->20 m/s launched faster than the spec window");
            Assert.That(launchTime, Is.LessThanOrEqualTo(1.8f), "0->20 m/s launched slower than the spec window");
        }

        // ------------------------------------------------------------------ 6. the ram still flips it

        /// <summary>
        /// The ram response is the one behaviour V2 could plausibly have broken: the model carries an
        /// auto-roll that levels the car, and levelling is exactly what a flip has to survive. A corner ram
        /// at 25 m/s has to still put a V2 car past 90 degrees of tilt inside the roll-mute window.
        /// </summary>
        [UnityTest]
        public IEnumerator Acceptance6_CornerRamOnAV2Car_StillFlipsItPastNinetyDegrees()
        {
            CreateCar();
            _body.transform.position = new Vector3(0f, SpawnHeight, 0f);
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.gameObject.AddComponent<CarRamLift>().Bind(_body, _definition);

            Rigidbody aggressor = _rig.Body("v2 ram aggressor", BodyMass, BodySize);
            aggressor.transform.position = new Vector3(RamAggressorX, SpawnHeight, RamAggressorZ);
            // The live game builds cars with continuous detection; a 25 m/s side ram would otherwise tunnel a
            // whole tick of travel before the contact is seen.
            aggressor.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            aggressor.gameObject.AddComponent<CarRamLift>().Bind(aggressor, _definition);

            for (int i = 0; i < RamIdleTicks; i++)
            {
                // Braked, so the steering floor cannot creep the victim out of the ram's aim while the
                // suspension settles: this case is about the flip, not about the creep.
                _driver.SetInput(HeldStill);
                yield return new WaitForFixedUpdate();
            }

            aggressor.linearVelocity = new Vector3(RamApproachSpeed, 0f, 0f);
            Assert.That(RamApproachSpeed, Is.GreaterThan(_definition.ramLiftThreshold),
                "the setup must close faster than the configured ram threshold");

            float maxTilt = 0f;
            for (int tick = 0; tick < RamTicks; tick++)
            {
                _driver.SetInput(default);
                yield return new WaitForFixedUpdate();
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(_body.transform.up, Vector3.up));
            }

            CarMotorTestRig.AssertFinite(_body.angularVelocity, "victim spin must stay finite after a ram");
            Debug.Log("metric v2b test6 ram: maxTilt=" + maxTilt.ToString("F1")
                + "deg (needs > 90) yawRate=" + _driver.YawRate.ToString("F2") + " rad/s");

            Assert.That(maxTilt, Is.GreaterThan(90f),
                "a 25 m/s corner ram must roll a V2 car past 90 degrees; measured max tilt was " + maxTilt);
            Assert.That(_body.transform.position.y, Is.GreaterThan(-1f), "the victim fell through the floor");
        }

        // -------------------------------------------------------------------------- 7. no allocation

        /// <summary>
        /// The measurement is the calling thread's allocation counter, not the process heap. The heap version
        /// of this case also charged whatever the Editor and its background workers allocated while the batch
        /// ran, which measured 0, 4096, 315392 and 319488 bytes (0, 1, 77 and 78 GC pages) for the same
        /// allocation-free motor, against a bound of one page: that noise alone could turn the case red. A
        /// per-tick allocation in the motor still lands here, multiplied by the batch length. The motor is
        /// stepped directly because a fixed step can only be reached through a frame yield, and the frames
        /// would contribute their own allocations to the sample.
        /// </summary>
        [Test]
        public void Acceptance7_NoAllocationOnTheV2TickPath()
        {
            _definition = _rig.Definition();
            _definition.bodySize = BodySize;
            _body = _rig.Body("v2 allocation car", BodyMass, BodySize);
            _body.linearVelocity = _body.transform.forward * StartSpeed;

            var motor = new ArcadeCarMotor(
                _body,
                _definition,
                HandlingProfile.ResolvePreset(HandlingPreset.Balanced),
                HandlingPreset.Balanced,
                JoystickDriveMode.Direction,
                _bearing.transform);

            var input = new CarInput(0f, 1f, false, false, false, 0.4f, 0.9f);
            const float dt = 0.02f;
            for (int i = 0; i < WarmupTicks; i++)
                motor.Step(input, dt);

            // The rig and the motor allocated; only the steady-state tick is measured.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < MeasuredTicks; i++)
                motor.Step(input, dt);

            long after = GC.GetAllocatedBytesForCurrentThread();
            long allocated = after - before;

            Debug.Log("metric v2b test7 allocations: " + allocated + " bytes over " + MeasuredTicks
                + " ticks (bound " + AllocationBoundBytes + ")");

            Assert.That(motor.IsGrounded, Is.True, "an airborne motor would skip most of the tick path");
            Assert.That(allocated, Is.LessThanOrEqualTo(AllocationBoundBytes),
                "the V2 tick allocated " + allocated + " bytes over " + MeasuredTicks + " ticks");
        }

        // ------------------------------------- 8. the bot reading turns the way its own steer axis asked

        /// <summary>
        /// Bots, and any car the config leaves on the legacy reading, feed a signed steer axis that is
        /// positive to the right. The adapter converts that axis into a heading error before the mapper
        /// sees it, and an inverted sign there is invisible to every stick-driven case: the car drives,
        /// turns, and simply spirals away from the goal its own steering asked for. This case pins the
        /// direction from the axis a bot feeds, on the same rig, so a regression cannot hide behind the
        /// stick cases passing.
        /// </summary>
        [UnityTest]
        public IEnumerator Acceptance8_CarRelativeSteerAxis_YawsTheWayTheAxisAsks()
        {
            CreateCar(JoystickDriveMode.CarRelative);
            yield return SettleAt(StartSpeed);

            Vector3 forward = _body.transform.forward;
            float startYaw = -(float)Math.Atan2(forward.x, forward.z);
            float minYawRate = 0f;

            for (int i = 0; i < 40; i++)
            {
                // Full axis to the right, full pedal: what a bot feeds when its goal sits right of its nose.
                _driver.SetInput(new CarInput(1f, 1f, false, false, false));
                yield return new WaitForFixedUpdate();
                CarHandlingReadout readout = ReadAndAssertFinite();
                if (readout.YawRateRadiansPerSecond < minYawRate)
                    minYawRate = readout.YawRateRadiansPerSecond;
            }

            Vector3 end = _body.transform.forward;
            float headingDelta = Mathf.DeltaAngle(startYaw * DegreesPerRadian, -(float)Math.Atan2(end.x, end.z) * DegreesPerRadian)
                / DegreesPerRadian;

            Debug.Log("metric v2b test8 carrelative: headingDelta=" + (headingDelta * DegreesPerRadian).ToString("F1")
                + "deg worstYawRate=" + minYawRate.ToString("F3") + " rad/s speed=" + ForwardSpeed.ToString("F1"));

            // Right is clockwise, which is negative in the core's counter-clockwise-positive frame.
            Assert.That(headingDelta, Is.LessThan(-0.1f),
                "a positive steer axis turned the car counter-clockwise, so the bot chases its goal backwards");
            Assert.That(minYawRate, Is.LessThan(-0.05f),
                "a positive steer axis never produced a clockwise yaw rate");
        }

        // --------------------------------------------------------------------------------- helpers

        private struct HeldStickMetrics
        {
            public bool Committed;
            public float CommitSign;
            public float InitialSign;
            public float SettleTime;
            public float WorstOppositeYaw;
            public float MaxBeta;
            public float MinSpeed;
            public float MaxSpeed;
        }

        /// <summary>
        /// Builds the car, binds it through the factory the run uses and proves the switch landed on V2: a case
        /// that silently ran the legacy motor would measure twenty seconds of nothing and pass.
        /// </summary>
        private void CreateCar(JoystickDriveMode driveMode = JoystickDriveMode.Direction)
        {
            _config.HandlingModel = HandlingModel.V2;
            // Null is Balanced by contract, which is the preset the reference numbers were measured on.
            _config.Handling = null;
            _config.DriveMode = driveMode;
            _config.StickDeadZone = 0.15f;

            _definition = _rig.Definition();
            _definition.bodySize = BodySize;
            _body = _rig.Body("v2 acceptance car", BodyMass, BodySize);
            _driver = _body.gameObject.AddComponent<LocalCarFixedStepDriver>();

            MotorSetup setup = MotorSetupFactory.Resolve(_config, _config.DriveMode, _bearing.transform);
            _driver.Bind(_body, _definition, setup);

            Assert.That(_driver.BoundModel, Is.EqualTo(HandlingModel.V2),
                "the config did not select V2, so this case would have measured the legacy motor");
            Assert.That(_driver.Preset, Is.EqualTo(HandlingPreset.Balanced));
        }

        /// <summary>
        /// Idle ticks until the suspension carries the car, then the scenario's starting speed: the planar
        /// cases start from a settled planar state, so the real car has to be handed the same one.
        /// </summary>
        private IEnumerator SettleAt(float forwardSpeed)
        {
            for (int i = 0; i < IdleTicksBeforeScenario; i++)
            {
                _driver.SetInput(default);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(_driver.IsGrounded, Is.True, "the car never found the floor");
            _body.linearVelocity = _body.transform.forward * forwardSpeed;
            _body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private IEnumerator RunHeldStick(float bearingDegrees, float jitterDegrees, float seconds, int seed)
        {
            CreateCar();
            yield return SettleAt(StartSpeed);

            var noise = new Lcg(seed);
            _held = new HeldStickMetrics
            {
                SettleTime = -1f,
                MinSpeed = StartSpeed,
                MaxSpeed = StartSpeed
            };

            int ticks = (int)(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < ticks; i++)
            {
                float jitter = jitterDegrees > 0f ? noise.NextRange(-jitterDegrees, jitterDegrees) : 0f;
                float bearing = (bearingDegrees + jitter) / DegreesPerRadian;
                ApplyStick((float)Math.Sin(bearing), (float)Math.Cos(bearing));
                yield return new WaitForFixedUpdate();

                CarHandlingReadout readout = ReadAndAssertFinite();
                if (readout.TurnCommitted && !_held.Committed)
                {
                    _held.Committed = true;
                    _held.CommitSign = readout.TurnSign;
                }

                // The reference direction is the committed one when there is a commit and the initial request
                // otherwise, as the planar case resolves it: the down-left case never reaches the commit
                // threshold and would otherwise have no reference at all.
                if (_held.InitialSign == 0f)
                    _held.InitialSign = Math.Sign(readout.HeadingErrorRadians);

                // Only a rate above the epsilon counts as turning, and only while the heading is still outside
                // the settle tolerance: once the turn is over, a damping counter-oscillation is not a flip.
                float absoluteError = Math.Abs(readout.HeadingErrorRadians);
                float referenceSign = _held.Committed ? _held.CommitSign : _held.InitialSign;
                if (absoluteError > SettleDegrees / DegreesPerRadian && referenceSign != 0f
                    && Math.Abs(_driver.YawRate) > YawRateSignEpsilon)
                {
                    float opposite = -referenceSign * _driver.YawRate;
                    if (opposite > _held.WorstOppositeYaw)
                        _held.WorstOppositeYaw = opposite;
                }

                if (_held.SettleTime < 0f && absoluteError < SettleDegrees / DegreesPerRadian)
                    _held.SettleTime = i * Time.fixedDeltaTime;

                if (Math.Abs(readout.BetaRadians) > _held.MaxBeta)
                    _held.MaxBeta = Math.Abs(readout.BetaRadians);

                float speed = ForwardSpeed;
                if (speed < _held.MinSpeed)
                    _held.MinSpeed = speed;
                if (speed > _held.MaxSpeed)
                    _held.MaxSpeed = speed;
            }
        }

        /// <summary>
        /// One joystick sample, with the pedal following the stick's own distance so the resolved control mode
        /// is the one the shipped controls resolve. The mapper still owns the throttle: in Direction mode the
        /// axes are only what the mode readout reads.
        /// </summary>
        private void ApplyStick(float stickX, float stickY)
        {
            float throttle = Mathf.Clamp01((float)Math.Sqrt(stickX * stickX + stickY * stickY));
            _driver.SetInput(new CarInput(0f, throttle, false, false, false, stickX, stickY));
        }

        private CarHandlingReadout ReadAndAssertFinite()
        {
            Vector3 velocity = _body.linearVelocity;
            bool finite = !(float.IsNaN(velocity.x) || float.IsInfinity(velocity.x)
                || float.IsNaN(velocity.y) || float.IsInfinity(velocity.y)
                || float.IsNaN(velocity.z) || float.IsInfinity(velocity.z));
            Assert.That(finite, Is.True, "velocity must stay finite while the V2 motor steps");
            return _driver.ReadHandlingDebug();
        }

        /// <summary>Body-frame forward speed, the axis the reference sim integrates.</summary>
        private float ForwardSpeed
        {
            get
            {
                Vector3 up = _body.transform.up;
                if (up.sqrMagnitude < 1e-6f)
                    return 0f;
                up.Normalize();
                Vector3 forward = Vector3.ProjectOnPlane(_body.transform.forward, up);
                if (forward.sqrMagnitude < 1e-6f)
                    return 0f;
                forward.Normalize();
                return Vector3.Dot(_body.linearVelocity, forward);
            }
        }

        private float YawMaxRadiansPerSecond(float speed)
        {
            return Math.Min(
                _params.MaxYawRateRadiansPerSecond,
                _params.LateralAccelBudgetMetersPerSecondSquared
                    / Math.Max(Math.Abs(speed), _params.MinReferenceSpeedMetersPerSecond));
        }

        /// <summary>A tiny deterministic generator: the jitter has to be identical on every run.</summary>
        private struct Lcg
        {
            private uint _state;

            public Lcg(int seed)
            {
                _state = (uint)seed * 2654435761u + 1u;
            }

            public uint Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return _state;
            }

            public float NextRange(float min, float max)
            {
                float unit = (Next() >> 8) * (1f / 16777216f);
                return min + (max - min) * unit;
            }
        }
    }
}
