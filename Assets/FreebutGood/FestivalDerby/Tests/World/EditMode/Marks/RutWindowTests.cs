using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the rut mask window: the clamps on authored numbers, the world-to-UV projection the
    /// road shader repeats, and — the one that matters — that every scroll is a whole number of texels,
    /// because that is what lets the mask be copied 1:1 instead of resampled.
    /// </summary>
    [TestFixture]
    public sealed class RutWindowTests
    {
        [Test]
        public void Create_ResolutionSnapsToPowerOfTwoInsideTheBand()
        {
            Assert.That(RutWindow.Create(72f, 800).Resolution, Is.EqualTo(1024));
            Assert.That(RutWindow.Create(72f, 100).Resolution, Is.EqualTo(128));
            Assert.That(RutWindow.Create(72f, 512).Resolution, Is.EqualTo(512));
            Assert.That(RutWindow.Create(72f, 4096).Resolution, Is.EqualTo(RutWindow.MaxResolution));
            Assert.That(RutWindow.Create(72f, 0).Resolution, Is.EqualTo(RutWindow.MinResolution));
        }

        [Test]
        public void Create_AreaIsClampedToTheDocumentedBand()
        {
            Assert.That(RutWindow.Create(10f, 1024).AreaSize, Is.EqualTo(RutWindow.MinAreaSize));
            Assert.That(RutWindow.Create(500f, 1024).AreaSize, Is.EqualTo(RutWindow.MaxAreaSize));
            Assert.That(RutWindow.Create(float.NaN, 1024).AreaSize, Is.EqualTo(RutWindow.MaxAreaSize));
            Assert.That(RutWindow.Create(72f, 1024).AreaSize, Is.EqualTo(72f));
        }

        [Test]
        public void SnapOrigin_FloorsInsteadOfRounding()
        {
            var window = RutWindow.Create(72f, 1024);
            float snap = window.SnapWorld;

            // Centred on the origin the raw corner is negative, and a rounding snap would jump it back up
            // to zero. The floor keeps it at -36 m, so the window is centred as asked.
            Assert.That(RutWindow.SnapOrigin(0f, 72f, snap), Is.EqualTo(-36f));

            // Any centre inside the same snap cell lands on the same origin, in both directions.
            float left = RutWindow.SnapOrigin(-20f, 72f, snap);
            float right = RutWindow.SnapOrigin(-19.9f, 72f, snap);
            Assert.That(right, Is.EqualTo(left));
        }

        [Test]
        public void Scroll_SameSnapCell_ReportsNoScroll()
        {
            var window = RutWindow.Create(72f, 1024);
            // Inside the middle of a snap cell: on the exact grid line any movement crosses a boundary and
            // a scroll is the correct answer, which is a different test.
            window.SnapTo(1f, 1f);
            float originX = window.OriginX;
            float originZ = window.OriginZ;

            float deltaU;
            float deltaV;
            bool scrolled = window.Scroll(1.5f, 0.2f, out deltaU, out deltaV);

            Assert.That(scrolled, Is.False);
            Assert.That(deltaU, Is.EqualTo(0f));
            Assert.That(deltaV, Is.EqualTo(0f));
            Assert.That(window.OriginX, Is.EqualTo(originX));
            Assert.That(window.OriginZ, Is.EqualTo(originZ));
        }

        [Test]
        public void Scroll_MovesWholeSnapSteps_SoTheCopyIsOneToOne()
        {
            var window = RutWindow.Create(72f, 1024);
            window.SnapTo(0f, 0f);
            float startX = window.OriginX;

            float deltaU;
            float deltaV;
            bool scrolled = window.Scroll(40f, 0f, out deltaU, out deltaV);

            Assert.That(scrolled, Is.True);
            Assert.That(deltaV, Is.EqualTo(0f));

            // The shift in UV has to be an exact multiple of SnapTexels / Resolution: anything else means
            // the update pass would sample between texels and blur the ruts away.
            float texelsMoved = deltaU * window.Resolution;
            Assert.That(texelsMoved, Is.EqualTo(System.Math.Round(texelsMoved)).Within(1e-3f));
            Assert.That(texelsMoved % RutWindow.SnapTexels, Is.EqualTo(0f).Within(1e-3f));

            // And the origin moved by the same distance in world metres.
            Assert.That(window.OriginX - startX, Is.EqualTo(deltaU * window.AreaSize).Within(1e-4f));
        }

        [Test]
        public void Scroll_BackAndForth_ReturnsToTheSameCell()
        {
            var window = RutWindow.Create(72f, 1024);
            window.SnapTo(0f, 0f);
            float startX = window.OriginX;

            float deltaU;
            float deltaV;
            window.Scroll(40f, -40f, out deltaU, out deltaV);
            window.Scroll(0f, 0f, out deltaU, out deltaV);

            Assert.That(window.OriginX, Is.EqualTo(startX));
        }

        [Test]
        public void TryWorldToUv_CentreIsHalfAndEdgesAreZeroOrOne()
        {
            var window = RutWindow.Create(72f, 1024);
            window.SnapTo(0f, 0f);

            float u;
            float v;
            Assert.That(window.TryWorldToUv(window.OriginX + 36f, window.OriginZ + 36f, out u, out v), Is.True);
            Assert.That(u, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(v, Is.EqualTo(0.5f).Within(1e-5f));

            Assert.That(window.TryWorldToUv(window.OriginX, window.OriginZ, out u, out v), Is.True);
            Assert.That(u, Is.EqualTo(0f));
            Assert.That(v, Is.EqualTo(0f));

            Assert.That(window.TryWorldToUv(window.OriginX + 72f, window.OriginZ + 72f, out u, out v), Is.True);
            Assert.That(u, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(v, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TryWorldToUv_OutsideTheWindow_IsFalse()
        {
            var window = RutWindow.Create(72f, 1024);
            window.SnapTo(0f, 0f);

            float u;
            float v;
            Assert.That(window.TryWorldToUv(window.OriginX - 1f, window.OriginZ + 10f, out u, out v), Is.False);
            Assert.That(window.TryWorldToUv(window.OriginX + 10f, window.OriginZ + 73f, out u, out v), Is.False);
            Assert.That(window.Contains(window.OriginX - 1f, window.OriginZ), Is.False);
            Assert.That(window.Contains(window.OriginX + 1f, window.OriginZ + 1f), Is.True);
        }

        [Test]
        public void UnconfiguredWindow_IsNotUsable()
        {
            var window = new RutWindow();
            float u;
            float v;

            Assert.That(window.IsUsable, Is.False);
            Assert.That(window.TryWorldToUv(0f, 0f, out u, out v), Is.False);
            Assert.That(window.Contains(0f, 0f), Is.False);
        }
    }
}
