using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the engine-free ribbon builder: the written vertex/index counts must equal the
    /// advertised requirements, kerbs must alternate red and white per block, an edge line must be
    /// white, the ribbon must sit on the correct side of the centreline, and the fill must not
    /// allocate once the buffers exist.
    /// </summary>
    [TestFixture]
    public sealed class KerbStripMeshTests
    {
        private const float RoadHalfWidth = 8f;

        [Test]
        public void RequiredCounts_MatchWhatFillWrites()
        {
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            TracksidePlacement placement = Kerb(length: 10f, side: 1, distanceAlong: 0f);

            int neededVertices = KerbStripMesh.RequiredVertexCount(placement, rules);
            int neededIndices = KerbStripMesh.RequiredIndexCount(placement, rules);
            var positions = new float[neededVertices * 3];
            var colors = new float[neededVertices * 4];
            var indices = new int[neededIndices];

            BakedPath path = TracksideDressingFixtures.Straight(20f, RoadHalfWidth, 0.5f);
            KerbStripMesh.Fill(path, placement, rules, positions, colors, indices,
                out int vertexCount, out int indexCount);

            // Ten metres at a two metre block with 1.5 m steps: 15 stations, 30 vertices, 14 quads.
            Assert.That(neededVertices, Is.EqualTo(30));
            Assert.That(neededIndices, Is.EqualTo(84));
            Assert.That(vertexCount, Is.EqualTo(neededVertices));
            Assert.That(indexCount, Is.EqualTo(neededIndices));
        }

        [Test]
        public void Kerb_AlternatesRedAndWhiteBlocks()
        {
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            TracksidePlacement placement = Kerb(length: 10f, side: 1, distanceAlong: 0f);
            int stationCount = KerbStripMesh.RequiredVertexCount(placement, rules) / 2;
            var positions = new float[stationCount * 2 * 3];
            var colors = new float[stationCount * 2 * 4];
            var indices = new int[KerbStripMesh.RequiredIndexCount(placement, rules)];

            BakedPath path = TracksideDressingFixtures.Straight(20f, RoadHalfWidth, 0.5f);
            KerbStripMesh.Fill(path, placement, rules, positions, colors, indices, out _, out _);

            int runs = 0;
            bool previousRed = false;
            for (int station = 0; station < stationCount; station++)
            {
                bool red = colors[station * 2 * 4 + 1] < 0.5f;
                if (station == 0 || red != previousRed)
                    runs++;
                previousRed = red;
            }

            Assert.That(runs, Is.EqualTo(5));
            Assert.That(colors[1], Is.LessThan(0.5f), "the first block is red");
        }

        [Test]
        public void EdgeLine_IsWhite()
        {
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            TracksidePlacement placement = new TracksidePlacement(TracksideKind.EdgeLine, 0f, 0f, 0f, 1,
                0f, 10f, rules.EdgeLineWidth, false, false);
            int stationCount = KerbStripMesh.RequiredVertexCount(placement, rules) / 2;
            var positions = new float[stationCount * 2 * 3];
            var colors = new float[stationCount * 2 * 4];
            var indices = new int[KerbStripMesh.RequiredIndexCount(placement, rules)];

            BakedPath path = TracksideDressingFixtures.Straight(20f, RoadHalfWidth, 0.5f);
            KerbStripMesh.Fill(path, placement, rules, positions, colors, indices,
                out int vertexCount, out _);

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                Assert.That(colors[vertex * 4], Is.GreaterThan(0.9f));
                Assert.That(colors[vertex * 4 + 1], Is.GreaterThan(0.9f));
                Assert.That(colors[vertex * 4 + 2], Is.GreaterThan(0.9f));
                Assert.That(colors[vertex * 4 + 3], Is.EqualTo(1f));
            }
        }

        [Test]
        public void Fill_PlacesTheRibbonBesideTheRoad()
        {
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            TracksidePlacement placement = Kerb(length: 4f, side: 1, distanceAlong: 0f);
            var positions = new float[KerbStripMesh.RequiredVertexCount(placement, rules) * 3];
            var indices = new int[KerbStripMesh.RequiredIndexCount(placement, rules)];

            BakedPath path = TracksideDressingFixtures.Straight(20f, RoadHalfWidth, 0.5f);
            KerbStripMesh.Fill(path, placement, rules, positions, null, indices, out _, out _);

            // Heading +X, so the road's right is -Z: the ribbon runs from the road edge outward.
            Assert.That(positions[0], Is.EqualTo(0f).Within(1e-3f));
            Assert.That(positions[2], Is.EqualTo(-RoadHalfWidth).Within(1e-3f));
            Assert.That(positions[5], Is.EqualTo(-(RoadHalfWidth + placement.WidthMetres)).Within(1e-3f));
        }

        [Test]
        public void Fill_DoesNotAllocate_OnceTheBuffersExist()
        {
            TracksideRuleSet rules = TracksideRuleSet.CircuitDefault;
            TracksidePlacement placement = Kerb(length: 10f, side: 1, distanceAlong: 0f);
            var positions = new float[KerbStripMesh.RequiredVertexCount(placement, rules) * 3];
            var colors = new float[KerbStripMesh.RequiredVertexCount(placement, rules) * 4];
            var indices = new int[KerbStripMesh.RequiredIndexCount(placement, rules)];
            BakedPath path = TracksideDressingFixtures.Straight(20f, RoadHalfWidth, 0.5f);

            for (int warmup = 0; warmup < 3; warmup++)
                KerbStripMesh.Fill(path, placement, rules, positions, colors, indices, out _, out _);

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                KerbStripMesh.Fill(path, placement, rules, positions, colors, indices, out _, out _);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        private static TracksidePlacement Kerb(float length, int side, float distanceAlong)
        {
            return new TracksidePlacement(TracksideKind.Kerb, 0f, 0f, 0f, side, distanceAlong, length,
                TracksideRuleSet.CircuitDefault.KerbWidth, false, false);
        }
    }
}
