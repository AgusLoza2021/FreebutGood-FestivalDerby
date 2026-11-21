using System.Collections;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G2c.1b RED fixture: pins the handling half of the motor contract (brake without crossing
    /// zero, gradual reverse, progressive steering yaw and brake-mode lateral grip) while those
    /// paths are still unimplemented. Bodies and definitions come from CarMotorTestRig so both
    /// halves of the contract are proven against exactly the same setup.
    /// </summary>
    public sealed class CarMotorHandlingPlayModeTests
    {
        private const int ReverseTicks = 120;
        private const int MatchedTicks = 3;
        private const int SteerApproachTicks = 30;
        // A real tire model relaxes whatever yaw the body already had toward the cornering
        // equilibrium of the current steer angle, so the case has to inject yaw below that
        // equilibrium: only there must the tires add yaw instead of scrubbing it away.
        private const float InjectedYaw = 0.05f;
        private const int FullLockTicks = 250;
        private const float MaxSelfInducedTiltDegrees = 20f;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [UnityTest]
        public IEnumerator Brake_OpposesMotionWithoutCrossingZeroInOneStep()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("brake body", definition.mass);
            var motor = new CarMotor(body, definition);
            body.linearVelocity = new Vector3(0f, 0f, 10f);
            yield return new WaitForFixedUpdate();
            float before = body.linearVelocity.z;
            Assert.That(before, Is.EqualTo(10f).Within(0.01f), "Setup momentum must survive one settled tick.");
            motor.Step(new CarInput(0f, true, false, false), Time.fixedDeltaTime);
            float immediate = body.linearVelocity.z;
            Assert.That(immediate, Is.GreaterThanOrEqualTo(0f), "One braking step must never reverse the body.");
            Assert.That(immediate, Is.LessThanOrEqualTo(before + 0.001f), "Braking must not add forward speed.");
            yield return new WaitForFixedUpdate();
            float after = body.linearVelocity.z;
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while braking.");
            Assert.That(after, Is.LessThan(before), "Braking must reduce forward speed.");
            Assert.That(after, Is.GreaterThan(0f), "Brake deceleration must not cross zero in one step.");
            // Second phase at creep speed: one raw step of brakeDeceleration (25 m/s^2 * 0.02 s =
            // 0.5 m/s) would overshoot the 0.1 m/s baseline past zero, so the motor must clamp
            // its own deceleration to the remaining speed instead of reversing the body.
            var creepBody = _rig.Body("brake creep body", definition.mass);
            var creepMotor = new CarMotor(creepBody, definition);
            creepBody.linearVelocity = new Vector3(0f, 0f, 0.1f);
            float creepFrom = Time.fixedTime;
            yield return new WaitForFixedUpdate();
            CarMotorTestRig.AssertFixedClockRan(creepFrom);
            float creepStart = creepBody.linearVelocity.z;
            Assert.That(creepStart, Is.EqualTo(0.1f).Within(0.001f), "Setup must inject sub-step forward speed.");
            creepMotor.Step(new CarInput(0f, true, false, false), Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            CarMotorTestRig.AssertFinite(creepBody.linearVelocity, "Velocity must stay finite at low-speed braking.");
            Assert.That(creepBody.linearVelocity.z, Is.GreaterThanOrEqualTo(0f),
                "Low-speed braking must stop at zero, never reverse.");
            Assert.That(creepBody.linearVelocity.z, Is.LessThanOrEqualTo(creepStart + 0.001f),
                "Low-speed braking must not speed the body up.");
        }

        [UnityTest]
        public IEnumerator ReverseGate_TransitionsWithoutVelocityFlip()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("reverse body", definition.mass);
            var motor = new CarMotor(body, definition);
            float fixedTimeBefore = Time.fixedTime;
            yield return _rig.RunSampled(body, motor, new CarInput(0f, false, true, false), ReverseTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(_rig.FirstTickSpeed, Is.GreaterThan(-definition.maxReverseSpeed),
                "Reverse must engage gradually, never flip the velocity in one tick.");
            Assert.That(_rig.MinSpeed, Is.GreaterThanOrEqualTo(-definition.maxReverseSpeed - 0.2f),
                "Reverse speed must respect the configured cap.");
            Assert.That(body.linearVelocity.z, Is.LessThan(-0.5f),
                "Held reverse input must build backward speed gradually.");
        }

        [UnityTest]
        public IEnumerator Steering_AddsProgressiveYawWithoutReplacingExistingAngularMomentum()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("steer body", definition.mass);
            var motor = new CarMotor(body, definition);
            // Tire forces only turn a moving car: a steered car at rest would just damp whatever yaw
            // it already had, so the case builds real forward speed before asserting on yaw.
            var settle = new CarInput(0f, false, false, false);
            for (int tick = 0; tick < SteerApproachTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                motor.Step(settle, Time.fixedDeltaTime);
            }

            float forwardSpeed = Vector3.Dot(body.linearVelocity, body.transform.forward);
            Assert.That(forwardSpeed, Is.GreaterThan(2f),
                "Setup must build forward speed, or the steering forces cannot turn the car.");
            body.angularVelocity = new Vector3(0f, InjectedYaw, 0f);
            yield return new WaitForFixedUpdate();
            float target = definition.maxYawRateDegrees * Mathf.Deg2Rad;
            float initial = body.angularVelocity.y;
            Assert.That(initial, Is.GreaterThan(0f).And.LessThan(target), "Setup must inject partial yaw momentum.");
            motor.Step(new CarInput(1f, false, false, false), Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            float first = body.angularVelocity.y;
            Assert.That(first, Is.GreaterThan(initial),
                "Steering must add yaw from the injected momentum on the first step, never discard it.");
            Assert.That(first, Is.LessThan(target - 0.02f), "One step must not replace yaw with the fixed target.");
            var input = new CarInput(1f, false, false, false);
            for (int tick = 0; tick < 5; tick++)
            {
                yield return new WaitForFixedUpdate();
                motor.Step(input, Time.fixedDeltaTime);
            }
            CarMotorTestRig.AssertFinite(body.angularVelocity, "Angular velocity must stay finite while steering.");
            Assert.That(body.angularVelocity.y, Is.GreaterThan(first), "Yaw must keep progressing over matched ticks.");
        }

        [UnityTest]
        public IEnumerator BrakeMode_ReducesLateralGripAcrossMatchedTicks()
        {
            var definition = _rig.Definition();
            var brakingBody = _rig.Body("braking body", definition.mass);
            var matchedBody = _rig.Body("matched body", definition.mass);
            var brakingMotor = new CarMotor(brakingBody, definition);
            var matchedMotor = new CarMotor(matchedBody, definition);
            brakingBody.linearVelocity = new Vector3(0.5f, 0f, 0f);
            matchedBody.linearVelocity = new Vector3(0.5f, 0f, 0f);
            float settleFrom = Time.fixedTime;
            yield return new WaitForFixedUpdate();
            CarMotorTestRig.AssertFixedClockRan(settleFrom);
            float drivenFrom = Time.fixedTime;
            var brakingInput = new CarInput(0f, true, false, false);
            var matchedInput = new CarInput(0f, false, false, false);
            for (int tick = 0; tick < MatchedTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                brakingMotor.Step(brakingInput, Time.fixedDeltaTime);
                matchedMotor.Step(matchedInput, Time.fixedDeltaTime);
            }
            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            CarMotorTestRig.AssertFinite(brakingBody.linearVelocity, "Braking body velocity must stay finite.");
            CarMotorTestRig.AssertFinite(matchedBody.linearVelocity, "Matched body velocity must stay finite.");
            // Reduced lateral grip under brake mode means the braking body slides more, not that it
            // scrubs harder: per GDD line 75 (brake combined with steering starts a controlled slide)
            // and line 83 (lateral grip, recoverable drift), it must retain strictly MORE lateral speed.
            // The injected slip stays inside the tire force cap, or both axles would saturate and the
            // two grip settings would be indistinguishable.
            Assert.That(Mathf.Abs(brakingBody.linearVelocity.x), Is.GreaterThan(0.001f),
                "Setup must keep lateral slip inside the force cap, or the comparison proves nothing.");
            Assert.That(Mathf.Abs(brakingBody.linearVelocity.x),
                Is.GreaterThan(Mathf.Abs(matchedBody.linearVelocity.x)),
                "Reduced grip under braking must retain more lateral slip than neutral forward input.");
        }

        // Wreckfest-style rams may flip a car, but its own steering never may: a full-lock turn at top
        // speed on flat ground has to stay planted. Uses the shipped definition so the project's -30
        // gravity and the real suspension, grip and damping values are what is being proven.
        [UnityTest]
        public IEnumerator FullLockAtTopSpeed_NeverRollsTheCarOver()
        {
            var shipped = Resources.Load<CarDefinition>("CarDefinition");
            Assert.That(shipped, Is.Not.Null, "The shipped CarDefinition asset must be loadable from Resources.");
            var definition = Object.Instantiate(shipped);
            var body = _rig.Body("full lock body", definition.mass);
            body.angularDamping = definition.angularDamping;
            body.maxAngularVelocity = definition.maxAngularVelocity;
            var motor = new CarMotor(body, definition);
            body.linearVelocity = body.transform.forward * definition.maxForwardSpeed;
            var input = new CarInput(1f, false, false, false);
            float maxTilt = 0f;
            for (int tick = 0; tick < FullLockTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                motor.Step(input, Time.fixedDeltaTime);
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(body.transform.up, Vector3.up));
            }

            Object.Destroy(definition);
            CarMotorTestRig.AssertFinite(body.angularVelocity, "Angular velocity must stay finite at full lock.");
            Assert.That(Vector3.Dot(body.linearVelocity, body.transform.forward), Is.GreaterThan(5f),
                "Setup must keep the car moving, or the tilt measurement proves nothing.");
            Assert.That(maxTilt, Is.LessThan(MaxSelfInducedTiltDegrees),
                "Steering alone must never roll the car; measured max tilt was " + maxTilt);
        }
    }
}
