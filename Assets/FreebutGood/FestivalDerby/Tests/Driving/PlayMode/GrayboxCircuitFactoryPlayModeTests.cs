using System;
using System.Collections;
using System.Collections.Generic;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>H2b Editor construction/lifetime only; run FestivalDerby.Driving.PlayModeTests.</summary>
    public sealed class GrayboxCircuitFactoryPlayModeTests
    {
        private const float Tolerance = 0.0001f;

        // Keep a wall margin while using 65% of the racing half extent for the route,
        // so goals are not crowded around the center or tied to a floor constant.
        private const float CornerRadiusFraction = 0.65f;

        // Marker boxes keep a low readable arrow profile on the driving surface.
        private const float MarkerBoxHeight = 0.12f;

        private static readonly GrayboxCircuitLayout Layout = new GrayboxCircuitLayout(
            new Vector2(3f, -5f), new Vector2(24f, 48f), -0.35f);

        private GameObject _parent, _sentinel;
        private readonly HashSet<Object> _cleanup = new HashSet<Object>();
        private readonly List<OwnedGrayboxCircuit> _handles = new List<OwnedGrayboxCircuit>();

        private static float Left => Layout.Center.x - Layout.Size.x * 0.5f;
        private static float Right => Layout.Center.x + Layout.Size.x * 0.5f;
        private static float Bottom => Layout.Center.y - Layout.Size.y * 0.5f;
        private static float Top => Layout.Center.y + Layout.Size.y * 0.5f;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("H2b test parent");
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
            // Explicit fallback destruction also protects fixtures while Dispose is still a stub.
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

        private OwnedGrayboxCircuit Create(in GrayboxCircuitLayout layout, Transform parent = null)
        {
            var before = ResourcesNow();
            try
            {
                var handle = GrayboxCircuitFactory.Create(layout, parent);
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

        private void RequireHandle(OwnedGrayboxCircuit handle)
        {
            Assert.That(handle, Is.Not.Null, "Create must return owned circuit geometry, not the RED null stub.");
            _cleanup.Add(handle.Root);
            _cleanup.Add(handle.Material);
            Assert.That(handle.Root, Is.Not.Null);
            Assert.That(handle.Material, Is.Not.Null);
        }

        private static void VectorNear(Vector3 actual, Vector3 expected)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.That(float.IsNaN(actual[axis]) || float.IsInfinity(actual[axis]), Is.False);
                Assert.That(actual[axis], Is.EqualTo(expected[axis]).Within(Tolerance));
            }
        }

        private static Vector3 WorldSize(BoxCollider collider)
        {
            var scale = collider.transform.lossyScale;
            return Vector3.Scale(collider.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        private static void WorldBounds(BoxCollider collider, out Vector3 min, out Vector3 max)
        {
            var center = collider.transform.TransformPoint(collider.center);
            var half = WorldSize(collider) * 0.5f;
            min = center - half;
            max = center + half;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Create_OwnsOnlyFreshBoxesAtWorldPose_WithUnitParent(bool parented)
        {
            var existingMaterial = _sentinel.GetComponent<Renderer>().sharedMaterial;
            var existingColor = existingMaterial.color;
            var parentPosition = _parent.transform.position;
            var parentRotation = _parent.transform.rotation;
            var handle = Create(Layout, parented ? _parent.transform : null);
            RequireHandle(handle);
            Assert.That(handle.IsDisposed, Is.False);
            Assert.That(handle.Root, Is.Not.SameAs(_parent).And.Not.SameAs(_sentinel));
            Assert.That(handle.Root.transform.parent, Is.EqualTo(parented ? _parent.transform : null));
            VectorNear(handle.Root.transform.position, Vector3.zero);
            VectorNear(handle.Root.transform.lossyScale, Vector3.one);
            Assert.That(Quaternion.Angle(handle.Root.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            Assert.That(handle.Root.GetComponentsInChildren<Rigidbody>(true), Is.Empty, "The circuit never simulates.");
            Assert.That(handle.Walls, Has.Length.EqualTo(4));
            Assert.That(handle.StartGate, Is.Not.Null);
            Assert.That(handle.StartGate.IsChildOf(handle.Root.transform), Is.True);
            Assert.That(handle.Layout.Center, Is.EqualTo(Layout.Center));
            Assert.That(handle.Layout.Size, Is.EqualTo(Layout.Size));
            Assert.That(handle.Layout.SurfaceY, Is.EqualTo(Layout.SurfaceY).Within(Tolerance));
            Assert.That(handle.Layout.SpawnHeight, Is.EqualTo(Layout.SpawnHeight).Within(Tolerance));
            Assert.That(handle.Layout.WallHeight, Is.EqualTo(Layout.WallHeight).Within(Tolerance));
            Assert.That(handle.Layout.WallThickness, Is.EqualTo(Layout.WallThickness).Within(Tolerance));
            var renderers = handle.Root.GetComponentsInChildren<Renderer>(true);
            // H2c feature contract: four walls, three gate boxes and two chevron boxes per route goal.
            Assert.That(renderers, Has.Length.EqualTo(17), "Four walls, three gate boxes and two boxes per route goal.");
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
            int enabled = 0;
            foreach (var collider in handle.Root.GetComponentsInChildren<Collider>(true))
                if (collider.enabled)
                {
                    var wall = collider as BoxCollider;
                    Assert.That(wall != null && Array.IndexOf(handle.Walls, wall) >= 0, Is.True,
                        "Only the four perimeter walls may collide.");
                    Assert.That(collider.isTrigger, Is.False);
                    Assert.That(collider.attachedRigidbody, Is.Null);
                    enabled++;
                }
            Assert.That(enabled, Is.EqualTo(4), "Decorative primitive colliders must be disabled immediately.");
            if (parented)
            {
                VectorNear(_parent.transform.position, parentPosition);
                Assert.That(Quaternion.Angle(_parent.transform.rotation, parentRotation), Is.LessThan(0.001f));
                Assert.That(_parent.transform.childCount, Is.EqualTo(2));
                Assert.That(_sentinel.transform.parent, Is.SameAs(_parent.transform));
            }
        }

        [Test]
        public void Walls_EncloseExactlyTheRacingBoundsWithTheirInnerFacesOnTheSurface()
        {
            var handle = Create(Layout);
            RequireHandle(handle);
            var found = new int[4]; // west, east, south, north
            foreach (var wall in handle.Walls)
            {
                Assert.That(wall.enabled, Is.True);
                Assert.That(wall.isTrigger, Is.False);
                WorldBounds(wall, out var min, out var max);
                var size = max - min;
                Assert.That(min.y, Is.EqualTo(Layout.SurfaceY).Within(Tolerance), "Wall bases sit on the driving surface.");
                Assert.That(size.y, Is.EqualTo(Layout.WallHeight).Within(Tolerance));
                if (size.x < size.z)
                {
                    Assert.That(size.x, Is.EqualTo(Layout.WallThickness).Within(Tolerance));
                    Assert.That(size.z, Is.EqualTo(Layout.Size.y + Layout.WallThickness * 2f).Within(Tolerance),
                        "The long walls own the corners, so the enclosure has no gap.");
                    if (max.x < Layout.Center.x)
                    {
                        Assert.That(max.x, Is.EqualTo(Left).Within(Tolerance), "The inner face is the racing edge.");
                        found[0]++;
                    }
                    else
                    {
                        Assert.That(min.x, Is.EqualTo(Right).Within(Tolerance));
                        found[1]++;
                    }
                }
                else
                {
                    Assert.That(size.z, Is.EqualTo(Layout.WallThickness).Within(Tolerance));
                    Assert.That(size.x, Is.EqualTo(Layout.Size.x).Within(Tolerance));
                    Assert.That(min.x, Is.EqualTo(Left).Within(Tolerance));
                    Assert.That(max.x, Is.EqualTo(Right).Within(Tolerance));
                    if (max.z < Layout.Center.y)
                    {
                        Assert.That(max.z, Is.EqualTo(Bottom).Within(Tolerance));
                        found[2]++;
                    }
                    else
                    {
                        Assert.That(min.z, Is.EqualTo(Top).Within(Tolerance));
                        found[3]++;
                    }
                }
            }
            Assert.That(found, Is.EqualTo(new[] { 1, 1, 1, 1 }), "One wall per side, and no side doubled.");
        }

        [Test]
        public void StartGate_SpansTheRacingWidthAndNeverBlocksTheLine()
        {
            var handle = Create(Layout);
            RequireHandle(handle);
            var gate = handle.StartGate;
            int enabled = 0;
            foreach (var collider in gate.GetComponentsInChildren<Collider>(true))
                if (collider.enabled)
                    enabled++;
            Assert.That(enabled, Is.EqualTo(0), "The gate is decorative: it must never block the racing line.");
            var renderers = gate.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(3), "Two legs and one crossbar.");
            int legs = 0, beams = 0;
            Bounds beam = default;
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                Assert.That(bounds.center.z, Is.EqualTo(Layout.Center.y).Within(Tolerance), "The gate stands on the start line.");
                if (Mathf.Abs(bounds.min.y - Layout.SurfaceY) < Tolerance)
                {
                    legs++;
                }
                else
                {
                    beams++;
                    beam = bounds;
                }
            }
            Assert.That(legs, Is.EqualTo(2), "Both legs stand on the surface.");
            Assert.That(beams, Is.EqualTo(1));
            Assert.That(beam.size.x, Is.EqualTo(Layout.Size.x - Layout.WallThickness).Within(Tolerance));
            Assert.That(beam.min.y, Is.GreaterThan(Layout.SurfaceY), "The crossbar hangs above the line, out of the car's way.");
        }

        [Test]
        public void SpawnPose_ClearsTheSurfaceInsideTheBoundsBehindTheLine()
        {
            var handle = Create(Layout);
            RequireHandle(handle);
            var spawn = handle.SpawnPose;
            Assert.That(spawn.position.y, Is.EqualTo(Layout.SurfaceY + Layout.SpawnHeight).Within(Tolerance));
            Assert.That(spawn.position.x, Is.InRange(Left, Right));
            Assert.That(spawn.position.z, Is.InRange(Bottom, Top));
            Assert.That(spawn.position.z, Is.LessThan(Layout.Center.y),
                "The car starts behind the start line and drives into it.");
            VectorNear(spawn.position, new Vector3(spawn.position.x, Layout.SurfaceY + Layout.SpawnHeight, spawn.position.z));
            VectorNear(spawn.rotation * Vector3.forward, Vector3.forward);
        }

        [Test]
        public void Create_InvalidLayoutRejectsBeforeAllocating()
        {
            var invalid = new[]
            {
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(0f, 48f), -0.35f),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(-24f, 48f), -0.35f),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(float.NaN, 48f), -0.35f),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(24f, float.PositiveInfinity), -0.35f),
                new GrayboxCircuitLayout(new Vector2(float.NaN, 0f), new Vector2(24f, 48f), -0.35f),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(24f, 48f), float.NegativeInfinity),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(24f, 48f), -0.35f, float.NaN),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(24f, 48f), -0.35f, 0.35f, 0f),
                new GrayboxCircuitLayout(Vector2.zero, new Vector2(24f, 48f), -0.35f, 0.35f, 1.2f, -0.5f),
            };
            foreach (var layout in invalid)
            {
                Assert.That(layout.IsValid, Is.False, layout.Size.ToString());
                var before = ResourcesNow();
                Assert.Throws<ArgumentException>(() => Create(layout, _parent.transform));
                Assert.That(ResourcesNow(), Is.EquivalentTo(before), "Rejection must not leak objects/materials.");
            }
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
            Assert.Throws<ArgumentException>(() => Create(Layout, inherited ? _sentinel.transform : _parent.transform));
            Assert.That(ResourcesNow(), Is.EquivalentTo(before), "Rejection must not leak objects/materials.");
        }

        [UnityTest]
        public IEnumerator Dispose_DisablesImmediately_DestroysOnceAfterFrame_AndPreservesExistingObjects()
        {
            var created = ResourcesNow();
            var handle = Create(Layout, _parent.transform);
            RequireHandle(handle);
            var afterCreation = ResourcesNow();
            afterCreation.ExceptWith(created);
            int materialCount = 0;
            foreach (var item in afterCreation)
                if (item is Material) materialCount++;
            Assert.That(materialCount, Is.EqualTo(1), "Exactly one new runtime material, including implicit clones.");
            var root = handle.Root;
            var material = handle.Material;
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
            foreach (var child in children) Assert.That(child == null, Is.True);
            foreach (var item in afterCreation) Assert.That(item == null, Is.True, "No owned resource leaks.");
            Assert.That(_parent != null && _sentinel != null, Is.True);
            VectorNear(_parent.transform.position, parentPosition);
            Assert.That(Quaternion.Angle(_parent.transform.rotation, parentRotation), Is.LessThan(0.001f));
            Assert.That(_parent.transform.childCount, Is.EqualTo(1));
            Assert.That(_sentinel.transform.parent, Is.SameAs(_parent.transform));
            Assert.That(handle.IsDisposed, Is.True);
            Assert.DoesNotThrow(handle.Dispose);
        }

        [UnityTest]
        public IEnumerator Create_TwoIndependentCircuits_DisposingOneDoesNotTouchTheOther()
        {
            var first = Create(Layout);
            RequireHandle(first);
            var second = Create(new GrayboxCircuitLayout(new Vector2(-20f, 40f), new Vector2(16f, 32f), 0f));
            RequireHandle(second);
            Assert.That(second.Root, Is.Not.SameAs(first.Root));
            Assert.That(second.Material, Is.Not.SameAs(first.Material));
            first.Dispose();
            yield return null;
            Assert.That(second.Root != null && second.Material != null, Is.True);
            Assert.That(second.Root.activeSelf, Is.True);
            Assert.That(second.IsDisposed, Is.False);
            Assert.That(second.Walls, Has.Length.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator FigureEight_OwnsRoadMeshAndDestroysItOnDispose()
        {
            var layout = new GrayboxCircuitLayout(new Vector2(3f, -5f), new Vector2(24f, 48f),
                -0.35f, figureEight: true);
            var handle = Create(layout, _parent.transform);
            RequireHandle(handle);
            var asphalt = handle.Root.transform.Find("Figure-eight asphalt");
            Assert.That(asphalt, Is.Not.Null, "The circuit must own a continuous asphalt surface.");
            var filter = asphalt.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            var mesh = filter.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            _cleanup.Add(mesh);
            Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(48));
            Assert.That(asphalt.GetComponent<Collider>(), Is.Null,
                "The existing floor remains the sole standing surface.");
            handle.Dispose();
            Assert.That(handle.Root.activeSelf, Is.False);
            yield return null;
            Assert.That(mesh == null, Is.True, "The generated road mesh must not leak after disposal.");
        }

        [Test]
        public void FigureEight_RouteHasOrderedFlatCrossoverAndCurvedLobes()
        {
            var layout = new GrayboxCircuitLayout(new Vector2(3f, -5f), new Vector2(24f, 48f),
                -0.35f, figureEight: true);
            var handle = Create(layout, _parent.transform);
            RequireHandle(handle);
            Assert.That(handle.RouteCount, Is.EqualTo(24), "A figure-eight needs 24 ordered goals, not the legacy rectangle's five.");
            var crossing = new Vector3(layout.Center.x, layout.SurfaceY, layout.Center.y);
            VectorNear(handle.GetRoutePose(0).position, crossing);
            VectorNear(handle.GetRoutePose(12).position, crossing);
            var firstDiagonal = handle.GetRoutePose(0).forward;
            var secondDiagonal = handle.GetRoutePose(12).forward;
            foreach (var diagonal in new[] { firstDiagonal, secondDiagonal })
            {
                Assert.That(Mathf.Abs(diagonal.x), Is.GreaterThan(0.1f));
                Assert.That(Mathf.Abs(diagonal.z), Is.GreaterThan(0.1f));
                Assert.That(diagonal.y, Is.EqualTo(0f).Within(Tolerance));
            }
            Assert.That(Vector3.Cross(firstDiagonal, secondDiagonal).magnitude, Is.GreaterThan(0.5f),
                "The flat crossover must have two nonparallel outgoing branches.");
            float laneMargin = 0.1f * Mathf.Min(layout.Size.x, layout.Size.y);
            bool positiveZ = false, negativeZ = false, positiveX = false, negativeX = false;
            for (int i = 0; i < handle.RouteCount; i++)
            {
                var pose = handle.GetRoutePose(i);
                float t = 2f * Mathf.PI * i / 24f;
                var expected = new Vector3(layout.Center.x + 0.32f * layout.Size.x * Mathf.Sin(2f * t),
                    layout.SurfaceY, layout.Center.y + 0.375f * layout.Size.y * Mathf.Sin(t));
                VectorNear(pose.position, expected);
                var outgoing = (handle.GetRoutePose((i + 1) % handle.RouteCount).position - pose.position).normalized;
                Assert.That(Vector3.Dot(pose.forward, outgoing), Is.GreaterThan(0.999f),
                    $"Figure-eight goal {i} must face the next ordered goal, even at the crossing.");
                Assert.That(pose.position.x, Is.InRange(layout.Center.x - layout.Size.x * 0.5f + laneMargin,
                    layout.Center.x + layout.Size.x * 0.5f - laneMargin));
                Assert.That(pose.position.z, Is.InRange(layout.Center.y - layout.Size.y * 0.5f + laneMargin,
                    layout.Center.y + layout.Size.y * 0.5f - laneMargin));
                positiveZ |= pose.position.z > layout.Center.y + Tolerance;
                negativeZ |= pose.position.z < layout.Center.y - Tolerance;
                positiveX |= pose.position.x > layout.Center.x + Tolerance;
                negativeX |= pose.position.x < layout.Center.x - Tolerance;
            }
            Assert.That(positiveZ && negativeZ && positiveX && negativeX, Is.True,
                "Both curved lobes must visit both sides of the floor center.");
            Assert.That(handle.Walls.Length, Is.GreaterThan(4), "Curved barriers must replace the four-wall racing rectangle.");
        }

        [UnityTest]
        public IEnumerator Create_OrderedRouteMarkers_AreNonCollidableAndOwned()
        {
            // Parented and translated: the route must derive world-space geometry from the actual
            // walls, never from the parent pose or from duplicated floor constants.
            var handle = Create(Layout, _parent.transform);
            RequireHandle(handle);
            yield return RouteContract(handle);

            // A second, different valid layout proves the corner insets are derived, not hard-coded.
            var other = Create(new GrayboxCircuitLayout(new Vector2(-20f, 40f), new Vector2(16f, 32f), 0f));
            RequireHandle(other);
            yield return RouteContract(other);
        }

        private IEnumerator RouteContract(OwnedGrayboxCircuit handle)
        {
            // FIRST observable assertion: the marker root does not exist before H2c lands.
            var markers = handle.Root.transform.Find("Route markers");
            Assert.That(markers, Is.Not.Null,
                "A created circuit must own its ordered route markers under 'Route markers'.");

            // The route data is reached only through reflection so a missing API fails as RED
            // here, never as a compile error. The cached poses stay private: no public Pose[].
            var circuitType = typeof(OwnedGrayboxCircuit);
            var routeCountProperty = circuitType.GetProperty("RouteCount");
            var getRoutePose = circuitType.GetMethod("GetRoutePose");
            Assert.That(routeCountProperty, Is.Not.Null, "OwnedGrayboxCircuit.RouteCount is part of the H2c contract.");
            Assert.That(routeCountProperty.PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(getRoutePose, Is.Not.Null, "OwnedGrayboxCircuit.GetRoutePose(int) is part of the H2c contract.");
            Assert.That(getRoutePose.ReturnType, Is.EqualTo(typeof(Pose)));
            Assert.That(getRoutePose.IsStatic, Is.False, "The route is per circuit instance.");
            foreach (var property in circuitType.GetProperties())
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(Pose[])),
                    "The route stays an immutable private cache, never a public mutable Pose[].");
            foreach (var field in circuitType.GetFields())
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(Pose[])),
                    "The route stays an immutable private cache, never a public mutable Pose[].");
            int count = (int)routeCountProperty.GetValue(handle);
            Assert.That(count, Is.EqualTo(5), "Bottom-right, bottom-left, top-left, top-right, finish.");

            // Derive the inner wall faces from the actual wall geometry, as the walls test does.
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (var wall in handle.Walls)
            {
                WorldBounds(wall, out var min, out var max);
                if (max.x - min.x < max.z - min.z)
                {
                    if (max.x < handle.Layout.Center.x) left = max.x; else right = min.x;
                }
                else
                {
                    if (max.z < handle.Layout.Center.y) bottom = max.z; else top = min.z;
                }
            }
            Assert.That(left < right && bottom < top, Is.True, "One inner face per side must be found.");

            // The remaining 35% of each racing half extent is the margin from its inner wall face.
            float insetX = (1f - CornerRadiusFraction) * (right - left) * 0.5f;
            float insetZ = (1f - CornerRadiusFraction) * (top - bottom) * 0.5f;
            var surfaceY = handle.Layout.SurfaceY;
            var expected = new[]
            {
                new Vector3(right - insetX, surfaceY, bottom + insetZ),
                new Vector3(left + insetX, surfaceY, bottom + insetZ),
                new Vector3(left + insetX, surfaceY, top - insetZ),
                new Vector3(right - insetX, surfaceY, top - insetZ),
                new Vector3(right - insetX, surfaceY, handle.Layout.Center.y),
            };

            var poses = new Pose[count];
            for (int i = 0; i < count; i++)
            {
                poses[i] = (Pose)getRoutePose.Invoke(handle, new object[] { i });
                VectorNear(poses[i].position, expected[i]);
                Assert.That(poses[i].position.y, Is.EqualTo(surfaceY).Within(Tolerance),
                    "Route poses ride on the driving surface.");
                var direction = (expected[(i + 1) % count] - expected[i]).normalized;
                Assert.That(Vector3.Dot(poses[i].forward, direction), Is.GreaterThan(0.999f),
                    $"Route pose {i} must face the next goal; the finish faces bottom-right.");
            }

            Assert.That(markers.name, Is.EqualTo("Route markers"), "The marker root name is frozen contract.");
            Assert.That(markers.parent, Is.SameAs(handle.Root.transform));
            Assert.That(markers.childCount, Is.EqualTo(count), "One goal marker per route pose.");
            Assert.That(markers.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(2 * count),
                "Two chevron boxes per goal.");
            var markerColliders = new List<Collider>();
            for (int i = 0; i < markers.childCount; i++)
            {
                var goal = markers.GetChild(i);
                Assert.That(goal.position.x, Is.EqualTo(poses[i].position.x).Within(Tolerance));
                Assert.That(goal.position.z, Is.EqualTo(poses[i].position.z).Within(Tolerance));
                foreach (var renderer in goal.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterial, Is.SameAs(handle.Material),
                        "Markers share the circuit material.");
                    var bounds = renderer.bounds;
                    Assert.That(bounds.min.y, Is.EqualTo(surfaceY).Within(Tolerance),
                        "Marker boxes stand on the driving surface.");
                    Assert.That(bounds.size.y, Is.EqualTo(MarkerBoxHeight).Within(Tolerance),
                        "Marker boxes keep the low readable arrow profile.");
                    Assert.That(bounds.extents.x, Is.GreaterThan(0f));
                    Assert.That(bounds.extents.z, Is.GreaterThan(0f), "The chevron must stay visible from above.");
                }
                markerColliders.AddRange(goal.GetComponentsInChildren<Collider>(true));
            }
            Assert.That(markerColliders, Has.Count.EqualTo(2 * count));
            foreach (var collider in markerColliders)
                Assert.That(collider.enabled, Is.False, "Markers are presentation only: no physics, ever.");

            // Existing lifecycle contract, applied to the marker subtree.
            var root = handle.Root;
            var material = handle.Material;
            var children = markers.GetComponentsInChildren<Transform>(true);
            handle.Dispose();
            Assert.That(handle.IsDisposed, Is.True);
            Assert.That(root.activeSelf, Is.False, "Destroy is deferred, so activity must stop synchronously.");
            Assert.DoesNotThrow(handle.Dispose);
            yield return null;
            Assert.That(root == null, Is.True);
            Assert.That(material == null, Is.True);
            Assert.That(markers == null, Is.True);
            foreach (var child in children) Assert.That(child == null, Is.True);
            foreach (var collider in markerColliders) Assert.That(collider == null, Is.True);
        }

        [Test]
        public void Create_ChevronTips_PointTowardNextGoal()
        {
            var handle = Create(Layout);
            RequireHandle(handle);
            var markers = handle.Root.transform.Find("Route markers");
            Assert.That(markers, Is.Not.Null, "H2c chevron regression needs the 'Route markers' root.");
            for (int i = 0; i < markers.childCount; i++)
            {
                var goal = markers.GetChild(i);
                var nextIndex = (i + 1) % markers.childCount;
                var next = markers.GetChild(nextIndex);
                var wings = goal.GetComponentsInChildren<Renderer>(true);
                Assert.That(wings, Has.Length.EqualTo(2), $"Goal {i} must own exactly two chevron wing boxes.");
                var direction = next.position - goal.position;
                Assert.That(direction.y, Is.EqualTo(0f).Within(Tolerance),
                    $"Goal {i} to goal {nextIndex} must stay a yaw-only leg on the driving surface.");
                var forward = direction.normalized;
                foreach (var wing in wings)
                {
                    // CreateBox scales a unit cube, so the renderer's own box is the wing geometry.
                    var scale = wing.transform.lossyScale;
                    Assert.That(scale.y, Is.EqualTo(MarkerBoxHeight).Within(Tolerance),
                        $"Wing '{wing.name}' keeps the low marker profile.");
                    Assert.That(scale.z, Is.GreaterThan(scale.x).And.GreaterThan(scale.y),
                        $"Wing '{wing.name}' must be long along local Z, the chevron axis.");
                }
                var frontLeft = wings[0].transform.TransformPoint(Vector3.forward * 0.5f);
                var frontRight = wings[1].transform.TransformPoint(Vector3.forward * 0.5f);
                var rearLeft = wings[0].transform.TransformPoint(Vector3.back * 0.5f);
                var rearRight = wings[1].transform.TransformPoint(Vector3.back * 0.5f);
                var frontGap = Vector3.Distance(frontLeft, frontRight);
                var rearGap = Vector3.Distance(rearLeft, rearRight);
                Assert.That(frontGap, Is.LessThan(rearGap * 0.1f),
                    $"Goal {i}: chevron tips must converge ahead (front gap {frontGap:F4} vs rear gap {rearGap:F4}); " +
                    "the visual arrow must point toward the next goal, not backward.");
                var frontMid = (frontLeft + frontRight) * 0.5f;
                var rearMid = (rearLeft + rearRight) * 0.5f;
                Assert.That(Vector3.Dot(frontMid - goal.position, forward), Is.GreaterThan(0f),
                    $"Goal {i}: the converged apex must sit ahead along the actual next-goal direction.");
                Assert.That(Vector3.Dot(rearMid - goal.position, forward), Is.LessThan(0f),
                    $"Goal {i}: the open rear must sit behind the goal.");
            }
        }
    }
}
