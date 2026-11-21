using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Match
{
    /// <summary>
    /// The truck as something the cars can touch: a cab, a flat bed and a rear ramp built from boxes in
    /// code, on one kinematic body that drives the racing line at a constant speed. It owns its own clock
    /// (advanced in FixedUpdate), so the pose it is judged against is exactly the pose it was moved to.
    ///
    /// "Riding the bed" is decided by <see cref="TruckBed"/> from the wheel contact points, never from what
    /// the colliders report: the car motor discards the collider it stands on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TruckView : MonoBehaviour, IGroundVelocitySource
    {
        // Deck and ramp thickness: thick enough that a fast wheel cannot tunnel, thin enough to read as a deck.
        private const float SlabThickness = 0.3f;
        private const float GroundClearance = 0.25f;
        private const float CabLength = 2.2f;
        private const float CabExtraHeight = 0.9f;

        // Loud on purpose so the truck is never mistaken for scenery or a car.
        private static readonly Color DeckColor = new Color(0.95f, 0.55f, 0.12f, 1f);
        private static readonly Color RampColor = new Color(0.98f, 0.82f, 0.18f, 1f);
        private static readonly Color CabColor = new Color(0.16f, 0.52f, 0.62f, 1f);
        private static readonly Color ChassisColor = new Color(0.18f, 0.19f, 0.22f, 1f);

        private BakedPath _line;
        private TruckSettings _settings;
        private Rigidbody _body;

        private double _elapsed;
        private bool _running;
        private TruckPose _pose;
        private bool _hasPose;
        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _velocity;
        private Vector3 _angularVelocity;

        public TruckSettings Settings
        {
            get { return _settings; }
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>Line distance of the truck's origin, or 0 before the first pose.</summary>
        public float Distance
        {
            get { return _hasPose ? _pose.Distance : 0f; }
        }

        public float LoopLength
        {
            get { return _line != null ? _line.TotalLength : 0f; }
        }

        public bool TryGetPose(out TruckPose pose)
        {
            pose = _pose;
            return _hasPose;
        }

        /// <summary>Builds the truck standing at its start distance. Null when the line cannot place it.</summary>
        public static TruckView Spawn(BakedPath line, in TruckSettings settings, Material material, Transform parent)
        {
            if (!settings.IsValid || !TruckMotion.TryPoseAt(line, in settings, 0.0, out TruckPose pose))
                return null;

            var root = new GameObject("Truck");
            if (parent != null)
                root.transform.SetParent(parent, false);

            TruckView view = root.AddComponent<TruckView>();
            view.Build(line, in settings, material, in pose);
            return view;
        }

        /// <summary>The truck waits at its start until this is called, like the cars on the grid.</summary>
        public void Begin()
        {
            _running = true;
        }

        public void Stop()
        {
            _running = false;
            StandStill();
        }

        private void Build(BakedPath line, in TruckSettings settings, Material material, in TruckPose pose)
        {
            _line = line;
            _settings = settings;
            ApplyPose(in pose);

            // Placed before the body exists as a physics object, so the first fixed step sweeps from the
            // start pose and not from the world origin through every car on the grid.
            transform.SetPositionAndRotation(ToVector(pose.Position), _rotation);

            _body = gameObject.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            // Moved by MovePosition every fixed step: interpolation keeps the render smooth between steps,
            // and the speculative mode keeps a fast bed from skipping over a wheel.
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            BuildGeometry(in settings, material);
        }

        private void FixedUpdate()
        {
            if (!_running || _body == null)
            {
                StandStill();
                return;
            }

            float dt = Time.fixedDeltaTime;
            double next = _elapsed + dt;
            if (!TruckMotion.TryPoseAt(_line, in _settings, next, out TruckPose pose))
            {
                StandStill();
                return;
            }

            Vector3 previous = _hasPose ? ToVector(_pose.Position) : ToVector(pose.Position);
            Quaternion previousRotation = _rotation;
            _elapsed = next;
            ApplyPose(in pose);

            // A kinematic body moved by MovePosition reports no velocity, so the one the cars need is the
            // displacement of the pose just commanded over this step; the turn is needed as well as the
            // travel, or the yaw damping of a car on the bed fights it.
            Vector3 position = ToVector(pose.Position);
            _velocity = dt > 0f ? (position - previous) / dt : Vector3.zero;
            float yawStep = Vector3.SignedAngle(previousRotation * Vector3.forward, _rotation * Vector3.forward,
                Vector3.up);
            _angularVelocity = dt > 0f ? Vector3.up * (yawStep * Mathf.Deg2Rad / dt) : Vector3.zero;

            _body.MovePosition(position);
            _body.MoveRotation(_rotation);
        }

        // A step that does not move the body must not keep reporting the last step's motion: a car on the
        // bed would read a truck that has stopped as one still driving and drive itself off its front.
        private void StandStill()
        {
            _velocity = Vector3.zero;
            _angularVelocity = Vector3.zero;
        }

        private void ApplyPose(in TruckPose pose)
        {
            _pose = pose;
            _hasPose = true;
            Vector3 forward = ToVector(pose.Forward);
            _rotation = forward.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>
        /// True when the car's four wheels are grounded on the bed and it rides with the truck. False when
        /// the car has no driver, no body or its motor measures no contacts, which reads as "not settled".
        /// </summary>
        public bool IsRiding(LocalCarFixedStepDriver driver, Rigidbody body)
        {
            if (!_hasPose || driver == null || body == null)
                return false;

            if (!driver.TryGetWheelContactPoint(0, out Vector3 w0, out bool g0)
                || !driver.TryGetWheelContactPoint(1, out Vector3 w1, out bool g1)
                || !driver.TryGetWheelContactPoint(2, out Vector3 w2, out bool g2)
                || !driver.TryGetWheelContactPoint(3, out Vector3 w3, out bool g3))
                return false;

            byte mask = 0;
            if (g0) mask |= 1;
            if (g1) mask |= 2;
            if (g2) mask |= 4;
            if (g3) mask |= 8;

            Vector3 velocity = body.linearVelocity;
            var probe = new WheelProbe(ToPath(w0), ToPath(w1), ToPath(w2), ToPath(w3), mask,
                new PathVector3(velocity.x, velocity.y, velocity.z));
            return TruckBed.IsSettled(in _pose, in _settings, in probe);
        }

        /// <summary>The truck's surface velocity at a world point, from the poses it commands.</summary>
        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            Vector3 pivot = _body != null ? _body.position : ToVector(_pose.Position);
            return _velocity + Vector3.Cross(_angularVelocity, worldPoint - pivot);
        }

        public Vector3 AngularVelocity
        {
            get { return _angularVelocity; }
        }

        // ------------------------------------------------------------------ geometry

        private void BuildGeometry(in TruckSettings settings, Material material)
        {
            float bedFront = settings.BedCenterOffset + settings.BedLength * 0.5f;
            float bedRear = settings.BedCenterOffset - settings.BedLength * 0.5f;

            // The deck's top face sits exactly at BedHeight: that is the plane the bed test measures against.
            AddBox("Deck",
                new Vector3(0f, settings.BedHeight - SlabThickness * 0.5f, settings.BedCenterOffset),
                Quaternion.identity,
                new Vector3(settings.BedWidth, SlabThickness, settings.BedLength),
                DeckColor, material);

            // A solid body under the deck, so a car cannot drive in beneath the thin slab from the side.
            float chassisTop = settings.BedHeight - SlabThickness;
            float chassisHeight = chassisTop - GroundClearance;
            if (chassisHeight > 0.05f)
            {
                AddBox("Chassis",
                    new Vector3(0f, GroundClearance + chassisHeight * 0.5f, settings.BedCenterOffset),
                    Quaternion.identity,
                    new Vector3(settings.BedWidth * 0.9f, chassisHeight, settings.BedLength * 0.96f),
                    ChassisColor, material);
            }

            float cabTop = settings.BedHeight + CabExtraHeight;
            float cabHeight = cabTop - GroundClearance;
            AddBox("Cab",
                new Vector3(0f, GroundClearance + cabHeight * 0.5f, bedFront + CabLength * 0.5f),
                Quaternion.identity,
                new Vector3(settings.BedWidth, cabHeight, CabLength),
                CabColor, material);

            BuildRamp(in settings, bedRear, material);
        }

        // A slab whose top face runs from the ground at the foot of the ramp up to the deck's rear edge. The
        // slab is centred half a thickness below that line along the surface normal, so the top face (the part
        // a wheel touches) is exactly the sloped line and the rest of the slab hides under it.
        private void BuildRamp(in TruckSettings settings, float bedRear, Material material)
        {
            float run = settings.RampLength;
            float rise = settings.BedHeight;
            float slopeLength = Mathf.Sqrt(run * run + rise * rise);
            float angle = Mathf.Atan2(rise, run);

            float footZ = bedRear - run;
            Vector3 surfaceMid = new Vector3(0f, rise * 0.5f, footZ + run * 0.5f);
            Vector3 normal = new Vector3(0f, Mathf.Cos(angle), -Mathf.Sin(angle));
            Vector3 center = surfaceMid - normal * (SlabThickness * 0.5f);

            // Pitched about the right axis so the slab's +Z runs up the slope.
            Quaternion pitch = Quaternion.Euler(-angle * Mathf.Rad2Deg, 0f, 0f);
            AddBox("Ramp", center, pitch, new Vector3(settings.BedWidth, SlabThickness, slopeLength), RampColor,
                material);
        }

        private void AddBox(string label, Vector3 center, Quaternion rotation, Vector3 size, Color color,
            Material material)
        {
            // A primitive cube brings the unit mesh and a unit BoxCollider, so scaling the child sizes both.
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = label;

            Transform partTransform = part.transform;
            partTransform.SetParent(transform, false);
            partTransform.localPosition = center;
            partTransform.localRotation = rotation;
            partTransform.localScale = size;

            var renderer = part.GetComponent<Renderer>();
            if (material != null)
                renderer.sharedMaterial = material;

            // Tinted through a property block: the shared material is never edited.
            var block = new MaterialPropertyBlock();
            block.SetColor(Shader.PropertyToID("_BaseColor"), color);
            block.SetColor(Shader.PropertyToID("_Color"), color);
            renderer.SetPropertyBlock(block);
        }

        private static Vector3 ToVector(PathVector3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static PathVector3 ToPath(Vector3 value)
        {
            return new PathVector3(value.x, value.y, value.z);
        }
    }
}
