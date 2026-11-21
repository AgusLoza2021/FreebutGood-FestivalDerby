using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// The surface side of the baked path: an old asset without layers still answers Street, a bake
    /// stamps the authored surfaces, and the query stays allocation-free for the tick.
    /// </summary>
    [TestFixture]
    public sealed class BakedPathSurfaceTests
    {
        [Test]
        public void SurfaceAt_OldPathWithoutLayers_ReadsStreet()
        {
            BakedPath path = WorldTestFixtures.Straight(20f, 21);

            SurfaceBlend blend = path.SurfaceAt(7f);

            Assert.That(blend.Primary, Is.EqualTo(SurfaceId.Street));
            Assert.That(blend.Secondary, Is.EqualTo(SurfaceId.Street));
            Assert.That(blend.Blend, Is.Zero);
        }

        [Test]
        public void ApplySurfaces_StampsTheAuthoredSurfaceOnEverySample()
        {
            BakedPath path = WorldTestFixtures.Straight(20f, 21);

            path.ApplySurfaces(new SurfaceSpanSet(
                new[] { new SurfaceSpan(0f, 20f, SurfaceId.Gravel) }, 20f, false, SurfaceId.Street));

            Assert.That(path.DataVersion, Is.EqualTo(2));
            Assert.That(path.SurfaceAt(10f).Primary, Is.EqualTo(SurfaceId.Gravel));
            Assert.That(path.SurfaceAt(19.9f).Primary, Is.EqualTo(SurfaceId.Gravel));
        }

        [Test]
        public void SurfaceAt_QueryDoesNotAllocate()
        {
            BakedPath path = WorldTestFixtures.Straight(20f, 21);
            path.ApplySurfaces(new SurfaceSpanSet(new[]
            {
                new SurfaceSpan(0f, 10f, SurfaceId.Gravel),
                new SurfaceSpan(10f, 20f, SurfaceId.Snow, 1f),
            }, 20f, false, SurfaceId.Street));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
                path.SurfaceAt(i * 0.01f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }
    }
}
