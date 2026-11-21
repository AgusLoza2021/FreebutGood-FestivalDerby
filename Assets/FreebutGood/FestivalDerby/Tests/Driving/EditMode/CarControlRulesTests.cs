using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G1 behavior contract: run EditMode restricted to FestivalDerby.Driving.EditModeTests.
    /// These tests intentionally precede implementation; the default stub is not a deliverable.
    /// Numeric examples exercise boundaries, not selected driving tuning or physics behavior.
    /// </summary>
    [TestFixture]
    public sealed class CarControlRulesTests
    {
        [TestCase(-2f, -1f)]
        [TestCase(-1f, -1f)]
        [TestCase(-0.6f, -0.6f)]
        [TestCase(-0.2001f, -0.2001f)]
        [TestCase(0.2001f, 0.2001f)]
        [TestCase(0.6f, 0.6f)]
        [TestCase(1f, 1f)]
        [TestCase(2f, 1f)]
        [TestCase(float.MinValue, -1f)]
        [TestCase(float.MaxValue, 1f)]
        public void FiniteSteering_ClampsWithoutRescaling(float steer, float expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(steer, false, false, false), 0f, 1f, 0.2f);

            Assert.That(decision.Steer, Is.EqualTo(expected));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidSteering_IsNeutral(float steer)
        {
            var decision = CarControlRules.Resolve(new CarInput(steer, false, false, false), 0f, 1f, 0.2f);

            Assert.That(decision.Steer, Is.Zero);
        }

        [TestCase(-0.2f)]
        [TestCase(-0.1f)]
        [TestCase(0f)]
        [TestCase(0.1f)]
        [TestCase(0.2f)]
        public void Steering_AtOrInsideDeadZone_IsNeutral(float steer)
        {
            var decision = CarControlRules.Resolve(new CarInput(steer, false, false, false), 0f, 1f, 0.2f);

            Assert.That(decision.Steer, Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.5f)]
        public void InvalidOrNegativeDeadZone_IsZero(float deadZone)
        {
            var decision = CarControlRules.Resolve(new CarInput(0.25f, false, false, false), 0f, 1f, deadZone);

            Assert.That(decision.Steer, Is.EqualTo(0.25f));
        }

        [TestCase(-0.5f, 0.25f, 0.25f)]
        [TestCase(0f, 0.25f, 0.25f)]
        [TestCase(1f, 0.9f, 0f)]
        [TestCase(1f, -1f, 0f)]
        [TestCase(2f, 2f, 0f)]
        [TestCase(2f, -2f, 0f)]
        public void FiniteDeadZone_IsClamped(float deadZone, float steer, float expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(steer, false, false, false), 0f, 1f, deadZone);

            Assert.That(decision.Steer, Is.EqualTo(expected));
        }

        [TestCase(-8f)]
        [TestCase(0f)]
        [TestCase(8f)]
        public void NoBrakeOrReverse_RequestsForwardIntention(float speed)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, false, false, false), speed, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Forward));
        }

        [TestCase(false, -3f)]
        [TestCase(false, 0f)]
        [TestCase(false, 1f)]
        [TestCase(false, 10f)]
        [TestCase(true, -3f)]
        [TestCase(true, 0f)]
        [TestCase(true, 1f)]
        [TestCase(true, 10f)]
        public void Brake_DominatesForwardAndReverse(bool reverse, float speed)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, true, reverse, false), speed, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Brake));
        }

        [TestCase(0.999f, 1f, CarControlMode.Reverse)]
        [TestCase(1f, 1f, CarControlMode.Brake)]
        [TestCase(1.001f, 1f, CarControlMode.Brake)]
        [TestCase(0f, 1f, CarControlMode.Reverse)]
        [TestCase(-1f, 1f, CarControlMode.Reverse)]
        [TestCase(0f, 0f, CarControlMode.Reverse)]
        [TestCase(0.001f, 0f, CarControlMode.Brake)]
        [TestCase(-0.001f, 0f, CarControlMode.Reverse)]
        [TestCase(0f, 0.5f, CarControlMode.Reverse)]
        [TestCase(10f, 0.5f, CarControlMode.Brake)]
        public void ReverseRequest_RespectsStrictThresholdAndStationaryException(
            float speed, float threshold, CarControlMode expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, false, true, false), speed, threshold, 0f);

            Assert.That(decision.Mode, Is.EqualTo(expected));
        }

        [TestCase(float.NaN, 0f, CarControlMode.Reverse)]
        [TestCase(float.NaN, 0.1f, CarControlMode.Brake)]
        [TestCase(float.NaN, -1f, CarControlMode.Reverse)]
        [TestCase(float.PositiveInfinity, 0f, CarControlMode.Reverse)]
        [TestCase(float.PositiveInfinity, 0.1f, CarControlMode.Brake)]
        [TestCase(float.PositiveInfinity, -1f, CarControlMode.Reverse)]
        [TestCase(float.NegativeInfinity, 0f, CarControlMode.Reverse)]
        [TestCase(float.NegativeInfinity, 0.1f, CarControlMode.Brake)]
        [TestCase(float.NegativeInfinity, -1f, CarControlMode.Reverse)]
        [TestCase(-1f, 0f, CarControlMode.Reverse)]
        [TestCase(-1f, 0.1f, CarControlMode.Brake)]
        [TestCase(-1f, -1f, CarControlMode.Reverse)]
        public void InvalidOrNegativeReverseThreshold_SanitizesToZero(
            float threshold, float speed, CarControlMode expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, false, true, false), speed, threshold, 0f);

            Assert.That(decision.Mode, Is.EqualTo(expected));
        }

        [TestCase(float.NaN, false, false)]
        [TestCase(float.NaN, false, true)]
        [TestCase(float.NaN, true, false)]
        [TestCase(float.NaN, true, true)]
        [TestCase(float.PositiveInfinity, false, false)]
        [TestCase(float.PositiveInfinity, false, true)]
        [TestCase(float.PositiveInfinity, true, false)]
        [TestCase(float.PositiveInfinity, true, true)]
        [TestCase(float.NegativeInfinity, false, false)]
        [TestCase(float.NegativeInfinity, false, true)]
        [TestCase(float.NegativeInfinity, true, false)]
        [TestCase(float.NegativeInfinity, true, true)]
        public void InvalidSignedSpeed_AlwaysRequestsFailSafeBrake(float speed, bool brake, bool reverse)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, brake, reverse, false), speed, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Brake));
        }

        [TestCase(0.999f, CarControlMode.Reverse)]
        [TestCase(1f, CarControlMode.Brake)]
        [TestCase(1.001f, CarControlMode.Brake)]
        public void BrakeReleaseWhileReverseHeld_ReevaluatesSpeed(float speed, CarControlMode expected)
        {
            var held = CarControlRules.Resolve(new CarInput(0f, true, true, false), speed, 1f, 0f);
            var released = CarControlRules.Resolve(new CarInput(0f, false, true, false), speed, 1f, 0f);

            Assert.That(held.Mode, Is.EqualTo(CarControlMode.Brake));
            Assert.That(released.Mode, Is.EqualTo(expected));
        }

        [TestCase(-2f, CarControlMode.Reverse)]
        [TestCase(0f, CarControlMode.Reverse)]
        [TestCase(2f, CarControlMode.Brake)]
        public void ReverseRelease_ReturnsForwardIntention(float speed, CarControlMode heldMode)
        {
            var held = CarControlRules.Resolve(new CarInput(0f, false, true, false), speed, 1f, 0f);
            var released = CarControlRules.Resolve(new CarInput(0f, false, false, false), speed, 1f, 0f);

            // The same speed is supplied: this contract cannot instantly flip physical velocity.
            Assert.That(held.Mode, Is.EqualTo(heldMode));
            Assert.That(released.Mode, Is.EqualTo(CarControlMode.Forward));
        }

        [TestCase(true, float.NaN, 0f, false, false)]
        [TestCase(false, float.NaN, 0f, false, false)]
        [TestCase(true, 2f, 4f, true, true)]
        [TestCase(false, 2f, 4f, true, true)]
        [TestCase(true, -2f, -1f, false, true)]
        [TestCase(false, -2f, -1f, false, true)]
        [TestCase(true, 0.1f, float.PositiveInfinity, false, false)]
        [TestCase(false, 0.1f, float.PositiveInfinity, false, false)]
        public void NitroPulse_PassesVerbatimThroughNormalization(
            bool pulse, float steer, float speed, bool brake, bool reverse)
        {
            var decision = CarControlRules.Resolve(new CarInput(steer, brake, reverse, pulse), speed, 1f, 0.2f);

            Assert.That(decision.NitroPulse, Is.EqualTo(pulse));
        }

        [Test]
        public void NitroPulse_DoesNotGenerateHoldEdges()
        {
            var pulse = new CarInput(0f, false, false, true);
            var quiet = new CarInput(0f, false, false, false);

            Assert.That(CarControlRules.Resolve(pulse, 0f, 1f, 0f).NitroPulse, Is.True);
            Assert.That(CarControlRules.Resolve(pulse, 0f, 1f, 0f).NitroPulse, Is.True);
            Assert.That(CarControlRules.Resolve(quiet, 0f, 1f, 0f).NitroPulse, Is.False);
            Assert.That(CarControlRules.Resolve(pulse, 0f, 1f, 0f).NitroPulse, Is.True);
        }

        [TestCase(0.2001f, 0.2001f)]
        [TestCase(0.25f, 0.25f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 1f)]
        [TestCase(2f, 1f)]
        public void PedalDepth_BecomesTheDriveFraction(float throttle, float expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, throttle, false, false, false), 0f, 1f, 0.2f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Forward));
            Assert.That(decision.DriveFraction, Is.EqualTo(expected));
        }

        [TestCase(-0.2f)]
        [TestCase(0f)]
        [TestCase(0.2f)]
        public void ReleasedPedal_CoastsAtAnySpeed(float throttle)
        {
            var input = new CarInput(0f, throttle, false, false, false);

            foreach (float speed in new[] { -8f, 0f, 8f })
            {
                var decision = CarControlRules.Resolve(input, speed, 1f, 0.2f);

                Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Coast),
                    $"A released pedal at {speed} m/s must coast instead of driving the car.");
                Assert.That(decision.DriveFraction, Is.Zero, "Coasting applies no force to scale.");
            }
        }

        [Test]
        public void LegacyConstructor_KeepsItsAutoForwardMeaning()
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, false, false, false), 0f, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Forward),
                "Pre-throttle call sites used the buttonless input as a drive request.");
            Assert.That(decision.DriveFraction, Is.EqualTo(1f));
        }

        [TestCase(-2f, CarControlMode.Reverse)]
        [TestCase(0f, CarControlMode.Reverse)]
        [TestCase(0.999f, CarControlMode.Reverse)]
        [TestCase(1f, CarControlMode.Brake)]
        [TestCase(10f, CarControlMode.Brake)]
        public void HeldBackStick_ReversesOnceSlowEnoughAndBrakesWhileFast(float speed, CarControlMode expected)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, -0.5f, false, false, false), speed, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(expected),
                "Holding the stick back is one gesture whose meaning the reverse gate decides.");
            Assert.That(decision.DriveFraction, Is.EqualTo(0.5f), "The gesture keeps its own depth.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidPedal_Coasts(float throttle)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, throttle, false, false, false), 0f, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Coast));
            Assert.That(decision.DriveFraction, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BrakeButton_DominatesAPartialPedal(bool reverseHeld)
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, 0.5f, true, reverseHeld, false), 0f, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Brake));
            Assert.That(decision.DriveFraction, Is.EqualTo(1f),
                "A held brake button is a full stop request, never a partial one.");
        }

        [Test]
        public void NitroPulse_SurvivesACoastingPedal()
        {
            var decision = CarControlRules.Resolve(new CarInput(0f, 0f, false, false, true), 4f, 1f, 0f);

            Assert.That(decision.Mode, Is.EqualTo(CarControlMode.Coast));
            Assert.That(decision.NitroPulse, Is.True, "Nitro stays a button beside the drive axis.");
        }
    }
}
