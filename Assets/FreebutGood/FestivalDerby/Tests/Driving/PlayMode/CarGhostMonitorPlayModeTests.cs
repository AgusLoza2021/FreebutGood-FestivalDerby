using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The PF.2 monitor's lifecycle on a minimal rig: exactly one true after the full stuck window
    /// of throttle-demanded immobility, a ghost window that starts on that true and ends by itself
    /// after ghostSeconds (colliders back, exact materials back), and a countdown that real movement
    /// or a released pedal restarts. Deterministic quarter-second ticks keep every boundary exact.
    /// </summary>
    public sealed class CarGhostMonitorPlayModeTests
    {
        // 0.25 divides both windows exactly and is exact in binary floating point.
        private const float Dt = 0.25f;
        private const float StuckSeconds = 1f;
        private const int StuckTicks = 4;
        private const float GhostSeconds = 2f;
        private const int GhostTicks = 8;
        private const float GhostAlpha = 0.45f;

        private GameObject _body;
        private GameObject _visualRoot;
        private OwnedCarGeometry _geometry;
        private GameObject _dropped;

        [TearDown]
        public void TearDown()
        {
            if (_body != null)
                Object.Destroy(_body);
            if (_geometry != null)
                _geometry.Dispose();
            _geometry = null;
            if (_dropped != null)
                Object.Destroy(_dropped);
        }

        /// <summary>
        /// Catch playtest 1 ("al resetear se pierde collider y me caigo del mapa"): turning the chassis box
        /// off left the body with no shape, Unity recomputed its automatic inertia tensor from nothing, and an
        /// 1100 kg car ran the window with a unit tensor. The ghost must leave the mass properties exactly as
        /// the solid car had them, and hand them back to the colliders when it ends.
        /// </summary>
        [UnityTest]
        public IEnumerator GhostWindow_KeepsTheChassisInertia()
        {
            _body = new GameObject("Body");
            var rigidbody = _body.AddComponent<Rigidbody>();
            rigidbody.mass = 1100f;
            rigidbody.useGravity = false;
            var chassis = _body.AddComponent<BoxCollider>();
            chassis.size = new Vector3(2f, 1f, 4f);
            yield return new WaitForFixedUpdate();

            Vector3 solid = rigidbody.inertiaTensor;
            Assert.That(solid.y, Is.GreaterThan(100f), "precondition: the box gives the body a real tensor");

            var monitor = new CarGhostMonitor(StuckSeconds, 0.5f, GhostSeconds, GhostAlpha);
            monitor.Bind(null, new Collider[] { chassis });
            Assert.That(monitor.RequestForcedRespawn(), Is.True);
            yield return new WaitForFixedUpdate();

            Assert.That(chassis.enabled, Is.False);
            AssertSameTensor(solid, rigidbody.inertiaTensor, "during the ghost window");

            for (int i = 0; i < GhostTicks; i++)
                monitor.Tick(Dt, 0f, 0f);
            yield return new WaitForFixedUpdate();

            Assert.That(chassis.enabled, Is.True);
            Assert.That(rigidbody.automaticInertiaTensor, Is.True, "the colliders own the tensor again");
            AssertSameTensor(solid, rigidbody.inertiaTensor, "after the ghost window");
        }

        /// <summary>
        /// The owner-visible half of the same bug: a V2 car put back on the road with the ghost on must stay on
        /// the road, upright, for the whole window, and be solid again afterwards (a box dropped on it lands on
        /// its roof instead of falling through).
        /// </summary>
        [UnityTest]
        public IEnumerator RespawnedCar_StaysOnTheFloorThroughTheGhost_AndIsSolidAfterIt()
        {
            CarDefinition definition = Resources.Load<CarDefinition>("CarDefinition");
            Assert.IsNotNull(definition, "CarDefinition must ship in Resources");

            var spawn = new Pose(new Vector3(0f, 2f, 0f), Quaternion.identity);
            _geometry = CarGeometryFactory.Create(definition, spawn);
            Rigidbody body = _geometry.Body;
            var driver = body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            driver.Bind(body, definition,
                MotorSetup.ForPreset(HandlingPreset.Balanced, JoystickDriveMode.CarRelative, null, 0f));
            float floorTop = _geometry.Floor.bounds.max.y;

            for (int i = 0; i < 50; i++)
                yield return new WaitForFixedUpdate();

            // The respawn's own write (CircuitRespawn.Commit): pose, rotation, at rest. Then the ghost.
            body.position = spawn.position;
            body.rotation = spawn.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            var monitor = new CarGhostMonitor(StuckSeconds, 0.5f, GhostSeconds, GhostAlpha);
            monitor.Bind(null, new Collider[] { _geometry.Chassis });
            Assert.That(monitor.RequestForcedRespawn(), Is.True);

            float elapsed = 0f;
            while (elapsed < 2f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
                monitor.Tick(Time.fixedDeltaTime, 0f, 0f);
                Assert.That(body.position.y, Is.GreaterThan(floorTop),
                    "the respawned car fell through the floor after " + elapsed.ToString("F2") + " s");
                Assert.That(Vector3.Angle(body.transform.up, Vector3.up), Is.LessThan(50f),
                    "the respawned car tipped over after " + elapsed.ToString("F2") + " s");
            }

            while (monitor.IsGhosted)
            {
                monitor.Tick(Time.fixedDeltaTime, 0f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(_geometry.Chassis.enabled, Is.True);
            float roof = _geometry.Chassis.bounds.max.y;
            _dropped = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _dropped.transform.position = body.position + Vector3.up * 3f;
            _dropped.AddComponent<Rigidbody>().mass = 50f;
            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.That(_dropped.transform.position.y, Is.GreaterThan(roof),
                "a solid car must stop a box dropped on its roof");
        }

        private static void AssertSameTensor(Vector3 expected, Vector3 actual, string when)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(expected.x * 0.01f), when);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(expected.y * 0.01f), when);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(expected.z * 0.01f), when);
        }

        private CarGhostMonitor CreateMonitor(out BoxCollider chassis, out Renderer panel,
            out Material original)
        {
            _body = new GameObject("Body");
            chassis = _body.AddComponent<BoxCollider>();
            _visualRoot = new GameObject("Visual");
            _visualRoot.transform.SetParent(_body.transform, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Panel";
            Object.Destroy(cube.GetComponent<BoxCollider>());
            cube.transform.SetParent(_visualRoot.transform, false);
            panel = cube.GetComponent<Renderer>();
            original = panel.sharedMaterial;

            var monitor = new CarGhostMonitor(StuckSeconds, 0.5f, GhostSeconds, GhostAlpha);
            monitor.Bind(_visualRoot.transform, new Collider[] { chassis });
            return monitor;
        }

        [Test]
        public void StuckWindow_FiresExactlyOnce_AndStartsTheGhost()
        {
            CarGhostMonitor monitor = CreateMonitor(out BoxCollider chassis, out _, out _);

            for (int i = 0; i < StuckTicks - 1; i++)
            {
                Assert.That(monitor.Tick(Dt, 0f, 1f), Is.False);
            }

            Assert.That(monitor.Tick(Dt, 0f, 1f), Is.True, "the full stuck window must fire the respawn");
            Assert.That(chassis.enabled, Is.False, "the ghost must be on as the respawn fires");
            Assert.That(monitor.IsGhosted, Is.True);
            Assert.That(monitor.IsArmed, Is.False, "the clock must be disarmed for the whole window");

            Assert.That(monitor.Tick(Dt, 0f, 1f), Is.False, "the respawn must fire exactly once");
        }

        [Test]
        public void GhostWindow_EndsByItself_AndRestoresTheCar()
        {
            CarGhostMonitor monitor = CreateMonitor(out BoxCollider chassis, out Renderer panel,
                out Material original);
            RunToRespawn(monitor);

            for (int i = 0; i < GhostTicks; i++)
            {
                Assert.That(monitor.Tick(Dt, 0f, 1f), Is.False);
            }

            Assert.That(monitor.IsGhosted, Is.False);
            Assert.That(monitor.IsArmed, Is.True, "a closed window must hand the clock back armed");
            Assert.That(chassis.enabled, Is.True, "the window must give the collider back");
            Assert.That(panel.sharedMaterial, Is.SameAs(original),
                "the window must restore the exact original material references");

            // And the cycle must be repeatable: the car can get pinned again on the next run.
            Assert.That(RunToRespawn(monitor), Is.True);
        }

        [Test]
        public void RealMovement_RestartsTheStuckWindow()
        {
            CarGhostMonitor monitor = CreateMonitor(out BoxCollider chassis, out _, out _);

            Assert.That(RunToRespawn(monitor, 3), Is.False, "three ticks alone must not fire");

            Assert.That(monitor.Tick(Dt, 0.5f, 1f), Is.False, "speed at the limit is real movement");

            Assert.That(RunToRespawn(monitor, StuckTicks - 1), Is.False,
                "the whole window must be earned again after movement");
            Assert.That(monitor.Tick(Dt, 0f, 1f), Is.True);
            Assert.That(chassis.enabled, Is.False);
        }

        [Test]
        public void DemandRelease_RestartsTheStuckWindow()
        {
            CarGhostMonitor monitor = CreateMonitor(out BoxCollider chassis, out _, out _);

            Assert.That(RunToRespawn(monitor, 3), Is.False);

            Assert.That(monitor.Tick(Dt, 0f, 0.1f), Is.False,
                "a pedal below the demand floor releases the stuck window");

            Assert.That(RunToRespawn(monitor, StuckTicks - 1), Is.False);
            Assert.That(monitor.Tick(Dt, 0f, 1f), Is.True);
            Assert.That(chassis.enabled, Is.False);
        }

        [Test]
        public void Teardown_EndsAnActiveGhost_AndReleasesIt()
        {
            CarGhostMonitor monitor = CreateMonitor(out BoxCollider chassis, out Renderer panel,
                out Material original);
            RunToRespawn(monitor);
            Assert.That(chassis.enabled, Is.False);

            monitor.Teardown();

            Assert.That(chassis.enabled, Is.True, "teardown must make the car solid again");
            Assert.That(panel.sharedMaterial, Is.SameAs(original));
            Assert.That(monitor.IsGhosted, Is.False);
        }

        /// <summary>
        /// Ticks up to <paramref name="maxTicks"/> of throttle-demanded immobility, stopping at the
        /// respawn. Returns whether the respawn fired within the budget.
        /// </summary>
        private static bool RunToRespawn(CarGhostMonitor monitor, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (monitor.Tick(Dt, 0f, 1f))
                    return true;
            }

            return false;
        }

        /// <summary>Runs the full stuck window, asserting the respawn fires on the last tick.</summary>
        private static bool RunToRespawn(CarGhostMonitor monitor)
        {
            Assert.That(RunToRespawn(monitor, StuckTicks), Is.True);
            return true;
        }
    }
}
