using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// Opens the real Drive scene with a chosen number of bots and waits for the bootstrap to build. The
    /// bot count is written from <see cref="SceneManager.sceneLoaded"/>, which runs after Awake and before
    /// Start, so the bootstrap builds with it exactly as if it had been typed in the Inspector.
    /// </summary>
    internal sealed class DriveSceneHarness
    {
        public const string SceneName = "Gameplay";
        private const float BuildTimeoutSeconds = 10f;

        private readonly int _botCount;
        private bool _previousRunInBackground;
        private int _previousTargetFrameRate;

        public DriveSceneHarness(int botCount)
        {
            _botCount = botCount;
        }

        public DriveBootstrap Bootstrap { get; private set; }

        /// <summary>The Bots config as the run saw it, so a test can read the count it was clamped to.</summary>
        public BotsConfig BotsConfig { get; private set; }

        public IEnumerator Load()
        {
            _previousRunInBackground = Application.runInBackground;
            _previousTargetFrameRate = Application.targetFrameRate;
            // A background Editor freezes the player loop; nothing would move.
            Application.runInBackground = true;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(SceneName);
            yield return null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Assert.AreEqual(SceneName, SceneManager.GetActiveScene().name,
                "The Drive scene must be in the build settings (Festival Derby/Build Drive Scene).");

            float waited = 0f;
            while (waited < BuildTimeoutSeconds)
            {
                Bootstrap = Object.FindFirstObjectByType<DriveBootstrap>();
                if (Bootstrap != null && Bootstrap.Built)
                    break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsNotNull(Bootstrap, "The Drive scene must carry a DriveBootstrap.");
            Assert.IsTrue(Bootstrap.Built, "The bootstrap did not build within " + BuildTimeoutSeconds + " s.");
        }

        /// <summary>
        /// The Drive scene builds a whole circuit and its cars at the world origin. Left loaded, it would sit
        /// under every later PlayMode test's rig, so it is swapped for an empty scene here.
        /// </summary>
        public IEnumerator Unload()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Scene drive = SceneManager.GetSceneByName(SceneName);
            if (drive.IsValid() && drive.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("DriveTestEmpty" + Time.frameCount);
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(drive);
            }

            Application.runInBackground = _previousRunInBackground;
            Application.targetFrameRate = _previousTargetFrameRate;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName)
                return;

            BotsConfig = Object.FindFirstObjectByType<BotsConfig>();
            if (BotsConfig != null)
                BotsConfig.Count = _botCount;
        }
    }
}
