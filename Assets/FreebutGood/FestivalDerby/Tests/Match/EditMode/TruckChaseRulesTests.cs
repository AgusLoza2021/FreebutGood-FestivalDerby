using NUnit.Framework;

namespace FestivalDerby.Match.Tests
{
    [TestFixture]
    public sealed class TruckChaseRulesTests
    {
        private const float Loop = 1000f;
        private const float Half = 4f;
        private const float Step = 0.1f;

        private static TruckChaseSettings Settings(float interval = 20f)
        {
            return new TruckChaseSettings(3f, interval, Half);
        }

        // ---- geometry

        [TestCase(100f, 110f, -10f)]
        [TestCase(110f, 100f, 10f)]
        [TestCase(995f, 5f, -10f)]
        [TestCase(5f, 995f, 10f)]
        public void RelativeProgress_IsSignedAndWrapsTheLoop(float car, float truck, float expected)
        {
            Assert.That(ChaseGeometry.RelativeProgress(car, truck, Loop), Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void DistanceToTail_IsZeroLevelWithTheTruck()
        {
            Assert.That(ChaseGeometry.DistanceToTail(502f, 500f, Loop, Half), Is.EqualTo(0f));
        }

        [Test]
        public void DistanceToTail_BehindIsTheGapToTheTail()
        {
            Assert.That(ChaseGeometry.DistanceToTail(480f, 500f, Loop, Half), Is.EqualTo(16f).Within(0.001f));
        }

        [Test]
        public void DistanceToTail_AfterOvertakingIsAlmostALap()
        {
            Assert.That(ChaseGeometry.DistanceToTail(520f, 500f, Loop, Half), Is.EqualTo(Loop - 24f).Within(0.001f));
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        public void BadLoopLength_NeverProducesNaN(float loop)
        {
            Assert.That(ChaseGeometry.RelativeProgress(10f, 5f, loop), Is.EqualTo(0f));
            Assert.That(ChaseGeometry.DistanceToTail(10f, 5f, loop, Half), Is.EqualTo(0f));
        }

        // ---- boarding

        [Test]
        public void OnBed_AccumulatesContinuousTime()
        {
            var state = BoardingState.Initial;
            state = BoardingRules.Advance(state, true, 0f, Half, 1f);
            state = BoardingRules.Advance(state, true, 0f, Half, 1.5f);

            Assert.That(state.SecondsOnBed, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(state.Armed, Is.True);
        }

        [Test]
        public void LeavingTheBed_ResetsTheTime()
        {
            var state = BoardingRules.Advance(BoardingState.Initial, true, 0f, Half, 2f);
            state = BoardingRules.Advance(state, false, -10f, Half, Step);

            Assert.That(state.SecondsOnBed, Is.EqualTo(0f));
        }

        [Test]
        public void PassingTheTruckFront_DisarmsAndTimeNoLongerCounts()
        {
            var state = BoardingRules.Advance(BoardingState.Initial, false, Half + 1f, Half, Step);
            Assert.That(state.Armed, Is.False);

            state = BoardingRules.Advance(state, true, 0f, Half, 5f);
            Assert.That(state.SecondsOnBed, Is.EqualTo(0f));
            Assert.That(state.Armed, Is.False);
        }

        [Test]
        public void ComingBackRoundBehindTheTail_RearmsBoarding()
        {
            var state = BoardingRules.Advance(BoardingState.Initial, false, Half + 1f, Half, Step);
            state = BoardingRules.Advance(state, false, -(Half + 1f), Half, Step);
            Assert.That(state.Armed, Is.True);

            state = BoardingRules.Advance(state, true, 0f, Half, 1f);
            Assert.That(state.SecondsOnBed, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void AlongsideTheTruck_KeepsWhateverStateItHad()
        {
            var disarmed = new BoardingState(0f, false);
            Assert.That(BoardingRules.Advance(disarmed, false, 0f, Half, Step).Armed, Is.False);
            Assert.That(BoardingRules.Advance(BoardingState.Initial, false, 0f, Half, Step).Armed, Is.True);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void BadDt_ChangesNothing(float dt)
        {
            var state = new BoardingState(1f, true);
            var next = BoardingRules.Advance(state, true, 0f, Half, dt);

            Assert.That(next.SecondsOnBed, Is.EqualTo(1f));
        }

        // ---- clock

        [Test]
        public void Clock_ExpiresOnceAndRestartsItself()
        {
            var clock = new EliminationClock(10f);
            Assert.That(clock.Tick(9.9f), Is.False);
            Assert.That(clock.Tick(0.2f), Is.True);
            Assert.That(clock.Remaining, Is.EqualTo(10f));
            Assert.That(clock.Tick(5f), Is.False);
        }

        [Test]
        public void Clock_ResetRestoresTheFullInterval()
        {
            var clock = new EliminationClock(10f);
            clock.Tick(7f);
            clock.Reset();

            Assert.That(clock.Remaining, Is.EqualTo(10f));
        }

        // ---- match

        private static void Run(TruckChaseMatch match, float seconds, float[] progress, bool[] onBed, float truck)
        {
            for (float t = 0f; t < seconds && !match.IsFinished; t += Step)
                match.Tick(Step, progress, onBed, truck, Loop);
        }

        [Test]
        public void FirstCarToStayThreeSecondsOnTheBed_Wins()
        {
            var match = new TruckChaseMatch(3, Settings());
            var progress = new[] { 500f, 480f, 470f };
            var onBed = new[] { false, true, false };

            Run(match, 2.5f, progress, onBed, 500f);
            Assert.That(match.IsFinished, Is.False);

            Run(match, 1f, progress, onBed, 500f);
            Assert.That(match.Winner, Is.EqualTo(1));
            Assert.That(match.OutcomeFor(1), Is.EqualTo(MatchOutcome.Won));
            Assert.That(match.OutcomeFor(0), Is.EqualTo(MatchOutcome.Lost));
        }

        [Test]
        public void TwoSecondsAndAFall_DoesNotWin()
        {
            var match = new TruckChaseMatch(2, Settings());
            var progress = new[] { 500f, 480f };
            var onBed = new[] { true, false };

            Run(match, 2f, progress, onBed, 500f);
            onBed[0] = false;
            progress[0] = 490f;
            Run(match, 0.5f, progress, onBed, 500f);
            onBed[0] = true;
            progress[0] = 500f;
            Run(match, 2f, progress, onBed, 500f);

            Assert.That(match.IsFinished, Is.False);
        }

        [Test]
        public void OvertakingTheTruck_BlocksTheWinUntilTheLoopBringsItBackBehind()
        {
            var match = new TruckChaseMatch(2, Settings(1000f));
            var progress = new[] { 520f, 400f }; // car 0 is already past the truck
            var onBed = new[] { false, false };
            match.Tick(Step, progress, onBed, 500f, Loop);
            Assert.That(match.IsArmed(0), Is.False);

            // It rides the bed by some accident: time does not count.
            onBed[0] = true;
            progress[0] = 500f;
            Run(match, 5f, progress, onBed, 500f);
            Assert.That(match.IsFinished, Is.False);

            // It goes round the lap and appears behind the tail again.
            onBed[0] = false;
            progress[0] = 470f;
            match.Tick(Step, progress, onBed, 500f, Loop);
            Assert.That(match.IsArmed(0), Is.True);

            onBed[0] = true;
            progress[0] = 500f;
            Run(match, 3.2f, progress, onBed, 500f);
            Assert.That(match.Winner, Is.EqualTo(0));
        }

        [Test]
        public void ClockExpiry_EliminatesTheCarFurthestFromTheTail_AndRestarts()
        {
            var match = new TruckChaseMatch(3, Settings(5f));
            var progress = new[] { 490f, 460f, 300f };
            var onBed = new[] { false, false, false };

            Run(match, 5.2f, progress, onBed, 500f);

            Assert.That(match.IsAlive(2), Is.False);
            Assert.That(match.LastEliminated, Is.EqualTo(2));
            Assert.That(match.EliminationCount, Is.EqualTo(1));
            Assert.That(match.ClockRemaining, Is.GreaterThan(4f));
        }

        [Test]
        public void ACarThatOvertookTheTruck_RanksLastAndGoesFirst()
        {
            var match = new TruckChaseMatch(3, Settings(5f));
            var progress = new[] { 490f, 460f, 520f };
            var onBed = new[] { false, false, false };

            Run(match, 5.2f, progress, onBed, 500f);

            Assert.That(match.LastEliminated, Is.EqualTo(2));
        }

        [Test]
        public void EliminatedPlayer_LosesImmediately()
        {
            var match = new TruckChaseMatch(3, Settings(5f));
            var progress = new[] { 100f, 460f, 480f }; // index 0 (the player) is last
            var onBed = new[] { false, false, false };

            Run(match, 5.2f, progress, onBed, 500f);

            Assert.That(match.OutcomeFor(0), Is.EqualTo(MatchOutcome.Lost));
            Assert.That(match.IsFinished, Is.False);
        }

        [Test]
        public void LastSurvivor_Wins()
        {
            var match = new TruckChaseMatch(2, Settings(5f));
            var progress = new[] { 490f, 300f };
            var onBed = new[] { false, false };

            Run(match, 5.2f, progress, onBed, 500f);

            Assert.That(match.Winner, Is.EqualTo(0));
            Assert.That(match.OutcomeFor(0), Is.EqualTo(MatchOutcome.Won));
        }

        [Test]
        public void AFinishedMatch_IgnoresFurtherTicks()
        {
            var match = new TruckChaseMatch(2, Settings(5f));
            var progress = new[] { 500f, 480f };
            var onBed = new[] { true, false };
            Run(match, 4f, progress, onBed, 500f);

            int winner = match.Winner;
            onBed[0] = false;
            onBed[1] = true;
            progress[1] = 500f;
            Run(match, 10f, progress, onBed, 500f);

            Assert.That(match.Winner, Is.EqualTo(winner));
        }

        [Test]
        public void ShortArrays_AreIgnoredInsteadOfThrowing()
        {
            var match = new TruckChaseMatch(3, Settings());

            Assert.DoesNotThrow(() => match.Tick(Step, new float[1], new bool[3], 0f, Loop));
            Assert.DoesNotThrow(() => match.Tick(Step, null, null, 0f, Loop));
        }
    }
}
