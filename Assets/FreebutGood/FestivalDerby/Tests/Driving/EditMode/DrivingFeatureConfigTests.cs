using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.Driving.Tests
{
    public sealed class DrivingFeatureConfigTests
    {
        private DrivingFeatureConfig _config;
        private CarDefinition _selectedCar;
        private CarDefinition _fallbackCar;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DrivingFeatureConfig>();
            _selectedCar = ScriptableObject.CreateInstance<CarDefinition>();
            _fallbackCar = ScriptableObject.CreateInstance<CarDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_selectedCar);
            Object.DestroyImmediate(_fallbackCar);
        }

        [Test]
        public void ResolveCar_ChosenCarWinsOverSceneFallback()
        {
            _config.Car = _selectedCar;

            CarDefinition resolved = DrivingFeatureConfig.ResolveCar(_config, _fallbackCar);

            Assert.That(resolved, Is.SameAs(_selectedCar));
        }

        [Test]
        public void ResolveCar_MissingConfigOrSelectionPreservesSceneFallback()
        {
            Assert.That(_config.Car, Is.Null);
            Assert.That(DrivingFeatureConfig.ResolveCar(null, _fallbackCar),
                Is.SameAs(_fallbackCar));
            Assert.That(DrivingFeatureConfig.ResolveCar(_config, _fallbackCar),
                Is.SameAs(_fallbackCar));
        }

        [Test]
        public void ResolveCamera_MissingConfigAndNewConfigKeepLegacyFraming()
        {
            Assert.That(_config.CameraOffset, Is.EqualTo(new Vector3(-12f, 11f, -8f)));
            Assert.That(_config.LookHeight, Is.EqualTo(2f));
            Assert.That(_config.FollowSharpness, Is.EqualTo(10f));
            AssertLegacyFraming(DrivingFeatureConfig.ResolveCamera(null));
            AssertLegacyFraming(DrivingFeatureConfig.ResolveCamera(_config));
        }

        [Test]
        public void ResolveCamera_ExplicitValidConfigOverridesAllThreeParameters()
        {
            _config.CameraOffset = new Vector3(-7f, 6f, -4f);
            _config.LookHeight = 1.25f;
            _config.FollowSharpness = 5f;

            DrivingCameraSettings resolved = DrivingFeatureConfig.ResolveCamera(_config);

            Assert.That(resolved.Offset, Is.EqualTo(new Vector3(-7f, 6f, -4f)));
            Assert.That(resolved.LookHeight, Is.EqualTo(1.25f));
            Assert.That(resolved.FollowSharpness, Is.EqualTo(5f));
        }

        [Test]
        public void ResolveCamera_NaNOffsetFallsBackWithoutMutatingConfigOrCarDefinition()
        {
            _config.Car = _fallbackCar;
            _config.CameraOffset = new Vector3(float.NaN, 6f, -4f);
            _config.LookHeight = 1.25f;
            _config.FollowSharpness = 5f;
            // Invalid physical tuning detects accidental sanitization of the scene definition.
            _fallbackCar.mass = -23f;
            _fallbackCar.bodySize = new Vector3(-1f, 2f, 3f);
            string originalCarFields = JsonUtility.ToJson(_fallbackCar);

            DrivingCameraSettings resolved = DrivingFeatureConfig.ResolveCamera(_config);

            AssertLegacyFraming(resolved);
            Assert.That(_config.Car, Is.SameAs(_fallbackCar));
            Assert.That(float.IsNaN(_config.CameraOffset.x), Is.True);
            Assert.That(_config.CameraOffset.y, Is.EqualTo(6f));
            Assert.That(_config.CameraOffset.z, Is.EqualTo(-4f));
            Assert.That(_config.LookHeight, Is.EqualTo(1.25f));
            Assert.That(_config.FollowSharpness, Is.EqualTo(5f));
            Assert.That(_fallbackCar.mass, Is.EqualTo(-23f));
            Assert.That(_fallbackCar.bodySize, Is.EqualTo(new Vector3(-1f, 2f, 3f)));
            Assert.That(JsonUtility.ToJson(_fallbackCar), Is.EqualTo(originalCarFields));
        }

        [Test]
        public void ResolveDriveMode_DefaultsToDirectionAndHonoursCarRelative()
        {
            Assert.That(_config.DriveMode, Is.EqualTo(JoystickDriveMode.Direction));
            Assert.That(DrivingFeatureConfig.ResolveDriveMode(null), Is.EqualTo(JoystickDriveMode.Direction));
            Assert.That(DrivingFeatureConfig.ResolveDriveMode(_config), Is.EqualTo(JoystickDriveMode.Direction));

            _config.DriveMode = JoystickDriveMode.CarRelative;

            Assert.That(DrivingFeatureConfig.ResolveDriveMode(_config), Is.EqualTo(JoystickDriveMode.CarRelative),
                "The alternative reading must survive the resolver the bootstrap calls.");
        }

        [Test]
        public void ResolveStickDeadZone_DefaultsAndRejectsUnusableRings()
        {
            Assert.That(_config.StickDeadZone, Is.EqualTo(0.15f));
            Assert.That(DrivingFeatureConfig.ResolveStickDeadZone(null), Is.EqualTo(0.15f));
            Assert.That(DrivingFeatureConfig.ResolveStickDeadZone(_config), Is.EqualTo(0.15f));

            foreach (float usable in new[] { 0f, 0.05f, 0.5f, 0.89f })
            {
                _config.StickDeadZone = usable;
                Assert.That(DrivingFeatureConfig.ResolveStickDeadZone(_config), Is.EqualTo(usable));
            }

            // A ring this big would swallow the whole stick, so the car could never be driven.
            foreach (float unusable in new[] { -0.01f, 0.9f, 1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                _config.StickDeadZone = unusable;
                Assert.That(DrivingFeatureConfig.ResolveStickDeadZone(_config), Is.EqualTo(0.15f),
                    $"A dead zone of {unusable} must fall back to the authored default.");
            }
        }

        private static void AssertLegacyFraming(DrivingCameraSettings settings)
        {
            Assert.That(settings.Offset, Is.EqualTo(new Vector3(-12f, 11f, -8f)));
            Assert.That(settings.LookHeight, Is.EqualTo(2f));
            Assert.That(settings.FollowSharpness, Is.EqualTo(10f));
        }
    }
}
