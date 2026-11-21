using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// The starting shapes an author picks instead of dragging ninety knots by hand. A preset writes
    /// knots and the authoring numbers that suit them, then everything is editable as if it had been
    /// drawn; nothing here is a second source of truth for the bake.
    /// </summary>
    public static class DerbyMapPresets
    {
        public enum Kind
        {
            Oval = 0,

            /// <summary>
            /// Two lobes crossing at the origin. The crossing is a junction, which the current scope
            /// refuses, so MapValidator reports it: the preset exists to have the shape on screen and
            /// get that feedback, not to bake a junction map.
            /// </summary>
            FigureEight = 1,

            /// <summary>A short, wide rounded rectangle for a DERBY arena.</summary>
            Arena = 2,
        }

        private const int OvalKnots = 96;
        private const int FigureEightKnots = 64;
        private const int ArenaKnots = 48;

        /// <summary>Half-extents and corner radius of the arena preset, in metres.</summary>
        private const float ArenaHalfWidth = 35f;
        private const float ArenaHalfLength = 22f;
        private const float ArenaRadius = 10f;

        private const float FigureEightRadius = 30f;

        public static int DefaultKnotCount(Kind kind)
        {
            switch (kind)
            {
                case Kind.FigureEight: return FigureEightKnots;
                case Kind.Arena: return ArenaKnots;
                default: return OvalKnots;
            }
        }

        public static Vector3[] Build(Kind kind, int knotCount, Vector3 origin)
        {
            switch (kind)
            {
                case Kind.FigureEight: return FigureEight(knotCount, origin);
                case Kind.Arena: return Arena(knotCount, origin);
                default: return Oval(knotCount, origin);
            }
        }

        /// <summary>
        /// The shipped big oval: the same stadium TrackLayoutSettings already builds, sampled at the
        /// same lap distances so the spline pipeline has a map it can be compared against.
        /// </summary>
        public static Vector3[] Oval(int knotCount, Vector3 origin)
        {
            int count = Mathf.Max(8, knotCount);
            float lap = OvalMapGenerator.LapLength();
            var knots = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                // The generator's own sampler, so the preset oval is the shipped oval by construction
                // and a change to the track layout cannot leave the preset at an old size.
                OvalMapGenerator.CenterlineSample(lap * i / count, out Vector2 point);
                knots[i] = new Vector3(origin.x + point.x, origin.y, origin.z + point.y);
            }

            return knots;
        }

        /// <summary>A stadium with the arena's extents, sampled through the oval's own corner math.</summary>
        public static Vector3[] Arena(int knotCount, Vector3 origin)
        {
            int count = Mathf.Max(8, knotCount);
            float lap = OvalMapGenerator.LapLength(ArenaHalfWidth, ArenaHalfLength, ArenaRadius);
            var knots = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                OvalMapGenerator.CenterlineSample(lap * i / count, ArenaHalfWidth, ArenaHalfLength, ArenaRadius,
                    out Vector2 point);
                knots[i] = new Vector3(origin.x + point.x, origin.y, origin.z + point.y);
            }

            return knots;
        }

        /// <summary>
        /// A Gerono lemniscate: x = a cos t, z = a sin t cos t. It is used instead of two circles
        /// because the sample count stays uniform around one closed loop, so the crossing lands
        /// between two knots instead of needing a knot exactly on it.
        /// </summary>
        public static Vector3[] FigureEight(int knotCount, Vector3 origin)
        {
            int count = Mathf.Max(8, knotCount);
            var knots = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = 2f * Mathf.PI * i / count;
                float x = FigureEightRadius * Mathf.Cos(t);
                float z = FigureEightRadius * Mathf.Sin(t) * Mathf.Cos(t);
                knots[i] = new Vector3(origin.x + x, origin.y, origin.z + z);
            }

            return knots;
        }

        /// <summary>
        /// Writes a preset into the authoring object and its spline in one undo step, including the
        /// numbers that suit the shape, so a preset behaves like one edit and can be undone as one.
        /// </summary>
        public static void Apply(DerbyMapAuthoring authoring, Kind kind)
        {
            if (authoring == null)
            {
                return;
            }

            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            if (container == null)
            {
                return;
            }

            Undo.RecordObject(authoring, "Derby map preset");
            authoring.Kind = kind == Kind.Arena ? DerbyMapAuthoring.MapKind.Derby : DerbyMapAuthoring.MapKind.Lap;
            authoring.ClosedLoop = true;
            authoring.RoadWidth = kind == Kind.Arena ? 18f : 16f;
            authoring.Sanitize();
            Vector3[] knots = Build(kind, DefaultKnotCount(kind), authoring.GridOrigin);
            DerbyMapGeometry.WriteWorldKnots(container, authoring, knots, "Derby map preset");
            EditorUtility.SetDirty(authoring);
        }
    }
}
