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
    /// <summary>
    /// PlayMode contract for the config-driven big track: the geometry the layout numbers produce,
    /// and the null-track fallback that keeps the inherited figure-eight. Editor construction and
    /// lifetime only; run FestivalDerby.Driving.PlayModeTests.
    /// </summary>
    public sealed class TrackLayoutDefinitionPlayModeTests
    {
        private const float Tolerance = 0.05f;
        // A barrier box spans the chord between two centreline samples, so its midpoint sits a
        // sagitta inside the arc: measured worst offset is 0.29 m on the inner edge and 0.55 m on
        // the outer, both on a 25 m corner. 0.75 leaves margin for a re-tuned radius while still
        // catching a ribbon placed at half or double the intended offset.
        private const float WallLateralTolerance = 0.75f;
        private const int CenterlineProbes = 2048;

        private CarDefinition _definition;
        private GameObject _parent;
        private Transform _anchor;
        private readonly List<OwnedGrayboxCircuit> _circuits = new List<OwnedGrayboxCircuit>();
        private readonly List<OwnedCarGeometry> _cars = new List<OwnedCarGeometry>();
        private readonly List<Object> _cleanup = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<CarDefinition>();
            _cleanup.Add(_definition);
            _parent = new GameObject("D16 track test parent");
            _cleanup.Add(_parent);
            _anchor = _parent.transform;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var circuit in _circuits)
                circuit.Dispose();
            foreach (var car in _cars)
                car.Dispose();
            foreach (var item in _cleanup)
                if (item != null)
                    Object.Destroy(item);
            _circuits.Clear();
            _cars.Clear();
            _cleanup.Clear();
            yield return null;
        }

        [Test]
        public void DefaultOval_BuildsTheFullRibbonWithBarriersOnBothEdges()
        {
            var settings = TrackLayoutSettings.Defaults;
            var circuit = Build(settings, out var geometry);

            Assert.That(circuit.Layout.IsFigureEight, Is.False);
            Assert.That(circuit.RouteCount, Is.EqualTo(48), "One ordered route pose per centerline sample.");
            Assert.That(circuit.HasSpawnGrid, Is.True);
            Assert.That(circuit.SpawnPoseCount, Is.EqualTo(6));
            // One barrier per sample step on each of the two ribbon edges.
            Assert.That(circuit.Walls.Length, Is.EqualTo(96), "48 steps on each of the inner and outer edge.");

            int inner = 0, outer = 0;
            foreach (var wall in circuit.Walls)
            {
                Assert.That(wall.enabled, Is.True);
                Assert.That(wall.isTrigger, Is.False);
                Assert.That(wall.attachedRigidbody, Is.Null, "The circuit never simulates.");
                var midpoint = wall.transform.TransformPoint(wall.center);
                float lateral = NearestCenterline(settings, circuit.Layout, midpoint, out _, out _);
                Assert.That(Mathf.Abs(lateral - settings.TrackWidth * 0.5f),
                    Is.LessThan(WallLateralTolerance), $"Wall midpoint {midpoint} is off the ribbon edge.");
                float signed = SignedLateral(settings, circuit.Layout, midpoint);
                if (signed > 0f) outer++; else inner++;
            }
            Assert.That(inner, Is.GreaterThan(0), "The inner edge must be walled too.");
            Assert.That(outer, Is.GreaterThan(0));

            var asphalt = circuit.Root.transform.Find("Oval asphalt");
            Assert.That(asphalt, Is.Not.Null);
            Assert.That(asphalt.GetComponent<Collider>(), Is.Null,
                "The floor stays the sole standing surface; asphalt never collides.");
            Assert.That(geometry.Floor, Is.Not.Null);
        }

        [Test]
        public void Floor_CoversTheAsphaltOuterBoundsAndTheRunOff()
        {
            var settings = TrackLayoutSettings.Defaults;
            var circuit = Build(settings, out var geometry);
            var floor = geometry.Floor.bounds;
            var center = circuit.Layout.Center;

            Assert.That(floor.size.x, Is.EqualTo(settings.SurfaceSize.x).Within(Tolerance));
            Assert.That(floor.size.z, Is.EqualTo(settings.SurfaceSize.y).Within(Tolerance));
            Assert.That(floor.size.y, Is.EqualTo(0.5f).Within(Tolerance));

            float asphaltHalfX = 0.5f * (settings.CenterlineSize.x + settings.TrackWidth);
            float asphaltHalfZ = 0.5f * (settings.CenterlineSize.y + settings.TrackWidth);
            Assert.That(floor.min.x, Is.LessThanOrEqualTo(center.x - asphaltHalfX));
            Assert.That(floor.max.x, Is.GreaterThanOrEqualTo(center.x + asphaltHalfX));
            Assert.That(floor.min.z, Is.LessThanOrEqualTo(center.y - asphaltHalfZ));
            Assert.That(floor.max.z, Is.GreaterThanOrEqualTo(center.y + asphaltHalfZ));
            Assert.That(floor.max.y, Is.EqualTo(circuit.Layout.SurfaceY).Within(Tolerance),
                "The floor's top face is the surface the route rides on.");
        }

        [Test]
        public void RoutePoses_RideOnTheAsphaltAndLandOnTheFloor()
        {
            var settings = TrackLayoutSettings.Defaults;
            var circuit = Build(settings, out _);
            Physics.SyncTransforms();

            for (int i = 0; i < circuit.RouteCount; i++)
            {
                var pose = circuit.GetRoutePose(i);
                Assert.That(pose.position.y, Is.EqualTo(circuit.Layout.SurfaceY).Within(Tolerance));
                float lateral = NearestCenterline(settings, circuit.Layout, pose.position, out _, out _);
                Assert.That(lateral, Is.LessThan(0.5f * settings.TrackWidth),
                    $"Route pose {i} must sit on the asphalt ribbon.");
                Assert.That(RaycastFloor(pose.position, circuit.Layout.SurfaceY), Is.True,
                    $"Route pose {i} must land on the driving floor.");
            }
        }

        [Test]
        public void SpawnGrid_PlacesAFittingCarOnTheTrackAtEverySlot()
        {
            var settings = TrackLayoutSettings.Defaults;
            var circuit = Build(settings, out _);
            Physics.SyncTransforms();

            // Raycast the surface at each slot before any car can block its own probe.
            for (int i = 0; i < circuit.SpawnPoseCount; i++)
            {
                var pose = circuit.GetSpawnPose(i);
                Assert.That(RaycastFloor(pose.position, circuit.Layout.SurfaceY), Is.True,
                    $"Grid slot {i} must stand on the driving floor.");
                Assert.That(NearestCenterline(settings, circuit.Layout, pose.position, out _, out _),
                    Is.LessThan(0.5f * settings.TrackWidth));
            }

            float halfTrack = 0.5f * settings.TrackWidth;
            for (int i = 0; i < circuit.SpawnPoseCount; i++)
            {
                var car = CarGeometryFactory.CreateWithoutFloor(_definition, circuit.GetSpawnPose(i), _anchor);
                _cars.Add(car);
                var bounds = car.Chassis.bounds;
                // The AABB of a yaw-rotated chassis is conservative, so fitting it proves the car fits.
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        var corner = new Vector3(bounds.center.x + cx * bounds.extents.x,
                            circuit.Layout.SurfaceY, bounds.center.z + cz * bounds.extents.z);
                        float lateral = NearestCenterline(settings, circuit.Layout, corner, out _, out _);
                        Assert.That(lateral, Is.LessThan(halfTrack),
                            $"A car at grid slot {i} would clip the barrier.");
                    }
            }
        }

        [Test]
        public void NullTrackPath_KeepsTheInheritedFigureEightAndItsFloor()
        {
            // This is the shipped state: with no referenced layout the run must not build the oval.
            var handle = CarGeometryFactory.Create(_definition,
                new Pose(new Vector3(0f, 2f, 0f), Quaternion.identity), _anchor);
            _cars.Add(handle);
            Assert.That(handle.Floor, Is.Not.Null);
            Assert.That(handle.Floor.bounds.size.x, Is.EqualTo(24f).Within(Tolerance));
            Assert.That(handle.Floor.bounds.size.z, Is.EqualTo(48f).Within(Tolerance));

            var bounds = handle.Floor.bounds;
            var layout = new GrayboxCircuitLayout(
                new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(bounds.size.x, bounds.size.z),
                bounds.max.y,
                handle.Chassis.bounds.extents.y,
                figureEight: true);
            var circuit = GrayboxCircuitFactory.Create(layout, _anchor);
            _circuits.Add(circuit);

            Assert.That(circuit.Layout.IsFigureEight, Is.True);
            Assert.That(circuit.RouteCount, Is.EqualTo(24));
            Assert.That(circuit.HasSpawnGrid, Is.False);
            Assert.That(circuit.SpawnPoseCount, Is.EqualTo(1));
        }

        [Test]
        public void CreateOval_RejectsBrokenLayoutsBeforeAllocating()
        {
            var settings = TrackLayoutSettings.Defaults;
            var invalidLayout = new GrayboxCircuitLayout(Vector2.zero, new Vector2(0f, 48f), 0f);
            Assert.That(invalidLayout.IsValid, Is.False);
            Assert.Throws<ArgumentException>(() => GrayboxCircuitFactory.CreateOval(invalidLayout, settings));

            var invalidSettings = default(TrackLayoutSettings);
            Assert.That(invalidSettings.IsValid, Is.False);
            var layout = new GrayboxCircuitLayout(Vector2.zero, new Vector2(116f, 176f), 0f);
            Assert.Throws<ArgumentException>(() => GrayboxCircuitFactory.CreateOval(layout, invalidSettings));

            // A figure-eight settings value is not a rounded oval, so it bounces too.
            var figureEight = new TrackLayoutSettings(TrackLayoutKind.FigureEight, 150f, 90f, 16f, 25f, 1.2f, 0.5f,
                5f, 48, new TrackSpawnGrid(3, 2, 4.5f), false, Vector3.zero, 2f);
            Assert.That(figureEight.IsValid, Is.True);
            Assert.Throws<ArgumentException>(() => GrayboxCircuitFactory.CreateOval(layout, figureEight));
        }

        private OwnedGrayboxCircuit Build(in TrackLayoutSettings settings, out OwnedCarGeometry geometry)
        {
            var spawn = new Pose(new Vector3(0f, 2f, 0f), Quaternion.identity);
            geometry = CarGeometryFactory.Create(_definition, spawn, _anchor, settings.SurfaceSize);
            _cars.Add(geometry);

            var bounds = geometry.Floor.bounds;
            var layout = new GrayboxCircuitLayout(
                new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(settings.SurfaceSize.x, settings.SurfaceSize.y),
                bounds.max.y,
                geometry.Chassis.bounds.extents.y,
                settings.WallHeight,
                settings.WallThickness);
            var circuit = GrayboxCircuitFactory.CreateOval(layout, settings, _anchor);
            _circuits.Add(circuit);
            return circuit;
        }

        private static bool RaycastFloor(Vector3 posePosition, float surfaceY)
        {
            var origin = new Vector3(posePosition.x, surfaceY + 2f, posePosition.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 6f);
            foreach (var hit in hits)
                if (hit.collider.attachedRigidbody == null
                    && Mathf.Abs(hit.point.y - surfaceY) < 0.06f)
                    return true;
            return false;
        }

        private static float SignedLateral(in TrackLayoutSettings settings, in GrayboxCircuitLayout layout,
            Vector3 world)
        {
            NearestCenterline(settings, layout, world, out var nearest, out var tangent);
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            return Vector3.Dot(world - nearest, right);
        }

        /// <summary>Distance from <paramref name="world"/> to the nearest centerline probe.</summary>
        private static float NearestCenterline(in TrackLayoutSettings settings, in GrayboxCircuitLayout layout,
            Vector3 world, out Vector3 nearest, out Vector3 tangent)
        {
            float lap = settings.CenterlineLapLength;
            float best = float.MaxValue;
            nearest = default;
            tangent = Vector3.forward;
            for (int i = 0; i < CenterlineProbes; i++)
            {
                settings.CenterlineSample(lap * i / CenterlineProbes, out var point, out var direction);
                var probe = new Vector3(layout.Center.x + point.x, layout.SurfaceY, layout.Center.y + point.y);
                float squared = (probe - world).sqrMagnitude;
                if (squared >= best)
                    continue;
                best = squared;
                nearest = probe;
                tangent = new Vector3(direction.x, 0f, direction.y);
            }
            return Mathf.Sqrt(best);
        }
    }
}
