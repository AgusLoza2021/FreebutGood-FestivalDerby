using FestivalDerby.Driving;
using FestivalDerby.Driving.Handling;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// A tactic that may take the wheel for one physics step. Return true and fill
    /// <paramref name="input"/> to drive; return false to leave the racing line in charge.
    /// </summary>
    public delegate bool BotInputOverride(in BotSelfState self, float dt, out CarInput input);

    /// <summary>
    /// One AI car. It builds the same physics car the player gets (same factory, same fixed-step driver,
    /// same motor setup) and then replaces the driver's input with a <see cref="RacingLineBrain"/>: the
    /// car cannot tell a bot from a thumb. The respawn rules are the player's too.
    /// </summary>
    public sealed class RacingLineBot
    {
        private readonly RacingLineBrain _brain;
        private readonly BakedPath _line;
        private readonly float _topSpeed;
        private OwnedCarGeometry _geometry;
        private LocalCarFixedStepDriver _driver;
        private CarGhostMonitor _ghost;
        private PlayerRespawn _respawn;

        public RacingLineBot(BakedPath racingLine, CarDefinition definition, float pace)
        {
            _line = racingLine;
            _topSpeed = definition != null && definition.maxForwardSpeed > 1f ? definition.maxForwardSpeed : 26f;
            _brain = new RacingLineBrain(pace);
        }

        public Rigidbody Body
        {
            get { return _geometry != null ? _geometry.Body : null; }
        }

        public LocalCarFixedStepDriver Driver
        {
            get { return _driver; }
        }

        public OwnedCarGeometry Geometry
        {
            get { return _geometry; }
        }

        public float TravelledMeters
        {
            get { return _brain.TravelledMeters; }
        }

        public float Pace
        {
            get { return _brain.Pace; }
        }

        /// <summary>The car's top speed, for tactics that plan their own throttle.</summary>
        public float TopSpeed
        {
            get { return _topSpeed; }
        }

        /// <summary>Full steer, in degrees, at the given speed: what one unit of the steer axis means.</summary>
        public float FullSteerDegrees(float speed)
        {
            return _brain.Steering.FullSteerDegrees(speed);
        }

        /// <summary>
        /// Optional tactic consulted before the racing line each physics step. Null (the default) leaves
        /// the bot exactly as it was.
        /// </summary>
        public BotInputOverride InputOverride { get; set; }

        /// <summary>Builds the car on <paramref name="spawn"/> and hands its driver to the brain.</summary>
        public void Spawn(CarDefinition definition, in MotorSetup setup, Pose spawn, Transform parent)
        {
            _geometry = CarGeometryFactory.CreateWithoutFloor(definition, spawn, parent);
            _driver = _geometry.Body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            _driver.Bind(_geometry.Body, definition, setup);
            _brain.Steering = SteeringOf(_driver);
            // Bind clears callbacks, so the sampler goes in afterwards. A method group is one delegate,
            // allocated here once, never per step.
            _driver.SetInputSampler(Sample);
        }

        /// <summary>The player's stuck/flip/fall respawn, with or without the ghost window.</summary>
        public void ArmRespawn(OwnedGrayboxCircuit circuit, CarGhostMonitor ghost)
        {
            _ghost = ghost;
            if (_ghost != null)
                _ghost.Bind(_geometry.Body.transform.Find(CarVisualAttacher.VisualPivotName),
                    new Collider[] { _geometry.Chassis });
            _respawn = new PlayerRespawn(_geometry.Body, circuit, _ghost);
        }

        /// <summary>One render frame: only the respawn clock lives here, the driving is in the physics step.</summary>
        public void Tick(float deltaTime)
        {
            if (_respawn == null || _driver == null)
                return;

            CarHandlingReadout readout = _driver.ReadHandlingDebug();
            _respawn.Tick(deltaTime, readout.SpeedMetersPerSecond, readout.Throttle, _driver.IsOnMovingGround);
        }

        public void Dispose()
        {
            if (_driver != null)
            {
                _driver.SetInputSampler(null);
                _driver.Unbind();
                _driver = null;
            }

            _ghost?.Teardown();
            _ghost = null;
            _respawn = null;
            _geometry?.Dispose();
            _geometry = null;
        }

        private CarInput Sample(float dt)
        {
            Rigidbody body = Body;
            if (body == null)
                return default;

            Transform t = body.transform;
            Vector3 position = t.position;
            Vector3 forward = t.forward;
            Vector3 velocity = body.linearVelocity;
            float planar = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);

            var self = new BotSelfState
            {
                Position = new PathVector3(position.x, position.y, position.z),
                Forward = new PathVector3(forward.x, 0f, forward.z).Normalized,
                Speed = velocity.x * forward.x + velocity.z * forward.z < 0f ? -planar : planar,
                TopSpeed = _topSpeed,
            };
            BotInputOverride tactic = InputOverride;
            if (tactic != null && tactic(in self, dt, out CarInput forced))
            {
                _brain.Track(self, _line);
                return forced;
            }

            return _brain.Decide(self, _line, dt);
        }

        /// <summary>
        /// V2 scales the steer axis by the yaw rate the tyres can still make, which shrinks with speed;
        /// the legacy motor uses a fixed wheel angle. Read from the bound motor so the bot always steers
        /// with the scale of the car it is actually driving.
        /// </summary>
        private static BotSteeringLimits SteeringOf(LocalCarFixedStepDriver driver)
        {
            if (driver.BoundModel != HandlingModel.V2)
                return BotSteeringLimits.Legacy;

            if (!driver.TryGetBoundHandling(out HandlingParams handling))
                handling = HandlingProfile.ResolvePreset(driver.Preset);
            return BotSteeringLimits.V2(
                handling.LateralAccelBudgetMetersPerSecondSquared,
                handling.HeadingGain,
                handling.MaxYawRateRadiansPerSecond,
                handling.MinReferenceSpeedMetersPerSecond);
        }
    }
}
