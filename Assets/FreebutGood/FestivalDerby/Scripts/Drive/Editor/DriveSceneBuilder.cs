using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FestivalDerby.Drive.Editor
{
    /// <summary>
    /// Writes the one scene the project needs: an empty world with a single "Drive" object carrying the
    /// <see cref="DriveBootstrap"/>. Everything else is built at runtime, so the scene never drifts from the
    /// code. Idempotent: running it again rewrites the same scene.
    ///
    /// From the command line (no Editor window):
    /// Unity -batchmode -nographics -quit -projectPath . -executeMethod FestivalDerby.Drive.Editor.DriveSceneBuilder.Build
    /// </summary>
    public static class DriveSceneBuilder
    {
        public const string ScenePath = "Assets/FreebutGood/FestivalDerby/Scenes/Drive.unity";
        private const string ConfigPath = "Assets/FreebutGood/FestivalDerby/Resources/DrivingFeatureConfig.asset";
        private const string TracksidePath = "Assets/FreebutGood/FestivalDerby/Resources/World/TracksideKit.asset";

        [MenuItem("Festival Derby/Build Drive Scene")]
        public static void Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<DrivingFeatureConfig>(ConfigPath);
            var trackside = AssetDatabase.LoadAssetAtPath<TracksideKit>(TracksidePath);
            if (config == null)
                throw new System.InvalidOperationException("Missing " + ConfigPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Drive");
            var bootstrap = root.AddComponent<DriveBootstrap>();
            bootstrap.Config = config;
            bootstrap.Trackside = trackside;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException("Could not save " + ScenePath);

            // The only scene in the build, so Play from any scene and a device build both land here.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[drive] wrote " + ScenePath);
        }
    }
}
