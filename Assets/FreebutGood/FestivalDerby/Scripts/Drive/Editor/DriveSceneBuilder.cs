using FestivalDerby.Driving.Runtime;
using FestivalDerby.Look;
using FestivalDerby.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FestivalDerby.Drive.Editor
{
    /// <summary>
    /// Writes the one scene the project needs. Idempotent: running it again rewrites the same scene, so the
    /// scene never drifts from the code. The Hierarchy it writes is the project's table of contents:
    /// <code>
    /// Drive            DriveBootstrap: builds the game, holds no tuning
    ///   Config
    ///     Car          CarConfig       -> DrivingFeatureConfig asset
    ///     Controls     ControlsConfig
    ///     Camera       CameraConfig    -> LookProfile camera group
    ///     Look         LookConfig      -> LookProfile asset
    ///     Track        TrackConfig     -> MapAsset + TracksideKit assets
    ///     Bots         BotsConfig
    ///     Match        MatchConfig     (win/lose rules; free drive by default)
    ///   Runtime        empty: track, cars, camera and touch controls are spawned here on Play
    /// </code>
    /// From the command line (no Editor window):
    /// Unity -batchmode -nographics -quit -projectPath . -executeMethod FestivalDerby.Drive.Editor.DriveSceneBuilder.Build
    /// </summary>
    public static class DriveSceneBuilder
    {
        public const string ScenePath = "Assets/FreebutGood/FestivalDerby/Scenes/Drive.unity";
        private const string DrivingPath = "Assets/FreebutGood/FestivalDerby/Resources/DrivingFeatureConfig.asset";
        private const string TracksidePath = "Assets/FreebutGood/FestivalDerby/Resources/World/TracksideKit.asset";

        [MenuItem("Festival Derby/Build Drive Scene")]
        public static void Build()
        {
            var driving = AssetDatabase.LoadAssetAtPath<DrivingFeatureConfig>(DrivingPath);
            var trackside = AssetDatabase.LoadAssetAtPath<TracksideKit>(TracksidePath);
            if (driving == null)
                throw new System.InvalidOperationException("Missing " + DrivingPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Drive");
            var bootstrap = root.AddComponent<DriveBootstrap>();
            Transform config = Child(root.transform, "Config");

            var car = Child(config, "Car").gameObject.AddComponent<CarConfig>();
            car.Driving = driving;
            var controls = Child(config, "Controls").gameObject.AddComponent<ControlsConfig>();
            var camera = Child(config, "Camera").gameObject.AddComponent<CameraConfig>();
            var look = Child(config, "Look").gameObject.AddComponent<LookConfig>();
            // The shared asset already names the map and look the game ships with; the config objects
            // point at those same assets so the scene and the asset never disagree on day one.
            look.Profile = driving.Look as LookProfile;
            var track = Child(config, "Track").gameObject.AddComponent<TrackConfig>();
            track.Map = driving.Map as MapAsset;
            track.Trackside = trackside;
            var bots = Child(config, "Bots").gameObject.AddComponent<BotsConfig>();
            var match = Child(config, "Match").gameObject.AddComponent<MatchConfig>();
            Transform runtime = Child(root.transform, "Runtime");

            var wiring = new SerializedObject(bootstrap);
            wiring.FindProperty("_carConfig").objectReferenceValue = car;
            wiring.FindProperty("_controlsConfig").objectReferenceValue = controls;
            wiring.FindProperty("_cameraConfig").objectReferenceValue = camera;
            wiring.FindProperty("_lookConfig").objectReferenceValue = look;
            wiring.FindProperty("_trackConfig").objectReferenceValue = track;
            wiring.FindProperty("_botsConfig").objectReferenceValue = bots;
            wiring.FindProperty("_matchConfig").objectReferenceValue = match;
            wiring.FindProperty("_runtime").objectReferenceValue = runtime;
            wiring.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException("Could not save " + ScenePath);

            // The only scene in the build, so Play from any scene and a device build both land here.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[drive] wrote " + ScenePath);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
    }
}
