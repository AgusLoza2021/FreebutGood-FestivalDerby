using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.Drive.Tests
{
    public sealed class PauseMenuPlayModeTests
    {
        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            _host = new GameObject("PauseHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);
            Time.timeScale = 1f;
        }

        [Test]
        public void Opening_StopsTheGame_AndClosingResumesIt()
        {
            PauseMenu menu = PauseMenu.Build(_host.transform, "MainMenu");

            menu.Open();
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            menu.Close();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void Closing_RestoresTheTimeScaleThatWasRunning()
        {
            Time.timeScale = 0.5f;
            PauseMenu menu = PauseMenu.Build(_host.transform, "MainMenu");

            menu.Open();
            menu.Close();

            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
        }

        [Test]
        public void DestroyingAnOpenMenu_NeverLeavesTimeStopped()
        {
            PauseMenu menu = PauseMenu.Build(_host.transform, "MainMenu");
            menu.Open();

            Object.DestroyImmediate(_host);

            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void TheMenuStartsHidden_WithTheGameRunning()
        {
            PauseMenu menu = PauseMenu.Build(_host.transform, "MainMenu");

            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(menu.transform.Find("Overlay").gameObject.activeSelf, Is.False);
            Assert.That(menu.transform.Find("PauseButton"), Is.Not.Null);
        }

        [Test]
        public void TheMainMenuScene_IsInTheBuild_SoTheButtonCanReachIt()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded("MainMenu"), Is.True);
        }
    }
}
