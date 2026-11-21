using System.Collections.Generic;
using FestivalDerby.World;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// Reproduces today's big oval as a spline, then bakes it to the shipped MapAsset. The point is
    /// parity: the baked map must describe the same stadium the graybox oval factory already builds,
    /// so the two paths can be compared side by side and the spline pipeline has a real reference.
    ///
    /// The centerline math is copied from TrackLayoutSettings.CenterlineSample (90 x 150 m, 25 m
    /// corners) rather than referenced, because the World editor assembly must not depend on the
    /// driving runtime; the constants below are the same numbers, and the oval generator's job is
    /// to be reviewable against that function.
    /// </summary>
    public static class OvalMapGenerator
    {
        public const string GenerateMenu = "Festival Derby/Generate Oval Map (D21)";
        public const string MapAssetPath = "Assets/FreebutGood/FestivalDerby/Resources/Maps/Map_BigOval.asset";
        private const string MapsFolder = "Assets/FreebutGood/FestivalDerby/Resources/Maps";
        private const string SceneObjectName = "Map_BigOval (spline)";

        internal const float OuterWidth = 90f;
        internal const float OuterLength = 150f;
        internal const float CornerRadius = 25f;

        /// <summary>Enough knots that the auto-smoothed spline stays within a few millimetres of the true arc.</summary>
        private const int KnotCount = 96;

        [MenuItem(GenerateMenu)]
        public static void Generate()
        {
            SplineContainer container = CreateOrReplaceSceneSpline();
            MapAsset map = MapBaker.Bake(container, BakedMapSettings.OvalDefaults());

            if (!AssetDatabase.IsValidFolder(MapsFolder))
                AssetDatabase.CreateFolder("Assets/FreebutGood/FestivalDerby/Resources", "Maps");
            MapBaker.WriteAsset(map, MapAssetPath);

            Selection.activeGameObject = container.gameObject;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MapAsset>(MapAssetPath));
            Debug.Log("OvalMapGenerator: baked " + MapAssetPath + " (lap " +
                map.Roads[0].Path.TotalLength.ToString("F1") + " m, " + map.Roads[0].Path.Count + " rings).");
        }

        private static SplineContainer CreateOrReplaceSceneSpline()
        {
            GameObject existing = GameObject.Find(SceneObjectName);
            SplineContainer container = existing != null ? existing.GetComponent<SplineContainer>() : null;
            if (container == null)
            {
                var go = new GameObject(SceneObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create oval spline");
                container = go.AddComponent<SplineContainer>();
            }
            Undo.RecordObject(container, "Bake oval spline");

            var knots = new List<float3>(KnotCount);
            float lap = LapLength();
            for (int i = 0; i < KnotCount; i++)
            {
                CenterlineSample(lap * i / KnotCount, out Vector2 point);
                // The road sits on the surface; the baked path is what the collider follows.
                knots.Add(new float3(point.x, 0f, point.y));
            }

            container.Splines = new List<Spline> { new Spline(knots, TangentMode.AutoSmooth, closed: true) };
            return container;
        }

        /// <summary>
        /// Perimeter of the shipped stadium. Public so a test can assert the baked path against the
        /// preset's own geometry instead of a copied number that would drift when the layout changes.
        /// </summary>
        public static float LapLength()
        {
            return LapLength(0.5f * OuterWidth, 0.5f * OuterLength, CornerRadius);
        }

        /// <summary>
        /// Perimeter of the same stadium for arbitrary extents. A preset can describe a smaller
        /// arena without a second copy of the corner logic, and the numbers stay comparable to the
        /// oval this generator reproduces.
        ///
        /// The straights are counted twice each: the sampler's corner centers sit at halfWidth - radius
        /// and halfLength - radius, so the four arcs are joined by TWO straights of 2 * (halfLength -
        /// radius) at the sides and TWO of 2 * (halfWidth - radius) at the ends. Using one of each
        /// (the naive stadium formula) undershoots the lap by a quarter of the perimeter, which makes a
        /// preset write knots that stop short of the seam.
        /// </summary>
        internal static float LapLength(float halfWidth, float halfLength, float radius)
        {
            float straightX = Mathf.Max(0f, halfWidth - radius);
            float straightZ = Mathf.Max(0f, halfLength - radius);
            return 4f * straightX + 4f * straightZ + 2f * Mathf.PI * radius;
        }

        /// <summary>Centerline point at a lap distance, in floor-local XZ, centered on the origin.</summary>
        internal static void CenterlineSample(float distance, out Vector2 point)
        {
            CenterlineSample(distance, 0.5f * OuterWidth, 0.5f * OuterLength, CornerRadius, out point);
        }

        /// <summary>
        /// Centerline point of a stadium with the given extents. Exposed so a preset builds the same
        /// shape at another size instead of reimplementing the two straights and four quarter arcs.
        /// </summary>
        internal static void CenterlineSample(float distance, float halfWidth, float halfLength, float radius,
            out Vector2 point)
        {
            float hx = Mathf.Max(0f, halfWidth - radius);
            float hz = Mathf.Max(0f, halfLength - radius);
            float quarter = 0.5f * Mathf.PI * radius;

            float c1 = hz;
            float c2 = c1 + quarter;
            float c3 = c2 + 2f * hx;
            float c4 = c3 + quarter;
            float c5 = c4 + 2f * hz;
            float c6 = c5 + quarter;
            float c7 = c6 + 2f * hx;
            float c8 = c7 + quarter;
            float lap = c8 + hz;

            float s = distance % lap;
            if (s < 0f)
                s += lap;

            if (s < c1)
                point = new Vector2(halfWidth, s);
            else if (s < c2)
                point = Arc(hx, hz, s - c1, 0f, radius);
            else if (s < c3)
                point = new Vector2(hx - (s - c2), halfLength);
            else if (s < c4)
                point = Arc(-hx, hz, s - c3, 0.5f * Mathf.PI, radius);
            else if (s < c5)
                point = new Vector2(-halfWidth, hz - (s - c4));
            else if (s < c6)
                point = Arc(-hx, -hz, s - c5, Mathf.PI, radius);
            else if (s < c7)
                point = new Vector2(-hx + (s - c6), -halfLength);
            else if (s < c8)
                point = Arc(hx, -hz, s - c7, 1.5f * Mathf.PI, radius);
            else
                point = new Vector2(halfWidth, -hz + (s - c8));
        }

        /// <summary>Corner sample at an arc distance into a quarter turn, in floor-local XZ.</summary>
        private static Vector2 Arc(float centerX, float centerZ, float arcDistance, float startAngle, float radius)
        {
            float angle = startAngle + arcDistance / radius;
            return new Vector2(
                centerX + radius * Mathf.Cos(angle),
                centerZ + radius * Mathf.Sin(angle));
        }
    }
}
