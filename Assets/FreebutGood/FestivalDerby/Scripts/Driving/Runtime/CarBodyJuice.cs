using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The sanitized feel numbers one <see cref="CarBodyJuice"/> runs on, resolved once at bind time.
    /// The values come from <see cref="DrivingFeatureConfig.ResolveBodyRollGain(DrivingFeatureConfig)"/>
    /// and its siblings; a config asset that predates the fields still resolves to the shipped defaults.
    /// </summary>
    public readonly struct CarBodyJuiceSettings
    {
        public readonly float RollGain;
        public readonly float RollMaxDegrees;
        public readonly float PitchGain;
        public readonly float PitchMaxDegrees;
        public readonly float SquashScale;
        public readonly float SquashSeconds;
        public readonly float FilterTau;

        public CarBodyJuiceSettings(float rollGain, float rollMaxDegrees, float pitchGain,
            float pitchMaxDegrees, float squashScale, float squashSeconds, float filterTau)
        {
            RollGain = rollGain;
            RollMaxDegrees = rollMaxDegrees;
            PitchGain = pitchGain;
            PitchMaxDegrees = pitchMaxDegrees;
            SquashScale = squashScale;
            SquashSeconds = squashSeconds;
            FilterTau = filterTau;
        }
    }

    /// <summary>
    /// Body juice for the player's cosmetic model: roll leaning out of the turn, pitch squatting on
    /// power and diving on lift, and a squash on landing. Everything drives the model's VisualPivot —
    /// the identity transform <see cref="CarVisualAttacher"/> hangs the clone under — so the fitted
    /// clone and the physics body are never touched, and <see cref="OnDisable"/> puts the pivot back to
    /// exactly where it was. Accelerations are exponential-filtered and the step allocates nothing, so
    /// LateUpdate stays hot-path clean.
    /// </summary>
    public sealed class CarBodyJuice : MonoBehaviour
    {
        private const float MinimumSeconds = 1e-4f;

        private Transform _pivot;
        private Rigidbody _body;
        private LocalCarFixedStepDriver _driver;
        private CarBodyJuiceSettings _settings;
        // Null while the drift switch is off, so the off path costs one null check.
        private CarDriftJuice _drift;

        private Vector3 _restLocalPosition = Vector3.zero;
        private Quaternion _restLocalRotation = Quaternion.identity;
        private Vector3 _restLocalScale = Vector3.one;

        private float _lateralFiltered;
        private float _longitudinalFiltered;
        private float _lastForwardSpeed;
        private bool _hasLastForwardSpeed;
        private int _lastGroundedWheels = -1;
        private float _squashTimeLeft;

        /// <summary>The sanitized settings a run uses, resolved from the feature config.</summary>
        public static CarBodyJuiceSettings ResolveSettings(DrivingFeatureConfig config)
        {
            return new CarBodyJuiceSettings(
                DrivingFeatureConfig.ResolveBodyRollGain(config),
                DrivingFeatureConfig.ResolveBodyRollMaxDeg(config),
                DrivingFeatureConfig.ResolveBodyPitchGain(config),
                DrivingFeatureConfig.ResolveBodyPitchMaxDeg(config),
                DrivingFeatureConfig.ResolveBodySquashScale(config),
                DrivingFeatureConfig.ResolveBodySquashSeconds(config),
                DrivingFeatureConfig.ResolveBodyFilterTau(config));
        }

        /// <summary>The sanitized drift tuning a run uses; the default (switch off) disables the swing.</summary>
        public static CarDriftJuiceSettings ResolveDriftSettings(DrivingFeatureConfig config)
        {
            return DrivingFeatureConfig.ResolveDriftSettings(config);
        }

        /// <summary>
        /// Binds the juice to one pivot under one body. The driver may be null (tests drive
        /// <see cref="Apply"/> directly); without a pivot or body the component no-ops. The pivot's
        /// current local pose is the rest pose OnDisable restores.
        /// </summary>
        public void Bind(Transform pivot, Rigidbody body, LocalCarFixedStepDriver driver,
            in CarBodyJuiceSettings settings, CarDriftJuiceSettings drift = default)
        {
            _pivot = pivot;
            _body = body;
            _driver = driver;
            _settings = settings;
            _drift = drift.Enabled ? new CarDriftJuice(drift) : null;
            if (pivot != null)
            {
                _restLocalPosition = pivot.localPosition;
                _restLocalRotation = pivot.localRotation;
                _restLocalScale = pivot.localScale;
            }
        }

        /// <summary>
        /// One juice step from the driver's presentation readout plus the body's measured velocity.
        /// Allocation-free; called from LateUpdate with the live driver, or from tests with a synthetic
        /// readout. The roll leans out of the turn (positive yaw rate is a right turn, so the body tips
        /// toward its local +z), the pitch squats on power and dives on lift, and a grounded-wheels
        /// transition from 0 to any grip squeezes the pivot's height once over SquashSeconds.
        /// </summary>
        public void Apply(in CarHandlingReadout readout, float dt)
        {
            if (_pivot == null || _body == null || dt <= MinimumSeconds)
                return;

            // Longitudinal acceleration is measured, not commanded: the readout's pedal is what the
            // driver asked for, while the body's velocity along its own forward axis is what the
            // suspension actually delivered.
            Vector3 forward = _body.rotation * Vector3.forward;
            float forwardSpeed = Vector3.Dot(_body.linearVelocity, forward);
            float longitudinalAccel = 0f;
            if (_hasLastForwardSpeed)
                longitudinalAccel = (forwardSpeed - _lastForwardSpeed) / dt;
            _lastForwardSpeed = forwardSpeed;
            _hasLastForwardSpeed = true;

            // Lateral acceleration from the readout's yaw rate and the measured speed: what a passenger
            // would feel pressing into the door.
            float lateralAccel = readout.YawRateRadiansPerSecond * forwardSpeed;

            float alpha = _settings.FilterTau > MinimumSeconds
                ? 1f - Mathf.Exp(-dt / _settings.FilterTau)
                : 1f;
            _longitudinalFiltered += (longitudinalAccel - _longitudinalFiltered) * alpha;
            _lateralFiltered += (lateralAccel - _lateralFiltered) * alpha;

            // Positive local z rotation tips the roof toward the outside of a right-hand turn; nose up
            // (squat) is negative local x.
            float rollDegrees = Mathf.Clamp(_lateralFiltered * _settings.RollGain,
                -_settings.RollMaxDegrees, _settings.RollMaxDegrees);
            float pitchDegrees = Mathf.Clamp(-_longitudinalFiltered * _settings.PitchGain,
                -_settings.PitchMaxDegrees, _settings.PitchMaxDegrees);

            float squash = 0f;
            int groundedWheels = readout.GroundedWheels;
            if (_lastGroundedWheels == 0 && groundedWheels > 0)
                _squashTimeLeft = _settings.SquashSeconds;
            _lastGroundedWheels = groundedWheels;
            if (_squashTimeLeft > 0f)
            {
                // A smooth swell-and-recover bump: zero at the touchdown frame, peaking midway.
                _squashTimeLeft -= dt;
                float progress = 1f - Mathf.Max(_squashTimeLeft, 0f) / _settings.SquashSeconds;
                squash = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            }

            // The drift yaws the whole model around a point on the car's axis, so the position has to
            // move with it: rotating about P keeps P fixed, which is P - R*P added to the rest pose.
            Quaternion yaw = Quaternion.identity;
            Vector3 pivotShift = Vector3.zero;
            if (_drift != null)
            {
                float yawDegrees = _drift.Step(readout.YawRateRadiansPerSecond,
                    readout.ForwardSpeedMetersPerSecond, dt);
                yaw = Quaternion.Euler(0f, yawDegrees, 0f);
                var p = new Vector3(0f, 0f, _drift.PivotZ);
                pivotShift = p - yaw * p;
            }

            _pivot.localPosition = _restLocalPosition + pivotShift;
            _pivot.localScale = new Vector3(
                _restLocalScale.x,
                _restLocalScale.y * (1f - _settings.SquashScale * squash),
                _restLocalScale.z);
            _pivot.localRotation = yaw * _restLocalRotation * Quaternion.Euler(pitchDegrees, 0f, rollDegrees);
        }

        private void LateUpdate()
        {
            if (_driver == null)
                return;

            // Presentation-only readout: never allocates, never feeds back into a step.
            Apply(_driver.ReadHandlingDebug(), Time.deltaTime);
        }

        private void OnDisable()
        {
            if (_pivot != null)
            {
                _pivot.localPosition = _restLocalPosition;
                _pivot.localRotation = _restLocalRotation;
                _pivot.localScale = _restLocalScale;
            }

            // The transient state goes with it: a re-enable starts from rest, not from a stale filter
            // or a half-finished squash.
            _drift?.Reset();
            _lateralFiltered = 0f;
            _longitudinalFiltered = 0f;
            _hasLastForwardSpeed = false;
            _lastGroundedWheels = -1;
            _squashTimeLeft = 0f;
        }
    }
}
