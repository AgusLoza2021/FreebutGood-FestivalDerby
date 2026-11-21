using System;
using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// RED fixture for the arcade ram response: a fast car that drives into a rival must pop that
    /// rival upward, and nothing else may. The selling point of the handling pass is that other
    /// cars can launch and flip you, so the cases below pin the lift, its threshold gate, the
    /// rival-only rule and the fact that the aggressor itself is never lifted.
    ///
    /// Every case is a real physics collision between two boxed cars on the shared frictionless rig
    /// floor, so no callback is invoked by hand and the impulse is measured on the body that
    /// received it.
    /// </summary>
    public sealed class CarRamLiftPlayModeTests
    {
        // Both cars share one lane so a real contact happens; the aggressor starts with its bumper
        // 0.4 m short of the victim's rear face (3 m long chassis, so centres sit 3.4 m apart).
        // Starting inside the victim would be a depenetration launch, not a ram.
        private const float LaneX = 0f;
        private const float SpawnHeight = 0.43f;
        private const float VictimStartZ = 0f;
        private const float AggressorStartZ = -3.4f;
        private const float FastApproachSpeed = 12f;
        private const float SlowApproachSpeed = 3f;
        // A T-bone: the aggressor drives along +X into the victim's side. The 2.0 m centre offset
        // leaves a 0.4 m gap between the aggressor's +X face and the victim's -X side face.
        private const float SideApproachSpeed = 20f;
        private const float SideAggressorX = -2.1f;
        // The same side hit, but aimed at the victim's rear corner so the contact lever also pitches.
        private const float FlipApproachSpeed = 25f;
        private const float RearCornerAggressorZ = -1.2f;
        private const int CollisionTicks = 26;
        private const int SettleTicks = 15;

        private readonly CarMotorTestRig _rig = new CarMotorTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        [Test]
        public void Bind_RejectsMissingBodyOrDefinition()
        {
            var definition = _rig.Definition();
            var body = _rig.Body("bind guard body", definition.mass);
            var lift = body.gameObject.AddComponent<CarRamLift>();
            Assert.Throws<ArgumentNullException>(() => lift.Bind(null, definition));
            Assert.Throws<ArgumentNullException>(() => lift.Bind(body, null));
        }

        [UnityTest]
        public IEnumerator Ram_AboveThreshold_LiftsTheVictimAndNotTheAggressor()
        {
            var definition = _rig.Definition();
            var victim = AddCar("ram victim", definition, VictimStartZ);
            var aggressor = AddCar("ram aggressor", definition, AggressorStartZ);
            yield return Settle();
            aggressor.linearVelocity = new Vector3(0f, 0f, FastApproachSpeed);
            Assert.That(FastApproachSpeed, Is.GreaterThan(definition.ramLiftThreshold),
                "Setup must close faster than the configured ram threshold.");

            float victimLift = 0f;
            float aggressorLift = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                victimLift = Mathf.Max(victimLift, victim.linearVelocity.y);
                aggressorLift = Mathf.Max(aggressorLift, aggressor.linearVelocity.y);
            }

            CarMotorTestRig.AssertFinite(victim.linearVelocity, "Victim velocity must stay finite after a ram.");
            CarMotorTestRig.AssertFinite(aggressor.linearVelocity, "Aggressor velocity must stay finite after a ram.");
            Assert.That(victimLift, Is.GreaterThan(1f),
                "A car rammed above the threshold must be lifted off the ground.");
            Assert.That(aggressorLift, Is.LessThan(0.5f),
                "Only the rammed car is lifted: the aggressor must stay on the ground.");
        }

        [UnityTest]
        public IEnumerator Ram_BelowThreshold_PushesWithoutLifting()
        {
            var definition = _rig.Definition();
            var victim = AddCar("slow victim", definition, VictimStartZ);
            var aggressor = AddCar("slow aggressor", definition, AggressorStartZ);
            yield return Settle();
            aggressor.linearVelocity = new Vector3(0f, 0f, SlowApproachSpeed);
            Assert.That(SlowApproachSpeed, Is.LessThan(definition.ramLiftThreshold),
                "Setup must close slower than the configured ram threshold.");

            float victimLift = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                victimLift = Mathf.Max(victimLift, victim.linearVelocity.y);
            }

            CarMotorTestRig.AssertFinite(victim.linearVelocity, "Victim velocity must stay finite after a slow hit.");
            Assert.That(victim.linearVelocity.z, Is.GreaterThan(0.1f),
                "The slow hit must still have happened, or the lift assertion proves nothing.");
            Assert.That(victimLift, Is.LessThan(0.5f),
                "A hit under the closing-speed threshold must push, never launch.");
        }

        [UnityTest]
        public IEnumerator Ram_AgainstACarWithoutTheComponent_DoesNotLift()
        {
            var definition = _rig.Definition();
            var obstacle = _rig.Body("ram obstacle", definition.mass);
            Vector3 obstacleStart = new Vector3(LaneX, SpawnHeight, VictimStartZ);
            obstacle.transform.position = obstacleStart;
            obstacle.isKinematic = true;
            var aggressor = AddCar("obstacle aggressor", definition, AggressorStartZ);
            yield return Settle();
            aggressor.linearVelocity = new Vector3(0f, 0f, FastApproachSpeed);

            float aggressorLift = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                aggressorLift = Mathf.Max(aggressorLift, aggressor.linearVelocity.y);
            }

            CarMotorTestRig.AssertFinite(aggressor.linearVelocity, "Aggressor velocity must stay finite after a wall hit.");
            Assert.That(aggressor.linearVelocity.z, Is.LessThan(1f),
                "The wall hit must have happened, or the lift assertion proves nothing.");
            Assert.That((obstacle.position - obstacleStart).sqrMagnitude, Is.LessThan(1e-10f),
                "A rival without the ram component must never be launched.");
            Assert.That(aggressorLift, Is.LessThan(0.5f),
                "Ramming a plain object must never lift either body.");
        }

        // A side ram lifts the victim clear of the ground and rolls it, and the roll never exceeds the
        // configured spin cap because CarRamLift tightens the victim's own angular limit to that cap.
        [UnityTest]
        public IEnumerator SideRam_AboveThreshold_LiftsAndRollsTheVictimWithinTheSpinCap()
        {
            var definition = _rig.Definition();
            var victim = AddCarAt("side victim", definition, new Vector3(LaneX, SpawnHeight, VictimStartZ));
            var aggressor = AddCarAt("side aggressor", definition,
                new Vector3(SideAggressorX, SpawnHeight, VictimStartZ));
            yield return Settle();
            aggressor.linearVelocity = new Vector3(SideApproachSpeed, 0f, 0f);
            Assert.That(SideApproachSpeed, Is.GreaterThan(definition.ramLiftThreshold),
                "Setup must close faster than the configured ram threshold.");

            float apex = victim.position.y;
            float maxSpin = 0f;
            float aggressorLift = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                apex = Mathf.Max(apex, victim.position.y);
                maxSpin = Mathf.Max(maxSpin, victim.angularVelocity.magnitude);
                aggressorLift = Mathf.Max(aggressorLift, aggressor.linearVelocity.y);
            }

            CarMotorTestRig.AssertFinite(victim.linearVelocity, "Victim velocity must stay finite after a side ram.");
            CarMotorTestRig.AssertFinite(victim.angularVelocity, "Victim spin must stay finite after a side ram.");
            Assert.That(apex, Is.GreaterThan(1f),
                "A T-bone above the threshold must throw the victim more than a metre into the air.");
            Assert.That(maxSpin, Is.GreaterThan(1f),
                "A T-bone must roll the victim, not just lift it flat.");
            Assert.That(maxSpin, Is.LessThanOrEqualTo(definition.ramLiftMaxSpin + 0.05f),
                "The ram must never spin the victim faster than the configured cap.");
            // A tumbling victim scrapes over the aggressor's bumper, which can nudge it up by about a
            // metre per second; the launch itself is several times that, so 1.5 still proves the
            // aggressor is not launched.
            Assert.That(aggressorLift, Is.LessThan(1.5f),
                "Only the rammed car is launched: the aggressor must stay on the ground.");
        }

        // A high-speed corner ram couples lift, roll and pitch, so the victim tumbles. Measuring the
        // tilt of its up axis covers both axes and is what a player reads as a flip.
        [UnityTest]
        public IEnumerator CornerRam_AtHighSpeed_FlipsTheVictimPastNinetyDegrees()
        {
            var definition = _rig.Definition();
            var victim = AddCarAt("corner victim", definition, new Vector3(LaneX, SpawnHeight, VictimStartZ));
            var aggressor = AddCarAt("corner aggressor", definition,
                new Vector3(SideAggressorX, SpawnHeight, RearCornerAggressorZ));
            yield return Settle();
            aggressor.linearVelocity = new Vector3(FlipApproachSpeed, 0f, 0f);
            Assert.That(FlipApproachSpeed, Is.GreaterThan(definition.ramLiftThreshold),
                "Setup must close faster than the configured ram threshold.");

            float maxTilt = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(victim.transform.up, Vector3.up));
            }

            CarMotorTestRig.AssertFinite(victim.angularVelocity, "Victim spin must stay finite after a corner ram.");
            Assert.That(maxTilt, Is.GreaterThan(90f),
                "A 25 m/s corner ram must roll the victim past 90 degrees; measured max tilt was " + maxTilt);
        }

        [UnityTest]
        public IEnumerator SideRam_BelowThreshold_PushesWithoutLifting()
        {
            var definition = _rig.Definition();
            var victim = AddCarAt("slow side victim", definition, new Vector3(LaneX, SpawnHeight, VictimStartZ));
            var aggressor = AddCarAt("slow side aggressor", definition,
                new Vector3(SideAggressorX, SpawnHeight, VictimStartZ));
            yield return Settle();
            aggressor.linearVelocity = new Vector3(SlowApproachSpeed, 0f, 0f);
            Assert.That(SlowApproachSpeed, Is.LessThan(definition.ramLiftThreshold),
                "Setup must close slower than the configured ram threshold.");

            float victimLift = 0f;
            for (int tick = 0; tick < CollisionTicks; tick++)
            {
                yield return new WaitForFixedUpdate();
                victimLift = Mathf.Max(victimLift, victim.linearVelocity.y);
            }

            CarMotorTestRig.AssertFinite(victim.linearVelocity, "Victim velocity must stay finite after a slow side hit.");
            Assert.That(victim.linearVelocity.x, Is.GreaterThan(0.1f),
                "The slow side hit must still have happened, or the lift assertion proves nothing.");
            Assert.That(victimLift, Is.LessThan(0.5f),
                "A side hit under the closing-speed threshold must push, never launch.");
        }

        // Cars spawn a few centimetres above the floor and drop onto it. A ram has to meet cars at rest,
        // not mid-landing, or the landing velocity swallows the lift the case is measuring.
        private static IEnumerator Settle()
        {
            for (int tick = 0; tick < SettleTicks; tick++)
                yield return new WaitForFixedUpdate();
        }

        private Rigidbody AddCar(string name, CarDefinition definition, float startZ)
        {
            return AddCarAt(name, definition, new Vector3(LaneX, SpawnHeight, startZ));
        }

        private Rigidbody AddCarAt(string name, CarDefinition definition, Vector3 position)
        {
            var body = _rig.Body(name, definition.mass);
            body.transform.position = position;
            // The live game builds cars with continuous detection, and a 20+ m/s side ram would
            // otherwise tunnel or depenetrate a whole tick's travel before the contact is seen.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.gameObject.AddComponent<CarRamLift>().Bind(body, definition);
            return body;
        }
    }
}
