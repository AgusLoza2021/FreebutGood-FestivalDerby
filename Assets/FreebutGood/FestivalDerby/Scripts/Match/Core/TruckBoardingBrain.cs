using System;
using FestivalDerby.World;

namespace FestivalDerby.Match
{
    /// <summary>A bot's own car reduced to the numbers the boarding planner needs.</summary>
    public readonly struct BoardingSelf
    {
        public readonly PathVector3 Position;

        /// <summary>Horizontal, normalised heading.</summary>
        public readonly PathVector3 Forward;

        /// <summary>Signed speed along the heading. Negative while reversing.</summary>
        public readonly float Speed;

        public readonly float TopSpeed;

        public BoardingSelf(PathVector3 position, PathVector3 forward, float speed, float topSpeed)
        {
            Position = position;
            Forward = forward;
            Speed = speed;
            TopSpeed = topSpeed;
        }
    }

    /// <summary>The command the boarding planner wants for one physics step.</summary>
    public readonly struct BoardingCommand
    {
        public readonly float Steer;
        public readonly float Throttle;
        public readonly bool Brake;
        public readonly bool Reverse;

        public BoardingCommand(float steer, float throttle, bool brake, bool reverse)
        {
            Steer = steer;
            Throttle = throttle;
            Brake = brake;
            Reverse = reverse;
        }
    }

    /// <summary>
    /// Takes over a bot while it chases the truck: line up with the ramp, close the gap on a braking
    /// curve so it arrives at the foot of the ramp only a little faster than the truck, climb with
    /// momentum, then hold station near the middle of the bed at the truck's speed.
    ///
    /// It only claims the bot while the bot is armed and within reach behind the truck. After an
    /// overtake (or far away) it declines and the bot keeps following the racing line, which is how a
    /// bot "goes round for another lap". Engine-free and allocation-free: one instance per car.
    /// </summary>
    public sealed class TruckBoardingBrain
    {
        /// <summary>Gap behind the tail at which the planner starts to care about the truck.</summary>
        public const float ApproachMeters = 50f;

        private const float BrakeDecel = 6f;

        // Enough speed over the truck to climb the ramp without stalling on it.
        private const float MinClimbRelative = 3f;
        private const float HoldGain = 0.8f;
        private const float HoldRelativeLimit = 3f;
        private const float BrakeMargin = 1f;
        private const float BaseLookAhead = 6f;
        private const float LookAheadSpeedGain = 0.35f;
        private const float LookAheadLateralGain = 2.5f;
        private const float MinAimAdvance = 1.5f;

        private const float StallSpeed = 0.8f;
        private const float StallSeconds = 1.5f;
        private const float ReverseSeconds = 0.8f;

        private readonly float _maxClosing;
        private float _stalled;
        private float _reversing;

        /// <param name="pace">
        /// 0.5..1, the same pace the bot's racing brain has: a bolder driver closes on the truck faster.
        /// </param>
        public TruckBoardingBrain(float pace)
        {
            float clamped = pace < 0.5f ? 0.5f : (pace > 1f ? 1f : pace);
            _maxClosing = 6f + 8f * (clamped - 0.5f) / 0.5f;
        }

