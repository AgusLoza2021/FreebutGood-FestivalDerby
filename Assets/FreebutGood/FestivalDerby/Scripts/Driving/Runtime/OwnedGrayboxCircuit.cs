using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The boxes one circuit construction owns. Disposal is deliberately the same contract the car
    /// geometry already follows: stop activity synchronously, then let the deferred Destroy run.
    /// </summary>
    public sealed class OwnedGrayboxCircuit : IDisposable
    {
        // Private owned cache: callers read the route through Count/GetPose but can never mutate it.
        private Pose[] _route;
        // The starting grid, front slot first. One entry for the legacy and figure-eight layouts;
        // one per grid slot for a layout that defines a spawn grid.
        private Pose[] _spawnPoses;
        private readonly Mesh _roadMesh;

        internal OwnedGrayboxCircuit(GameObject root, Material material, in GrayboxCircuitLayout layout,
            BoxCollider[] walls, Transform startGate, Pose[] spawnPoses, Pose[] route, Mesh roadMesh = null)
        {
            Root = root;
            Material = material;
            Layout = layout;
            Walls = walls;
            StartGate = startGate;
            _spawnPoses = spawnPoses ?? Array.Empty<Pose>();
            _route = route ?? Array.Empty<Pose>();
            _roadMesh = roadMesh;
        }

        public GameObject Root { get; }
        public Material Material { get; }
        public GrayboxCircuitLayout Layout { get; }

        /// <summary>Perimeter walls or curved edge barriers; the only colliders this geometry owns.</summary>
        public BoxCollider[] Walls { get; }

        /// <summary>Container of the decorative start/finish gate.</summary>
        public Transform StartGate { get; }

        /// <summary>Car centre behind the line, facing the next goal and clearing the surface.</summary>
        public Pose SpawnPose => _spawnPoses.Length > 0 ? _spawnPoses[0] : default;

        /// <summary>
        /// How many separated grid slots this circuit offers. One for the legacy and figure-eight
        /// layouts; the full row-by-row grid for a layout that defines one. The player owns slot 0.
        /// </summary>
        public int SpawnPoseCount => _spawnPoses.Length;

        /// <summary>True when the layout placed a real starting grid rather than a single spawn.</summary>
        public bool HasSpawnGrid => _spawnPoses.Length > 1;

        /// <summary>
        /// Returns one grid slot by value, front row first. Like the route, the grid stays an
        /// immutable private cache and a valid index never allocates.
        /// </summary>
        public Pose GetSpawnPose(int index)
        {
            if (index < 0 || index >= _spawnPoses.Length)
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Spawn index {index} is outside the {_spawnPoses.Length} grid slots.");
            return _spawnPoses[index];
        }

        /// <summary>
        /// Ordered world-space lap goals: five for the legacy rectangle, 24 for the flat eight, or
        /// one per centerline sample for the rounded oval (48 by default). The figure-eight's two
        /// crossing visits remain distinct ordered entries, never nearest-branch choices.
        /// </summary>
        public int RouteCount => _route.Length;

        /// <summary>
        /// Returns one route pose by value. The index is validated on every call, but a valid
        /// index never allocates or copies the array, so per-tick route queries stay alloc-free.
        /// </summary>
        public Pose GetRoutePose(int index)
        {
            if (index < 0 || index >= _route.Length)
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Route index {index} is outside the {_route.Length} cached goals.");
            return _route[index];
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            // Clearing the caches keeps RouteCount/GetRoutePose/SpawnPoseCount consistent with the
            // torn-down markers and grid.
            _route = Array.Empty<Pose>();
            _spawnPoses = Array.Empty<Pose>();
            if (Root != null)
            {
                Root.SetActive(false);
                UnityEngine.Object.Destroy(Root);
            }
            ReleaseIfRuntime(_roadMesh);
            ReleaseIfRuntime(Material);
        }

        /// <summary>
        /// A baked map stores its road chunks and surface material as sub-assets, so destroying them
        /// here would delete project assets; only a mesh or material built at runtime belongs to this
        /// circuit. Unity releases the persistent ones together with the map.
        /// </summary>
        private static void ReleaseIfRuntime(UnityEngine.Object owned)
        {
            if (owned == null)
                return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(owned))
                return;
#endif
            UnityEngine.Object.Destroy(owned);
        }
    }
}
