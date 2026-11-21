using System;
using System.Collections;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G2c.1a RED fixture: constructor/tuning-snapshot contract, fail-safe dead-input guards and
    /// progressive propulsion for CarMotor while it is an intentional no-op stub. Brake, reverse,
    /// steering yaw and lateral grip live in the G2c.1b fixture. Every case owns its bodies and
    /// definitions through the shared rig and drives only the default automatic simulation.
    /// </summary>
    public sealed class CarMotorPropulsionPlayModeTests
    {
        private const int CruiseTicks = 90;      // ~1.8 s at the default 0.02 s fixed step.
        private const int CapHorizonTicks = 900; // Long enough to approach maxForwardSpeed.
        private const int MatchedTicks = 60;

        private CarMotorTestRig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = new CarMotorTestRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
                _rig.Dispose();
        }

        [TestCase(true)]  // A missing body must be rejected before any field is used.
        [TestCase(false)] // A missing definition must be rejected just as hard.
        public void Constructor_RejectsNullBodyOrDefinition(bool missingBody)
        {
            var definition = missingBody ? _rig.Definition() : null;
            var body = missingBody ? null : _rig.Body("constructor body", 1000f);
            Assert.Throws<ArgumentNullException>(() => new CarMotor(body, definition));
        }

        [UnityTest]
        public IEnumerator Step_RejectsKinematicBody()
        {
            // A kinematic body must be left untouched, with no force banked for later ticks.
            var definition = _rig.Definition();
            var body = _rig.Body("kinematic body", definition.mass);
            body.isKinematic = true;
            body.linearVelocity = new Vector3(1f, 0f, 2f);
            var motor = new CarMotor(body, definition);
            yield return _rig.AssertDeadInputFailsafe(body, motor, Time.fixedDeltaTime, true);
        }

        [UnityTest]
        public IEnumerator Step_RejectsZeroDeltaTime()
        {
            // A zero deltaTime must be a fail-safe no-op on a dynamic body.
            var definition = _rig.Definition();
            var body = _rig.Body("zero-delta body", definition.mass);
            body.linearVelocity = new Vector3(1f, 0f, 2f);
            var motor = new CarMotor(body, definition);
            yield return _rig.AssertDeadInputFailsafe(body, motor, 0f, false);
        }

        [UnityTest]
        public IEnumerator Step_RejectsNaNDeltaTime()
        {
            // A NaN deltaTime must be a fail-safe no-op on a dynamic body.
            var definition = _rig.Definition();
            var body = _rig.Body("nan-delta body", definition.mass);
            body.linearVelocity = new Vector3(1f, 0f, 2f);
            var motor = new CarMotor(body, definition);
            yield return _rig.AssertDeadInputFailsafe(body, motor, float.NaN, false);
        }

        [UnityTest]
        public IEnumerator FullThrottle_PropelsForwardOnFixedTicks()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("default-input body", definition.mass);
            var motor = new CarMotor(body, definition);
            float fixedTimeBefore = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, 1f, false, false, false), CruiseTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0.5f), "A full pedal must propel forward.");
            Assert.That(body.transform.position.z, Is.GreaterThan(0f), "Forward velocity must displace the body.");
        }

        [UnityTest]
        public IEnumerator ReleasedPedal_FromRest_DoesNotPropelTheCar()
        {
            // D20: the drive axis is the only source of drive, so a released pedal must leave a
            // resting car where it is instead of launching it.
            var definition = _rig.Definition();
            var body = _rig.Body("released-pedal body", definition.mass);
            var motor = new CarMotor(body, definition);
            float fixedTimeBefore = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, 0f, false, false, false), CruiseTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.05f),
                "A released pedal must not accelerate a resting car.");
            Assert.That(body.transform.position.z, Is.EqualTo(0f).Within(0.05f),
                "A released pedal must leave the resting car in place.");
        }

        [UnityTest]
        public IEnumerator HalfPedal_IsSlowerThanFullPedalOnTheSameWindow()
        {
            var definition = _rig.Definition();
            var halfBody = _rig.Body("half-pedal body", definition.mass);
            var halfMotor = new CarMotor(halfBody, definition);
            yield return _rig.RunSampled(halfBody, halfMotor, new CarInput(0f, 0.5f, false, false, false), CruiseTicks);
            float half = halfBody.linearVelocity.z;

            var fullBody = _rig.Body("full-pedal body", definition.mass);
            var fullMotor = new CarMotor(fullBody, definition);
            yield return _rig.RunSampled(fullBody, fullMotor, new CarInput(0f, 1f, false, false, false), CruiseTicks);

            Assert.That(half, Is.GreaterThan(0f), "A half pedal must still move the car.");
            Assert.That(half, Is.LessThan(fullBody.linearVelocity.z),
                "The pedal depth must scale the drive force, not just switch it on.");
        }

        [UnityTest]
        public IEnumerator ForwardPropulsion_ApproachesConfiguredCapWithoutExceedingIt()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("cap body", definition.mass);
            var motor = new CarMotor(body, definition);
            float fixedTimeBefore = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, false, false, false), CapHorizonTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(_rig.MaxSpeed, Is.LessThanOrEqualTo(definition.maxForwardSpeed + 0.05f),
                "Forward propulsion must never exceed the configured cap.");
            Assert.That(body.linearVelocity.z, Is.GreaterThanOrEqualTo(definition.maxForwardSpeed * 0.9f),
                "The cap must be approached within the bounded horizon.");
        }

        [UnityTest]
        public IEnumerator ExistingMomentumAboveCap_IsNotHardClamped()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("momentum body", definition.mass);
            var motor = new CarMotor(body, definition);
            body.linearVelocity = new Vector3(0f, 0f, definition.maxForwardSpeed * 1.5f);
            float settleFrom = Time.fixedTime;
            yield return new WaitForFixedUpdate();
            CarMotorTestRig.AssertFixedClockRan(settleFrom);
            float start = body.linearVelocity.z;
            Assert.That(start, Is.GreaterThan(definition.maxForwardSpeed), "Setup must inject above-cap momentum.");
            float drivenFrom = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, false, false, false), MatchedTicks);
            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            Assert.That(_rig.MinSpeed, Is.GreaterThan(definition.maxForwardSpeed),
                "Existing momentum must never be hard-clamped down to the cap.");
            Assert.That(body.linearVelocity.z, Is.LessThan(start),
                "Above-cap momentum must bleed gradually rather than be erased in one tick.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(definition.maxForwardSpeed),
                "The bleed must still leave the body above the cap inside the matched window.");
        }

        [UnityTest]
        public IEnumerator TuningSnapshot_IsFiniteImmutableAndNeverMutatesDefinition()
        {
            var definition = _rig.Definition();
            definition.driveForce = 4000f;
            definition.maxForwardSpeed = 8f;
            var body = _rig.Body("snapshot body", definition.mass);
            var motor = new CarMotor(body, definition);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must be finite before stepping.");
            // Mutating the asset after construction must not leak into the motor's copied tuning.
            definition.driveForce = 200000f;
            definition.maxForwardSpeed = 200f;
            float fixedTimeBefore = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, false, false, false), MatchedTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(body.linearVelocity.z, Is.LessThanOrEqualTo(8f + 0.05f),
                "The motor must keep behaving with the tuning copied at construction.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(1f),
                "The copied acceleration must still propel the body.");
            var nanDefinition = _rig.Definition();
            nanDefinition.driveForce = float.NaN;
            var nanBody = _rig.Body("non-finite body", nanDefinition.mass);
            var nanMotor = new CarMotor(nanBody, nanDefinition);
            yield return _rig.RunSampled(nanBody, nanMotor, new CarInput(0f, false, false, false), 30);
            CarMotorTestRig.AssertFinite(nanBody.linearVelocity, "Non-finite tuning must never produce non-finite velocity.");
            Assert.That(nanBody.linearVelocity.z, Is.EqualTo(0f).Within(0.001f),
                "Non-finite acceleration must apply no forward propulsion.");
            Assert.That(nanDefinition.driveForce, Is.NaN,
                "Stepping must never mutate or sanitize the definition asset.");
        }
    }
}
