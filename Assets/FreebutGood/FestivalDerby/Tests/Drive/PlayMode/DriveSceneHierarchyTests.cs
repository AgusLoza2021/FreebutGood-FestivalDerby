using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.Drive.Tests
{
    /// <summary>
    /// The Hierarchy is the project's table of contents: every config has its own object under Config,
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
            Transform config = drive.Find("Config");
            Assert.IsNotNull(config, "Drive must have a Config child.");

            AssertConfig(config, "Car", typeof(CarConfig));
            AssertConfig(config, "Controls", typeof(ControlsConfig));
            AssertConfig(config, "Camera", typeof(CameraConfig));
            AssertConfig(config, "Look", typeof(LookConfig));
            AssertConfig(config, "Track", typeof(TrackConfig));
            AssertConfig(config, "Bots", typeof(BotsConfig));

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
