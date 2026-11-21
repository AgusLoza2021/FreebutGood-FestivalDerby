using System.Collections;
using FestivalDerby.Bots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// The AI racers end to end: the Bots config decides how many cars line up, never more than twelve,
    /// and every one of them drives off along the racing line on its own.
    /// </summary>
    public sealed class DriveBotsPlayModeTests
    {
        private const float RaceSeconds = 8f;
        private const float MinTravelMeters = 15f;

        private DriveSceneHarness _scene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene != null)
                yield return _scene.Unload();
        }

        [UnityTest]
        public IEnumerator DriveScene_ThreeBots_SpawnThreeCarsThatRaceAlongTheTrack()
        {
            yield return LoadAndRace(3, 3);
        }

        [UnityTest]
        public IEnumerator DriveScene_NinetyNineBotsRequested_SpawnsTheTwelveCarCap()
        {
            yield return LoadAndRace(99, BotField.MaxBots);
            Assert.AreEqual(BotField.MaxBots, _scene.BotsConfig.Count, "The Bots config must clamp its count to 12.");
        }

        [Test]
        public void BotField_ClampsAnyRequestToTwelve()
        {
            Assert.AreEqual(12, BotField.MaxBots);
            Assert.AreEqual(12, BotField.Clamp(99));
            Assert.AreEqual(0, BotField.Clamp(-3));
            Assert.AreEqual(0, BotField.GridSlotsFor(0), "No bots must leave the map's authored grid untouched.");
        }

        private IEnumerator LoadAndRace(int requested, int expected)
        {
            _scene = new DriveSceneHarness(requested);
            yield return _scene.Load();
            DriveBootstrap bootstrap = _scene.Bootstrap;

            Assert.AreEqual(expected, bootstrap.BotCount, "Requested " + requested + " bots.");
            var starts = new Vector3[bootstrap.BotCount];
            for (int i = 0; i < starts.Length; i++)
            {
                Assert.IsNotNull(bootstrap.GetBotBody(i), "Bot " + i + " has no car.");
                Assert.AreNotSame(bootstrap.CarBody, bootstrap.GetBotBody(i), "A bot must be its own car.");
                starts[i] = bootstrap.GetBotBody(i).position;
            }

            // Nobody touches the player's controls: every metre the bots cover is their own driving.
            yield return new WaitForSeconds(RaceSeconds);

            for (int i = 0; i < starts.Length; i++)
            {
                float travelled = bootstrap.GetBotTravelledMeters(i);
                float moved = Vector3.Distance(starts[i], bootstrap.GetBotBody(i).position);
                Debug.Log("[drive-bots] bot " + i + " line=" + travelled.ToString("F1") + " m, moved=" +
                          moved.ToString("F1") + " m");
                Assert.Greater(travelled, MinTravelMeters,
                    "Bot " + i + " should have driven along the racing line in " + RaceSeconds + " s.");
            }
        }
    }
}
