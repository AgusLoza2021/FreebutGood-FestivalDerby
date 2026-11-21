using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>G2b Editor construction/lifetime only; run FestivalDerby.Driving.PlayModeTests.</summary>
    public sealed class CarGeometryFactoryPlayModeTests
    {
        private CarDefinition _definition;
        private GameObject _parent, _sentinel;
        private readonly HashSet<Object> _cleanup = new HashSet<Object>();
        private readonly List<OwnedCarGeometry> _handles = new List<OwnedCarGeometry>();
        private static readonly Pose Spawn = new Pose(new Vector3(4f, 3f, -2f), Quaternion.Euler(0f, 37f, 0f));
        private static readonly FieldInfo[] Fields = typeof(CarDefinition).GetFields(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<CarDefinition>();
            _cleanup.Add(_definition);
            _parent = new GameObject("G2b test parent");
            _cleanup.Add(_parent);
            _parent.transform.SetPositionAndRotation(new Vector3(12f, 4f, -9f), Quaternion.Euler(15f, 50f, 7f));
            _sentinel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cleanup.Add(_sentinel);
            _sentinel.GetComponent<Collider>().enabled = false;
            _sentinel.transform.SetParent(_parent.transform, false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Explicit fallback destruction also protects fixtures when Dispose is still a stub.
            foreach (var handle in _handles)
                handle.Dispose();
            foreach (var item in _cleanup)
                if (item != null)
                    Object.Destroy(item);
            _handles.Clear();
            _cleanup.Clear();
            yield return null;
        }

        private static HashSet<Object> ResourcesNow()
        {
            var result = new HashSet<Object>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid())
                    result.Add(go);
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                result.Add(material);
            return result;
        }

        private OwnedCarGeometry Create(CarDefinition definition, Transform parent = null)
        {
            var before = ResourcesNow();
            try
            {
                var handle = CarGeometryFactory.Create(definition, Spawn, parent);
                if (handle != null)
                    _handles.Add(handle);
                return handle;
            }
            finally
            {
                // Snapshot only this synchronous creation, not unrelated objects appearing across frames.
                foreach (var item in ResourcesNow())
                    if (!before.Contains(item) && item.hideFlags == HideFlags.None)
                        _cleanup.Add(item);
            }
        }

        private void RequireHandle(OwnedCarGeometry handle)
        {
            Assert.That(handle, Is.Not.Null, "Create must return owned geometry, not the RED null stub.");
            _cleanup.Add(handle.Root);
            _cleanup.Add(handle.Material);
            Assert.That(handle.Root, Is.Not.Null);
            Assert.That(handle.Body, Is.Not.Null);
            Assert.That(handle.Chassis, Is.Not.Null);
            Assert.That(handle.Floor, Is.Not.Null);
            Assert.That(handle.Material, Is.Not.Null);
        }

        private static void VectorNear(Vector3 actual, Vector3 expected)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.That(float.IsNaN(actual[axis]) || float.IsInfinity(actual[axis]), Is.False);
                Assert.That(actual[axis], Is.EqualTo(expected[axis]).Within(0.0001f));
            }
        }

        private static Vector3 WorldSize(BoxCollider collider)
        {
            var scale = collider.transform.lossyScale;
            return Vector3.Scale(collider.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Create_OwnsOnlyFreshBoxesAtWorldPose_WithUnitParent(bool parented)
        {
            var existingMaterial = _sentinel.GetComponent<Renderer>().sharedMaterial;
            var existingColor = existingMaterial.color;
            var handle = Create(_definition, parented ? _parent.transform : null);
            RequireHandle(handle);
            Assert.That(handle.IsDisposed, Is.False);
            Assert.That(handle.Root, Is.Not.SameAs(_parent).And.Not.SameAs(_sentinel));
            Assert.That(handle.Root.transform.parent, Is.EqualTo(parented ? _parent.transform : null));
            VectorNear(handle.Root.transform.position, Vector3.zero);
            VectorNear(handle.Root.transform.lossyScale, Vector3.one);
            Assert.That(Quaternion.Angle(handle.Root.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            VectorNear(handle.Body.position, Spawn.position);
            Assert.That(Quaternion.Angle(handle.Body.rotation, Spawn.rotation), Is.LessThan(0.001f));
            Assert.That(handle.Body.isKinematic, Is.False);
            Assert.That(handle.Body.mass, Is.EqualTo(_definition.mass).Within(0.001f));
            Assert.That(handle.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate),
                "A driven car must interpolate so its rendered pose matches the fixed simulation tick.");
            Assert.That(handle.Body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic),
                "A derby car must not tunnel through a rival at ramming speed.");
            Assert.That(handle.Body.constraints, Is.EqualTo(RigidbodyConstraints.None),
                "Nothing may constrain rotation: cars are meant to launch, roll and land on their roof.");
            Assert.That(handle.Body.centerOfMass.y, Is.LessThan(0f),
                "A lowered centre of mass keeps the arcade chassis readable instead of tippy.");
            Assert.That(handle.Body.maxDepenetrationVelocity, Is.GreaterThan(0f).And.LessThanOrEqualTo(6f),
                "Overlapping cars must separate at a bounded speed instead of being flung apart.");
            Assert.That(handle.Body.maxLinearVelocity,
                Is.EqualTo(_definition.maxForwardSpeed * 1.3f).Within(0.001f),
                "The hard velocity ceiling must sit above the drivable top speed, with headroom for rams.");
            Assert.That(handle.Body.GetComponent<CarRamLift>(), Is.Not.Null,
                "Every car must be able to ram a rival up and over.");
            Assert.That(handle.Chassis.sharedMaterial, Is.Not.Null, "The chassis owns its arcade contact material.");
            Assert.That(handle.Chassis.sharedMaterial.bounciness, Is.GreaterThan(0f),
                "A slightly live chassis keeps scrapes arcade instead of sticky.");
            Assert.That(handle.Body.transform.IsChildOf(handle.Root.transform), Is.True);
            Assert.That(handle.Chassis.attachedRigidbody, Is.SameAs(handle.Body));
            Assert.That(handle.Floor.transform.IsChildOf(handle.Root.transform), Is.True);
            Assert.That(handle.Floor.attachedRigidbody, Is.Null);
            Assert.That(handle.Root.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            VectorNear(WorldSize(handle.Chassis), _definition.bodySize);
            VectorNear(handle.Chassis.transform.TransformPoint(handle.Chassis.center), Spawn.position);
            VectorNear(WorldSize(handle.Floor), new Vector3(24f, 0.5f, 48f));
            VectorNear(handle.Floor.transform.TransformPoint(handle.Floor.center),
                Spawn.position - Vector3.up * (_definition.bodySize.y * 0.5f + 0.25f));
            Assert.That(Quaternion.Angle(handle.Floor.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            int enabled = 0;
            foreach (var collider in handle.Root.GetComponentsInChildren<Collider>(true))
                if (collider.enabled)
                {
                    Assert.That(collider == handle.Chassis || collider == handle.Floor, Is.True);
                    Assert.That(collider.isTrigger, Is.False);
                    enabled++;
                }
            Assert.That(enabled, Is.EqualTo(2), "Decorative primitive colliders must be disabled immediately.");
            Assert.That(handle.Body.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(6), "Chassis, cabin, four wheels.");
            var renderers = handle.Root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(7));
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "An unavailable URP shader must fail explicitly, never use a pink fallback.");
            Assert.That(handle.Material.shader, Is.SameAs(shader));
            Assert.That(handle.Material, Is.Not.SameAs(existingMaterial));
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
                Assert.That(renderer.sharedMaterial, Is.SameAs(handle.Material));
            }
            Assert.That(_sentinel.GetComponent<Renderer>().sharedMaterial, Is.SameAs(existingMaterial));
            Assert.That(existingMaterial.color, Is.EqualTo(existingColor));
        }

        private static int[] SnapshotFloatBits(object value)
        {
            // Preserve NaN payload/sign and signed zero rather than using floating-point equality.
            var components = value is Vector3 vector ? new[] { vector.x, vector.y, vector.z } : new[] { (float)value };
            var bits = new int[components.Length];
            for (int i = 0; i < components.Length; i++)
                bits[i] = BitConverter.ToInt32(BitConverter.GetBytes(components[i]), 0);
            return bits;
        }

        [TestCase(float.NaN, 0.1f, 1f)]
        [TestCase(float.PositiveInfinity, 0.1f, 1f)]
        [TestCase(float.NegativeInfinity, 0.1f, 1f)]
        [TestCase(-2f, 0.1f, 1f)]
        [TestCase(0f, 0.1f, 1f)]
        [TestCase(0.05f, 0.1f, 1f)]
        [TestCase(0.1f, 0.1f, 1f)]
        [TestCase(1f, 1f, 1f)]
        [TestCase(2.75f, 2.75f, 2.75f)]
        public void Create_CopiesSafeDimensionsAndMass_WithoutSanitizingDefinition(float input, float size, float mass)
        {
            _definition.bodySize = new Vector3(input, input, input);
            _definition.mass = input;
            _definition.driveForce = float.NaN;
            _definition.nitroDuration = -7f;
            var original = new object[Fields.Length];
            for (int i = 0; i < Fields.Length; i++) original[i] = SnapshotFloatBits(Fields[i].GetValue(_definition));
            var handle = Create(_definition);
            RequireHandle(handle);
            VectorNear(WorldSize(handle.Chassis), Vector3.one * size);
            Assert.That(handle.Body.mass, Is.EqualTo(mass).Within(0.0001f));
            for (int i = 0; i < Fields.Length; i++)
                Assert.That(SnapshotFloatBits(Fields[i].GetValue(_definition)), Is.EqualTo(original[i]), Fields[i].Name);
        }

        [Test]
        public void Create_NullDefinitionRejectsBeforeAllocating()
        {
            var before = ResourcesNow();
            Assert.Throws<ArgumentNullException>(() => Create(null, _parent.transform));
            Assert.That(ResourcesNow(), Is.EquivalentTo(before), "Rejection must not leak objects/materials.");
        }

        [TestCase(2f, 2f, 2f, false)]
        [TestCase(-1f, -1f, -1f, false)]
        [TestCase(1f, 2f, 1f, false)]
        [TestCase(0f, 1f, 1f, false)]
        [TestCase(2f, 2f, 2f, true)]
        public void Create_NonUnitParentRejectsBeforeAllocating(float x, float y, float z, bool inherited)
        {
            _parent.transform.localScale = new Vector3(x, y, z);
            var before = ResourcesNow();
            Assert.Throws<ArgumentException>(() => Create(_definition, inherited ? _sentinel.transform : _parent.transform));
            Assert.That(ResourcesNow(), Is.EquivalentTo(before), "Rejection must not leak objects/materials.");
        }

        [UnityTest]
        public IEnumerator Dispose_DisablesImmediately_DestroysOnceAfterFrame_AndPreservesExistingObjects()
        {
            var created = ResourcesNow();
            var handle = Create(_definition, _parent.transform);
            RequireHandle(handle);
            var afterCreation = ResourcesNow();
            afterCreation.ExceptWith(created);
            int materialCount = 0;
            foreach (var item in afterCreation)
                if (item is Material) materialCount++;
            Assert.That(materialCount, Is.EqualTo(1), "Exactly one new runtime material, including implicit clones.");
            var root = handle.Root;
            var material = handle.Material;
            var floorMaterial = handle.Floor.sharedMaterial ?? throw new InvalidOperationException("Owned floor material is missing.");
            var children = root.GetComponentsInChildren<Transform>(true);
            var parentPosition = _parent.transform.position;
            var parentRotation = _parent.transform.rotation;
            handle.Dispose();
            Assert.That(handle.IsDisposed, Is.True);
            Assert.That(root.activeSelf, Is.False, "Destroy is deferred, so activity must stop synchronously.");
            Assert.DoesNotThrow(handle.Dispose);
            yield return null;
            Assert.That(root == null, Is.True);
            Assert.That(material == null, Is.True);
            Assert.That(floorMaterial == null, Is.True);
            foreach (var child in children) Assert.That(child == null, Is.True);
            foreach (var item in afterCreation) Assert.That(item == null, Is.True, "No owned resource leaks.");
            Assert.That(_parent != null && _sentinel != null && _definition != null, Is.True);
            VectorNear(_parent.transform.position, parentPosition);
            Assert.That(Quaternion.Angle(_parent.transform.rotation, parentRotation), Is.LessThan(0.001f));
            Assert.That(_parent.transform.childCount, Is.EqualTo(1));
            Assert.That(_sentinel.transform.parent, Is.SameAs(_parent.transform));
            Assert.That(handle.IsDisposed, Is.True);
            Assert.DoesNotThrow(handle.Dispose);
        }

        [Test]
        public void RouteBot_DistantFirstGoalIsNotSkipped()
        {
            var player = Create(_definition);
            RequireHandle(player);
            var bounds = player.Floor.bounds;
            var layout = new GrayboxCircuitLayout(new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(bounds.size.x, bounds.size.z), bounds.max.y, _definition.bodySize.y * 0.5f);
            OwnedGrayboxCircuit circuit = null;
            GrayboxRouteBot bot = null;
            try
            {
                circuit = GrayboxCircuitFactory.Create(layout);
                _cleanup.Add(circuit.Root);
                _cleanup.Add(circuit.Material);
                var first = circuit.GetRoutePose(0);
                var previous = circuit.GetRoutePose(circuit.RouteCount - 1);
                var incoming = Vector3.ProjectOnPlane(first.position - previous.position, Vector3.up).normalized;
                var position = new Vector3(bounds.center.x, bounds.max.y + layout.SpawnHeight, bounds.max.z - _definition.bodySize.z);
                bot = new GrayboxRouteBot(circuit, _definition);
                var handle = bot.CreateBody(new Pose(position, Quaternion.LookRotation(incoming)), null);
                _handles.Add(handle);
                _cleanup.Add(handle.Root);
                _cleanup.Add(handle.Material);
                var toGoal = Vector3.ProjectOnPlane(first.position - handle.Body.position, Vector3.up);
                Assert.That(toGoal.magnitude, Is.GreaterThan(6f), "First goal must be outside ReachRadius.");
                Assert.That(Vector3.Dot(incoming, -toGoal), Is.LessThan(0f), "Body must be behind the incoming goal.");
                var index = typeof(GrayboxRouteBot).GetField("_goalIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                var sample = typeof(GrayboxRouteBot).GetMethod("SampleInput", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(index.GetValue(bot), Is.EqualTo(0));
                sample.Invoke(bot, new object[] { 0.02f });
                Assert.That(index.GetValue(bot), Is.EqualTo(0), "Sampling must not skip a distant goal ahead on its incoming segment.");
            }
            finally
            {
                bot?.Dispose();
                circuit?.Dispose();
            }
        }

        [Test]
        public void RouteBot_FigureEightApproachDoesNotSkipOrderedCrossing()
            => AssertFigureEightCrossingApproach(0);

        [Test]
        public void RouteBot_FigureEightSecondApproachDoesNotSkipOrderedCrossing()
            => AssertFigureEightCrossingApproach(12);

        private void AssertFigureEightCrossingApproach(int firstGoalIndex)
        {
            var player = Create(_definition);
            RequireHandle(player);
            var bounds = player.Floor.bounds;
            var layout = new GrayboxCircuitLayout(new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(24f, 48f), bounds.max.y, _definition.bodySize.y * 0.5f, figureEight: true);
            OwnedGrayboxCircuit circuit = null;
            GrayboxRouteBot bot = null;
            try
            {
                circuit = GrayboxCircuitFactory.Create(layout);
                _cleanup.Add(circuit.Root);
                _cleanup.Add(circuit.Material);
                Assert.That(circuit.RouteCount, Is.EqualTo(24));
                var goal = circuit.GetRoutePose(firstGoalIndex);
                var previous = circuit.GetRoutePose((firstGoalIndex + circuit.RouteCount - 1) % circuit.RouteCount);
                var incoming = Vector3.ProjectOnPlane(goal.position - previous.position, Vector3.up).normalized;
                var position = goal.position - incoming * 4f;
                position.y = layout.SurfaceY + layout.SpawnHeight;
                bot = firstGoalIndex == 0 ? new GrayboxRouteBot(circuit, _definition)
                    : new GrayboxRouteBot(circuit, _definition, firstGoalIndex);
                var handle = bot.CreateBody(new Pose(position, Quaternion.LookRotation(incoming)), null);
                _handles.Add(handle);
                _cleanup.Add(handle.Root);
                _cleanup.Add(handle.Material);
                var toGoal = Vector3.ProjectOnPlane(goal.position - handle.Body.position, Vector3.up);
                Assert.That(toGoal.magnitude, Is.EqualTo(4f).Within(0.0001f));
                Assert.That(Vector3.Dot(incoming, -toGoal), Is.EqualTo(-4f).Within(0.0001f));
                var index = typeof(GrayboxRouteBot).GetField("_goalIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                var sample = typeof(GrayboxRouteBot).GetMethod("SampleInput", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(index.GetValue(bot), Is.EqualTo(firstGoalIndex));
                var input = (CarInput)sample.Invoke(bot, new object[] { 0.02f });
                Assert.That(input.reverse || input.brake || input.nitroPressed, Is.False, "Approach retains forward input.");
                Assert.That(index.GetValue(bot), Is.EqualTo(firstGoalIndex),
                    "Stay on the ordered incoming goal until reached; never skip or select the nearest crossing branch.");
            }
            finally
            {
                bot?.Dispose();
                circuit?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Create_TwoIndependentOwners_DisposingOneDoesNotTouchTheOther()
        {
            var first = Create(_definition);
            RequireHandle(first);
            var second = Create(_definition);
            RequireHandle(second);
            Assert.That(second.Root, Is.Not.SameAs(first.Root));
            Assert.That(second.Material, Is.Not.SameAs(first.Material));
            first.Dispose();
            yield return null;
            Assert.That(second.Root != null && second.Material != null, Is.True);
            Assert.That(second.Root.activeSelf, Is.True);
            Assert.That(second.IsDisposed, Is.False);
        }
        [UnityTest]
        public IEnumerator RouteBots_HeadOnContact_RecoversWithoutPhysicsOverride()
        {
            bool previousBackground = Application.runInBackground;
            var player = Create(_definition);
            RequireHandle(player);
            var bounds = player.Floor.bounds;
            var layout = new GrayboxCircuitLayout(new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(bounds.size.x, bounds.size.z), bounds.max.y, _definition.bodySize.y * 0.5f);
            var bots = new GrayboxRouteBot[2];
            var geometries = new OwnedCarGeometry[2];
            var drivers = new LocalCarFixedStepDriver[2];
            var starts = new Vector3[2];
            var counts = new int[2];
            OwnedGrayboxCircuit circuit = null;
            try
            {
                circuit = GrayboxCircuitFactory.Create(layout);
                _cleanup.Add(circuit.Root);
                _cleanup.Add(circuit.Material);
                Application.runInBackground = true;
                var index = typeof(GrayboxRouteBot).GetField("_goalIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < bots.Length; i++)
                {
                    int goalIndex = 1 - i;
                    starts[i] = new Vector3(bounds.center.x + (i == 0 ? 1f : -1f) * _definition.bodySize.z * 0.5f,
                        circuit.SpawnPose.position.y, circuit.GetRoutePose(0).position.z);
                    var goal = circuit.GetRoutePose(goalIndex).position;
                    var previous = circuit.GetRoutePose((goalIndex + circuit.RouteCount - 1) % circuit.RouteCount).position;
                    var toGoal = Vector3.ProjectOnPlane(goal - starts[i], Vector3.up);
                    var incoming = Vector3.ProjectOnPlane(goal - previous, Vector3.up).normalized;
                    Assert.That(toGoal.magnitude, Is.GreaterThan(6f), "Jam must start outside ReachRadius.");
                    Assert.That(Vector3.Dot(incoming, -toGoal), Is.LessThan(2f), "Jam must not exceed the passed-goal margin.");
                    bots[i] = new GrayboxRouteBot(circuit, _definition);
                    geometries[i] = bots[i].CreateBody(new Pose(starts[i], Quaternion.Euler(0f, i == 0 ? 270f : 90f, 0f)), null);
                    _handles.Add(geometries[i]);
                    _cleanup.Add(geometries[i].Root);
                    _cleanup.Add(geometries[i].Material);
                    index.SetValue(bots[i], goalIndex);
                    drivers[i] = geometries[i].Body.GetComponent<LocalCarFixedStepDriver>();
                    counts[i] = drivers[i].Snapshot().FixedStepCount;
                }
                Assert.That(drivers, Has.None.Null.And.Unique);
                // Touching noses reproduce the live jam without forcing momentum or bypassing the motor.
                // Recovery is a back-and-forth: after the stall timer the pair reverses apart, then drives
                // forward again and may meet near the start by the final tick. The proof that a bot left
                // the jam is therefore the farthest it got, not where the run happens to end.
                var farthest = new float[bots.Length];
                for (int step = 0; step < 180; step++)
                {
                    yield return new WaitForFixedUpdate();
                    for (int i = 0; i < bots.Length; i++)
                        farthest[i] = Mathf.Max(farthest[i], Vector3.ProjectOnPlane(
                            geometries[i].Body.position - starts[i], Vector3.up).sqrMagnitude);
                }
                for (int i = 0; i < bots.Length; i++)
                {
                    Assert.That(drivers[i].Snapshot().FixedStepCount, Is.GreaterThan(counts[i]), $"Bot {i} must keep ticking.");
                    Assert.That(drivers[i].Snapshot().IsPaused, Is.False, $"Bot {i} must be unpaused.");
                }
                for (int i = 0; i < bots.Length; i++)
                    Assert.That(farthest[i], Is.GreaterThan(0.25f), $"Bot {i} must move more than 0.5m from head-on contact.");
            }
            finally
            {
                foreach (var bot in bots) bot?.Dispose();
                circuit?.Dispose();
                Application.runInBackground = previousBackground;
            }
        }

        [UnityTest]
        public IEnumerator RouteBots_ShareFloor_PauseAndDispose()
        {
            var player = Create(_definition);
            RequireHandle(player);
            var bounds = player.Floor.bounds;
            var layout = new GrayboxCircuitLayout(new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(bounds.size.x, bounds.size.z), bounds.max.y, _definition.bodySize.y * 0.5f);
            var bots = new GrayboxRouteBot[3];
            var geometries = new OwnedCarGeometry[3];
            var drivers = new LocalCarFixedStepDriver[3];
            var starts = new Vector3[3];
            var counts = new int[3];
            OwnedGrayboxCircuit circuit = null;
            try
            {
                var before = ResourcesNow();
                try
                {
                    circuit = GrayboxCircuitFactory.Create(layout);
                    for (int i = 0; i < bots.Length; i++)
                    {
                        starts[i] = new Vector3(bounds.center.x + (i - 1) * (_definition.bodySize.x + 2f),
                            bounds.max.y + layout.SpawnHeight, bounds.center.z + _definition.bodySize.z * 2f);
                        bots[i] = new GrayboxRouteBot(circuit, _definition);
                        geometries[i] = bots[i].CreateBody(new Pose(starts[i], Quaternion.Euler(0f, 180f, 0f)), null);
                        _handles.Add(geometries[i]);
                        drivers[i] = geometries[i].Body.GetComponent<LocalCarFixedStepDriver>();
                        Assert.That(geometries[i].Floor, Is.Null);
                        Assert.That(geometries[i].Body.isKinematic || geometries[i].Chassis.isTrigger, Is.False);
                        Assert.That(Array.FindAll(geometries[i].Root.GetComponentsInChildren<Collider>(true), c => c.enabled),
                            Is.EqualTo(new Collider[] { geometries[i].Chassis }));
                    }
                }
                finally
                {
                    foreach (var item in ResourcesNow())
                        if (!before.Contains(item) && item.hideFlags == HideFlags.None) _cleanup.Add(item);
                }
                Assert.That(drivers, Has.None.Null.And.Unique);
                for (int step = 0; step < 16; step++) yield return new WaitForFixedUpdate();
                for (int i = 0; i < bots.Length; i++)
                {
                    Assert.That(drivers[i].Snapshot().FixedStepCount, Is.GreaterThan(0));
                    Assert.That(Vector3.ProjectOnPlane(geometries[i].Body.position - starts[i], Vector3.up).sqrMagnitude,
                        Is.GreaterThan(0.000001f),
                        $"Bot {i}: steps={drivers[i].Snapshot().FixedStepCount}, mode={drivers[i].Snapshot().ResolvedMode}, " +
                        $"simulation={Physics.simulationMode}, velocity={geometries[i].Body.linearVelocity.ToString("F9")}");
                    counts[i] = drivers[i].Snapshot().FixedStepCount;
                    bots[i].SetPaused(true);
                }
                // Cooperative pause gates driver steps, not the body's existing physics momentum.
                for (int step = 0; step < 2; step++) yield return new WaitForFixedUpdate();
                for (int i = 0; i < bots.Length; i++)
                {
                    Assert.That(drivers[i].Snapshot().FixedStepCount, Is.EqualTo(counts[i]));
                    bots[i].SetPaused(false);
                }
                for (int step = 0; step < 2; step++) yield return new WaitForFixedUpdate();
                for (int i = 0; i < bots.Length; i++)
                {
                    Assert.That(drivers[i].Snapshot().FixedStepCount, Is.GreaterThan(counts[i]));
                    bots[i].Dispose();
                    bots[i].Dispose();
                }
                yield return null;
                for (int i = 0; i < bots.Length; i++)
                    Assert.That(geometries[i].Root == null && geometries[i].Material == null && drivers[i] == null, Is.True);
                Assert.That(player.Floor != null && player.Floor.enabled && circuit.Root != null
                    && circuit.Root.activeSelf && circuit.Material != null && !circuit.IsDisposed, Is.True);
            }
            finally
            {
                foreach (var bot in bots) bot?.Dispose();
                circuit?.Dispose();
            }
        }
    }
}
