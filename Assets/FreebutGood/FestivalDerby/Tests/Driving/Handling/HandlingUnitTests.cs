using System;
using FestivalDerby.Driving.Handling;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// Unit coverage for the individual models. The acceptance fixture judges the car as a whole; these
    /// tests pin the contracts the whole depends on, so a later retune that breaks one rule fails on
    /// the rule instead of on a lap time.
    /// </summary>
    [TestFixture]
    public class HandlingUnitTests
    {
        private const float Dt = 0.02f;

        private static float Deg(float radians)
        {
            return radians * HandlingParams.DegreesPerRadian;
        }

        private static float Rad(float degrees)
        {
            return degrees * HandlingParams.RadiansPerDegree;
        }

        // ------------------------------------------------------------------------------- the presets

        [Test]
        public void Presets_CarryTheValuesFromTheSpecTable()
        {
            HandlingParams grippy = HandlingParams.Grippy;
            HandlingParams balanced = HandlingParams.Balanced;
            HandlingParams drifty = HandlingParams.Drifty;

            Assert.That(grippy.MuPeak, Is.EqualTo(2.6f));
            Assert.That(balanced.MuPeak, Is.EqualTo(2.0f));
            Assert.That(drifty.MuPeak, Is.EqualTo(1.5f));

            Assert.That(grippy.RearAxleCurve.Evaluate(1f), Is.EqualTo(0.5f));
            Assert.That(balanced.RearAxleCurve.Evaluate(1f), Is.EqualTo(0.35f));
            Assert.That(drifty.RearAxleCurve.Evaluate(1f), Is.EqualTo(0.22f));

            Assert.That(Deg(grippy.BetaMaxRadians), Is.EqualTo(15f).Within(0.01f));
            Assert.That(Deg(balanced.BetaMaxRadians), Is.EqualTo(22f).Within(0.01f));
            Assert.That(Deg(drifty.BetaMaxRadians), Is.EqualTo(30f).Within(0.01f));

            Assert.That(grippy.LateralAccelBudgetMetersPerSecondSquared, Is.EqualTo(18f));
            Assert.That(balanced.LateralAccelBudgetMetersPerSecondSquared, Is.EqualTo(16f));
            Assert.That(drifty.LateralAccelBudgetMetersPerSecondSquared, Is.EqualTo(13f));

            Assert.That(grippy.AssistGain, Is.EqualTo(8f));
            Assert.That(balanced.AssistGain, Is.EqualTo(6f));
            Assert.That(drifty.AssistGain, Is.EqualTo(3f));

            Assert.That(grippy.HeadingGain, Is.EqualTo(5f));
            Assert.That(balanced.HeadingGain, Is.EqualTo(4f));
            // FP0 lifted Drifty from the spec table's 3 to the 4/s floor: the clamp is a hard floor on
            // every preset, so the slide preset cannot ship below it either.
            Assert.That(drifty.HeadingGain, Is.EqualTo(4f));

            // FEEL2 (2026-10-15 playtest) took the launch samples up: the owner asked for the fast car back,
            // and the pedal at rest is what has to reach 20 m/s in about a second.
            Assert.That(grippy.ThrottleCurve.Evaluate(0f), Is.EqualTo(0.8f));
            Assert.That(balanced.ThrottleCurve.Evaluate(0f), Is.EqualTo(0.85f));
            Assert.That(drifty.ThrottleCurve.Evaluate(0f), Is.EqualTo(0.9f));

            // Shared body of the table.
            Assert.That(balanced.CommitEnterRadians, Is.EqualTo(Rad(150f)).Within(0.0001f));
            Assert.That(balanced.CommitExitRadians, Is.EqualTo(Rad(110f)).Within(0.0001f));
            Assert.That(balanced.DeadZone, Is.EqualTo(0.12f));
            // V2e.1 retune: 30 ms so the stick reaches the mapper in half the old time.
            Assert.That(balanced.StickLowPassSeconds, Is.EqualTo(0.03f));
            Assert.That(Deg(balanced.MaxYawRateRadiansPerSecond), Is.EqualTo(150f).Within(0.01f));
            Assert.That(Deg(balanced.SteerRateRadiansPerSecond), Is.EqualTo(180f).Within(0.01f));
            Assert.That(Deg(balanced.SteerMaxRadians), Is.EqualTo(32f).Within(0.01f));
            Assert.That(Deg(balanced.SteerMinRadians), Is.EqualTo(5f).Within(0.01f));
            Assert.That(balanced.GuardYawGain, Is.EqualTo(10f));
            Assert.That(balanced.AirGravityScale, Is.EqualTo(1.3f));
        }

        [Test]
        public void AxleCurves_MatchTheSpecControlPoints()
        {
            Piecewise4 front = HandlingParams.Balanced.FrontAxleCurve;
            Assert.That(front.Evaluate(0f), Is.EqualTo(1f));
            Assert.That(front.Evaluate(0.15f), Is.EqualTo(1f));
            Assert.That(front.Evaluate(0.4f), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(front.Evaluate(1f), Is.EqualTo(0.45f));

            Piecewise4 rear = HandlingParams.Balanced.RearAxleCurve;
            Assert.That(rear.Evaluate(0f), Is.EqualTo(1f));
            Assert.That(rear.Evaluate(0.12f), Is.EqualTo(1f));
            Assert.That(rear.Evaluate(0.35f), Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(rear.Evaluate(1f), Is.EqualTo(0.35f));

            // Halfway between two control points is halfway between their values: no easing, no wrap.
            Assert.That(rear.Evaluate(0.675f), Is.EqualTo((0.65f + 0.35f) * 0.5f).Within(0.0001f));
        }

        // ------------------------------------------------------------------------------ intent mapping

        [Test]
        public void Throttle_IsNeverNegative_ForEveryStickBearing()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            float minimum = 1f;
            for (int i = 0; i < 360; i++)
            {
                float bearing = Rad(i);
                float throttle = IntentMapper.Evaluate(
                    (float)Math.Sin(bearing),
                    (float)Math.Cos(bearing),
                    0f,
                    0f,
                    0f,
                    Dt,
                    p,
                    ref state).Throttle;

                Assert.That(throttle, Is.GreaterThanOrEqualTo(0f), "negative throttle at bearing " + i);
                if (throttle < minimum)
                {
                    minimum = throttle;
                }
            }

            // A U-turn bearing still drives forward, just less: that is the whole "never brake" rule.
            // FEEL2 deepened the lift from 10% to 4% of the pedal because accelMax doubled: at 10% the old
            // coast-down-through-the-arc behaviour was gone (the U-turn held speed and would not rotate).
            // The rule the number defends is "never zero", so the bound follows the deeper lift.
            Assert.That(minimum, Is.GreaterThan(0.03f), "a bearing asked for almost no throttle");
        }

        [Test]
        public void Throttle_IsFullAtTheRingAndZeroInsideTheDeadZone()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            for (int i = 0; i < 40; i++)
            {
                IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, p, ref state);
            }

            float atRing = IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, p, ref state).Throttle;
            Assert.That(atRing, Is.GreaterThan(0.99f));

            state.Reset();
            IntentOutput inside = IntentMapper.Evaluate(0.05f, 0.05f, 0f, 0f, 0f, Dt, p, ref state);
            Assert.That(inside.HasIntent, Is.False);
            Assert.That(inside.Throttle, Is.EqualTo(0f));
            Assert.That(inside.UTurnFactor, Is.EqualTo(0f));
        }

        [Test]
        public void Commitment_HoldsTheTurnSignThroughPlusMinus180Jitter()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            sbyte locked = 0;
            int committedTicks = 0;

            for (int i = 0; i < 400; i++)
            {
                // The pathological case from the design doc: a stick sitting dead astern sampling to
                // either side of the wrap, which used to flip the requested turn every frame.
                float bearing = i % 2 == 0 ? Rad(179f) : Rad(-179f);
                IntentOutput o = IntentMapper.Evaluate(
                    (float)Math.Sin(bearing),
                    (float)Math.Cos(bearing),
                    0f,
                    0f,
                    0f,
                    Dt,
                    p,
                    ref state);

                Assert.That(o.Committed, Is.True, "the turn should be committed past 150 deg");
                committedTicks++;
                if (locked == 0)
                {
                    locked = o.TurnSign;
                }

                Assert.That(o.TurnSign, Is.EqualTo(locked), "the committed turn sign changed");
                Assert.That(o.ErrorRadians * locked, Is.GreaterThan(0f),
                    "a committed error must keep pointing the locked way");
                Assert.That(Math.Abs(Deg(o.ErrorRadians)), Is.GreaterThan(150f), "committed error shrank under 150 deg");
            }

            Assert.That(committedTicks, Is.EqualTo(400));
        }

        [Test]
        public void Commitment_ReleasesUnderTheUnlockAngle()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();

            // Lock on a genuine 180 degree error first.
            IntentOutput locked = IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, p, ref state);
            Assert.That(locked.Committed, Is.True);

            // Then hand it the stick straight ahead and let the filter bring it around; the lock has
            // to drop out somewhere on the way, and the error then follows the stick again.
            bool released = false;
            IntentOutput o = locked;
            for (int i = 0; i < 40; i++)
            {
                o = IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, p, ref state);
                if (!o.Committed)
                {
                    released = true;
                }
            }

            Assert.That(released, Is.True, "the lock should release under 110 deg");
            Assert.That(Math.Abs(Deg(o.ErrorRadians)), Is.LessThan(10f), "a released mapper follows the stick again");
        }

        [Test]
        public void UTurnLift_EngagesTowardsDeadAstern()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            for (int i = 0; i < 60; i++)
            {
                // At speed: the lift is the fast-arc tool, so the engagement case runs there.
                IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, 15f, p, ref state);
            }

            float straight = IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, 15f, p, ref state).Throttle;

            state.Reset();
            for (int i = 0; i < 60; i++)
            {
                IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, 15f, p, ref state);
            }

            IntentOutput uTurn = IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, 15f, p, ref state);
            Assert.That(uTurn.UTurnFactor, Is.GreaterThan(0.99f));
            Assert.That(uTurn.Throttle, Is.EqualTo(straight * p.UTurnThrottleScale).Within(0.01f));
            Assert.That(uTurn.Throttle, Is.GreaterThan(0f));
        }

        // Drive rescue S1/S3: the U-turn lift exists to tighten a fast arc, which is a rationale that
        // vanishes at walking pace. A car that just hit a wall sits near 0 m/s facing the wrong way;
        // the lift must not throttle it down to 4% there, or the owner's "stuck against a wall, the
        // car will not respond" is exactly what ships.
        [Test]
        public void UTurnLift_DoesNotApplyAtNearStandstill()
        {
            HandlingParams p = HandlingParams.Balanced;
            Assert.That(p.UTurnMinSpeedMetersPerSecond, Is.GreaterThan(0f), "the speed floor is not configured");

            var state = new IntentState();
            for (int i = 0; i < 60; i++)
            {
                IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, 0f, p, ref state);
            }

            float straight = IntentMapper.Evaluate(0f, 1f, 0f, 0f, 0f, Dt, 0f, p, ref state).Throttle;

            state.Reset();
            for (int i = 0; i < 60; i++)
            {
                IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, 0f, p, ref state);
            }

            IntentOutput atStandstill = IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, 0f, p, ref state);
            Assert.That(atStandstill.UTurnFactor, Is.GreaterThan(0.99f), "the standstill case is not a U-turn");
            Assert.That(atStandstill.Throttle, Is.EqualTo(straight).Within(0.01f),
                "the lift throttled a car that is not moving");

            IntentOutput atSpeed = IntentMapper.Evaluate(0f, -1f, 0f, 0f, 0f, Dt, 15f, p, ref state);
            Assert.That(atSpeed.Throttle, Is.EqualTo(straight * p.UTurnThrottleScale).Within(0.01f),
                "the lift no longer tightens a fast U-turn");
        }

        // ------------------------------------------------------------------------------------ steering

        [Test]
        public void SteerMax_ShrinksWithSpeed()
        {
            HandlingParams p = HandlingParams.Balanced;
            float slow = Deg(SteeringModel.SteerMaxFor(5f, p));
            float mid = Deg(SteeringModel.SteerMaxFor(15f, p));
            float fast = Deg(SteeringModel.SteerMaxFor(30f, p));

            Assert.That(slow, Is.GreaterThan(mid));
            Assert.That(mid, Is.GreaterThan(fast));
            Assert.That(slow, Is.LessThanOrEqualTo(32f));
            Assert.That(fast, Is.GreaterThanOrEqualTo(5f));
        }

        [Test]
        public void YawTarget_IsClampedToTheLateralBudget()
        {
            HandlingParams p = HandlingParams.Balanced;
            float yawTarget;
            float steerMax;
            float steerCommand;

            for (int speedIndex = 0; speedIndex < 5; speedIndex++)
            {
                float speed = 5f + speedIndex * 5f;
                SteeringModel.Evaluate(Rad(179f), speed, 0f, 0f, Dt, p, out yawTarget, out steerMax, out steerCommand);
                float allowed = Math.Min(p.MaxYawRateRadiansPerSecond, p.LateralAccelBudgetMetersPerSecondSquared / speed);
                Assert.That(Math.Abs(yawTarget), Is.LessThanOrEqualTo(allowed + 0.0001f), "yaw target past the budget");
                Assert.That(Math.Abs(steerCommand), Is.LessThanOrEqualTo(steerMax + 0.0001f), "steer command past lock");
            }
        }

        [Test]
        public void SteerAngle_MovesAtTheRateLimit()
        {
            HandlingParams p = HandlingParams.Balanced;
            float yawTarget;
            float steerMax;
            float steerCommand;
            float angle = 0f;
            float previous = 0f;
            for (int i = 0; i < 20; i++)
            {
                angle = SteeringModel.Evaluate(Rad(170f), 10f, 0f, angle, Dt, p, out yawTarget, out steerMax, out steerCommand);
                Assert.That(Math.Abs(angle - previous), Is.LessThanOrEqualTo(p.SteerRateRadiansPerSecond * Dt + 0.0001f));
                previous = angle;
            }

            Assert.That(Math.Abs(angle), Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------------------------------- lateral

        [Test]
        public void LateralForce_IsCappedByMuTimesAxleLoad()
        {
            HandlingParams p = HandlingParams.Balanced;
            float load = 6000f;
            float forceX;
            float forceY;
            float slip;
            float mu;

            // A small side slip: the curve is still at full grip, so the request passes through.
            LateralModel.Evaluate(20f, 1.5f, 0f, 0f, load, 1f, Dt, p.RearAxleCurve, p, out forceX, out forceY, out slip, out mu);
            Assert.That(mu, Is.EqualTo(1f), "a small slip is full grip");
            Assert.That(slip, Is.LessThan(0.12f));
            Assert.That(forceY, Is.LessThan(0f), "a leftward slide is answered rightward");
            float requested = Math.Abs(forceY);

            // A vicious side slip: the request is enormous, so the cap is what comes out.
            LateralModel.Evaluate(20f, 25f, 0f, 0f, load, 1f, Dt, p.RearAxleCurve, p, out forceX, out forceY, out slip, out mu);
            Assert.That(Math.Abs(forceY), Is.LessThanOrEqualTo(p.MuPeak * mu * load + 1f));
            Assert.That(Math.Abs(forceY), Is.EqualTo(p.MuPeak * mu * load).Within(1f), "the cap has to bind");
            Assert.That(Math.Abs(forceY), Is.LessThan(requested), "more slip must not mean more force");
            Assert.That(forceY, Is.LessThan(0f), "a leftward slide must be answered rightward");
            Assert.That(slip, Is.GreaterThan(0.5f));
        }

        [Test]
        public void LateralForce_FallsOffWhenTheRearSlides()
        {
            HandlingParams p = HandlingParams.Balanced;
            float forceX;
            float forceY;
            float slip;
            float mu;

            // Grip available at the rear drops as the slip ratio grows, which is what lets a car
            // rotate instead of plowing.
            LateralModel.Evaluate(20f, 1.2f, 0f, PlanarCarSim.RearAxleX, 6000f, 1f, Dt, p.RearAxleCurve, p, out forceX, out forceY, out slip, out mu);
            float small = mu;

            LateralModel.Evaluate(20f, 12f, 0f, PlanarCarSim.RearAxleX, 6000f, 1f, Dt, p.RearAxleCurve, p, out forceX, out forceY, out slip, out mu);
            float large = mu;

            Assert.That(small, Is.GreaterThan(large), "the rear grip curve should fall with slip");
        }

        [Test]
        public void LateralForce_IsZeroWhenNothingIsSliding()
        {
            HandlingParams p = HandlingParams.Balanced;
            float forceX;
            float forceY;
            float slip;
            float mu;
            LateralModel.Evaluate(20f, 0f, 0f, PlanarCarSim.RearAxleX, 6000f, 1f, Dt, p.RearAxleCurve, p, out forceX, out forceY, out slip, out mu);
            Assert.That(Math.Abs(forceY), Is.LessThan(0.001f));
            Assert.That(slip, Is.EqualTo(0f));
        }

        // ---------------------------------------------------------------------------------- stabilizer

        [Test]
        public void Assist_FadesToZeroInASlideAndGainsNothingAtACrawl()
        {
            HandlingParams p = HandlingParams.Balanced;
            float fade;
            float assist;
            float damping;

            YawStabilizer.Evaluate(20f, 0f, 1f, 0f, 2088f, p, out fade, out assist, out damping);
            Assert.That(fade, Is.EqualTo(1f).Within(0.0001f), "full assist at speed with the rear planted");
            Assert.That(assist, Is.GreaterThan(0f));
            Assert.That(damping, Is.EqualTo(0f));

            YawStabilizer.Evaluate(0.5f, 0f, 1f, 0f, 2088f, p, out fade, out assist, out damping);
            Assert.That(fade, Is.EqualTo(0f), "no assist while crawling");

            YawStabilizer.Evaluate(20f, 0f, 1f, 0.6f, 2088f, p, out fade, out assist, out damping);
            Assert.That(fade, Is.EqualTo(0f), "the assist must be gone once the rear is sliding");

            YawStabilizer.Evaluate(20f, 0.5f, 0f, 0f, 2088f, p, out fade, out assist, out damping);
            Assert.That(damping, Is.LessThan(0f), "yaw damping always opposes the yaw rate");
        }

        // ------------------------------------------------------------------------------- sideslip guard

        [Test]
        public void SideslipGuard_DoesNothingBelowBetaMax()
        {
            HandlingParams p = HandlingParams.Balanced;
            float beta;
            float excess;
            float boost;
            float torque = SideslipGuard.Evaluate(20f, -3f, 2088f, 4, p, out beta, out excess, out boost);
            Assert.That(torque, Is.EqualTo(0f));
            Assert.That(excess, Is.EqualTo(0f));
            Assert.That(boost, Is.EqualTo(1f));
            Assert.That(Deg(beta), Is.LessThan(0f).And.GreaterThan(-22f));
        }

        [Test]
        public void SideslipGuard_TorquesTowardTheVelocityVector()
        {
            HandlingParams p = HandlingParams.Balanced;
            float beta;
            float excess;
            float boost;

            // Car sliding to its own right: the nose has to come right (negative yaw) to line up.
            float rightSlide = SideslipGuard.Evaluate(20f, -10f, 2088f, 4, p, out beta, out excess, out boost);
            Assert.That(Deg(beta), Is.EqualTo(-26.57f).Within(0.1f));
            Assert.That(Deg(excess), Is.EqualTo(4.57f).Within(0.1f));
            Assert.That(rightSlide, Is.LessThan(0f));
            Assert.That(boost, Is.GreaterThan(1f));

            // Car sliding to its own left: mirror image.
            float leftSlide = SideslipGuard.Evaluate(20f, 10f, 2088f, 4, p, out beta, out excess, out boost);
            Assert.That(leftSlide, Is.GreaterThan(0f));
            Assert.That(Math.Abs(leftSlide), Is.EqualTo(Math.Abs(rightSlide)).Within(0.001f));
        }

        [Test]
        public void SideslipGuard_StaysOffBelowTheGuardSpeed()
        {
            HandlingParams p = HandlingParams.Balanced;
            float beta;
            float excess;
            float boost;
            float torque = SideslipGuard.Evaluate(3f, -3f, 2088f, 4, p, out beta, out excess, out boost);
            Assert.That(torque, Is.EqualTo(0f), "no guard at parking speed");
            Assert.That(boost, Is.EqualTo(1f));

            float leftBeta;
            float leftExcess;
            float leftBoost;
            float grounded = SideslipGuard.Evaluate(20f, -10f, 2088f, 0, p, out leftBeta, out leftExcess, out leftBoost);
            Assert.That(grounded, Is.EqualTo(0f), "no guard with no wheels on the ground");
        }

        // -------------------------------------------------------------------------------- longitudinal

        [Test]
        public void Brake_NeverPushesTheCarBackwards()
        {
            HandlingParams p = HandlingParams.Balanced;
            float curve;
            float resistance;
            for (int i = 0; i < 6; i++)
            {
                float speed = 0.1f + i * 2f;
                float smoothed = 1f;
                float force = LongitudinalModel.Evaluate(0f, speed, Dt, true, false, 1f, 1f, 4, 1200f, p, ref smoothed, out curve, out resistance);
                Assert.That(force, Is.LessThanOrEqualTo(0f), "braking should oppose the motion");
                float nextSpeed = speed + force / 1200f * Dt;
                Assert.That(nextSpeed, Is.GreaterThanOrEqualTo(0f), "the brake reversed the car");
            }
        }

        [Test]
        public void Drive_IsSoftAtRestAndStrongerInTheMidRange()
        {
            HandlingParams p = HandlingParams.Balanced;
            float curve;
            float resistance;

            float smoothed = 1f;
            float atRest = LongitudinalModel.Evaluate(1f, 0f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothed, out curve, out resistance);

            smoothed = 1f;
            float midRange = LongitudinalModel.Evaluate(1f, 9f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothed, out curve, out resistance);

            smoothed = 1f;
            float atSpeed = LongitudinalModel.Evaluate(1f, 30f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothed, out curve, out resistance);

            Assert.That(atRest, Is.LessThan(midRange), "launch has to be softer than the mid range");
            Assert.That(atSpeed, Is.LessThan(midRange), "drive has to fall off at the top end");
            Assert.That(atRest, Is.GreaterThan(0f));
        }

        [Test]
        public void Drive_DropsToAQuarterWithFewerThanThreeWheelsGrounded()
        {
            HandlingParams p = HandlingParams.Balanced;
            float curve;
            float resistanceAll;
            float resistanceTwo;
            float smoothedA = 1f;
            float smoothedB = 1f;
            float allWheels = LongitudinalModel.Evaluate(1f, 10f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothedA, out curve, out resistanceAll);
            float twoWheels = LongitudinalModel.Evaluate(1f, 10f, Dt, false, false, 1f, 1f, 2, 1200f, p, ref smoothedB, out curve, out resistanceTwo);

            // The coast term is not part of the drive scale, so compare the drive components.
            float driveAll = allWheels - resistanceAll;
            float driveTwo = twoWheels - resistanceTwo;
            Assert.That(driveTwo / driveAll, Is.EqualTo(0.25f).Within(0.01f));
        }

        // ----------------------------------------------------------------------------------------- air

        [Test]
        public void Air_GroundedIsNeutralAndAirborneSteers()
        {
            HandlingParams p = HandlingParams.Balanced;
            float gravityScale;
            float yawTorque;
            float rollTorque;

            AirModel.Evaluate(false, false, 1f, 0.3f, 1f, 1f, 2088f, 1200f, p, out gravityScale, out yawTorque, out rollTorque);
            Assert.That(gravityScale, Is.EqualTo(1f));
            Assert.That(yawTorque, Is.EqualTo(0f));
            Assert.That(rollTorque, Is.EqualTo(0f));

            AirModel.Evaluate(true, true, 0f, 0f, 0f, 0f, 2088f, 1200f, p, out gravityScale, out yawTorque, out rollTorque);
            Assert.That(gravityScale, Is.EqualTo(1.3f), "descent pulls harder than a static fall");

            AirModel.Evaluate(true, true, 1f, 0f, 0f, 0f, 2088f, 1200f, p, out gravityScale, out yawTorque, out rollTorque);
            Assert.That(yawTorque, Is.GreaterThan(0f), "a held stick steers in the air");

            AirModel.Evaluate(true, true, 0f, 0.4f, 0f, 0f, 2088f, 1200f, p, out gravityScale, out yawTorque, out rollTorque);
            Assert.That(rollTorque, Is.LessThan(0f), "auto-level rolls a tilted car back upright");
        }

        // --------------------------------------------------------------------------------------- math

        [Test]
        public void MathHelpers_BehaveOnTheEdges()
        {
            Assert.That(Deg(HandlingMath.WrapPi(0f)), Is.EqualTo(0f).Within(0.001f));
            Assert.That(Deg(HandlingMath.WrapPi(Rad(370f))), Is.EqualTo(10f).Within(0.001f));
            Assert.That(Deg(HandlingMath.WrapPi(Rad(-370f))), Is.EqualTo(-10f).Within(0.001f));
            Assert.That(Deg(HandlingMath.WrapPi(Rad(190f))), Is.EqualTo(-170f).Within(0.001f));
            Assert.That(Deg(HandlingMath.WrapPi(Rad(1080f))), Is.EqualTo(0f).Within(0.001f));

            Assert.That(HandlingMath.Clamp01(-3f), Is.EqualTo(0f));
            Assert.That(HandlingMath.Clamp01(3f), Is.EqualTo(1f));
            Assert.That(HandlingMath.MoveTowards(0f, 1f, 0.25f), Is.EqualTo(0.25f));
            Assert.That(HandlingMath.MoveTowards(0f, 1f, 5f), Is.EqualTo(1f), "move towards must not overshoot");
            Assert.That(HandlingMath.MoveTowards(0f, -1f, 5f), Is.EqualTo(-1f));
            Assert.That(HandlingMath.Smoothstep(0f, 1f, 0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HandlingMath.Smoothstep(0f, 1f, -1f), Is.EqualTo(0f));
            Assert.That(HandlingMath.Smoothstep(0f, 1f, 2f), Is.EqualTo(1f));
        }

        // ---------------------------------------------------------------------------- whole tick sanity

        [Test]
        public void Step_ProducesFiniteOutputAcrossPresetsAndEdgeInputs()
        {
            HandlingParams[] presets = { HandlingParams.Grippy, HandlingParams.Balanced, HandlingParams.Drifty };
            float[] speeds = { -5f, 0f, 0.5f, 12f, 30f, 60f };
            float[] laterals = { -20f, 0f, 20f };
            float[] yawRates = { -4f, 0f, 4f };
            float[] sticks = { -1f, -0.13f, 0f, 0.13f, 1f };

            for (int a = 0; a < presets.Length; a++)
            {
                for (int b = 0; b < speeds.Length; b++)
                {
                    for (int c = 0; c < laterals.Length; c++)
                    {
                        for (int d = 0; d < yawRates.Length; d++)
                        {
                            for (int e = 0; e < sticks.Length; e++)
                            {
                                var state = new HandlingState();
                                var input = new HandlingInput(
                                    stickX: sticks[e],
                                    stickY: sticks[(e + 2) % sticks.Length],
                                    cameraYawRadians: 0f,
                                    carYawRadians: Rad(37f),
                                    throttleOverride: -1f,
                                    brake: false,
                                    reverse: false,
                                    nitroScale: 1f,
                                    forwardSpeed: speeds[b],
                                    lateralSpeed: laterals[c],
                                    yawRateRadiansPerSecond: yawRates[d],
                                    massKilograms: 1200f,
                                    yawInertiaKilogramMetersSquared: 2088f,
                                    axleFrontX: 1.25f,
                                    axleRearX: -1.25f,
                                    axleLoadFrontNewton: 5884f,
                                    axleLoadRearNewton: 5884f,
                                    surfaceGripScale: 1f,
                                    zoneWheelGripScale: 1f,
                                    zoneTopSpeedScale: 1f,
                                    groundedWheels: 4,
                                    deltaTime: Dt,
                                    parameters: presets[a]);

                                HandlingOutput o = HandlingStep.Step(input, ref state);
                                AssertFinite(a, b, c, d, e, o);
                                Assert.That(o.Throttle, Is.GreaterThanOrEqualTo(0f));
                            }
                        }
                    }
                }
            }
        }

        private static void AssertFinite(int a, int b, int c, int d, int e, in HandlingOutput o)
        {
            string where = " at preset " + a + " speed " + b + " lateral " + c + " yaw " + d + " stick " + e;
            Assert.That(float.IsNaN(o.LongitudinalForceNewton) || float.IsInfinity(o.LongitudinalForceNewton), Is.False, "longitudinal force" + where);
            Assert.That(float.IsNaN(o.FrontForceYNewton) || float.IsInfinity(o.FrontForceYNewton), Is.False, "front lateral force" + where);
            Assert.That(float.IsNaN(o.RearForceYNewton) || float.IsInfinity(o.RearForceYNewton), Is.False, "rear lateral force" + where);
            Assert.That(float.IsNaN(o.TotalYawTorqueNewtonMeters) || float.IsInfinity(o.TotalYawTorqueNewtonMeters), Is.False, "yaw torque" + where);
            Assert.That(float.IsNaN(o.SteerAngleRadians) || float.IsInfinity(o.SteerAngleRadians), Is.False, "steer angle" + where);
            Assert.That(float.IsNaN(o.BetaRadians) || float.IsInfinity(o.BetaRadians), Is.False, "beta" + where);
            Assert.That(float.IsNaN(o.Throttle) || float.IsInfinity(o.Throttle), Is.False, "throttle" + where);
        }
    }
}
