using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the mask driver's LIFECYCLE, not for its pixels: it arms only with a stamp material,
    /// it displaces rather than duplicates the active driver, it refuses stamps outside the window, it
    /// stops touching the mask while paused, and it rebuilds the texture (cleared to white) if the platform
    /// took it away — which is what an Android background/resume does.
    ///
    /// Honestly labelled: these run in the EDITOR only, they need a graphics device (a
    /// <c>-nographics</c> batch run cannot create a RenderTexture), and nothing here reads back a texel.
    /// Whether the ruts actually look like ruts is a Play Mode capture question, answered by
    /// Documentation/Design/SNOW_RUTS.md's checklist, not by an assertion.
    /// </summary>
    [TestFixture]
    public sealed class GroundMaskDriverEditModeTests
    {
        private const string StampShaderName = "FestivalDerby/GroundMaskStamp";
        private const string UpdateShaderName = "Hidden/FestivalDerby/GroundMaskUpdate";

        private GameObject _host;
        private GameObject _follow;
        private Material _stamp;
        private Material _update;

        [SetUp]
        public void SetUp()
        {
            Shader stampShader = Shader.Find(StampShaderName);
            Shader updateShader = Shader.Find(UpdateShaderName);
            Assert.That(stampShader, Is.Not.Null, "Missing shader " + StampShaderName);
            Assert.That(updateShader, Is.Not.Null, "Missing shader " + UpdateShaderName);

            // Instancing on, as SnowRutSetup authors it: the driver draws every stamp with one
            // DrawMeshInstanced and refuses a stamp material without it.
            _stamp = new Material(stampShader) { enableInstancing = true };
            _update = new Material(updateShader);

            _host = new GameObject("GroundMaskDriverTests");
            _follow = new GameObject("Follow");
            _follow.transform.position = new Vector3(12f, 0f, -30f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_follow);
            Object.DestroyImmediate(_stamp);
            Object.DestroyImmediate(_update);
        }

        private GroundMaskDriver CreateDriver()
        {
            var host = new GameObject("Mask");
            host.transform.SetParent(_host.transform, false);
            return host.AddComponent<GroundMaskDriver>();
        }

        [Test]
        public void Configure_WithoutStampMaterial_DoesNotArm()
        {
            GroundMaskDriver driver = CreateDriver();
            // The refusal is loud on purpose: a half-authored setup must say what to run.
            LogAssert.Expect(LogType.Error, new Regex("No stamp material"));
            driver.Configure(_follow.transform, 72f, 1024, 8f, null, _update);

            Assert.That(driver.IsReady, Is.False);
            Assert.That(driver.MaskTexture, Is.Null, "no texture may be allocated for a driver that cannot draw");
            Assert.That(driver.enabled, Is.False);
        }

        [Test]
        public void Configure_WithAStampMaterialWithoutInstancing_DoesNotArm()
        {
            GroundMaskDriver driver = CreateDriver();
            var plain = new Material(_stamp) { enableInstancing = false };
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("does not have GPU instancing"));
                driver.Configure(_follow.transform, 72f, 1024, 8f, plain, _update);

                Assert.That(driver.IsReady, Is.False);
                Assert.That(driver.MaskTexture, Is.Null);
                Assert.That(driver.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void Configure_ArmsAndClampsTheWindow()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 500f, 700, 8f, _stamp, _update);

            Assert.That(driver.IsReady, Is.True);
            Assert.That(GroundMaskDriver.Active, Is.SameAs(driver));
            // Hostile authored numbers are clamped by the window itself, not by the driver.
            Assert.That(driver.Window.AreaSize, Is.EqualTo(RutWindow.MaxAreaSize));
            Assert.That(driver.Window.Resolution, Is.EqualTo(RutWindow.MaxResolution));
            Assert.That(driver.MaskTexture.width, Is.EqualTo(RutWindow.MaxResolution));
        }

        [Test]
        public void Configure_SecondDriverDisplacesTheFirst()
        {
            GroundMaskDriver first = CreateDriver();
            first.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);

            GroundMaskDriver second = CreateDriver();
            second.Configure(_follow.transform, 48f, 512, 8f, _stamp, _update);

            Assert.That(GroundMaskDriver.Active, Is.SameAs(second));
            Assert.That(first.enabled, Is.False, "the displaced driver must stop, not keep writing the same mask");
            Assert.That(second.enabled, Is.True);
        }

        [Test]
        public void Submit_RefusesPointsOutsideTheWindow()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);

            RutWindow window = driver.Window;
            Assert.That(driver.Submit(window.OriginX - 5f, window.OriginZ + 10f, 0.45f, 1f, 0f, 0.55f), Is.False);
            Assert.That(driver.Submit(window.OriginX + 10f, window.OriginZ + 10f, 0.45f, 1f, 0f, 0.55f), Is.True);
        }

        [Test]
        public void Submit_OverflowIsDroppedAndCounted_FlushRecovers()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);

            RutWindow window = driver.Window;
            float x = window.OriginX + window.AreaSize * 0.5f;
            float z = window.OriginZ + window.AreaSize * 0.5f;

            for (int i = 0; i < RutStampQueue.Capacity + 4; i++)
                driver.Submit(x, z, 0.45f, 1f, 0f, 0.55f);

            Assert.That(driver.DroppedStamps, Is.EqualTo(4));

            driver.FlushStamps();
            Assert.That(driver.Submit(x, z, 0.45f, 1f, 0f, 0.55f), Is.True, "the queue must be empty after a flush");
        }

        [Test]
        public void Flush_WhilePaused_DrawsNothing()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);

            RutWindow window = driver.Window;
            float x = window.OriginX + window.AreaSize * 0.5f;
            float z = window.OriginZ + window.AreaSize * 0.5f;

            driver.Pause();
            Assert.That(driver.IsPaused, Is.True);

            driver.Submit(x, z, 0.45f, 1f, 0f, 0.55f);
            Assert.DoesNotThrow(() => driver.FlushStamps());

            // Pausing drops what was queued: a rut is worth less than a frame drawn into a texture the
            // platform may have taken away.
            Assert.That(driver.Submit(x, z, 0.45f, 1f, 0f, 0.55f), Is.True);

            driver.Resume();
            Assert.That(driver.IsPaused, Is.False);
        }

        [Test]
        public void Resume_RecreatesAMaskThePlatformTookAway()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);

            driver.MaskTexture.Release();
            Assert.That(driver.MaskTexture.IsCreated(), Is.False);

            driver.Resume();
            // The per-frame path is the one that calls EnsureMaskAlive. An EditMode test has no player
            // loop and SendMessage asserts outside play mode, so it calls Tick, which LateUpdate runs.
            driver.Tick();

            Assert.That(driver.MaskTexture.IsCreated(), Is.True, "the mask must come back cleared to white");
            Assert.That(driver.IsReady, Is.True);
        }

        [Test]
        public void Destroy_ClearsTheActiveSlot()
        {
            GroundMaskDriver driver = CreateDriver();
            driver.Configure(_follow.transform, 72f, 1024, 8f, _stamp, _update);
            Assert.That(GroundMaskDriver.Active, Is.SameAs(driver));

            Object.DestroyImmediate(driver.gameObject);

            // Unity's null, not NUnit's: in edit mode a component without [ExecuteAlways] gets no
            // OnDestroy, so the static still holds the destroyed object. What matters to the next
            // Configure (and to the runtime) is that the slot no longer resolves to a live driver.
            Assert.That(GroundMaskDriver.Active == null, Is.True);
        }
    }
}
