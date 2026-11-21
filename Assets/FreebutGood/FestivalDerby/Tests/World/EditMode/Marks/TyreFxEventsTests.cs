using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the per-tick burst list: a sliding Street tyre yields a smoke burst, a loose surface
    /// yields dust, the list is capped instead of growing, and Clear resets it in place.
    /// </summary>
    [TestFixture]
    public sealed class TyreFxEventsTests
    {
        private static TyreMarkDecision Mark(SurfaceId surface, float smoke, float dust)
        {
            return new TyreMarkDecision(true, 0.8f, 0.3f, 0.12f, 0.12f, 0.13f, smoke, dust, surface);
        }

        [Test]
        public void StreetSlide_YieldsSmoke()
        {
            var fx = new TyreFxEvents(8);
            fx.Submit(Mark(SurfaceId.Street, 12f, 0f), 1f, 2f);

            Assert.That(fx.Count, Is.EqualTo(1));
            TyreFxBurst burst = fx.At(0);
            Assert.That(burst.Kind, Is.EqualTo(TyreFxKind.Smoke));
            Assert.That(burst.Rate, Is.EqualTo(12f));
            Assert.That(burst.X, Is.EqualTo(1f));
            Assert.That(burst.Z, Is.EqualTo(2f));
        }

        [Test]
        public void DirtSlide_YieldsDust()
        {
            var fx = new TyreFxEvents(8);
            fx.Submit(Mark(SurfaceId.Dirt, 0f, 18f), 0f, 0f);

            Assert.That(fx.Count, Is.EqualTo(1));
            Assert.That(fx.At(0).Kind, Is.EqualTo(TyreFxKind.Dust));
            Assert.That(fx.At(0).Surface, Is.EqualTo(SurfaceId.Dirt));
        }

        [Test]
        public void GrippingWheel_YieldsNothing()
        {
            var fx = new TyreFxEvents(8);
            int added = fx.Submit(Mark(SurfaceId.Street, 0f, 0f), 0f, 0f);

            Assert.That(added, Is.EqualTo(0));
            Assert.That(fx.Count, Is.EqualTo(0));
        }

        [Test]
        public void FullList_DropsTheExtraBurst()
        {
            var fx = new TyreFxEvents(2);

            Assert.That(fx.Add(0f, 0f, 5f, SurfaceId.Street, TyreFxKind.Smoke), Is.True);
            Assert.That(fx.Add(0f, 0f, 5f, SurfaceId.Street, TyreFxKind.Smoke), Is.True);
            Assert.That(fx.Add(0f, 0f, 5f, SurfaceId.Street, TyreFxKind.Smoke), Is.False);
            Assert.That(fx.Count, Is.EqualTo(2), "the cap holds");
        }

        [Test]
        public void NonPositiveRate_IsIgnored()
        {
            var fx = new TyreFxEvents(2);
            Assert.That(fx.Add(0f, 0f, 0f, SurfaceId.Street, TyreFxKind.Smoke), Is.False);
            Assert.That(fx.Count, Is.EqualTo(0));
        }

        [Test]
        public void Clear_ResetsCountInPlace()
        {
            var fx = new TyreFxEvents(4);
            fx.Submit(Mark(SurfaceId.Street, 12f, 0f), 0f, 0f);
            fx.Clear();

            Assert.That(fx.Count, Is.EqualTo(0));
            Assert.That(fx.Capacity, Is.EqualTo(4));
        }
    }
}
