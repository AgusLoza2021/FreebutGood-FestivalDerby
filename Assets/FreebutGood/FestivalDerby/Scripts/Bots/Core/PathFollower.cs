using FestivalDerby.World;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// The cached segment index a follower threads through its queries. Keeping it in a struct the
    /// caller owns means the follower allocates nothing and still avoids a binary search per tick.
    /// </summary>
    public struct PathQuery
    {
        public int HintIndex;
    }

    /// <summary>
    /// Layer A: pure pursuit. The look-ahead point grows with speed so a fast car steers to where it
    /// will be rather than where it is, which is what keeps a 40 m/s racer from sawing the wheel.
    /// </summary>
    public static class PathFollower
    {
        /// <summary>
        /// Resolves the look-ahead point on the racing line. <paramref name="lookAhead"/> is the
        /// arc-length lead in metres; a caller normally passes BaseLookAhead + gain * speed.
        /// </summary>
        public static bool LookAheadPoint(in BotSelfState self, BakedPath path, ref PathQuery query,
            float lookAhead, out PathVector3 point, out float distanceOnPath, out PathVector3 tangent)
        {
            point = self.Position;
            distanceOnPath = 0f;
            tangent = self.Forward;
            if (path == null || !path.IsUsable)
                return false;

            distanceOnPath = path.NearestDistance(self.Position, ref query.HintIndex);
            PathSample sample;
            if (!path.Sample(distanceOnPath + lookAhead, out sample))
                return false;

            point = sample.Position;
            tangent = sample.Tangent;
            return true;
        }

        /// <summary>
        /// A look-ahead distance that grows with speed: <c>base + gain * |speed|</c>, clamped to
        /// <paramref name="maxLookAhead"/>. At a standstill a bot looks just past its nose; at full
        /// speed it looks far enough ahead to plan the corner.
        /// </summary>
        public static float LookAheadForSpeed(float speed, float baseLookAhead, float speedGain, float maxLookAhead)
        {
            float lead = baseLookAhead + speedGain * BotMath.Abs(speed);
            if (lead > maxLookAhead) lead = maxLookAhead;
            return lead;
        }

        /// <summary>
        /// Sets the steering normal: the first local direction from the car to <paramref name="target"/>
        /// is a turn of <c>SignedAngleDegrees(forward, target - position)</c>, normalised by
        /// <paramref name="fullSteerDegrees"/> and clamped to [-1, 1]. Positive turns toward
        /// <see cref="BotMath.Right"/>, exactly the motor's steer sign.
        /// </summary>
        public static float SteerTowards(in BotSelfState self, PathVector3 target, float fullSteerDegrees)
        {
            float span = fullSteerDegrees > 1f ? fullSteerDegrees : 1f;
            float degrees = BotMath.SignedAngleDegrees(self.Forward, target - self.Position);
            return BotMath.Clamp(degrees / span, -1f, 1f);
        }
    }
}
