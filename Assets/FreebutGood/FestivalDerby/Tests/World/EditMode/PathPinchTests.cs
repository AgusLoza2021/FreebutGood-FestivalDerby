using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// The pinch scan fires only where the inner half-width reaches the local radius of curvature and
    /// reports one issue per contiguous fold, so a long cusp is one warning rather than a wall of them.
    /// </summary>
    [TestFixture]
    public sealed class PathPinchTests
    {
        [Test]
        public void StraightPath_ProducesNoPinch()
        {
            var path = Build(new[] { 0f, 0f, 0f, 0f }, 5f, 5f);

            Assert.That(PathPinch.Scan(path, 0f, new MapIssue[8]), Is.Zero);
        }

        [Test]
        public void GentleTurn_WhereInnerWidthStaysInsideRadius_ProducesNoPinch()
        {
            // Curvature 0.05 -> radius 20 m, well clear of the 5 m inner half-width.
            var path = Build(new[] { 0.05f, 0.05f, 0.05f }, 5f, 5f);

            Assert.That(PathPinch.Scan(path, 0f, new MapIssue[8]), Is.Zero);
        }

        [Test]
        public void TightTurn_WhereInnerWidthReachesRadius_ReportsTheFirstPinchedSample()
        {
            // Curvature 0.5 -> radius 2 m; a 3 m half-width on the left-turning side folds.
            var path = Build(new[] { 0f, 0f, 0.5f, 0.5f, 0f }, 3f, 3f);

            var issues = new MapIssue[8];
            int count = PathPinch.Scan(path, 0f, issues);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(issues[0].Code, Is.EqualTo(MapIssueCode.Pinch));
            Assert.That(issues[0].Index, Is.EqualTo(2));
        }

        [Test]
        public void ContiguousFold_ReportsExactlyOneIssue()
        {
            var path = Build(new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 4f, 4f);

            Assert.That(PathPinch.Scan(path, 0f, new MapIssue[8]), Is.EqualTo(1));
        }

        [Test]
        public void TwoSeparateFolds_ReportTwoIssues()
        {
            var path = Build(new[] { 0.5f, 0f, 0.5f }, 3f, 3f);

            var issues = new MapIssue[8];
            int count = PathPinch.Scan(path, 0f, issues);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(issues[0].Index, Is.Zero);
            Assert.That(issues[1].Index, Is.EqualTo(2));
        }

        [Test]
        public void WidthMargin_WidensDetectionAheadOfTheExactCusp()
        {
            // Radius 2 m, inner width 1.8 m: no cusp yet, but a 0.2 margin warns at 1.6 m.
            var path = Build(new[] { 0.5f, 0.5f }, 1.8f, 1.8f);

            Assert.That(PathPinch.Scan(path, 0f, new MapIssue[8]), Is.Zero);
            Assert.That(PathPinch.Scan(path, 0.2f, new MapIssue[8]), Is.EqualTo(1));
        }

        [Test]
        public void Guards_NullPathOrNullBuffer_ReturnZero()
        {
            var path = Build(new[] { 0.5f, 0.5f }, 3f, 3f);

            Assert.That(PathPinch.Scan(null, 0f, new MapIssue[8]), Is.Zero);
            Assert.That(PathPinch.Scan(path, 0f, null), Is.Zero);
        }

        [Test]
        public void LeftTurn_FoldsTheRightEdgeNotTheLeft()
        {
            // Positive curvature turns left, so the centre of curvature sits left and the RIGHT edge
            // is the inner one; swapping the wide side must clear the warning.
            var wideRight = Build(new[] { 0.5f, 0.5f, 0.5f }, 0.5f, 3f);
            var wideLeft = Build(new[] { 0.5f, 0.5f, 0.5f }, 3f, 0.5f);

            Assert.That(PathPinch.Scan(wideRight, 0f, new MapIssue[8]), Is.EqualTo(1));
            Assert.That(PathPinch.Scan(wideLeft, 0f, new MapIssue[8]), Is.Zero);
        }

        /// <summary>
        /// A straight fixture plus explicit per-sample curvature and symmetric half-widths; the shared
        /// fixture leaves Curvature null on purpose, so the pinch tests set their own field.
        /// </summary>
        private static BakedPath Build(float[] curvature, float widthLeft, float widthRight)
        {
            int count = curvature.Length;
            BakedPath path = WorldTestFixtures.Straight(100f, count);
            path.Curvature = curvature;
            for (int i = 0; i < count; i++)
            {
                path.WidthLeft[i] = widthLeft;
                path.WidthRight[i] = widthRight;
            }

            return path;
        }
    }
}
