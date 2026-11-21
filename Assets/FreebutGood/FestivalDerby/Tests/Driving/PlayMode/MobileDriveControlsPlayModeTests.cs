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
    /// G3a RED fixture: the scene-owned mobile driving controls against their inert skeleton.
    /// Every case is authored against the frozen contract, so an unimplemented or inert surface
    /// must fail here with a real assertion, never by accident; the absence cases are expected
    /// to pass vacuously in RED and are recorded as non-discriminating, never as coverage.
    /// </summary>
    public sealed class MobileDriveControlsPlayModeTests
    {
        private const int ComposeFrames = 2;
        private const int ModeFrames = 3;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>A fake steer source keeps the steering decisions explicit, with no live joystick.</summary>
        private sealed class FakeSteerSource : ISteerSource
        {
            public float Horizontal { get; set; }
            public float Vertical { get; set; }
            public bool HasInput { get; set; }
        }

        // The control rides on the rig-owned body GameObject, so teardown stays tracked; binding
        // the control is each case's own decision, because some cases bind nothing or bind badly.
        private (LocalCarFixedStepDriver Driver, MobileDriveControls Controls) CreateOnRigBody(string bodyName)
        {
            var definition = _rig.Definition();
            var body = _rig.Body(bodyName, definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            var controls = body.gameObject.AddComponent<MobileDriveControls>();
            return (driver, controls);
        }

        // One wait per fixed frame: the control composes in Update and the driver steps in its
        // own FixedUpdate, so a real frame advances both without ever calling Step by hand.
        private static IEnumerator RunFixedFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
                yield return new WaitForFixedUpdate();
        }

        private static void AssertSteerFinite(float steer, string message)
        {
            // Same predicate CarControlRules and CarMotor use; Mathf has no IsFinite in this editor.
            Assert.That(!float.IsNaN(steer) && !float.IsInfinity(steer), Is.True, message);
        }

        [UnityTest]
        public IEnumerator Bind_RejectsNullDriverOrNullSource_BeforeMutatingState()
        {
            var (driver, controls) = CreateOnRigBody("bind guard body");
            var source = new FakeSteerSource { HasInput = true, Horizontal = 1f };

            Assert.Throws<ArgumentNullException>(() => controls.Bind(null, source),
                "A null driver must be rejected with ArgumentNullException before anything is stored.");
            Assert.Throws<ArgumentNullException>(() => controls.Bind(driver, null),
                "A null steer source must be rejected with ArgumentNullException before anything is stored.");

            // If either failed bind had mutated state, a held flag could still leak from here.
            controls.Set(DriveControlSlot.Brake, true);
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedFrames(ModeFrames);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(controls.CurrentInput.brake, Is.False,
                "A Set after a failed bind must stay invisible in the composed input.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Coast),
                "A Set after a failed bind must not reach any driver, so the driver keeps the released default.");
        }

        [UnityTest]
        public IEnumerator NeutralSource_SendsAZeroSteerRatherThanThrowing()
        {
            var (driver, controls) = CreateOnRigBody("neutral source body");
            // CarRelative reads the stick as pedal-and-wheel, so the setup proof can assert the raw
            // 0.5 reaches the driver un-normalized; the default Direction mode would resolve it to
            // a full-lock 1.0 and the proof would no longer describe this case's intent.
            controls.Configure(JoystickDriveMode.CarRelative, 0.15f, null);
            var live = new FakeSteerSource { HasInput = true, Horizontal = 0.5f };
            controls.Bind(driver, live);

            // Setup proof: a live source must reach the composed input first, so the neutral
            // checks below cannot pass on a control that never composes anything.
            yield return RunFixedFrames(ComposeFrames);
            Assert.That(controls.CurrentInput.steer, Is.EqualTo(0.5f).Within(0.0001f),
                "Setup proof: a live steer source must reach the composed input before the neutral checks.");

            live.HasInput = false;
            yield return RunFixedFrames(ComposeFrames);
            Assert.That(controls.CurrentInput.steer, Is.EqualTo(0f),
                "A source reporting HasInput == false must compose a neutral steer rather than throwing.");

            Assert.DoesNotThrow(() => controls.SetSteerSource(null),
                "A null steer source must be accepted as 'no steering input', never thrown on.");
            yield return RunFixedFrames(ComposeFrames);
            Assert.That(controls.CurrentInput.steer, Is.EqualTo(0f),
                "A bound control with a null source must compose a neutral steer instead of throwing.");
        }

        [UnityTest]
        public IEnumerator Steer_IsBoundedToMinusOneAndOne()
        {
            var (driver, controls) = CreateOnRigBody("steer bound body");
            var source = new FakeSteerSource { HasInput = true };
            controls.Bind(driver, source);
            float[] rawSteers = { 57f, -57f };
            float[] clampedSteers = { 1f, -1f };
            for (int variant = 0; variant < rawSteers.Length; variant++)
            {
                source.Horizontal = rawSteers[variant];
                yield return RunFixedFrames(ComposeFrames);
                AssertSteerFinite(controls.CurrentInput.steer,
                    "The composed steer must stay finite for any raw source value.");
                Assert.That(controls.CurrentInput.steer, Is.EqualTo(clampedSteers[variant]).Within(0.0001f),
                    "A raw steer far outside [-1, 1] must reach the driver clamped to the bound.");
                Assert.That(controls.CurrentInput.steer, Is.InRange(-1f, 1f),
                    "The steer sent to the driver must never leave the [-1, 1] bound.");
            }
        }

        [UnityTest]
        public IEnumerator HoldSlots_DoNotLeakIntoEachOther()
        {
            var (driver, controls) = CreateOnRigBody("slot body");
            // HasInput stays false: the slots alone must own the resolved mode.
            controls.Bind(driver, new FakeSteerSource());

            controls.Set(DriveControlSlot.Brake, true);
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedFrames(ModeFrames);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Brake),
                "A held brake with no steering must resolve the mode to Brake.");

            controls.Set(DriveControlSlot.Brake, false);
            controls.Set(DriveControlSlot.Reverse, true);
            yield return RunFixedFrames(ModeFrames);
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Reverse),
                "Releasing brake and holding reverse must resolve the mode to Reverse; brake must not leak.");

            controls.Set(DriveControlSlot.Reverse, false);
            controls.Set(DriveControlSlot.Nitro, true);
            yield return RunFixedFrames(ModeFrames);
            Assert.That(controls.CurrentInput.nitroPressed, Is.True,
                "The nitro flag must reach the composed input for this mode check to prove anything.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Coast),
                "The nitro flag alone must never put the car in gear: a neutral stick is a release.");
        }

        [UnityTest]
        public IEnumerator FullyDeflectedStick_DrivesWithoutAnyButton()
        {
            var (driver, controls) = CreateOnRigBody("stick drive body");
            var source = new FakeSteerSource { HasInput = true, Vertical = 1f };
            controls.Bind(driver, source);

            // No camera is configured, so the reading falls back to the car's own heading, which
            // makes "up on the stick" mean "where the car already points".
            yield return RunFixedFrames(ModeFrames);
            Assert.That(controls.CurrentInput.throttle, Is.EqualTo(1f).Within(0.0001f),
                "A stick pushed up must reach the composed input as full throttle.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Forward),
                "The stick alone must drive the car: no brake or reverse button is held.");
            Assert.That(controls.CurrentInput.reverse, Is.False,
                "A stick pushed forward must not carry the reverse flag.");

            source.Vertical = -1f;
            yield return RunFixedFrames(ModeFrames);
            Assert.That(controls.CurrentInput.throttle, Is.EqualTo(-1f).Within(0.0001f),
                "A stick pushed down must reach the composed input as a reversed pedal.");
            Assert.That(controls.CurrentInput.reverse, Is.True,
                "A stick pulled down must carry the reverse flag: the V2 motor never reads a negative pedal.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Reverse),
                "A stick pulled down must reverse the car with no reverse button held.");

            // The threshold belongs to the contract: a stick travelling back through centre is a turn,
            // not yet a decision to back up.
            source.Vertical = -0.3f;
            yield return RunFixedFrames(ModeFrames);
            Assert.That(controls.CurrentInput.reverse, Is.False,
                "A shallow pull must not carry the reverse flag.");

            source.Vertical = 0f;
            yield return RunFixedFrames(ModeFrames);
            Assert.That(controls.CurrentInput.throttle, Is.Zero,
                "A centred stick must release the pedal.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Coast),
                "A released pedal must coast: the motor no longer accelerates on its own.");
        }

        [UnityTest]
        public IEnumerator RepeatedReleaseAndReleaseAll_LeaveEverySlotClear()
        {
            var (driver, controls) = CreateOnRigBody("release body");
            // Neutral source: with every slot released, the composed input must come out neutral.
            controls.Bind(driver, new FakeSteerSource());

            Assert.DoesNotThrow(() => controls.Set(DriveControlSlot.Brake, false),
                "Releasing an already clear brake must be harmless.");
            Assert.DoesNotThrow(() => controls.Set(DriveControlSlot.Brake, false),
                "A duplicated release must be idempotent, never an exception.");
            Assert.DoesNotThrow(() => controls.Set(DriveControlSlot.Reverse, false),
                "Releasing an already clear reverse must be harmless.");
            Assert.DoesNotThrow(() => controls.Set(DriveControlSlot.Nitro, false),
                "Releasing an already clear nitro must be harmless.");
            Assert.DoesNotThrow(controls.ReleaseAll, "ReleaseAll must clear every slot without exception.");

            yield return RunFixedFrames(ComposeFrames);
            AssertSteerFinite(controls.CurrentInput.steer,
                "A fully released control must compose a finite steer.");
            Assert.That(controls.CurrentInput.steer, Is.EqualTo(0f),
                "A fully released control with a neutral source must compose a neutral steer.");
            Assert.That(controls.CurrentInput.brake, Is.False, "ReleaseAll must leave the brake slot clear.");
            Assert.That(controls.CurrentInput.reverse, Is.False, "ReleaseAll must leave the reverse slot clear.");
            Assert.That(controls.CurrentInput.nitroPressed, Is.False, "ReleaseAll must leave the nitro slot clear.");
        }

        [UnityTest]
        public IEnumerator UnboundControl_AppliesNothingAndNeverThrows()
        {
            var (driver, controls) = CreateOnRigBody("unbound body");
            // The control is deliberately never bound: the driver exists and keeps stepping,
            // but no control input may reach it.
            Assert.DoesNotThrow(
                () => controls.SetSteerSource(new FakeSteerSource { HasInput = true, Horizontal = 1f }),
                "SetSteerSource on an unbound control must never throw.");
            Assert.DoesNotThrow(controls.ReleaseAll, "ReleaseAll on an unbound control must never throw.");
            // Held from here on: if any path reached a driver, the snapshot mode would flip.
            Assert.DoesNotThrow(() => controls.Set(DriveControlSlot.Brake, true),
                "Set on an unbound control must never throw.");

            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedFrames(ModeFrames);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            AssertSteerFinite(controls.CurrentInput.steer,
                "An unbound control must keep a finite, untouched input.");
            Assert.That(controls.CurrentInput.steer, Is.EqualTo(0f),
                "An unbound control must compose nothing, so the observed input stays neutral.");
            Assert.That(controls.CurrentInput.brake, Is.False, "An unbound control must not hold a brake flag.");
            Assert.That(controls.CurrentInput.reverse, Is.False, "An unbound control must not hold a reverse flag.");
            Assert.That(controls.CurrentInput.nitroPressed, Is.False, "An unbound control must not hold a nitro flag.");
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Coast),
                "An unbound control must reach no driver, so a held brake must not resolve Brake anywhere.");
        }
    }
}
