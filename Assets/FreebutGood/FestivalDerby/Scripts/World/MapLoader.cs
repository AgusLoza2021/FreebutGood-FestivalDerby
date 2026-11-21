using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.World
{
    /// <summary>
    /// Turns a <see cref="MapAsset"/> into scene geometry at runtime. It is the only place the baked
    /// data becomes boxes and a mesh, and it deliberately knows nothing about driving: it returns a
    /// <see cref="LoadedMap"/> of plain pieces. The driving bootstrap hands those pieces to the
    /// circuit factory through one explicit seam, so the map never references the driving assembly.
    ///
    /// The ground is one MeshCollider per road ribbon rather than a run of box strips: the asphalt
    /// is already a strip mesh, and the collider is the same triangles, so corners have no seams and
    /// there is no per-strip overlap to tune. The ribbons carry no visual thickness, which is fine
    /// for a static ground a car raycasts down onto.
    /// </summary>
    public static class MapLoader
    {
        private const float GateLegSize = 0.35f;
        private const float GateBeamThickness = 0.2f;
        private const float GateHeight = 2.2f;
        private static readonly Color AsphaltColor = new Color(0.13f, 0.15f, 0.17f, 1f);
        private static readonly Color BarrierColor = new Color(0.82f, 0.84f, 0.88f, 1f);

        // One barrier material for every wall segment and gate piece of every map this process loads.
        // A shared material (no per-renderer property block) is what lets the SRP Batcher draw the
        // ~500 wall boxes of a baked map as a handful of batches. It is never destroyed by a map, so a
        // torn-down circuit cannot leave the next one's walls pointing at a dead material; a play-mode
        // exit destroys it and the null check rebuilds it.
        private static Material s_barrierMaterial;

        /// <summary>
        /// Builds the whole map. <paramref name="collidersOnly"/> is for a headless consumer that
        /// needs physics but no renderers; it still creates the ground MeshCollider and the barrier
        /// BoxColliders. Any throw cleans up everything already built, so a failed load leaves no
        /// orphans. <paramref name="minSpawnSlots"/> grows the starting grid by whole rows behind the authored
        /// ones when a run needs more slots than the map authored (an offline 24-car field); the authored slots
        /// are unchanged, so 0 keeps the grid byte for byte.
        /// </summary>
        public static LoadedMap Load(MapAsset map, bool collidersOnly = false, Transform parent = null, int minSpawnSlots = 0)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            map.Sanitize();
            RoadAsset main = map.PrimaryRoad;
            if (main == null || main.Path == null || !main.Path.IsUsable)
                throw new ArgumentException("A map needs a primary road with a usable baked path.", nameof(map));

            LoadedMap loaded = new LoadedMap(parent);
            try
            {
                loaded.CollidersOnly = collidersOnly;
                loaded.Material = BuildMaterial(map);

                var walls = new List<BoxCollider>();
                BuildRoads(map, loaded, walls, collidersOnly);
                Material barrier = BarrierMaterial();
                BuildEdges(map, loaded, barrier, walls, collidersOnly);

                loaded.Walls = walls.ToArray();
                loaded.StartGate = BuildGate(main, loaded.Root.transform, barrier, collidersOnly);
                loaded.SpawnPoses = BuildSpawnGrid(map, main, minSpawnSlots);
                loaded.MainPath = main.Path;
                loaded.RacingLine = map.RacingLine != null && map.RacingLine.IsUsable ? map.RacingLine : main.Path;
                loaded.RoutePoses = BuildRoute(loaded.RacingLine, map.RouteStep);
                loaded.CheckpointDistances = map.CheckpointDistances ?? Array.Empty<float>();
                loaded.SupportedModes = map.SupportedModes ?? Array.Empty<int>();
                loaded.CameraOffset = map.CameraOffset;
                loaded.CameraLookHeight = map.CameraLookHeight;
                loaded.HasCameraOverride = map.HasCameraOverride;
                loaded.SpawnHeight = map.SpawnHeight;
                loaded.WallHeight = map.WallHeight;
                loaded.WallThickness = map.WallThickness;
                ComputeBounds(map, loaded);

                loaded.Root.SetActive(true);
                return loaded;
            }
            catch
            {
                loaded.Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------- roads

        /// <summary>
        /// The material every road piece renders with. A baked surface-blend material is a project
        /// asset, so it is cloned: the loader must own a runtime instance it can tint and destroy,
        /// because the circuit takes the material over and destroying a project asset would be data
        /// loss. With no baked material (an older map, or the road art never built) a plain URP
        /// material keeps the run drivable, just without the per-surface look.
        /// </summary>
        private static Material BuildMaterial(MapAsset map)
        {
            if (map.RoadMaterial != null)
                return new Material(map.RoadMaterial) { name = "Baked road material" };

            return new Material(RequireShader())
            {
                name = "Baked map material",
                color = AsphaltColor,
            };
        }

        private static void BuildRoads(MapAsset map, LoadedMap loaded, List<BoxCollider> walls,
            bool collidersOnly)
        {
            RoadAsset[] roads = map.Roads ?? Array.Empty<RoadAsset>();

            // The asphalt tint used to be a per-renderer property block, which keeps the SRP Batcher from
            // batching. A map built only from runtime ribbons (no swept chunks) is tinted on its own
            // runtime material instead; a chunked map was never tinted and keeps its baked look.
            bool anyChunks = false;
            for (int i = 0; i < roads.Length; i++)
            {
                if (roads[i] != null && roads[i].Chunks != null && roads[i].Chunks.Length > 0)
                    anyChunks = true;
            }
            if (!anyChunks && !collidersOnly)
                SetColor(loaded.Material, AsphaltColor);

            for (int i = 0; i < roads.Length; i++)
            {
                RoadAsset road = roads[i];
                if (road == null || road.Path == null || !road.Path.IsUsable)
                    continue;

                // A swept road ships as chunks; only a map baked before the sweep falls back to one
                // ribbon mesh built here, so the pre-sweep asset keeps loading unchanged.
                if (road.Chunks != null && road.Chunks.Length > 0)
                {
                    BuildRoadChunks(road, i, loaded, collidersOnly);
                    continue;
                }

                Mesh mesh = BuildRoadMesh(road, i);
                loaded.AddMesh(mesh);
                var go = new GameObject("Road " + (string.IsNullOrEmpty(road.RoadId) ? i.ToString() : road.RoadId));
                go.transform.SetParent(loaded.Root.transform, false);
                if (road.GroundLayer != 0)
                    go.layer = road.GroundLayer;

                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (!collidersOnly)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = loaded.Material;
                }

                if (i == map.SpawnRoadIndex || loaded.RoadMesh == null)
                    loaded.RoadMesh = mesh;
            }
        }

        /// <summary>
        /// One MeshCollider and (unless headless) one renderer per swept chunk. The chunks share
        /// their boundary ring, so the colliders meet without a crack and a car crosses the seam
        /// without a bump.
        ///
        /// The chunk meshes belong to the map ASSET, not to this load, so nothing here takes ownership
        /// of them: no RoadMeshOwner, and they are never handed on as <see cref="LoadedMap.RoadMesh"/>
        /// (the circuit destroys whatever mesh it is given). The asset guard in those owners only exists
        /// in the Editor, so in a player build the first teardown destroyed the baked chunks and the
        /// second race loaded a road with no collider under it.
        /// </summary>
        private static void BuildRoadChunks(RoadAsset road, int roadIndex, LoadedMap loaded,
            bool collidersOnly)
        {
            string id = string.IsNullOrEmpty(road.RoadId) ? roadIndex.ToString() : road.RoadId;
            for (int c = 0; c < road.Chunks.Length; c++)
            {
                Mesh mesh = road.Chunks[c].Mesh;
                if (mesh == null)
                    continue;

                var go = new GameObject("Road " + id + " chunk " + c);
                go.transform.SetParent(loaded.Root.transform, false);
                if (road.GroundLayer != 0)
                    go.layer = road.GroundLayer;

                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (!collidersOnly)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = loaded.Material;
                }
            }
        }

        /// <summary>
        /// The asphalt ribbon as a closed (or open) triangle strip between the two edge rings. One
        /// vertex pair per baked sample, so the strip is as dense as the bake chose.
        /// </summary>
        private static Mesh BuildRoadMesh(RoadAsset road, int index)
        {
            BakedPath path = road.Path;
            int count = path.Count;
            int segments = path.Closed ? count : count - 1;
            var vertices = new Vector3[count * 2];
            var normals = new Vector3[count * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < count; i++)
            {
                PathVector3 point = path.Positions[i];
                Vector3 right = PathConvert.Right(path.Tangents[i]);
                var position = new Vector3(point.x, point.y, point.z);
                vertices[2 * i] = position - right * path.WidthLeft[i];
                vertices[2 * i + 1] = position + right * path.WidthRight[i];
                normals[2 * i] = normals[2 * i + 1] = Vector3.up;
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % count;
                int k = i * 6;
                triangles[k] = 2 * i;
                triangles[k + 1] = 2 * next;
                triangles[k + 2] = 2 * i + 1;
                triangles[k + 3] = 2 * i + 1;
                triangles[k + 4] = 2 * next;
                triangles[k + 5] = 2 * next + 1;
            }

            // The winding follows the tangent, which turns through the loop, so a fixed order is not
            // reliably upward along the whole strip; never back-face a triangle.
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).y >= 0f)
                    continue;
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }

            var mesh = new Mesh { name = "Baked road mesh " + index };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------- edges

        private static void BuildEdges(MapAsset map, LoadedMap loaded, Material barrier, List<BoxCollider> walls,
            bool collidersOnly)
        {
            EdgeAsset[] edges = map.Edges ?? Array.Empty<EdgeAsset>();
            for (int i = 0; i < edges.Length; i++)
            {
                EdgeAsset edge = edges[i];
                if (edge == null || edge.Segments == null || edge.Segments.Length == 0)
                    continue;

                var group = new GameObject("Edge " +
                    (string.IsNullOrEmpty(edge.EdgeId) ? edge.Side.ToString() : edge.EdgeId));
                group.transform.SetParent(loaded.Root.transform, false);
                for (int s = 0; s < edge.Segments.Length; s++)
                {
                    BakedEdgeSegment segment = edge.Segments[s];
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = "Segment " + s;
                    box.transform.SetParent(group.transform, false);
                    box.transform.localPosition = new Vector3(segment.Center.x, segment.Center.y, segment.Center.z);
                    box.transform.localRotation = Quaternion.Euler(0f, segment.YawDegrees, 0f);
                    box.transform.localScale = new Vector3(segment.Size.x, segment.Size.y, segment.Size.z);
                    if (edge.Layer != 0)
                        box.layer = edge.Layer;

                    var collider = box.GetComponent<BoxCollider>();
                    if (collider != null)
                        walls.Add(collider);

                    var renderer = box.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        continue;
                    renderer.sharedMaterial = barrier;
                    if (collidersOnly)
                        renderer.enabled = false;
                }
            }
        }

        // ------------------------------------------------------------- gate

        /// <summary>
        /// The start/finish gate above the line. Decoration only: its colliders are removed so the
        /// gate can never block a car, exactly like the oval factory's gate.
        /// </summary>
        private static Transform BuildGate(RoadAsset main, Transform parent, Material material, bool collidersOnly)
        {
            if (!main.Path.Sample(0f, out PathSample sample))
                return null;

            var gate = new GameObject("Start finish gate");
            gate.transform.SetParent(parent, false);
            gate.transform.localPosition = PathConvert.ToVector3(sample.Position);
            gate.transform.localRotation = PathConvert.HeadingRotation(sample.Tangent);

            float span = Mathf.Max(main.Width, 1f);
            float halfSpan = span * 0.5f;
            float legY = GateHeight * 0.5f;
            CreateDecorativeBox("Gate leg west", gate.transform, new Vector3(GateLegSize, GateHeight, GateLegSize),
                new Vector3(-halfSpan, legY, 0f), material, collidersOnly);
            CreateDecorativeBox("Gate leg east", gate.transform, new Vector3(GateLegSize, GateHeight, GateLegSize),
                new Vector3(halfSpan, legY, 0f), material, collidersOnly);
            CreateDecorativeBox("Gate crossbar", gate.transform, new Vector3(span, GateBeamThickness, GateLegSize),
                new Vector3(0f, GateHeight - GateBeamThickness * 0.5f, 0f), material, collidersOnly);
            return gate.transform;
        }

        private static void CreateDecorativeBox(string name, Transform parent, Vector3 size, Vector3 position,
            Material material, bool collidersOnly)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            var collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                // Deferred destroy: disable now so the gate never participates in physics for a frame.
                collider.enabled = false;
                Object.Destroy(collider);
            }
            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;
            renderer.sharedMaterial = material;
            if (collidersOnly)
                renderer.enabled = false;
        }

        // ------------------------------------------------------------- spawns and route

        /// <summary>
        /// The starting grid behind the line: front row first, left to right, the player on slot 0.
        /// The grid follows the road's own tangent, so it is correct on a curve as well as a straight.
        /// A field bigger than the authored rows x columns adds rows behind them at the same spacing, so a
        /// map authors its grid once and still starts any field the match can hold.
        /// </summary>
        private static Pose[] BuildSpawnGrid(MapAsset map, RoadAsset road, int minSpawnSlots)
        {
            int columns = Mathf.Max(map.SpawnColumns, 1);
            int rows = Mathf.Max(map.SpawnRows, SpawnRowsFor(minSpawnSlots, columns));
            var poses = new Pose[columns * rows];
            int slot = 0;
            for (int row = 0; row < rows; row++)
            {
                float back = map.SpawnBackOffset + row * map.SpawnSpacing;
                if (!road.Path.Sample(road.Path.Wrap(-back), out PathSample sample))
                    break;

                Vector3 right = PathConvert.Right(sample.Tangent);
                Quaternion rotation = PathConvert.HeadingRotation(sample.Tangent);
                for (int column = 0; column < columns; column++)
                {
                    float lateral = (column - (columns - 1) * 0.5f) * map.SpawnSpacing;
                    Vector3 position = PathConvert.ToVector3(sample.Position) + right * lateral;
                    position.y += map.SpawnHeight;
                    poses[slot++] = new Pose(position, rotation);
                }
            }
            return poses;
        }

        /// <summary>Whole rows of <paramref name="columns"/> slots needed to seat <paramref name="slots"/> cars.</summary>
        public static int SpawnRowsFor(int slots, int columns)
        {
            if (slots <= 0 || columns <= 0)
                return 1;
            return (slots + columns - 1) / columns;
        }

        /// <summary>Ordered lap goals sampled from the racing line, so bots can follow it directly.</summary>
        private static Pose[] BuildRoute(BakedPath line, float step)
        {
            if (!line.IsUsable || step <= 0f)
                return Array.Empty<Pose>();

            int count = Mathf.Max(1, Mathf.FloorToInt(line.TotalLength / step));
            var poses = new Pose[count];
            for (int i = 0; i < count; i++)
            {
                if (!line.Sample(line.Wrap(i * step), out PathSample sample))
                    break;
                poses[i] = new Pose(PathConvert.ToVector3(sample.Position),
                    PathConvert.HeadingRotation(sample.Tangent));
            }
            return poses;
        }

        // ------------------------------------------------------------- bounds

        private static void ComputeBounds(MapAsset map, LoadedMap loaded)
        {
            RoadAsset main = map.PrimaryRoad;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue;
            RoadAsset[] roads = map.Roads ?? Array.Empty<RoadAsset>();
            for (int r = 0; r < roads.Length; r++)
            {
                BakedPath path = roads[r] != null ? roads[r].Path : null;
                if (path == null || path.Count == 0)
                    continue;
                for (int i = 0; i < path.Count; i++)
                {
                    PathVector3 point = path.Positions[i];
                    float radius = Mathf.Max(path.WidthLeft[i], path.WidthRight[i]);
                    minX = Mathf.Min(minX, point.x - radius);
                    maxX = Mathf.Max(maxX, point.x + radius);
                    minZ = Mathf.Min(minZ, point.z - radius);
                    maxZ = Mathf.Max(maxZ, point.z + radius);
                    minY = Mathf.Min(minY, point.y);
                }
            }

            if (minX > maxX)
            {
                loaded.Center = Vector2.zero;
                loaded.Size = new Vector2(1f, 1f);
                loaded.SurfaceY = main.Path.Count > 0 ? main.Path.Positions[0].y : 0f;
                return;
            }

            loaded.Center = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            loaded.Size = new Vector2(Mathf.Max(maxX - minX, 1f), Mathf.Max(maxZ - minZ, 1f));
            loaded.SurfaceY = minY;
        }

        // ------------------------------------------------------------- helpers

        private static Shader RequireShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Baked map geometry requires a URP Simple Lit or Lit shader.");
            return shader;
        }

        /// <summary>The process-wide barrier material; see <see cref="s_barrierMaterial"/>.</summary>
        private static Material BarrierMaterial()
        {
            if (s_barrierMaterial != null)
                return s_barrierMaterial;

            s_barrierMaterial = new Material(RequireShader()) { name = "Map barrier material (shared)" };
            SetColor(s_barrierMaterial, BarrierColor);
            return s_barrierMaterial;
        }

        /// <summary>
        /// Colours a runtime material through whichever of the two conventional colour properties its
        /// shader has. Material colour rather than a property block, so every renderer sharing the
        /// material stays SRP-Batcher compatible.
        /// </summary>
        private static void SetColor(Material material, Color color)
        {
            if (material == null)
                return;

            int baseColor = Shader.PropertyToID("_BaseColor");
            int legacyColor = Shader.PropertyToID("_Color");
            if (material.HasProperty(baseColor))
                material.SetColor(baseColor, color);
            if (material.HasProperty(legacyColor))
                material.SetColor(legacyColor, color);
        }
    }
}
