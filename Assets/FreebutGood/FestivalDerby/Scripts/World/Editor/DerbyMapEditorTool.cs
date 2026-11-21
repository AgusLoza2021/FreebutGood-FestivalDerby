using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// The Scene View tool for drawing a Derby road: click the floor to add a knot, drag one to move it
    /// (both snapped to the authoring grid), shift-click to remove, and a radius handle for the road
    /// width. Edges are drawn live at half the width on each side and coloured by the surface span that
    /// covers them, so the profile is visible while driving the shape instead of only at bake time.
    ///
    /// Every mutation goes through DerbyMapGeometry, which records undo and rewrites the whole knot list:
    /// a spline is a view of the authored points, so there is never a stale tangent or an orphaned edge.
    /// </summary>
    [EditorTool("Derby Map Authoring", typeof(DerbyMapAuthoring))]
    public sealed class DerbyMapEditorTool : EditorTool
    {
        private const float EdgeWidth = 3f;
        private const float GhostWidth = 2f;
        private const float KnotSize = 0.09f;

        private static readonly Color KnotColor = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color FirstKnotColor = new Color(0.3f, 1f, 0.5f, 1f);
        private static readonly Color GhostColor = new Color(0.35f, 0.8f, 1f, 0.6f);

        private GUIContent _icon;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_icon == null)
                {
                    _icon = EditorGUIUtility.IconContent("d_EditCollider", "Derby map authoring");
                }

                return _icon;
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView))
            {
                return;
            }

            DerbyMapAuthoring authoring = Active();
            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            if (container == null || container.Splines.Count == 0)
            {
                return;
            }

            Vector3[] knots = DerbyMapGeometry.ReadWorldKnots(container);
            if (knots.Length < 2)
            {
                return;
            }

            DrawPreview(DerbyMapGeometry.SamplePreview(container, authoring, DerbyMapGeometry.PreviewSamples));
            DrawWidthHandle(authoring, container);
            DragKnots(container, authoring, knots);
            HandleClick(window as SceneView, container, authoring, knots);
        }

        /// <summary>
        /// The authoring object being edited. Selection is read instead of the tool's target so the tool
        /// keeps working after a domain reload, when the target reference has been rebuilt but the tool
        /// is still the active one.
        /// </summary>
        private static DerbyMapAuthoring Active()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponent<DerbyMapAuthoring>() : null;
        }

        private static void DrawPreview(DerbyMapGeometry.EdgePreview preview)
        {
            if (preview.Left == null || preview.Right == null)
            {
                return;
            }

            for (int i = 0; i + 1 < preview.Left.Length; i++)
            {
                Handles.color = preview.Colors[i];
                Handles.DrawAAPolyLine(EdgeWidth, preview.Left[i], preview.Left[i + 1]);
                Handles.DrawAAPolyLine(EdgeWidth, preview.Right[i], preview.Right[i + 1]);
            }

            if (preview.Ghost != null && preview.Ghost.Length > 1)
            {
                Handles.color = GhostColor;
                Handles.DrawAAPolyLine(GhostWidth, preview.Ghost);
            }
        }

        private static void DragKnots(SplineContainer container, DerbyMapAuthoring authoring, Vector3[] knots)
        {
            for (int i = 0; i < knots.Length; i++)
            {
                float size = HandleUtility.GetHandleSize(knots[i]) * KnotSize;
                Handles.color = i == 0 ? FirstKnotColor : KnotColor;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(knots[i], size, Vector3.zero, Handles.SphereHandleCap);
                if (!EditorGUI.EndChangeCheck())
                {
                    continue;
                }

                knots[i] = DerbyMapGeometry.Snap(authoring, moved);
                DerbyMapGeometry.WriteWorldKnots(container, authoring, knots, "Move Derby knot");
            }
        }

        /// <summary>
        /// A radius handle at the middle of the road. It is placed at the midpoint rather than on a knot
        /// because the width belongs to the road, not to one of the points that draw it.
        /// </summary>
        private static void DrawWidthHandle(DerbyMapAuthoring authoring, SplineContainer container)
        {
            const float t = 0.5f;
            Vector3 centre = container.EvaluatePosition(t);
            Vector3 tangent = (float3)container.EvaluateTangent(t);
            Quaternion rotation = tangent.sqrMagnitude > 1e-6f
                ? Quaternion.FromToRotation(Vector3.up, tangent.normalized)
                : Quaternion.identity;

            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(rotation, centre, authoring.RoadWidth * 0.5f);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(authoring, "Change Derby road width");
            authoring.RoadWidth = Mathf.Max(2f, radius * 2f);
            EditorUtility.SetDirty(authoring);
        }

        /// <summary>
        /// Adds a knot where the click lands on the grid plane, or removes the nearest one on
        /// shift-click. A click that grabbed a knot handle already owns the hot control, so it is not
        /// mistaken for a click on the floor.
        /// </summary>
        private void HandleClick(SceneView view, SplineContainer container, DerbyMapAuthoring authoring,
            Vector3[] knots)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.MouseDown || current.button != 0
                || current.alt || GUIUtility.hotControl != 0)
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!DerbyMapGeometry.TryProjectToGround(authoring, ray, out Vector3 world))
            {
                return;
            }

            Vector3 snapped = DerbyMapGeometry.Snap(authoring, world);
            float threshold = Mathf.Max(authoring.GridStep, 1f) * 1.5f;

            if (current.shift)
            {
                int removed = DerbyMapGeometry.RemoveNearest(knots, authoring.ClosedLoop, snapped, threshold);
                if (removed < 0)
                {
                    return;
                }

                var kept = new List<Vector3>(knots.Length - 1);
                for (int i = 0; i < knots.Length - 1; i++)
                {
                    kept.Add(knots[i]);
                }

                DerbyMapGeometry.WriteWorldKnots(container, authoring, kept, "Delete Derby knot");
            }
            else if (DerbyMapGeometry.NearestKnot(knots, snapped, threshold) < 0)
            {
                InsertKnot(container, authoring, knots, snapped);
            }
            else
            {
                // Clicking on top of an existing knot is a miss, not a new point on top of it.
                return;
            }

            current.Use();
            if (view != null)
            {
                view.Repaint();
            }
        }

        /// <summary>
        /// Inserts into the segment nearest the click instead of appending, so adding a point to a closed
        /// loop keeps the loop in order instead of folding the last edge back on itself.
        /// </summary>
        private static void InsertKnot(SplineContainer container, DerbyMapAuthoring authoring, Vector3[] knots,
            Vector3 world)
        {
            int after = DerbyMapGeometry.SegmentInsertIndex(knots, authoring.ClosedLoop, world);
            int index = after < 0 ? knots.Length : after + 1;

            var list = new List<Vector3>(knots.Length + 1);
            for (int i = 0; i < knots.Length; i++)
            {
                if (i == index)
                {
                    list.Add(world);
                }

                list.Add(knots[i]);
            }

            if (index >= knots.Length)
            {
                list.Add(world);
            }

            DerbyMapGeometry.WriteWorldKnots(container, authoring, list, "Add Derby knot");
        }
    }
}
