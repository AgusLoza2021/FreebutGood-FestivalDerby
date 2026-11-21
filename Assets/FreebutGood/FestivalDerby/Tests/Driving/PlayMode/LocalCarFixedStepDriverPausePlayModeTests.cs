using System.Collections;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G2c.3 RED fixture: pause semantics and the Snapshot surface of LocalCarFixedStepDriver on
    /// real fixed ticks. Every case is authored against the frozen contract, so an unimplemented
    /// or inert surface must fail here; assertions are never softened to tidy the RED.
    /// </summary>
    public sealed class LocalCarFixedStepDriverPausePlayModeTests
    {
        private const int SpinUpTicks = 5;
        private const int PausedTicks = 20;
        private const int PauseProbeTicks = 10;
        private const int ResumeTicks = 10;
        private const int ModeTicks = 3;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        // The driver must act inside Unity's own fixed loop, so every case waits out real
        // FixedUpdate calls instead of stepping anything by hand.
        private static IEnumerator RunFixedTicks(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
                yield return new WaitForFixedUpdate();
        }

        private static void AssertComponentFinite(float value, string message)
        {
            // Same predicate CarMotor and CarControlRules use; Mathf has no IsFinite in this editor.
            Assert.That(!float.IsNaN(value) && !float.IsInfinity(value), Is.True, message);
        }

        [Test]
        public void SetPaused_IsIdempotentAndTouchesNothingElse()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("pause-flag body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            Assert.That(driver.IsPaused, Is.False, "A fresh bound driver must start unpaused.");

            driver.SetPaused(true);
            driver.SetPaused(true);
            Assert.That(driver.IsPaused, Is.True, "SetPaused(true) must leave the driver paused.");

            driver.SetPaused(false);
            driver.SetPaused(false);
            Assert.That(driver.IsPaused, Is.False, "SetPaused(false) must leave the driver unpaused.");

            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "Pausing the driver is a simulation decision and must never touch the global time scale.");
            Assert.That(
                body.constraints,
                Is.EqualTo(RigidbodyConstraints.None),
                "Pause must never constrain the body, and the arcade model keeps every tilt axis free.");
        }

        [UnityTest]
        public IEnumerator PausedDriver_AddsNoForceWhileTheFixedClockKeepsRunning()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("paused body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            // A released input coasts now, so the spin-up needs a real forward throttle to move
            // the body at all; the silence check below is meaningless on a car that never moved.
            driver.SetInput(new CarInput(0f, false, false, false));
            yield return RunFixedTicks(SpinUpTicks);
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0f),
                "The silence check cannot be proven on a body that never moved.");

            driver.SetPaused(true);
            float velocityWhenPaused = body.linearVelocity.z;
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(PausedTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while the driver is paused.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(velocityWhenPaused).Within(0.02f),
                "A paused driver must apply no force while the fixed clock keeps running; the tolerance only covers the rig's free-fall landing, not a driving force.");
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "Pausing the driver must never touch the global time scale.");
        }

        [UnityTest]
        public IEnumerator Resume_ContinuesFromThePausedVelocity()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("resuming body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(default);
            yield return RunFixedTicks(SpinUpTicks);

            driver.SetPaused(true);
            yield return RunFixedTicks(PauseProbeTicks);
            float velocityWhenPaused = body.linearVelocity.z;

            driver.SetPaused(false);
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(ResumeTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite after resuming.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(velocityWhenPaused),
                "A resumed driver must keep accelerating from the paused velocity.");
        }

        [UnityTest]
        public IEnumerator Snapshot_ReportsBoundBodyStateAndStopsCountingWhilePaused()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("snapshot body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(default);
            yield return RunFixedTicks(SpinUpTicks);

            CarSnapshot snapshot = driver.Snapshot();
            Assert.That(snapshot.HasBody, Is.True, "A bound driver must report a bound body.");
            AssertComponentFinite(snapshot.Position.x, "Snapshot position X must be finite.");
            AssertComponentFinite(snapshot.Position.y, "Snapshot position Y must be finite.");
            AssertComponentFinite(snapshot.Position.z, "Snapshot position Z must be finite.");
            AssertComponentFinite(snapshot.Rotation.x, "Snapshot rotation X must be finite.");
            AssertComponentFinite(snapshot.Rotation.y, "Snapshot rotation Y must be finite.");
            AssertComponentFinite(snapshot.Rotation.z, "Snapshot rotation Z must be finite.");
            AssertComponentFinite(snapshot.Rotation.w, "Snapshot rotation W must be finite.");
            CarMotorTestRig.AssertFinite(snapshot.LinearVelocity, "Snapshot linear velocity must be finite.");
            Assert.That(snapshot.LinearVelocity.z, Is.EqualTo(body.linearVelocity.z).Within(0.0001f),
                "The snapshot must report the live body velocity, not a cached or default one.");
            Assert.That(Mathf.Abs(snapshot.LinearVelocity.y), Is.LessThan(0.1f),
                "The rig floor holds the car on its suspension, so the vertical velocity must stay settled.");
            Assert.That(snapshot.IsPaused, Is.False,
                "The driver was never paused before this snapshot, so it must report itself unpaused.");
            // Never a hard-coded tick count, so a catch-up frame cannot flake this case.
            Assert.That(snapshot.FixedStepCount, Is.GreaterThan(0),
                "The driver has already run real fixed ticks, so the count must reflect them.");

            int countBeforePause = snapshot.FixedStepCount;
            driver.SetPaused(true);
            yield return RunFixedTicks(PauseProbeTicks);
            CarSnapshot paused = driver.Snapshot();
            Assert.That(paused.IsPaused, Is.True, "A paused driver must report itself paused.");
            Assert.That(paused.FixedStepCount, Is.EqualTo(countBeforePause),
                "A paused driver must not advance the step counter at all.");
        }

        [UnityTest]
        public IEnumerator Snapshot_ReportsTheModeTheMotorActuallyResolved()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("mode body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(default);
            yield return RunFixedTicks(ModeTicks);
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Coast),
                "A released input must resolve to Coast in the snapshot: the car has no drive of its own.");

            // Coast is not the enum's zero value, so a never-written snapshot field cannot satisfy
            // the assertion above; the release case is discriminating on its own now.
            driver.SetInput(new CarInput(0f, 1f, false, false, false));
            yield return RunFixedTicks(ModeTicks);
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Forward),
                "A full-throttle input must resolve to Forward in the snapshot.");

            driver.SetInput(new CarInput(0f, true, false, false));
            yield return RunFixedTicks(ModeTicks);
            Assert.That(driver.Snapshot().ResolvedMode, Is.EqualTo(CarControlMode.Brake),
                "A brake input must resolve to Brake in the snapshot.");
        }

        [Test]
        public void Snapshot_ResetsWhenUnbound()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("unbound snapshot body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            Assert.That(driver.Snapshot().HasBody, Is.True, "A bound driver must report a bound body.");

            driver.Unbind();
            CarSnapshot snapshot = driver.Snapshot();
            Assert.That(snapshot.HasBody, Is.False, "An unbound driver must report no body.");
            Assert.That(snapshot.FixedStepCount, Is.EqualTo(0), "Unbinding must reset the step count.");
            Assert.That(snapshot.IsPaused, Is.False, "Unbinding must reset the pause flag.");
        }
    }
}
