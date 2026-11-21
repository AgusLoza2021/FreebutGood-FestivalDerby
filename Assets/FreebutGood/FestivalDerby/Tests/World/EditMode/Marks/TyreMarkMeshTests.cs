using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the mesh fill: the advertised counts are what it writes, consecutive segments of a
    /// strip share their edge vertices, the colour alpha carries intensity times fade, and an
    /// under-sized buffer writes nothing.
    /// </summary>
    [TestFixture]
    public sealed class TyreMarkMeshTests
    {
        private static TyreMarkDecision Mark(float intensity = 0.8f)
        {
            return new TyreMarkDecision(true, intensity, 0.3f, 0.12f, 0.12f, 0.13f, 14f, 0f, SurfaceId.Street);
        }

        /// <summary>Straight strip of <paramref name="segments"/> one-metre segments along +x.</summary>
        private static TyreMarkTrail Straight(int segments)
        {
            var trail = new TyreMarkTrail(capacitySegments: 32, wheelCount: 1);
            trail.Submit(0, Mark(), 0f, 0f);
            for (int i = 1; i <= segments; i++)
                trail.Submit(0, Mark(), i, 0f);
            return trail;
        }

        [Test]
        public void Counts_MatchWhatFillWrites()
        {
            TyreMarkTrail trail = Straight(3);

            int neededVertices = TyreMarkMesh.RequiredVertexCount(trail);
            int neededIndices = TyreMarkMesh.RequiredIndexCount(trail);
            var positions = new float[neededVertices * 3];
            var colors = new float[neededVertices * 4];
            var uvs = new float[neededVertices * 2];
            var indices = new int[neededIndices];

            TyreMarkMeshFill fill = TyreMarkMesh.Fill(
                trail, TyreMarkMesh.LiftMeters, positions, colors, indices, uvs);

            // Three segments, one strip: four vertices for the strip start, two per segment after.
            Assert.That(neededVertices, Is.EqualTo(8));
            Assert.That(neededIndices, Is.EqualTo(18));
            Assert.That(fill.VertexCount, Is.EqualTo(neededVertices));
            Assert.That(fill.IndexCount, Is.EqualTo(neededIndices));
        }

        [Test]
        public void SharedVertices_JoinConsecutiveSegmentsOfAStrip()
        {
            TyreMarkTrail trail = Straight(2);
            int neededVertices = TyreMarkMesh.RequiredVertexCount(trail);
            var positions = new float[neededVertices * 3];
            var colors = new float[neededVertices * 4];
            var indices = new int[TyreMarkMesh.RequiredIndexCount(trail)];

            TyreMarkMesh.Fill(trail, TyreMarkMesh.LiftMeters, positions, colors, indices, null);

            // Quad 0 spans vertices 0,1 (its A pair) and 2,3 (its B pair). Quad 1 must start from that
            // same pair, which is the shared edge that removes the gap at the join.
            Assert.That(indices[0], Is.EqualTo(0));
            Assert.That(indices[6], Is.EqualTo(2));
            Assert.That(indices[9], Is.EqualTo(3));
        }

        [Test]
        public void Bend_StillSharesVertices()
        {
            var trail = new TyreMarkTrail(capacitySegments: 32, wheelCount: 1);
            trail.Submit(0, Mark(), 0f, 0f);
            trail.Submit(0, Mark(), 1f, 0f);
            // A 45 degree turn: the joining pair is the kink point.
            trail.Submit(0, Mark(), 1.7f, 0.7f);

            int neededVertices = TyreMarkMesh.RequiredVertexCount(trail);
            var positions = new float[neededVertices * 3];
            var indices = new int[TyreMarkMesh.RequiredIndexCount(trail)];

            TyreMarkMesh.Fill(trail, TyreMarkMesh.LiftMeters, positions, null, indices, null);

            Assert.That(trail.Count, Is.EqualTo(2));
            Assert.That(neededVertices, Is.EqualTo(6));
            Assert.That(indices[6], Is.EqualTo(2));
            for (int i = 0; i < neededVertices * 3; i++)
                Assert.That(float.IsNaN(positions[i]), Is.False);
        }

        [Test]
        public void Alpha_CarriesIntensityTimesFade()
        {
            TyreMarkTrail trail = Straight(1);
            int neededVertices = TyreMarkMesh.RequiredVertexCount(trail);
            var positions = new float[neededVertices * 3];
            var colors = new float[neededVertices * 4];
            var indices = new int[TyreMarkMesh.RequiredIndexCount(trail)];

            TyreMarkMesh.Fill(trail, TyreMarkMesh.LiftMeters, positions, colors, indices, null);
            Assert.That(colors[3], Is.EqualTo(0.8f).Within(1e-4f), "fresh segment, fade 1");

            trail.Advance(trail.LifetimeSeconds * 0.5f);
            TyreMarkMesh.Fill(trail, TyreMarkMesh.LiftMeters, positions, colors, indices, null);
            Assert.That(colors[3], Is.EqualTo(0.4f).Within(1e-4f), "half the lifetime, half the alpha");
        }

        [Test]
        public void TwoStrips_GiveATwoVertexPrefixEach()
        {
            var trail = new TyreMarkTrail(capacitySegments: 32, wheelCount: 2);
            trail.Submit(0, Mark(), 0f, 0f);
            trail.Submit(0, Mark(), 1f, 0f);
            trail.Submit(1, Mark(), 0f, 5f);
            trail.Submit(1, Mark(), 1f, 5f);

            // Two single-segment strips: 2 * (2 segments + 2 strips) = 8 vertices.
            Assert.That(trail.Count, Is.EqualTo(2));
            Assert.That(TyreMarkMesh.RequiredVertexCount(trail), Is.EqualTo(8));
        }

        [Test]
        public void SmallBuffer_WritesNothing()
        {
            TyreMarkTrail trail = Straight(3);
            var positions = new float[4];
            var indices = new int[4];

            TyreMarkMeshFill fill = TyreMarkMesh.Fill(
                trail, TyreMarkMesh.LiftMeters, positions, null, indices, null);

            Assert.That(fill.VertexCount, Is.EqualTo(0));
            Assert.That(fill.IndexCount, Is.EqualTo(0));
        }

        [Test]
        public void DirtyRange_IsConsumedByClearDirty()
        {
            TyreMarkTrail trail = Straight(2);

            Assert.That(trail.DirtyVertexStart, Is.EqualTo(0));
            trail.ClearDirty();

            int neededVertices = TyreMarkMesh.RequiredVertexCount(trail);
            var positions = new float[neededVertices * 3];
            var indices = new int[TyreMarkMesh.RequiredIndexCount(trail)];
            TyreMarkMeshFill fill = TyreMarkMesh.Fill(
                trail, TyreMarkMesh.LiftMeters, positions, null, indices, null);

            Assert.That(fill.DirtyVertexCount, Is.EqualTo(0), "nothing changed since the last upload");

            // The next segment dirties a tail of the buffer again.
            trail.Submit(0, Mark(), 3f, 0f);
            Assert.That(trail.DirtyVertexStart, Is.LessThan(TyreMarkMesh.RequiredVertexCount(trail)));
        }
    }
}
