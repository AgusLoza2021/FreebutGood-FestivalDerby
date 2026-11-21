using System;
using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// F1 behavior contract for Heading Drive: the stick only ever asks for a heading, the pedal is
    /// always forward, and the heading loop is rate-limited by grip. Written before the
    /// implementation, per the repo's RED-first rule; the default stub is not a deliverable.
    ///
    /// Numbers here are the shipped defaults read back from <see cref="HeadingDriveSettings.Default"/>,
    /// never copies, so retuning a default cannot silently turn these contracts into tautologies.
    /// The closed-loop cases integrate a kinematic bicycle model that carries the same grip ceiling
    /// the controller assumes: a steer request cannot produce more yaw than the tyres could hold.
    /// </summary>
    [TestFixture]
    public sealed class HeadingDriveControllerTests
    {
        private const float Deg2Rad = 0.017453292f;
        private const float MaxSteerRadians = 0.523598776f;

        /// <summary>
        /// Yaw response time of the closed-loop model. A 1300 kg car cannot change its yaw rate in
        /// one step, and an instantaneous model would make the rate feedback look unstable: the lag
        /// is the model's inertia, not a tuning knob for the controller.
        /// </summary>
        private const float YawLagSeconds = 0.12f;

        private static float Clamp(float value, float limit)
        {
            return value > limit ? limit : (value < -limit ? -limit : value);
        }

        private static HeadingDriveInput Input(
            float stickX,
            float stickY,
            float cameraYawDegrees,
            float carYawDegrees,
            float yawRateRadiansPerSecond,
            float speed,
            float maxSteerDegrees = 30f)
        {
            return new HeadingDriveInput(
                stickX,
                stickY,
                cameraYawDegrees * Deg2Rad,
                carYawDegrees * Deg2Rad,
                yawRateRadiansPerSecond,
                speed,
                maxSteerDegrees * Deg2Rad);
        }

        private static HeadingDriveOutput Evaluate(in HeadingDriveInput input)
        {
            return HeadingDriveController.Evaluate(input, HeadingDriveSettings.Default);
        }

        // ------------------------------------------------------------- the ring

        [Test]
        public void FullRingSweep_KeepsPowerAndNeverBrakes()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            float lifted = settings.ThrottleAtRing * settings.LargeAngleThrottleScale;

            for (int bearing = 0; bearing < 360; bearing += 5)
            {
                float radians = bearing * Deg2Rad;
                HeadingDriveOutput output = Evaluate(
                    Input((float)Math.Sin(radians), (float)Math.Cos(radians), 0f, 0f, 0f, 20f));

                string where = "The ring asks for " + bearing + " degrees.";
                Assert.That(output.Throttle, Is.GreaterThanOrEqualTo(lifted - 1e-4f), where);
                Assert.That(output.Throttle, Is.LessThanOrEqualTo(1f), where);
                Assert.That(output.Steer, Is.InRange(-1f, 1f), where);

                // The owner's complaint, as a contract: whatever the stick says, the pedal stays a
                // forward request, so the resolver can never answer Brake or Reverse for it.
                CarControlDecision decision = CarControlRules.Resolve(
                    new CarInput(output.Steer, output.Throttle, false, false, false), 20f, 1f, 0f);
                Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Forward), where);
            }
        }

        [Test]
        public void RingSweep_MirrorsTheTurnDirection()
        {
            for (int bearing = 5; bearing <= 85; bearing += 5)
            {
                float right = Evaluate(Input((float)Math.Sin(bearing * Deg2Rad), (float)Math.Cos(bearing * Deg2Rad),
                    0f, 0f, 0f, 20f)).Steer;
                float left = Evaluate(Input(-(float)Math.Sin(bearing * Deg2Rad), (float)Math.Cos(bearing * Deg2Rad),
                    0f, 0f, 0f, 20f)).Steer;

                Assert.That(right, Is.GreaterThan(0f), "A stick to the right must ask for a right turn.");
                Assert.That(left, Is.EqualTo(-right).Within(1e-4f), "The ring is symmetric around the nose.");
            }
        }

        [Test]
        public void StickStraightBehind_LiftsPowerAndFlagsTheSlide()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            HeadingDriveOutput ahead = Evaluate(Input(0f, 1f, 0f, 0f, 0f, 20f));
            HeadingDriveOutput behind = Evaluate(Input(0f, -1f, 0f, 0f, 0f, 20f));

            Assert.That(behind.DriftAssist, Is.True, "Dead astern is past the large-angle threshold.");
            Assert.That(behind.Throttle, Is.GreaterThan(0f), "A lift is not a stop.");
            Assert.That(behind.Throttle, Is.EqualTo(settings.ThrottleAtRing * settings.LargeAngleThrottleScale)
                .Within(1e-3f), "Past the threshold the ring keeps the pedal, scaled down.");
            Assert.That(behind.Throttle, Is.LessThan(ahead.Throttle),
                "The lift must be visible against the same stick pointing forward.");
            Assert.That(ahead.DriftAssist, Is.False);
        }

        [Test]
        public void StraightBehindAtStandstill_StillAsksForward()
        {
            HeadingDriveOutput output = Evaluate(Input(0f, -1f, 0f, 0f, 0f, 0f));
            CarControlDecision decision = CarControlRules.Resolve(
                new CarInput(output.Steer, output.Throttle, false, false, false), 0f, 1f, 0f);

            // The old reading reversed here; Heading Drive must not resurrect that behaviour.
            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Forward));
            Assert.That(output.Throttle, Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------ dead zone

        [TestCase(0f)]
        [TestCase(0.05f)]
        [TestCase(0.11f)]
        public void InsideTheDeadZone_CoastsWithNoOutput(float magnitude)
        {
            for (int bearing = 0; bearing < 360; bearing += 45)
            {
                float radians = bearing * Deg2Rad;
                HeadingDriveOutput output = Evaluate(Input(
                    magnitude * (float)Math.Sin(radians), magnitude * (float)Math.Cos(radians), 0f, 0f, 0f, 20f));

                Assert.That(output.Throttle, Is.Zero);
                Assert.That(output.Steer, Is.Zero);
                Assert.That(output.DriftAssist, Is.False);
            }
        }

        [Test]
        public void OutsideTheDeadZone_StartsAtTheCruiseFloor()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            HeadingDriveOutput output = Evaluate(Input(0f, settings.DeadZone + 0.01f, 0f, 0f, 0f, 20f));

            Assert.That(output.Throttle, Is.GreaterThanOrEqualTo(settings.ThrottleFloor));
            Assert.That(output.Throttle, Is.LessThan(settings.ThrottleAtRing),
                "The first millimetre of travel must not be full power.");
        }

        // -------------------------------------------------- the heading loop

        [Test]
        public void CameraYaw_RotatesTheTargetHeading()
        {
            HeadingDriveOutput centred = Evaluate(Input(0f, 1f, 0f, 0f, 0f, 20f));
            HeadingDriveOutput turned = Evaluate(Input(0f, 1f, 90f, 0f, 0f, 20f));
            HeadingDriveOutput mirrored = Evaluate(Input(0f, 1f, -90f, 0f, 0f, 20f));
            HeadingDriveOutput aligned = Evaluate(Input(0f, 1f, 90f, 90f, 0f, 20f));
            HeadingDriveOutput stickRight = Evaluate(Input(1f, 0f, 0f, 0f, 0f, 20f));

            Assert.That(centred.Steer, Is.Zero.Within(1e-4f), "Stick up with no yaw error is no request.");
            Assert.That(aligned.Steer, Is.Zero.Within(1e-4f), "A car already on the target needs no steer.");
            Assert.That(turned.Steer, Is.GreaterThan(0.1f), "A yawed camera moves the target with it.");
            Assert.That(mirrored.Steer, Is.EqualTo(-turned.Steer).Within(1e-4f));
            Assert.That(stickRight.Steer, Is.EqualTo(turned.Steer).Within(1e-4f),
                "Stick right and camera up yawed right are the same heading request.");
        }

        [Test]
        public void HighSpeedTurn_IsLimitedByTheGrip()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            const float speed = 26f;
            HeadingDriveOutput fast = Evaluate(Input(1f, 0f, 0f, 0f, 0f, speed));

            float maxYawRate = settings.MaxLateralAcceleration / speed;
            float feedForward = (float)Math.Atan(maxYawRate * settings.Wheelbase / speed) / MaxSteerRadians;
            // The measured yaw rate is zero, so the damping term is a full rate error.
            Assert.That(fast.Steer, Is.EqualTo(feedForward + settings.YawRateDamping).Within(2e-3f),
                "The grip-limited yaw rate is what the feed-forward converts to lock.");
            Assert.That(fast.Steer, Is.LessThan(0.6f),
                "Above the reference speed the same request must not call for full lock.");

            HeadingDriveOutput slow = Evaluate(Input(1f, 0f, 0f, 0f, 0f, 12f));
            HeadingDriveOutput crawling = Evaluate(Input(1f, 0f, 0f, 0f, 0f, settings.MinimumReferenceSpeed));

            Assert.That(slow.Steer, Is.GreaterThan(fast.Steer + 0.1f),
                "Half the speed needs more lock for the same heading request.");
            Assert.That(crawling.Steer, Is.EqualTo(1f),
                "At the reference speed a 90 degree request is full lock.");
        }

        [Test]
        public void YawRateFeedback_DampsAgainstTheMeasuredRate()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            const float speed = 20f;
            HeadingDriveOutput still = Evaluate(Input(1f, 0f, 0f, 0f, 0f, speed));
            HeadingDriveOutput spinning = Evaluate(Input(1f, 0f, 0f, 0f, 1f, speed));

            Assert.That(spinning.Steer, Is.LessThan(still.Steer),
                "A car already yawing the right way needs less lock than one that is not.");
        }

        [TestCase(8f)]
        [TestCase(15f)]
        [TestCase(25f)]
        public void StepHeadingChange_ConvergesWithoutOvershoot(float speed)
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            const float dt = 1f / 60f;
            const float target = 30f * Deg2Rad;
            const float tolerance = 1f * Deg2Rad;
            float lag = dt / (dt + YawLagSeconds);

            float yaw = 0f;
            float yawRate = 0f;
            float worstOvershoot = 0f;
            float settleTime = -1f;

            for (int i = 0; i < 300; i++)
            {
                HeadingDriveOutput output = Evaluate(Input(0f, 1f, target / Deg2Rad, yaw / Deg2Rad, yawRate, speed));
                float steerAngle = output.Steer * MaxSteerRadians;
                float requested = speed / settings.Wheelbase * (float)Math.Tan(steerAngle);
                float limit = settings.MaxLateralAcceleration / Math.Max(speed, settings.MinimumReferenceSpeed);
                yawRate += (Clamp(requested, limit) - yawRate) * lag;
                yaw += yawRate * dt;

                float elapsed = (i + 1) * dt;
                if (settleTime < 0f && Math.Abs(target - yaw) <= tolerance)
                    settleTime = elapsed;
                if (yaw - target > worstOvershoot)
                    worstOvershoot = yaw - target;
            }

            Assert.That(settleTime, Is.GreaterThan(0f), "The car never reached the requested heading.");
            Assert.That(settleTime, Is.LessThan(3f), "A heading step must settle well inside three seconds.");
            Assert.That(worstOvershoot, Is.LessThan(10f * Deg2Rad), "The loop must not overshoot by ten degrees.");
        }

        [Test]
        public void HighSpeedStep_StaysInsideTheGripCeiling()
        {
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            const float dt = 1f / 60f;
            const float speed = 26f;
            const float target = 90f * Deg2Rad;
            float limit = settings.MaxLateralAcceleration / speed;
            float lag = dt / (dt + YawLagSeconds);

            float yaw = 0f;
            float yawRate = 0f;
            float peakYawRate = 0f;
            float peakSteer = 0f;

            for (int i = 0; i < 300; i++)
            {
                HeadingDriveOutput output = Evaluate(Input(0f, 1f, target / Deg2Rad, yaw / Deg2Rad, yawRate, speed));
                if (output.Steer > peakSteer)
                    peakSteer = output.Steer;

                float requested = speed / settings.Wheelbase * (float)Math.Tan(output.Steer * MaxSteerRadians);
                yawRate += (Clamp(requested, limit) - yawRate) * lag;
                yaw += yawRate * dt;
                float magnitude = yawRate < 0f ? -yawRate : yawRate;
                if (magnitude > peakYawRate)
                    peakYawRate = magnitude;
            }

            Assert.That(peakYawRate, Is.LessThanOrEqualTo(limit + 1e-3f));
            Assert.That(peakSteer, Is.LessThan(0.6f), "A fast car must not be asked for full lock.");
            Assert.That(Math.Abs(target - yaw), Is.LessThan(2f * Deg2Rad), "The turn still has to happen.");
        }

        // ----------------------------------------------------------- the adapter

        [Test]
        public void Adapter_ReadsTheSameHeadingTheControllerSees()
        {
            // Camera forward +Z (yaw 0), car forward -Z (yaw 180 degrees): stick up is a reversal.
            JoystickDriveMapping.HeadingDrive(
                0f, 1f, 0f, 1f, 0f, -1f, 0f, 20f, MaxSteerRadians,
                HeadingDriveSettings.Default, out HeadingDriveOutput output);

            HeadingDriveOutput direct = Evaluate(Input(0f, 1f, 0f, 180f, 0f, 20f));

            Assert.That(output.Throttle, Is.EqualTo(direct.Throttle).Within(1e-6f));
            Assert.That(output.Steer, Is.EqualTo(direct.Steer).Within(1e-6f));
            Assert.That(output.DriftAssist, Is.True);
        }

        [Test]
        public void Adapter_WithoutACamera_KeepsTheStickRelativeToTheNose()
        {
            // A zero camera axis falls back to the car heading, so stick up means "straight on".
            JoystickDriveMapping.HeadingDrive(
                0f, 1f, 0f, 0f, 0f, 1f, 0f, 20f, MaxSteerRadians,
                out HeadingDriveOutput noCamera);

            Assert.That(noCamera.Steer, Is.Zero.Within(1e-4f));
            Assert.That(noCamera.Throttle, Is.GreaterThan(0.9f));

            // Without a car heading there is no error to steer by, so the sample is dropped.
            JoystickDriveMapping.HeadingDrive(
                0f, 1f, 0f, 1f, 0f, 0f, 0f, 20f, MaxSteerRadians,
                out HeadingDriveOutput noCar);

            Assert.That(noCar.Throttle, Is.Zero);
            Assert.That(noCar.Steer, Is.Zero);
        }

        // ---------------------------------------------------------- no garbage

        [Test]
        public void Evaluate_DoesNotAllocate()
        {
#if NET5_0_OR_GREATER
            HeadingDriveSettings settings = HeadingDriveSettings.Default;
            HeadingDriveInput input = Input(0.5f, 0.5f, 10f, 3f, 0.2f, 18f);
            float sum = 0f;
            for (int i = 0; i < 200; i++)
                sum += HeadingDriveController.Evaluate(input, settings).Steer;

            long before = GC.GetAllocatedBytesForCurrentThread();
            float measured = 0f;
            for (int i = 0; i < 500; i++)
                measured += HeadingDriveController.Evaluate(input, settings).Steer;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(sum, Is.GreaterThan(0f), "The warm-up must exercise a real steering request.");
            Assert.That(measured, Is.EqualTo(sum * 2.5f).Within(1e-3f), "The measured loop must be the warm-up loop.");
            Assert.That(bytes, Is.Zero, "A stick sample in the hot path may not allocate.");
#endif
        }
    }
}
