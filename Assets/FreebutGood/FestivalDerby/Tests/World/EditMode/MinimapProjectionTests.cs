using NUnit.Framework;
#if NET5_0_OR_GREATER
using System;
#endif

namespace FestivalDerby.World.Tests
{
    [TestFixture]
    public sealed class MinimapProjectionTests
    {
        [Test]
        public void Projection_MapsBoundsAndCachesPolyline()
        {
            var path = new BakedPath { Positions = new[] { new PathVector3(-10f, 0f, -10f), new PathVector3(10f, 0f, 10f) } };
            var projection = new MinimapProjection(path, 100f, 100f, 0f, false);
            MinimapPoint low = projection.Project(-10f, -10f);
            MinimapPoint high = projection.Project(10f, 10f);
            Assert.That(low.X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(low.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(high.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(high.Y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(projection.PolylineCount, Is.EqualTo(2));
        }

#if NET5_0_OR_GREATER
        [Test]
        public void ProjectionCalls_AllocateNothing()
        {
            var path = new BakedPath { Positions = new[] { new PathVector3(-10f, 0f, -10f), new PathVector3(10f, 0f, 10f) } };
            var projection = new MinimapProjection(path, 100f, 100f, 2f, false);
            for (int i = 0; i < 100; i++) projection.Project(i * 0.1f, i * -0.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) projection.Project(i * 0.1f, i * -0.1f);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }
#endif

        [Test]
        public void Projection_FitsAspectWithoutClipping()
        {
            var path = new BakedPath { Positions = new[] { new PathVector3(-10f, 0f, 0f), new PathVector3(10f, 0f, 0f) } };
            var projection = new MinimapProjection(path, 100f, 50f, 5f, false);
            MinimapPoint a = projection.Project(-10f, 0f), b = projection.Project(10f, 0f);
            Assert.That(a.X, Is.GreaterThanOrEqualTo(0.049f));
            Assert.That(b.X, Is.LessThanOrEqualTo(0.951f));
            Assert.That(a.Y, Is.EqualTo(0.5f).Within(0.001f));
        }
    }
}
