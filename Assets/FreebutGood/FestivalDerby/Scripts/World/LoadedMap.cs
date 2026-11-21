using System;
using System.Collections.Generic;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// Everything one <see cref="MapLoader"/> call built. It owns the root, the asphalt material and
    /// the generated meshes until <see cref="DetachOwnership"/> hands them to another owner, which
    /// is what lets the driving bootstrap reuse the same teardown contract the oval circuit has.
    /// </summary>
    public sealed class LoadedMap : IDisposable
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        internal LoadedMap(Transform parent)
        {
            Root = new GameObject("Baked map");
            Root.SetActive(false);
            Root.transform.SetParent(parent, false);
            // Parent first, then place: the root lands at world zero whatever the parent's pose is.
            Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public GameObject Root { get; private set; }
        public Material Material { get; internal set; }

        /// <summary>The main road's ribbon mesh, shared by its MeshFilter and MeshCollider.</summary>
        public Mesh RoadMesh { get; internal set; }

        // Layout primitives, in the shape GrayboxCircuitLayout expects, so the bootstrap can build
        // the enclosure around the baked surface without knowing how the map was authored.
        public Vector2 Center { get; internal set; }
        public Vector2 Size { get; internal set; }
        public float SurfaceY { get; internal set; }
        public float SpawnHeight { get; internal set; }
        public float WallHeight { get; internal set; }
        public float WallThickness { get; internal set; }

        public BoxCollider[] Walls { get; internal set; } = Array.Empty<BoxCollider>();
        public Transform StartGate { get; internal set; }
        public Pose[] SpawnPoses { get; internal set; } = Array.Empty<Pose>();
        public Pose[] RoutePoses { get; internal set; } = Array.Empty<Pose>();
        public float[] CheckpointDistances { get; internal set; } = Array.Empty<float>();

        public BakedPath MainPath { get; internal set; }
        public BakedPath RacingLine { get; internal set; }
        public int[] SupportedModes { get; internal set; } = Array.Empty<int>();

        /// <summary>Camera hint from the map, so a big map can pull the chase camera back.</summary>
        public Vector3 CameraOffset { get; internal set; }
        public float CameraLookHeight { get; internal set; }
        public bool HasCameraOverride { get; internal set; }

        /// <summary>True when the loader skipped visuals, for a headless collider-only consumer.</summary>
        public bool CollidersOnly { get; internal set; }

        public bool IsDisposed { get; private set; }

        internal void AddMesh(Mesh mesh)
        {
            if (mesh != null)
                _meshes.Add(mesh);
        }

        /// <summary>
        /// Hands the root, material and meshes to a new owner. After this the map no longer destroys
        /// them, because the driving circuit took them over through the factory seam.
        /// </summary>
        public void DetachOwnership()
        {
            Root = null;
            Material = null;
            RoadMesh = null;
            _meshes.Clear();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            Walls = Array.Empty<BoxCollider>();
            SpawnPoses = Array.Empty<Pose>();
            RoutePoses = Array.Empty<Pose>();

            if (Root != null)
            {
                Root.SetActive(false);
                UnityEngine.Object.Destroy(Root);
                Root = null;
            }
            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] != null)
                    UnityEngine.Object.Destroy(_meshes[i]);
            }
            _meshes.Clear();
            if (Material != null)
            {
                UnityEngine.Object.Destroy(Material);
                Material = null;
            }
            RoadMesh = null;
        }
    }
}
