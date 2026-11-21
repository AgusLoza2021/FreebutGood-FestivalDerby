using System;

namespace FestivalDerby.Driving
{
    /// <summary>
    /// One frame of chase-camera input: where the car is and how fast it is going, planar only.
    /// A readonly value type so the per-frame call allocates nothing and the model stays testable
    /// without an editor, a Rigidbody or a Transform.
    /// </summary>
    public readonly struct ChaseFramingTarget
    {
        /// <summary>Car position on the world X axis, in metres.</summary>
        public readonly float X;

        /// <summary>Car position on the world Y axis, in metres.</summary>
        public readonly float Y;

        /// <summary>Car position on the world Z axis, in metres.</summary>
        public readonly float Z;

        /// <summary>Car velocity on the world X axis, in m/s.</summary>
        public readonly float VelocityX;

        /// <summary>Car velocity on the world Z axis, in m/s.</summary>
        public readonly float VelocityZ;

        public ChaseFramingTarget(float x, float y, float z, float velocityX, float velocityZ)
        {
            X = x;
            Y = y;
            Z = z;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
        }
    }

    /// <summary>
    /// The chase framing as arithmetic: from the car's pose and velocity it produces the world point
    /// the view is built around. It owns no Transform, no Camera and no time source, so the follow
    /// behaviour can be asserted as numbers by the offline harness rather than inferred from frames.
    ///
    /// Planar tracking is a critically damped spring with velocity feed-forward: the damping is taken
    /// against the car's own velocity, so the equilibrium is exactly the car (no steady-state lag) and
    /// no overshoot can ring. A plain follower would settle v/sharpness metres behind, which at 28 m/s
    /// is most of a car length of framing error in every corner. Vertical tracking stays a first-order
    /// ease on a softer rate: a crest must lift the frame, not bounce it.
    /// </summary>
    public struct ChaseFramingModel
    {
        private float _positionX;
        private float _positionY;
        private float _positionZ;
        private float _velocityX;
        private float _velocityZ;
        private float _leadX;
        private float _leadZ;
        // The car's position when the previous step was solved. The step's error has to be measured
        // against where the car WAS: measuring it against where the car now IS folds one frame of the
        // car's own motion into the feedback, so the framing settles behind the car at speed instead
        // of on it.
        private float _targetX;
        private float _targetZ;
        private bool _hasFrame;

        /// <summary>Filtered car position, X axis, in metres.</summary>
        public float PositionX => _positionX;

        /// <summary>Filtered car position, Y axis, in metres.</summary>
        public float PositionY => _positionY;

        /// <summary>Filtered car position, Z axis, in metres.</summary>
        public float PositionZ => _positionZ;

        /// <summary>Planar velocity the framing is currently tracking, X axis, in m/s.</summary>
        public float VelocityX => _velocityX;

        /// <summary>Planar velocity the framing is currently tracking, Z axis, in m/s.</summary>
        public float VelocityZ => _velocityZ;

        /// <summary>Smoothed lead, X axis, in metres.</summary>
        public float LeadX => _leadX;

        /// <summary>Smoothed lead, Z axis, in metres.</summary>
        public float LeadZ => _leadZ;

        /// <summary>Filtered car position plus lead: the point the view is centred on, X axis.</summary>
        public float FramedX => _positionX + _leadX;

        /// <summary>Filtered car position plus lead: the point the view is centred on, Y axis.</summary>
        public float FramedY => _positionY;

        /// <summary>Filtered car position plus lead: the point the view is centred on, Z axis.</summary>
        public float FramedZ => _positionZ + _leadZ;

        /// <summary>True once a frame has been produced, so the first frame snaps instead of easing.</summary>
        public bool HasFrame => _hasFrame;

        /// <summary>Forgets every frame. The next <see cref="Step"/> snaps, exactly like a fresh Follow.</summary>
        public void Reset()
        {
            _hasFrame = false;
            _leadX = 0f;
            _leadZ = 0f;
            _velocityX = 0f;
            _velocityZ = 0f;
        }

        /// <summary>Forgets the lead but keeps the framing, for a camera whose lead was retuned
        /// mid-run: a new lead must not be inherited from the old one.</summary>
        public void ResetLead()
        {
            _leadX = 0f;
            _leadZ = 0f;
        }

        /// <summary>
        /// Puts the framing on the car without easing and without a lead, as the first frame of a run
        /// must: a camera that drifts in from its previous run's pose is a visible defect.
        /// </summary>
        public void SnapTo(in ChaseFramingTarget target)
        {
            _positionX = target.X;
            _positionY = target.Y;
            _positionZ = target.Z;
            _velocityX = target.VelocityX;
            _velocityZ = target.VelocityZ;
            _leadX = 0f;
            _leadZ = 0f;
            _targetX = target.X;
            _targetZ = target.Z;
            _hasFrame = true;
        }

        /// <summary>
        /// Advances the framing by one rendered frame. A non-finite or non-positive delta leaves the
        /// state untouched: a paused frame must not move the camera, and NaN must not poison it.
        /// </summary>
        public void Step(float deltaTime, in ChaseFramingTarget target, in ChaseFramingSettings settings)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f)
                return;

            if (!_hasFrame)
            {
                SnapTo(in target);
                return;
            }

