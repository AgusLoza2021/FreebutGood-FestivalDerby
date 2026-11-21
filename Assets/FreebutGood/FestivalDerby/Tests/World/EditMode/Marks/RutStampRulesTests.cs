using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the rut decision: the one that separates ruts from skid marks — a wheel that is only
    /// ROLLING still presses a rut on loose ground — plus the cases that must never stamp, and the grip
    /// round-trip the driving layer's single float has to survive.
    /// </summary>
    [TestFixture]
    public sealed class RutStampRulesTests
    {
        /// <summary>What SnowRutSystem builds: a wider mark table so a rut reads as 0.45 m on snow.</summary>
        private static readonly TyreMarkRules RutRules = new TyreMarkRules(markWidthMeters: 0.45f);

        private static TyreContact Contact(
            float lateral,
            float longitudinal,
            SurfaceId surface = SurfaceId.Snow,
            float speed = 20f,
            bool grounded = true)
        {
            return new TyreContact(0f, 0f, lateral, longitudinal, speed, grounded, surface);
        }

        private static RutStampDecision Decide(TyreContact contact)
        {
            var state = new TyreMarkWheelState();
            TyreMarkDecision mark = TyreMarkRules.Evaluate(RutRules, contact, ref state);
            return RutStampRules.Decide(RutRules, contact, mark);
        }

        [Test]
        public void RollingWheelOnSnow_StampsEvenWithNoSlip()
        {
            // The point of the whole feature: grip, straight line, still a rut.
            RutStampDecision decision = Decide(Contact(0f, 0f));

            Assert.That(decision.Stamping, Is.True);
            Assert.That(decision.Surface, Is.EqualTo(SurfaceId.Snow));
            Assert.That(decision.WidthMeters, Is.EqualTo(0.45f).Within(1e-5f));
            Assert.That(decision.Depth, Is.EqualTo(RutStampRules.RollingDepth).Within(1e-5f));
        }

        [Test]
        public void RollingWheelOnDirt_StampsWider()
        {
            // Sand is Dirt in this road bake: the loose surface shares its numbers.
            RutStampDecision decision = Decide(Contact(0f, 0f, SurfaceId.Dirt));

            Assert.That(decision.Stamping, Is.True);
            Assert.That(decision.WidthMeters, Is.EqualTo(0.45f * 1.30f).Within(1e-5f));
        }

        [Test]
        public void SlidingWheel_DigsDeeperThanRolling()
        {
            RutStampDecision rolling = Decide(Contact(0f, 0f));
            RutStampDecision sliding = Decide(Contact(6f, 0f));

            Assert.That(sliding.Stamping, Is.True);
            Assert.That(sliding.Depth, Is.LessThan(rolling.Depth));
            Assert.That(sliding.Depth, Is.GreaterThanOrEqualTo(RutStampRules.DiggingDepth));
        }

        [Test]
        public void HardSlip_ClampsToTheFloorOfTheMask()
        {
            RutStampDecision decision = Decide(Contact(40f, 0f));

            Assert.That(decision.Depth, Is.EqualTo(RutStampRules.DiggingDepth).Within(1e-4f));
        }

        [Test]
        public void AirborneWheel_NeverStamps()
        {
            Assert.That(Decide(Contact(0f, 0f, grounded: false)).Stamping, Is.False);
            Assert.That(Decide(Contact(8f, 0f, grounded: false)).Stamping, Is.False);
        }

        [Test]
        public void SlowWheel_NeverStamps()
        {
            // Below the mark table's own floor: a parked car does not keep drilling the same patch.
            Assert.That(Decide(Contact(0f, 0f, speed: 0.1f)).Stamping, Is.False);
        }

        [Test]
        public void HardSurfaces_NeverStamp()
        {
            Assert.That(Decide(Contact(0f, 0f, SurfaceId.Street)).Stamping, Is.False);
            Assert.That(Decide(Contact(8f, 0f, SurfaceId.Gravel)).Stamping, Is.False);
            Assert.That(Decide(Contact(8f, 0f, SurfaceId.Mud)).Stamping, Is.False);
        }

        [Test]
        public void IsRutSurface_CoversLooseGroundOnly()
        {
            Assert.That(RutStampRules.IsRutSurface(SurfaceId.Snow), Is.True);
            Assert.That(RutStampRules.IsRutSurface(SurfaceId.Dirt), Is.True);
            Assert.That(RutStampRules.IsRutSurface(SurfaceId.Mud), Is.False);
            Assert.That(RutStampRules.IsRutSurface(SurfaceId.Gravel), Is.False);
            Assert.That(RutStampRules.IsRutSurface(SurfaceId.Street), Is.False);
        }

        [Test]
        public void DepthBand_IsOrderedSoDeeperMeansDarker()
        {
            // The mask is white-is-intact, so the digging value must be the SMALLER one.
            Assert.That(RutStampRules.DiggingDepth, Is.LessThan(RutStampRules.RollingDepth));
            Assert.That(RutStampRules.DiggingDepth, Is.GreaterThan(0f));
            Assert.That(RutStampRules.RollingDepth, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void SurfaceFromGrip_MatchesTheStockTable()
        {
            SurfaceTable table = SurfaceTable.Defaults;

            Assert.That(RutStampRules.SurfaceFromGrip(table, 1f), Is.EqualTo(SurfaceId.Street));
            Assert.That(RutStampRules.SurfaceFromGrip(table, 0.82f), Is.EqualTo(SurfaceId.Dirt));
            Assert.That(RutStampRules.SurfaceFromGrip(table, 0.55f), Is.EqualTo(SurfaceId.Snow));
            Assert.That(RutStampRules.SurfaceFromGrip(table, 0.68f), Is.EqualTo(SurfaceId.Mud));
            Assert.That(RutStampRules.SurfaceFromGrip(table, 0.76f), Is.EqualTo(SurfaceId.Gravel));
        }

        [Test]
        public void SurfaceFromGrip_NearestWins_AndNoTableIsStreet()
        {
            SurfaceTable table = SurfaceTable.Defaults;

            // 0.60 is closer to Snow's 0.55 than to Mud's 0.68.
            Assert.That(RutStampRules.SurfaceFromGrip(table, 0.60f), Is.EqualTo(SurfaceId.Snow));
            // A grip above the top of the table still resolves, to the nearest (Street).
            Assert.That(RutStampRules.SurfaceFromGrip(table, 1.4f), Is.EqualTo(SurfaceId.Street));
            // No table means no ruts rather than a default that would stamp everywhere.
            Assert.That(RutStampRules.SurfaceFromGrip(null, 0.55f), Is.EqualTo(SurfaceId.Street));
        }
    }
}
