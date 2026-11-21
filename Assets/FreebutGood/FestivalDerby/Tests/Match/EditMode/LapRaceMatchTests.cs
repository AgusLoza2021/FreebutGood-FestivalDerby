using NUnit.Framework;

namespace FestivalDerby.Match.Tests
{
    [TestFixture]
    public sealed class LapRaceMatchTests
    {
        private const float Loop = 1000f;

        // Drives every car to the given progress in steps small enough to look like driving.
        private static void Drive(LapRaceMatch match, float[] progress, int car, float from, float to, float step = 10f)
        {
            for (float p = from + step; p <= to + 0.001f && !match.IsFinished; p += step)
            {
                progress[car] = ((p % Loop) + Loop) % Loop;
                match.Tick(progress);
            }
        }

        [Test]
        public void ForwardDriving_BanksDistance_AcrossTheStartLine()
        {
            var match = new LapRaceMatch(2, 3, Loop);
            var progress = new[] { 950f, 0f };
            match.Tick(progress);

            Drive(match, progress, 0, 950f, 1050f);

            Assert.That(match.DistanceOf(0), Is.EqualTo(100f).Within(0.01f));
        }

        [Test]
        public void TheFirstCarToBankAllLaps_Wins()
        {
            var match = new LapRaceMatch(2, 2, Loop);
            var progress = new[] { 0f, 0f };
            match.Tick(progress);

            Drive(match, progress, 1, 0f, 1500f);
            Drive(match, progress, 0, 0f, 2000f);
            Drive(match, progress, 1, 1500f, 2000f);

            Assert.That(match.Winner, Is.EqualTo(0));
            Assert.That(match.OutcomeFor(0), Is.EqualTo(MatchOutcome.Won));
            Assert.That(match.OutcomeFor(1), Is.EqualTo(MatchOutcome.Lost));
        }

        [Test]
        public void ARaceInProgress_HasNoOutcomeYet()
        {
            var match = new LapRaceMatch(2, 3, Loop);
            var progress = new[] { 0f, 0f };
            match.Tick(progress);
            Drive(match, progress, 0, 0f, 500f);

            Assert.That(match.IsFinished, Is.False);
            Assert.That(match.OutcomeFor(0), Is.EqualTo(MatchOutcome.Ongoing));
        }

        [Test]
        public void LapOf_IsOneBased_AndCappedAtTheRaceLength()
        {
            var match = new LapRaceMatch(1, 3, Loop);
            var progress = new[] { 0f };
            match.Tick(progress);
            Assert.That(match.LapOf(0), Is.EqualTo(1));

            Drive(match, progress, 0, 0f, 1200f);
            Assert.That(match.LapOf(0), Is.EqualTo(2));
        }

        [Test]
        public void ATeleport_IsIgnored()
        {
            var match = new LapRaceMatch(1, 3, Loop);
            var progress = new[] { 100f };
            match.Tick(progress);

            progress[0] = 600f; // a respawn half a lap away
            match.Tick(progress);

            Assert.That(match.DistanceOf(0), Is.EqualTo(0f));
        }

        [Test]
        public void ReversingNeverGoesBelowZero_AndDrivingBackCostsDistance()
        {
            var match = new LapRaceMatch(1, 3, Loop);
            var progress = new[] { 100f };
            match.Tick(progress);

            progress[0] = 60f;
            match.Tick(progress);
            Assert.That(match.DistanceOf(0), Is.EqualTo(0f));

            progress[0] = 160f;
            match.Tick(progress);
            progress[0] = 130f;
            match.Tick(progress);
            Assert.That(match.DistanceOf(0), Is.EqualTo(70f).Within(0.01f));
        }

        [Test]
        public void Positions_FollowDistance_WithTiesToTheLowerIndex()
        {
            var match = new LapRaceMatch(3, 5, Loop);
            var progress = new[] { 0f, 0f, 0f };
            match.Tick(progress);
            Drive(match, progress, 2, 0f, 300f);
            Drive(match, progress, 1, 0f, 100f);

            Assert.That(match.PositionOf(2), Is.EqualTo(1));
            Assert.That(match.PositionOf(1), Is.EqualTo(2));
            Assert.That(match.PositionOf(0), Is.EqualTo(3));
        }

        [Test]
        public void Rebase_MovesTheAnchorWithoutBankingDistance()
        {
            var match = new LapRaceMatch(1, 3, Loop);
            var progress = new[] { 100f };
            match.Tick(progress);

            progress[0] = 150f;
            match.Rebase(progress);
            Assert.That(match.DistanceOf(0), Is.EqualTo(0f));

            progress[0] = 170f;
            match.Tick(progress);
            Assert.That(match.DistanceOf(0), Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void ABadProgressValue_IsIgnoredInsteadOfPoisoningTheDistance()
        {
            var match = new LapRaceMatch(1, 3, Loop);
            var progress = new[] { 100f };
            match.Tick(progress);

            progress[0] = float.NaN;
            match.Tick(progress);
            progress[0] = 120f;
            match.Tick(progress);

            Assert.That(match.DistanceOf(0), Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void AFinishedRace_IgnoresFurtherTicks_AndShortArraysAreSafe()
        {
            var match = new LapRaceMatch(2, 1, Loop);
            var progress = new[] { 0f, 0f };
            match.Tick(progress);
            Drive(match, progress, 0, 0f, 1000f);
            int winner = match.Winner;

            Drive(match, progress, 1, 0f, 1500f);

            Assert.That(match.Winner, Is.EqualTo(winner));
            Assert.DoesNotThrow(() => match.Tick(new float[1]));
            Assert.DoesNotThrow(() => match.Tick(null));
        }
    }
}
