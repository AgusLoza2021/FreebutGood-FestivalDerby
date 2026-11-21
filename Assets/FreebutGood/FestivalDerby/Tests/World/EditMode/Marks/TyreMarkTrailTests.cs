using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the segment ring: emission is gated by distance, the ring overwrites its oldest
    /// segment, a release or a gap starts a new strip, and the whole thing resets in place.
    /// </summary>
    [TestFixture]
    public sealed class TyreMarkTrailTests
    {
        private static TyreMarkDecision Mark(SurfaceId surface = SurfaceId.Street)
        {
            return new TyreMarkDecision(true, 0.8f, 0.3f, 0.12f, 0.12f, 0.13f, 14f, 0f, surface);
        }

        private static TyreMarkDecision Idle()
        {
            return new TyreMarkDecision(false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, SurfaceId.Street);
        }

        [Test]
        public void MinSegmentLength_GatesEmission()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 1);
            TyreMarkDecision mark = Mark();

            Assert.That(trail.Submit(0, mark, 0f, 0f), Is.False, "the first point never emits");
            Assert.That(trail.Submit(0, mark, 0.1f, 0f), Is.False, "too short");
            Assert.That(trail.Submit(0, mark, 0.2f, 0f), Is.False, "still too short");
            Assert.That(trail.Submit(0, mark, 0.3f, 0f), Is.True, "past the minimum");
            Assert.That(trail.Count, Is.EqualTo(1));

            TyreMarkSegment segment = trail.SegmentAt(0);
            Assert.That(segment.X0, Is.EqualTo(0f));
            Assert.That(segment.X1, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void Ring_OverwritesOldestAndCapsCount()
        {
            var trail = new TyreMarkTrail(capacitySegments: 4, wheelCount: 1);
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            for (int i = 1; i <= 6; i++)
                trail.Submit(0, mark, i, 0f);

            Assert.That(trail.Count, Is.EqualTo(4), "the count is capped at the capacity");
            // Six segments were emitted, so the two oldest were overwritten; the live head is (2 -> 3).
            Assert.That(trail.SegmentAt(0).X0, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(trail.SegmentAt(3).X1, Is.EqualTo(6f).Within(1e-5f));
        }

        [Test]
        public void Release_BreaksTheStrip()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 1);
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            trail.Submit(0, mark, 1f, 0f);
            trail.Submit(0, Idle(), 1.5f, 0f);
            trail.Submit(0, mark, 2f, 0f);
            trail.Submit(0, mark, 3f, 0f);

            Assert.That(trail.Count, Is.EqualTo(2));
            Assert.That(trail.SegmentAt(0).StripId, Is.Not.EqualTo(trail.SegmentAt(1).StripId));
        }

        [Test]
        public void LongJump_StartsANewStripWithoutSpanningTheMap()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 1);
            trail.BreakLengthMeters = 2f;
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            Assert.That(trail.Submit(0, mark, 1f, 0f), Is.True);
            // A respawn-style jump: no segment across the gap.
            Assert.That(trail.Submit(0, mark, 20f, 0f), Is.False);
            Assert.That(trail.Submit(0, mark, 21f, 0f), Is.True);

            Assert.That(trail.Count, Is.EqualTo(2));
            Assert.That(trail.SegmentAt(0).StripId, Is.Not.EqualTo(trail.SegmentAt(1).StripId));
            Assert.That(trail.SegmentAt(1).X0, Is.EqualTo(20f).Within(1e-5f));
        }

        [Test]
        public void Advance_AgesSegments()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 1);
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            trail.Submit(0, mark, 1f, 0f);
            trail.Advance(0.5f);

            Assert.That(trail.SegmentAt(0).Age, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(trail.FadeFor(0.5f), Is.LessThan(1f));
            Assert.That(trail.FadeFor(0f), Is.EqualTo(1f));
        }

        [Test]
        public void Clear_ResetsCountAndStrips()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 2);
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            trail.Submit(0, mark, 1f, 0f);
            Assert.That(trail.Count, Is.EqualTo(1));

            trail.Clear();

            Assert.That(trail.Count, Is.EqualTo(0));
            // The same wheel re-marking after a clear opens a fresh strip at its new point.
            Assert.That(trail.Submit(0, mark, 5f, 5f), Is.False, "the first point never emits");
            Assert.That(trail.Submit(0, mark, 6f, 5f), Is.True);
        }

        [Test]
        public void TwoWheels_KeepIndependentStrips()
        {
            var trail = new TyreMarkTrail(capacitySegments: 16, wheelCount: 2);
            TyreMarkDecision mark = Mark();

            trail.Submit(0, mark, 0f, 0f);
            trail.Submit(1, mark, 0f, 1f);
            trail.Submit(0, mark, 1f, 0f);
            trail.Submit(1, mark, 1f, 1f);

            Assert.That(trail.Count, Is.EqualTo(2));
            Assert.That(trail.SegmentAt(0).StripId, Is.Not.EqualTo(trail.SegmentAt(1).StripId));
        }

        [Test]
        public void Deterministic_SameSequenceSameSegments()
        {
            TyreMarkSegment[] first = RunSequence();
            TyreMarkSegment[] second = RunSequence();

            Assert.That(second.Length, Is.EqualTo(first.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(second[i].X0, Is.EqualTo(first[i].X0));
                Assert.That(second[i].Z1, Is.EqualTo(first[i].Z1));
                Assert.That(second[i].StripId, Is.EqualTo(first[i].StripId));
                Assert.That(second[i].Intensity, Is.EqualTo(first[i].Intensity));
            }
        }

        private static TyreMarkSegment[] RunSequence()
        {
            var trail = new TyreMarkTrail(capacitySegments: 8, wheelCount: 2);
            TyreMarkDecision mark = Mark();
            float x = 0f;
            for (int i = 0; i < 30; i++)
            {
                x += 0.37f;
                trail.Submit(i % 2, mark, x, (float)(i % 3));
            }

            var segments = new TyreMarkSegment[trail.Count];
            for (int i = 0; i < trail.Count; i++)
                segments[i] = trail.SegmentAt(i);
            return segments;
        }
    }
}
