namespace FestivalDerby.Match
{
    /// <summary>
    /// One car's boarding progress. <see cref="Armed"/> is false after the car overtook the truck: time on
    /// the bed does not count again until the car has come back round behind the tail.
    /// </summary>
    public readonly struct BoardingState
    {
        public readonly float SecondsOnBed;
        public readonly bool Armed;

        public BoardingState(float secondsOnBed, bool armed)
        {
            SecondsOnBed = secondsOnBed;
            Armed = armed;
        }

        public static BoardingState Initial
        {
            get { return new BoardingState(0f, true); }
        }
    }

    /// <summary>Pure boarding clock. No Unity dependency, no allocation.</summary>
    public static class BoardingRules
    {
        /// <summary>
        /// Advances one car by <paramref name="dt"/>. Time on the bed is continuous: leaving it for a
        /// moment resets it to zero. Leaving it past the truck's front disarms the car; leaving it behind
        /// the tail (or never reaching it) keeps or restores the armed state.
        /// </summary>
        public static BoardingState Advance(in BoardingState state, bool onBed, float relativeProgress,
            float truckHalfLength, float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
                return state;

            if (onBed)
                return state.Armed
                    ? new BoardingState(state.SecondsOnBed + dt, true)
                    : new BoardingState(0f, false);

            if (relativeProgress > truckHalfLength)
                return new BoardingState(0f, false);
            if (relativeProgress < -truckHalfLength)
                return new BoardingState(0f, true);
            return new BoardingState(0f, state.Armed);
        }
    }
}
