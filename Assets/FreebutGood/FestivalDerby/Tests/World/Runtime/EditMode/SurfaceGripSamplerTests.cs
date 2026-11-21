using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Edit-mode coverage for the grip read the driving model samples. The sampler is pure path math,
    /// so the exact multiplier a car feels on asphalt, dirt or a blend can be pinned here, without a
    /// scene, a car or physics.
    /// </summary>
    public class SurfaceGripSamplerTests
    {
        private const float Radius = 25f;
        private const float Width = 16f;
        private const int Rings = 64;
        private static readonly float LoopLength = 2f * Mathf.PI * Radius;

        [Test]
        public void MultiplierAt_AsphaltIsNeutral()
        {
            BakedPath path = BuildCirclePath();
            var sampler = new SurfaceGripSampler(path, SurfaceTable.Defaults);

            Assert.AreEqual(1f, sampler.MultiplierAt(0f), 1e-5f,
                "Asphalt must be the neutral grip the handling model was tuned against.");
        }

        [Test]
        public void MultiplierAt_DirtUsesTheDefaultGrip()
        {
            BakedPath path = BuildCirclePath(dirtFromRatio: 0f);
            var sampler = new SurfaceGripSampler(path, SurfaceTable.Defaults);

            Assert.AreEqual(0.82f, sampler.MultiplierAt(LoopLength * 0.25f), 1e-4f,
                "Dirt's stock grip is 0.82, so a dirt lap scrubs the tyres more slowly than asphalt.");
        }

        [Test]
        public void MultiplierAt_BlendsTheTwoLayersByWeight()
        {
            BakedPath path = BuildCirclePath(dirtFromRatio: 0f);
            for (int i = 0; i < Rings; i++)
            {
                path.SurfacePrimary[i] = (byte)SurfaceId.Street;
                path.SurfaceSecondary[i] = (byte)SurfaceId.Dirt;
                path.SurfaceBlend[i] = 128;
            }
            var sampler = new SurfaceGripSampler(path, SurfaceTable.Defaults);

            float expected = 1f + (0.82f - 1f) * (128f / 255f);
            Assert.AreEqual(expected, sampler.MultiplierAt(0f), 1e-4f,
                "A half-way blend must read half-way between the two surfaces.");
        }

        [Test]
        public void Multiplier_WorldPositionResolvesTheSurfaceUnderTheCar()
        {
            BakedPath path = BuildCirclePath(dirtFromRatio: 0.5f);
            var sampler = new SurfaceGripSampler(path, SurfaceTable.Defaults);

            // Angle 0.25 of the loop is z > 0, the asphalt half; 0.75 is z < 0, the dirt half.
            float angleAsphalt = 0.25f * 2f * Mathf.PI;
            float angleDirt = 0.75f * 2f * Mathf.PI;
            var onAsphalt = new Vector3(Mathf.Cos(angleAsphalt) * Radius, 0f, Mathf.Sin(angleAsphalt) * Radius);
            var onDirt = new Vector3(Mathf.Cos(angleDirt) * Radius, 0f, Mathf.Sin(angleDirt) * Radius);

            Assert.AreEqual(1f, sampler.Multiplier(onAsphalt), 1e-4f);
            Assert.AreEqual(0.82f, sampler.Multiplier(onDirt), 1e-4f);
            // The cached nearest-path hint must survive a second query across the loop, not latch on
            // the first answer; a hint that never moves would make a car feel its start surface all lap.
            Assert.AreEqual(1f, sampler.Multiplier(onAsphalt), 1e-4f);
            Assert.AreEqual(0.82f, sampler.Multiplier(onDirt), 1e-4f);
        }

        [Test]
        public void Multiplier_WithoutAPathIsNeutral()
        {
            var sampler = new SurfaceGripSampler(null, null);

            Assert.AreEqual(1f, sampler.Multiplier(Vector3.zero), 1e-6f,
                "A car on a map with no baked path must drive on neutral grip, not on a null path.");
            Assert.AreEqual(1f, sampler.MultiplierAt(10f), 1e-6f);
        }

        // ------------------------------------------------------------- fixture

        private static BakedPath BuildCirclePath(float dirtFromRatio = 1f)
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[Rings],
                Tangents = new PathVector3[Rings],
                Ups = new PathVector3[Rings],
                WidthLeft = new float[Rings],
                WidthRight = new float[Rings],
                Banking = new float[Rings],
                Distances = new float[Rings],
                SurfacePrimary = new byte[Rings],
                SurfaceSecondary = new byte[Rings],
                SurfaceBlend = new byte[Rings],
                Closed = true,
            };

            for (int i = 0; i < Rings; i++)
            {
                float angle = 2f * Mathf.PI * i / Rings;
                var position = new Vector3(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius);
                var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                path.Positions[i] = PathConvert.ToPathVector(position);
                path.Tangents[i] = PathConvert.ToPathVector(tangent);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = Width * 0.5f;
                path.WidthRight[i] = Width * 0.5f;
                path.Distances[i] = LoopLength * i / Rings;

                bool dirt = dirtFromRatio <= 0f
                    || (dirtFromRatio < 1f && path.Distances[i] > LoopLength * dirtFromRatio);
                path.SurfacePrimary[i] = (byte)(dirt ? SurfaceId.Dirt : SurfaceId.Street);
                path.SurfaceSecondary[i] = (byte)SurfaceId.Street;
                path.SurfaceBlend[i] = 0;
            }
            path.TotalLength = LoopLength;
            path.ComputeCurvature();
            return path;
        }
    }
}
