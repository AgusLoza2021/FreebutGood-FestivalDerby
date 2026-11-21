using System.Collections.Generic;
using FestivalDerby.World.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the multi-road bake: the leading N splines become drivable roads, the rest stay
    /// free-standing barrier edges, and road 0 stays "main" so the runtime's PrimaryRoad routing is
    /// untouched. The bake goes through MapBaker, so the authoring numbers are proven to survive the
    /// real bake rather than a stub.
    /// </summary>
    [TestFixture]
    public sealed class MultiRoadBakeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void RoadCountOne_KeepsASingleMainRoadAndDemotesTheRestToEdges()
        {
            DerbyMapAuthoring authoring = CreateAuthoring(2);
            authoring.RoadCount = 1;

            MapAsset map = Bake(authoring);

            Assert.That(map.Roads.Length, Is.EqualTo(1));
            Assert.That(map.Roads[0].RoadId, Is.EqualTo("main"));
            Assert.That(map.PrimaryRoad, Is.SameAs(map.Roads[0]));
            // Two synthesized road sides plus the second spline as one free edge, which is the bake
            // every map authored before RoadCount existed produced.
            Assert.That(map.Edges.Length, Is.EqualTo(3));
            Assert.That(HasFreeEdge(map, "free_1"), Is.True);
        }

        [Test]
        public void RoadCountThree_BakesEveryLeadingSplineAsADrivableRoad()
        {
            DerbyMapAuthoring authoring = CreateAuthoring(3);
            authoring.RoadCount = 3;

            MapAsset map = Bake(authoring);

            Assert.That(map.Roads.Length, Is.EqualTo(3));
            Assert.That(map.Roads[0].RoadId, Is.EqualTo("main"));
            for (int i = 1; i < map.Roads.Length; i++)
            {
                Assert.That(map.Roads[i].RoadId, Is.EqualTo("road_" + i));
                Assert.That(map.Roads[i].Path, Is.Not.Null);
                Assert.That(map.Roads[i].Path.IsUsable, Is.True);
                Assert.That(map.Roads[i].Path.TotalLength, Is.GreaterThan(0f));
            }

            // The splines claimed as roads must not also be emitted as free barrier edges.
            Assert.That(map.Edges.Length, Is.EqualTo(2));
            Assert.That(HasFreeEdge(map, "free_1"), Is.False);
            Assert.That(HasFreeEdge(map, "free_2"), Is.False);
        }

        [Test]
        public void RoadCount_LargerThanTheSplineCountIsClampedToOneRoad()
        {
            DerbyMapAuthoring authoring = CreateAuthoring(1);
            authoring.RoadCount = 9;

            MapAsset map = Bake(authoring);

            Assert.That(map.Roads.Length, Is.EqualTo(1));
            Assert.That(map.Roads[0].RoadId, Is.EqualTo("main"));
        }

        private static bool HasFreeEdge(MapAsset map, string edgeId)
        {
            for (int i = 0; i < map.Edges.Length; i++)
            {
                if (map.Edges[i] != null && map.Edges[i].EdgeId == edgeId)
                {
                    return true;
                }
            }

            return false;
        }

        private MapAsset Bake(DerbyMapAuthoring authoring)
        {
            authoring.Sanitize();
            SplineContainer container = _host.GetComponent<SplineContainer>();
            return MapBaker.Bake(container, DerbyMapBake.SettingsFrom(authoring));
        }

        private DerbyMapAuthoring CreateAuthoring(int splineCount)
        {
            _host = new GameObject("MultiRoadBakeTests");
            var container = _host.AddComponent<SplineContainer>();
            // Splines is an IReadOnlyList, so the list is built first and assigned once: the setter
            // takes it whole, and a read-only view has no Add to grow in place.
            var splines = new List<Spline>(splineCount);
            for (int i = 0; i < splineCount; i++)
            {
                // Every spline is its own closed square, offset so each alternate road is a distinct
                // path that still bakes to a usable length.
                splines.Add(Square(20f + 4f * i, new Vector3(120f * i, 0f, 0f)));
            }

            container.Splines = splines;

            return _host.AddComponent<DerbyMapAuthoring>();
        }

        private static Spline Square(float size, Vector3 origin)
        {
            var knots = new float3[]
            {
                new float3(origin.x, origin.y, origin.z),
                new float3(origin.x + size, origin.y, origin.z),
                new float3(origin.x + size, origin.y, origin.z + size),
                new float3(origin.x, origin.y, origin.z + size),
            };
            return new Spline(knots, TangentMode.AutoSmooth, true);
        }
    }
}
