namespace FestivalDerby.Driving
{
    /// <summary>One lap-progress transition; None means the touch changed nothing.</summary>
    public enum LapEvent
    {
        None,
        CheckpointPassed,
        LapCompleted,
        LapInvalidated,
        RaceCompleted
    }

    /// <summary>
    /// Lap progress for one participant. NextCheckpoint is the checkpoint the car must touch next,
    /// and reaching checkpointCount opens the gate. LapInvalid marks a lap that skipped a gate.
    /// </summary>
    public readonly struct LapState
    {
        public readonly int NextCheckpoint;
        public readonly int CompletedLaps;
        public readonly bool LapInvalid;

        public LapState(int nextCheckpoint, int completedLaps, bool lapInvalid)
        {
            NextCheckpoint = nextCheckpoint;
            CompletedLaps = completedLaps;
            LapInvalid = lapInvalid;
        }
    }

    public readonly struct LapDecision
    {
        public readonly LapEvent Event;
        public readonly LapState State;

        public LapDecision(LapEvent lapEvent, LapState state)
        {
            Event = lapEvent;
            State = state;
        }
    }

    /// <summary>Pure lap sequencing. No Unity dependency, no state of its own, no allocation.</summary>
    public static class LapRules
    {
        /// <summary>
        /// checkpointIndex in 0..checkpointCount, where checkpointCount is the gate itself.
        /// A gate skipped out of order marks the lap invalid instead of granting it, and a lap
        /// that is invalid when it reaches the gate is discarded, never completed. A touch behind
        /// the cursor is ignored, because re-crossing a gate you already passed is not skipping it.
        /// An incomplete lap at the gate grants nothing yet. Bad configuration and out-of-range
        /// indexes grant nothing and change nothing: a wrong number must never finish a race.
        /// lapCount is the caller's configuration, never a constant.
        /// </summary>
        public static LapDecision Advance(
            in LapState state, int checkpointIndex, int checkpointCount, int lapCount)
        {
            if (checkpointCount < 1 || lapCount < 1)
                return new LapDecision(LapEvent.None, state);

            if (checkpointIndex < 0 || checkpointIndex > checkpointCount)
                return new LapDecision(LapEvent.None, state);

            if (checkpointIndex == checkpointCount)
            {
                // A lap already marked invalid is discarded here, whether or not its sequence ended.
                if (state.LapInvalid)
                    return new LapDecision(LapEvent.LapInvalidated, new LapState(0, state.CompletedLaps, false));

                if (state.NextCheckpoint < checkpointCount)
                    return new LapDecision(LapEvent.None, state);

                var completed = state.CompletedLaps + 1;
                if (completed >= lapCount)
                    return new LapDecision(LapEvent.RaceCompleted, new LapState(0, completed, false));

                return new LapDecision(LapEvent.LapCompleted, new LapState(0, completed, false));
            }

            if (checkpointIndex < state.NextCheckpoint)
                return new LapDecision(LapEvent.None, state);

            if (checkpointIndex > state.NextCheckpoint)
            {
                // Skipping does not move the cursor; it only marks the lap that is being run.
                if (state.LapInvalid)
                    return new LapDecision(LapEvent.None, state);

                return new LapDecision(
                    LapEvent.None, new LapState(state.NextCheckpoint, state.CompletedLaps, true));
            }

            return new LapDecision(
                LapEvent.CheckpointPassed,
                new LapState(checkpointIndex + 1, state.CompletedLaps, state.LapInvalid));
        }
    }
}