        /// <summary>
        /// One physics step. False means "not mine": the caller keeps its normal driving. True fills
        /// <paramref name="command"/>. <paramref name="distanceToTail"/> comes from
        /// <see cref="ChaseGeometry.DistanceToTail"/>, so a car half a lap away is never mistaken for one
        /// that merely lies near the truck in space.
        /// </summary>
        public bool TryDecide(in BoardingSelf self, in TruckPose truck, in TruckSettings settings, bool armed,
            float distanceToTail, float fullSteerDegrees, float dt, out BoardingCommand command)
        {
            command = default;
            if (!armed || !(distanceToTail <= ApproachMeters))
            {
                _stalled = 0f;
                _reversing = 0f;
                return false;
            }

            PathVector3 forward = truck.Forward;
            var right = new PathVector3(forward.z, 0f, -forward.x);
            PathVector3 offset = self.Position - truck.Position;
            float along = offset.x * forward.x + offset.z * forward.z;
            float lateral = offset.x * right.x + offset.z * right.z;

            float bedFront = settings.BedCenterOffset + settings.BedLength * 0.5f;
            float rearEdge = settings.BedCenterOffset - settings.BedLength * 0.5f;
            float rampFoot = rearEdge - settings.RampLength;
            float hold = settings.BedCenterOffset;

            // Past the front of the truck there is nothing left to board: this is an overtake in progress.
            if (along > bedFront + 1f)
            {
                _stalled = 0f;
                _reversing = 0f;
                return false;
            }

            float step = dt > 0f && dt < 1f ? dt : 0f;
            if (TryReverseOutOfStall(self.Speed, along < rearEdge + 0.5f, step, out command))
                return true;

            float relative = RelativeSpeedFor(along, rampFoot, rearEdge, hold);
            float target = settings.Speed + relative;
            if (target < 0f)
                target = 0f;

            float lookAhead = Max(BaseLookAhead, LookAheadSpeedGain * Max(self.Speed, 0f), LookAheadLateralGain * Abs(lateral));
            float aimAlong = Max(Min(along + lookAhead, hold), along + MinAimAdvance);
            PathVector3 aim = truck.Position + forward * aimAlong;
            float steer = SteerTowards(self, aim, fullSteerDegrees);

            float topSpeed = self.TopSpeed > 1e-3f ? self.TopSpeed : 1f;
            float speed = self.Speed > 0f ? self.Speed : 0f;
            bool brake = speed > target + BrakeMargin;
            float throttle = brake ? 0f : Clamp(target / topSpeed, 0f, 1f);

            command = new BoardingCommand(steer, throttle, brake, false);
            return true;
        }

        /// <summary>
        /// Speed to keep relative to the truck, from where the car is along the truck's own axis. Before the
        /// ramp it follows a braking curve; on the ramp it keeps climbing; on the bed it holds station.
        /// </summary>
        private float RelativeSpeedFor(float along, float rampFoot, float rearEdge, float hold)
        {
            if (along < rampFoot)
                return Clamp((float)Math.Sqrt(2f * BrakeDecel * (rampFoot - along)), MinClimbRelative, _maxClosing);

            if (along < rearEdge + 0.5f)
                return MinClimbRelative;

            return Clamp(HoldGain * (hold - along), -HoldRelativeLimit, HoldRelativeLimit);
        }

        private bool TryReverseOutOfStall(float speed, bool belowTheBed, float step, out BoardingCommand command)
        {
            if (_reversing <= 0f)
            {
                _stalled = belowTheBed && Abs(speed) < StallSpeed ? _stalled + step : 0f;
                if (_stalled >= StallSeconds)
                {
                    _stalled = 0f;
                    _reversing = ReverseSeconds;
                }
            }

            if (_reversing > 0f)
            {
                _reversing -= step;
                command = new BoardingCommand(0f, 1f, false, true);
                return true;
            }

            command = default;
            return false;
        }

        /// <summary>Positive turns toward the car's right, the sign the motor's steer axis expects.</summary>
        private static float SteerTowards(in BoardingSelf self, PathVector3 target, float fullSteerDegrees)
        {
            float span = fullSteerDegrees > 1f ? fullSteerDegrees : 1f;
            float toX = target.x - self.Position.x;
            float toZ = target.z - self.Position.z;
            float toLength = (float)Math.Sqrt(toX * toX + toZ * toZ);
            if (toLength < 1e-4f)
                return 0f;

            toX /= toLength;
            toZ /= toLength;
            float dot = self.Forward.x * toX + self.Forward.z * toZ;
            float cross = self.Forward.z * toX - self.Forward.x * toZ;
            float degrees = (float)(Math.Atan2(cross, dot) * (180.0 / Math.PI));
            return Clamp(degrees / span, -1f, 1f);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static float Abs(float value)
        {
            return value < 0f ? -value : value;
        }

        private static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        private static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        private static float Max(float a, float b, float c)
        {
            return Max(Max(a, b), c);
        }
    }
}
