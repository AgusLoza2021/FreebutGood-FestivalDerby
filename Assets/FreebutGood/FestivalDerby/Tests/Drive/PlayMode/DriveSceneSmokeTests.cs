using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// The one end-to-end check of the template: open the Drive scene, push the stick forward for a few
    /// seconds, and prove the car actually drove and the chase camera came along.
    /// </summary>
    public sealed class DriveSceneSmokeTests
    {
        private const string SceneName = "Drive";
        private const float BuildTimeoutSeconds = 10f;
        private const float SettleSeconds = 1f;
        private const float DriveSeconds = 4f;

        /// <summary>Stick held straight up the screen, as a thumb would.</summary>
        private sealed class StickForward : ISteerSource
        {
            public float Horizontal => 0f;
            public float Vertical => 1f;
            public bool HasInput => true;
        }

        private bool _previousRunInBackground;
        private int _previousTargetFrameRate;

        [SetUp]
        public void SetUp()
        {
            _previousRunInBackground = Application.runInBackground;
            _previousTargetFrameRate = Application.targetFrameRate;
            // A background Editor freezes the player loop; the car would never move.
            Application.runInBackground = true;
        }

        /// <summary>
        /// The Drive scene builds a whole circuit, a look and a car at the world origin. Left loaded, it
        /// would sit under every later PlayMode test's rig, so it is swapped for an empty scene here.
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene drive = SceneManager.GetSceneByName(SceneName);
            if (drive.IsValid() && drive.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("DriveSmokeEmpty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(drive);
            }

            Application.runInBackground = _previousRunInBackground;
            Application.targetFrameRate = _previousTargetFrameRate;
        }

        [UnityTest]
        public IEnumerator DriveScene_StickForward_CarMovesAndCameraFollows()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            Assert.AreEqual(SceneName, SceneManager.GetActiveScene().name,
                "The Drive scene must be in the build settings (Festival Derby/Build Drive Scene).");

            DriveBootstrap bootstrap = null;
            float waited = 0f;
            while (waited < BuildTimeoutSeconds)
            {
                bootstrap = Object.FindFirstObjectByType<DriveBootstrap>();
                if (bootstrap != null && bootstrap.Built)
                    break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsNotNull(bootstrap, "The Drive scene must carry a DriveBootstrap.");
            Assert.IsTrue(bootstrap.Built, "The bootstrap did not build within " + BuildTimeoutSeconds + " s.");
            Assert.IsNotNull(bootstrap.CarBody, "No car was spawned.");
            Assert.IsNotNull(bootstrap.Camera, "No camera was created.");

            // Let the car land on the grid before measuring.
            yield return new WaitForSeconds(SettleSeconds);

            Rigidbody car = bootstrap.CarBody;
            Transform camera = bootstrap.Camera.transform;
            Vector3 carStart = car.position;
            Vector3 cameraStart = camera.position;
            float framingDistance = Vector3.Distance(cameraStart, carStart);

            bootstrap.Controls.SetSteerSource(new StickForward());
            yield return new WaitForSeconds(DriveSeconds);

            float carTravel = Vector3.Distance(carStart, car.position);
            float cameraTravel = Vector3.Distance(cameraStart, camera.position);
            float cameraToCar = Vector3.Distance(camera.position, car.position);
            Debug.Log("[drive-smoke] car travel=" + carTravel.ToString("F2") + " m, camera travel=" +
                      cameraTravel.ToString("F2") + " m, camera-to-car=" + cameraToCar.ToString("F2") + " m");

            Assert.Greater(carTravel, 5f, "The car should cover several metres in " + DriveSeconds + " s.");
            Assert.Greater(cameraTravel, 3f, "The chase camera should move with the car.");
            // The look profile decides how far back the camera sits; following means keeping roughly
            // that framing while the car moves, not being left behind at the grid.
            Assert.Less(cameraToCar, framingDistance + 10f, "The chase camera should keep its framing.");
        }
    }
}
