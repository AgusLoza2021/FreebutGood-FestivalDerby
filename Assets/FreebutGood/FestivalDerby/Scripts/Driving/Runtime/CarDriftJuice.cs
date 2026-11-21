using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Tuning for the cosmetic drift. Resolved once from <see cref="DrivingFeatureConfig"/>;
    /// <c>default</c> is "off", so a caller that never opts in keeps the body juice exactly as it was.
    /// </summary>
    public readonly struct CarDriftJuiceSettings
    {
        public readonly bool Enabled;
        /// <summary>0..100. 0 never moves the tail, 100 sends it the full <see cref="TailSwingMetres"/>.</summary>
        public readonly float Sensitivity;
        /// <summary>Largest sideways distance the rear pivot is thrown, in metres.</summary>
        public readonly float TailSwingMetres;
        /// <summary>Body-space z of the front pivot, in metres. It never moves: the nose holds its line.</summary>
        public readonly float FrontPivotZ;
        /// <summary>Body-space z of the rear pivot, in metres (negative is toward the tail).</summary>
        public readonly float RearPivotZ;
        /// <summary>How fast the rear pivot reacts, in Hz. Lower is a longer delay.</summary>
        public readonly float SpringHz;
        /// <summary>Below 1 the tail overshoots its target and settles; 1 and above is a plain slide.</summary>
        public readonly float DampingRatio;
        /// <summary>Forward speed, in m/s, at which the drift reaches full strength; slower fades it out.</summary>
        public readonly float MinSpeed;
        /// <summary>Yaw rate, in rad/s, that counts as the user turning all the way.</summary>
        public readonly float FullYawRate;

        public CarDriftJuiceSettings(bool enabled, float sensitivity, float tailSwingMetres,
            float frontPivotZ, float rearPivotZ, float springHz, float dampingRatio, float minSpeed,
            float fullYawRate)
        {
            Enabled = enabled;
            Sensitivity = sensitivity;
            TailSwingMetres = tailSwingMetres;
            FrontPivotZ = frontPivotZ;
            RearPivotZ = rearPivotZ;
            SpringHz = springHz;
            DampingRatio = dampingRatio;
            MinSpeed = minSpeed;
            FullYawRate = fullYawRate;
        }
    }

    /// <summary>
    /// The drift as two pivots. The front pivot is the physics car and never moves. The rear pivot is a
    /// simulated point that is thrown sideways, to the outside of the turn, by how hard and how fast the
    /// car is turning, and that chases its target through an underdamped spring: it reacts late, flicks
    /// past, and settles. The model's yaw is whatever angle puts the line from the rear pivot to the
    /// front pivot back through both, so the nose stays and the tail goes. Pure state with no engine
    /// objects, and <see cref="Step"/> allocates nothing.
    /// </summary>
    public sealed class CarDriftJuice
    {
        // A hitch must not feed the spring a step big enough to blow it up.
        private const float MaxStepSeconds = 1f / 30f;

        private readonly CarDriftJuiceSettings _settings;
        private float _rearOffset;
        private float _rearVelocity;

        public CarDriftJuice(in CarDriftJuiceSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Sideways offset of the rear pivot in body space, in metres. Positive is the car's right.</summary>
        public float RearOffsetMetres => _rearOffset;

        /// <summary>Current visual yaw offset, in degrees. Positive turns the nose right.</summary>
        public float YawDegrees
        {
            get
            {
                float length = Mathf.Max(_settings.FrontPivotZ - _settings.RearPivotZ, 0.01f);
                // The rear pivot sits at -offset from the front's axis line, so the line rear -> front
                // leans toward the side the rear left: tail out on the left, nose points right.
                return Mathf.Atan2(-_rearOffset, length) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Body-space z the model is yawed around. The front pivot, so only the tail swings.</summary>
        public float PivotZ => _settings.FrontPivotZ;

        public void Reset()
        {
            _rearOffset = 0f;
            _rearVelocity = 0f;
        }

        /// <summary>
        /// Advances the rear pivot one frame and returns the yaw offset in degrees. The yaw rate uses the
        /// motor's convention: positive is a left turn, negative a right turn.
        /// </summary>
        public float Step(float yawRateRadiansPerSecond, float forwardSpeed, float deltaTime)
        {
            if (!_settings.Enabled || deltaTime <= 0f)
                return YawDegrees;

            float dt = Mathf.Min(deltaTime, MaxStepSeconds);
            float turn = _settings.FullYawRate > 0f
                ? Mathf.Clamp(yawRateRadiansPerSecond / _settings.FullYawRate, -1f, 1f)
                : 0f;
            // Reversing and crawling are not drifts: the swing only fades in with forward speed.
            float speed = _settings.MinSpeed > 0f
                ? Mathf.Clamp01(forwardSpeed / _settings.MinSpeed)
                : (forwardSpeed > 0f ? 1f : 0f);
            // The motor reports yaw rate counter-clockwise (positive is a LEFT turn: ArcadeCarMotor negates
            // the body's angular velocity about up). The tail goes to the outside of the turn, so a left
            // turn throws it to the right, which is positive body-space x.
            float target = turn * speed * Mathf.Clamp(_settings.Sensitivity, 0f, 100f) * 0.01f
                * _settings.TailSwingMetres;

            float omega = 2f * Mathf.PI * _settings.SpringHz;
            float acceleration = omega * omega * (target - _rearOffset)
                - 2f * _settings.DampingRatio * omega * _rearVelocity;
            // Semi-implicit Euler: stable for the clamped step at any sane SpringHz.
            _rearVelocity += acceleration * dt;
            _rearOffset += _rearVelocity * dt;
            return YawDegrees;
        }
    }
}
