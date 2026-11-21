namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Engine-free hold counter for a press-and-hold affordance: accumulates while <c>held</c>,
    /// fires exactly once when the held time reaches <c>holdSeconds</c>, and stays latched until
    /// <see cref="Reset"/>. Releasing the hold restarts the count, so a thumb that stutters across
    /// the button can never reach the threshold by averaging attempts.
    ///
    /// Deliberately pure: no Unity API, no allocation, so the fixed tick, an unscaled UI update and
    /// the engine-free harness can all call it. Mirror of <see cref="StuckRespawnClock"/>'s contract
    /// for the one-shot latch, minus the demand/speed inputs it does not need.
    /// </summary>
    public sealed class HoldProgress
    {
        private readonly float _holdSeconds;
        private float _heldSeconds;
        private bool _due;

        public HoldProgress(float holdSeconds)
        {
            _holdSeconds = holdSeconds;
        }

        /// <summary>
        /// Held fraction 0..1. A latched hold reports 1, and a non-positive duration reports 1 as
        /// soon as it is held (a zero-second hold is an always-fire, never a stuck bar).
        /// </summary>
        public float Progress01
        {
            get
            {
                if (_due)
                    return 1f;
                if (_holdSeconds <= 0f)
                    return 0f;
                return _heldSeconds / _holdSeconds;
            }
        }

        /// <summary>Clears the count and the latch, so the next hold must earn the full duration again.</summary>
        public void Reset()
        {
            _heldSeconds = 0f;
            _due = false;
        }

        /// <summary>
        /// Feeds one step. Returns true from the first step where the continuous hold has elapsed,
        /// and keeps returning true until <see cref="Reset"/> — the latch is what makes "exactly
        /// once" a property of the counter rather than of the caller's discipline. Any released
        /// step banks nothing: the count restarts from zero.
        /// </summary>
        public bool Tick(float deltaTime, bool held)
        {
            if (!_due)
            {
                if (held)
                    _heldSeconds += deltaTime;
                else
                    _heldSeconds = 0f;

                _due = _heldSeconds >= _holdSeconds;
            }

            return _due;
        }
    }
}
