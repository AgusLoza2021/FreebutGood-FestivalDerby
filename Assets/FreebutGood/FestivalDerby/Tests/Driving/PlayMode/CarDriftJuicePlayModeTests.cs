using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The drift is presentation only: the front pivot holds still, the rear pivot is thrown out of the
    /// turn after a delay in proportion to the 0..100 sensitivity and the steering, it fades out below
    /// speed and when the switch is off, and OnDisable restores the rest pose.
    /// </summary>
    public sealed class CarDriftJuicePlayModeTests
    {
        private static readonly CarBodyJuiceSettings Body = new CarBodyJuiceSettings(
            0.5f, 8f, 0.5f, 6f, 0.2f, 0.4f, 1e-4f);

        private const float Dt = 1f / 60f;
        // Pivots at +1 and -1 are 2 m apart; a 1 m tail swing is atan(1 / 2) of yaw.
        private static readonly float FullSwingDegrees = Mathf.Atan2(1f, 2f) * Mathf.Rad2Deg;
        private static readonly float HalfSwingDegrees = Mathf.Atan2(0.5f, 2f) * Mathf.Rad2Deg;

        private GameObject _bodyObject;
        private Rigidbody _body;
        private Transform _pivot;
        private CarBodyJuice _juice;

        [SetUp]
        public void SetUp()
        {
            _bodyObject = new GameObject("Body");
            _body = _bodyObject.AddComponent<Rigidbody>();
            _pivot = new GameObject(CarVisualAttacher.VisualPivotName).transform;
            _pivot.SetParent(_bodyObject.transform, false);
            _juice = _bodyObject.AddComponent<CarBodyJuice>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_bodyObject != null)
                Object.Destroy(_bodyObject);
        }

        // Full-turn yaw 1 rad/s, full strength at 5 m/s, a 1 m tail swing at sensitivity 100.
        private static CarDriftJuiceSettings Drift(bool enabled = true, float sensitivity = 100f,
            float damping = 0.8f)
            => new CarDriftJuiceSettings(enabled, sensitivity, 1f, 1f, -1f, 4f, damping, 5f, 1f);

        private static CarHandlingReadout Readout(float yawRate, float forwardSpeed)
            => new CarHandlingReadout(forwardSpeed, forwardSpeed, 0f, 0f, false, 0, 0f, yawRate, 0f, 0f, 4, default);

        // Bind captures the pivot's current pose as its rest pose, so a rebind inside one test has to
        // start from the identity pivot the attacher would have handed over.
        private void BindDrift(CarDriftJuiceSettings drift)
        {
            _pivot.localPosition = Vector3.zero;
            _pivot.localRotation = Quaternion.identity;
            _juice.Bind(_pivot, _body, null, Body, drift);
        }

        private void Run(float yawRate, float forwardSpeed, int frames = 240)
        {
            _body.linearVelocity = Vector3.forward * forwardSpeed;
            for (int i = 0; i < frames; i++)
                _juice.Apply(Readout(yawRate, forwardSpeed), Dt);
        }

        private float PivotYaw() => _pivot.localRotation.eulerAngles.y > 180f
            ? _pivot.localRotation.eulerAngles.y - 360f
            : _pivot.localRotation.eulerAngles.y;

        [Test]
        public void FullTurnAtSpeed_ThrowsTheTail_AndPointsTheNoseIntoTheTurn()
        {
            // The motor reports yaw rate counter-clockwise: negative is a right turn, positive a left one.
            BindDrift(Drift());
            Run(yawRate: -1f, forwardSpeed: 10f);
            Assert.That(PivotYaw(), Is.EqualTo(FullSwingDegrees).Within(0.1f), "Right turn: nose right.");

            BindDrift(Drift());
            Run(yawRate: 1f, forwardSpeed: 10f);
            Assert.That(PivotYaw(), Is.EqualTo(-FullSwingDegrees).Within(0.1f), "Left turn: nose left.");
        }

        [Test]
        public void FrontPivotHoldsItsLine_WhileTheRearPivotGoesOutsideTheTurn()
        {
            var front = new Vector3(0f, 0f, 1f);
            var rear = new Vector3(0f, 0f, -1f);

            BindDrift(Drift());
            Run(yawRate: -1f, forwardSpeed: 10f); // right turn

            Assert.That(Vector3.Distance(_pivot.localPosition + _pivot.localRotation * front, front), Is.LessThan(0.01f),
                "The nose stays where the physics car is.");
            Assert.That((_pivot.localPosition + _pivot.localRotation * rear).x, Is.LessThan(-0.5f),
                "The tail goes to the outside (left) of a right turn.");
        }

        [Test]
        public void TheTailReactsWithADelay_AndFlicksPastItsTarget_WhenUnderdamped()
        {
            BindDrift(Drift(damping: 0.3f));
            _body.linearVelocity = Vector3.forward * 10f;

            // Three frames (0.05 s) after turn-in the tail has barely started.
            for (int i = 0; i < 3; i++)
                _juice.Apply(Readout(-1f, 10f), Dt);
            Assert.That(PivotYaw(), Is.LessThan(FullSwingDegrees * 0.5f), "The tail is late.");

            float peak = 0f;
            for (int i = 0; i < 240; i++)
            {
                _juice.Apply(Readout(-1f, 10f), Dt);
                peak = Mathf.Max(peak, PivotYaw());
            }

            Assert.That(peak, Is.GreaterThan(FullSwingDegrees * 1.05f), "It flicks past the target.");
            Assert.That(PivotYaw(), Is.EqualTo(FullSwingDegrees).Within(0.2f), "Then it settles on it.");
        }

        [Test]
        public void Sensitivity_ScalesTheSwing_FromNothingToFull()
        {
            BindDrift(Drift(sensitivity: 50f));
            Run(-1f, 10f);
            Assert.That(PivotYaw(), Is.EqualTo(HalfSwingDegrees).Within(0.1f));

            BindDrift(Drift(sensitivity: 0f));
            Run(-1f, 10f);
            Assert.That(PivotYaw(), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void SteeringAmount_ScalesTheSwing()
        {
            BindDrift(Drift());
            Run(yawRate: -0.5f, forwardSpeed: 10f);
            Assert.That(PivotYaw(), Is.EqualTo(HalfSwingDegrees).Within(0.1f), "Half a turn throws the tail half as far.");
        }

        [Test]
        public void SlowAndReversing_DoNotDrift()
        {
            BindDrift(Drift());
            Run(1f, 0f);
            Assert.That(PivotYaw(), Is.EqualTo(0f).Within(0.01f));

            Run(1f, -8f);
            Assert.That(PivotYaw(), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void SwitchOff_LeavesTheBodyJuiceUntouched()
        {
            BindDrift(Drift(enabled: false));
            Run(-1f, 10f);

            // Only the roll acts: no yaw and no pivot shift.
            Assert.That(PivotYaw(), Is.EqualTo(0f).Within(0.01f));
            Assert.That(_pivot.localPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Resolve_NullOrOff_IsDisabled_OnClampsBadValues_AndTheCameraOnlyChangesWhileOn()
        {
            Assert.That(DrivingFeatureConfig.ResolveDriftSettings(null).Enabled, Is.False);
            Assert.That(DrivingFeatureConfig.ResolveDriftCameraYawSharpness(null, 8f), Is.EqualTo(8f));

            var config = ScriptableObject.CreateInstance<DrivingFeatureConfig>();
            try
            {
                Assert.That(DrivingFeatureConfig.ResolveDriftSettings(config).Enabled, Is.False, "Born off.");
                Assert.That(DrivingFeatureConfig.ResolveDriftCameraYawSharpness(config, 8f), Is.EqualTo(8f),
                    "Off keeps the look's own camera.");
                Assert.That(DrivingFeatureConfig.ResolveDriftCameraYawMaxDegPerSec(config, 120f), Is.EqualTo(120f));

                config.DriftJuiceEnabled = true;
                config.DriftSensitivity = 250f;
                config.DriftSpringHz = float.NaN;
                var resolved = DrivingFeatureConfig.ResolveDriftSettings(config);

                Assert.That(resolved.Enabled, Is.True);
                Assert.That(resolved.Sensitivity, Is.EqualTo(55f), "Out of 0..100 falls back to the default.");
                Assert.That(resolved.SpringHz, Is.EqualTo(2.5f), "Non-finite falls back to the default.");
                Assert.That(DrivingFeatureConfig.ResolveDriftCameraYawSharpness(config, 8f), Is.EqualTo(2.5f),
                    "On, the camera lags the car's heading.");
                Assert.That(DrivingFeatureConfig.ResolveDriftCameraYawMaxDegPerSec(config, 120f), Is.EqualTo(70f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void OnDisable_RestoresThePivotToItsRestPose()
        {
            BindDrift(Drift());
            Run(-1f, 10f);
            Assert.That(PivotYaw(), Is.Not.EqualTo(0f).Within(0.5f));

            _juice.enabled = false;

            Assert.That(_pivot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(_pivot.localRotation, Is.EqualTo(Quaternion.identity));
        }
    }
}
