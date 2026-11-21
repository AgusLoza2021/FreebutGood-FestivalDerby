using UnityEngine;
using FestivalDerby.World;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Where the chase camera should face, as a world yaw in degrees (0 = +Z, 90 = +X).
    /// </summary>
    public interface IChaseHeadingSource
    {
        /// <summary>False when no heading is known for <paramref name="position"/>; the camera then holds its yaw.</summary>
        bool TryGetHeading(Vector3 position, out float yawDegrees);
    }

    /// <summary>
    /// The direction the car is actually travelling, read from its velocity rather than its nose: in a
    /// slide the nose swings while the travel direction stays smooth, so the camera rides the slide
    /// instead of shaking with it. Slow or reversing, it falls back to the nose, so backing up never
    /// spins the camera round to face the car.
    /// </summary>
    public sealed class CarHeadingSource : IChaseHeadingSource
    {
        private const float MinTravelSpeed = 3f;

        private readonly Rigidbody _body;

        public CarHeadingSource(Rigidbody body)
        {
            _body = body;
        }

        public bool TryGetHeading(Vector3 position, out float yawDegrees)
        {
            yawDegrees = 0f;
            if (_body == null)
                return false;

            Vector3 nose = _body.transform.forward;
            Vector3 velocity = _body.linearVelocity;
            Vector3 travel = new Vector3(velocity.x, 0f, velocity.z);
            bool forwardTravel = travel.sqrMagnitude > MinTravelSpeed * MinTravelSpeed
                && Vector3.Dot(travel, nose) > 0f;
            Vector3 direction = forwardTravel ? travel : new Vector3(nose.x, 0f, nose.z);
            if (direction.sqrMagnitude < 1e-8f)
                return false;

            yawDegrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return true;
        }
    }

    /// <summary>
    /// The direction the circuit takes a fixed distance ahead of the car. Reading the track rather than
    /// the car is what keeps the camera calm: a slide or a spin changes the car's heading every frame,
    /// while the road ahead only turns where the corner is, and it turns before the car gets there.
    /// </summary>
    public sealed class TrackHeadingSource : IChaseHeadingSource
    {
        private readonly BakedPath _path;
        private readonly float _lookAheadMetres;
        // The search hint is the previous frame's nearest sample, so the projection stays a short local
        // scan instead of a walk over the whole lap.
        private int _hint = -1;

        private TrackHeadingSource(BakedPath path, float lookAheadMetres)
        {
            _path = path;
            _lookAheadMetres = lookAheadMetres > 0f ? lookAheadMetres : 0f;
        }

        /// <summary>A source on the map's main path, or null when the map has no usable path.</summary>
        public static TrackHeadingSource FromMap(LoadedMap map, float lookAheadMetres)
        {
            if (map == null || map.MainPath == null || !map.MainPath.IsUsable)
                return null;
            return new TrackHeadingSource(map.MainPath, lookAheadMetres);
        }

        public bool TryGetHeading(Vector3 position, out float yawDegrees)
        {
            yawDegrees = 0f;
            float along = _path.NearestDistance(new PathVector3(position.x, position.y, position.z), ref _hint);
            if (!_path.Sample(along + _lookAheadMetres, out PathSample sample))
                return false;

            float x = sample.Tangent.x;
            float z = sample.Tangent.z;
            if (x * x + z * z < 1e-8f)
                return false;

            yawDegrees = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            return true;
        }
    }
}
