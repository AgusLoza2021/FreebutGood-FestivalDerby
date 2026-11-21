using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// Turns the authored component into one bake. It is the only place that decides which of the
    /// baker's defaults an authoring kind overrides, so a LAP and a DERBY arena differ in one readable
    /// table instead of in scattered arguments.
    /// </summary>
    public static class DerbyMapBake
    {
        public static BakedMapSettings SettingsFrom(DerbyMapAuthoring authoring)
        {
            BakedMapSettings settings = BakedMapSettings.OvalDefaults();
            if (authoring == null)
            {
                return settings.Sanitized();
            }

            settings.MapId = string.IsNullOrEmpty(authoring.MapId) ? authoring.name : authoring.MapId;
            settings.RoadWidth = authoring.RoadWidth;
            settings.RoadCount = authoring.RoadCount;
            settings.SurfaceSpans = authoring.SurfaceSpans;

            if (authoring.Kind == DerbyMapAuthoring.MapKind.Derby)
            {
                // A derby arena is short and wide: a denser grid, closer to the line, and only the
                // start and mid-lap checkpoints a short race needs.
                settings.SpawnColumns = 4;
                settings.SpawnRows = 3;
                settings.SpawnSpacing = 4f;
                settings.SpawnBackOffset = 5f;
                settings.RouteStep = 6f;
                settings.CheckpointCount = 2;
                settings.CameraOffset = new Vector3(-12f, 18f, -10f);
            }

            return settings.Sanitized();
        }

        /// <summary>
        /// Length of the path this authoring would bake, without writing anything. The validator needs a
        /// real arc length to resolve surface spans, and a degenerate spline is a validation result
        /// rather than an exception an overlay button should throw.
        /// </summary>
        public static float MeasureLength(SplineContainer container, DerbyMapAuthoring authoring)
        {
            if (container == null || container.Splines.Count == 0)
            {
                return 0f;
            }

            try
            {
                BakedPath path = MapBaker.BakePath(container, container.Splines[0], SettingsFrom(authoring));
                return path != null ? path.TotalLength : 0f;
            }
            catch (ArgumentException)
            {
                return 0f;
            }
        }

        /// <summary>
        /// Bakes and writes the target asset, returning a line the overlay can show. The target
        /// reference is re-resolved afterwards because WriteAsset deletes and recreates the file, which
        /// would otherwise leave the component pointing at a destroyed instance after the first bake.
        /// </summary>
        public static string BakeAndWrite(DerbyMapAuthoring authoring)
        {
            if (authoring == null)
            {
                return "Select an authoring object first.";
            }

            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            if (container == null || container.Splines.Count == 0)
            {
                return "No spline to bake.";
            }

            if (authoring.Target == null)
            {
                return "Pick a bake target MapAsset first.";
            }

            string path = AssetDatabase.GetAssetPath(authoring.Target);
            if (string.IsNullOrEmpty(path))
            {
                return "The bake target is not a saved asset.";
            }

            authoring.Sanitize();
            BakedMapSettings settings = SettingsFrom(authoring);
            MapAsset map;
            try
            {
                map = MapBaker.Bake(container, settings);
            }
            catch (ArgumentException error)
            {
                return "Bake refused the spline: " + error.Message;
            }

            // A bake is one user action. The asset file itself is rewritten wholesale, which Unity
            // cannot undo, but the authoring object's reference to it is restored by a single Undo, so
            // the Bake button and Ctrl+Z behave as a pair instead of leaving a dangling target.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Bake Derby map");

            MapBaker.WriteAsset(map, path);
            var written = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
            if (written != null)
            {
                MapBaker.WriteRoadMeshes(written, settings);
                Undo.RecordObject(authoring, "Bake Derby map");
                authoring.Target = written;
                EditorUtility.SetDirty(authoring);
            }

            return Describe(written, path);
        }

        private static string Describe(MapAsset map, string path)
        {
            if (map == null || map.PrimaryRoad == null || map.PrimaryRoad.Path == null)
            {
                return "Baked " + path + ", but the result is empty.";
            }

            BakedPath road = map.PrimaryRoad.Path;
            string surfaces = road.SurfacePrimary != null && road.SurfacePrimary.Length > 0
                ? road.SurfacePrimary.Length + " surface samples"
                : "no surface samples";
            string chunks = map.PrimaryRoad.Chunks != null && map.PrimaryRoad.Chunks.Length > 0
                ? map.PrimaryRoad.Chunks.Length + " mesh chunks"
                : "no mesh chunks";
            return "Baked " + road.TotalLength.ToString("0.0") + " m, " + road.Count + " rings, "
                + surfaces + ", " + chunks;
        }
    }
}
