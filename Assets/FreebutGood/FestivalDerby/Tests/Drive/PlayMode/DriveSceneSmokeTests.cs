using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// The one end-to-end check of the template: open the Drive scene with no bots, push the stick forward
    /// for a few seconds, and prove the car actually drove and the chase camera came along.
    /// </summary>
    public sealed class DriveSceneSmokeTests
    {
        private const float SettleSeconds = 1f;
        private const float DriveSeconds = 4f;

        /// <summary>Stick held straight up the screen, as a thumb would.</summary>
        private sealed class StickForward : ISteerSource
        {
            public float Horizontal => 0f;
            public float Vertical => 1f;
            public bool HasInput => true;
        }

        private DriveSceneHarness _scene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene != null)
                yield return _scene.Unload();
        }

        /// <summary>With zero bots the scene must behave exactly as the solo template did before bots existed.</summary>
        [UnityTest]
        public IEnumerator DriveScene_NoBots_StickForward_CarMovesAndCameraFollows()
        {
            _scene = new DriveSceneHarness(0);
            yield return _scene.Load();
            DriveBootstrap bootstrap = _scene.Bootstrap;

            Assert.AreEqual(0, bootstrap.BotCount, "Bots count 0 must spawn no AI car.");
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
