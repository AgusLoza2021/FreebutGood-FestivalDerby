using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Edit-mode coverage for the road sweep. These run without physics, so they can assert the exact
    /// shape of the mesh a bake produces: how many vertices a ring costs, where the loop seam lands,
    /// that the vertex colours name the surfaces the bake wrote, and that a second bake of the same
    /// input is bit-identical to the first.
    /// </summary>
    public class RoadMeshBuilderTests
    {
        private const float Radius = 25f;
        private const float Width = 16f;
        private const int Rings = 64;
        private static readonly float LoopLength = 2f * Mathf.PI * Radius;

        [Test]
        public void Build_EveryChunkIsAPairPerRingAndAQuadPerGap()
        {
            BakedPath path = BuildCirclePath();

            RoadMeshChunk[] chunks = RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults());

            Assert.Greater(chunks.Length, 0, "A 157 m loop must sweep into at least one chunk.");
            int totalRings = 1;
            for (int i = 0; i < chunks.Length; i++)
            {
                Mesh mesh = chunks[i].Mesh;
                Assert.IsNotNull(mesh);
                Assert.AreEqual(0, mesh.vertexCount % 2, "Rings are always left/right vertex pairs.");

                int ringCount = mesh.vertexCount / 2;
                Assert.AreEqual(mesh.vertexCount, mesh.uv.Length, "One UV per vertex.");
                Assert.AreEqual(ringCount - 1, mesh.triangles.Length / 6,
                    "One quad per gap between neighbouring rings.");
                Assert.LessOrEqual(ringCount, 65001 / 2, "A chunk must stay inside the 16-bit index budget.");

                if (i > 0)
                {
                    Assert.AreEqual(chunks[i - 1].EndDistance, chunks[i].StartDistance,
                        "Neighbouring chunks share their boundary ring, so the seam has no gap.");
                }
                totalRings += ringCount - 1;
            }
            Assert.Greater(totalRings, Rings, "The sweep resamples by arc length, not by baked index.");
        }

        [Test]
        public void Build_EveryVertexStaysOnTheRoadSurface()
        {
            BakedPath path = BuildCirclePath();

            RoadMeshChunk[] chunks = RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults());

            // The sweep samples the baked chords, so a sampled point can sit up to the chord sagitta
            // inside the ideal 25 m circle (one 64-gon segment sags ~0.031 m). A ring whose midpoint
            // drifts further than that, or whose two edges are not a full road width apart, has left
            // the swept surface.
            const float sag = 0.05f;
            for (int c = 0; c < chunks.Length; c++)
            {
                Vector3[] vertices = chunks[c].Mesh.vertices;
                Assert.AreEqual(0, vertices.Length % 2, "Rings are left/right vertex pairs.");
                for (int i = 0; i < vertices.Length; i += 2)
                {
                    Vector3 left = vertices[i];
                    Vector3 right = vertices[i + 1];
                    Assert.AreEqual(Width, Vector3.Distance(left, right), 1e-3f,
                        "One ring must span the full road width.");

                    Vector3 middle = (left + right) * 0.5f;
                    float radius = new Vector2(middle.x, middle.z).magnitude;
                    Assert.AreEqual(Radius, radius, sag,
                        "Ring " + (i / 2) + " of chunk " + c + " is not centred on the road.");
                    Assert.AreEqual(0f, middle.y, 1e-4f, "A level road sweeps on one plane.");
                }
            }
        }

        [Test]
        public void Build_IsDeterministicAcrossRuns()
        {
            BakedPath path = BuildCirclePath();

            RoadMeshChunk[] first = RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults());
            RoadMeshChunk[] second = RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults());

            Assert.AreEqual(first.Length, second.Length, "A re-bake must chunk identically.");
            for (int c = 0; c < first.Length; c++)
            {
                Assert.AreEqual(first[c].StartDistance, second[c].StartDistance);
                Assert.AreEqual(first[c].EndDistance, second[c].EndDistance);
                AssertSame(first[c].Mesh, second[c].Mesh);
            }
        }

        [Test]
        public void Uv_RunsFromZeroToTheLoopTileCountAndClosesOnPosition()
        {
            BakedPath path = BuildCirclePath();
            var settings = RoadMeshSettings.Defaults();
            settings.MetersPerTile = 4f;

            RoadMeshChunk[] chunks = RoadMeshBuilder.Build(path, settings);

            Mesh firstChunk = chunks[0].Mesh;
            Mesh lastChunk = chunks[chunks.Length - 1].Mesh;
            Vector2[] first = firstChunk.uv;
            Vector2[] last = lastChunk.uv;

            Assert.AreEqual(0f, first[0].x, 1e-4f, "The first ring starts at the origin of the tile run.");
            Assert.AreEqual(LoopLength / settings.MetersPerTile, last[last.Length - 1].x, 1e-3f,
                "u advances a whole number of tiles around the loop, which is what closes a repeating seam.");

            // Position continuity at the seam: the closing ring lands on the opening ring. The UV is
            // deliberately not wrapped back to zero, so a Repeat texture shows no visible join.
            Vector3 opening = firstChunk.vertices[0];
            Vector3 closing = lastChunk.vertices[lastChunk.vertexCount - 2];
            Assert.AreEqual(opening.x, closing.x, 1e-4f);
            Assert.AreEqual(opening.z, closing.z, 1e-4f);

            // Lateral v spans the full road width; left edge is 0 and right edge is the width in tiles.
            Assert.AreEqual(0f, first[0].y, 1e-4f);
            Assert.AreEqual(Width / settings.MetersPerTile, first[1].y, 1e-4f);
        }

        [Test]
        public void VertexColors_EncodeTheSurfaceSpanUnderEachRing()
        {
            BakedPath path = BuildCirclePath();
            // The far half of the loop is dirt, the near half asphalt: a hard boundary at half the lap.
            for (int i = 0; i < Rings; i++)
            {
                bool dirt = path.Distances[i] > LoopLength * 0.5f;
                path.SurfacePrimary[i] = (byte)(dirt ? SurfaceId.Dirt : SurfaceId.Street);
                path.SurfaceSecondary[i] = (byte)SurfaceId.Street;
                path.SurfaceBlend[i] = 0;
            }

            RoadMeshChunk[] chunks = RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults());

            bool sawStreet = false;
            bool sawDirt = false;
            for (int c = 0; c < chunks.Length; c++)
            {
                Color32[] colors = chunks[c].Mesh.colors32;
                for (int v = 0; v < colors.Length; v++)
                {
                    byte id = colors[v].r;
                    Assert.IsTrue(id == (byte)SurfaceId.Street || id == (byte)SurfaceId.Dirt,
                        "Only the two authored surfaces may appear.");
                    Assert.AreEqual(0, colors[v].b, "A single-layer sample has blend weight zero.");
                    sawStreet |= id == (byte)SurfaceId.Street;
                    sawDirt |= id == (byte)SurfaceId.Dirt;
                }
            }
            Assert.IsTrue(sawStreet, "The asphalt half must be in the mesh.");
            Assert.IsTrue(sawDirt, "The dirt half must be in the mesh.");
        }

        [Test]
        public void Build_UnusablePathYieldsNoChunks()
        {
            var path = new BakedPath();
            Assert.AreEqual(0, RoadMeshBuilder.Build(path, RoadMeshSettings.Defaults()).Length);
        }

        // ------------------------------------------------------------- fixture

        private static BakedPath BuildCirclePath()
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
            }
            path.TotalLength = LoopLength;
            path.ComputeCurvature();
            return path;
        }

        private static void AssertSame(Mesh expected, Mesh actual)
        {
            Vector3[] expectedVertices = expected.vertices;
            Vector3[] actualVertices = actual.vertices;
            Assert.AreEqual(expectedVertices.Length, actualVertices.Length);
            for (int i = 0; i < expectedVertices.Length; i++)
                Assert.AreEqual(expectedVertices[i], actualVertices[i], "Vertex " + i + " drifted.");

            Vector2[] expectedUv = expected.uv;
            Vector2[] actualUv = actual.uv;
            for (int i = 0; i < expectedUv.Length; i++)
                Assert.AreEqual(expectedUv[i], actualUv[i], "UV " + i + " drifted.");

            Color32[] expectedColors = expected.colors32;
            Color32[] actualColors = actual.colors32;
            for (int i = 0; i < expectedColors.Length; i++)
                Assert.AreEqual(expectedColors[i], actualColors[i], "Color " + i + " drifted.");

            int[] expectedTriangles = expected.triangles;
            int[] actualTriangles = actual.triangles;
            Assert.AreEqual(expectedTriangles.Length, actualTriangles.Length);
            for (int i = 0; i < expectedTriangles.Length; i++)
                Assert.AreEqual(expectedTriangles[i], actualTriangles[i], "Index " + i + " drifted.");
        }
    }
}
