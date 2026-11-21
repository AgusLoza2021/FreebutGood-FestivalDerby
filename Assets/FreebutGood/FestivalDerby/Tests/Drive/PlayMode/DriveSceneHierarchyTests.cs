using System;
using System.Collections;
using FestivalDerby.Drive;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// The Hierarchy is the project's table of contents: every config has its own object under Config_GRP,
    /// carrying exactly the component that owns that concern, and spawned objects go under Runtime.
    /// </summary>
    public sealed class DriveSceneHierarchyTests
    {
        private DriveSceneHarness _scene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene != null)
                yield return _scene.Unload();
        }

        [UnityTest]
        public IEnumerator DriveScene_HasOneConfigObjectPerConcern()
        {
            _scene = new DriveSceneHarness(0);
            yield return _scene.Load();
            Transform drive = _scene.Bootstrap.transform;

            Assert.AreEqual("Drive", drive.name);
            Transform config = drive.Find("Config_GRP");
            Assert.IsNotNull(config, "Drive must have a Config_GRP child.");

            AssertConfig(config, "CarConfig", typeof(CarConfig));
            AssertConfig(config, "ControlsConfig", typeof(ControlsConfig));
            AssertConfig(config, "CameraConfig", typeof(CameraConfig));
            AssertConfig(config, "LookConfig", typeof(LookConfig));
            AssertConfig(config, "TrackConfig", typeof(TrackConfig));
            AssertConfig(config, "BotsConfig", typeof(BotsConfig));
            AssertConfig(drive, "GamemodeConfig", typeof(MatchConfig));

            Transform runtime = drive.Find("Runtime");
            Assert.IsNotNull(runtime, "Drive must have a Runtime child.");
            Assert.AreSame(runtime, _scene.Bootstrap.RuntimeRoot, "Spawned objects must go under Runtime.");
            Assert.IsTrue(_scene.Bootstrap.CarBody.transform.IsChildOf(runtime),
                "The player's car must be spawned under Runtime.");
        }

        private static void AssertConfig(Transform config, string name, Type component)
        {
            Transform child = config.Find(name);
            Assert.IsNotNull(child, "Config must have a '" + name + "' child.");
            Assert.IsNotNull(child.GetComponent(component), "'" + name + "' must carry a " + component.Name + ".");
        }
    }
}
