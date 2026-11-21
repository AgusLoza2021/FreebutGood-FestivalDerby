namespace FestivalDerby.Match
{
    /// <summary>
    /// The countdown that removes the last car. It expires, the caller eliminates someone, and it starts
    /// over: the interval restarts on every elimination, not on a fixed beat.
    /// </summary>
    public sealed class EliminationClock
    {
        private readonly float _interval;
        private float _remaining;

        public EliminationClock(float interval)
        {
            _interval = interval > 0f ? interval : 1f;
            _remaining = _interval;
        }

        public float Interval
        {
            get { return _interval; }
        }

        public float Remaining
        {
            get { return _remaining; }
        }

        /// <summary>Counts down. True exactly when it reaches zero; it then restarts itself.</summary>
        public bool Tick(float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
                return false;

            _remaining -= dt;
            if (_remaining > 0f)
                return false;

            _remaining = _interval;
            return true;
        }

        public void Reset()
        {
            _remaining = _interval;
        }
    }
}
