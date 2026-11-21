namespace FestivalDerby.Match
{
    /// <summary>
    /// A classic lap race for N cars (the player is just one index). Each car banks the distance it
    /// drives forward along the racing line; the first to bank <c>laps * loop length</c> wins. Distance is
    /// measured, not gated by checkpoints, so a car that cuts a corner simply banks less, and a teleport
    /// (a respawn, a bad sample) is ignored instead of granting or costing a lap.
    /// Engine-free and allocation-free after construction.
    /// </summary>
    public sealed class LapRaceMatch
    {
        private readonly int _laps;
        private readonly float _loopLength;
        private readonly float[] _distance;
        private readonly float[] _last;
        private readonly bool[] _hasLast;
        private int _winner = -1;

        public LapRaceMatch(int participants, int laps, float loopLength)
        {
            int count = participants > 0 ? participants : 0;
            _laps = laps > 0 ? laps : 1;
            _loopLength = loopLength > 0f ? loopLength : 1f;
            _distance = new float[count];
            _last = new float[count];
            _hasLast = new bool[count];
        }

        public int Laps
        {
            get { return _laps; }
        }

        public int ParticipantCount
        {
            get { return _distance.Length; }
        }

        public bool IsFinished
        {
            get { return _winner >= 0; }
        }

        public int Winner
        {
            get { return _winner; }
        }

        /// <summary>Metres banked by a car, 0 for an index out of range.</summary>
        public float DistanceOf(int index)
        {
            return index >= 0 && index < _distance.Length ? _distance[index] : 0f;
        }

        /// <summary>The lap a car is on, 1-based, capped at the race length.</summary>
        public int LapOf(int index)
        {
            int lap = (int)(DistanceOf(index) / _loopLength) + 1;
            return lap > _laps ? _laps : lap;
        }

        /// <summary>1 for the leader. Ties go to the lower index, so the order is always total.</summary>
        public int PositionOf(int index)
        {
            if (index < 0 || index >= _distance.Length)
                return 0;

            int position = 1;
            for (int i = 0; i < _distance.Length; i++)
                if (_distance[i] > _distance[index] || (_distance[i] == _distance[index] && i < index))
                    position++;
            return position;
        }

        public MatchOutcome OutcomeFor(int index)
        {
            if (index < 0 || index >= _distance.Length || _winner < 0)
                return MatchOutcome.Ongoing;
            return _winner == index ? MatchOutcome.Won : MatchOutcome.Lost;
        }

        /// <summary>
        /// Re-anchors every car to where it is now without banking anything: used while the grid is held, so
        /// rolling forward before the start earns no distance.
        /// </summary>
        public void Rebase(float[] progress)
        {
            if (IsFinished || progress == null || progress.Length < _distance.Length)
                return;

            for (int i = 0; i < _distance.Length; i++)
            {
                if (float.IsNaN(progress[i]) || float.IsInfinity(progress[i]))
                    continue;

                _last[i] = progress[i];
                _hasLast[i] = true;
            }
        }

        /// <summary>One step. <paramref name="progress"/> is each car's racing-line distance.</summary>
        public void Tick(float[] progress)
        {
            if (IsFinished || progress == null || progress.Length < _distance.Length)
                return;

            float target = _laps * _loopLength;
            int best = -1;
            float bestDistance = 0f;
            for (int i = 0; i < _distance.Length; i++)
            {
                Bank(i, progress[i]);
                if (_distance[i] >= target && _distance[i] > bestDistance)
                {
                    best = i;
                    bestDistance = _distance[i];
                }
            }

            if (best >= 0)
                _winner = best;
        }

        private void Bank(int index, float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress))
                return;

            if (!_hasLast[index])
            {
                _hasLast[index] = true;
                _last[index] = progress;
                return;
            }

            float delta = ChaseGeometry.RelativeProgress(progress, _last[index], _loopLength);
            _last[index] = progress;

            // A jump of a quarter lap in one step is a respawn or a bad sample, not driving.
            if (delta > _loopLength * 0.25f || delta < -_loopLength * 0.25f)
                return;

            float next = _distance[index] + delta;
            _distance[index] = next < 0f ? 0f : next;
        }
    }
}
