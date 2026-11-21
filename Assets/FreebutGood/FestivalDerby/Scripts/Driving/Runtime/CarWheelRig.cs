using System;
using System.Collections.Generic;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Spins and steers the cosmetic wheels of an attached car model. The rig is built once, after
    /// <see cref="CarVisualAttacher"/> has fitted the clone: it wraps every child whose name contains
    /// "wheel" as Steer pivot -> Spin pivot -> node, with the pivots placed at the node's own
    /// renderer-bounds centre, so no FBX pivot is trusted and nothing moves visually. Each frame it
    /// reads the driver's existing debug readout and touches only the clone's subtree: the fixed tick,
    /// the body and the physics are untouched. A model with no wheel node (or no clone at all) makes
    /// the rig a no-op.
    /// </summary>
    public sealed class CarWheelRig : MonoBehaviour
    {
        // A wheel bound under this radius has no meaningful rolling diameter; spinning it would
        // divide by ~zero, so its spin is left alone.
        private const float MinimumSpinRadius = 1e-4f;

        private const string SteerSuffix = ".Steer";
        private const string SpinSuffix = ".Spin";

        private struct WheelPivot
        {
            public Transform Steer;
            public Transform Spin;
            public float Radius;
            public bool Steers;
            public float SpinDegrees;
            // Both axes are the model's own, expressed in the pivot's local frame, and each is already
            // oriented so that a positive angle does the thing the driver asked for: the axle rolls the
            // top of the wheel towards the nose, the steer axis turns the wheel towards the car's right.
            public Vector3 SpinAxis;
            public Vector3 SteerAxis;
        }

        // One build-time sort entry; never kept past Build.
        private struct WheelNode
        {
            public Transform Node;
            // Position in the visual's local space: front is +z, left is -x.
            public float Z;
            public float X;
        }

        private WheelPivot[] _wheels = Array.Empty<WheelPivot>();
        private LocalCarFixedStepDriver _driver;

        /// <summary>How many wheel nodes the rig wrapped, in FL, FR, RL, RR order.</summary>
        public int WheelCount => _wheels.Length;

        /// <summary>The steer pivot of the wheel at <paramref name="index"/>, or null.</summary>
        public Transform GetSteerPivot(int index)
            => index >= 0 && index < _wheels.Length ? _wheels[index].Steer : null;

        /// <summary>The spin pivot of the wheel at <paramref name="index"/>, or null.</summary>
        public Transform GetSpinPivot(int index)
            => index >= 0 && index < _wheels.Length ? _wheels[index].Spin : null;

        /// <summary>
        /// Discovers and wraps the wheel nodes of one attached clone. Safe to call with a null
        /// driver (the rig then never spins on its own) or a clone without wheel nodes (the rig
        /// no-ops). Build-time discovery may allocate; <see cref="Apply"/> never does.
        /// </summary>
        /// <param name="spinSign">
        /// The owner's per-model roll direction (a negative value flips this model and only this one).
        /// The axis and its base sign come from the wheel mesh, so 1 is right for a model whose wheels
        /// are round about the axle the exporter gave them.
        /// </param>
        public void Build(Transform visual, LocalCarFixedStepDriver driver, float spinSign = 1f)
        {
            _driver = driver;
            _wheels = Array.Empty<WheelPivot>();
            if (visual == null)
                return;

            // Depth-first traversal reports parents before children, so a node nested inside an
            // already-collected wheel is dropped instead of being wrapped twice.
            var transforms = visual.GetComponentsInChildren<Transform>(true);
            var nodes = new List<WheelNode>(4);
            for (int i = 0; i < transforms.Length; i++)
            {
                var node = transforms[i];
                if (node == null || node == visual || !IsWheelNode(node.name))
                    continue;

                bool nested = false;
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (node.IsChildOf(nodes[j].Node))
                    {
                        nested = true;
                        break;
                    }
                }
                if (nested)
                    continue;

                // InverseTransformPoint equals localPosition for a direct child and stays honest
                // through an FBX's own rotations and scales.
                Vector3 local = visual.InverseTransformPoint(node.position);
                nodes.Add(new WheelNode { Node = node, X = local.x, Z = local.z });
            }

            if (nodes.Count == 0)
                return;

            nodes.Sort(CompareFrontLeftFirst);
            _wheels = BuildPivots(nodes, visual, spinSign < 0f ? -1f : 1f);
        }

        /// <summary>
        /// One visual step: the front pair steers to the readout's steer angle and every wheel with a
        /// usable radius rolls by forwardSpeed / radius. Allocation-free; called from LateUpdate with
        /// the driver's readout, or directly from tests with a synthetic one.
        /// </summary>
        public void Apply(in CarHandlingReadout readout, float dt)
        {
            if (_wheels.Length == 0 || dt <= 0f)
                return;

            // The steer angle is a core angle, and the core's yaw is the mirror of Unity's: its positive
            // yaw is a left turn while a positive rotation about the visual's up is a right one. Applying
            // it unreserved is what made the cosmetic wheels point away from the corner they were
            // entering, so the one sign that differs between the two frames is flipped here.
            float steerDegrees = -readout.SteerAngleDegrees;
            for (int i = 0; i < _wheels.Length; i++)
            {
                ref WheelPivot wheel = ref _wheels[i];
                if (wheel.Steers && wheel.Steer != null)
                    wheel.Steer.localRotation = Quaternion.AngleAxis(steerDegrees, wheel.SteerAxis);

                if (wheel.Radius > MinimumSpinRadius && wheel.Spin != null)
                {
                    // Travel along the ground divided by the rolling circumference, accumulated in
                    // degrees and wrapped so a long run cannot drift into float precision loss.
                    wheel.SpinDegrees = Mathf.Repeat(
                        wheel.SpinDegrees + readout.ForwardSpeedMetersPerSecond / wheel.Radius
                            * Mathf.Rad2Deg * dt, 360f);
                    wheel.Spin.localRotation = Quaternion.AngleAxis(wheel.SpinDegrees, wheel.SpinAxis);
                }
            }
        }

        private void LateUpdate()
        {
            if (_driver == null)
                return;

            // Presentation-only readout: never allocates, never feeds back into a step.
            Apply(_driver.ReadHandlingDebug(), Time.deltaTime);
        }

        private WheelPivot[] BuildPivots(List<WheelNode> nodes, Transform visual, float spinSign)
        {
            // The front half of the sorted set steers; for the usual four-node model that is exactly
            // the FL/FR pair, and any other count still splits at the middle of the z range.
            float maxZ = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                maxZ = Mathf.Max(maxZ, nodes[i].Z);
                minZ = Mathf.Min(minZ, nodes[i].Z);
            }
            float midZ = (minZ + maxZ) * 0.5f;

            var pivots = new WheelPivot[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                Transform node = nodes[i].Node;
                Transform parent = node.parent;
                bool hasBounds = TryWheelBounds(node, out Bounds bounds);

                var steer = new GameObject(node.name + SteerSuffix);
                steer.transform.SetParent(parent, false);
                steer.transform.localPosition = hasBounds
                    ? parent.InverseTransformPoint(bounds.center)
                    : node.localPosition;
                steer.transform.localRotation = Quaternion.identity;
                steer.transform.localScale = Vector3.one;

                var spin = new GameObject(node.name + SpinSuffix);
                spin.transform.SetParent(steer.transform, false);
                spin.transform.localPosition = Vector3.zero;
                spin.transform.localRotation = Quaternion.identity;
                spin.transform.localScale = Vector3.one;

                // The clone's own axes, in the frame the pivots live in: the clone is what the driver
                // reads as "forward" and "right", and an FBX may carry any rotation between it and this
                // node's parent.
                Vector3 frameUp = DirectionInFrame(parent, visual.up, Vector3.up);
                Vector3 frameForward = DirectionInFrame(parent, visual.forward, Vector3.forward);
                Vector3 frameRight = DirectionInFrame(parent, visual.right, Vector3.right);

                // Read the model's own axle before the re-parent moves the node, so the direction is the
                // one the exporter wrote rather than the one the new chain reports.
                Vector3 spinAxis = OrientForForwardRoll(
                    WheelAxleInFrame(node, parent), frameUp, frameForward) * spinSign;

                // World-pose-stays re-parent: Unity rewrites the node's locals, so the mesh does not
                // move even when the FBX chain carries its own rotation or scale.
                node.SetParent(spin.transform, true);

                pivots[i] = new WheelPivot
                {
                    Steer = steer.transform,
                    Spin = spin.transform,
                    Radius = hasBounds ? 0.5f * MaxComponent(bounds.size) : 0f,
                    Steers = nodes[i].Z >= midZ,
                    SpinAxis = spinAxis,
                    SteerAxis = OrientForRightSteer(frameUp, frameForward, frameRight),
                };
            }

            return pivots;
        }

        /// <summary>
        /// A direction of the clone's frame, expressed in <paramref name="frame"/>. Falls back to the
        /// clone's own axis when the two frames are the same or the parent has collapsed a scale.
        /// </summary>
        private static Vector3 DirectionInFrame(Transform frame, Vector3 direction, Vector3 fallback)
        {
            if (frame == null)
                return fallback;

            Vector3 local = frame.InverseTransformDirection(direction);
            return local.sqrMagnitude > 1e-8f ? local.normalized : fallback;
        }

        /// <summary>
        /// The axle of one wheel: the thinnest dimension of the wheel's own mesh, mapped through whatever
        /// rotation the FBX put on the node. A wheel is a disc about its axle, so the two long dimensions
        /// are its diameter and the short one is the axle. Reading the mesh instead of assuming local X
        /// is what lets one rig drive a model exported Y-up and a model exported nose-forward, and it is
        /// also what tells the owner's per-model sign which way round the axle came in.
        /// </summary>
        private static Vector3 WheelAxleInFrame(Transform node, Transform frame)
        {
            var filter = node.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null || frame == null)
                return Vector3.right;

            Vector3 axis = ThinnestAxis(filter.sharedMesh.bounds.size);
            if (axis == Vector3.zero)
                return Vector3.right;

            Vector3 local = frame.InverseTransformDirection(filter.transform.TransformDirection(axis));
            return local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.right;
        }

        private static Vector3 ThinnestAxis(Vector3 size)
        {
            float x = Mathf.Abs(size.x);
            float y = Mathf.Abs(size.y);
            float z = Mathf.Abs(size.z);
            if (x <= y && x <= z)
                return x > 0f ? Vector3.right : Vector3.zero;

            if (y <= z)
                return y > 0f ? Vector3.up : Vector3.zero;

            return z > 0f ? Vector3.forward : Vector3.zero;
        }

        /// <summary>
        /// Flips an axle so that a positive roll angle carries the top of the wheel towards the nose,
        /// which is what a car driving forward does to its wheels. The displacement of a point under a
        /// rotation is angle times the cross product of the axis with that point, so comparing the cross
        /// of the axle with the frame's up against the frame's forward reads the sign instead of guessing it.
        /// </summary>
        private static Vector3 OrientForForwardRoll(Vector3 axle, Vector3 frameUp, Vector3 frameForward)
            => Vector3.Dot(Vector3.Cross(axle, frameUp), frameForward) < 0f ? -axle : axle;

        /// <summary>
        /// Flips the steer axis so that a positive angle points the wheel towards the car's right, matching
        /// a positive rotation about the clone's up.
        /// </summary>
        private static Vector3 OrientForRightSteer(Vector3 frameUp, Vector3 frameForward, Vector3 frameRight)
            => Vector3.Dot(Vector3.Cross(frameUp, frameForward), frameRight) < 0f ? -frameUp : frameUp;

        private static bool TryWheelBounds(Transform node, out Bounds bounds)
        {
            var renderers = node.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool has = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                if (has)
                    bounds.Encapsulate(renderers[i].bounds);
                else
                {
                    bounds = renderers[i].bounds;
                    has = true;
                }
            }

            return has;
        }

        private static float MaxComponent(Vector3 value)
            => Mathf.Max(value.x, Mathf.Max(value.y, value.z));

        private static bool IsWheelNode(string name)
            => !string.IsNullOrEmpty(name) && name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;

        // Front first (larger z), then left first (smaller x): FL, FR, RL, RR.
        private static int CompareFrontLeftFirst(WheelNode a, WheelNode b)
        {
            int byZ = b.Z.CompareTo(a.Z);
            return byZ != 0 ? byZ : a.X.CompareTo(b.X);
        }
    }
}
