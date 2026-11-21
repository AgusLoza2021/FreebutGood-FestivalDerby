using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Play-mode coverage for the swept road: the loader must turn baked chunks into one collider and
    /// one renderer per chunk, the material must be a runtime clone of the baked one, and a car must
    /// cross a chunk seam without a vertical spike. The fixtures are built in memory so the suite runs
    /// on a fresh checkout with nobody having pressed Bake.
    /// </summary>
    public class RoadMeshPlayModeTests
    {
        private const float Radius = 25f;
        private const float Width = 16f;
        private const int Rings = 64;
        private static readonly float LoopLength = 2f * Mathf.PI * Radius;

        private readonly List<Object> _spawned = new List<Object>();
        private LoadedMap _loaded;
        private SimulationMode _previousSimulationMode;

        [SetUp]
        public void SetUp()
        {
            _previousSimulationMode = Physics.simulationMode;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _previousSimulationMode;
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
        public IEnumerator Loader_BuildsOneColliderAndRendererPerChunk()
        {
            MapAsset map = BuildChunkedMap(out RoadMeshChunk[] chunks);
            Assert.Greater(chunks.Length, 1, "The fixture must split into several chunks to test a seam.");

            _loaded = MapLoader.Load(map);
            Physics.SyncTransforms();
            yield return null;

            MeshCollider[] colliders = _loaded.Root.GetComponentsInChildren<MeshCollider>();
            Assert.AreEqual(chunks.Length, colliders.Length,
                "Every chunk needs its own ground collider; a missing one is a hole in the road.");
            for (int i = 0; i < colliders.Length; i++)
                Assert.IsTrue(colliders[i].enabled, "Chunk colliders must take part in physics.");

            int roadRenderers = 0;
            MeshRenderer[] renderers = _loaded.Root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].gameObject.name.StartsWith("Road "))
                    continue;
                roadRenderers++;
                Assert.AreSame(_loaded.Material, renderers[i].sharedMaterial,
                    "Every chunk draws with the map's runtime material.");
            }
            Assert.AreEqual(chunks.Length, roadRenderers, "Every chunk needs a renderer.");
        }

        /// <summary>
        /// LAN2 / mobile audit: the chunk meshes belong to the map asset. The loader used to hand them to
        /// a RoadMeshOwner (and the first one on as LoadedMap.RoadMesh, which the circuit destroys), and
        /// the asset guard only exists in the Editor, so in a player the first race's teardown destroyed
        /// the baked road and the second race loaded no road collider at all. Load, tear down, load again:
        /// the second road must still stand on live meshes.
        /// </summary>
        [UnityTest]
        public IEnumerator Loader_TearDownThenReload_KeepsTheChunkMeshesAndColliders()
        {
            MapAsset map = BuildChunkedMap(out RoadMeshChunk[] chunks);

            _loaded = MapLoader.Load(map);
            Assert.IsNull(_loaded.RoadMesh,
                "A chunked road must not hand an asset mesh on as the run-owned RoadMesh.");
            Assert.AreEqual(0, _loaded.Root.GetComponentsInChildren<RoadMeshOwner>(true).Length,
                "Nothing may take ownership of the asset's chunk meshes.");

            _loaded.Dispose();
            _loaded = null;
            // Destroy is deferred to the end of the frame, which is exactly when the old owner struck.
            yield return null;

            for (int i = 0; i < chunks.Length; i++)
                Assert.IsTrue(chunks[i].Mesh != null, "Tearing a map down must not destroy chunk mesh " + i + ".");

            _loaded = MapLoader.Load(map);
            Physics.SyncTransforms();
            yield return null;

            MeshCollider[] colliders = _loaded.Root.GetComponentsInChildren<MeshCollider>();
            Assert.AreEqual(chunks.Length, colliders.Length, "The second load must rebuild every road collider.");
            for (int i = 0; i < colliders.Length; i++)
                Assert.IsTrue(colliders[i].sharedMesh != null, "Road collider " + i + " lost its mesh.");
        }

        [UnityTest]
        public IEnumerator Loader_ClonesTheBakedMaterialInsteadOfOwningTheAsset()
        {
            MapAsset map = BuildChunkedMap(out _);
            map.RoadMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _spawned.Add(map.RoadMaterial);

            _loaded = MapLoader.Load(map);
            yield return null;

            Assert.IsNotNull(_loaded.Material, "The road needs a material.");
            Assert.AreNotSame(map.RoadMaterial, _loaded.Material,
                "The loader must clone the baked material: the circuit disposes what it is handed, and " +
                "destroying a project asset is data loss.");
            Assert.AreEqual(map.RoadMaterial.shader, _loaded.Material.shader);
        }

        [UnityTest]
        public IEnumerator Seam_DrivingAcrossItHasNoVerticalSpike()
        {
            MapAsset map = BuildChunkedMap(out RoadMeshChunk[] chunks);
            _loaded = MapLoader.Load(map);
            Physics.SyncTransforms();
            yield return null;

            BakedPath path = _loaded.MainPath;
            float seam = chunks[0].EndDistance;

            // The colliders must meet before anything drives over them: walking the physical ground
            // across the seam has to give a continuous height, not a step a wheel would trip on.
            float groundStep = MaxGroundStepAcrossSeam(path, seam);
            Assert.Less(groundStep, 0.02f,
                "The neighbouring chunk colliders do not meet: the ground steps by " +
                groundStep.ToString("0.000") + " m at the seam.");

            // Start short of the seam so the crossing really happens inside the measured window.
            Assert.True(path.Sample(path.Wrap(seam - 2f), out PathSample sample));
            Vector3 position = PathConvert.ToVector3(sample.Position);
            Vector3 tangent = PathConvert.ToVector3(sample.Tangent).normalized;

            var go = new GameObject("Seam car");
            _spawned.Add(go);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 1300f;
            body.sleepThreshold = 0f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.transform.position = position + new Vector3(0f, 0.42f, 0f);
            go.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            go.AddComponent<BoxCollider>().size = new Vector3(1.8f, 0.8f, 4f);

            // Drive physics by hand so the crossing is deterministic and instant instead of real-time.
            // The 40 steps settle the chassis onto the surface before it is set moving: what this
            // test measures is the seam, not the first landing.
            Physics.simulationMode = SimulationMode.Script;
            for (int i = 0; i < 40; i++)
                Physics.Simulate(0.02f);

            // Hold the forward speed constant so friction cannot park the chassis short of the seam.
            // The vertical velocity stays whatever physics made of it, which is the spike measured.
            float maxVertical = 0f;
            Vector3 start = body.position;
            for (int i = 0; i < 18; i++)
            {
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(tangent.x * 20f, velocity.y, tangent.z * 20f);
                Physics.Simulate(0.02f);
                maxVertical = Mathf.Max(maxVertical, Mathf.Abs(body.linearVelocity.y));
            }

            Assert.Greater(Vector3.Dot(body.position - start, tangent), 5f,
                "The car must actually cross the seam, not sit on it.");
            Assert.Less(maxVertical, 1.5f,
                "A vertical spike means the neighbouring chunk colliders do not meet: the car fell " +
                "into the seam. Max |vy| was " + maxVertical.ToString("0.00"));
            Assert.Greater(body.position.y, -1f, "The car must not fall through the road.");
            yield return null;
        }

        /// <summary>
        /// The largest vertical step the physical ground takes along the centreline across the seam.
        /// A hole between two chunk colliders shows up here either as a step or as a missing hit.
        /// </summary>
        private static float MaxGroundStepAcrossSeam(BakedPath path, float seam)
        {
            const float halfWindow = 4f;
            const float probeStep = 0.25f;
            float previous = float.NaN;
            float maxStep = 0f;
            for (float distance = seam - halfWindow; distance <= seam + halfWindow + 1e-3f; distance += probeStep)
            {
                Assert.True(path.Sample(path.Wrap(distance), out PathSample sample),
                    "The seam window must stay on the path, at arc length " + distance.ToString("0.0") + ".");
                Vector3 origin = PathConvert.ToVector3(sample.Position) + Vector3.up * 5f;
                Assert.True(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore),
                    "The road needs a collider on both sides of the seam, at arc length " +
                    distance.ToString("0.0") + ".");
                if (!float.IsNaN(previous))
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(hit.point.y - previous));
                previous = hit.point.y;
            }
            return maxStep;
        }

        // ------------------------------------------------------------- fixture

        private MapAsset BuildChunkedMap(out RoadMeshChunk[] chunks)
        {
            BakedPath path = BuildCirclePath();

            var meshSettings = RoadMeshSettings.Defaults();
            // Force several chunks on a 157 m loop so the seam under test really exists.
            meshSettings.ChunkLength = 40f;
            chunks = RoadMeshBuilder.Build(path, meshSettings);
            for (int i = 0; i < chunks.Length; i++)
                _spawned.Add(chunks[i].Mesh);

            var road = ScriptableObject.CreateInstance<RoadAsset>();
            road.RoadId = "main";
            road.Path = path;
            road.Width = Width;
            road.Chunks = chunks;

            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.MapId = "TestChunked";
            map.Roads = new[] { road };
            map.Edges = new EdgeAsset[0];
            map.RacingLine = path.Clone();
            map.SpawnRoadIndex = 0;
            map.SpawnColumns = 3;
            map.SpawnRows = 2;
            map.SpawnSpacing = 4.5f;
            map.SpawnBackOffset = 4.5f;
            map.SpawnHeight = 0.35f;
            map.WallHeight = 1.2f;
            map.WallThickness = 0.5f;
            map.RouteStep = 10f;
            map.CheckpointDistances = new[] { 0f, path.TotalLength * 0.5f };
            map.SupportedModes = new[] { 0, 2 };
            map.Sanitize();
            _spawned.Add(map);
            _spawned.Add(road);
            return map;
        }

        private static BakedPath BuildCirclePath()
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[Rings],
                Tangents = new PathVector3[Rings],
                Ups = new PathVector3[Rings],
                WidthLeft = new float[Rings],
                WidthRight = new float[Rings],
                Banking = new float[Rings],
                Distances = new float[Rings],
                SurfacePrimary = new byte[Rings],
                SurfaceSecondary = new byte[Rings],
                SurfaceBlend = new byte[Rings],
                Closed = true,
            };

            for (int i = 0; i < Rings; i++)
            {
                float angle = 2f * Mathf.PI * i / Rings;
                var position = new Vector3(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius);
                var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                path.Positions[i] = PathConvert.ToPathVector(position);
                path.Tangents[i] = PathConvert.ToPathVector(tangent);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = Width * 0.5f;
                path.WidthRight[i] = Width * 0.5f;
                path.Distances[i] = LoopLength * i / Rings;
            }
            path.TotalLength = LoopLength;
            path.ComputeCurvature();
            return path;
        }
    }
}
