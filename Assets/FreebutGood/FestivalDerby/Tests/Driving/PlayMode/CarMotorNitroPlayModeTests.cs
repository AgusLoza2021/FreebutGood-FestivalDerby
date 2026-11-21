using System.Collections;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G2c.4 RED fixture: pins the forward-only one-shot nitro contract. GDD line 77 defines the
    /// impulse as a tap that consumes one ready charge and then runs for a fixed duration for a
    /// faster top speed, and GDD line 293 leaves the number of charges to the owner, so this fixture
    /// proves the single charge, its cooldown gate and the boosted cap. Every comparison is paired
    /// against a control body inside the same tick loop, so no case depends on absolute timing,
    /// catch-up frames or floating-point integration drift.
    /// </summary>
    public sealed class CarMotorNitroPlayModeTests
    {
        private const int BoostProbeTicks = 30;
        private const int BrakePhaseTicks = 90;
        private const int ForwardProbeTicks = 5;
        private const int RepeatedPulseTicks = 90;
        private const int LatePulseTick = 85;
        private const int PausedBoostTicks = 90;
        private const int ResumeProbeTicks = 25;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [UnityTest]
        public IEnumerator Pulse_OutlastsTheTapAndAcceleratesFasterThanTheSameUnpulsedInput()
        {
            var definition = _rig.Definition();
            var pulsedBody = _rig.Body("nitro pulsed body", definition.mass);
            var plainBody = _rig.Body("nitro plain body", definition.mass);
            var pulsedMotor = new CarMotor(pulsedBody, definition);
            var plainMotor = new CarMotor(plainBody, definition);
            yield return new WaitForFixedUpdate();

            var pulse = new CarInput(0f, false, false, true);
            var quiet = new CarInput(0f, false, false, false);
            float drivenFrom = Time.fixedTime;
            pulsedMotor.Step(pulse, Time.fixedDeltaTime);
            plainMotor.Step(quiet, Time.fixedDeltaTime);
            for (int tick = 1; tick < BoostProbeTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                // Releasing the tap must not cancel the boost: every later tick is a quiet input.
                pulsedMotor.Step(quiet, Time.fixedDeltaTime);
                plainMotor.Step(quiet, Time.fixedDeltaTime);
            }

            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            CarMotorTestRig.AssertFinite(pulsedBody.linearVelocity, "Pulsed velocity must stay finite.");
            CarMotorTestRig.AssertFinite(plainBody.linearVelocity, "Control velocity must stay finite.");
            Assert.That(plainBody.linearVelocity.z, Is.GreaterThan(0f),
                "The control body must build speed, or this comparison proves nothing.");
            Assert.That(pulsedBody.linearVelocity.z, Is.GreaterThan(plainBody.linearVelocity.z * 1.1f),
                "One tap must out-accelerate the identical quiet car for the whole fixed window, and releasing it must not cancel the boost.");
            Assert.That(pulsedBody.linearVelocity.z,
                Is.LessThan(definition.maxForwardSpeed * definition.nitroTopSpeedMultiplier + 0.05f),
                "The boost raises the cap by a fixed multiplier, never past it.");
        }

        [UnityTest]
        public IEnumerator RejectedPulseInBrakeAndReverseDoesNotConsumeTheSingleCharge()
        {
            var definition = _rig.Definition();
            var rejectedBody = _rig.Body("nitro rejected body", definition.mass);
            var controlBody = _rig.Body("nitro control body", definition.mass);
            var rejectedMotor = new CarMotor(rejectedBody, definition);
            var controlMotor = new CarMotor(controlBody, definition);
            yield return new WaitForFixedUpdate();

            rejectedMotor.Step(new CarInput(0f, true, false, true), Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            Assert.That(rejectedBody.linearVelocity.z, Is.LessThanOrEqualTo(0.001f),
                "A pulse in brake mode must add no forward propulsion.");

            rejectedMotor.Step(new CarInput(0f, false, true, true), Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            Assert.That(rejectedBody.linearVelocity.z, Is.LessThan(0f),
                "A pulse in reverse mode must not stop reverse from engaging.");
            Assert.That(rejectedBody.linearVelocity.z, Is.GreaterThanOrEqualTo(-definition.maxReverseSpeed),
                "Rejecting a pulse must not defeat the configured reverse speed cap.");

            // Reset the pose, then prove the charge survived both rejections: the first forward pulse
            // must still boost. A motor that consumed the charge while rejecting would match the
            // quiet control exactly.
            rejectedBody.linearVelocity = Vector3.zero;
            controlBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            var pulse = new CarInput(0f, false, false, true);
            var quiet = new CarInput(0f, false, false, false);
            float drivenFrom = Time.fixedTime;
            rejectedMotor.Step(pulse, Time.fixedDeltaTime);
            controlMotor.Step(quiet, Time.fixedDeltaTime);
            for (int tick = 1; tick < BoostProbeTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                rejectedMotor.Step(quiet, Time.fixedDeltaTime);
                controlMotor.Step(quiet, Time.fixedDeltaTime);
            }

            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            CarMotorTestRig.AssertFinite(rejectedBody.linearVelocity, "Rejected-pulse velocity must stay finite.");
            Assert.That(controlBody.linearVelocity.z, Is.GreaterThan(0f),
                "The quiet control must build speed, or this comparison proves nothing.");
            Assert.That(rejectedBody.linearVelocity.z, Is.GreaterThan(controlBody.linearVelocity.z * 1.1f),
                "Brake and reverse must reject the pulse without consuming the charge, so the first forward pulse must still boost.");
        }

        [UnityTest]
        public IEnumerator LatePulseAfterTheBoostExpiredDoesNotReactivateIt()
        {
            var definition = _rig.Definition();
            var singleBody = _rig.Body("nitro single-pulse body", definition.mass);
            var lateBody = _rig.Body("nitro late-pulse body", definition.mass);
            var quietBody = _rig.Body("nitro quiet body", definition.mass);
            var singleMotor = new CarMotor(singleBody, definition);
            var lateMotor = new CarMotor(lateBody, definition);
            var quietMotor = new CarMotor(quietBody, definition);
            yield return new WaitForFixedUpdate();

            var pulse = new CarInput(0f, false, false, true);
            var quiet = new CarInput(0f, false, false, false);
            float drivenFrom = Time.fixedTime;
            singleMotor.Step(pulse, Time.fixedDeltaTime);
            lateMotor.Step(pulse, Time.fixedDeltaTime);
            quietMotor.Step(quiet, Time.fixedDeltaTime);
            for (int tick = 1; tick < RepeatedPulseTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                singleMotor.Step(quiet, Time.fixedDeltaTime);
                lateMotor.Step(tick == LatePulseTick ? pulse : quiet, Time.fixedDeltaTime);
                quietMotor.Step(quiet, Time.fixedDeltaTime);
            }

            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            CarMotorTestRig.AssertFinite(lateBody.linearVelocity, "Late-pulse velocity must stay finite.");
            CarMotorTestRig.AssertFinite(singleBody.linearVelocity, "Single-pulse velocity must stay finite.");
            Assert.That(quietBody.linearVelocity.z, Is.GreaterThan(0f),
                "The quiet control must build speed independently.");
            Assert.That(singleBody.linearVelocity.z, Is.GreaterThan(quietBody.linearVelocity.z * 1.05f),
                "The first pulse must have boosted, or the late-pulse comparison proves nothing.");
            Assert.That(lateBody.linearVelocity.z, Is.LessThan(definition.maxForwardSpeed),
                "Neither pulsed car may reach the speed cap, or the equality below would be hidden by it.");
            Assert.That(lateBody.linearVelocity.z, Is.EqualTo(singleBody.linearVelocity.z).Within(0.05f),
                "A pulse inside the cooldown must not reactivate or extend the boost: one charge, gated until it recharges.");
        }

        [UnityTest]
        public IEnumerator ActiveBoostAddsNothingInBrakeWhileItsClockKeepsRunning()
        {
            var definition = _rig.Definition();
            var pulsedBody = _rig.Body("nitro brake-pulsed body", definition.mass);
            var plainBody = _rig.Body("nitro brake-plain body", definition.mass);
            var pulsedMotor = new CarMotor(pulsedBody, definition);
            var plainMotor = new CarMotor(plainBody, definition);
            pulsedBody.linearVelocity = new Vector3(0f, 0f, 3f);
            plainBody.linearVelocity = new Vector3(0f, 0f, 3f);
            yield return new WaitForFixedUpdate();

            var pulse = new CarInput(0f, false, false, true);
            var brake = new CarInput(0f, true, false, false);
            // Consume the charge in forward first, so the boost is genuinely active when braking starts.
            pulsedMotor.Step(pulse, Time.fixedDeltaTime);
            plainMotor.Step(brake, Time.fixedDeltaTime);
            float drivenFrom = Time.fixedTime;
            for (int tick = 1; tick < BrakePhaseTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                pulsedMotor.Step(brake, Time.fixedDeltaTime);
                plainMotor.Step(brake, Time.fixedDeltaTime);
            }

            CarMotorTestRig.AssertFixedClockRan(drivenFrom);
            CarMotorTestRig.AssertFinite(pulsedBody.linearVelocity, "Boosted braking velocity must stay finite.");
            Assert.That(pulsedBody.linearVelocity.z, Is.EqualTo(plainBody.linearVelocity.z).Within(0.05f),
                "An active boost must add no propulsion in brake mode.");

            // The brake phase outlasts the configured duration, so the probe must be unboosted in
            // both cars. A clock that stopped during braking would leave the pulsed car quicker.
            var quiet = new CarInput(0f, false, false, false);
            yield return new WaitForFixedUpdate();
            pulsedMotor.Step(quiet, Time.fixedDeltaTime);
            plainMotor.Step(quiet, Time.fixedDeltaTime);
            for (int tick = 1; tick < ForwardProbeTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                pulsedMotor.Step(quiet, Time.fixedDeltaTime);
                plainMotor.Step(quiet, Time.fixedDeltaTime);
            }

            CarMotorTestRig.AssertFinite(pulsedBody.linearVelocity, "Probed velocity must stay finite.");
            Assert.That(pulsedBody.linearVelocity.z, Is.GreaterThan(0f),
                "The forward probe must build speed, or this comparison proves nothing.");
            Assert.That(pulsedBody.linearVelocity.z, Is.EqualTo(plainBody.linearVelocity.z).Within(0.05f),
                "The boost duration must keep running in brake mode, so it must be spent when forward input returns.");
        }

        [UnityTest]
        public IEnumerator PausedDriver_FreezesTheBoostClock()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("nitro paused body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(new CarInput(0f, false, false, true));
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0f),
                "The tap must start the boost before the pause can freeze its clock.");
            driver.SetInput(new CarInput(0f, false, false, false));

            driver.SetPaused(true);
            float velocityWhenPaused = body.linearVelocity.z;
            float pausedFrom = Time.fixedTime;
            for (int tick = 0; tick < PausedBoostTicks; tick++)
                yield return new WaitForFixedUpdate();
            CarMotorTestRig.AssertFixedClockRan(pausedFrom);
            Assert.That(body.linearVelocity.z, Is.EqualTo(velocityWhenPaused).Within(0.05f),
                "A paused driver applies nothing, so the boost clock is the only thing the probe can prove.");

            // A cooperative pause freezes the boost clock, so the resumed probe must still be
            // boosted: a spent or restarting boost would land under the unboosted curve. The bound
            // is the analytic solution of the motor's own asymptotic pull toward the raised cap,
            // evaluated without the boost, so no absolute integration value is baked in here.
            // 1.8 s of real fixed time pass while paused, which is longer than the configured boost.
            driver.SetPaused(false);
            float beforeProbe = body.linearVelocity.z;
            for (int tick = 0; tick < ResumeProbeTicks; tick++)
                yield return new WaitForFixedUpdate();
            float probeDelta = body.linearVelocity.z - beforeProbe;

            CarMotorTestRig.AssertFinite(body.linearVelocity, "Resumed velocity must stay finite.");
            float probeSeconds = ResumeProbeTicks * Time.fixedDeltaTime;
            float unboostedBound = (definition.maxForwardSpeed - beforeProbe) *
                (1f - Mathf.Exp(-(definition.driveForce / definition.mass)
                    / definition.maxForwardSpeed * probeSeconds));
            Assert.That(unboostedBound, Is.GreaterThan(0f), "The bound must be a real bound, not a trivial zero.");
            Assert.That(probeDelta, Is.GreaterThan(unboostedBound * 1.15f),
                "A cooperative pause must freeze the boost clock, so a resumed probe must still beat the unboosted curve.");
        }
    }
}
