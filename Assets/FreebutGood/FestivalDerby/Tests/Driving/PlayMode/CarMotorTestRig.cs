using System;
using System.Collections;
using System.Collections.Generic;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Shared support for the CarMotor PlayMode fixtures: a caller-owned frictionless floor, body and
    /// definition creation with tracked teardown, finite/clock setup proofs, the sampled motor-owned
    /// tick loop and the shared dead-input fail-safe assertion flow.
    ///
    /// The arcade motor carries the chassis on raycast suspension, so the rig has to give it real
    /// weight and a real surface: bodies stay dynamic, keep gravity on and carry a chassis box, and
    /// the floor is frictionless so a case measures the motor instead of a physics material.
    /// </summary>
    public sealed class CarMotorTestRig : IDisposable
    {
        // Wide enough that no case can drive off the surface: the cap case runs 900 ticks at up to
        // 16 m/s, which is over 200 m of travel from its spawn lane.
        private const float FloorHalfExtent = 1000f;
        private const float ChassisHeight = 0.7f;
        // Body centre that leaves the chassis floor just under the suspension's static quiet
        // position, so a car starts settled instead of dropping or popping on the first step.
        private const float SpawnHeight = 0.43f;
        private const float BodySpacing = 4f;

        private readonly List<Object> _owned = new List<Object>();
        private int _bodyCount;

        private GameObject _floor;

        public CarMotorTestRig()
        {
            EnsureFloor();
        }

        // Fixtures hold one rig for all their cases (field initializer) while every TearDown disposes
        // it, so the floor has to be rebuilt on demand: a case that found no surface would free-fall
        // and the suspension rays would never touch ground.
        private void EnsureFloor()
        {
            if (_floor != null)
                return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "CarMotor rig floor";
            floor.transform.SetPositionAndRotation(new Vector3(0f, -0.5f, 0f), Quaternion.identity);
            floor.transform.localScale = new Vector3(FloorHalfExtent * 2f, 1f, FloorHalfExtent * 2f);
            // Minimum combine against a body with no material keeps tangential friction at zero, so
            // the only force a horizontal assertion can see is the one the motor applied.
            var floorMaterial = new PhysicsMaterial("CarMotor rig floor")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f
            };
            floor.GetComponent<Collider>().sharedMaterial = floorMaterial;
            _floor = floor;
            _owned.Add(floor);
            _owned.Add(floorMaterial);
            // A collider created this frame is invisible to raycasts until the physics scene syncs, and a
            // one-tick case would otherwise run its only suspension step over an empty world.
            Physics.SyncTransforms();
        }

        /// <summary>Extremes and first-tick forward speed sampled by the most recent RunSampled call.</summary>
        public float MaxSpeed { get; private set; }
        public float MinSpeed { get; private set; }
        public float FirstTickSpeed { get; private set; }

        public void Dispose()
        {
            // Caller-owned cleanup only; never touch the scene, other fixtures or project assets.
            foreach (var item in _owned)
                if (item != null)
                    Object.Destroy(item);
            _owned.Clear();
            _floor = null;
            _bodyCount = 0;
        }

        public CarDefinition Definition()
        {
            var definition = ScriptableObject.CreateInstance<CarDefinition>();
            definition.mass = 1000f;
            definition.driveForce = 12000f;
            definition.maxForwardSpeed = 16f;
            definition.maxReverseSpeed = 4f;
            definition.brakeDeceleration = 25f;
            definition.reverseEngageSpeed = 1f;
            definition.maxYawRateDegrees = 75f;
            definition.steeringSmoothing = 8f;
            definition.yawAssist = 0.5f;
            definition.frontGrip = 0.9f;
            definition.rearGrip = 0.65f;
            definition.rearDriftGrip = 0.2f;
            definition.maxLateralForce = 20000f;
            definition.steeringDeadZone = 0.12f;
            definition.suspensionRestLength = 0.18f;
            definition.suspensionSpring = 100000f;
            definition.suspensionDamper = 5500f;
            definition.maxSuspensionForce = 60000f;
            definition.antiRoll = 30000f;
            definition.nitroDuration = 1.5f;
            definition.nitroAccelerationMultiplier = 1.5f;
            definition.nitroTopSpeedMultiplier = 1.25f;
            definition.nitroCooldown = 6f;
            definition.ramLiftFactor = 0.5f;
            definition.ramLiftThreshold = 5f;
            definition.ramLiftMax = 10f;
            definition.ramLiftRollGain = 1.2f;
            definition.ramLiftPitchShare = 0.5f;
            definition.ramLiftMaxSpin = 8f;
            definition.airAngularDamping = 0.2f;
            definition.maxAngularVelocity = 8f;
            _owned.Add(definition);
            return definition;
        }

        public Rigidbody Body(string name, float mass)
        {
            return Body(name, mass, new Vector3(1.6f, ChassisHeight, 3f));
        }

        /// <summary>
        /// The same body with an explicit chassis size. The size is not cosmetic: Unity derives the inertia
        /// tensor from the collider, so a case that is calibrated against a reference box of particular
        /// dimensions has to spawn that box or it is measuring a different car.
        /// </summary>
        public Rigidbody Body(string name, float mass, Vector3 chassisSize)
        {
            EnsureFloor();
            var owner = new GameObject(name);
            _owned.Add(owner);
            // Every rig body gets its own lane, so two cars in one case never share a volume.
            owner.transform.position = new Vector3(-40f + BodySpacing * _bodyCount++, SpawnHeight, 0f);
            var body = owner.AddComponent<Rigidbody>();
            body.mass = mass;
            body.centerOfMass = Vector3.down * (ChassisHeight * 0.45f);
            var chassis = owner.AddComponent<BoxCollider>();
            chassis.size = chassisSize;
            // Same slippery, slightly bouncy chassis material as CarGeometryFactory. With the engine's
            // default friction a rammed car grips the aggressor's face and the contact eats most of the
            // launch and roll, which no live car does.
            var chassisMaterial = new PhysicsMaterial("CarMotor rig chassis")
            {
                staticFriction = 0.1f,
                dynamicFriction = 0.1f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0.15f,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };
            chassis.sharedMaterial = chassisMaterial;
            _owned.Add(chassisMaterial);
            // The collider was just resized, and a raycast in this same frame would still see the shape it
            // had when the physics scene last synced.
            Physics.SyncTransforms();
            // No constraint on any axis: the motor is free to launch, roll and land on its roof.
            return body;
        }

        public static void AssertFinite(Vector3 velocity, string context)
        {
            Assert.That(
                float.IsNaN(velocity.x) || float.IsInfinity(velocity.x)
                || float.IsNaN(velocity.y) || float.IsInfinity(velocity.y)
                || float.IsNaN(velocity.z) || float.IsInfinity(velocity.z),
                Is.False, context);
        }

        public static void AssertFixedClockRan(float fixedTimeBefore)
        {
            // A frozen or zero-tick setup must fail as setup, never as a silent behavior pass.
            Assert.That(Time.fixedTime, Is.GreaterThan(fixedTimeBefore), "Fixed time did not advance; zero ticks ran.");
            Assert.That(Time.fixedDeltaTime, Is.GreaterThan(0f), "A non-positive fixed step cannot drive physics.");
        }

        public IEnumerator RunSampled(Rigidbody body, CarMotor motor, CarInput input, int ticks)
        {
            // The motor owns its tick loop: Step is called explicitly once per fixed tick.
            MaxSpeed = float.NegativeInfinity;
            MinSpeed = float.PositiveInfinity;
            for (int tick = 0; tick < ticks; tick++)
            {
                yield return new WaitForFixedUpdate();
                motor.Step(input, Time.fixedDeltaTime);
                float speed = body.linearVelocity.z;
                AssertFinite(body.linearVelocity, "Velocity must stay finite while stepping.");
                if (tick == 0) FirstTickSpeed = speed;
                if (speed > MaxSpeed) MaxSpeed = speed;
                if (speed < MinSpeed) MinSpeed = speed;
            }
        }

        public IEnumerator AssertDeadInputFailsafe(Rigidbody body, CarMotor motor, float deltaTime, bool releaseKinematic)
        {
            // Only the horizontal pair is compared: the rig is a real, gravity-loaded body, so an
            // invalid step must be proven inert on the axes a driving force would move.
            var baseline = body.linearVelocity;
            Assert.DoesNotThrow(() => motor.Step(new CarInput(0f, false, false, false), deltaTime));
            Assert.That(body.linearVelocity.x, Is.EqualTo(baseline.x).Within(0.0001f), "Velocity must stay unchanged.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(baseline.z).Within(0.0001f), "Velocity must stay unchanged.");
            // A kinematic AddForce accumulates silently and is invisible to immediate reads; releasing
            // the body to dynamic and running one real simulation tick surfaces anything the
            // dead-input step applied or banked.
            if (releaseKinematic)
                body.isKinematic = false;
            float settleFrom = Time.fixedTime;
            yield return new WaitForFixedUpdate();
            AssertFixedClockRan(settleFrom);
            AssertFinite(body.linearVelocity, "Velocity must stay finite after the dead-input step.");
            Assert.That(body.linearVelocity.x, Is.EqualTo(baseline.x).Within(0.001f),
                "The motor must apply and accumulate nothing across the tick.");
            Assert.That(body.linearVelocity.z, Is.EqualTo(baseline.z).Within(0.001f),
                "The motor must apply and accumulate nothing across the tick.");
        }
    }
}
