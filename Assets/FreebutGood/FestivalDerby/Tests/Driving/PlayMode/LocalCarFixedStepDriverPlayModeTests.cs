using System;
using System.Collections;
using System.Reflection;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G2c.2 RED fixture: bind/unbind ownership, unconstrained rotation, latest-input semantics and
    /// real fixed-tick propulsion for LocalCarFixedStepDriver while it is an intentional no-op
    /// stub. The driver always lives on a rig body so rig teardown owns every GameObject, and the
    /// fixture never drives the motor by hand: acceleration must prove the driver drives itself.
    /// </summary>
    public sealed class LocalCarFixedStepDriverPlayModeTests
    {
        private const int RejectionTicks = 10;
        private const int AccelerationTicks = 30;
        private const int ReverseTicks = 2;
        private const int SteeringApproachTicks = 30;
        private const int SteeringTicks = 10;
        private const int SilenceTicks = 20;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        // The driver must act inside Unity's own fixed loop, so every case waits out real
        // FixedUpdate calls instead of simulating anything by hand.
        private static IEnumerator RunFixedTicks(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
                yield return new WaitForFixedUpdate();
        }

        [TestCase(true)]  // A missing body must be rejected before any state is stored.
        [TestCase(false)] // A missing definition must be rejected just as hard.
        public void Bind_RejectsNullBodyOrDefinition(bool nullBody)
        {
            var body = nullBody ? null : _rig.Body("bind-null body", 1000f);
            var definition = nullBody ? _rig.Definition() : null;
            // The driver component still needs a host even when the body itself is the rejected argument.
            var host = _rig.Body("bind-null host", 1000f);
            var driver = host.gameObject.AddComponent<LocalCarFixedStepDriver>();
            Assert.Throws<ArgumentNullException>(() => driver.Bind(body, definition));
            if (body != null)
                Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints.None),
                    "A rejected bind must not change the body constraints.");
        }

        [UnityTest]
        public IEnumerator Bind_RejectsBodyOnForeignGameObject()
        {
            var definition = _rig.Definition();
            var owner = _rig.Body("bind owner", definition.mass);
            var foreign = _rig.Body("foreign body", definition.mass);
            var driver = owner.gameObject.AddComponent<LocalCarFixedStepDriver>();
            Assert.Throws<ArgumentException>(() => driver.Bind(foreign, definition),
                "A driver must reject a body that lives on another GameObject.");
            Assert.That(foreign.constraints, Is.EqualTo(RigidbodyConstraints.None),
                "A rejected bind must not change the foreign body constraints.");
            driver.SetInput(new CarInput(0f, false, false, false));
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(RejectionTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(foreign.linearVelocity, "Velocity must stay finite on the rejected body.");
            Assert.That(foreign.linearVelocity.z, Is.EqualTo(0f).Within(0.01f),
                "A rejected bind must never drive the foreign body; the tolerance only covers the rig's free-fall landing.");
        }

        [Test]
        public void Driver_IsSingleComponentWithDisallowMultipleComponent()
        {
            var body = _rig.Body("single body", 1000f);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            Assert.That(
                Attribute.IsDefined(typeof(LocalCarFixedStepDriver), typeof(DisallowMultipleComponent)),
                Is.True, "The driver must reject duplicate instances by attribute.");
            Assert.That(body.gameObject.GetComponent<LocalCarFixedStepDriver>(), Is.SameAs(driver));
        }

        [UnityTest]
        public IEnumerator BoundDriver_AcceleratesOnRealFixedTicksWithoutManualStep()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("accelerating body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(new CarInput(0f, false, false, false));
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(AccelerationTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while the driver accelerates.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0.5f),
                "The bound driver must accelerate the body on real fixed ticks without a manual Step call.");
        }

        [UnityTest]
        public IEnumerator SetInput_LatestInputDrivesNextTick()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("latest-input body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(new CarInput(0f, false, true, false));
            float reverseFrom = Time.fixedTime;
            yield return RunFixedTicks(ReverseTicks);
            CarMotorTestRig.AssertFixedClockRan(reverseFrom);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while reversing.");
            Assert.That(body.linearVelocity.z, Is.LessThan(0f),
                "The latest stored input must drive the next fixed ticks.");

            // Tire forces turn a moving car, so the steer assertion needs forward speed first:
            // steering a car still rolling backwards would damp its yaw instead of adding it.
            driver.SetInput(new CarInput(0f, false, false, false));
            yield return RunFixedTicks(SteeringApproachTicks);
            Assert.That(body.linearVelocity.z, Is.GreaterThan(2f),
                "Setup must build forward speed before the steering assertion.");

            driver.SetInput(new CarInput(1f, false, false, false));
            float steeringFrom = Time.fixedTime;
            yield return RunFixedTicks(SteeringTicks);
            CarMotorTestRig.AssertFixedClockRan(steeringFrom);
            CarMotorTestRig.AssertFinite(body.angularVelocity, "Angular velocity must stay finite while steering.");
            Assert.That(body.angularVelocity.y, Is.GreaterThan(0f),
                "A newer stored input must replace the previous one.");
        }

        [UnityTest]
        public IEnumerator Unbind_ClearsInputRestoresConstraintsAndKeepsBodyIdentity()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("unbind body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            Assert.That(
                body.constraints,
                Is.EqualTo(RigidbodyConstraints.None),
                "Binding must never constrain rotation: the arcade model lets a car launch and roll.");
            // A released input coasts now, so the binding tick needs a real forward throttle to
            // impart the speed this silence check is built on.
            driver.SetInput(new CarInput(0f, false, false, false));
            yield return RunFixedTicks(1);
            driver.Unbind();
            Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints.None),
                "Unbind must leave the body's constraints exactly as it found them.");
            // The binding tick was required to impart speed, and the contract forbids Unbind from
            // clearing the caller's momentum, so silence is proven as a delta, never as a return to zero.
            float velocityAtUnbind = body.linearVelocity.z;
            Assert.That(velocityAtUnbind, Is.GreaterThan(0f),
                "The active tick must impart speed, or this silence check proves nothing.");
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(SilenceTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite after unbind.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(velocityAtUnbind).Within(0.01f),
                "An unbound driver must not change the caller's velocity; the tolerance only covers the rig's free-fall landing.");
            Assert.That(body, Is.Not.Null);
            Assert.That(body.gameObject.activeInHierarchy, Is.True,
                "Unbind must not destroy or disable the caller-owned body.");
        }

        [UnityTest]
        public IEnumerator DisabledDriver_AppliesNoForce()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("disabled body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);
            driver.SetInput(default);
            driver.enabled = false;
            Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints.None),
                "A disabled driver must leave the body's constraints untouched.");
            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(SilenceTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while the driver is disabled.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.01f),
                "A disabled driver must apply no force; the tolerance only covers the rig's free-fall landing.");
        }

        // H2f.U0 test-first sampler contract. The planned production seam is public
        // SetInputSampler(Func<float, CarInput>); it does not exist yet, so every case admits it
        // by reflection and the first real PlayMode run must RED on the missing capability,
        // never on a compile error. Callbacks capture only pre-allocated state and allocate
        // nothing inside the tick.
        private static Action<Func<float, CarInput>> ReflectSamplerInstaller(LocalCarFixedStepDriver driver)
        {
            var method = typeof(LocalCarFixedStepDriver).GetMethod(
                "SetInputSampler",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Func<float, CarInput>) },
                modifiers: null);
            Assert.That(method, Is.Not.Null,
                "SetInputSampler(Func<float, CarInput>) must exist as the planned public sampler seam (expected first RED).");
            // The closed delegate is built once here: invoking it later allocates nothing per tick.
            return (Action<Func<float, CarInput>>)Delegate.CreateDelegate(
                typeof(Action<Func<float, CarInput>>), driver, method);
        }

        [UnityTest]
        public IEnumerator SetInputSampler_SamplerWinsOverSetInputOncePerUnpausedFixedTick()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("sampler body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);

            var sampledInput = new CarInput(1f, false, false, false);
            int sampleCalls = 0;
            float sampledDt = -1f;
            Func<float, CarInput> sampler = dt => { sampleCalls++; sampledDt = dt; return sampledInput; };
            ReflectSamplerInstaller(driver)(sampler);
            // A stored human brake must lose while the sampler requests forward motion.
            driver.SetInput(new CarInput(0f, true, false, false));

            float fixedTimeBefore = Time.fixedTime;
            yield return RunFixedTicks(AccelerationTicks);
            CarMotorTestRig.AssertFixedClockRan(fixedTimeBefore);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite while the sampler drives.");
            Assert.That(sampleCalls, Is.EqualTo(driver.Snapshot().FixedStepCount),
                "The sampler must run exactly once per applied fixed step, paired with the motor.");
            Assert.That(sampledDt, Is.EqualTo(Time.fixedDeltaTime).Within(1e-6f),
                "The sampler must receive the same fixed dt the motor is stepped with.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0.5f),
                "An installed sampler must win over SetInput and drive the motor on real fixed ticks.");
        }

        [UnityTest]
        public IEnumerator SetInputSampler_PausedOrUnboundDriverIsNeverSampled()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("sampler-pause body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);

            var sampledInput = new CarInput(1f, false, false, false);
            int sampleCalls = 0;
            Func<float, CarInput> sampler = dt => { sampleCalls++; return sampledInput; };
            ReflectSamplerInstaller(driver)(sampler);
            driver.SetPaused(true);

            float pausedFrom = Time.fixedTime;
            yield return RunFixedTicks(SilenceTicks);
            CarMotorTestRig.AssertFixedClockRan(pausedFrom);
            Assert.That(sampleCalls, Is.EqualTo(0), "A paused driver must never sample.");
            Assert.That(driver.Snapshot().FixedStepCount, Is.EqualTo(0),
                "Paused ticks must neither step nor count.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.01f),
                "A paused driver must not move the body; the tolerance only covers the rig's free-fall landing.");

            driver.SetPaused(false);
            yield return RunFixedTicks(1);
            Assert.That(sampleCalls, Is.GreaterThanOrEqualTo(1), "Unpausing must resume sampling.");

            driver.Unbind();
            int callsAtUnbind = sampleCalls;
            yield return RunFixedTicks(SilenceTicks);
            Assert.That(sampleCalls, Is.EqualTo(callsAtUnbind),
                "An unbound driver must not call a still-installed sampler.");
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0f),
                "Unbind preserves the caller's momentum, so the frozen count is proven on a moving body.");
        }

        [UnityTest]
        public IEnumerator SetInputSampler_UnbindDropsStaleSamplerAndNullRestoresSetInput()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("sampler-rebind body", definition.mass);
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition);

            var sampledInput = new CarInput(1f, false, false, false);
            int sampleCalls = 0;
            Func<float, CarInput> sampler = dt => { sampleCalls++; return sampledInput; };
            ReflectSamplerInstaller(driver)(sampler);
            // Stored reverse input must lose while the forward sampler is installed.
            driver.SetInput(new CarInput(0f, false, true, false));
            float reverseFrom = Time.fixedTime;
            yield return RunFixedTicks(ReverseTicks);
            CarMotorTestRig.AssertFixedClockRan(reverseFrom);
            Assert.That(body.linearVelocity.z, Is.GreaterThan(0f),
                "The installed sampler must win over the stored human input.");

            driver.Unbind();
            driver.Bind(body, definition);
            int callsAtRebind = sampleCalls;
            yield return RunFixedTicks(SilenceTicks);
            Assert.That(body.constraints,
                Is.EqualTo(RigidbodyConstraints.None),
                "A rebind must never re-freeze rotation.");
            Assert.That(sampleCalls, Is.EqualTo(callsAtRebind),
                "A rebind must not resurrect a sampler installed before Unbind.");

            // Exercise clearing an installed sampler, not only a callback already removed by Unbind.
            ReflectSamplerInstaller(driver)(sampler);
            ReflectSamplerInstaller(driver)(null);
            float zAtClear = body.linearVelocity.z;
            driver.SetInput(new CarInput(0f, false, true, false));
            float restoredFrom = Time.fixedTime;
            yield return RunFixedTicks(SteeringTicks);
            CarMotorTestRig.AssertFixedClockRan(restoredFrom);
            CarMotorTestRig.AssertFinite(body.linearVelocity, "Velocity must stay finite after the sampler is cleared.");
            Assert.That(sampleCalls, Is.EqualTo(callsAtRebind),
                "A cleared sampler must never be called again.");
            // Momentum carries no drag here (proven by the unbind silence case), so a strictly
            // lower z can only come from the restored human reverse input, not from decay.
            Assert.That(body.linearVelocity.z, Is.LessThan(zAtClear),
                "SetInputSampler(null) must restore SetInput's latest-wins human input.");
        }
    }
}
