using FestivalDerby.Driving.Runtime;
using UnityEngine;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// A kinematic deck moved the way the CATCH carrier is moved: MovePosition and MoveRotation in the fixed
    /// step, so the body itself reads back zero velocity and the deck has to state its own motion. It stands
    /// still until <see cref="Begin"/>, so a car can settle on it first.
    /// </summary>
    public sealed class RideTestDeck : MonoBehaviour, IGroundVelocitySource
    {
        private Rigidbody _body;
        private float _speed;
        private float _yawRateRadians;
        private float _headingRadians;
        private Vector3 _position;
        private Vector3 _velocity;
        private Vector3 _angularVelocity;

        public Rigidbody Body
        {
            get { return _body; }
        }

        public void Bind(Rigidbody body)
        {
            _body = body;
            _position = body.position;
        }

        /// <summary>Starts moving forward at <paramref name="speed"/>, turning at a constant yaw rate.</summary>
        public void Begin(float speed, float yawRateRadians)
        {
            _speed = speed;
            _yawRateRadians = yawRateRadians;
            _position = _body.position;
            _headingRadians = _body.rotation.eulerAngles.y * Mathf.Deg2Rad;
            UpdateMotion();
        }

        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            Vector3 lever = _body != null ? worldPoint - _body.position : Vector3.zero;
            return _velocity + Vector3.Cross(_angularVelocity, lever);
        }

        public Vector3 AngularVelocity
        {
            get { return _angularVelocity; }
        }

        private void FixedUpdate()
        {
            if (_body == null || (_speed == 0f && _yawRateRadians == 0f))
                return;

            float dt = Time.fixedDeltaTime;
            _headingRadians += _yawRateRadians * dt;
            UpdateMotion();
            _position += _velocity * dt;
            _body.MovePosition(_position);
            _body.MoveRotation(Heading());
        }

        private void UpdateMotion()
        {
            _velocity = Heading() * Vector3.forward * _speed;
            _angularVelocity = Vector3.up * _yawRateRadians;
        }

        private Quaternion Heading()
        {
            return Quaternion.Euler(0f, _headingRadians * Mathf.Rad2Deg, 0f);
        }
    }
}
