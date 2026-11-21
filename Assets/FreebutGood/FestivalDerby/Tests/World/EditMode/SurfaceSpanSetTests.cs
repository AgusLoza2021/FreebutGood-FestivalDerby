using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the normalized surface profile: base gaps, overlap priority, closed-loop wrap,
    /// the two transition edges and the two-layer cap.
    /// </summary>
    [TestFixture]
    public sealed class SurfaceSpanSetTests
    {
        [Test]
        public void Evaluate_Gap_FallsBackToBaseSurface()
        {
            var set = new SurfaceSpanSet(
                new[] { new SurfaceSpan(1f, 3f, SurfaceId.Dirt) }, 10f, false, SurfaceId.Street);

            Assert.That(set.Evaluate(0f).Primary, Is.EqualTo(SurfaceId.Street));
            Assert.That(set.Evaluate(2f).Primary, Is.EqualTo(SurfaceId.Dirt));
            Assert.That(set.Evaluate(6f).Primary, Is.EqualTo(SurfaceId.Street));
        }

        [Test]
        public void Evaluate_Overlap_LaterSpanWins()
        {
            var set = new SurfaceSpanSet(new[]
            {
                new SurfaceSpan(1f, 9f, SurfaceId.Dirt),
                new SurfaceSpan(3f, 7f, SurfaceId.Snow),
            }, 10f, false, SurfaceId.Street);

            Assert.That(set.Evaluate(2f).Primary, Is.EqualTo(SurfaceId.Dirt));
            Assert.That(set.Evaluate(5f).Primary, Is.EqualTo(SurfaceId.Snow));
            Assert.That(set.Evaluate(8f).Primary, Is.EqualTo(SurfaceId.Dirt));
        }

        [Test]
        public void Evaluate_ClosedLoop_WrapsAcrossStartLine()
        {
            var set = new SurfaceSpanSet(
                new[] { new SurfaceSpan(8f, 2f, SurfaceId.Dirt) }, 10f, true, SurfaceId.Street);

            Assert.That(set.Evaluate(9f).Primary, Is.EqualTo(SurfaceId.Dirt));
            Assert.That(set.Evaluate(1f).Primary, Is.EqualTo(SurfaceId.Dirt));
            Assert.That(set.Evaluate(5f).Primary, Is.EqualTo(SurfaceId.Street));
        }

        [Test]
        public void Evaluate_OpenPath_DoesNotWrapAcrossStartLine()
        {
            var set = new SurfaceSpanSet(
                new[] { new SurfaceSpan(8f, 2f, SurfaceId.Dirt) }, 10f, false, SurfaceId.Street);

            Assert.That(set.Evaluate(9f).Primary, Is.EqualTo(SurfaceId.Street));
            Assert.That(set.Evaluate(1f).Primary, Is.EqualTo(SurfaceId.Street));
        }

        [Test]
        public void Evaluate_EnteringTransition_MidpointBlendsHalfway()
        {
            var set = new SurfaceSpanSet(
                new[] { new SurfaceSpan(3f, 7f, SurfaceId.Snow, 2f) }, 10f, false, SurfaceId.Street);

            SurfaceBlend blend = set.Evaluate(4f);

            Assert.That(blend.Primary, Is.EqualTo(SurfaceId.Street));
            Assert.That(blend.Secondary, Is.EqualTo(SurfaceId.Snow));
            Assert.That((int)blend.Blend, Is.EqualTo(128).Within(1));
            Assert.That(set.Evaluate(3f).Primary, Is.EqualTo(SurfaceId.Street));
            Assert.That(set.Evaluate(5f).Primary, Is.EqualTo(SurfaceId.Snow));
        }

        [Test]
        public void Evaluate_LeavingTransition_FadesFromSpanIntoNeighbour()
        {
            var set = new SurfaceSpanSet(
                new[] { new SurfaceSpan(3f, 7f, SurfaceId.Snow, 2f) }, 10f, false, SurfaceId.Street);

            SurfaceBlend blend = set.Evaluate(6f);

            // The span is what you are driving on at the start of the fade, so it stays primary and
            // the following surface is the one being mixed in.
            Assert.That(blend.Primary, Is.EqualTo(SurfaceId.Snow));
            Assert.That(blend.Secondary, Is.EqualTo(SurfaceId.Street));
            Assert.That((int)blend.Blend, Is.EqualTo(128).Within(1));
            Assert.That(set.Evaluate(5f).Secondary, Is.EqualTo(SurfaceId.Street));
            Assert.That(set.Evaluate(6.9f).Secondary, Is.EqualTo(SurfaceId.Street));
        }

        [Test]
        public void Evaluate_ThreeOverlappingSpans_NeverExposesAThirdLayer()
        {
            var set = new SurfaceSpanSet(new[]
            {
                new SurfaceSpan(0f, 10f, SurfaceId.Dirt),
                new SurfaceSpan(2f, 8f, SurfaceId.Snow),
                new SurfaceSpan(4f, 6f, SurfaceId.Mud, 2f),
            }, 10f, false, SurfaceId.Gravel);

            SurfaceBlend blend = set.Evaluate(5f);

            // The winner is the last span; the two earlier ones are dropped, never composited.
            Assert.That(blend.Secondary, Is.EqualTo(SurfaceId.Mud));
            Assert.That(blend.Primary, Is.EqualTo(SurfaceId.Gravel));
            Assert.That(blend.Primary, Is.Not.EqualTo(SurfaceId.Dirt));
            Assert.That(blend.Primary, Is.Not.EqualTo(SurfaceId.Snow));
        }
    }
}
