using System.Collections.Generic;
using FestivalDerby.World;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// The bridge between the authored spline and the Core knot math, plus the preview geometry the
    /// Scene View tool draws. It is deliberately free of Handles and Editor GUI: the capture script
    /// builds the same edges with LineRenderers, and a test can call it without a Scene View.
    /// </summary>
    public static class DerbyMapGeometry
    {
        /// <summary>Preview resolution along the curve. 160 keeps the edge polyline visibly smooth.</summary>
        public const int PreviewSamples = 160;

        /// <summary>The road edge polylines and their per-sample surface colour, ready to draw.</summary>
        public struct EdgePreview
        {
            public Vector3[] Left;
            public Vector3[] Right;
            public Color[] Colors;

            /// <summary>The completed loop when symmetry is linked, or null when there is nothing to ghost.</summary>
            public Vector3[] Ghost;

            public float Length;
        }

        private static readonly Color AsphaltColor = new Color(0.42f, 0.42f, 0.44f, 1f);
        private static readonly Color DirtColor = new Color(0.55f, 0.42f, 0.26f, 1f);
        private static readonly Color SnowColor = new Color(0.82f, 0.88f, 0.95f, 1f);
        private static readonly Color MudColor = new Color(0.36f, 0.29f, 0.2f, 1f);
        private static readonly Color GravelColor = new Color(0.68f, 0.64f, 0.58f, 1f);

        /// <summary>
        /// The container the authoring data describes. The authoring component deliberately does not
        /// reference the spline package, so every editor entry point resolves the container here; a
        /// missing one is a normal state (an object the author has not set up yet), never an exception.
        /// </summary>
        public static SplineContainer ContainerOf(DerbyMapAuthoring authoring)
        {
            return authoring != null ? authoring.GetComponent<SplineContainer>() : null;
        }

        public static Vector3[] ReadWorldKnots(SplineContainer container)
        {
            if (container == null || container.Splines.Count == 0)
            {
                return new Vector3[0];
            }

            Spline spline = container.Splines[0];
            var result = new Vector3[spline.Count];
            Matrix4x4 toWorld = container.transform.localToWorldMatrix;
            for (int i = 0; i < spline.Count; i++)
            {
                result[i] = toWorld.MultiplyPoint3x4((Vector3)(float3)spline[i].Position);
            }

            return result;
        }

        /// <summary>
        /// Central-difference tangents in world space. Auto-smooth recomputes the real curve tangents on
        /// every write, so these only feed the symmetry math, which needs a direction and not a curvature.
        /// </summary>
        public static Vector3[] ReadWorldTangents(Vector3[] positions, bool closed)
        {
            if (positions == null || positions.Length < 2)
            {
                return positions == null ? new Vector3[0] : new Vector3[positions.Length];
            }

            int count = positions.Length;
            var tangents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int previous = i - 1;
                int next = i + 1;
                if (previous < 0)
                {
                    previous = closed ? count - 1 : 0;
                }

                if (next >= count)
                {
                    next = closed ? 0 : count - 1;
                }

                Vector3 tangent = positions[next] - positions[previous];
                tangents[i] = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
            }

            return tangents;
        }

        /// <summary>
        /// Replaces the spline with this knot list. The spline is treated as a view of the authored
        /// points: knots are rewritten wholesale with auto-smoothed tangents so the road can never keep
        /// a stale tangent from an earlier drag.
        /// </summary>
        public static void WriteWorldKnots(SplineContainer container, DerbyMapAuthoring authoring,
            IList<Vector3> world, string undoName)
        {
            if (container == null || container.Splines.Count == 0 || world == null || world.Count < 2)
            {
                return;
            }

            Undo.RecordObject(container, undoName);
            var local = new float3[world.Count];
            Matrix4x4 toLocal = container.transform.worldToLocalMatrix;
            for (int i = 0; i < world.Count; i++)
            {
                local[i] = toLocal.MultiplyPoint3x4(world[i]);
            }

            Spline spline = container.Splines[0];
            spline.Clear();
            spline.AddRange(local, TangentMode.AutoSmooth);
            spline.Closed = authoring == null || authoring.ClosedLoop;
            EditorUtility.SetDirty(container);
        }

        public static Vector3 Snap(DerbyMapAuthoring authoring, Vector3 world)
        {
            if (authoring == null || authoring.GridStep <= 0f)
            {
                return world;
            }

            PathVector3 snapped = authoring.BuildGridSnap().Snap(ToPath(world));
            return new Vector3(snapped.x, world.y, snapped.z);
        }

        /// <summary>
        /// Where a click lands on the floor. The graybox ground has no collider and an authored map may
        /// not have a ground object at all, so the tool intersects the grid's own horizontal plane
        /// instead of raycasting the physics scene. Returns false for a camera looking away from it.
        /// </summary>
        public static bool TryProjectToGround(DerbyMapAuthoring authoring, Ray ray, out Vector3 world)
        {
            float height = authoring != null ? authoring.GridOrigin.y : 0f;
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                world = ray.GetPoint(distance);
                return true;
            }

            world = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Inserts a knot into the segment nearest to the click instead of appending, so adding a point
        /// to a closed loop keeps the loop in order rather than folding the last edge back on itself.
        /// </summary>
        public static int RemoveNearest(Vector3[] knots, bool closed, Vector3 world, float maxDistance)
        {
            if (knots == null || knots.Length <= 2)
            {
                return -1;
            }

            int nearest = NearestKnot(knots, world, maxDistance);
            if (nearest < 0)
            {
                return -1;
            }

            var kept = new List<Vector3>(knots.Length - 1);
            for (int i = 0; i < knots.Length; i++)
            {
                if (i != nearest)
                {
                    kept.Add(knots[i]);
                }
            }

            for (int i = 0; i < kept.Count; i++)
            {
                knots[i] = kept[i];
            }

            return nearest;
        }

        public static int NearestKnot(Vector3[] knots, Vector3 world, float maxDistance)
        {
            if (knots == null || knots.Length == 0)
            {
                return -1;
            }

            int nearest = -1;
            float best = maxDistance * maxDistance;
            for (int i = 0; i < knots.Length; i++)
            {
                float distance = (knots[i] - world).sqrMagnitude;
                if (distance <= best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>Index the click should be inserted *after*. -1 means append.</summary>
        public static int SegmentInsertIndex(Vector3[] knots, bool closed, Vector3 world)
        {
            if (knots == null || knots.Length < 2)
            {
                return -1;
            }

            int segments = closed ? knots.Length : knots.Length - 1;
            int bestIndex = knots.Length - 1;
            float best = float.MaxValue;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = knots[i];
                Vector3 b = knots[(i + 1) % knots.Length];
                float distance = DistanceToSegment(world, a, b);
                if (distance < best)
                {
                    best = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Completes the authored half into the loop the bake would see. Mirroring is finished here
        /// rather than in Core because MirrorTransform.Weld only trims the mirrored half: joining the
        /// two halves and dropping the two seam duplicates is the editor's job.
        /// </summary>
        public static Vector3[] BuildSymmetry(DerbyMapAuthoring authoring, Vector3[] authored)
        {
            if (authoring == null || authored == null || authored.Length < 2
                || authoring.Symmetry.Mode == DerbyMapAuthoring.SymmetryMode.None)
            {
                return authored;
            }

            bool closed = authoring.ClosedLoop;
            Vector3[] tangents = ReadWorldTangents(authored, closed);
            var source = new SymmetryKnot[authored.Length];
            for (int i = 0; i < authored.Length; i++)
            {
                source[i] = new SymmetryKnot(ToPath(authored[i]), ToPath(tangents[i]));
            }

            float tolerance = Mathf.Max(authoring.Symmetry.WeldTolerance, 0.001f);

            if (authoring.Symmetry.Mode == DerbyMapAuthoring.SymmetryMode.Rotational)
            {
                RotationalSymmetry rotational = authoring.BuildRotational();
                var buffer = new SymmetryKnot[source.Length * rotational.Count];
                int count = KnotListSymmetry.Apply(source, buffer, rotational, tolerance);
                return ToWorld(buffer, count);
            }

            MirrorTransform mirror = authoring.BuildMirror();
            var mirrored = new SymmetryKnot[source.Length];
            int mirroredCount = KnotListSymmetry.Apply(source, mirrored, mirror, tolerance);

            // One seam: the last mirrored knot is the mirror of the first authored knot. On the axis
            // those are the same point, and keeping both would put a zero-length edge in the loop.
            if (mirroredCount > 0 && (mirrored[mirroredCount - 1].Position - source[0].Position).Magnitude <= tolerance)
            {
                mirroredCount--;
            }

            var full = new SymmetryKnot[mirroredCount + source.Length];
            for (int i = 0; i < mirroredCount; i++)
            {
                full[i] = mirrored[i];
            }

            for (int i = 0; i < source.Length; i++)
            {
                full[mirroredCount + i] = source[i];
            }

            // The other seam: the loop closes from the last authored knot back to the first mirrored one.
            int total = full.Length;
            if (total > 1 && (full[total - 1].Position - full[0].Position).Magnitude <= tolerance)
            {
                total--;
            }

            var result = new Vector3[total];
            for (int i = 0; i < total; i++)
            {
                result[i] = ToVector(full[i].Position);
            }

            return result;
        }

        /// <summary>
        /// Samples the road edges. The arc length is measured along the sampled polyline, which is what
        /// the surface colours are keyed on, and the total is fed to SurfaceSpanSet so the preview and
        /// the bake agree about where a span starts.
        /// </summary>
        public static EdgePreview SamplePreview(SplineContainer container, DerbyMapAuthoring authoring, int samples)
        {
            var preview = new EdgePreview();
            if (container == null || container.Splines.Count == 0 || authoring == null)
            {
                return preview;
            }

            if (samples < 8)
            {
                samples = PreviewSamples;
            }

            bool closed = authoring.ClosedLoop;
            int count = samples + 1;
            var centres = new Vector3[count];
            var rights = new Vector3[count];
            var distances = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / samples;
                centres[i] = container.EvaluatePosition(t);
                Vector3 tangent = (Vector3)(float3)container.EvaluateTangent(t);
                Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
                Vector3 right = flat.sqrMagnitude > 1e-8f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                rights[i] = right;
                distances[i] = i == 0 ? 0f : distances[i - 1] + (centres[i] - centres[i - 1]).magnitude;
            }

            float length = distances[count - 1];
            var spans = new SurfaceSpanSet(authoring.SurfaceSpans, length, closed, SurfaceId.Street);
            float halfWidth = authoring.RoadWidth * 0.5f;

            preview.Left = new Vector3[count];
            preview.Right = new Vector3[count];
            preview.Colors = new Color[count];
            preview.Length = length;
            for (int i = 0; i < count; i++)
            {
                preview.Left[i] = centres[i] - rights[i] * halfWidth;
                preview.Right[i] = centres[i] + rights[i] * halfWidth;
                preview.Colors[i] = SurfaceColor(spans.Evaluate(distances[i]));
            }

            if (authoring.Symmetry.Mode != DerbyMapAuthoring.SymmetryMode.None && authoring.Symmetry.Linked)
            {
                preview.Ghost = BuildSymmetry(authoring, ReadWorldKnots(container));
            }

            return preview;
        }

        /// <summary>Surface colour for the road preview, blended the same way the runtime blends grip.</summary>
        public static Color SurfaceColor(SurfaceBlend blend)
        {
            Color primary = SurfaceColor(blend.Primary);
            if (blend.Secondary == blend.Primary || blend.Blend == 0)
            {
                return primary;
            }

            float t = Mathf.Clamp01(blend.Blend / 255f);
            return Color.Lerp(primary, SurfaceColor(blend.Secondary), t);
        }

        public static Color SurfaceColor(SurfaceId surface)
        {
            switch (surface)
            {
                case SurfaceId.Dirt: return DirtColor;
                case SurfaceId.Snow: return SnowColor;
                case SurfaceId.Mud: return MudColor;
                case SurfaceId.Gravel: return GravelColor;
                default: return AsphaltColor;
            }
        }

        /// <summary>Bounds of the authored knots, used to frame an issue in the Scene View.</summary>
        public static bool TryGetBounds(SplineContainer container, out Bounds bounds)
        {
            Vector3[] knots = ReadWorldKnots(container);
            if (knots.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds(knots[0], Vector3.zero);
            for (int i = 1; i < knots.Length; i++)
            {
                bounds.Encapsulate(knots[i]);
            }

            return true;
        }

        public static PathVector3 ToPath(Vector3 value)
        {
            return new PathVector3(value.x, value.y, value.z);
        }

        public static Vector3 ToVector(PathVector3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Vector3[] ToWorld(SymmetryKnot[] knots, int count)
        {
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ToVector(knots[i].Position);
            }

            return result;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 1e-8f)
            {
                return (point - a).magnitude;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
            return (point - (a + ab * t)).magnitude;
        }
    }
}
