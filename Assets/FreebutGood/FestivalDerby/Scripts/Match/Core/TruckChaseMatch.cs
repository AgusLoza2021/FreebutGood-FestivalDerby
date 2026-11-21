namespace FestivalDerby.Match
{
    public enum MatchOutcome
    {
        Ongoing,
        Won,
        Lost
    }

    /// <summary>Tuning of one truck chase. Values come from a config asset, never from code.</summary>
    public readonly struct TruckChaseSettings
    {
        public readonly float RequiredSeconds;
        public readonly float EliminationInterval;
        public readonly float TruckHalfLength;

        public TruckChaseSettings(float requiredSeconds, float eliminationInterval, float truckHalfLength)
        {
            RequiredSeconds = requiredSeconds;
            EliminationInterval = eliminationInterval;
            TruckHalfLength = truckHalfLength;
        }
    }

    /// <summary>
    /// The rules of the truck chase for N cars (the player is just one index). The first car to stay on
    /// the bed for the required time wins; the elimination clock removes the car furthest from the
    /// truck tail whenever it runs out, and restarts after every elimination; the last survivor wins.
    /// Engine-free and allocation-free after construction: the caller owns the per-car arrays.
    /// </summary>
    public sealed class TruckChaseMatch
    {
        private readonly TruckChaseSettings _settings;
        private readonly BoardingState[] _boarding;
        private readonly bool[] _alive;
        private readonly EliminationClock _clock;
        private int _aliveCount;
        private int _winner = -1;
        private int _lastEliminated = -1;
        private int _eliminations;

        public TruckChaseMatch(int participants, in TruckChaseSettings settings)
        {
            int count = participants > 0 ? participants : 0;
            _settings = settings;
            _boarding = new BoardingState[count];
            _alive = new bool[count];
            for (int i = 0; i < count; i++)
            {
                _boarding[i] = BoardingState.Initial;
                _alive[i] = true;
            }

            _aliveCount = count;
            _clock = new EliminationClock(settings.EliminationInterval);
        }

        public int ParticipantCount
        {
            get { return _alive.Length; }
        }

        public int AliveCount
        {
            get { return _aliveCount; }
        }

        public bool IsFinished
        {
            get { return _winner >= 0; }
        }

        /// <summary>Index of the winner, or -1 while the match is still on.</summary>
        public int Winner
        {
            get { return _winner; }
        }

        public float ClockRemaining
        {
            get { return _clock.Remaining; }
        }

        /// <summary>The car removed by the most recent elimination, or -1 before the first one.</summary>
        public int LastEliminated
        {
            get { return _lastEliminated; }
        }

        /// <summary>Counts every elimination so a view can react to a new one without an event.</summary>
        public int EliminationCount
        {
            get { return _eliminations; }
        }

        public bool IsAlive(int index)
        {
            return index >= 0 && index < _alive.Length && _alive[index];
        }

        public float BoardSeconds(int index)
        {
            return index >= 0 && index < _boarding.Length ? _boarding[index].SecondsOnBed : 0f;
        }

        public bool IsArmed(int index)
        {
            return index >= 0 && index < _boarding.Length && _boarding[index].Armed;
        }

        public MatchOutcome OutcomeFor(int index)
        {
            if (index < 0 || index >= _alive.Length)
                return MatchOutcome.Ongoing;
            if (_winner == index)
                return MatchOutcome.Won;
            if (!_alive[index] || _winner >= 0)
                return MatchOutcome.Lost;
            return MatchOutcome.Ongoing;
        }

        /// <summary>
        /// One step. <paramref name="progress"/> is each car's racing-line distance and
        /// <paramref name="onBed"/> says whether it is riding the bed right now; both are indexed like the
        /// match's participants and read, never kept.
        /// </summary>
        public void Tick(float dt, float[] progress, bool[] onBed, float truckProgress, float loopLength)
        {
            if (IsFinished || progress == null || onBed == null
                || progress.Length < _alive.Length || onBed.Length < _alive.Length)
                return;

            AdvanceBoarding(dt, progress, onBed, truckProgress, loopLength);
            if (IsFinished)
                return;

            if (_aliveCount > 1 && _clock.Tick(dt))
                EliminateLast(progress, truckProgress, loopLength);
        }

        private void AdvanceBoarding(float dt, float[] progress, bool[] onBed, float truckProgress, float loopLength)
        {
            int best = -1;
            float bestSeconds = 0f;
            for (int i = 0; i < _alive.Length; i++)
            {
                if (!_alive[i])
                    continue;

                float relative = ChaseGeometry.RelativeProgress(progress[i], truckProgress, loopLength);
                _boarding[i] = BoardingRules.Advance(_boarding[i], onBed[i], relative, _settings.TruckHalfLength, dt);

                float seconds = _boarding[i].SecondsOnBed;
                if (seconds >= _settings.RequiredSeconds && seconds > bestSeconds)
                {
                    best = i;
                    bestSeconds = seconds;
                }
            }

            if (best >= 0)
                _winner = best;
        }

        private void EliminateLast(float[] progress, float truckProgress, float loopLength)
        {
            int last = -1;
            float worst = -1f;
            for (int i = 0; i < _alive.Length; i++)
            {
                if (!_alive[i])
                    continue;

                float distance = ChaseGeometry.DistanceToTail(progress[i], truckProgress, loopLength,
                    _settings.TruckHalfLength);
                if (distance > worst)
                {
                    worst = distance;
                    last = i;
                }
            }

            if (last < 0)
                return;

            _alive[last] = false;
            _boarding[last] = BoardingState.Initial;
            _aliveCount--;
            _lastEliminated = last;
            _eliminations++;
            _clock.Reset();

            if (_aliveCount == 1)
                for (int i = 0; i < _alive.Length; i++)
                    if (_alive[i])
                        _winner = i;
        }
    }
}
