using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Body juice is presentation only: under a synthetic readout it rolls out of the turn, squats on
    /// power and dives on lift, squashes once on landing, and OnDisable puts the pivot back to its
    /// exact rest pose so a disabled component can never leave a tilted car behind.
    /// </summary>
    public sealed class CarBodyJuicePlayModeTests
    {
        // Immediate filters (tau ~ 0 makes alpha 1), so the assertions below are deterministic.
        private static readonly CarBodyJuiceSettings Settings = new CarBodyJuiceSettings(
            0.5f, 8f, 0.5f, 6f, 0.2f, 0.4f, 1e-4f);

        private GameObject _bodyObject;
        private Rigidbody _body;
        private Transform _pivot;
        private CarBodyJuice _juice;

        [SetUp]
        public void SetUp()
        {
            _bodyObject = new GameObject("Body");
            _body = _bodyObject.AddComponent<Rigidbody>();
            var pivotObject = new GameObject(CarVisualAttacher.VisualPivotName);
            _pivot = pivotObject.transform;
            _pivot.SetParent(_bodyObject.transform, false);

            _juice = _bodyObject.AddComponent<CarBodyJuice>();
            _juice.Bind(_pivot, _body, null, Settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bodyObject != null)
                Object.Destroy(_bodyObject);
        }

        private static CarHandlingReadout Readout(float yawRate = 0f, int groundedWheels = 4)
            => new CarHandlingReadout(0f, 0f, 0f, 0f, false, 0, 0f, yawRate, 0f, 0f, groundedWheels, default);

        [Test]
        public void Apply_RollsOutOfTheTurn_AndLeavesPitchAlone()
        {
            // 10 m/s in a 0.5 rad/s right turn: 5 m/s^2 of lateral accel, gain 0.5, so 2.5 degrees of
            // lean toward the local +z (the outside of a right-hand turn).
            _body.linearVelocity = Vector3.forward * 10f;

            _juice.Apply(Readout(yawRate: 0.5f), 0.1f);

            var expected = Quaternion.Euler(0f, 0f, 2.5f);
            Assert.That(Quaternion.Angle(_pivot.localRotation, expected), Is.LessThan(0.01f));
        }

        [Test]
        public void Apply_SquatsOnPower_AndDivesOnLift()
        {
            // First call only establishes the speed the acceleration is measured against.
            _body.linearVelocity = Vector3.forward * 2f;
            _juice.Apply(Readout(), 0.1f);
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));

            // 2 -> 3 m/s in 0.1 s is 10 m/s^2 of power; gain 0.5 squats the nose 5 degrees up.
            _body.linearVelocity = Vector3.forward * 3f;
            _juice.Apply(Readout(), 0.1f);
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.Euler(-5f, 0f, 0f)), Is.LessThan(0.01f));

            // 3 -> 1 m/s in 0.1 s is -20 m/s^2 on the lift, clamped to the 6 degree dive.
            _body.linearVelocity = Vector3.forward * 1f;
            _juice.Apply(Readout(), 0.1f);
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.Euler(6f, 0f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void Apply_SquashesOnceOnLanding_AndRecovers()
        {
            _body.linearVelocity = Vector3.forward * 5f;
            _juice.Apply(Readout(groundedWheels: 0), 0.1f);
            Assert.That(_pivot.localScale.y, Is.EqualTo(1f).Within(0.0001f), "Airborne: no squash yet.");

            // Touchdown: the swell peaks midway through SquashSeconds, so the first 0.1 s of a 0.4 s
            // squash with a 0.2 scale reads 1 - 0.2 * sin(45 degrees) of height.
            _juice.Apply(Readout(groundedWheels: 4), 0.1f);
            Assert.That(_pivot.localScale.y,
                Is.EqualTo(1f - 0.2f * Mathf.Sin(0.25f * Mathf.PI)).Within(0.001f));

            // The squash runs its course and the pivot height returns to rest exactly.
            for (int i = 0; i < 5; i++)
                _juice.Apply(Readout(groundedWheels: 4), 0.1f);
            Assert.That(_pivot.localScale.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void OnDisable_RestoresThePivotToItsRestPose()
        {
            _body.linearVelocity = Vector3.forward * 10f;
            _juice.Apply(Readout(yawRate: 0.5f), 0.1f);
            Assert.That(Quaternion.Angle(_pivot.localRotation, Quaternion.identity), Is.GreaterThan(0.01f));

            _juice.enabled = false;

            Assert.That(_pivot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(_pivot.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(_pivot.localScale, Is.EqualTo(Vector3.one));
        }
    }
}
