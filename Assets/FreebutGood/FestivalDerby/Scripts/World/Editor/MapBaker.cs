using System;
using System.Collections.Generic;
using FestivalDerby.World;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// The authoring knobs one bake uses. Everything the runtime needs is derived from these, so a
    /// bake is reproducible from the settings alone and the recipe can live in a generator.
    /// </summary>
    [Serializable]
    public struct BakedMapSettings
    {
        public string MapId;
        public float RoadWidth;
        public float BankingDegrees;

        /// <summary>
        /// How many leading splines the bake turns into drivable roads; splines after these stay
        /// barrier edges. The primary road is always splines[0], so raising this count never moves the
        /// road the runtime routes from (MapAsset.PrimaryRoad is Roads[0]: spawn grid, racing line,
        /// bot route, checkpoints and grip all stay put).
        /// </summary>
        public int RoadCount;

        // Authored arc-length surface spans. Null means "all asphalt", which is what the oval recipe
        // and every map baked before surfaces existed produce.
        public SurfaceSpan[] SurfaceSpans;

        // Baking resolution: long rings on a straight, short rings through a corner.
        public float StraightSampleStep;
        public float CornerSampleStep;
        public float CornerCurvatureThreshold;

        // Barrier lines, in metres.
        public float EdgeOffset;
        public float EdgeHeight;
        public float EdgeThickness;
        public float EdgeSegmentLength;
        public float EdgeOverlap;
        public int EdgeLayer;

        public float WallHeight;
        public float WallThickness;

        // Starting grid, matching the oval's 3x2 grid so the baked map starts the same way.
        public int SpawnColumns;
        public int SpawnRows;
        public float SpawnSpacing;
        public float SpawnBackOffset;
        public float SpawnHeight;

        public float RouteStep;
        public float RacingLateralAccel;
        public float RacingTopSpeed;
        public float RacingBrakeDecel;
        public int CheckpointCount;

        public Vector3 CameraOffset;
        public float CameraLookHeight;

        public static BakedMapSettings OvalDefaults()
        {
            return new BakedMapSettings
            {
                MapId = "BigOval",
                RoadWidth = 16f,
                BankingDegrees = 0f,
                RoadCount = 1,
                StraightSampleStep = 3f,
                CornerSampleStep = 1.5f,
                CornerCurvatureThreshold = 0.02f,
                EdgeOffset = 0.5f,
                EdgeHeight = 1.2f,
                EdgeThickness = 1f,
                EdgeSegmentLength = 3f,
                EdgeOverlap = 0.3f,
                EdgeLayer = 0,
                WallHeight = 1.2f,
                WallThickness = 0.5f,
                SpawnColumns = 3,
                SpawnRows = 2,
                SpawnSpacing = 4.5f,
                SpawnBackOffset = 4.5f,
                SpawnHeight = 0.35f,
                RouteStep = 10f,
                RacingLateralAccel = 9f,
                RacingTopSpeed = 40f,
                RacingBrakeDecel = 8f,
                CheckpointCount = 4,
                CameraOffset = new Vector3(-14f, 24f, -12f),
                CameraLookHeight = 2f,
            };
        }

        /// <summary>Replaces every nonsensical value so a bake can never divide by zero.</summary>
        public BakedMapSettings Sanitized()
        {
            BakedMapSettings result = this;
            BakedMapSettings fallback = OvalDefaults();
            result.MapId = string.IsNullOrEmpty(MapId) ? fallback.MapId : MapId;
            result.RoadWidth = Positive(RoadWidth, fallback.RoadWidth);
            result.BankingDegrees = Finite(BankingDegrees, 0f);
            result.RoadCount = Mathf.Max(1, result.RoadCount);
            result.StraightSampleStep = Positive(StraightSampleStep, fallback.StraightSampleStep);
            result.CornerSampleStep = Positive(CornerSampleStep, fallback.CornerSampleStep);
            if (result.CornerSampleStep > result.StraightSampleStep)
                result.CornerSampleStep = result.StraightSampleStep;
            result.CornerCurvatureThreshold = Positive(CornerCurvatureThreshold, fallback.CornerCurvatureThreshold);
            result.EdgeOffset = Mathf.Max(Finite(EdgeOffset, fallback.EdgeOffset), 0f);
            result.EdgeHeight = Positive(EdgeHeight, fallback.EdgeHeight);
            result.EdgeThickness = Mathf.Max(Positive(EdgeThickness, fallback.EdgeThickness), 1f);
            result.EdgeSegmentLength = Positive(EdgeSegmentLength, fallback.EdgeSegmentLength);
            result.EdgeOverlap = Mathf.Max(Finite(EdgeOverlap, fallback.EdgeOverlap), 0f);
            result.WallHeight = Positive(WallHeight, fallback.WallHeight);
            result.WallThickness = Positive(WallThickness, fallback.WallThickness);
            result.SpawnColumns = Mathf.Max(SpawnColumns, 1);
            result.SpawnRows = Mathf.Max(SpawnRows, 1);
            result.SpawnSpacing = Positive(SpawnSpacing, fallback.SpawnSpacing);
            result.SpawnBackOffset = Mathf.Max(Finite(SpawnBackOffset, fallback.SpawnBackOffset), 0f);
            result.SpawnHeight = Positive(SpawnHeight, fallback.SpawnHeight);
            result.RouteStep = Positive(RouteStep, fallback.RouteStep);
            result.RacingLateralAccel = Positive(RacingLateralAccel, fallback.RacingLateralAccel);
            result.RacingTopSpeed = Positive(RacingTopSpeed, fallback.RacingTopSpeed);
            result.RacingBrakeDecel = Positive(RacingBrakeDecel, fallback.RacingBrakeDecel);
            result.CheckpointCount = Mathf.Max(CheckpointCount, 1);
            result.CameraLookHeight = Finite(CameraLookHeight, fallback.CameraLookHeight);
            return result;
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static float Positive(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// The editor half of the authoring split: it reads the spline package and writes plain data.
    /// Nothing here ships in a build; the runtime only ever sees the MapAsset this produces.
    /// </summary>
    public static class MapBaker
    {
        public const string BakeMenu = "Festival Derby/Bake Map From Selection";

        /// <summary>
        /// Bakes the selected GameObject's SplineContainer with the oval defaults: one road, and every
        /// further spline a free-standing barrier line.
        /// </summary>
        [MenuItem(BakeMenu)]
        public static void BakeSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null || selected.GetComponent<SplineContainer>() == null)
            {
                EditorUtility.DisplayDialog("Bake map",
                    "Select a GameObject that has a SplineContainer.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save baked map",
                selected.name + "_Map", "asset", "Where should the MapAsset be written?");
            if (string.IsNullOrEmpty(path))
                return;

            BakedMapSettings settings = BakedMapSettings.OvalDefaults();
            MapAsset map = Bake(selected.GetComponent<SplineContainer>(), settings);
            WriteAsset(map, path);
            WriteRoadMeshes(AssetDatabase.LoadAssetAtPath<MapAsset>(path), settings);
        }

        /// <summary>
        /// The pure bake: spline container in, MapAsset out. Separated from the menu so the oval
        /// generator and an automated test can call it without any editor UI.
        /// </summary>
        public static MapAsset Bake(SplineContainer container, BakedMapSettings settings)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            settings = settings.Sanitized();
            IReadOnlyList<Spline> splines = container.Splines;
            if (splines.Count == 0)
                throw new ArgumentException("The spline container has no splines to bake.", nameof(container));

            // Clamp so a count past the authored spline count is an authoring slip, not a bake crash.
            int roadCount = Mathf.Clamp(settings.RoadCount, 1, splines.Count);

            BakedPath roadPath = BakePath(container, splines[0], settings);
            if (!roadPath.IsUsable)
                throw new ArgumentException("The first spline is too short or degenerate to bake into a road.",
                    nameof(container));

            var roads = new List<RoadAsset>(roadCount) { CreateRoad(roadPath, 0, settings) };
            for (int i = 1; i < roadCount; i++)
            {
                BakedPath alternate = BakePath(container, splines[i], settings);
                if (!alternate.IsUsable)
                    throw new ArgumentException("Spline " + i + " is too short or degenerate to bake into a road.",
                        nameof(container));
                roads.Add(CreateRoad(alternate, i, settings));
            }

            // Only road 0 gets synthesized side barriers. An alternate lane's inner side is
            // scene-dependent (a split's hand-off between lanes must stay open), so extra roads are
            // deliberately left without edges in this slice.
            var edges = new List<EdgeAsset>
            {
                BakeEdgeAtRoadSide(roadPath, EdgeSide.Left, settings),
                BakeEdgeAtRoadSide(roadPath, EdgeSide.Right, settings),
            };
            for (int i = roadCount; i < splines.Count; i++)
                edges.Add(BakeFreeEdge(container, splines[i], i, settings));

            BakedPath racingLine = roadPath.Clone();
            racingLine.BuildTargetSpeed(settings.RacingLateralAccel, settings.RacingTopSpeed,
                settings.RacingBrakeDecel, 3);

            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.name = settings.MapId + "_Map";
            map.MapId = settings.MapId;
            map.Roads = roads.ToArray();
            map.Edges = edges.ToArray();
            map.RacingLine = racingLine;
            map.RacingLateralAccel = settings.RacingLateralAccel;
            map.RacingTopSpeed = settings.RacingTopSpeed;
            map.RacingBrakeDecel = settings.RacingBrakeDecel;
            map.SpawnRoadIndex = 0;
            map.SpawnColumns = settings.SpawnColumns;
            map.SpawnRows = settings.SpawnRows;
            map.SpawnSpacing = settings.SpawnSpacing;
            map.SpawnBackOffset = settings.SpawnBackOffset;
            map.SpawnHeight = settings.SpawnHeight;
            map.WallHeight = settings.WallHeight;
            map.WallThickness = settings.WallThickness;
            map.RouteStep = settings.RouteStep;
            map.CameraOffset = settings.CameraOffset;
            map.CameraLookHeight = settings.CameraLookHeight;
            map.HasCameraOverride = true;
            map.CheckpointDistances = EvenCheckpoints(roadPath.TotalLength, settings.CheckpointCount);
            map.SupportedModes = new[] { 0, 2 };
            map.Sanitize();
            return map;
        }

        /// <summary>
        /// Writes the map and its roads and edges into one asset file, so a map is a single thing to
        /// reference. The BakedPath instances are plain classes and serialize inline, so they need
        /// no sub-asset of their own.
        /// </summary>
        public static void WriteAsset(MapAsset map, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("An asset path is required.", nameof(assetPath));
            if (AssetDatabase.LoadAssetAtPath<MapAsset>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CreateAsset(map, assetPath);
            for (int i = 0; i < map.Roads.Length; i++)
            {
                if (map.Roads[i] != null)
                    AssetDatabase.AddObjectToAsset(map.Roads[i], map);
            }
            for (int i = 0; i < map.Edges.Length; i++)
            {
                if (map.Edges[i] != null)
                    AssetDatabase.AddObjectToAsset(map.Edges[i], map);
            }
            // Save only the map file (sub-assets included): SaveAssets would also flush dirty scenes.
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);
        }

        /// <summary>
        /// Sweeps every road into chunk meshes and stores them as sub-assets of the map, so a map is
        /// still one file to reference and its geometry travels with it. The road material is written
        /// onto the map as a project asset reference; the loader clones it, so a runtime tear-down can
        /// never destroy the asset.
        /// </summary>
        public static void WriteRoadMeshes(MapAsset map, BakedMapSettings settings)
        {
            if (map == null)
                return;

            RoadMeshSettings meshSettings = MeshSettingsFrom(settings);
            Material material = RoadSurfaceArt.EnsureMaterial();
            RoadSurfaceArt.ApplyTiling(material, meshSettings);
            map.RoadMaterial = material;

            RoadAsset[] roads = map.Roads ?? Array.Empty<RoadAsset>();
            for (int i = 0; i < roads.Length; i++)
            {
                RoadAsset road = roads[i];
                if (road == null)
                    continue;
                if (road.Path == null || !road.Path.IsUsable)
                {
                    road.Chunks = null;
                    continue;
                }

                RoadMeshChunk[] chunks = RoadMeshBuilder.Build(road.Path, meshSettings);
                for (int c = 0; c < chunks.Length; c++)
                {
                    Mesh mesh = chunks[c].Mesh;
                    if (mesh == null)
                        continue;
                    mesh.name = map.name + "_road" + i + "_chunk" + c;
                    AssetDatabase.AddObjectToAsset(mesh, map);
                }

                road.Chunks = chunks;
                EditorUtility.SetDirty(road);
            }

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);
        }

        /// <summary>
        /// The sweep resolution comes from the same authored steps the path bake used, so raising the
        /// authored corner resolution tightens the mesh instead of leaving the two out of step. The
        /// chunk length and tile size keep their defaults: they are presentation choices, not
        /// authored road shape.
        /// </summary>
        public static RoadMeshSettings MeshSettingsFrom(BakedMapSettings settings)
        {
            settings = settings.Sanitized();
            RoadMeshSettings mesh = RoadMeshSettings.Defaults();
            mesh.StraightSpacing = settings.StraightSampleStep;
            mesh.CornerSpacing = settings.CornerSampleStep;
            mesh.CornerCurvatureThreshold = settings.CornerCurvatureThreshold;
            return mesh.Sanitized();
        }

        /// <summary>
        /// Bakes one spline to a BakedPath. It samples the spline densely, then keeps rings at the
        /// straight or corner step the local curvature asks for, so a straight costs few vertices
        /// while a 25 m corner keeps its chord error under a few centimetres.
        /// </summary>
        public static BakedPath BakePath(SplineContainer container, Spline spline, BakedMapSettings settings)
        {
            int curveCount = spline.Closed ? spline.Count : spline.Count - 1;
            if (curveCount < 1)
                throw new ArgumentException("The spline needs at least one curve.", nameof(spline));

            var cumulative = new float[curveCount + 1];
            for (int i = 0; i < curveCount; i++)
                cumulative[i + 1] = cumulative[i] + spline.GetCurveLength(i);
            float length = cumulative[curveCount];
            if (length < 1f)
                throw new ArgumentException("The spline is shorter than one metre.", nameof(spline));

            float fineStep = Mathf.Max(0.25f, Mathf.Min(settings.StraightSampleStep, settings.CornerSampleStep));
            int denseCount = spline.Closed
                ? Mathf.Max(8, Mathf.RoundToInt(length / fineStep))
                : Mathf.Max(2, Mathf.RoundToInt(length / fineStep));
            int denseSamples = spline.Closed ? denseCount : denseCount + 1;

            var densePositions = new Vector3[denseSamples];
            var denseTangents = new Vector3[denseSamples];
            for (int i = 0; i < denseSamples; i++)
            {
                float distance = length * i / denseCount;
                SampleSpline(container, spline, cumulative, curveCount, distance,
                    out densePositions[i], out denseTangents[i]);
            }

            // Keep the first sample and the open end unconditionally; the ends define the lap.
            var indices = new List<int>(denseSamples);
            var density = new float[denseSamples];
            for (int i = 0; i < denseSamples; i++)
                density[i] = CurvatureMagnitude(densePositions, i, spline.Closed);

            float since = float.MaxValue;
            for (int i = 0; i < denseSamples; i++)
            {
                if (!spline.Closed && i == denseSamples - 1)
                {
                    if (indices.Count == 0 || indices[indices.Count - 1] != i)
                        indices.Add(i);
                    break;
                }
                float step = density[i] > settings.CornerCurvatureThreshold
                    ? settings.CornerSampleStep
                    : settings.StraightSampleStep;
                if (since >= step)
                {
                    indices.Add(i);
                    since = 0f;
                }
                since += fineStep;
            }
            if (indices.Count < 2)
                throw new ArgumentException("The spline collapsed to fewer than two rings.", nameof(spline));

            var positions = new List<Vector3>(indices.Count);
            var tangents = new List<Vector3>(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                positions.Add(densePositions[indices[i]]);
                tangents.Add(denseTangents[indices[i]]);
            }
            return FinishPath(positions, tangents, settings, spline.Closed);
        }

        private static void SampleSpline(SplineContainer container, Spline spline, float[] cumulative,
            int curveCount, float distance, out Vector3 position, out Vector3 tangent)
        {
            int curve = 0;
            while (curve < curveCount - 1 && cumulative[curve + 1] <= distance)
                curve++;
            float t = spline.GetCurveInterpolation(curve, distance - cumulative[curve]);
            BezierCurve bezier = spline.GetCurve(curve);
            float3 localPosition = CurveUtility.EvaluatePosition(bezier, t);
            float3 localTangent = CurveUtility.EvaluateTangent(bezier, t);
            Transform transform = container.transform;
            position = transform.TransformPoint((Vector3)localPosition);
            tangent = transform.TransformDirection((Vector3)localTangent).normalized;
        }

        /// <summary>Menger curvature over the dense ring, used only to choose the ring step.</summary>
        private static float CurvatureMagnitude(Vector3[] positions, int index, bool closed)
        {
            int count = positions.Length;
            int previous = closed ? (index - 1 + count) % count : Mathf.Max(index - 1, 0);
            int next = closed ? (index + 1) % count : Mathf.Min(index + 1, count - 1);
            Vector3 a = positions[index] - positions[previous];
            Vector3 b = positions[next] - positions[index];
            Vector3 c = positions[next] - positions[previous];
            float denominator = a.magnitude * b.magnitude * c.magnitude;
            return denominator > 1e-8f ? 2f * Vector3.Cross(a, b).magnitude / denominator : 0f;
        }

        private static BakedPath FinishPath(List<Vector3> positions, List<Vector3> tangents,
            BakedMapSettings settings, bool closed)
        {
            int count = positions.Count;
            var path = new BakedPath
            {
                Positions = new PathVector3[count],
                Tangents = new PathVector3[count],
                Ups = new PathVector3[count],
                WidthLeft = new float[count],
                WidthRight = new float[count],
                Banking = new float[count],
                Distances = new float[count],
                Closed = closed,
            };

            float halfWidth = settings.RoadWidth * 0.5f;
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                path.Positions[i] = PathConvert.ToPathVector(positions[i]);
                path.Tangents[i] = PathConvert.ToPathVector(tangents[i].normalized);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = halfWidth;
                path.WidthRight[i] = halfWidth;
                path.Banking[i] = settings.BankingDegrees;
                path.Distances[i] = total;
                if (i < count - 1)
                    total += Vector3.Distance(positions[i], positions[i + 1]);
                else if (closed)
                    total += Vector3.Distance(positions[count - 1], positions[0]);
            }
            path.TotalLength = total;

            // Surface spans are authored in arc length and resolved into one id per sample here, at bake
            // time. The runtime and the server only ever read the baked bytes, so the normalization
            // (clipping to the path, wrapping a closed loop, later spans winning) happens exactly once.
            path.ApplySurfaces(new SurfaceSpanSet(settings.SurfaceSpans, total, closed, SurfaceId.Street));
            path.ComputeCurvature();
            return path;
        }

        // ------------------------------------------------------------- edge baking

        /// <summary>
        /// Both barrier lines as overlapping boxes offset from the asphalt edge. Each segment spans
        /// its own arc step plus the overlap, so consecutive boxes interpenetrate and a car nose
        /// cannot catch a joint.
        /// </summary>
        private static EdgeAsset BakeEdgeAtRoadSide(BakedPath road, EdgeSide side, BakedMapSettings settings)
        {
            int segments = Mathf.Max(1, Mathf.RoundToInt(road.TotalLength / settings.EdgeSegmentLength));
            float step = road.TotalLength / segments;
            float halfWidth = settings.RoadWidth * 0.5f;
            float lateral = halfWidth + settings.EdgeOffset + settings.EdgeThickness * 0.5f;

            var edge = ScriptableObject.CreateInstance<EdgeAsset>();
            edge.name = "Edge_" + side.ToString().ToLowerInvariant();
            edge.EdgeId = side == EdgeSide.Left ? "left" : "right";
            edge.Side = side;
            edge.RoadIndex = 0;
            edge.Offset = settings.EdgeOffset;
            edge.Height = settings.EdgeHeight;
            edge.Thickness = settings.EdgeThickness;
            edge.SegmentLength = step;
            edge.Overlap = settings.EdgeOverlap;
            edge.Layer = settings.EdgeLayer;

            var baked = new BakedEdgeSegment[segments];
            float sign = side == EdgeSide.Left ? -1f : 1f;
            for (int i = 0; i < segments; i++)
            {
                float from = i * step;
                float to = from + step;
                road.Sample(from, out PathSample a);
                road.Sample(to, out PathSample b);
                Vector3 start = PathConvert.ToVector3(a.Position);
                Vector3 end = PathConvert.ToVector3(b.Position);
                Vector3 right = PathConvert.Right(a.Tangent);
                Vector3 center = (start + end) * 0.5f + right * (sign * lateral);
                center.y = Mathf.Max(start.y, end.y) + settings.EdgeHeight * 0.5f;
                baked[i] = new BakedEdgeSegment
                {
                    Center = PathConvert.ToPathVector(center),
                    Size = new PathVector3(settings.EdgeThickness, settings.EdgeHeight,
                        (end - start).magnitude + settings.EdgeOverlap),
                    YawDegrees = Yaw(start, end),
                };
            }
            edge.Segments = baked;
            return edge;
        }

        /// <summary>
        /// One road asset from one baked path. Index 0 keeps the "main" id the runtime routes from;
        /// alternates are numbered so a bake of N roads is reproducible from its settings alone.
        /// </summary>
        private static RoadAsset CreateRoad(BakedPath path, int index, BakedMapSettings settings)
        {
            var road = ScriptableObject.CreateInstance<RoadAsset>();
            road.name = index == 0 ? "Road_main" : "Road_" + index;
            road.RoadId = index == 0 ? "main" : "road_" + index;
            road.Path = path;
            road.Width = settings.RoadWidth;
            road.BankingDegrees = settings.BankingDegrees;
            road.StraightSampleStep = settings.StraightSampleStep;
            road.CornerSampleStep = settings.CornerSampleStep;
            road.CornerCurvatureThreshold = settings.CornerCurvatureThreshold;
            return road;
        }

        /// <summary>An extra spline baked as a free-standing barrier line at its own points.</summary>
        private static EdgeAsset BakeFreeEdge(SplineContainer container, Spline spline, int index,
            BakedMapSettings settings)
        {
            BakedPath path = BakePath(container, spline, settings);
            int segments = Mathf.Max(1, Mathf.RoundToInt(path.TotalLength / settings.EdgeSegmentLength));
            float step = path.TotalLength / segments;

            var edge = ScriptableObject.CreateInstance<EdgeAsset>();
            edge.name = "Edge_free_" + index;
            edge.EdgeId = "free_" + index;
            edge.Side = EdgeSide.Free;
            edge.RoadIndex = -1;
            edge.Offset = 0f;
            edge.Height = settings.EdgeHeight;
            edge.Thickness = settings.EdgeThickness;
            edge.SegmentLength = step;
            edge.Overlap = settings.EdgeOverlap;
            edge.Layer = settings.EdgeLayer;

            var baked = new BakedEdgeSegment[segments];
            for (int i = 0; i < segments; i++)
            {
                float from = i * step;
                float to = from + step;
                path.Sample(from, out PathSample a);
                path.Sample(to, out PathSample b);
                Vector3 start = PathConvert.ToVector3(a.Position);
                Vector3 end = PathConvert.ToVector3(b.Position);
                Vector3 center = (start + end) * 0.5f;
                center.y = Mathf.Max(start.y, end.y) + settings.EdgeHeight * 0.5f;
                baked[i] = new BakedEdgeSegment
                {
                    Center = PathConvert.ToPathVector(center),
                    Size = new PathVector3(settings.EdgeThickness, settings.EdgeHeight,
                        (end - start).magnitude + settings.EdgeOverlap),
                    YawDegrees = Yaw(start, end),
                };
            }
            edge.Segments = baked;
            return edge;
        }

        private static float Yaw(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static float[] EvenCheckpoints(float length, int count)
        {
            count = Mathf.Max(count, 1);
            var checkpoints = new float[count];
            for (int i = 0; i < count; i++)
                checkpoints[i] = length * i / count;
            return checkpoints;
        }
    }
}
