using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Determinism and bounds for the seeded scatter: the same seed repeats, a different one moves
    /// placements, an edit far away leaves earlier buckets alone, degenerate rules emit nothing and
    /// the fill never overruns the caller buffer nor allocates.
    /// </summary>
    [TestFixture]
    public sealed class ScatterGeneratorTests
    {
        [Test]
        public void Fill_SameSeed_IsIdentical()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            var first = new ScatterPoint[60];
            var second = new ScatterPoint[60];

            int count = ScatterGenerator.Fill(path, rule, 123, first);

            Assert.That(ScatterGenerator.Fill(path, rule, 123, second), Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(second[i].Distance, Is.EqualTo(first[i].Distance));
                Assert.That(second[i].Lateral, Is.EqualTo(first[i].Lateral));
                Assert.That(second[i].Yaw, Is.EqualTo(first[i].Yaw));
                Assert.That(second[i].Scale, Is.EqualTo(first[i].Scale));
                Assert.That(second[i].VariantIndex, Is.EqualTo(first[i].VariantIndex));
            }
        }

        [Test]
        public void Fill_DifferentSeed_Differs()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            var a = new ScatterPoint[60];
            var b = new ScatterPoint[60];

            int na = ScatterGenerator.Fill(path, rule, 123, a);
            int nb = ScatterGenerator.Fill(path, rule, 456, b);

            bool differs = na != nb;
            for (int i = 0; i < na && i < nb && !differs; i++)
                differs = a[i].Distance != b[i].Distance || a[i].Yaw != b[i].Yaw;
            Assert.That(differs, Is.True);
        }

        [Test]
        public void Fill_EditFarAway_KeepsEarlierBucketsIdentical()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            var before = new ScatterPoint[60];
            var after = new ScatterPoint[60];

            int count = ScatterGenerator.Fill(path, rule, 123, before);

            ScatterRule edited = rule;
            edited.Density = new[]
            {
                new DensityKey(0f, 1f),
                new DensityKey(0.8f, 1f),
                new DensityKey(1f, 0f),
            };
            ScatterGenerator.Fill(path, edited, 123, after);

            // The first 40 buckets sit below the edited curve's 0.8 knee, so they cannot move.
            Assert.That(count, Is.GreaterThanOrEqualTo(40));
            for (int i = 0; i < 40; i++)
                Assert.That(after[i].Distance, Is.EqualTo(before[i].Distance));
        }

        [Test]
        public void Fill_ProbabilityZero_EmitsNothing()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            rule.Probability = 0f;
            var buffer = new ScatterPoint[60];

            Assert.That(ScatterGenerator.Fill(path, rule, 123, buffer), Is.Zero);
        }

        [Test]
        public void Fill_ZeroDensity_EmitsNothing()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            rule.Density = new[] { new DensityKey(0f, 0f), new DensityKey(1f, 0f) };
            var buffer = new ScatterPoint[60];

            Assert.That(ScatterGenerator.Fill(path, rule, 123, buffer), Is.Zero);
        }

        [Test]
        public void Fill_RespectsBufferCapacity()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            var buffer = new ScatterPoint[8];

            int written = ScatterGenerator.Fill(path, rule, 123, buffer);

            Assert.That(written, Is.EqualTo(8));
        }

        [Test]
        public void Fill_DoesNotAllocate()
        {
            BakedPath path = WorldTestFixtures.Straight(100f, 101);
            ScatterRule rule = Rule();
            var buffer = new ScatterPoint[60];
            ScatterGenerator.Fill(path, rule, 123, buffer);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                ScatterGenerator.Fill(path, rule, 123, buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        private static ScatterRule Rule()
        {
            return new ScatterRule
            {
                Spacing = 2f,
                JitterAlong = 0.2f,
                JitterLateral = 0.5f,
                MinYaw = -10f,
                MaxYaw = 10f,
                MinScale = 0.8f,
                MaxScale = 1.2f,
                LateralMin = 2f,
                LateralMax = 3f,
                Probability = 1f,
                Layer = 7,
                VariantCount = 4,
            };
        }
    }
}
