using FestivalDerby.World;

namespace FestivalDerby.Match
{
    /// <summary>Where the truck is: origin, horizontal heading, velocity and line distance.</summary>
    public readonly struct TruckPose
    {
        public readonly PathVector3 Position;

        /// <summary>Unit length and horizontal: the bed test lives in the XZ plane.</summary>
        public readonly PathVector3 Forward;

        public readonly PathVector3 Velocity;

        /// <summary>Distance along the line, wrapped into [0, line length).</summary>
        public readonly float Distance;

        public TruckPose(PathVector3 position, PathVector3 forward, PathVector3 velocity, float distance)
        {
            Position = position;
            Forward = forward;
            Velocity = velocity;
            Distance = distance;
        }
    }

    /// <summary>
    /// What the physics layer reports about one car for the bed test: its four wheel contact points in
    /// world space, which wheels are grounded, and its velocity. A plain value, free to pass every step.
    /// </summary>
    public readonly struct WheelProbe
    {
        public readonly PathVector3 Wheel0;
        public readonly PathVector3 Wheel1;
        public readonly PathVector3 Wheel2;
        public readonly PathVector3 Wheel3;

        /// <summary>Bit i is set when wheel i touches something; only the low four bits are read.</summary>
        public readonly byte GroundedMask;

        public readonly PathVector3 Velocity;

        public WheelProbe(PathVector3 wheel0, PathVector3 wheel1, PathVector3 wheel2, PathVector3 wheel3,
            byte groundedMask, PathVector3 velocity)
        {
            Wheel0 = wheel0;
            Wheel1 = wheel1;
            Wheel2 = wheel2;
            Wheel3 = wheel3;
            GroundedMask = groundedMask;
            Velocity = velocity;
        }
    }

    /// <summary>
    /// The geometric "settled on the bed" rule. It works from wheel contact points rather than colliders:
    /// the car motor discards the collider it stands on, and a geometric test reads the same in a test and
    /// in the game.
    /// </summary>
    public static class TruckBed
    {
        private const byte AllWheels = 0xF;

        /// <summary>One contact point inside the bed rectangle and height band, in the truck's own frame.</summary>
        public static bool IsWheelOnBed(in TruckPose pose, in TruckSettings settings, PathVector3 point)
        {
            PathVector3 forward = pose.Forward;
            float dx = point.x - pose.Position.x;
            float dy = point.y - pose.Position.y;
            float dz = point.z - pose.Position.z;

            // right = cross(up, forward) with up = (0, 1, 0) and a horizontal forward.
            float lateral = dx * forward.z - dz * forward.x;
            float along = dx * forward.x + dz * forward.z - settings.BedCenterOffset;
            float height = dy - settings.BedHeight;

            // Positive comparisons, so a NaN coordinate falls through to "not on the bed".
            return Abs(lateral) <= settings.BedWidth * 0.5f
                && Abs(along) <= settings.BedLength * 0.5f
                && Abs(height) <= settings.BedHeightTolerance;
        }

        /// <summary>
        /// Settled means every wheel grounded and on the bed, and the car riding with the truck. One wheel
        /// still on the ramp, or a car merely flying over the bed, does not count.
        /// </summary>
        public static bool IsSettled(in TruckPose pose, in TruckSettings settings, in WheelProbe probe)
        {
            if ((probe.GroundedMask & AllWheels) != AllWheels)
                return false;

            if (!IsWheelOnBed(in pose, in settings, probe.Wheel0)
                || !IsWheelOnBed(in pose, in settings, probe.Wheel1)
                || !IsWheelOnBed(in pose, in settings, probe.Wheel2)
                || !IsWheelOnBed(in pose, in settings, probe.Wheel3))
                return false;

            PathVector3 relative = probe.Velocity - pose.Velocity;
            float limit = settings.MaxRelativeSpeed;

            // A NaN speed makes the comparison false, which correctly refuses to settle.
            return relative.SqrMagnitude <= limit * limit;
        }

        private static float Abs(float value)
        {
            return value < 0f ? -value : value;
        }
    }
}
