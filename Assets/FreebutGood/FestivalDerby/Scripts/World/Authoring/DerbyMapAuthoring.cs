using System;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// Authoring-only companion for the SplineContainer that draws a Derby road. The spline keeps its
    /// job of holding the curve; everything a bake needs that is not a curve (kind, width, closed loop,
    /// surface spans, symmetry, grid, target asset) lives here so the tool and the overlay read one
    /// source and undo works on a plain serialized component.
    ///
    /// It lives in this runtime assembly, not in the Editor folder next to its tools, because Unity
    /// refuses to attach an Editor-assembly MonoBehaviour to a GameObject. It stays spline-free for the
    /// same reason the runtime has no spline dependency: the container is found by the editor-side
    /// helpers, so nothing that ships ever touches the spline package. An object carrying this
    /// component is an authoring rehearsal, never a shipped scene object.
    /// </summary>
    [AddComponentMenu("Festival Derby/Derby Map Authoring")]
    public sealed class DerbyMapAuthoring : MonoBehaviour
    {
        public enum MapKind
        {
            /// <summary>A long closed lap; the racing line is the whole spline.</summary>
            Lap = 0,

            /// <summary>A short, wide arena that spawns the field in a grid and races a few laps.</summary>
            Derby = 1,
        }

        /// <summary>
        /// How the authored half is completed into a full loop. The axes name the plane they mirror
        /// across, matching MirrorTransform: MirrorX keeps z and flips x around Pivot.x.
        /// </summary>
        public enum SymmetryMode
        {
            None = 0,
            MirrorX = 1,
            MirrorZ = 2,
            Rotational = 3,
        }

        [Serializable]
        public struct SymmetrySettings
        {
            public SymmetryMode Mode;
            public Vector3 Pivot;

            [Range(2, 12)]
            public int RotationalCount;

            /// <summary>While set, edits draw the completed loop as a ghost. Cleared by "Apply symmetry".</summary>
            public bool Linked;

            /// <summary>Two knots closer than this are the same knot; the seam duplicate is dropped.</summary>
            public float WeldTolerance;
        }

        [Header("Map")]
        public MapKind Kind = MapKind.Lap;

        /// <summary>Bake id and asset name stem. Also what a run asks MapLoader for.</summary>
        public string MapId = "BigOval";

        public bool ClosedLoop = true;

        [Tooltip("Full road width in metres. The preview edges sit at half of this on each side.")]
        public float RoadWidth = 16f;

        [Header("Roads")]
        [Tooltip("Leading splines the bake turns into drivable roads. Splines after these stay barrier edges.")]
        [Range(1, 8)]
        public int RoadCount = 1;

        [Header("Surfaces (metres of arc length, later span wins)")]
        public SurfaceSpan[] SurfaceSpans = new SurfaceSpan[0];

        [Header("Symmetry")]
        public SymmetrySettings Symmetry = new SymmetrySettings
        {
            Mode = SymmetryMode.None,
            Pivot = Vector3.zero,
            RotationalCount = 4,
            Linked = false,
            WeldTolerance = 0.25f,
        };

        [Header("Grid")]
        [Tooltip("Snap step for new and dragged knots. Zero turns snapping off.")]
        public float GridStep = 1f;

        public Vector3 GridOrigin = Vector3.zero;

        [Header("Bake")]
        public MapAsset Target;

        private void OnValidate()
        {
            Sanitize();
        }

        /// <summary>
        /// Clamps the authoring numbers the way every consumer will read them, so a half-typed value in
        /// the Inspector cannot reach MapBaker, the validator or the mesh sweep.
        /// </summary>
        public void Sanitize()
        {
            RoadWidth = Mathf.Max(RoadWidth, 2f);
            RoadCount = Mathf.Max(1, RoadCount);
            GridStep = Mathf.Max(GridStep, 0f);
            Symmetry.RotationalCount = Mathf.Clamp(Symmetry.RotationalCount, 2, 12);
            Symmetry.WeldTolerance = Mathf.Max(Symmetry.WeldTolerance, 0.001f);

            if (string.IsNullOrEmpty(MapId))
            {
                MapId = name;
            }

            if (SurfaceSpans == null)
            {
                SurfaceSpans = new SurfaceSpan[0];
                return;
            }

            for (int i = 0; i < SurfaceSpans.Length; i++)
            {
                SurfaceSpan span = SurfaceSpans[i];
                if (span.EndDistance < span.StartDistance)
                {
                    float swap = span.StartDistance;
                    span.StartDistance = span.EndDistance;
                    span.EndDistance = swap;
                }

                span.TransitionLength = Mathf.Max(span.TransitionLength, 0f);
                SurfaceSpans[i] = span;
            }
        }

        public GridSnap BuildGridSnap()
        {
            return new GridSnap(GridStep, new PathVector3(GridOrigin.x, GridOrigin.y, GridOrigin.z));
        }

        public MirrorTransform BuildMirror()
        {
            GridAxis axis = Symmetry.Mode == SymmetryMode.MirrorZ ? GridAxis.Z : GridAxis.X;
            return new MirrorTransform(axis, new PathVector3(Symmetry.Pivot.x, Symmetry.Pivot.y, Symmetry.Pivot.z));
        }

        public RotationalSymmetry BuildRotational()
        {
            return new RotationalSymmetry(Mathf.Max(2, Symmetry.RotationalCount),
                new PathVector3(Symmetry.Pivot.x, Symmetry.Pivot.y, Symmetry.Pivot.z));
        }
    }
}
