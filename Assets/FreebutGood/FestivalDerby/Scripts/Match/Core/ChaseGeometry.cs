namespace FestivalDerby.Match
{
    /// <summary>
    /// Where a car is relative to the truck, measured along the racing line. Progress values are arc
    /// lengths on a closed loop, so everything here wraps instead of special-casing the start line.
    /// </summary>
    public static class ChaseGeometry
    {
        /// <summary>Folds <paramref name="distance"/> into [0, loopLength). A bad loop length gives 0.</summary>
        public static float Wrap(float distance, float loopLength)
        {
            if (!(loopLength > 0f) || float.IsNaN(distance) || float.IsInfinity(distance))
                return 0f;

            float wrapped = distance % loopLength;
            return wrapped < 0f ? wrapped + loopLength : wrapped;
        }

        /// <summary>
        /// Signed metres of the car relative to the truck's centre, in [-L/2, L/2). Positive means the car
        /// is ahead of the truck. Because it wraps, a car that overtook the truck and keeps going comes back
        /// round to negative values, behind the tail again: that lap is what re-arms boarding.
        /// </summary>
        public static float RelativeProgress(float carProgress, float truckProgress, float loopLength)
        {
            if (!(loopLength > 0f))
                return 0f;

            float shifted = Wrap(carProgress - truckProgress + loopLength * 0.5f, loopLength);
            return shifted - loopLength * 0.5f;
        }

        /// <summary>
        /// Distance the car still has to cover to reach the truck's tail from behind: 0 while it is level
        /// with the truck, small when it trails it, nearly a lap when it has just passed it. It is the
        /// "who is last" measure, so a car that overtook the truck ranks behind one that never did.
        /// </summary>
        public static float DistanceToTail(float carProgress, float truckProgress, float loopLength,
            float truckHalfLength)
        {
            if (!(loopLength > 0f))
                return 0f;

            float relative = RelativeProgress(carProgress, truckProgress, loopLength);
            if (relative >= -truckHalfLength && relative <= truckHalfLength)
                return 0f;

            return Wrap(truckProgress - truckHalfLength - carProgress, loopLength);
        }
    }
}
