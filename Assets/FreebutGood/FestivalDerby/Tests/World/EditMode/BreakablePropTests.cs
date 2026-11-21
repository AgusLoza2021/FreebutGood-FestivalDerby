using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contract for the server-authoritative breakable light props (GDD: destruction of light props
    /// only, routes stay stable). It pins the parts that must not drift: the authored opt-in defaults
    /// off, a prop breaks once above its threshold, a replay is a no-op, a broken prop stops colliding,
    /// the grid finds props across cell borders and the whole tick allocates nothing.
    /// </summary>
    [TestFixture]
    public sealed class BreakablePropTests
    {
        private const float CarRadius = 0.5f;

        // ---------------------------------------------------------------- authored data

        [Test]
        public void AuthoredEdgeProp_DefaultsToNotBreakable()
        {
            var prop = default(AuthoredEdgeProp);

            // Adding the flag must not have changed any map authored before it existed.
            Assert.That(prop.Breakable, Is.False);
        }

        [Test]
        public void Build_KeepsOnlyFlaggedPropsInAuthoredOrder()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set[0].Id, Is.Zero);
            Assert.That(set[0].Kind, Is.EqualTo(BreakablePropKind.Cone));
            Assert.That(set[0].X, Is.EqualTo(1f));
            Assert.That(set[1].Id, Is.EqualTo(1));
            Assert.That(set[1].Kind, Is.EqualTo(BreakablePropKind.Box));
            Assert.That(set[1].X, Is.EqualTo(3f));
        }

        [Test]
        public void Build_FreshProps_AreIntact()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);

            Assert.That(set[0].Broken, Is.False);
            Assert.That(set[0].BrokenTick, Is.EqualTo(BreakablePropSet.IntactTick));
            Assert.That(set[0].Blocks, Is.True);
        }

        [Test]
        public void Build_MoreBreakablePropsThanCapacity_Throws()
        {
            Assert.Throws<ArgumentException>(() => BreakablePropSet.Build(Sample(), 1));
        }

        [Test]
        public void SpecTable_ThresholdsRiseFromConeToSign()
        {
            // Documents the intended feel: a cone gives way to a nudge, a sign needs a real hit.
            Assert.That(BreakablePropSpecTable.Cone.BreakSpeed, Is.LessThan(BreakablePropSpecTable.Box.BreakSpeed));
            Assert.That(BreakablePropSpecTable.Box.BreakSpeed, Is.LessThan(BreakablePropSpecTable.Sign.BreakSpeed));
        }

        [Test]
        public void SpecTable_SpeedPenaltyIsOffByDefault()
        {
            Assert.That(BreakablePropSpecTable.Cone.HasSpeedPenalty, Is.False);
            Assert.That(BreakablePropSpecTable.Cone.SpeedMultiplier, Is.EqualTo(1f));
            Assert.That(BreakablePropSpecTable.Box.HasSpeedPenalty, Is.False);
            Assert.That(BreakablePropSpecTable.Sign.HasSpeedPenalty, Is.False);
        }

        // ---------------------------------------------------------------- the break rule

        [Test]
        public void Step_BelowThreshold_DoesNotBreak()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] cars = { Car(0.2f, 0f, 2.9f, 0f) };

            int broken = rules.Step(10, cars);

            Assert.That(broken, Is.Zero);
            Assert.That(set[0].Broken, Is.False);
        }

        [Test]
        public void Step_AtThreshold_BreaksAndReportsTheEvent()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] cars = { Car(0.2f, 0f, 3f, 0f) };

            int broken = rules.Step(10, cars);

            Assert.That(broken, Is.EqualTo(1));
            Assert.That(set[0].Broken, Is.True);
            Assert.That(set[0].BrokenTick, Is.EqualTo(10));

            PropBrokenEvent evt = rules.Event(0);
            Assert.That(evt.PropId, Is.Zero);
            Assert.That(evt.Tick, Is.EqualTo(10));
            Assert.That(evt.CarIndex, Is.Zero);
            Assert.That(evt.ImpulseX, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(evt.ImpulseZ, Is.Zero.Within(1e-4f));
        }

        [Test]
        public void Step_MovingAway_DoesNotBreak()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            // Same speed, opposite direction: the component along the contact normal is negative.
            BreakableCarSample[] cars = { Car(0.2f, 0f, -30f, 0f) };

            Assert.That(rules.Step(10, cars), Is.Zero);
        }

        [Test]
        public void Step_TwoCarsSameTick_BreaksOnce()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            // Both cars touch the first cone, arriving from opposite sides.
            BreakableCarSample[] cars = { Car(0.2f, 0f, 10f, 0f), Car(1.8f, 0f, -10f, 0f) };

            int broken = rules.Step(10, cars);

            Assert.That(broken, Is.EqualTo(1));
            Assert.That(set.BrokenCount, Is.EqualTo(1));
            // The first car to be processed owns the event; the second finds the prop already broken.
            Assert.That(rules.Event(0).CarIndex, Is.Zero);
        }

        [Test]
        public void Step_SameCarNextTick_BreaksOnce()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] cars = { Car(0.2f, 0f, 10f, 0f) };

            Assert.That(rules.Step(10, cars), Is.EqualTo(1));
            Assert.That(rules.Step(11, cars), Is.Zero);
            Assert.That(set[0].BrokenTick, Is.EqualTo(10));
        }

        [Test]
        public void Step_ReplayOfTheSameTick_IsANoOp()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] cars = { Car(0.2f, 0f, 10f, 0f) };

            Assert.That(rules.Step(40, cars), Is.EqualTo(1));
            rules.ClearEvents();
            int replayed = rules.Step(40, cars);

            Assert.That(replayed, Is.Zero);
            Assert.That(set.BrokenCount, Is.EqualTo(1));
        }

        [Test]
        public void Step_KillSwitchOff_EmitsNothingAndKeepsThePropSolid()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            rules.Enabled = false;
            BreakableCarSample[] cars = { Car(0.2f, 0f, 40f, 0f) };

            Assert.That(rules.Step(10, cars), Is.Zero);
            Assert.That(set[0].Broken, Is.False);
            Assert.That(set[0].Blocks, Is.True);
        }

        [Test]
        public void Step_BrokenPropNoLongerCollides()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] first = { Car(0.2f, 0f, 10f, 0f) };
            Assert.That(rules.Step(10, first), Is.EqualTo(1));
            Assert.That(set[0].Blocks, Is.False);

            BreakableCarSample[] second = { Car(0.2f, 0f, 30f, 0f) };
            Assert.That(rules.Step(11, second), Is.Zero);
        }

        [Test]
        public void Step_SecondPropIsStillBreakableAfterTheFirst()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);

            // The first cone, then a drive over to the box at x = 3.
            Assert.That(rules.Step(10, new[] { Car(0.2f, 0f, 10f, 0f) }), Is.EqualTo(1));
            Assert.That(rules.Step(11, new[] { Car(2.2f, 0f, 10f, 0f) }), Is.EqualTo(1));
            Assert.That(set.BrokenCount, Is.EqualTo(2));
            Assert.That(rules.Event(0).PropId, Is.EqualTo(1));
        }

        [Test]
        public void TryGetSpeedPenalty_IsOffEverywhereByDefault()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);

            Assert.That(rules.TryGetSpeedPenalty(0, out float multiplier, out int ticks), Is.False);
            Assert.That(multiplier, Is.EqualTo(1f));
            Assert.That(ticks, Is.Zero);
        }

        // ---------------------------------------------------------------- the grid

        [Test]
        public void Grid_FindsPropsWhoseCentresSitInOtherCells()
        {
            // Cell size 1: the two prop centres fall in cells 0 and 2 while the query centre is in
            // cell 1, so a grid that only scanned its own cell would miss them.
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            var grid = new BreakablePropGrid(set, 1f, 0f);
            var results = new int[8];

            int found = grid.Query(2f, 0f, 1.2f, results);

            Assert.That(found, Is.EqualTo(2));
            Assert.That(Contains(results, found, 0), Is.True);
            Assert.That(Contains(results, found, 1), Is.True);
        }

        [Test]
        public void Grid_QueryFarFromEveryProp_FindsNothing()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            var grid = new BreakablePropGrid(set, 1f, 0f);
            var results = new int[8];

            Assert.That(grid.Query(1000f, 1000f, 1f, results), Is.Zero);
        }

        [Test]
        public void Grid_QueryIsConservativeAndIncludesAPropAtTheReachEdge()
        {
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            var grid = new BreakablePropGrid(set, 1f, 0f);
            var results = new int[8];

            // Car circle 0.5 + prop circle 0.4 reaches 0.9, and this car is 0.8 from the box at x = 3.
            int found = grid.Query(2.2f, 0f, 0.5f, results);

            Assert.That(found, Is.GreaterThan(0));
            Assert.That(Contains(results, found, 1), Is.True);
        }

        [Test]
        public void Grid_QueryIsOnlyABroadPhase_SoStepStillRejectsNonOverlappingCandidates()
        {
            // The grid box grows by the largest prop radius but the rule does the exact circle test,
            // so a candidate can be returned and still be too far to break. This pins the split.
            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            var grid = new BreakablePropGrid(set, 1f, 0f);
            var results = new int[8];

            int found = grid.Query(4f, 0f, 0.5f, results);
            Assert.That(found, Is.EqualTo(1), "the box is inside the grid's query box");
            Assert.That(results[0], Is.EqualTo(1));

            // The car is 1.0 m from the box while their circles only reach 0.9, so the candidate must
            // be rejected by the rule even though the grid offered it.
            var rules = new BreakablePropRules(set, grid, set.Count);
            rules.Enabled = true;
            int broken = rules.Step(10, new[] { Car(4f, 0f, 10f, 0f) });
            Assert.That(broken, Is.Zero);
            Assert.That(set[1].Broken, Is.False);
        }

        // ---------------------------------------------------------------- the hot path

        [Test]
        public void Step_AThousandTicks_DoesNotAllocate()
        {
            BreakablePropSet warmSet = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules warm = Rules(warmSet);
            warm.Step(0, new[] { Car(0.2f, 0f, 10f, 0f) });

            BreakablePropSet set = BreakablePropSet.Build(Sample(), 8);
            BreakablePropRules rules = Rules(set);
            BreakableCarSample[] cars = { Car(0.2f, 0f, 10f, 0f), Car(20f, 20f, 0f, 0f) };

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 1000; tick++)
            {
                rules.Step(tick, cars);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        // ---------------------------------------------------------------- fixtures

        private static AuthoredEdgeProp[] Sample()
        {
            return new[]
            {
                new AuthoredEdgeProp(BreakablePropKind.Cone, 1f, 0f, 0.4f, true),
                new AuthoredEdgeProp(BreakablePropKind.Sign, 2f, 0f, 0.4f, false),
                new AuthoredEdgeProp(BreakablePropKind.Box, 3f, 0f, 0.4f, true),
            };
        }

        private static BreakablePropRules Rules(BreakablePropSet set)
        {
            var grid = new BreakablePropGrid(set, 4f, 2f);
            var rules = new BreakablePropRules(set, grid, set.Count);
            rules.Enabled = true;
            return rules;
        }

        private static BreakableCarSample Car(float x, float z, float vx, float vz)
        {
            return new BreakableCarSample(x, z, vx, vz, CarRadius);
        }

        private static bool Contains(int[] values, int count, int needle)
        {
            for (int i = 0; i < count; i++)
            {
                if (values[i] == needle)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
