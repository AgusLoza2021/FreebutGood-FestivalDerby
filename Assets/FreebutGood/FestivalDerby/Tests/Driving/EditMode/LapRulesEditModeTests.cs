using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// H1 behavior contract: run EditMode restricted to FestivalDerby.Driving.EditModeTests.
    /// These tests intentionally precede implementation; the default stub is not a deliverable.
    /// The cases follow the frozen contract: ordered checkpoints, a skipped gate invalidating a
    /// lap instead of granting it, and a failed configuration or bad index granting nothing.
    /// Race length is an input here, never a constant: the GDD leaves it open, so one case varies
    /// it precisely so that a hardcoded race length cannot pass this fixture.
    /// </summary>
    [TestFixture]
    public sealed class LapRulesEditModeTests
    {
        private const int Checkpoints = 3;
        private const int Laps = 2;

        private static LapState Fresh()
        {
            return new LapState(0, 0, false);
        }

        [Test]
        public void FirstCheckpoint_AdvancesTheCursorWithoutCompletingALap()
        {
            var decision = LapRules.Advance(Fresh(), 0, Checkpoints, Laps);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.CheckpointPassed));
            Assert.That(decision.State.NextCheckpoint, Is.EqualTo(1));
            Assert.That(decision.State.CompletedLaps, Is.Zero);
            Assert.That(decision.State.LapInvalid, Is.False);
        }

        [Test]
        public void FullOrderedPass_CompletesExactlyOneLap()
        {
            var state = Fresh();

            var first = LapRules.Advance(state, 0, Checkpoints, Laps);
            var second = LapRules.Advance(first.State, 1, Checkpoints, Laps);
            var third = LapRules.Advance(second.State, 2, Checkpoints, Laps);
            var gate = LapRules.Advance(third.State, Checkpoints, Checkpoints, Laps);

            Assert.That(first.Event, Is.EqualTo(LapEvent.CheckpointPassed));
            Assert.That(second.Event, Is.EqualTo(LapEvent.CheckpointPassed));
            Assert.That(third.Event, Is.EqualTo(LapEvent.CheckpointPassed));
            Assert.That(third.State.NextCheckpoint, Is.EqualTo(Checkpoints));
            Assert.That(gate.Event, Is.EqualTo(LapEvent.LapCompleted));
            Assert.That(gate.State.CompletedLaps, Is.EqualTo(1));
            Assert.That(gate.State.NextCheckpoint, Is.Zero);
            Assert.That(gate.State.LapInvalid, Is.False);
        }

        [Test]
        public void SkippedCheckpoint_InvalidatesWithoutAdvancing()
        {
            var decision = LapRules.Advance(Fresh(), 2, Checkpoints, Laps);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.None));
            Assert.That(decision.State.LapInvalid, Is.True);
            Assert.That(decision.State.NextCheckpoint, Is.Zero);
            Assert.That(decision.State.CompletedLaps, Is.Zero);
        }

        [Test]
        public void ReTouchedCheckpoint_IsIgnored()
        {
            var advanced = LapRules.Advance(Fresh(), 0, Checkpoints, Laps);

            var decision = LapRules.Advance(advanced.State, 0, Checkpoints, Laps);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.None));
            Assert.That(decision.State.NextCheckpoint, Is.EqualTo(1));
            Assert.That(decision.State.CompletedLaps, Is.Zero);
            Assert.That(decision.State.LapInvalid, Is.False);
        }

        [Test]
        public void IncompleteLapAtTheGate_GrantsNothing()
        {
            var decision = LapRules.Advance(Fresh(), Checkpoints, Checkpoints, Laps);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.None));
            Assert.That(decision.State.CompletedLaps, Is.Zero);
            Assert.That(decision.State.NextCheckpoint, Is.Zero);
            Assert.That(decision.State.LapInvalid, Is.False);
        }

        [Test]
        public void InvalidatedLapAtTheGate_IsDiscardedInsteadOfGranted()
        {
            var skipped = LapRules.Advance(Fresh(), 1, Checkpoints, Laps);

            var decision = LapRules.Advance(skipped.State, Checkpoints, Checkpoints, Laps);

            Assert.That(skipped.State.LapInvalid, Is.True);
            Assert.That(decision.Event, Is.EqualTo(LapEvent.LapInvalidated));
            Assert.That(decision.State.CompletedLaps, Is.Zero);
            Assert.That(decision.State.NextCheckpoint, Is.Zero);
            Assert.That(decision.State.LapInvalid, Is.False);
        }

        [Test]
        public void FinalValidLap_RaisesRaceCompleted()
        {
            var state = new LapState(Checkpoints, 1, false);

            var decision = LapRules.Advance(state, Checkpoints, Checkpoints, 2);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.RaceCompleted));
            Assert.That(decision.State.CompletedLaps, Is.EqualTo(2));
        }

        [Test]
        public void LongerRace_DoesNotFinishOnTheSecondLap()
        {
            var state = new LapState(Checkpoints, 1, false);

            var decision = LapRules.Advance(state, Checkpoints, Checkpoints, 3);

            Assert.That(decision.Event, Is.EqualTo(LapEvent.LapCompleted));
            Assert.That(decision.State.CompletedLaps, Is.EqualTo(2));
            Assert.That(decision.State.NextCheckpoint, Is.Zero);
            Assert.That(decision.State.LapInvalid, Is.False);
        }

        [Test]
        public void InvalidConfiguration_GrantsNothing()
        {
            var state = Fresh();

            var noCheckpoints = LapRules.Advance(state, 0, 0, Laps);
            var noLaps = LapRules.Advance(state, 0, Checkpoints, 0);

            Assert.That(noCheckpoints.Event, Is.EqualTo(LapEvent.None));
            Assert.That(noCheckpoints.State.NextCheckpoint, Is.Zero);
            Assert.That(noCheckpoints.State.CompletedLaps, Is.Zero);
            Assert.That(noLaps.Event, Is.EqualTo(LapEvent.None));
            Assert.That(noLaps.State.NextCheckpoint, Is.Zero);
            Assert.That(noLaps.State.CompletedLaps, Is.Zero);
        }

        [Test]
        public void IndexOutsideTheCircuit_GrantsNothing()
        {
            var state = new LapState(1, 1, false);

            var below = LapRules.Advance(state, -1, Checkpoints, Laps);
            var above = LapRules.Advance(state, Checkpoints + 1, Checkpoints, Laps);

            Assert.That(below.Event, Is.EqualTo(LapEvent.None));
            Assert.That(below.State.NextCheckpoint, Is.EqualTo(1));
            Assert.That(below.State.CompletedLaps, Is.EqualTo(1));
            Assert.That(above.Event, Is.EqualTo(LapEvent.None));
            Assert.That(above.State.NextCheckpoint, Is.EqualTo(1));
            Assert.That(above.State.CompletedLaps, Is.EqualTo(1));
        }
    }
}
