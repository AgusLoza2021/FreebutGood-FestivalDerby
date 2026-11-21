using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The heading the stick is read against, kept apart from the visible camera. The stick means
    /// "toward the camera's right", so a camera that lags the car on purpose (the drift look) would
    /// make a held direction chase a moving target and the car would stop turning the way it is told.
    /// This object tracks the car's heading with the look's own smoothing, the same maths the chase
    /// camera uses, and its rotation is what the controls and the motor read instead of the camera.
    /// </summary>
    public sealed class ControlHeadingFrame : MonoBehaviour
    {
        private IChaseHeadingSource _source;
        private Vector3 _sourcePosition;
        private Transform _follow;
        private float _sharpness;
        private float _maxDegreesPerSecond;
        private float _yawDegrees;

        /// <summary>Current frame yaw in degrees. It is also the object's world rotation about +y.</summary>
        public float YawDegrees => _yawDegrees;

        /// <summary>
        /// Starts tracking <paramref name="source"/> from <paramref name="follow"/>'s position. The yaw
        /// snaps to the current heading, so there is no swing-in on the first frame.
        /// </summary>
        /// <param name="sharpness">Heading follow rate. Non-positive snaps every frame.</param>
        /// <param name="maxDegreesPerSecond">Cap on the turn rate. Non-positive is uncapped.</param>
        public void Bind(IChaseHeadingSource source, Transform follow, float sharpness,
            float maxDegreesPerSecond)
        {
            _source = source;
            _follow = follow;
            _sharpness = float.IsFinite(sharpness) ? sharpness : 0f;
            _maxDegreesPerSecond = float.IsFinite(maxDegreesPerSecond) ? maxDegreesPerSecond : 0f;
            if (source != null && follow != null
                && source.TryGetHeading(follow.position, out float heading))
                _yawDegrees = heading;
            transform.rotation = Quaternion.Euler(0f, _yawDegrees, 0f);
        }

        /// <summary>One smoothing step. Allocation-free; LateUpdate is only this call.</summary>
        public void Tick(float deltaTime)
        {
            if (_source == null || _follow == null || deltaTime <= 0f)
                return;
            if (!_source.TryGetHeading(_follow.position, out float heading))
                return;

            // DeltaAngle keeps the swing on the short way round, so crossing +-180 never spins the frame.
            float error = Mathf.DeltaAngle(_yawDegrees, heading);
            float step = _sharpness > 0f ? error * (1f - Mathf.Exp(-_sharpness * deltaTime)) : error;
            if (_maxDegreesPerSecond > 0f)
            {
                float cap = _maxDegreesPerSecond * deltaTime;
                step = Mathf.Clamp(step, -cap, cap);
            }

            _yawDegrees = Mathf.DeltaAngle(0f, _yawDegrees + step);
            transform.rotation = Quaternion.Euler(0f, _yawDegrees, 0f);
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }
    }
}
