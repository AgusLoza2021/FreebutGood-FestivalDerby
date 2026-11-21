using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Snap and symmetry math: mirror on both axes, loop direction after mirroring, 2-fold and
    /// 4-fold rotation, the tolerance-based seam weld and grid snap around a non-zero origin.
    /// </summary>
    [TestFixture]
    public sealed class SymmetryTests
    {
        [Test]
        public void MirrorX_MirrorsPositionsAndTangents()
        {
            var source = new[]
            {
                new SymmetryKnot(new PathVector3(1f, 0f, 2f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(3f, 0f, 4f), new PathVector3(0f, 1f, 1f)),
            };
            var output = new SymmetryKnot[4];

            int count = KnotListSymmetry.Apply(
                source, output, new MirrorTransform(GridAxis.X, PathVector3.Zero), 0.001f);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(output[0].Position.x, Is.EqualTo(-3f));
            Assert.That(output[0].Position.z, Is.EqualTo(4f));
            Assert.That(output[1].Position.x, Is.EqualTo(-1f));
            Assert.That(output[1].Position.z, Is.EqualTo(2f));
            // Output is reversed and the mirrored tangents are negated back into traversal direction.
            Assert.That(output[0].Tangent.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(output[0].Tangent.y, Is.EqualTo(-1f));
            Assert.That(output[0].Tangent.z, Is.EqualTo(-1f));
            Assert.That(output[1].Tangent.x, Is.EqualTo(1f));
        }

        [Test]
        public void MirrorZ_MirrorsPositionsAndTangents()
        {
            var source = new[]
            {
                new SymmetryKnot(new PathVector3(2f, 0f, 1f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(4f, 0f, 3f), new PathVector3(0f, 0f, 1f)),
            };
            var output = new SymmetryKnot[4];

            int count = KnotListSymmetry.Apply(
                source, output, new MirrorTransform(GridAxis.Z, PathVector3.Zero), 0.001f);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(output[0].Position.x, Is.EqualTo(4f));
            Assert.That(output[0].Position.z, Is.EqualTo(-3f));
            Assert.That(output[1].Position.z, Is.EqualTo(-1f));
            Assert.That(output[0].Tangent.z, Is.EqualTo(1f));
            Assert.That(output[1].Tangent.x, Is.EqualTo(-1f));
        }

        [Test]
        public void Mirror_PreservesLoopDirection()
        {
            var source = new[]
            {
                new SymmetryKnot(new PathVector3(0f, 0f, 0f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(4f, 0f, 0f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(4f, 0f, 4f), new PathVector3(0f, 0f, 1f)),
            };
            var output = new SymmetryKnot[8];

            int count = KnotListSymmetry.Apply(
                source, output, new MirrorTransform(GridAxis.X, PathVector3.Zero), 0.001f);

            Assert.That(count, Is.EqualTo(3));
            // Every emitted tangent must point down the segment the mirrored half is walked along.
            for (int i = 0; i < count - 1; i++)
            {
                PathVector3 segment = (output[i + 1].Position - output[i].Position).Normalized;
                Assert.That(PathVector3.Dot(output[i].Tangent, segment), Is.GreaterThan(0.99f),
                    "tangent at knot " + i);
            }
        }

        [Test]
        public void Rotation_TwoFold_MapsHalfwayRound()
        {
            var rotation = new RotationalSymmetry(2, PathVector3.Zero);

            PathVector3 rotated = rotation.Position(new PathVector3(10f, 0f, 0f), 1);
            PathVector3 tangent = rotation.Tangent(new PathVector3(1f, 0f, 0f), 1);

            Assert.That(rotated.x, Is.EqualTo(-10f).Within(1e-4f));
            Assert.That(rotated.z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(tangent.x, Is.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void Rotation_FourFold_CyclesBackToStart()
        {
            var rotation = new RotationalSymmetry(4, PathVector3.Zero);
            var first = new PathVector3(10f, 0f, 0f);

            Assert.That(rotation.Position(first, 1).z, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(rotation.Position(first, 2).x, Is.EqualTo(-10f).Within(1e-4f));
            Assert.That(rotation.Position(first, 3).z, Is.EqualTo(-10f).Within(1e-4f));
            Assert.That(rotation.Position(first, 4).x, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void Weld_DropsSeamWithinTolerance()
        {
            var output = new SymmetryKnot[4];

            int count = KnotListSymmetry.Apply(
                SeamSource(), output, new MirrorTransform(GridAxis.X, PathVector3.Zero), 0.01f);

            Assert.That(count, Is.EqualTo(2), "a 0.005 seam duplicate is inside a 0.01 weld");
        }

        [Test]
        public void Weld_KeepsSeamBeyondTolerance()
        {
            var output = new SymmetryKnot[4];

            int count = KnotListSymmetry.Apply(
                SeamSource(), output, new MirrorTransform(GridAxis.X, PathVector3.Zero), 0.001f);

            Assert.That(count, Is.EqualTo(3), "a 0.005 seam gap is outside a 0.001 weld");
        }

        [Test]
        public void Snap_WithOriginAndNegativeCoordinate()
        {
            var snap = new GridSnap(0.5f, new PathVector3(10f, 0f, 10f));

            PathVector3 snapped = snap.Snap(new PathVector3(11.2f, 3f, 8.6f));

            Assert.That(snapped.x, Is.EqualTo(11f).Within(1e-4f));
            Assert.That(snapped.z, Is.EqualTo(8.5f).Within(1e-4f));
            Assert.That(snapped.y, Is.EqualTo(3f), "Y is untouched");

            var negative = new GridSnap(1f, PathVector3.Zero);
            Assert.That(negative.Snap(new PathVector3(-1.5f, 0f, -2.4f)).x, Is.EqualTo(-2f));
            Assert.That(negative.Snap(new PathVector3(-1.5f, 0f, -2.4f)).z, Is.EqualTo(-2f));

            Assert.That(new GridSnap(0f, PathVector3.Zero).Snap(new PathVector3(1.234f, 0f, 0f)).x,
                Is.EqualTo(1.234f), "step 0 disables snapping");
        }

        private static SymmetryKnot[] SeamSource()
        {
            return new[]
            {
                new SymmetryKnot(new PathVector3(0f, 0f, 0f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(4f, 0f, 0f), new PathVector3(1f, 0f, 0f)),
                new SymmetryKnot(new PathVector3(0.005f, 0f, 0f), new PathVector3(1f, 0f, 0f)),
            };
        }
    }
}