            // A respawn moves the car tens of metres in one frame. Easing through that would put the
            // car off screen for seconds, so the framing is rebuilt on the frame it happens.
            float gapX = target.X - _positionX;
            float gapZ = target.Z - _positionZ;
            float snapMetres = settings.SnapDistanceMetres;
            if (snapMetres > 0f && (gapX * gapX) + (gapZ * gapZ) > snapMetres * snapMetres)
            {
                SnapTo(in target);
                return;
            }

            float velocityX = target.VelocityX;
            float velocityZ = target.VelocityZ;
            float maxSpeed = settings.MaxFeedForwardSpeed;
            if (maxSpeed > 0f && (velocityX * velocityX) + (velocityZ * velocityZ) > maxSpeed * maxSpeed)
            {
                // Above the fastest the car can go this is not a speed, it is a broken reading: a
                // position-delta velocity source reports the whole teleport distance over one frame.
                // Discarding it falls back on the position error, which no bogus number can drive off.
                velocityX = 0f;
                velocityZ = 0f;
            }

            UpdateLead(deltaTime, velocityX, velocityZ, in settings);
            UpdateVertical(deltaTime, target.Y, settings.VerticalSharpness);
            // The planar solver is fed where the car should be if it kept its velocity, so the part of
            // its motion that the velocity does not explain (a teleport, a wall, an impact) stays an
            // error for the spring and the snap to deal with instead of being followed instantly.
            UpdatePlanar(deltaTime, _targetX + (velocityX * deltaTime), _targetZ + (velocityZ * deltaTime),
                _positionX - _targetX, _positionZ - _targetZ, velocityX, velocityZ, in settings);
            _targetX = target.X;
            _targetZ = target.Z;
        }

        /// <summary>
        /// Solves the planar half of the framing exactly, once per frame:
        /// e'' + 2*w*e' + w^2*e = 0 with e = position - target and the target's velocity held for the
        /// step, which has the closed form e(t) = (e0*(1 + w*t) + w0*t)*exp(-w*t). Integrating the
        /// closed form instead of stepping an Euler estimate is what makes the result identical at 30,
        /// 60 and 120 fps: an exponential ease of a moving target is not frame-rate independent, it
        /// settles a distance behind the target that depends on the step it was advanced with.
        /// </summary>
        /// <param name="predictedX">Where the car should be after this step if it kept its velocity.</param>
        /// <param name="errorX">Framing error at the START of this step, before the car moved.</param>
        private void UpdatePlanar(float deltaTime, float predictedX, float predictedZ, float errorX,
            float errorZ, float velocityX, float velocityZ, in ChaseFramingSettings settings)
        {
            float sharpness = settings.PositionSharpness;
            if (!(sharpness > 0f))
            {
                _positionX = predictedX;
                _positionZ = predictedZ;
                return;
            }

            float decay = (float)Math.Exp(-sharpness * deltaTime);
            float relativeX = _velocityX - velocityX;
            float relativeZ = _velocityZ - velocityZ;
            float ramp = 1f + (sharpness * deltaTime);
            float nextErrorX = ((errorX * ramp) + (relativeX * deltaTime)) * decay;
            float nextErrorZ = ((errorZ * ramp) + (relativeZ * deltaTime)) * decay;
            float nextRelativeX = (relativeX - (sharpness * ((relativeX + (sharpness * errorX)) * deltaTime))) * decay;
            float nextRelativeZ = (relativeZ - (sharpness * ((relativeZ + (sharpness * errorZ)) * deltaTime))) * decay;

            _positionX = predictedX + nextErrorX;
            _positionZ = predictedZ + nextErrorZ;
            _velocityX = velocityX + nextRelativeX;
            _velocityZ = velocityZ + nextRelativeZ;
        }

        /// <summary>
        /// Eases the vertical half. A first-order ease cannot overshoot, so a kerb or a crest never
        /// throws the frame past the car and back. No velocity feed-forward here on purpose: matching
        /// the climb rate would turn a bump into a vertical lurch.
        /// </summary>
        private void UpdateVertical(float deltaTime, float targetY, float sharpness)
        {
            if (!(sharpness > 0f))
            {
                _positionY = targetY;
                return;
            }

            _positionY += (targetY - _positionY) * (1f - (float)Math.Exp(-sharpness * deltaTime));
        }

        /// <summary>
        /// Eases the lead toward the planar velocity's direction, capped at the configured maximum.
        /// Nothing is allocated: floats, one Math.Exp and no collection.
        /// </summary>
        private void UpdateLead(float deltaTime, float velocityX, float velocityZ, in ChaseFramingSettings settings)
        {
            if (settings.LookAheadMetresPerSpeed <= 0f || settings.LookAheadMaxMetres <= 0f)
            {
                _leadX = 0f;
                _leadZ = 0f;
                return;
            }

            float desiredX = velocityX * settings.LookAheadMetresPerSpeed;
            float desiredZ = velocityZ * settings.LookAheadMetresPerSpeed;
            float magnitudeSqr = (desiredX * desiredX) + (desiredZ * desiredZ);
            float maxMetres = settings.LookAheadMaxMetres;
            if (magnitudeSqr > maxMetres * maxMetres)
            {
                float scale = maxMetres / (float)Math.Sqrt(magnitudeSqr);
                desiredX *= scale;
                desiredZ *= scale;
            }

            float blend = 1f - (float)Math.Exp(-settings.LookAheadSharpness * deltaTime);
            _leadX += (desiredX - _leadX) * blend;
            _leadZ += (desiredZ - _leadZ) * blend;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
