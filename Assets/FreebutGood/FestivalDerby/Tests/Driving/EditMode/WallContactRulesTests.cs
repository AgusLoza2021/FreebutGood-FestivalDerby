using System;
using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// WALL1: a barrier contact must be a glance. The owner's playtest found that hitting a wall "slows
    /// you down a LOT"; the numbers below are the target curve agreed for 25 m/s — a 15 degree graze keeps
    /// 95 % of the speed, 30 degrees keeps 95 % and a 60 degree hit keeps 75 % since catch playtest 1 (were
    /// 88 % and 60 %), and only a near head-on hit is a real stop.
    ///
    /// The second playtest ("still braked a lot") is pinned by the alignment cases at the end: the curve
    /// alone was not enough while the car kept its nose in the barrier.
    ///
    /// Every case here runs the pure rule, so the numbers are the rule's, not a physics step's.
    /// </summary>
    [TestFixture]
    public sealed class WallContactRulesTests
    {
        private const float Speed = 25f;

        // Wall normal pointing at the car, wall plane along X. A velocity built by Approach() drives into
        // the wall (−Z) at the requested angle from the wall plane.
        private const float NormalX = 0f;
        private const float NormalY = 0f;
        private const float NormalZ = 1f;

        private static WallGlanceResult Approach(float degrees)
        {
            float radians = degrees * (float)Math.PI / 180f;
            float vx = Speed * (float)Math.Cos(radians);
            float vz = -Speed * (float)Math.Sin(radians);
            Assert.That(
                WallContactRules.TryResolve(vx, 0f, vz, NormalX, NormalY, NormalZ, out WallGlanceResult result),
                Is.True,
                "a contact that drives into the wall must be resolved");
            return result;
        }

        [TestCase(15f, 0.94f)]
        [TestCase(30f, 0.86f)]
        [TestCase(75f, 0.14f)]
        public void RetainedSpeed_GrazeKeepsTheSpeedTheOwnerAskedFor(float degrees, float minRetained)
        {
            WallGlanceResult result = Approach(degrees);

            Assert.That(result.RetainedFraction, Is.GreaterThanOrEqualTo(minRetained),
                "a {0} degree contact must retain at least {1} of the speed", degrees, minRetained);
        }

        [Test]
        public void RetainedSpeed_SixtyDegreesKeepsThreeQuarters()
        {
            WallGlanceResult result = Approach(60f);

            // Catch playtest 1 raised the 60 degree key from 0.60 to 0.75 ("walls scrub too much").
            Assert.That(result.RetainedFraction, Is.InRange(0.72f, 0.78f),
                "a 60 degree hit keeps three quarters of the speed, not a stop");
        }

        [Test]
        public void RetainedSpeed_PlaytestOneKeys()
        {
            // Catch playtest 1: a 30 degree scrape keeps 95 % and a 60 degree hit 75 %; the curve never rises
            // with the angle, so a harder hit can never keep more than a softer one.
            Assert.That(WallContactRules.RetainedFractionFor(0.5f), Is.EqualTo(0.95f).Within(1e-4f));
            Assert.That(WallContactRules.RetainedFractionFor(0.8660f), Is.EqualTo(0.75f).Within(1e-4f));

            float previous = 1f;
            for (int i = 0; i <= 100; i++)
            {
                float retained = WallContactRules.RetainedFractionFor(i / 100f);
                Assert.That(retained, Is.LessThanOrEqualTo(previous + 1e-6f), "share " + (i / 100f));
                previous = retained;
            }
        }

        [Test]
        public void FreshHit_OnlyABeginningContactAfterASeparationGlances()
        {
            // Catch playtest 1: the glance re-applied on every step of a contact compounded into a brake.
            Assert.That(WallContactRules.IsFreshHit(true, float.PositiveInfinity), Is.True, "the first hit of a run");
            Assert.That(WallContactRules.IsFreshHit(true, float.NaN), Is.True);
            Assert.That(WallContactRules.IsFreshHit(true, WallContactRules.FreshHitSeparationSeconds), Is.True,
                "a re-contact after a real separation is a new hit");
            Assert.That(WallContactRules.IsFreshHit(false, float.PositiveInfinity), Is.False,
                "staying on the barrier is never a hit");
            Assert.That(WallContactRules.IsFreshHit(true, 0.02f), Is.False,
                "a contact re-made one step after the last is the same scrape");
        }

        [Test]
        public void AlignStep_TurnsTheCarInOneHit()
        {
            // The glance now runs once per hit, so one nudge carries the turn the per-step nudges used to.
            Assert.That(WallContactRules.AlignRateStepRadiansPerSecond, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void RetainedSpeed_TangentKissCostsNothing()
        {
            // A car sliding along the barrier at a hair over the wall still keeps its speed: the curve starts
            // at 1 for a pure tangent.
            Assert.That(WallContactRules.RetainedFractionFor(0f), Is.EqualTo(1f));
            Assert.That(Approach(1f).RetainedFraction, Is.GreaterThanOrEqualTo(0.99f));
        }

        [Test]
        public void ResolvedVelocity_SlidesAlongTheWallInsteadOfIntoIt()
        {
            WallGlanceResult result = Approach(30f);

            // The glance keeps the tangential direction: the car keeps travelling along the barrier and
            // only a small outward component is left.
            Assert.That(result.VelocityX, Is.GreaterThan(0f));
            Assert.That(Math.Abs(result.VelocityZ), Is.LessThan(result.VelocityX * 0.2f),
                "the resolved velocity must be almost entirely tangential");
        }

        [Test]
        public void HeadOn_IsARealStopWithASmallBounce()
        {
            WallGlanceResult result = Approach(90f);

            Assert.That(result.RetainedFraction, Is.EqualTo(0f).Within(1e-4f), "a head-on hit loses the speed");
            // A small bounce, outward along the wall normal, never a launch.
            Assert.That(result.VelocityZ, Is.GreaterThan(0f), "the bounce points away from the wall");
            Assert.That(result.VelocityZ, Is.LessThanOrEqualTo(WallContactRules.MaxBounceSpeedMetersPerSecond + 1e-3f));
            Assert.That(Math.Abs(result.VelocityX), Is.LessThan(0.01f));
        }

        [Test]
        public void HeadOnBounce_IsCappedEvenAtAnExtremeSpeed()
        {
            // 60 m/s into the wall: the restitution alone would throw the car back faster than a hit-stop
            // feels right, so the bounce is capped and the stop stays a stop.
            Assert.That(
                WallContactRules.TryResolve(0f, 0f, -60f, NormalX, NormalY, NormalZ, out WallGlanceResult result),
                Is.True);
            Assert.That(result.VelocityZ, Is.LessThanOrEqualTo(WallContactRules.MaxBounceSpeedMetersPerSecond + 1e-3f));
        }

        [Test]
        public void MovingAway_IsNeverRewritten()
        {
            // Driving away from the wall: touching it again must not teleport the car along its tangent.
            Assert.That(WallContactRules.TryResolve(10f, 0f, 4f, NormalX, NormalY, NormalZ, out _), Is.False);
        }

        [Test]
        public void RestingContact_IsLeftAlone()
        {
            // A car parked against the wall has no normal speed to correct; rewriting it every physics tick
            // is exactly the jitter this guard exists to prevent.
            Assert.That(WallContactRules.TryResolve(3f, 0f, -0.05f, NormalX, NormalY, NormalZ, out _), Is.False);
        }

        [Test]
        public void Ground_IsNotAWall()
        {
            // The floor, a road chunk and a landing on a ramp all report a mostly-vertical normal. Treating
            // one as a barrier would stop the car dead on every landing.
            Assert.That(WallContactRules.IsWallContact(false, 0f, 1f, 0f), Is.False);
            Assert.That(WallContactRules.IsWallContact(false, 0f, -0.9f, 0f), Is.False);
            Assert.That(WallContactRules.IsWallContact(false, 1f, 0f, 0f), Is.True);
            Assert.That(WallContactRules.IsWallContact(false, 0.6f, 0.4f, 0.6f), Is.True);
        }

        [Test]
        public void CarOnCar_IsNotAWall()
        {
            // Rival cars are a separate system (ram lift, zone damage, FEEL2 catch-up). The glance must never
            // touch them, so the probe asks this predicate — a body on the other side is never a barrier.
            Assert.That(WallContactRules.IsWallContact(true, 1f, 0f, 0f), Is.False,
                "a contact with a Rigidbody is car-on-car, not a wall");
        }

        [Test]
        public void DegenerateInputs_AreRejected()
        {
            Assert.That(WallContactRules.TryResolve(10f, 0f, -10f, 0f, 0f, 0f, out _), Is.False, "zero normal");
            Assert.That(WallContactRules.TryResolve(0f, 0f, 0f, NormalX, NormalY, NormalZ, out _), Is.False,
                "a car at rest has nothing to glance");
            Assert.That(WallContactRules.TryResolve(float.NaN, 0f, -10f, NormalX, NormalY, NormalZ, out _), Is.False);
        }

        [Test]
        public void Resolve_IsDeterministic()
        {
            Assert.That(
                WallContactRules.TryResolve(21.65f, 0f, -12.5f, NormalX, NormalY, NormalZ, out WallGlanceResult first),
                Is.True);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(
                    WallContactRules.TryResolve(21.65f, 0f, -12.5f, NormalX, NormalY, NormalZ, out WallGlanceResult again),
                    Is.True);
                // Bit-exact, not "close": the host and an offline replay must agree on every contact.
                Assert.That(again.RetainedFraction, Is.EqualTo(first.RetainedFraction));
                Assert.That(again.VelocityX, Is.EqualTo(first.VelocityX));
                Assert.That(again.VelocityZ, Is.EqualTo(first.VelocityZ));
            }
        }

        [Test]
        public void Resolve_AllocatesNothing()
        {
            // The rule runs inside a physics callback on a fixed step, so it must stay free of allocation.
            WallContactRules.TryResolve(21.65f, 0f, -12.5f, NormalX, NormalY, NormalZ, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            float sink = 0f;
            for (int i = 0; i < 10000; i++)
            {
                WallContactRules.TryResolve(21.65f, 0f, -12.5f, NormalX, NormalY, NormalZ, out WallGlanceResult result);
                WallContactRules.AlignYawRateRadiansPerSecond(0.3f, 0.1f, 0.1f, out float rate);
                // Accumulated locally on purpose: boxing a float just to hand it to GC.KeepAlive is the
                // test allocating 24 bytes per iteration, not the rule.
                sink += rate + result.VelocityX;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(sink, Is.GreaterThan(0f));
            Assert.That(allocated, Is.EqualTo(0L));
        }

        [Test]
        public void AlignYaw_TurnsTheCarTowardTheWallTangent()
        {
            // The car points into the wall, so its own tyres scrub the speed the glance just saved. The
            // alignment is what turns "slides along at a reduced speed" into "slides along".
            Assert.That(WallContactRules.AlignYawRateRadiansPerSecond(0.5f, 0f, 0.1f, out float rate), Is.True);
            Assert.That(rate, Is.GreaterThan(0f), "a positive error asks for a positive yaw rate");
            Assert.That(rate, Is.LessThanOrEqualTo(WallContactRules.MaxAlignYawRateRadiansPerSecond));
        }

        [Test]
        public void AlignYaw_IsCappedAndNeverOvershootsInOneTick()
        {
            // A full 90 degree error must still be a nudge, not a spin.
            Assert.That(WallContactRules.AlignYawRateRadiansPerSecond(1.5f, 0f, 0.05f, out float rate), Is.True);
            Assert.That(rate, Is.LessThanOrEqualTo(0.05f + 1e-6f), "one tick moves the rate by at most maxDelta");
        }

        [Test]
        public void AlignYaw_AlreadyAligned_IsNotApplied()
        {
            Assert.That(WallContactRules.AlignYawRateRadiansPerSecond(0.001f, 0.2f, 0.1f, out float rate), Is.False);
            Assert.That(rate, Is.EqualTo(0.2f), "an aligned car keeps whatever yaw rate it had");
        }

        [Test]
        public void AlignToWall_ARealHitAlignsToo()
        {
            // The defect this pins: a hit above the graze threshold used to skip the alignment, the nose
            // stayed pointed into the barrier, and the tyre model spent the speed the glance had just
            // preserved. The alignment is what makes a scrape a slide instead of a brake.
            Assert.That(WallContactRules.ShouldAlignToWall(15f, 0.35f), Is.True,
                "a 15 m/s into-wall hit still has to be turned along the barrier");
            Assert.That(WallContactRules.ShouldAlignToWall(2f, 0.35f), Is.True, "a graze keeps aligning");
        }

        [Test]
        public void AlignToWall_StandsDownForASpinOrAStalledContact()
        {
            // Travelling backwards along the barrier: turning the car would be a spin, not a glance.
            Assert.That(WallContactRules.ShouldAlignToWall(15f, WallContactRules.MaxAlignErrorRadians + 0.01f),
                Is.False);
            // Below the resolved-contact floor there is nothing left to align to.
            Assert.That(WallContactRules.ShouldAlignToWall(WallContactRules.MinNormalSpeedMetersPerSecond - 0.01f, 0.1f),
                Is.False);
            Assert.That(WallContactRules.ShouldAlignToWall(float.NaN, 0.1f), Is.False);
        }

        [Test]
        public void Graze_OnlyALightContactIsAGraze()
        {
            // Above this normal speed the contact is a real hit. Informational only: the alignment is
            // decided separately (see AlignToWall), so this flag carries no yaw contract any more.
            Assert.That(WallContactRules.IsGraze(2f), Is.True);
            Assert.That(WallContactRules.IsGraze(WallContactRules.GrazeNormalSpeedMetersPerSecond - 0.01f), Is.True);
            Assert.That(WallContactRules.IsGraze(WallContactRules.GrazeNormalSpeedMetersPerSecond), Is.False);
            Assert.That(WallContactRules.IsGraze(20f), Is.False);
        }
    }
}
