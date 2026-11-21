using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// The hold counter's contract: exactly <c>HoldSeconds</c> of continuous hold fires exactly
    /// once and stays latched until <see cref="HoldProgress.Reset"/>, any released step restarts
    /// the count, and a reset rearms. Deterministic dt values (multiples that divide the threshold
    /// exactly) keep the boundary assertions float-exact.
    /// </summary>
    [TestFixture]
    public class HoldProgressTests
    {
        // 0.25 divides 3 exactly and is exact in binary floating point, so the threshold tick is
        // reached on the 12th step with no drift.
        private const float Dt = 0.25f;
        private const float HoldSeconds = 3f;

        // HoldSeconds / Dt, kept as an integer so the boundary call sites stay exact and readable.
        private const int HoldTicks = 12;

        // The harness runner constructs a fresh fixture per test and never runs [SetUp], so each
        // test builds its own counter inline.
        private static HoldProgress NewHold()
        {
            return new HoldProgress(HoldSeconds);
        }

        [Test]
        public void ContinuousHold_FiresExactlyAtHoldSeconds()
        {
            HoldProgress hold = NewHold();
            for (int i = 0; i < HoldTicks - 1; i++)
            {
                Assert.That(hold.Tick(Dt, true), Is.False,
                    "the hold must not fire before the full duration has elapsed");
            }

            // The 12th quarter-second is exactly 3 s of held finger.
            Assert.That(hold.Tick(Dt, true), Is.True);
        }

        [Test]
        public void OneTickShort_DoesNotFire()
        {
            HoldProgress hold = NewHold();
            for (int i = 0; i < HoldTicks - 1; i++)
            {
                Assert.That(hold.Tick(Dt, true), Is.False);
            }

            Assert.That(hold.Progress01, Is.LessThan(1f),
                "one tick short of the duration must not read as complete");
            Assert.That(hold.Tick(Dt, false), Is.False,
                "releasing on the tick before the threshold must not fire");
        }

        [Test]
        public void Release_RestartsTheCount()
        {
            HoldProgress hold = NewHold();
            RunHeld(hold, 5);

            // One released step clears the banked time...
            Assert.That(hold.Tick(Dt, false), Is.False);
            Assert.That(hold.Progress01, Is.EqualTo(0f),
                "a released hold must report an empty bar, not a partial one");

            // ...and the whole duration must be earned again: one tick short is not enough.
            Assert.That(RunHeld(hold, HoldTicks - 1), Is.False,
                "after a release the hold must need the full duration again");
            Assert.That(hold.Tick(Dt, true), Is.True);
        }

        [Test]
        public void OnceDue_KeepsReportingDue_UntilReset()
        {
            HoldProgress hold = NewHold();
            Assert.That(RunHeld(hold, HoldTicks), Is.True);

            Assert.That(hold.Tick(Dt, false), Is.True,
                "a due hold must latch so a caller that is slow to react cannot miss the fire");
            Assert.That(hold.Tick(Dt, true), Is.True);
        }

        [Test]
        public void FiresExactlyOneTransition_UntilReset()
        {
            HoldProgress hold = NewHold();
            int transitions = 0;
            bool previous = false;
            // Two released ticks, then a long enough held run to fire mid-stream, then released
            // steps again: the latch must hold and no second false-to-true transition may appear
            // before a Reset.
            for (int i = 0; i < HoldTicks + 10; i++)
            {
                bool due = hold.Tick(Dt, i >= 2 && i < HoldTicks + 6);
                if (due && !previous)
                    transitions++;
                previous = due;
            }

            Assert.That(transitions, Is.EqualTo(1),
                "the counter must fire exactly once per window: the latch owns the one-shot");

            hold.Reset();
            Assert.That(hold.Tick(Dt, true), Is.False,
                "after the reset the next window starts unfired");
        }

        [Test]
        public void Reset_RearmsTheCount()
        {
            HoldProgress hold = NewHold();
            Assert.That(hold.Tick(HoldSeconds, true), Is.True);

            hold.Reset();

            Assert.That(hold.Progress01, Is.EqualTo(0f));
            Assert.That(hold.Tick(Dt, true), Is.False,
                "a reset hold must start counting from zero again");
            Assert.That(RunHeld(hold, HoldTicks - 1), Is.True);
        }

        [Test]
        public void ZeroDuration_FiresAsSoonAsItIsHeld()
        {
            HoldProgress hold = new HoldProgress(0f);
            Assert.That(hold.Tick(Dt, true), Is.True,
                "a zero-second hold is an always-fire, never a stuck bar");
            Assert.That(hold.Progress01, Is.EqualTo(1f));
        }

        /// <summary>Ticks <paramref name="ticks"/> of a held finger, returning the last due report.</summary>
        private static bool RunHeld(HoldProgress hold, int ticks)
        {
            bool due = false;
            for (int i = 0; i < ticks; i++)
            {
                due = hold.Tick(Dt, true);
            }

            return due;
        }
    }
}
