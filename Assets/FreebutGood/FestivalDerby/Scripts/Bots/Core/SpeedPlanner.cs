using FestivalDerby.World;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// Layer B: speed planning. The car must already be slow enough when it reaches a corner, so the
    /// planner previews the racing line's baked target speed ahead of the car and brakes for the
    /// slowest of the sampled points rather than for where it currently is.
    /// </summary>
    public static class SpeedPlanner
    {
        /// <summary>
        /// The slowest target speed among the line under the car and a few preview stations ahead.
        /// Returns <paramref name="topSpeed"/> when the line carries no baked target speed (a legacy
        /// circuit), so the planner degrades to "flat out" instead of braking for nothing.
        /// </summary>
        public static float PreviewTargetSpeed(BakedPath path, float distanceOnPath, float previewMeters, float topSpeed)
        {
            return PreviewTargetSpeed(path, distanceOnPath, previewMeters, topSpeed, 0f);
        }

        /// <summary>
        /// As above, with the corner speed this car's tires can actually make folded into the same
        /// preview. A baked line already carries a planned speed, but a line without one - a legacy
        /// circuit, or a map baked before the planner existed - would otherwise be driven flat out
        /// into a corner it cannot hold. Deriving the cap from each sample's own curvature makes the
        /// bot safe on any route, and it only ever asks for less than the bake did, never more.
        /// A <paramref name="lateralAccelBudget"/> of zero (or less) leaves the baked plan alone.
        /// </summary>
        public static float PreviewTargetSpeed(BakedPath path, float distanceOnPath, float previewMeters,
            float topSpeed, float lateralAccelBudget)
        {
            if (path == null || !path.IsUsable || previewMeters <= 0f)
                return topSpeed;

            float min = topSpeed;
            bool capCorners = lateralAccelBudget > 0f;
            // The near station is what matters in a corner; the far one is what starts the braking.
            for (int i = 0; i <= PreviewStations; i++)
            {
                float lead = previewMeters * i / PreviewStations;
                PathSample sample;
                if (!path.Sample(distanceOnPath + lead, out sample))
                    continue;

                if (sample.TargetSpeed > 0f && sample.TargetSpeed < min)
                    min = sample.TargetSpeed;
                if (capCorners)
                {
                    float corner = CornerSpeed(sample.Curvature, lateralAccelBudget);
                    if (corner < min)
                        min = corner;
                }
            }

            return min;
        }

        /// <summary>
        /// SPEED1: the speed this car's own tires allow along the preview, from curvature alone.
        ///
        /// <see cref="PreviewTargetSpeed"/> answers a different question: it reads the map's baked plan,
        /// which is a braking ramp for a car baked at 9 m/s^2 and therefore lifts before a corner by
        /// construction. A bot that keeps that ramp as its throttle fraction brakes fifty metres early,
        /// and a bot that takes it as its corner speed corners at 75% of what its tires can hold. This
        /// method ignores the bake completely and rebuilds what it needs from the line's curvature:
        /// every station is <c>sqrt(aLat / k)</c>, and then the stations are walked backwards so each
        /// one is lowered to what <paramref name="brakeDecel"/> can still reach from the next
        /// <c>step</c> metres ahead. That backward pass is what a driver does by eye - lift early,
        /// arrive at the corner already at its speed - and it is what keeps a bot whose command lands a
        /// reaction late from braking into the corner instead of before it.
        ///
        /// Returns positive infinity when nothing inside the preview limits the car: a straight is not
        /// a corner, and the motor owns the top end (the authored <c>CarDefinition</c> speed is below
        /// the V2 car's real terminal speed, so capping the plan at it braked bots on every straight).
        /// <paramref name="brakeDecel"/> of zero or less plans the corners without the ramp.
        /// </summary>
        public static float CornerLimitedTarget(BakedPath path, float distanceOnPath, float previewMeters,
            float lateralAccelBudget, float brakeDecel)
        {
            if (path == null || !path.IsUsable || previewMeters <= 0f || !(lateralAccelBudget > 0f))
                return float.PositiveInfinity;

            float step = previewMeters / PreviewStations;
            float reachable = float.PositiveInfinity;
            float allowed = float.PositiveInfinity;
            for (int i = PreviewStations; i >= 0; i--)
            {
                PathSample sample;
                float corner = path.Sample(distanceOnPath + step * i, out sample)
                    ? CornerSpeed(sample.Curvature, lateralAccelBudget)
                    : float.PositiveInfinity;

                allowed = corner < reachable ? corner : reachable;
                if (brakeDecel > 0f && allowed < float.PositiveInfinity)
                    reachable = (float)System.Math.Sqrt(allowed * allowed + 2f * brakeDecel * step);
                else
                    reachable = allowed;
            }

            return allowed;
        }

        /// <summary>
        /// The fastest <paramref name="curvature"/> can be taken at <paramref name="lateralAccelBudget"/>:
        /// <c>v = sqrt(aLat * R)</c>, the same rule the bake plans with. A straight run, or a
        /// curvature the bake could not measure, returns positive infinity so it never becomes the
        /// limit.
        /// </summary>
        public static float CornerSpeed(float curvature, float lateralAccelBudget)
        {
            float bend = BotMath.Abs(curvature);
            if (!(bend > 1e-5f) || !(lateralAccelBudget > 0f))
                return float.PositiveInfinity;

            return (float)System.Math.Sqrt(lateralAccelBudget / bend);
        }

        /// <summary>
        /// Turns the gap between the car's speed and its target speed into a throttle/brake pair.
        /// Above the target by more than <paramref name="brakeMargin"/> the car brakes and lifts;
        /// otherwise it drives at the fraction of top speed the target asks for.
        /// </summary>
        public static void Plan(float currentSpeed, float targetSpeed, float topSpeed, float brakeMargin,
            out float throttle, out bool brake)
        {
            // An unlimited target (a straight, from CornerLimitedTarget) is full pedal and no brake:
            // the infinity clamps to one and no finite speed can exceed it.
            if (targetSpeed < 0f) targetSpeed = 0f;
            if (currentSpeed < 0f) currentSpeed = 0f;

            float speed = topSpeed > 1e-3f ? topSpeed : 1f;
            float fraction = BotMath.Clamp(targetSpeed / speed, 0f, 1f);

            if (currentSpeed > targetSpeed + brakeMargin)
            {
                throttle = 0f;
                brake = true;
                return;
            }

            throttle = fraction;
            brake = false;
        }

        private const int PreviewStations = 4;
    }
}
