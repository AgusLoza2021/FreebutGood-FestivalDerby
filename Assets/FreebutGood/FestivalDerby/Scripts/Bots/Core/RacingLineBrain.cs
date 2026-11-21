using FestivalDerby.Driving;
using FestivalDerby.World;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// A deliberately small race driver: aim at a point a little ahead on the racing line (pure pursuit),
    /// plan the speed for the corners coming up (<see cref="SpeedPlanner"/>), and back out of a wall when
    /// the car has stopped. It is the race layer of the full game's bot brain with the tactics, avoidance,
    /// noise and recovery planner left out, so it reads in one sitting.
    ///
    /// Engine-free and allocation-free: one instance per car, one <see cref="Decide"/> per physics step.
    /// </summary>
    public sealed class RacingLineBrain
    {
        private const float BaseLookAhead = 6f;
        private const float LookAheadSpeedGain = 0.35f;
        private const float MaxLookAhead = 18f;
        private const float BrakeMargin = 1.5f;
        private const float PreviewMeters = 22f;

        // Below the car's real brake authority on purpose: the plan then lifts a little early, which is
        // the margin between the step a bot decides and the step its command reaches the wheels.
        private const float BrakePlanningDecel = 10f;

        // The lateral grip the shipped maps were baked with; only used for a car on the legacy motor,
        // which has no tyre numbers of its own to plan with.
        private const float LegacyLateralAccelBudget = 9f;

        // Aim point behind the car (a spin, a hit): cut the pedal so it can turn back inside the track.
        private const float PivotThrottleCut = 0.8f;
        private const float TurnBackBrakeAimDot = -0.5f;
        private const float PivotBrakeSpeed = 8f;

        // Stopped against something with the pedal down: reverse out of it, steering, for a moment.
        private const float StallSpeed = 0.4f;
        private const float StallSeconds = 1.5f;
        private const float ReverseSeconds = 0.8f;
        private const float ReverseSteer = 0.65f;

        private readonly float _pace;
        private PathQuery _query;
        private RouteProgress _progress;
        private float _stalled;
        private float _reversing;

        /// <summary>What one unit of this car's steer axis means; set by the Unity glue from the bound motor.</summary>
        public BotSteeringLimits Steering = BotSteeringLimits.Legacy;

        /// <param name="pace">
        /// 0.5..1: the share of the tyres' cornering grip this driver dares to use. 1 is a perfect driver,
        /// lower values brake earlier and corner slower, which is what spreads a field out.
        /// </param>
        public RacingLineBrain(float pace)
        {
            _pace = BotMath.Clamp(pace, 0.5f, 1f);
        }

        public float Pace
        {
            get { return _pace; }
        }

        /// <summary>Metres of racing line covered so far, forwards only. A stall signal and a test probe.</summary>
        public float TravelledMeters
        {
            get { return _progress.Travelled; }
        }

        /// <summary>
        /// Keeps the distance covered up to date on a step another driver controlled, so the progress
        /// readout never freezes while a tactic (like boarding the truck) has the wheel.
        /// </summary>
        public void Track(in BotSelfState self, BakedPath line)
        {
            _progress.Advance(line, self.Position);
        }

        /// <summary>One physics step: the command the car should receive.</summary>
        public CarInput Decide(in BotSelfState self, BakedPath line, float dt)
        {
            _progress.Advance(line, self.Position);

            if (TryReverseOutOfStall(self, dt, out CarInput reverse))
                return reverse;

            float lookAhead = PathFollower.LookAheadForSpeed(self.Speed, BaseLookAhead, LookAheadSpeedGain, MaxLookAhead);
            if (!PathFollower.LookAheadPoint(self, line, ref _query, lookAhead, out PathVector3 aim,
                    out float distanceOnLine, out PathVector3 _))
                return default; // No usable line: coast rather than invent a route.

            float targetSpeed;
            if (Steering.IsV2)
            {
                // A V2 car plans its corners from its own tyres; the pace is how much of them it uses.
                targetSpeed = SpeedPlanner.CornerLimitedTarget(line, distanceOnLine, PreviewMeters,
                    Steering.LateralAccelBudget * _pace, BrakePlanningDecel);
            }
            else
            {
                targetSpeed = SpeedPlanner.PreviewTargetSpeed(line, distanceOnLine, PreviewMeters,
                    self.TopSpeed, LegacyLateralAccelBudget * _pace);
            }

            SpeedPlanner.Plan(self.Speed, targetSpeed, self.TopSpeed, BrakeMargin, out float throttle, out bool brake);
            float steer = PathFollower.SteerTowards(self, aim, Steering.FullSteerDegrees(self.Speed));
            TurnBack(self, aim, ref throttle, ref brake);

            return new CarInput(steer, throttle, brake, false, false);
        }

        private bool TryReverseOutOfStall(in BotSelfState self, float dt, out CarInput command)
        {
            float step = dt > 0f && dt < 1f ? dt : 0f;
            if (_reversing <= 0f)
            {
                _stalled = BotMath.Abs(self.Speed) < StallSpeed ? _stalled + step : 0f;
                if (_stalled >= StallSeconds)
                {
                    _stalled = 0f;
                    _reversing = ReverseSeconds;
                }
            }

            if (_reversing > 0f)
            {
                _reversing -= step;
                command = new CarInput(ReverseSteer, 1f, false, true, false);
                return true;
            }

            command = default;
            return false;
        }

        /// <summary>
        /// An aim point behind the car means it is facing the wrong way. At full pedal its turning circle
        /// is wider than the track, so the throttle is cut with the angle and a fast car brakes first.
        /// </summary>
        private static void TurnBack(in BotSelfState self, PathVector3 aim, ref float throttle, ref bool brake)
        {
            PathVector3 toAim = BotMath.Horizontal(aim - self.Position).Normalized;
            float aimDot = self.Forward.x * toAim.x + self.Forward.z * toAim.z;
            if (aimDot >= 0f)
                return;

            throttle *= 1f - PivotThrottleCut * BotMath.Clamp01(-aimDot);
            if (aimDot < TurnBackBrakeAimDot && BotMath.Abs(self.Speed) > PivotBrakeSpeed)
                brake = true;
        }
    }
}
