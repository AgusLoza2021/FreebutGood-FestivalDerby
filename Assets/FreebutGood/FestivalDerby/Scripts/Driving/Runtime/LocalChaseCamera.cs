using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Owns the 3/4 chase framing on its own transform by default, or on a borrowed rendering view
    /// without ever writing that view's local pose: when a view is borrowed this component drives
    /// its OWN transform so the view's world pose lands on the framing. Leaving the view untouched
    /// is what lets a position shaker (MoreMountains' MMWiggle) live on the view: the shake stays a
    /// relative offset under the framing instead of pinning the camera to a pose captured at start.
    /// World-space offset and fixed bearing keep target yaw and follow lag from swinging the view;
    /// elevation follows from the offset and look height.
    /// It explicitly does NOT own rendering enablement,
    /// physics forces, the time scale or any Rigidbody: a camera that reached into those would
    /// become a second owner of state that already has one elsewhere.
    /// The follow, the lead, the teleport snap and the vertical ease are not computed here either:
    /// they live in <see cref="ChaseFramingModel"/>, which owns no engine type and can therefore be
    /// measured as arithmetic by the offline harness. This component only reads the car, feeds the
    /// model and places its transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalChaseCamera : MonoBehaviour
    {
        // Below this the look direction carries no usable orientation and LookRotation would
        // only complain, so a degenerate framing leaves the previous rotation alone.
        private const float ZeroLookVectorSqrMagnitude = 1e-8f;

        [SerializeField] private Transform _target;
        // Legacy name retained for serialization and Configure callers; the offset is world-space.
        [SerializeField] private Vector3 _localOffset;
        [SerializeField] private float _lookHeight;
        [SerializeField] private float _followSharpness;
        // Look-ahead: the framing leads the car by a distance proportional to its planar speed, so
        // the corner the car is heading into enters the frame before the car reaches it. Both zero
        // (the serialized default) means the framing is exactly the pre-look-ahead formula.
        [SerializeField] private float _lookAheadMetresPerSpeed;
        [SerializeField] private float _lookAheadMaxMetres;
        [SerializeField] private float _lookAheadSharpness;
        // Vertical settling rate and the planar gap that counts as a teleport. Both are read as "use
        // the model's default" when they are not positive: a scene or prefab authored before these
        // fields existed deserializes them as 0 (or keeps the initializer below), and a missing key
        // must not flatten the framing.
        [SerializeField] private float _verticalFollowSharpness = ChaseFramingSettings.DefaultVerticalSharpness;
        [SerializeField] private float _snapTeleportMetres = ChaseFramingSettings.DefaultSnapDistanceMetres;
        // Speed-widened field of view. Off until ConfigureSpeedFov is called, so a camera nobody asked
        // to widen never writes fieldOfView and another owner of that value stays its only writer.
        [SerializeField] private Camera _fovView;
        [SerializeField] private float _fovBaseDegrees;
        [SerializeField] private float _fovWidenDegrees;
        [SerializeField] private float _fovAtSpeed;
        [SerializeField] private float _fovSharpness;
        private float _fovDegrees;
        private bool _fovActive;
        // Optional velocity read. A Rigidbody is the only source that is exact at fixed-step
        // cadence; a position delta is the fallback for a target that has no body.
        [SerializeField] private Rigidbody _velocitySource;

        // Following yaw (the "cone"): the authored offset is swung around the car, at a fixed height and
        // distance, by a heading source. Relative mode measures the turn from the heading where the run
        // starts (the authored offset frames the start as tuned); absolute mode treats the offset as
        // car-local (-Z is behind). Off (null source) keeps the fixed world bearing exactly as before.
        private IChaseHeadingSource _headingSource;
        private bool _yawRelativeToStart;
        private float _yawSharpness;
        private float _yawMaxDegreesPerSecond;
        private float _yawReferenceDegrees;
        private float _yawDegrees;
        private bool _yawReferenceSet;
        // Metres ahead of the car, along the swung forward, that the camera aims at: the pivot that
        // keeps the road in front of the car in frame instead of the car's roof.
        private float _pivotAheadMetres;

        private ChaseFramingModel _framing;
        private ChaseFramingSettings _settings;
        private Vector3 _lastTargetPosition;
        private bool _hasLastTargetPosition;

        private bool _isFollowing;
        private Transform _framingTransform;
        // The view's pose relative to this transform, captured once at Follow and never refreshed:
        // refreshing it mid-shake would fold the shake back into the framing. Together they turn a
        // desired VIEW world pose into the ROOT pose that renders it.
        private Vector3 _viewOffsetFromRoot;
        private Quaternion _viewRotationFromRoot;
        private Quaternion _inverseViewRotationFromRoot;
        // A borrowed view is the only case where this component has to give a transform back: it
        // moved the rig root during the run and restores the root's authored pose on Stop. The
        // view was never written, so it needs no restore - its authored pose is the shaker's base.
        private bool _restoreRootPose;
        private Vector3 _rootPositionAtFollow;
        private Quaternion _rootRotationAtFollow;

        /// <summary>True only between a successful Follow and a Stop.</summary>
        public bool IsFollowing => _isFollowing;

        /// <summary>Starts following and snaps to the framed position instead of easing in from
        /// wherever the transform happened to be: a camera that drifts in from its old spot for
        /// its first second is a visible defect, not a smoothing.</summary>
        public void Follow(Transform target)
        {
            Follow(target, transform);
        }

        public void Follow(Transform target, Transform renderingView)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (renderingView == null)
                throw new ArgumentNullException(nameof(renderingView));

            Stop();
            _framingTransform = renderingView;
            _restoreRootPose = renderingView != transform;
            if (_restoreRootPose)
            {
                _rootPositionAtFollow = transform.position;
                _rootRotationAtFollow = transform.rotation;
                // Captured through the intermediate nodes, so a pitched rig node is part of the
                // offset instead of being mis-framed away. Both use the pre-write pose.
                _viewOffsetFromRoot = transform.InverseTransformPoint(renderingView.position);
                _viewRotationFromRoot = Quaternion.Inverse(transform.rotation) * renderingView.rotation;
            }
            else
            {
                _viewOffsetFromRoot = Vector3.zero;
                _viewRotationFromRoot = Quaternion.identity;
            }

            _inverseViewRotationFromRoot = Quaternion.Inverse(_viewRotationFromRoot);
            _target = target;
            _isFollowing = true;
            SnapToFraming();
        }

        /// <summary>Stops following. Idempotent. A borrowed view's rig root is restored to the pose
        /// it had at Follow, while the default self transform keeps its last pose; the borrowed view
        /// itself is never written, so it keeps the authored pose its shaker captured.</summary>
        public void Stop()
        {
            if (_restoreRootPose)
            {
                transform.position = _rootPositionAtFollow;
                transform.rotation = _rootRotationAtFollow;
            }

            _restoreRootPose = false;
            _framingTransform = null;
            _isFollowing = false;
            _target = null;
            // The lead is state about the old target, so it must not leak into the next Follow's
            // snap; otherwise the first frame of the next run is framed for the previous car.
            _framing.Reset();
            _hasLastTargetPosition = false;
            // The heading source belongs to the run's map; the next run brings its own.
            _headingSource = null;
            _yawReferenceSet = false;
            _yawDegrees = 0f;
            _pivotAheadMetres = 0f;
        }

        /// <summary>
        /// Swings the authored offset around the car by a heading source. A null source turns it off and
        /// restores the fixed bearing.
        /// </summary>
        /// <param name="relativeToStart">True: the heading where following starts is the reference, so the
        /// authored offset frames the start exactly as tuned (track mode). False: the offset is car-local,
        /// -Z behind, and the yaw is the heading itself (chase mode).</param>
        /// <param name="sharpness">How fast the yaw settles (1/s). Lower lags more, which is what swings the
        /// camera to the outside of a corner. Non-positive snaps.</param>
        /// <param name="maxDegreesPerSecond">Cap on the swing speed, so a hairpin pans instead of whipping.
        /// Non-positive is uncapped.</param>
        public void ConfigureYaw(IChaseHeadingSource source, bool relativeToStart, float sharpness,
            float maxDegreesPerSecond)
        {
            _headingSource = source;
            _yawRelativeToStart = relativeToStart;
            _yawSharpness = IsFinite(sharpness) ? sharpness : 0f;
            _yawMaxDegreesPerSecond = IsFinite(maxDegreesPerSecond) ? maxDegreesPerSecond : 0f;
            _yawDegrees = 0f;
            _yawReferenceDegrees = 0f;
            _yawReferenceSet = !relativeToStart;
            if (_isFollowing && _target != null)
            {
                if (relativeToStart)
                    CaptureYawReference(_target.position);
                else if (source != null && source.TryGetHeading(_target.position, out float heading))
                    _yawDegrees = heading; // start already behind the car, no swing-in on the first frame
                SnapToFraming();
            }
        }

        /// <summary>Aims the camera this many metres ahead of the car along the swung forward. 0 aims at the car.</summary>
        public void ConfigurePivotAhead(float metres)
        {
            _pivotAheadMetres = IsFinite(metres) && metres > 0f ? metres : 0f;
            if (_isFollowing)
                SnapToFraming();
        }

        private void CaptureYawReference(Vector3 targetPosition)
        {
            if (_headingSource != null && _headingSource.TryGetHeading(targetPosition, out float heading))
            {
                _yawReferenceDegrees = heading;
                _yawReferenceSet = true;
            }
        }

        private void UpdateYaw(Vector3 targetPosition, float deltaTime)
        {
            if (_headingSource == null)
                return;
            if (!_yawReferenceSet)
            {
                CaptureYawReference(targetPosition);
                return;
            }
            if (!_headingSource.TryGetHeading(targetPosition, out float heading))
                return;

            // DeltaAngle keeps the swing on the short way round, so crossing ±180 never spins the camera.
            float desired = _yawRelativeToStart ? Mathf.DeltaAngle(_yawReferenceDegrees, heading) : heading;
            float error = Mathf.DeltaAngle(_yawDegrees, desired);
            float step = _yawSharpness > 0f ? error * (1f - Mathf.Exp(-_yawSharpness * deltaTime)) : error;
            if (_yawMaxDegreesPerSecond > 0f)
            {
                float cap = _yawMaxDegreesPerSecond * deltaTime;
                step = Mathf.Clamp(step, -cap, cap);
            }
            _yawDegrees = Mathf.DeltaAngle(0f, _yawDegrees + step);
        }

        private Vector3 CurrentOffset()
        {
            return _headingSource == null || _yawDegrees == 0f
                ? _localOffset
                : Quaternion.Euler(0f, _yawDegrees, 0f) * _localOffset;
        }

        /// <summary>Provisional tuning only. Non-finite values and a negative sharpness leave
        /// the current values untouched rather than throwing, because tuning is not a contract
        /// violation.</summary>
        public void Configure(Vector3 localOffset, float lookHeight, float followSharpness)
        {
            if (!IsFinite(localOffset) || !IsFinite(lookHeight))
                return;
            if (!IsFinite(followSharpness) || followSharpness < 0f)
                return;

            _localOffset = localOffset;
            _lookHeight = lookHeight;
            _followSharpness = followSharpness;
            RebuildSettings();
        }

        /// <summary>
        /// Turns on the speed-proportional lead. Kept separate from <see cref="Configure"/> so the
        /// two callers that only tune the offset keep compiling, and so a run that never asks for a
        /// lead cannot acquire one from an offset value that happens to look like a distance.
        /// </summary>
        /// <param name="metresPerSpeed">Metres of lead per m/s of planar speed. 0 disables the lead.</param>
        /// <param name="maxMetres">Hard cap on the lead, in metres.</param>
        /// <param name="sharpness">How fast the lead settles. Non-finite or negative keeps the lead off.</param>
        /// <param name="velocitySource">Rigidbody whose planar velocity drives the lead; null falls
        /// back to the target's position delta.</param>
        public void ConfigureLookAhead(float metresPerSpeed, float maxMetres, float sharpness,
            Rigidbody velocitySource)
        {
            _lookAheadMetresPerSpeed = IsFinite(metresPerSpeed) && metresPerSpeed > 0f ? metresPerSpeed : 0f;
            _lookAheadMaxMetres = IsFinite(maxMetres) && maxMetres > 0f ? maxMetres : 0f;
            _lookAheadSharpness = IsFinite(sharpness) && sharpness >= 0f ? sharpness : 0f;
            _velocitySource = velocitySource;
            _framing.ResetLead();
            RebuildSettings();
        }

        /// <summary>
        /// Widens the framing's field of view with planar speed. A phone held at arm's length reads a
        /// 30 degree glass as flat at top speed, and the widen is smoothed on the same exponential as
        /// the lead so the horizon eases out instead of pumping on every throttle stab.
        /// </summary>
        /// <param name="view">Camera whose field of view this component then owns. Null turns it off.</param>
        /// <param name="baseFovDegrees">Field of view at rest, and the value written immediately.</param>
        /// <param name="widenDegrees">Extra degrees once the car is at <paramref name="atMetresPerSecond"/>.
        /// 0 or negative disables the widen.</param>
        /// <param name="atMetresPerSecond">Planar speed, m/s, at which the whole widen is reached.</param>
        /// <param name="sharpness">Settling rate. Non-finite or non-positive disables the widen.</param>
        public void ConfigureSpeedFov(Camera view, float baseFovDegrees, float widenDegrees,
            float atMetresPerSecond, float sharpness)
        {
            if (view == null || !IsFinite(baseFovDegrees) || widenDegrees <= 0f ||
                !IsFinite(widenDegrees) || atMetresPerSecond <= 0f || !IsFinite(atMetresPerSecond) ||
                sharpness <= 0f || !IsFinite(sharpness))
            {
                _fovActive = false;
                _fovView = null;
                return;
            }

            _fovView = view;
            _fovBaseDegrees = baseFovDegrees;
            _fovWidenDegrees = widenDegrees;
            _fovAtSpeed = atMetresPerSecond;
            _fovSharpness = sharpness;
            _fovDegrees = baseFovDegrees;
            _fovActive = true;
            view.fieldOfView = baseFovDegrees;
        }

        /// <summary>
        /// The per-frame framing update. Public so a test can drive the hot path without the player
        /// loop and prove it allocates nothing; <see cref="LateUpdate"/> is only this call.
        /// </summary>
        public void Tick()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// The same update with an explicit frame delta. The framing is solved from the delta rather
        /// than from the wall clock, and a batchmode editor renders at tens of thousands of frames per
        /// second, so a test that wants the settled framing has no way to get there from the clock.
        /// Passing the delta in is the seam that makes the follow assertable as arithmetic.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isFollowing || _target == null || _framingTransform == null)
                return;

            Vector3 targetPosition = _target.position;
            // Read once per frame: the Rigidbody read is only cheap next to doing it twice, and the
            // position-delta fallback has to see the same delta the model does.
            Vector3 velocity = PlanarVelocity(targetPosition, deltaTime);
            _framing.Step(deltaTime, new ChaseFramingTarget(targetPosition.x, targetPosition.y,
                targetPosition.z, velocity.x, velocity.z), in _settings);
            UpdateFov(velocity, deltaTime);
            UpdateYaw(targetPosition, deltaTime);

            // The framing is solved in world space on the car's filtered position; the offset is the
            // authored 3/4 bearing and is added after, so it never takes part in the easing.
            ApplyFraming(DesiredViewPosition());

            _lastTargetPosition = targetPosition;
            _hasLastTargetPosition = true;
        }

        private void Awake()
        {
            RebuildSettings();
        }

        private void OnValidate()
        {
            if (!IsFinite(_followSharpness) || _followSharpness < 0f)
                _followSharpness = 0f;
            if (!IsFinite(_verticalFollowSharpness) || _verticalFollowSharpness < 0f)
                _verticalFollowSharpness = 0f;
            if (!IsFinite(_snapTeleportMetres) || _snapTeleportMetres < 0f)
                _snapTeleportMetres = 0f;

            RebuildSettings();
        }

        /// <summary>
        /// Assembles the model's settings from the serialized tuning. The rates and distances the
        /// look profile does not carry fall back to the model's defaults, which is also what an
        /// untouched field reads as.
        /// </summary>
        private void RebuildSettings()
        {
            float vertical = _verticalFollowSharpness > 0f
                ? _verticalFollowSharpness
                : ChaseFramingSettings.DefaultVerticalSharpness;
            float snap = _snapTeleportMetres > 0f
                ? _snapTeleportMetres
                : ChaseFramingSettings.DefaultSnapDistanceMetres;
            _settings = new ChaseFramingSettings(_followSharpness, vertical, _lookAheadMetresPerSpeed,
                _lookAheadMaxMetres, _lookAheadSharpness, snap,
                ChaseFramingSettings.DefaultMaxFeedForwardSpeed);
        }

        private void LateUpdate()
        {
            Tick();
        }

        private void SnapToFraming()
        {
            // A snap is the first frame of a run, so it must not inherit the previous run's lead or
            // its widen: the next run starts on the authored field of view.
            _hasLastTargetPosition = false;
            if (_fovActive && _fovView != null)
            {
                _fovDegrees = _fovBaseDegrees;
                _fovView.fieldOfView = _fovBaseDegrees;
            }

            if (_target != null)
            {
                Vector3 targetPosition = _target.position;
                _framing.SnapTo(new ChaseFramingTarget(targetPosition.x, targetPosition.y,
                    targetPosition.z, 0f, 0f));
            }

            ApplyFraming(DesiredViewPosition());
        }

        /// <summary>
        /// Eases the written field of view toward base plus widen, scaled by how much of the widen's
        /// speed the car is actually making. Nothing is allocated and the value is only written while
        /// a widen is configured, so a lab run with a constant FOV pays one branch.
        /// </summary>
        private void UpdateFov(Vector3 velocity, float deltaTime)
        {
            if (!_fovActive || _fovView == null)
                return;

            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float desired = _fovBaseDegrees + _fovWidenDegrees * Mathf.Clamp01(speed / _fovAtSpeed);
            float blend = 1f - Mathf.Exp(-_fovSharpness * deltaTime);
            _fovDegrees = Mathf.Lerp(_fovDegrees, desired, blend);
            _fovView.fieldOfView = _fovDegrees;
        }

        /// <summary>
        /// Places this transform so the borrowed view's world pose lands on
        /// <paramref name="viewPosition"/>. The root rotation comes from the desired view rotation
        /// and the captured view-relative rotation; the root position then follows from the same
        /// pair. The view is never written, so a shaker below it keeps adding its offset on top of
        /// the framing instead of fighting it. Degenerate look geometry leaves the last rotation
        /// alone, exactly as before.
        /// </summary>
        private void ApplyFraming(Vector3 viewPosition)
        {
            Vector3 toLookPoint = Vector3.up * _lookHeight - CurrentOffset();
            if (_pivotAheadMetres > 0f && _headingSource != null)
            {
                // The pivot sits along the world heading: in relative mode the swing is measured from the
                // start's heading, so that heading is added back.
                float pivotYaw = _yawRelativeToStart ? _yawReferenceDegrees + _yawDegrees : _yawDegrees;
                toLookPoint += Quaternion.Euler(0f, pivotYaw, 0f) * Vector3.forward * _pivotAheadMetres;
            }
            if (toLookPoint.sqrMagnitude > ZeroLookVectorSqrMagnitude)
            {
                Quaternion viewRotation = Quaternion.LookRotation(toLookPoint, Vector3.up);
                transform.rotation = viewRotation * _inverseViewRotationFromRoot;
            }

            transform.position = viewPosition - transform.rotation * _viewOffsetFromRoot;
        }

        /// <summary>
        /// The world point the view is built around: the model's filtered car position with its lead,
        /// plus the authored offset. The offset is not smoothed because it is constant: easing a
        /// constant is a no-op, and adding it afterwards keeps the framing in car space.
        /// </summary>
        private Vector3 DesiredViewPosition()
        {
            return new Vector3(_framing.FramedX, _framing.FramedY, _framing.FramedZ) + CurrentOffset();
        }

        private Vector3 PlanarVelocity(Vector3 targetPosition, float deltaTime)
        {
            if (_velocitySource != null)
            {
                Vector3 bodyVelocity = _velocitySource.linearVelocity;
                return new Vector3(bodyVelocity.x, 0f, bodyVelocity.z);
            }

            if (!_hasLastTargetPosition || deltaTime <= 0f)
                return Vector3.zero;

            Vector3 delta = targetPosition - _lastTargetPosition;
            return new Vector3(delta.x, 0f, delta.z) / deltaTime;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
