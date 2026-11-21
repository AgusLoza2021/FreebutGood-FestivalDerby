using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the per-wheel mark decision: a gripping tyre is silent, a slide lays a mark, a
    /// brake lock lays one too, the hysteresis band holds a mark through a dip, and the surface picks
    /// the tint and whether it smokes or throws dust.
    /// </summary>
    [TestFixture]
    public sealed class TyreMarkRulesTests
    {
        private static TyreContact Contact(
            float lateral,
            float longitudinal,
            SurfaceId surface = SurfaceId.Street,
            float speed = 20f,
            bool grounded = true)
        {
            return new TyreContact(0f, 0f, lateral, longitudinal, speed, grounded, surface);
        }

        [Test]
        public void Gripping_NoMarkNoSmokeNoDust()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(rules, Contact(0.4f, 0.2f), ref state);

            Assert.That(decision.Marking, Is.False);
            Assert.That(decision.Intensity, Is.EqualTo(0f));
            Assert.That(decision.SmokeRate, Is.EqualTo(0f));
            Assert.That(decision.DustRate, Is.EqualTo(0f));
        }

        [Test]
        public void SidewaysSlide_MarksAndSmokesOnStreet()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(rules, Contact(5f, 0f), ref state);

            // slip 5 on a 2..6 ramp: 0.2 + 0.8 * 0.75 = 0.8.
            Assert.That(decision.Marking, Is.True);
            Assert.That(decision.Intensity, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(decision.SmokeRate, Is.GreaterThan(0f));
            Assert.That(decision.DustRate, Is.EqualTo(0f));
            Assert.That(decision.WidthMeters, Is.GreaterThan(0f));
        }

        [Test]
        public void HardBrakeLock_MarksAtFullIntensity()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            // A locked wheel is pure longitudinal slip; the lateral component is zero.
            TyreMarkDecision decision = TyreMarkRules.Evaluate(rules, Contact(0f, 8f, speed: 12f), ref state);

            Assert.That(decision.Marking, Is.True);
            Assert.That(decision.Intensity, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Hysteresis_HoldsThroughSmallDip()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkRules.Evaluate(rules, Contact(5f, 0f), ref state);
            Assert.That(state.Marking, Is.True, "a strong slide starts the mark");

            // 1.6 sits between SlipOff (1.4) and SlipOn (2.0): the mark must survive it.
            TyreMarkDecision held = TyreMarkRules.Evaluate(rules, Contact(1.6f, 0f), ref state);
            Assert.That(held.Marking, Is.True, "the hysteresis band must hold the mark");
            Assert.That(held.Intensity, Is.GreaterThanOrEqualTo(rules.MinMarkIntensity));
            Assert.That(held.Intensity, Is.GreaterThan(0f));

            // Below SlipOff the wheel really has regained grip.
            TyreMarkDecision released = TyreMarkRules.Evaluate(rules, Contact(1.0f, 0f), ref state);
            Assert.That(released.Marking, Is.False);
            Assert.That(released.Intensity, Is.EqualTo(0f));
        }

        [Test]
        public void Airborne_NoMarkEvenWhenSliding()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(
                rules, Contact(6f, 0f, grounded: false), ref state);

            Assert.That(decision.Marking, Is.False);
            Assert.That(decision.SmokeRate, Is.EqualTo(0f));
        }

        [Test]
        public void StoppedWheel_NoMark()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(
                rules, Contact(6f, 0f, speed: 0.1f), ref state);

            Assert.That(decision.Marking, Is.False);
        }

        [TestCase(SurfaceId.Dirt)]
        [TestCase(SurfaceId.Gravel)]
        [TestCase(SurfaceId.Mud)]
        public void LooseSurface_ThrowsDustNotSmoke(SurfaceId surface)
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(
                rules, Contact(5f, 0f, surface), ref state);

            Assert.That(decision.Marking, Is.True);
            Assert.That(decision.DustRate, Is.GreaterThan(0f));
            Assert.That(decision.SmokeRate, Is.EqualTo(0f));
        }

        [Test]
        public void Snow_MarksButThrowsNothing()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var state = new TyreMarkWheelState();

            TyreMarkDecision decision = TyreMarkRules.Evaluate(
                rules, Contact(5f, 0f, SurfaceId.Snow), ref state);

            Assert.That(decision.Marking, Is.True);
            Assert.That(decision.DustRate, Is.EqualTo(0f));
            Assert.That(decision.SmokeRate, Is.EqualTo(0f));
        }

        [Test]
        public void StreetTintIsDark_DirtTintIsBrown()
        {
            var rules = TyreMarkRules.Default;

            rules.TintFor(SurfaceId.Street, out float streetR, out float streetG, out float streetB);
            Assert.That(streetR, Is.LessThan(0.2f), "rubber is dark");
            Assert.That(streetG, Is.LessThan(0.2f));
            Assert.That(streetB, Is.LessThan(0.2f));

            rules.TintFor(SurfaceId.Dirt, out float dirtR, out float dirtG, out float dirtB);
            Assert.That(dirtR, Is.GreaterThan(dirtG), "a dirt rut is warm brown");
            Assert.That(dirtG, Is.GreaterThan(dirtB));
        }

        [Test]
        public void Deterministic_SameInputsSameBits()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var stateA = new TyreMarkWheelState();
            var stateB = new TyreMarkWheelState();

            TyreMarkDecision a = TyreMarkRules.Evaluate(rules, Contact(3.7f, 1.1f), ref stateA);
            TyreMarkDecision b = TyreMarkRules.Evaluate(rules, Contact(3.7f, 1.1f), ref stateB);

            Assert.That(a.Marking, Is.EqualTo(b.Marking));
            Assert.That(a.Intensity, Is.EqualTo(b.Intensity));
            Assert.That(a.WidthMeters, Is.EqualTo(b.WidthMeters));
            Assert.That(a.SmokeRate, Is.EqualTo(b.SmokeRate));
            Assert.That(a.TintR, Is.EqualTo(b.TintR));
        }
    }
}
