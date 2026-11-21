using System.Collections.Generic;
using FestivalDerby.World;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// Bakes the Festival Circuit - the compact club circuit the Circuit Superstars look is judged on -
    /// from a layout of straights and constant-radius arcs, and writes it as a shipped MapAsset with its
    /// road meshes.
    ///
    /// Three things make it different from <see cref="OvalMapGenerator"/>, which it is modelled on. It
    /// builds its spline on a temporary object it destroys before returning, so running the menu item can
    /// never leave a SplineContainer in whatever scene is open. It authors arcs rather than corners,
    /// because an auto-smoothed spline follows a polygon only as faithfully as the polygon is smooth: a
    /// corner knot with short neighbours bends far tighter than the corner it came from, while a chain of
    /// knots every <see cref="KnotChord"/> metres along a real arc is followed to within a few per cent of
    /// the arc's radius. And it measures the shape it authored before it writes anything: a lap outside
    /// the brief's band, or a corner under <see cref="MinimumRadius"/>, is reported and nothing is written.
    /// </summary>
    public static class CompactMapGenerator
    {
        public const string GenerateMenu = "Festival Derby/Maps/Generate Festival Circuit";
        public const string MapId = "FestivalCircuit";
        public const string MapAssetPath =
            "Assets/FreebutGood/FestivalDerby/Resources/Maps/Map_FestivalCircuit.asset";

        private const string MapsFolder = "Assets/FreebutGood/FestivalDerby/Resources/Maps";
        private const string TempObjectName = "FestivalCircuit (temp authoring)";

        /// <summary>
        /// Road width the brief asks for, in metres. Widened from 12 m with the 1.5x scale-up: the car
        /// did not grow, so the extra width is room to fight and overtake a 24-car field, not scale.
        /// </summary>
        public const float RoadWidth = 18f;

        /// <summary>Centreline radius no corner may go under, in metres.</summary>
        public const float MinimumRadius = 21f;

        /// <summary>
        /// Spacing of the guide's knots along straights and arcs, in metres. Scaled with the layout so
        /// the baked path keeps the same number of rings per corner rather than paying for the size-up
        /// twice.
        /// </summary>
        public const float KnotChord = 3.75f;

        /// <summary>Metres the fit targets when a layout edit lands outside the band.</summary>
        public const float TargetLapLength = 1050f;
        public const float MinLapLength = 900f;
        public const float MaxLapLength = 1200f;

        /// <summary>Checkpoints the lap is cut into; evenly spaced by the bake.</summary>
        public const int CheckpointCount = 6;

        /// <summary>
        /// The one dirt section, as fractions of the fitted lap so it follows the fit. It runs from the
        /// entry of the lap's longest left-hander, round it and out of its exit onto the straight after.
        /// </summary>
        private const float DirtStartFraction = 0.705f;
        private const float DirtEndFraction = 0.805f;
        private const float DirtTransitionMetres = 12f;

        /// <summary>Same warn-before-the-cusp margin the editor overlay uses, so the two agree.</summary>
        private const float PinchWidthMargin = 0.1f;
        private const int PinchCapacity = 64;

        /// <summary>
        /// Cameras are the look's business: these are the values the shipped oval carries and
        /// <c>LookDefaults</c> was tuned to, copied so a map override cannot disagree with the profile.
        /// </summary>
        private static readonly Vector3 CameraOffset = new Vector3(-16f, 30f, -14f);
        private const float CameraLookHeight = 2f;

        /// <summary>Where the lap starts, on the main straight, and the origin of every arc centre.</summary>
        private static readonly Vector2 LayoutStart = new Vector2(90f, 0f);

        /// <summary>
        /// The circuit, in lap order: straights with their solved lengths and arcs with their radius and
        /// sweep, all in metres and degrees. It carries the features the brief asks for - the long
        /// start/finish straight, a two-arc fast sweeper, a 180 degree hairpin, a right/left/right chicane
        /// and the long left-hander the dirt section sits on - and it closes back on the start line.
        ///
        /// The straight lengths are not free-hand: they were solved so the arc centres, the arc sweep
        /// total (exactly 360 degrees) and the straight headings return the walker to
        /// <see cref="LayoutStart"/> facing its original heading. Closing a lap like this by eye does not
        /// work - a residual of a few metres folds the loop somewhere and produces a hairpin nobody
        /// authored - so the lengths spread the closure instead of one straight absorbing it.
        ///
        /// Every number below is the authored shape at 1.5x. The scale is a homothety, so the closure
        /// the solver found stays exact and every corner keeps its sweep while gaining radius: nothing
        /// about the track's character moves, only how much room there is for a 24-car field.
        /// </summary>
        private static readonly Element[] Layout =
        {
            Element.S(90f),            // start/finish straight: the line sits 90 m in, the grid behind it
            Element.T(67.5f, 45f),     // fast sweeper, first half
            Element.S(70.2f),
            Element.T(48f, 45f),       // fast sweeper, second half: the two arcs make one 90 degree bend
            Element.S(60.45f),
            Element.T(30f, 180f),      // hairpin
            Element.S(9f),
            Element.T(30f, -90f),      // right-hander onto the back straight
            Element.S(9f),
            Element.T(45f, -50f),      // chicane, right
            Element.T(45f, 100f),      // chicane, left
            Element.T(45f, -50f),      // chicane, right
            Element.S(9f),
            Element.T(60f, -50f),
            Element.S(29.55f),
            Element.T(51f, 80f),       // the long left-hander that carries the dirt
            Element.S(15.15f),
            Element.T(48f, 67f),
            Element.S(36.45f),
            Element.T(60f, 50f),
            Element.S(66.9f),
            Element.T(60f, 33f),
            Element.S(90.75f),         // releases onto the start straight, collinear with Element 0
        };

        private static float guideClosureError;

        /// <summary>The guide knots, built once: the bake reads them and the report names them.</summary>
        private static readonly Vector2[] Guide = BuildGuide();

        /// <summary>One piece of the layout: a straight, or a constant-radius arc.</summary>
        private readonly struct Element
        {
            public readonly float Radius;
            public readonly float Length;
            public readonly float Sweep;

            private Element(float radius, float length, float sweep)
            {
                Radius = radius;
                Length = length;
                Sweep = sweep;
            }

            public bool IsArc => Radius > 0f;

            public static Element S(float length) => new Element(0f, length, 0f);
            public static Element T(float radius, float sweep) => new Element(radius, 0f, sweep);
        }

        /// <summary>
        /// Walks <see cref="Layout"/> and returns the knots of the closed centreline. It lands exactly
        /// back on the start point - the straight lengths were solved for that - so the duplicate closing
        /// knot is dropped: a repeated knot is a zero-length chord, the one thing an auto-smoothed
        /// tangent cannot handle. <see cref="guideClosureError"/> records how far off the walk landed.
        /// </summary>
        public static Vector2[] BuildGuide()
        {
            var points = new List<Vector2>(512) { LayoutStart };
            Vector2 cursor = LayoutStart;
            float heading = 0f;

            for (int i = 0; i < Layout.Length; i++)
            {
                Element element = Layout[i];
                if (!element.IsArc)
                {
                    int steps = Mathf.Max(1, Mathf.RoundToInt(element.Length / KnotChord));
                    Vector2 direction = Direction(heading);
                    for (int step = 1; step <= steps; step++)
                        points.Add(cursor + direction * (element.Length * step / steps));
                    cursor = points[points.Count - 1];
                    continue;
                }

                float sign = element.Sweep >= 0f ? 1f : -1f;
                Vector2 centre = cursor + Direction(heading + 90f * sign) * element.Radius;
                float startAngle = Mathf.Atan2(cursor.y - centre.y, cursor.x - centre.x);
                float sweep = element.Sweep * Mathf.Deg2Rad;
                int arcSteps = Mathf.Max(2,
                    Mathf.RoundToInt(Mathf.Abs(sweep) * element.Radius / KnotChord));
                for (int step = 1; step <= arcSteps; step++)
                {
                    float angle = startAngle + sweep * step / arcSteps;
                    points.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * element.Radius);
                }

                cursor = points[points.Count - 1];
                heading += element.Sweep;
            }

            guideClosureError = Vector2.Distance(cursor, LayoutStart);
            points.RemoveAt(points.Count - 1);
            return points.ToArray();
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        [MenuItem(GenerateMenu)]
        public static void Generate()
        {
            // The temp object is destroyed in the finally, and DontSave alone would leave it in the
            // hierarchy, so it is hidden as well: nothing this generator builds is the owner's to undo.
            var host = new GameObject(TempObjectName) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                SplineContainer container = host.AddComponent<SplineContainer>();

                // Pass one: the authored shape at authored size, to learn what lap it makes. The layout
                // is authored to land inside the band, so the fit only rescues an edit that ran long or
                // short; when it is already legal it is left alone, which keeps the designed radii.
                BakedPath authored = BakeProbe(container, 1f, null);
                float authoredLength = authored.TotalLength;
                bool inBand = authoredLength >= MinLapLength && authoredLength <= MaxLapLength;
                float scale = inBand ? 1f : TargetLapLength / Mathf.Max(authoredLength, 1f);

                // Pass two: the same shape scaled about the start line, with a dirt span measured off
                // the length it actually bakes to.
                BakedPath fitted = BakeProbe(container, scale, null);
                float lap = fitted.TotalLength;
                var spans = new[]
                {
                    new SurfaceSpan(0f, lap, SurfaceId.Street),
                    new SurfaceSpan(lap * DirtStartFraction, lap * DirtEndFraction, SurfaceId.Dirt,
                        DirtTransitionMetres),
                };

                BakedMapSettings settings = Settings(spans);
                MapAsset map = MapBaker.Bake(container, settings);

                float bakedLap = map.Roads[0].Path.TotalLength;
                float radius = MinRadius(map.Roads[0].Path);
                Report(authoredLength, scale, bakedLap, radius, map.Roads[0].Path, spans);
                LogTightest(map.Roads[0].Path, scale, 6);

                if (bakedLap < MinLapLength || bakedLap > MaxLapLength || radius < MinimumRadius)
                {
                    Debug.LogError("[agent] circuit refused: lap " + bakedLap.ToString("F1") + " m (band "
                        + MinLapLength + "-" + MaxLapLength + "), min radius " + radius.ToString("F1")
                        + " m (floor " + MinimumRadius + "). Nothing was written; fix the control points.");
                    return;
                }

                if (!AssetDatabase.IsValidFolder(MapsFolder))
                    AssetDatabase.CreateFolder("Assets/FreebutGood/FestivalDerby/Resources", "Maps");

                MapBaker.WriteAsset(map, MapAssetPath);
                MapBaker.WriteRoadMeshes(map, settings);
                Debug.Log("[agent] circuit wrote " + MapAssetPath + " (lap " + bakedLap.ToString("F1")
                    + " m, " + map.Roads[0].Path.Count + " rings, " + map.Roads[0].Chunks.Length
                    + " road chunks).");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static BakedPath BakeProbe(SplineContainer container, float scale, SurfaceSpan[] spans)
        {
            container.Splines = new List<Spline> { BuildSpline(scale) };
            return MapBaker.BakePath(container, container.Splines[0], Settings(spans));
        }

        /// <summary>The closed auto-smoothed spline the bake reads, scaled about the start line.</summary>
        private static Spline BuildSpline(float scale)
        {
            var knots = new List<float3>(Guide.Length);
            for (int i = 0; i < Guide.Length; i++)
            {
                Vector2 point = Guide[i] * scale;
                // The road lies on the ground plane; the baked path is what the collider follows.
                knots.Add(new float3(point.x, 0f, point.y));
            }

            return new Spline(knots, TangentMode.AutoSmooth, closed: true);
        }

        /// <summary>
        /// Everything the bake needs that is not the spline. The inherited oval values are kept for the
        /// barriers and the sampling resolution, because those were tuned against a road of the same
        /// order of size; the grid is the 3x2 the brief asks for.
        /// </summary>
        private static BakedMapSettings Settings(SurfaceSpan[] spans)
        {
            BakedMapSettings settings = BakedMapSettings.OvalDefaults();
            settings.MapId = MapId;
            settings.RoadWidth = RoadWidth;
            settings.SurfaceSpans = spans;
            settings.CheckpointCount = CheckpointCount;
            settings.CameraOffset = CameraOffset;
            settings.CameraLookHeight = CameraLookHeight;
            settings.SpawnColumns = 3;
            settings.SpawnRows = 2;
            settings.SpawnSpacing = 4.5f;
            // Curvature is inverse metres, so a 1.5x wider corner carries 1/1.5 of it. Left at the
            // oval's 2/100 m the bake would read the size-up's sweeper as a straight and sample it
            // coarse, so the threshold scales with the world it classifies.
            settings.CornerCurvatureThreshold = BakedMapSettings.OvalDefaults().CornerCurvatureThreshold / 1.5f;
            return settings;
        }

        // ------------------------------------------------------------- what the tests read

        /// <summary>
        /// Tightest centreline radius the baked path actually has, in metres. Straight samples have no
        /// curvature and are skipped; a path with no curve at all reports <c>infinity</c>.
        /// </summary>
        public static float MinRadius(BakedPath path)
        {
            if (path == null || path.Curvature == null)
                return float.PositiveInfinity;

            float tightest = float.MaxValue;
            for (int i = 0; i < path.Curvature.Length; i++)
            {
                float magnitude = Mathf.Abs(path.Curvature[i]);
                if (magnitude < 1e-6f)
                    continue;
                float radius = 1f / magnitude;
                if (radius < tightest)
                    tightest = radius;
            }

            return tightest == float.MaxValue ? float.PositiveInfinity : tightest;
        }

        /// <summary>Metres of one surface along the baked path.</summary>
        public static float SurfaceLength(BakedPath path, SurfaceId surface)
        {
            if (path == null || path.SurfacePrimary == null || path.Distances == null)
                return 0f;

            float metres = 0f;
            int rings = Mathf.Min(path.Count, path.SurfacePrimary.Length);
            for (int i = 0; i < rings - 1; i++)
            {
                if (path.SurfacePrimary[i] == (byte)surface)
                    metres += path.Distances[i + 1] - path.Distances[i];
            }

            return metres;
        }

        /// <summary>Arc length at which a surface first appears, or -1 when it never does.</summary>
        public static float FirstSurfaceDistance(BakedPath path, SurfaceId surface)
        {
            if (path == null || path.SurfacePrimary == null || path.Distances == null)
                return -1f;

            int rings = Mathf.Min(path.SurfacePrimary.Length, path.Distances.Length);
            for (int i = 0; i < rings; i++)
            {
                if (path.SurfacePrimary[i] == (byte)surface)
                    return path.Distances[i];
            }

            return -1f;
        }

        /// <summary>
        /// Runs the shipped validator over a baked map, the way the editor overlay runs it over what is
        /// authored: knot curvature, closure, overlap and surface coverage from the core validator, plus
        /// the pinch scan that only a path sampled with real widths and curvature can see. Knots come
        /// from the baked path itself, ten metres apart, so the answer is about the map that shipped and
        /// not about a second copy of the control points that could drift from it.
        /// </summary>
        public static MapIssue[] Validate(MapAsset map, MapIssue[] buffer)
        {
            if (map == null || map.PrimaryRoad == null || map.PrimaryRoad.Path == null)
                return new MapIssue[0];

            BakedPath path = map.PrimaryRoad.Path;
            var input = new MapValidationInput
            {
                Knots = KnotsAlong(path, 10f),
                ClosedExpected = path.Closed,
                MinimumRadius = MinimumRadius,
                SurfaceSpans = ExpectedSpans(path.TotalLength),
                Checkpoints = map.CheckpointDistances,
                PathLength = path.TotalLength,
                PathClosed = path.Closed,
            };

            int count = MapValidator.Validate(input, buffer);
            var pinch = new MapIssue[PinchCapacity];
            int pinchCount = PathPinch.Scan(path, PinchWidthMargin, pinch);

            var issues = new MapIssue[count + pinchCount];
            System.Array.Copy(buffer, issues, count);
            System.Array.Copy(pinch, 0, issues, count, pinchCount);
            return issues;
        }

        /// <summary>
        /// The surface profile this generator authors for a lap of <paramref name="lap"/> metres. Public
        /// so a test checks the coverage rule against the same spans the bake was asked for instead of
        /// against a second copy of the numbers.
        /// </summary>
        public static SurfaceSpan[] ExpectedSpans(float lap)
        {
            return new[]
            {
                new SurfaceSpan(0f, lap, SurfaceId.Street),
                new SurfaceSpan(lap * DirtStartFraction, lap * DirtEndFraction, SurfaceId.Dirt,
                    DirtTransitionMetres),
            };
        }

        private static SymmetryKnot[] KnotsAlong(BakedPath path, float spacing)
        {
            var knots = new List<SymmetryKnot>(Mathf.CeilToInt(path.TotalLength / spacing) + 1);
            float next = 0f;
            float last = path.TotalLength - spacing * 0.5f;
            for (int i = 0; i < path.Count - 1 && path.Distances[i] <= last; i++)
            {
                if (path.Distances[i] < next)
                    continue;

                knots.Add(new SymmetryKnot(path.Positions[i], path.Tangents[i]));
                next += spacing;
            }

            // A closed knot list repeats its first knot, which is what makes the closure rule and the
            // seam exception in the overlap rule mean the same thing here as they do for an authored loop.
            if (knots.Count > 0)
                knots.Add(knots[0]);

            return knots.ToArray();
        }

        /// <summary>
        /// Where the tightest corners actually are, with the control point each one came from. A bare
        /// "too tight" number says a layout is wrong; the location says which control point to move.
        /// </summary>
        public static void LogTightest(BakedPath path, float scale, int count)
        {
            if (path == null || path.Curvature == null)
                return;

            var worst = new float[count];
            var index = new int[count];
            for (int i = 0; i < count; i++)
                index[i] = -1;

            for (int i = 0; i < path.Curvature.Length; i++)
            {
                float magnitude = Mathf.Abs(path.Curvature[i]);
                if (magnitude <= 0f)
                    continue;
                for (int slot = 0; slot < count; slot++)
                {
                    if (index[slot] >= 0 && worst[slot] >= magnitude)
                        continue;
                    for (int shift = count - 1; shift > slot; shift--)
                    {
                        worst[shift] = worst[shift - 1];
                        index[shift] = index[shift - 1];
                    }

                    worst[slot] = magnitude;
                    index[slot] = i;
                    break;
                }
            }

            for (int slot = 0; slot < count && index[slot] >= 0; slot++)
            {
                int i = index[slot];
                Debug.Log("[agent] circuit tight #" + (slot + 1) + ": radius "
                    + (1f / worst[slot]).ToString("F1") + " m at " + path.Distances[i].ToString("F1")
                    + " m, position (" + path.Positions[i].x.ToString("F0") + ", "
                    + path.Positions[i].z.ToString("F0") + "), nearest guide knot #"
                    + NearestControlPoint(path.Positions[i], scale));
            }
        }

        private static string NearestControlPoint(PathVector3 position, float scale)
        {
            int nearest = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Guide.Length; i++)
            {
                float dx = Guide[i].x * scale - position.x;
                float dz = Guide[i].y * scale - position.z;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = i;
                }
            }

            if (nearest < 0)
                return "none";

            return nearest + " at (" + (Guide[nearest].x * scale).ToString("F0") + ", "
                + (Guide[nearest].y * scale).ToString("F0") + ")";
        }

        /// <summary>Asphalt width at the start of the baked path, in metres.</summary>
        private static float RoadWidthAt(BakedPath path)
        {
            if (path.WidthLeft == null || path.WidthRight == null || path.WidthLeft.Length == 0
                || path.WidthRight.Length == 0)
                return 0f;
            return path.WidthLeft[0] + path.WidthRight[0];
        }

        /// <summary>
        /// One line per fact the report quotes, grepped out of the batch log. A still number that only
        /// lived in a variable would not be evidence anyone could re-check.
        /// </summary>
        private static void Report(float authoredLength, float scale, float lap, float radius,
            BakedPath path, SurfaceSpan[] spans)
        {
            Debug.Log("[agent] circuit guide: " + Guide.Length + " knots, closure error "
                + guideClosureError.ToString("F2") + " m, chord " + KnotChord.ToString("F1") + " m");
            Debug.Log("[agent] circuit fit: authored lap " + authoredLength.ToString("F1") + " m, scale "
                + scale.ToString("F4") + ", fitted lap " + lap.ToString("F1") + " m");
            Debug.Log("[agent] circuit geometry: min radius " + radius.ToString("F1") + " m, width "
                + RoadWidthAt(path).ToString("F1") + " m, rings " + path.Count
                + ", closed " + path.Closed);
            Debug.Log("[agent] circuit dirt: " + SurfaceLength(path, SurfaceId.Dirt).ToString("F1")
                + " m starting at " + FirstSurfaceDistance(path, SurfaceId.Dirt).ToString("F1")
                + " m of " + path.TotalLength.ToString("F1") + " m (" + spans[1].StartDistance.ToString("F1")
                + " -> " + spans[1].EndDistance.ToString("F1") + ")");
        }
    }
}
