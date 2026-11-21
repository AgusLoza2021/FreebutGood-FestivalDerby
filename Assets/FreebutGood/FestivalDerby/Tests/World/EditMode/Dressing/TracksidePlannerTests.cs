using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the trackside dressing planner: determinism, kerbs only on corners (including a
    /// run that crosses the loop seam), gravel only on the quick corners, a far band that never
    /// touches the road and never self-overlaps, and a zero-GC steady state.
    /// </summary>
    [TestFixture]
    public sealed class TracksidePlannerTests
    {
        private const float RoadWidth = 16f;
        private const float HalfWidth = RoadWidth * 0.5f;

        // ------------------------------------------------------------- determinism

        [Test]
        public void Plan_SameSeed_IsIdentical()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            var first = new TracksidePlacement[4096];
            var second = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, rules, 1234u, first, out int firstCount);
            TracksidePlanner.Plan(path, RoadWidth, rules, 1234u, second, out int secondCount);

            Assert.That(secondCount, Is.EqualTo(firstCount));
            Assert.That(count, Is.GreaterThan(0));
            for (int i = 0; i < firstCount; i++)
                Assert.That(Same(first[i], second[i]), Is.True, "placement " + i);
        }

        [Test]
        public void Plan_DifferentSeed_MovesTheFarBand()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            var a = new TracksidePlacement[4096];
            var b = new TracksidePlacement[4096];

            TracksidePlanner.Plan(path, RoadWidth, rules, 11u, a, out int countA);
            TracksidePlanner.Plan(path, RoadWidth, rules, 99u, b, out int countB);

            int farA = 0;
            int farB = 0;
            for (int i = 0; i < countA; i++)
                if (TracksideKindInfo.IsFarBand(a[i].Kind))
                    farA++;
            for (int i = 0; i < countB; i++)
                if (TracksideKindInfo.IsFarBand(b[i].Kind))
                    farB++;

            Assert.That(farA, Is.GreaterThan(0));
            Assert.That(farB, Is.GreaterThan(0));
            Assert.That(FarBandsDiffer(a, countA, b, countB), Is.True);
        }

        // ------------------------------------------------------------- corners and kerbs

        [Test]
        public void Oval_PutsKerbsOnBothCorners_AndNoneOnTheStraights()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            var buffer = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, TracksideRuleSet.CircuitDefault, 7u, buffer, out _);

            Assert.That(CountKind(buffer, count, TracksideKind.Kerb, -1), Is.EqualTo(2));
            Assert.That(CountKind(buffer, count, TracksideKind.Kerb, 1), Is.EqualTo(2));

            // The middle of each 150 m straight must stay paint-free.
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].Kind != TracksideKind.Kerb)
                    continue;
                Assert.That(Covers(buffer[i], 75f, path.TotalLength), Is.False);
                Assert.That(Covers(buffer[i], 300f, path.TotalLength), Is.False);
            }
        }

        [Test]
        public void OvalWrap_JoinsTheKerbRunAcrossTheSeam()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, true);
            var buffer = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, TracksideRuleSet.CircuitDefault, 7u, buffer, out _);

            // A split run would read as three kerbs per side: the seam corner counted twice.
            Assert.That(CountKind(buffer, count, TracksideKind.Kerb, -1), Is.EqualTo(2));
            Assert.That(CountKind(buffer, count, TracksideKind.Kerb, 1), Is.EqualTo(2));

            bool spansSeam = false;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].Kind != TracksideKind.Kerb)
                    continue;
                if (buffer[i].DistanceAlong + buffer[i].LengthMetres > path.TotalLength + 1f)
                    spansSeam = true;
            }

            Assert.That(spansSeam, Is.True);
        }

        // ------------------------------------------------------------- run-off and gravel

        [Test]
        public void Gravel_TrapsOnlyTheQuickCorner()
        {
            BakedPath path = TracksideDressingFixtures.TwoCorners();
            var buffer = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, TracksideRuleSet.CircuitDefault, 3u, buffer, out _);

            // The R = 45 m corner is above the corner threshold but below the gravel one, so it still
            // gets kerbs on both sides and no trap. Only the R = 25 m corner qualifies.
            Assert.That(CountKind(buffer, count, TracksideKind.Kerb, 1), Is.EqualTo(2));

            int gravel = 0;
            for (int i = 0; i < count; i++)
            {
                TracksidePlacement placement = buffer[i];
                if (placement.Kind != TracksideKind.GravelTrap)
                    continue;
                gravel++;
                // R = 25 m turns left, so its outside is the right (+1) side, and the trap starts at
                // that corner's first sample. The R = 100 m control corner gets none.
                Assert.That(placement.Side, Is.EqualTo(1));
                Assert.That(placement.DistanceAlong, Is.InRange(39f, 41f));
            }

            Assert.That(gravel, Is.EqualTo(1));
        }

        // ------------------------------------------------------------- far band

        [Test]
        public void FarBand_KeepsClearOfTheRoadAndDoesNotOverlap()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            var buffer = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, rules, 42u, buffer, out _);

            int far = 0;
            var x = new float[count];
            var z = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!TracksideKindInfo.IsFarBand(buffer[i].Kind))
                    continue;
                float distance = TracksidePlanner.DistanceToCentreline(path, buffer[i].X, buffer[i].Z);
                Assert.That(distance, Is.GreaterThanOrEqualTo(HalfWidth + rules.FarBandMargin - 1e-2f));
                x[far] = buffer[i].X;
                z[far] = buffer[i].Z;
                far++;
            }

            Assert.That(far, Is.GreaterThan(0));
            float minimum = rules.FarBandRadius * 2f - 1e-2f;
            for (int i = 0; i < far; i++)
            {
                for (int j = i + 1; j < far; j++)
                {
                    float dx = x[i] - x[j];
                    float dz = z[i] - z[j];
                    Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(minimum * minimum));
                }
            }
        }

        // ------------------------------------------------------------- collider flags

        [Test]
        public void KindInfo_ColliderFlags_AreAsSpecified()
        {
            Assert.That(TracksideKindInfo.Collides(TracksideKind.TyreWall), Is.True);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Fence), Is.True);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Barrier), Is.True);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Cone), Is.True);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Box), Is.True);

            Assert.That(TracksideKindInfo.Collides(TracksideKind.Kerb), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.EdgeLine), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.GravelTrap), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Tree), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Grandstand), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Tent), Is.False);
            Assert.That(TracksideKindInfo.Collides(TracksideKind.Flag), Is.False);
        }

        [Test]
        public void Plan_FlagsCollidersPerKind_AndOnlyLightPropsBreak()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            rules.CornerPropChance = 1f;
            var buffer = new TracksidePlacement[4096];

            int count = TracksidePlanner.Plan(path, RoadWidth, rules, 5u, buffer, out _);

            int cones = 0;
            for (int i = 0; i < count; i++)
            {
                TracksidePlacement placement = buffer[i];
                Assert.That(placement.Collider, Is.EqualTo(TracksideKindInfo.Collides(placement.Kind)));
                bool lightProp = placement.Kind == TracksideKind.Cone || placement.Kind == TracksideKind.Box;
                Assert.That(placement.Breakable, Is.EqualTo(lightProp));
                if (placement.Kind == TracksideKind.Cone)
                    cones++;
            }

            Assert.That(cones, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------- buffers and GC

        [Test]
        public void Plan_RespectsBufferCapacity()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            var buffer = new TracksidePlacement[10];

            int count = TracksidePlanner.Plan(path, RoadWidth, TracksideRuleSet.CircuitDefault, 1u, buffer, out int reported);

            Assert.That(count, Is.EqualTo(10));
            Assert.That(reported, Is.EqualTo(10));
        }

        [Test]
        public void Plan_DoesNotAllocate_OnceTheScratchExists()
        {
            BakedPath path = TracksideDressingFixtures.Oval(150f, 25f, 1f, false);
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            var buffer = new TracksidePlacement[4096];

            for (int warmup = 0; warmup < 3; warmup++)
                TracksidePlanner.Plan(path, RoadWidth, rules, 77u, buffer, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
                TracksidePlanner.Plan(path, RoadWidth, rules, 77u, buffer, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        // ------------------------------------------------------------- helpers

        private static bool Same(in TracksidePlacement a, in TracksidePlacement b)
        {
            return a.Kind == b.Kind
                && a.X == b.X
                && a.Z == b.Z
                && a.YawDegrees == b.YawDegrees
                && a.Side == b.Side
                && a.DistanceAlong == b.DistanceAlong
                && a.LengthMetres == b.LengthMetres
                && a.WidthMetres == b.WidthMetres
                && a.Collider == b.Collider
                && a.Breakable == b.Breakable;
        }

        private static int CountKind(TracksidePlacement[] buffer, int count, TracksideKind kind, int side)
        {
            int found = 0;
            for (int i = 0; i < count; i++)
                if (buffer[i].Kind == kind && buffer[i].Side == side)
                    found++;
            return found;
        }

        private static bool FarBandsDiffer(TracksidePlacement[] a, int countA,
            TracksidePlacement[] b, int countB)
        {
            var first = new float[countA];
            var second = new float[countB];
            int firstCount = CollectFar(a, countA, first);
            int secondCount = CollectFar(b, countB, second);
            if (firstCount != secondCount)
                return true;
            for (int i = 0; i < firstCount; i++)
                if (first[i] != second[i])
                    return true;
            return false;
        }

        private static int CollectFar(TracksidePlacement[] buffer, int count, float[] destination)
        {
            int found = 0;
            for (int i = 0; i < count && found < destination.Length; i++)
            {
                if (!TracksideKindInfo.IsFarBand(buffer[i].Kind))
                    continue;
                destination[found] = buffer[i].X * 31f + buffer[i].Z * 17f + (int)buffer[i].Kind;
                found++;
            }
            return found;
        }

        private static bool Covers(in TracksidePlacement placement, float distance, float total)
        {
            if (placement.LengthMetres <= 0f)
                return false;
            float start = placement.DistanceAlong % total;
            if (start < 0f)
                start += total;
            float delta = distance - start;
            if (delta < 0f)
                delta += total;
            return delta <= placement.LengthMetres + 1e-3f;
        }
    }
}
