using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// The PF.2 stuck-respawn contract: the clock fires once after exactly <c>StuckSeconds</c> of
    /// continuous "pedal asking for motion, car not moving", any real movement or any release of
    /// the demand restarts the count, and an explicitly disarmed clock cannot fire again until it
    /// is re-armed. Deterministic dt values (multiples that divide the thresholds exactly) keep the
    /// boundary assertions float-exact.
    /// </summary>
    [TestFixture]
    public class StuckRespawnClockTests
    {
        // 0.25 divides 5 exactly and is exact in binary floating point, so the threshold tick is
        // reached on the 20th step with no drift.
        private const float Dt = 0.25f;
        private const float StuckSeconds = 5f;
        private const float SpeedLimit = 0.5f;

        // StuckSeconds / Dt, kept as an integer so the boundary call sites stay exact and readable.
        private const int StuckTicks = 20;

        // The harness runner constructs a fresh fixture per test and never runs [SetUp], so each
        // test builds its own clock inline.
        private static StuckRespawnClock NewClock()
        {
            return new StuckRespawnClock(StuckSeconds, SpeedLimit);
        }

        [Test]
        public void DemandAndImmobility_FiresExactlyAtStuckSeconds()
        {
            StuckRespawnClock clock = NewClock();
            for (int i = 0; i < StuckTicks - 1; i++)
            {
                Assert.That(clock.Update(Dt, 0f, true), Is.False,
                    "the clock must not fire before the full stuck window has elapsed");
            }

            // The 20th quarter-second is exactly 5 s of held demand-and-immobility.
            Assert.That(clock.Update(Dt, 0f, true), Is.True);
        }

        [Test]
        public void OnceDue_KeepsReportingDue_UntilReset()
        {
            StuckRespawnClock clock = NewClock();
            RunStuck(clock);

            Assert.That(clock.Update(Dt, 0f, true), Is.True);
            Assert.That(clock.Update(Dt, 0f, true), Is.True,
                "a due clock must latch so a caller that is slow to reset cannot miss the respawn");
        }

        [Test]
        public void RealMovement_RestartsTheCount()
        {
            StuckRespawnClock clock = NewClock();
            // Three quarters of the window banked...
            RunStuck(clock, 3);
            // ...one tick of real movement clears it...
            Assert.That(clock.Update(Dt, SpeedLimit, true), Is.False);
            Assert.That(clock.Update(Dt, SpeedLimit * 2f, true), Is.False,
                "any speed at or above the limit is real movement, however slow");
            // ...and the whole window must be earned again: one tick short is not enough.
            Assert.That(RunStuck(clock, StuckTicks - 1), Is.False,
                "after movement the clock must need the full stuck window again");
            Assert.That(clock.Update(Dt, 0f, true), Is.True);
        }

        [Test]
        public void DemandRelease_RestartsTheCount()
        {
            StuckRespawnClock clock = NewClock();
            RunStuck(clock, 3);

            Assert.That(clock.Update(Dt, 0f, false), Is.False);

            Assert.That(RunStuck(clock, StuckTicks - 1), Is.False,
                "after the pedal is released the clock must need the full stuck window again");
            Assert.That(clock.Update(Dt, 0f, true), Is.True);
        }

        [Test]
        public void NoThrottleDemand_NeverFires()
        {
            StuckRespawnClock clock = NewClock();
            for (int i = 0; i < 100; i++)
            {
                Assert.That(clock.Update(Dt, 0f, false), Is.False);
            }
        }

        [Test]
        public void SpeedAtTheLimit_IsNotStuck()
        {
            StuckRespawnClock clock = NewClock();
            // The limit is exclusive: at exactly the limit the car is moving and the clock must
            // not count the tick, so the boundary cannot fire on a rolling stop.
            for (int i = 0; i < StuckTicks + 10; i++)
            {
                Assert.That(clock.Update(Dt, SpeedLimit, true), Is.False);
            }
        }

        [Test]
        public void Reset_ClearsTheCountAndRearms()
        {
            StuckRespawnClock clock = NewClock();
            Assert.That(RunStuck(clock), Is.True);

            clock.Reset();

            Assert.That(clock.Update(Dt, 0f, true), Is.False,
                "a reset clock must start counting from zero again");
            Assert.That(RunStuck(clock, StuckTicks - 1), Is.True);
        }

        [Test]
        public void DisarmedClock_CannotFireUntilRearmed()
        {
            StuckRespawnClock clock = NewClock();
            Assert.That(RunStuck(clock), Is.True);

            // The wiring keeps the clock disarmed for the whole ghost window.
            clock.SetArmed(false);
            for (int i = 0; i < StuckTicks + 10; i++)
            {
                Assert.That(clock.Update(Dt, 0f, true), Is.False,
                    "a disarmed clock must never report the respawn due");
            }

            clock.SetArmed(true);
            Assert.That(clock.Update(Dt, 0f, true), Is.False,
                "re-arming must restart from a clean count, not inherit the stale one");
            Assert.That(RunStuck(clock, StuckTicks - 1), Is.True);
        }

        [Test]
        public void DisarmThenRearm_ClearsABankedCount()
        {
            StuckRespawnClock clock = NewClock();
            RunStuck(clock, 3);

            clock.SetArmed(false);
            clock.SetArmed(true);

            Assert.That(RunStuck(clock, StuckTicks - 1), Is.False,
                "a banked count must not survive a disarm/re-arm cycle");
            Assert.That(clock.Update(Dt, 0f, true), Is.True);
        }

        /// <summary>Ticks <paramref name="ticks"/> of held demand and immobility, returning the last due report.</summary>
        private bool RunStuck(StuckRespawnClock clock, int ticks)
        {
            bool due = false;
            for (int i = 0; i < ticks; i++)
            {
                due = clock.Update(Dt, 0f, true);
            }

            return due;
        }

        /// <summary>Runs the full window, asserting the clock fires on the last tick.</summary>
        private bool RunStuck(StuckRespawnClock clock)
        {
            bool due = RunStuck(clock, StuckTicks);
            Assert.That(due, Is.True);
            return due;
        }

        /// <summary>
        /// Catch playtest 1: on the carrier the speed is carrier-relative, so a car riding the bed at full
        /// pedal is "stuck" by the plain rule. The pause holds the count: it neither banks nor clears.
        /// </summary>
        [Test]
        public void Paused_NeitherBanksNorClears()
        {
            StuckRespawnClock clock = NewClock();
            for (int i = 0; i < StuckTicks - 1; i++)
                clock.Update(Dt, 0f, true);

            for (int i = 0; i < StuckTicks * 4; i++)
            {
                Assert.That(clock.Update(Dt, 0f, true, true), Is.False,
                    "a paused clock must never fire, however long the car rides the carrier");
            }

            Assert.That(clock.Update(Dt, 0f, true), Is.True,
                "the paused count was kept: one more stuck tick completes the window");
        }
    }

    /// <summary>
    /// Catch playtest 1 flip recovery: the drive cutoff, the upside-down test, the computed self-right
    /// floor and the guaranteed upside-down respawn clock.
    /// </summary>
    [TestFixture]
    public class FlipRecoveryRulesTests
    {
        [Test]
        public void Drive_IsCutPastFiftyDegrees()
        {
            Assert.That(FlipRecoveryRules.DriveAllowed(0f), Is.True);
            Assert.That(FlipRecoveryRules.DriveAllowed(49.9f), Is.True);
            Assert.That(FlipRecoveryRules.DriveAllowed(50.1f), Is.False);
            Assert.That(FlipRecoveryRules.DriveAllowed(180f), Is.False);
            Assert.That(FlipRecoveryRules.DriveAllowed(float.NaN), Is.False, "a bad pose must not be driven");
        }

        [Test]
        public void UpsideDown_IsPastNinetyDegrees()
        {
            Assert.That(FlipRecoveryRules.IsUpsideDown(90f), Is.False);
            Assert.That(FlipRecoveryRules.IsUpsideDown(91f), Is.True);
            Assert.That(FlipRecoveryRules.IsUpsideDown(float.NaN), Is.False);
        }

        [Test]
        public void SelfRight_ClearsTheRoofTipMomentOfThePlaytestCar()
        {
            // The playtest car: 1100 kg, 30 m/s² gravity, 0.8 m half width -> a 26.4 kN·m roof moment that
            // the configured 23 kN·m could never lift.
            float tipMoment = 1100f * 30f * 0.8f;
            float torque = FlipRecoveryRules.SelfRightTorque(23000f, 1100f, -30f, 0.8f);

            Assert.That(torque, Is.GreaterThanOrEqualTo(1.5f * tipMoment - 1f));
        }

        [Test]
        public void SelfRight_KeepsAStrongerConfiguredTorque_AndSurvivesBadInputs()
        {
            Assert.That(FlipRecoveryRules.SelfRightTorque(90000f, 1100f, 30f, 0.8f), Is.EqualTo(90000f));
            Assert.That(FlipRecoveryRules.SelfRightTorque(23000f, float.NaN, 30f, 0.8f), Is.EqualTo(23000f));
            Assert.That(FlipRecoveryRules.SelfRightTorque(23000f, 1100f, 0f, 0.8f), Is.EqualTo(23000f));
        }

        [Test]
        public void SelfRight_ScalesWithMassAndGravity()
        {
            float light = FlipRecoveryRules.SelfRightTorque(0f, 1000f, 30f, 0.8f);
            float heavy = FlipRecoveryRules.SelfRightTorque(0f, 2000f, 30f, 0.8f);
            float strongGravity = FlipRecoveryRules.SelfRightTorque(0f, 1000f, 60f, 0.8f);

            Assert.That(heavy, Is.EqualTo(light * 2f).Within(1f));
            Assert.That(strongGravity, Is.EqualTo(light * 2f).Within(1f));
        }

        [Test]
        public void UpsideDownClock_FiresAfterThreeSecondsWhateverTheCarDoes()
        {
            var clock = new UpsideDownRespawnClock();
            // 0.25 s steps are exact, so 11 steps are 2.75 s and the 12th is exactly 3.
            for (int i = 0; i < 11; i++)
                Assert.That(clock.Update(0.25f, 170f), Is.False);

            Assert.That(clock.Update(0.25f, 170f), Is.True);
            Assert.That(clock.Update(0.25f, 0f), Is.True, "the latch holds until the respawn resets it");

            clock.Reset();
            Assert.That(clock.Update(0.25f, 170f), Is.False);
        }

        [Test]
        public void UpsideDownClock_RightingRestartsTheWindow()
        {
            var clock = new UpsideDownRespawnClock();
            for (int i = 0; i < 11; i++)
                clock.Update(0.25f, 170f);

            Assert.That(clock.Update(0.25f, 60f), Is.False, "on its side is not on its roof");
            Assert.That(clock.HeldSeconds, Is.EqualTo(0f));
            for (int i = 0; i < 11; i++)
                Assert.That(clock.Update(0.25f, 170f), Is.False);
        }
    }
}
