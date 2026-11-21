using System.Collections.Generic;
using System.Reflection;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>G2a data contract; run EditMode in FestivalDerby.Driving.EditModeTests.</summary>
    public sealed class CarDefinitionTests
    {
        private CarDefinition _definition;

        private static readonly string[] NonNegativeFields =
        {
            nameof(CarDefinition.driveForce), nameof(CarDefinition.maxForwardSpeed),
            nameof(CarDefinition.maxReverseSpeed), nameof(CarDefinition.brakeDeceleration),
            nameof(CarDefinition.reverseEngageSpeed), nameof(CarDefinition.coastDrag),
            nameof(CarDefinition.maxSteerAngleDegrees), nameof(CarDefinition.highSpeedSteerAngleDegrees),
            nameof(CarDefinition.steeringSmoothing), nameof(CarDefinition.maxYawRateDegrees),
            nameof(CarDefinition.yawAssist), nameof(CarDefinition.frontGrip),
            nameof(CarDefinition.rearGrip), nameof(CarDefinition.rearDriftGrip),
            nameof(CarDefinition.maxLateralForce), nameof(CarDefinition.suspensionRestLength),
            nameof(CarDefinition.suspensionSpring), nameof(CarDefinition.suspensionDamper),
            nameof(CarDefinition.maxSuspensionForce), nameof(CarDefinition.antiRoll),
            nameof(CarDefinition.downforceCoefficient), nameof(CarDefinition.airControlTorque),
            nameof(CarDefinition.uprightTorque), nameof(CarDefinition.angularDamping),
            nameof(CarDefinition.airAngularDamping), nameof(CarDefinition.maxAngularVelocity), nameof(CarDefinition.nitroDuration),
            nameof(CarDefinition.nitroCooldown), nameof(CarDefinition.ramLiftFactor),
            nameof(CarDefinition.ramLiftThreshold), nameof(CarDefinition.ramLiftMax),
            nameof(CarDefinition.ramLiftRollGain), nameof(CarDefinition.ramLiftMaxSpin)
        };

        [SetUp]
        public void SetUp()
        {
            // Ephemeral objects isolate validation without creating or dirtying project assets.
            _definition = ScriptableObject.CreateInstance<CarDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_definition);
        }

        private static IEnumerable<TestCaseData> MinimumCases(object target, float minimum, float below)
        {
            yield return new TestCaseData(target, float.NaN, minimum);
            yield return new TestCaseData(target, float.PositiveInfinity, minimum);
            yield return new TestCaseData(target, float.NegativeInfinity, minimum);
            yield return new TestCaseData(target, -1f, minimum);
            yield return new TestCaseData(target, 0f, minimum);
            yield return new TestCaseData(target, below, minimum);
            float validBoundary = minimum == 0f ? 0.0001f : minimum;
            yield return new TestCaseData(target, validBoundary, validBoundary);
            yield return new TestCaseData(target, 2.75f, 2.75f);
            yield return new TestCaseData(target, float.MaxValue, float.MaxValue);
        }

        private static IEnumerable<TestCaseData> ScalarCases()
        {
            foreach (string field in NonNegativeFields)
                foreach (var sample in MinimumCases(field, 0f, -0.0001f))
                    yield return sample;
            foreach (var sample in MinimumCases(nameof(CarDefinition.mass), 1f, 0.9999f))
                yield return sample;
            foreach (var sample in MinimumCases(nameof(CarDefinition.nitroAccelerationMultiplier), 1f, 0.9999f))
                yield return sample;
            foreach (var sample in MinimumCases(nameof(CarDefinition.nitroTopSpeedMultiplier), 1f, 0.9999f))
                yield return sample;
        }

        private static IEnumerable<TestCaseData> BodyCases()
        {
            for (int axis = 0; axis < 3; axis++)
                foreach (var sample in MinimumCases(axis, 0.1f, 0.0999f))
                    yield return sample;
        }

        [TestCaseSource(nameof(ScalarCases))]
        public void Sanitize_ScalarIsFiniteAndRespectsMinimum(string name, float value, float expected)
        {
            FieldInfo field = typeof(CarDefinition).GetField(name);
            field.SetValue(_definition, value);

            _definition.Sanitize();

            Assert.That(field.GetValue(_definition), Is.EqualTo(expected), name);
        }

        [TestCaseSource(nameof(BodyCases))]
        public void Sanitize_EachBodyAxisIsFiniteAndAtLeastOneTenthMetre(int axis, float value, float expected)
        {
            Vector3 original = _definition.bodySize;
            Vector3 size = original;
            size[axis] = value;
            _definition.bodySize = size;

            _definition.Sanitize();

            Assert.That(_definition.bodySize[axis], Is.EqualTo(expected));
            for (int other = 0; other < 3; other++)
                if (other != axis)
                    Assert.That(_definition.bodySize[other], Is.EqualTo(original[other]));
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(0.0001f, 0.0001f)]
        [TestCase(0.9999f, 0.9999f)]
        [TestCase(1f, 1f)]
        [TestCase(1.0001f, 1f)]
        [TestCase(2.75f, 1f)]
        [TestCase(float.MaxValue, 1f)]
        public void Sanitize_DeadZoneIsFiniteAndClampedWithoutRescaling(float value, float expected)
        {
            _definition.steeringDeadZone = value;

            _definition.Sanitize();

            Assert.That(_definition.steeringDeadZone, Is.EqualTo(expected));
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(0.0001f, 0.0001f)]
        [TestCase(0.9999f, 0.9999f)]
        [TestCase(1f, 1f)]
        [TestCase(1.5f, 1.5f)]
        [TestCase(2.75f, 2.75f)]
        public void Sanitize_RollGainIsFiniteAndNonNegativeWithoutRescaling(float value, float expected)
        {
            _definition.ramLiftRollGain = value;

            _definition.Sanitize();

            Assert.That(_definition.ramLiftRollGain, Is.EqualTo(expected));
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1.0001f, 1f)]
        [TestCase(float.MaxValue, 1f)]
        public void Sanitize_PitchShareIsFiniteAndClampedWithoutRescaling(float value, float expected)
        {
            _definition.ramLiftPitchShare = value;

            _definition.Sanitize();

            Assert.That(_definition.ramLiftPitchShare, Is.EqualTo(expected));
        }

        [Test]
        public void ProvisionalDefaults_AreValidAndRemainUnchanged()
        {
            var expected = new Dictionary<string, float>
            {
                { nameof(CarDefinition.mass), 1300f },
                { nameof(CarDefinition.driveForce), 26000f },
                { nameof(CarDefinition.maxForwardSpeed), 26f },
                { nameof(CarDefinition.maxReverseSpeed), 6f },
                { nameof(CarDefinition.brakeDeceleration), 25f },
                { nameof(CarDefinition.reverseEngageSpeed), 1f },
                { nameof(CarDefinition.coastDrag), 0.3f },
                { nameof(CarDefinition.maxSteerAngleDegrees), 30f },
                { nameof(CarDefinition.highSpeedSteerAngleDegrees), 9f },
                { nameof(CarDefinition.steeringSmoothing), 10f },
                { nameof(CarDefinition.maxYawRateDegrees), 90f },
                { nameof(CarDefinition.yawAssist), 0.5f },
                { nameof(CarDefinition.steeringDeadZone), 0.12f },
                { nameof(CarDefinition.frontGrip), 0.9f },
                { nameof(CarDefinition.rearGrip), 0.65f },
                { nameof(CarDefinition.rearDriftGrip), 0.2f },
                { nameof(CarDefinition.maxLateralForce), 20000f },
                { nameof(CarDefinition.suspensionRestLength), 0.18f },
                { nameof(CarDefinition.suspensionSpring), 100000f },
                { nameof(CarDefinition.suspensionDamper), 6000f },
                { nameof(CarDefinition.maxSuspensionForce), 60000f },
                { nameof(CarDefinition.antiRoll), 30000f },
                { nameof(CarDefinition.downforceCoefficient), 8f },
                { nameof(CarDefinition.airControlTorque), 300f },
                { nameof(CarDefinition.uprightTorque), 24000f },
                { nameof(CarDefinition.angularDamping), 2.5f },
                { nameof(CarDefinition.airAngularDamping), 0.2f },
                { nameof(CarDefinition.maxAngularVelocity), 8f },
                { nameof(CarDefinition.nitroDuration), 1.5f },
                { nameof(CarDefinition.nitroAccelerationMultiplier), 1.5f },
                { nameof(CarDefinition.nitroTopSpeedMultiplier), 1.25f },
                { nameof(CarDefinition.nitroCooldown), 6f },
                { nameof(CarDefinition.ramLiftFactor), 0.5f },
                { nameof(CarDefinition.ramLiftThreshold), 5f },
                { nameof(CarDefinition.ramLiftMax), 10f },
                { nameof(CarDefinition.ramLiftRollGain), 1.2f },
                { nameof(CarDefinition.ramLiftPitchShare), 0.5f },
                { nameof(CarDefinition.ramLiftMaxSpin), 8f }
            };
            for (int pass = 0; pass < 2; pass++)
            {
                Assert.That(_definition.bodySize, Is.EqualTo(new Vector3(1.6f, 0.7f, 3f)));
                foreach (var pair in expected)
                    Assert.That(typeof(CarDefinition).GetField(pair.Key).GetValue(_definition),
                        Is.EqualTo(pair.Value), pair.Key);
                _definition.Sanitize();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sanitize_IsIdempotentAfterValidOrCorruptData(bool corrupt)
        {
            FieldInfo[] fields = typeof(CarDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
                if (field.FieldType == typeof(float))
                    field.SetValue(_definition, corrupt ? float.NaN : 2.75f);
            _definition.steeringDeadZone = corrupt ? float.NaN : 0.4f;
            _definition.bodySize = corrupt
                ? new Vector3(-1f, float.PositiveInfinity, float.NaN)
                : new Vector3(0.1f, 2.75f, 4f);

            _definition.Sanitize();

            // Prove normalization first; an empty method is otherwise trivially idempotent.
            Assert.That(_definition.bodySize, Is.EqualTo(corrupt
                ? new Vector3(0.1f, 0.1f, 0.1f) : new Vector3(0.1f, 2.75f, 4f)));
            foreach (string name in NonNegativeFields)
                Assert.That(typeof(CarDefinition).GetField(name).GetValue(_definition),
                    Is.EqualTo(corrupt ? 0f : 2.75f), name);
            Assert.That(_definition.mass, Is.EqualTo(corrupt ? 1f : 2.75f));
            Assert.That(_definition.nitroAccelerationMultiplier, Is.EqualTo(corrupt ? 1f : 2.75f));
            Assert.That(_definition.nitroTopSpeedMultiplier, Is.EqualTo(corrupt ? 1f : 2.75f));
            Assert.That(_definition.steeringDeadZone, Is.EqualTo(corrupt ? 0f : 0.4f));
            var snapshot = new Dictionary<FieldInfo, object>();
            foreach (var field in fields)
                snapshot.Add(field, field.GetValue(_definition));

            _definition.Sanitize();

            foreach (var pair in snapshot)
                Assert.That(pair.Key.GetValue(_definition), Is.EqualTo(pair.Value), pair.Key.Name);
        }

        [Test]
        public void OnValidate_UsesTheSameSanitizationContract()
        {
            _definition.bodySize = new Vector3(float.NaN, -1f, 0f);
            _definition.mass = float.PositiveInfinity;
            _definition.steeringDeadZone = 2f;

            // Invoke only this callback on an ephemeral object, not an Editor asset/import cycle.
            typeof(CarDefinition).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_definition, null);

            Assert.That(_definition.bodySize, Is.EqualTo(new Vector3(0.1f, 0.1f, 0.1f)));
            Assert.That(_definition.mass, Is.EqualTo(1f));
            Assert.That(_definition.steeringDeadZone, Is.EqualTo(1f));
        }
    }
}
