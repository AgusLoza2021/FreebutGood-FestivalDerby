using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The PF.2 ghost's rendering and collider contract, on a minimal rig: ghost-on disables every
    /// bound collider and swaps each renderer to a transparent clone at the configured alpha, and
    /// ghost-off gives back the exact original sharedMaterial references and the pre-ghost property
    /// block (the garage paint lives there, so "cleared" would be a visible regression). Idempotent
    /// in both directions, and tolerant of a car with no renderers or no colliders.
    /// </summary>
    public sealed class CarGhostPlayModeTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Stuck/ghost windows for the forced-respawn tests, in the same deterministic rhythm the
        // monitor's own fixture uses (0.25 divides both windows exactly).
        private const float Dt = 0.25f;
        private const float StuckSeconds = 1f;
        private const float StuckSpeedLimit = 0.5f;
        private const float GhostSeconds = 2f;
        private const int GhostTicks = 8;
        private const float GhostAlpha = 0.45f;

        private GameObject _body;
        private GameObject _visualRoot;

        [TearDown]
        public void TearDown()
        {
            if (_body != null)
                Object.Destroy(_body);
        }

        /// <summary>Body with one chassis collider and one rendered panel under a visual root.</summary>
        private BoxCollider CreateRig(out Renderer panel, out Material original)
        {
            _body = new GameObject("Body");
            BoxCollider chassis = _body.AddComponent<BoxCollider>();

            _visualRoot = new GameObject("Visual");
            _visualRoot.transform.SetParent(_body.transform, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Panel";
            // Decorative like the fitted FBX clone: the only collider under test is the chassis.
            Object.Destroy(cube.GetComponent<BoxCollider>());
            cube.transform.SetParent(_visualRoot.transform, false);
            panel = cube.GetComponent<Renderer>();
            original = panel.sharedMaterial;
            return chassis;
        }

        [Test]
        public void GhostOn_DisablesCollider_AndSwapsToTransparentCloneAtAlpha()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out Material original);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, new Collider[] { chassis });

            ghost.SetGhost(true, 0.45f);

            Assert.That(chassis.enabled, Is.False, "the ghost window must take the chassis collider off");
            Assert.That(panel.sharedMaterial, Is.Not.SameAs(original),
                "the ghost must swap in a clone, not mutate the shared material");
            Assert.That(panel.sharedMaterial.renderQueue, Is.EqualTo(3000),
                "the clone must sit on the Transparent queue or the alpha cannot blend");
            AssertGhostAlpha(panel, 0.45f);
        }

        [Test]
        public void GhostOff_RestoresOriginalMaterials_Collider_AndPropertyBlock()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out Material original);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, new Collider[] { chassis });
            ghost.SetGhost(true, 0.45f);

            ghost.SetGhost(false, 0.45f);

            Assert.That(chassis.enabled, Is.True);
            Assert.That(panel.sharedMaterial, Is.SameAs(original),
                "ghost-off must restore the exact original material references, not look-alikes");
            var block = new MaterialPropertyBlock();
            panel.GetPropertyBlock(block);
            Assert.That(block.GetColor(BaseColorId).a, Is.EqualTo(0f),
                "the ghost's tint must be removed entirely when no block was set before");
        }

        [Test]
        public void GhostOn_RestoresAPreExistingPaintBlock()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out _);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, new Collider[] { chassis });

            // The garage paint, applied the way CarPaintApplier applies it.
            var paint = new Color(0.9f, 0.2f, 0.1f, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, paint);
            panel.SetPropertyBlock(block);

            ghost.SetGhost(true, 0.45f);
            AssertGhostAlpha(panel, 0.45f);

            ghost.SetGhost(false, 0.45f);
            var restored = new MaterialPropertyBlock();
            panel.GetPropertyBlock(restored);
            Color afterGhost = restored.GetColor(BaseColorId);
            Assert.That(afterGhost.a, Is.EqualTo(1f), "the paint must survive the ghost window");
            Assert.That(afterGhost.r, Is.EqualTo(paint.r).Within(0.001f));
            Assert.That(afterGhost.g, Is.EqualTo(paint.g).Within(0.001f));
            Assert.That(afterGhost.b, Is.EqualTo(paint.b).Within(0.001f));
        }

        [Test]
        public void DoubleGhostOn_DoesNotLoseTheOriginals()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out Material original);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, new Collider[] { chassis });

            ghost.SetGhost(true, 0.45f);
            ghost.SetGhost(true, 0.45f);

            Assert.That(panel.sharedMaterial, Is.Not.SameAs(original));
            ghost.SetGhost(false, 0.45f);
            Assert.That(panel.sharedMaterial, Is.SameAs(original),
                "a repeated ghost-on must not double-swap and strand the first clone");
        }

        [Test]
        public void GhostOff_WhenAlreadyOff_IsANoOp()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out Material original);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, new Collider[] { chassis });

            ghost.SetGhost(false, 0.45f);

            Assert.That(chassis.enabled, Is.True);
            Assert.That(panel.sharedMaterial, Is.SameAs(original));
        }

        [Test]
        public void CarWithNoRenderers_StillGhostsItsCollider()
        {
            _body = new GameObject("Body");
            BoxCollider chassis = _body.AddComponent<BoxCollider>();
            var ghost = new CarGhost();
            ghost.Bind(null, new Collider[] { chassis });

            ghost.SetGhost(true, 0.45f);
            Assert.That(chassis.enabled, Is.False);
            ghost.SetGhost(false, 0.45f);
            Assert.That(chassis.enabled, Is.True);
        }

        [Test]
        public void CarWithNoColliders_StillGhostsItsRenderers()
        {
            CreateRig(out Renderer panel, out Material original);
            var ghost = new CarGhost();
            ghost.Bind(_visualRoot.transform, null);

            ghost.SetGhost(true, 0.45f);
            Assert.That(panel.sharedMaterial, Is.Not.SameAs(original));
            ghost.SetGhost(false, 0.45f);
            Assert.That(panel.sharedMaterial, Is.SameAs(original));
        }

        [Test]
        public void ForcedRespawn_GhostsTheCar_AtTheMonitorLevel_AndFiresOnce()
        {
            BoxCollider chassis = CreateRig(out Renderer panel, out Material original);
            var monitor = new CarGhostMonitor(StuckSeconds, StuckSpeedLimit, GhostSeconds, GhostAlpha);
            monitor.Bind(_visualRoot.transform, new Collider[] { chassis });

            // The forced request must take the same path as the clock latch: ghost on, clock disarmed,
            // and one true so the bootstrap teleports this frame.
            Assert.That(monitor.RequestForcedRespawn(), Is.True,
                "the forced respawn must fire on the first request");
            Assert.That(monitor.IsGhosted, Is.True,
                "the forced respawn must leave the car inside the ghost window");
            Assert.That(chassis.enabled, Is.False, "the forced ghost must take the chassis collider off");
            Assert.That(panel.sharedMaterial, Is.Not.SameAs(original),
                "the forced ghost must swap to the transparent clone like the automatic one");
            Assert.That(monitor.IsArmed, Is.False,
                "the forced path must disarm the clock for the window exactly as the latch does");

            // A second immediate call lands inside the window: it must not fire again.
            Assert.That(monitor.RequestForcedRespawn(), Is.False,
                "a forced respawn must not fire twice inside the same ghost window");

            // And once the window closes on its own, the monitor must rearm for the next escape.
            for (int i = 0; i < GhostTicks; i++)
            {
                monitor.Tick(Dt, 0f, 1f);
            }

            Assert.That(monitor.IsGhosted, Is.False);
            Assert.That(monitor.IsArmed, Is.True, "a closed window must hand the clock back armed");
        }

        private static void AssertGhostAlpha(Renderer renderer, float expectedAlpha)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(BaseColorId).a, Is.EqualTo(expectedAlpha).Within(0.001f),
                "the ghost alpha must ride the property block, leaving the clone's own colour intact");
        }
    }
}
