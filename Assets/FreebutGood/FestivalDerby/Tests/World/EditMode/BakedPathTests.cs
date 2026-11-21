using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Engine-free contracts for the baked path: sampling, nearest distance across the loop seam,
    /// wrap, curvature of a sampled circle and the speed/braking plan. Written before the runtime
    /// code so the harness can prove the math without Unity.
    /// </summary>
    [TestFixture]
    public sealed class BakedPathTests
    {
        private const float Radius = 25f;

        // ------------------------------------------------------------- sampling

        [Test]
        public void Sample_QuarterLap_ReturnsQuarterPoint()
        {
            BakedPath path = Circle(64);

            bool ok = path.Sample(path.TotalLength * 0.25f, out PathSample sample);

            Assert.That(ok, Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(sample.Position.z, Is.EqualTo(Radius).Within(1e-2f));
            Assert.That(sample.Tangent.z, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void Sample_WrapsIntoTheLoop()
        {
            BakedPath path = Circle(64);

            path.Sample(path.TotalLength + 7f, out PathSample wrapped);
            path.Sample(7f, out PathSample plain);

            Assert.That(wrapped.Position.x, Is.EqualTo(plain.Position.x).Within(1e-3f));
            Assert.That(wrapped.Position.z, Is.EqualTo(plain.Position.z).Within(1e-3f));
        }

        // ------------------------------------------------------------- wrap

        [Test]
        public void Wrap_FoldsBothDirections()
        {
            BakedPath path = Circle(32);

            Assert.That(path.Wrap(path.TotalLength + 5f), Is.EqualTo(5f).Within(1e-2f));
            Assert.That(path.Wrap(-2f), Is.EqualTo(path.TotalLength - 2f).Within(1e-2f));
            Assert.That(path.Wrap(0f), Is.EqualTo(0f).Within(1e-4f));
        }

        // ------------------------------------------------------------- nearest distance

        [Test]
        public void NearestDistance_NearSeam_ResolvesToEitherEnd()
        {
            BakedPath path = Circle(128);
            // A hair before angle zero: closest point is sample 0 and also the wrap end of the
            // last segment. The hint points at the last sample, so the search must cross the seam.
            float angle = -0.02f;
            var point = new PathVector3(
                Radius * (float)Math.Cos(angle), 0f, Radius * (float)Math.Sin(angle));
            int hint = path.Count - 1;

            float distance = path.NearestDistance(point, ref hint);

            // Distance and TotalLength - distance name the same spot; either end is correct.
            float circular = Math.Min(distance, path.TotalLength - distance);
            Assert.That(circular, Is.LessThan(1f));
            Assert.That(hint, Is.InRange(0, path.Count - 1));
        }

        [Test]
        public void NearestDistance_WithHint_TracksAMovingCar()
        {
            BakedPath path = Circle(256);
            int hint = 0;
            float previous = 0f;
            // Walk a quarter lap one metre at a time; the returned distance must stay monotonic and
            // the hint must advance, which is what makes the fixed-window search correct in a tick.
            for (float d = 0f; d < path.TotalLength * 0.25f; d += 1f)
            {
                path.Sample(d, out PathSample sample);
                float nearest = path.NearestDistance(sample.Position, ref hint);
                Assert.That(nearest, Is.GreaterThanOrEqualTo(previous - 0.5f));
                previous = nearest;
            }
        }

        // ------------------------------------------------------------- curvature

        [Test]
        public void Curvature_OnASampledCircle_IsInverseRadius()
        {
            BakedPath path = Circle(512);

            for (int i = 0; i < path.Count; i++)
                Assert.That(Math.Abs(path.Curvature[i]), Is.EqualTo(1f / Radius).Within(1e-3f),
                    "curvature at sample " + i);
        }

        // ------------------------------------------------------------- speed plan

        [Test]
        public void TargetSpeed_OnACircle_IsSqrtOfLateralAccelTimesRadius()
        {
            BakedPath path = Circle(512);

            path.BuildTargetSpeed(9f, 60f, 8f, 3);

            float expected = (float)Math.Sqrt(9f * Radius);
            for (int i = 0; i < path.Count; i++)
                Assert.That(path.TargetSpeed[i], Is.EqualTo(expected).Within(0.05f),
                    "target speed at sample " + i);
        }

        [Test]
        public void BrakingPass_LeavesEverySegmentFeasible()
        {
            const int count = 40;
            const float step = 2f;
            const int corner = 10;
            const float lateralAccel = 9f;
            const float topSpeed = 30f;
            const float brakeDecel = 6f;
            BakedPath path = CurvatureSpike(count, step, corner, 0.1f);

            path.BuildTargetSpeed(lateralAccel, topSpeed, brakeDecel, 3);

            Assert.That(path.TargetSpeed[corner], Is.EqualTo((float)Math.Sqrt(lateralAccel / 0.1f)).Within(0.02f));
            // The corner speed must bleed backwards: the sample one lap behind is already limited.
            Assert.That(path.TargetSpeed[0], Is.LessThan(topSpeed));
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float limit = (float)Math.Sqrt(
                    path.TargetSpeed[next] * path.TargetSpeed[next] + 2f * brakeDecel * step);
                Assert.That(path.TargetSpeed[i], Is.LessThanOrEqualTo(limit + 1e-3f),
                    "infeasible braking into sample " + next);
            }
        }

        // ------------------------------------------------------------- helpers

        private static BakedPath Circle(int samples)
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[samples],
                Tangents = new PathVector3[samples],
                Ups = new PathVector3[samples],
                WidthLeft = new float[samples],
                WidthRight = new float[samples],
                Banking = new float[samples],
                Distances = new float[samples],
                Closed = true,
            };

            float circumference = 2f * (float)Math.PI * Radius;
            for (int i = 0; i < samples; i++)
            {
                float angle = 2f * (float)Math.PI * i / samples;
                path.Positions[i] = new PathVector3(
                    Radius * (float)Math.Cos(angle), 0f, Radius * (float)Math.Sin(angle));
                path.Tangents[i] = new PathVector3(
                    -(float)Math.Sin(angle), 0f, (float)Math.Cos(angle));
                path.Ups[i] = PathVector3.Up;
                path.Distances[i] = circumference * i / samples;
            }

            path.TotalLength = circumference;
            path.ComputeCurvature();
            return path;
        }

        private static BakedPath CurvatureSpike(int count, float step, int spikeIndex, float curvature)
        {
            // A shape-free loop: the planner reads Curvature and Distances directly, so the test
            // does not need real geometry to prove the braking sweep.
            var path = new BakedPath
            {
                Positions = new PathVector3[count],
                Distances = new float[count],
                Curvature = new float[count],
                Closed = true,
                TotalLength = count * step,
            };
            for (int i = 0; i < count; i++)
            {
                path.Positions[i] = new PathVector3(i * step, 0f, 0f);
                path.Distances[i] = i * step;
            }
            path.Curvature[spikeIndex] = curvature;
            return path;
        }
    }
}
