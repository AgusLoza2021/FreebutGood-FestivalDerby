using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Play-mode coverage for the loader: a baked map must become a drivable stadium — one ground
    /// collider under every sample, barrier boxes down both sides and a starting grid that sits on
    /// the asphalt. The fixture is built in memory rather than loaded from an asset, so the test
    /// runs on a fresh checkout without anyone having opened the bake menu.
    /// </summary>
    public class MapLoaderPlayModeTests
    {
        private const float Radius = 25f;
        private const float Width = 16f;
        private const int Rings = 64;
        private const float EdgeOffset = 0.5f;
        private const float EdgeThickness = 1f;
        private const float EdgeHeight = 1.2f;

        private readonly List<Object> _spawned = new List<Object>();
        private LoadedMap _loaded;

        [TearDown]
        public void TearDown()
        {
            if (_loaded != null)
            {
                _loaded.Dispose();
                _loaded = null;
            }
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Object.Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator Load_BuildsOneGroundColliderForTheWholeRibbon()
        {
            _loaded = MapLoader.Load(BuildCircleMap());
            Physics.SyncTransforms();
            yield return null;

            Assert.IsNotNull(_loaded.RoadMesh, "The road ribbon should be built.");
            Assert.AreEqual(Rings * 2, _loaded.RoadMesh.vertexCount,
                "One vertex pair per baked sample, so the ribbon is as dense as the bake chose.");

            var colliders = _loaded.Root.GetComponentsInChildren<MeshCollider>();
            Assert.AreEqual(1, colliders.Length, "Exactly one ribbon collider, so corners have no seams.");
            Assert.IsTrue(colliders[0].enabled, "The ground collider must take part in physics.");
        }

        [UnityTest]
        public IEnumerator Ground_RaycastHitsBeneathEverySample()
        {
            _loaded = MapLoader.Load(BuildCircleMap());
            Physics.SyncTransforms();
            yield return null;

            BakedPath path = _loaded.MainPath;
            Assert.Greater(path.Count, 0);
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 point = PathConvert.ToVector3(path.Positions[i]);
                var origin = new Vector3(point.x, point.y + 5f, point.z);
                Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f),
                    "No ground under sample " + i + " at " + point + ".");
                Assert.AreEqual(point.y, hit.point.y, 0.25f,
                    "The ground under sample " + i + " should sit on the path surface.");
            }
        }

        [UnityTest]
        public IEnumerator Edges_StandOnBothSidesOutsideTheAsphalt()
        {
            _loaded = MapLoader.Load(BuildCircleMap());
            Physics.SyncTransforms();
            yield return null;

            BakedPath path = _loaded.MainPath;
            float halfWidth = Width * 0.5f;
            var sides = new HashSet<int>();
            BoxCollider[] walls = _loaded.Walls;
            Assert.Greater(walls.Length, 0, "The map should place barrier boxes.");
            for (int i = 0; i < walls.Length; i++)
            {
                Vector3 center = walls[i].bounds.center;
                // No previous answer exists to seed the windowed search with, so it must fall back to a
                // full scan. A zero hint would read this barrier against the start of the lap and
                // measure a lateral that belongs to another part of the loop.
                int hint = -1;
                Assert.IsTrue(path.Sample(path.NearestDistance(PathConvert.ToPathVector(center), ref hint),
                    out PathSample sample));

                Vector3 right = PathConvert.Right(sample.Tangent);
                float lateral = Vector3.Dot(center - PathConvert.ToVector3(sample.Position), right);
                Assert.Greater(Mathf.Abs(lateral), halfWidth,
                    "A barrier must clear the asphalt edge, not sit on it.");
                sides.Add(lateral > 0f ? 1 : -1);
            }

            Assert.AreEqual(2, sides.Count, "Both sides of the road need barriers.");
        }

        [UnityTest]
        public IEnumerator Spawns_SitOnTheGroundAndInsideTheWalls()
        {
            _loaded = MapLoader.Load(BuildCircleMap());
            Physics.SyncTransforms();
            yield return null;

            Assert.GreaterOrEqual(_loaded.SpawnPoses.Length, 2, "The grid should have a full front row.");
            BakedPath path = _loaded.MainPath;
            for (int i = 0; i < _loaded.SpawnPoses.Length; i++)
            {
                Vector3 position = _loaded.SpawnPoses[i].position;
                var origin = new Vector3(position.x, position.y + 3f, position.z);
                Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f),
                    "No ground under spawn " + i + ".");
                Assert.Less(hit.point.y, position.y, "A spawn should hover just above the asphalt.");

                // Same cold query as the barrier test: no hint to seed the fixed window with.
                int hint = -1;
                float distance = path.NearestDistance(PathConvert.ToPathVector(position), ref hint);
                path.Sample(distance, out PathSample sample);
                float lateral = Mathf.Abs(Vector3.Dot(
                    position - PathConvert.ToVector3(sample.Position), PathConvert.Right(sample.Tangent)));
                Assert.Less(lateral, Width * 0.5f, "A spawn must fit inside the asphalt.");
            }
        }

        [UnityTest]
        public IEnumerator Route_FollowsTheRacingLineInOrder()
        {
            _loaded = MapLoader.Load(BuildCircleMap());
            yield return null;

            Assert.Greater(_loaded.RoutePoses.Length, 3, "A 157 m lap needs several ordered goals.");
            for (int i = 0; i < _loaded.RoutePoses.Length; i++)
            {
                Vector3 position = _loaded.RoutePoses[i].position;
                float radius = new Vector2(position.x, position.z).magnitude;
                Assert.AreEqual(Radius, radius, 0.5f, "Every route goal should ride the centerline.");
            }
        }

        [UnityTest]
        public IEnumerator CollidersOnly_KeepsPhysicsAndDropsRenderers()
        {
            _loaded = MapLoader.Load(BuildCircleMap(), collidersOnly: true);
            Physics.SyncTransforms();
            yield return null;

            var colliders = _loaded.Root.GetComponentsInChildren<MeshCollider>();
            Assert.AreEqual(1, colliders.Length,
                "A headless consumer still needs the ground collider.");
            Assert.IsNull(colliders[0].gameObject.GetComponent<MeshFilter>(),
                "Colliders-only must not build render geometry for the ground.");

            MeshRenderer[] renderers = _loaded.Root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                Assert.IsFalse(renderers[i].enabled, "Nothing should draw in a colliders-only load.");
        }

        // ------------------------------------------------------------- fixture

        /// <summary>
        /// A closed circle in memory: the same shape the spline bake produces for a rounded oval,
        /// minus the spline package.
        /// </summary>
        private MapAsset BuildCircleMap()
        {
            BakedPath path = BuildCirclePath(Radius, Rings, Width);

            var road = ScriptableObject.CreateInstance<RoadAsset>();
            road.RoadId = "main";
            road.Path = path;
            road.Width = Width;

            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.MapId = "TestCircle";
            map.Roads = new[] { road };
            map.Edges = new[]
            {
                BuildEdge(path, EdgeSide.Left, "left"),
                BuildEdge(path, EdgeSide.Right, "right"),
            };
            map.RacingLine = path.Clone();
            map.SpawnRoadIndex = 0;
            map.SpawnColumns = 3;
            map.SpawnRows = 2;
            map.SpawnSpacing = 4.5f;
            map.SpawnBackOffset = 4.5f;
            map.SpawnHeight = 0.35f;
            map.WallHeight = EdgeHeight;
            map.WallThickness = 0.5f;
            map.RouteStep = 10f;
            map.CheckpointDistances = new[] { 0f, path.TotalLength * 0.5f };
            map.SupportedModes = new[] { 0, 2 };
            map.Sanitize();
            _spawned.Add(map);
            _spawned.Add(road);
            _spawned.AddRange(map.Edges);
            return map;
        }

        private static BakedPath BuildCirclePath(float radius, int rings, float width)
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[rings],
                Tangents = new PathVector3[rings],
                Ups = new PathVector3[rings],
                WidthLeft = new float[rings],
                WidthRight = new float[rings],
                Banking = new float[rings],
                Distances = new float[rings],
                Closed = true,
            };

            for (int i = 0; i < rings; i++)
            {
                float angle = 2f * Mathf.PI * i / rings;
                var position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                path.Positions[i] = PathConvert.ToPathVector(position);
                path.Tangents[i] = PathConvert.ToPathVector(tangent);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = width * 0.5f;
                path.WidthRight[i] = width * 0.5f;
                path.Distances[i] = 2f * Mathf.PI * radius * i / rings;
            }
            path.TotalLength = 2f * Mathf.PI * radius;
            path.ComputeCurvature();
            return path;
        }

        private static EdgeAsset BuildEdge(BakedPath path, EdgeSide side, string id)
        {
            int segments = Mathf.Max(1, Mathf.RoundToInt(path.TotalLength / 3f));
            float step = path.TotalLength / segments;
            float lateral = Width * 0.5f + EdgeOffset + EdgeThickness * 0.5f;
            float sign = side == EdgeSide.Left ? -1f : 1f;

            var edge = ScriptableObject.CreateInstance<EdgeAsset>();
            edge.EdgeId = id;
            edge.Side = side;
            edge.Height = EdgeHeight;
            edge.Thickness = EdgeThickness;
            edge.SegmentLength = step;
            edge.Overlap = 0.3f;
            edge.Segments = new BakedEdgeSegment[segments];
            for (int i = 0; i < segments; i++)
            {
                path.Sample(i * step, out PathSample a);
                path.Sample(path.Wrap(i * step + step), out PathSample b);
                Vector3 start = PathConvert.ToVector3(a.Position);
                Vector3 end = PathConvert.ToVector3(b.Position);
                Vector3 center = (start + end) * 0.5f + PathConvert.Right(a.Tangent) * (sign * lateral);
                center.y = EdgeHeight * 0.5f;
                edge.Segments[i] = new BakedEdgeSegment
                {
                    Center = PathConvert.ToPathVector(center),
                    Size = new PathVector3(EdgeThickness, EdgeHeight, (end - start).magnitude + 0.3f),
                    YawDegrees = Mathf.Atan2((end - start).x, (end - start).z) * Mathf.Rad2Deg,
                };
            }
            return edge;
        }
    }
}
