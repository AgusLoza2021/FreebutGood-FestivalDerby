using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FestivalDerby.World
{
    /// <summary>
    /// Dresses a loaded circuit at run start: red/white kerbs and white edge lines on the asphalt edge,
    /// sand run-off, and the planner's props (tyre walls, flags, trees, tents, grandstands). Purely visual:
    /// nothing it builds has a collider, because the server simulates the authoritative physics and a
    /// client-only collider would make the two disagree. Kinds that need a collider to make sense (fences,
    /// cones, boxes) are therefore skipped rather than drawn as ghosts.
    /// </summary>
    /// <remarks>
    /// Everything is built once at spawn, so the allocations here never touch the network tick. The dresser
    /// owns every mesh and material it creates; the circuit only owns its own, which is why
    /// <see cref="Dispose"/> must run even though the root is parented under the circuit.
    /// </remarks>
    public sealed class TracksideDresser : IDisposable
    {
        public const string RootName = "Trackside dressing (runtime)";

        // Strips are laid on the road surface, lifted just enough to never z-fight it. Kerbs sit above
        // the edge line so the corner kerb wins where the two overlap.
        private const float GravelLift = 0.012f;
        private const float EdgeLineLift = 0.02f;
        private const float KerbLift = 0.03f;

        private const int InitialPlacementCapacity = 2048;
        private const int MaxPlacementCapacity = 32768;

        private readonly List<Object> _owned = new List<Object>(8);
        private GameObject _root;

        /// <summary>Placements the planner produced, before the kit filtered them.</summary>
        public int PlannedCount { get; private set; }

        /// <summary>Discrete props actually instantiated.</summary>
        public int PropCount { get; private set; }

        /// <summary>Kerb, edge-line and gravel strips merged into the strip meshes.</summary>
        public int StripCount { get; private set; }

        public GameObject Root => _root;

        private TracksideDresser()
        {
        }

        /// <summary>
        /// Plans and builds the dressing for <paramref name="map"/>'s main path under
        /// <paramref name="parent"/>. Returns null when there is nothing to dress (no map, no usable path
        /// or no kit), so the caller's "is it dressed?" question is one null check.
        /// </summary>
        public static TracksideDresser Spawn(LoadedMap map, Transform parent, TracksideKit kit, uint seed)
        {
            if (map == null || kit == null)
                return null;

            BakedPath path = map.MainPath;
            if (path == null || !path.IsUsable)
                return null;

            var dresser = new TracksideDresser();
            dresser.Build(path, parent, kit, seed);
            return dresser;
        }

        private void Build(BakedPath path, Transform parent, TracksideKit kit, uint seed)
        {
            _root = new GameObject(RootName);
            if (parent != null)
                _root.transform.SetParent(parent, false);

            TracksideRuleSet rules = kit.Rules;
            TracksidePlacement[] placements = PlanAll(path, NominalRoadWidth(path), rules, seed, out int count);
            PlannedCount = count;

            BuildStrips(path, placements, count, rules, kit);
            BuildProps(path, placements, count, kit);
        }

        // ------------------------------------------------------------------ planning

        private static TracksidePlacement[] PlanAll(BakedPath path, float roadWidth, in TracksideRuleSet rules,
            uint seed, out int count)
        {
            // The planner truncates a full buffer rather than throwing, so a full buffer is the signal to
            // grow and plan again; the plan is deterministic from the seed, so the retry is identical.
            int capacity = InitialPlacementCapacity;
            while (true)
            {
                var buffer = new TracksidePlacement[capacity];
                TracksidePlanner.Plan(path, roadWidth, rules, seed, buffer, out count);
                if (count < capacity || capacity >= MaxPlacementCapacity)
                    return buffer;
                capacity *= 2;
            }
        }

        private static float NominalRoadWidth(BakedPath path)
        {
            return path.Sample(0f, out PathSample sample) ? sample.WidthLeft + sample.WidthRight : 0f;
        }

        // ------------------------------------------------------------------ strips

        private void BuildStrips(BakedPath path, TracksidePlacement[] placements, int count,
            in TracksideRuleSet rules, TracksideKit kit)
        {
            if (kit.StripMaterialTemplate == null)
                return;

            int maxVertices = 0;
            int maxIndices = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TracksideKindInfo.IsStrip(placements[i].Kind))
                    continue;
                maxVertices = Math.Max(maxVertices, KerbStripMesh.RequiredVertexCount(placements[i], rules));
                maxIndices = Math.Max(maxIndices, KerbStripMesh.RequiredIndexCount(placements[i], rules));
            }

            if (maxVertices == 0)
                return;

            var positions = new float[maxVertices * KerbStripMesh.FloatsPerPosition];
            var colors = new float[maxVertices * KerbStripMesh.FloatsPerColor];
            var indices = new int[maxIndices];

            var kerb = new StripBuilder(2);
            var line = new StripBuilder(1);
            var gravel = new StripBuilder(1);

            for (int i = 0; i < count; i++)
            {
                TracksidePlacement placement = placements[i];
                if (!TracksideKindInfo.IsStrip(placement.Kind))
                    continue;

                KerbStripMesh.Fill(path, placement, rules, positions, colors, indices,
                    out int vertexCount, out int indexCount);
                if (vertexCount == 0 || indexCount == 0)
                    continue;

                StripCount++;
                switch (placement.Kind)
                {
                    case TracksideKind.Kerb:
                        // The planner lays kerbs outward from the asphalt edge, which is exactly where the
                        // baked barrier walls stand; flipped onto the asphalt they read as a racing kerb.
                        FlipInward(positions, vertexCount);
                        kerb.Append(positions, colors, indices, vertexCount, indexCount, KerbLift, true);
                        break;
                    case TracksideKind.EdgeLine:
                        FlipInward(positions, vertexCount);
                        line.Append(positions, colors, indices, vertexCount, indexCount, EdgeLineLift, false);
                        break;
                    default:
                        gravel.Append(positions, colors, indices, vertexCount, indexCount, GravelLift, false);
                        break;
                }
            }

            Material red = OwnMaterial(kit.StripMaterialTemplate, kit.KerbRed, "Kerb red");
            Material white = OwnMaterial(kit.StripMaterialTemplate, kit.KerbWhite, "Kerb white");
            AddStripObject("Kerb strips", kerb, new[] { red, white });
            AddStripObject("Edge line strips",
                line, new[] { OwnMaterial(kit.StripMaterialTemplate, kit.EdgeLineColor, "Edge line") });
            AddStripObject("Gravel strips",
                gravel, new[] { OwnMaterial(kit.StripMaterialTemplate, kit.GravelColor, "Gravel") });
        }

        /// <summary>
        /// The fill writes each station as an (inner, outer) pair, inner on the asphalt edge. Moving the
        /// pair one width inwards keeps the strip's shape and puts it on the road side of the edge.
        /// </summary>
        private static void FlipInward(float[] positions, int vertexCount)
        {
            int stride = KerbStripMesh.FloatsPerPosition;
            for (int v = 0; v + 1 < vertexCount; v += 2)
            {
                int inner = v * stride;
                int outer = (v + 1) * stride;
                float dx = positions[outer] - positions[inner];
                float dz = positions[outer + 2] - positions[inner + 2];
                positions[outer] = positions[inner];
                positions[outer + 2] = positions[inner + 2];
                positions[inner] -= dx;
                positions[inner + 2] -= dz;
            }
        }

        private void AddStripObject(string name, StripBuilder builder, Material[] materials)
        {
            if (builder.VertexCount == 0)
                return;

            Mesh mesh = builder.ToMesh(name);
            _owned.Add(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private Material OwnMaterial(Material template, Color color, string name)
        {
            var material = new Material(template) { name = "Trackside " + name + " (runtime)" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", null);
            _owned.Add(material);
            return material;
        }

        /// <summary>Accumulates strips of one kind into a single mesh, one submesh per colour.</summary>
        private sealed class StripBuilder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>(4096);
            private readonly List<int>[] _submeshes;

            public StripBuilder(int submeshCount)
            {
                _submeshes = new List<int>[submeshCount];
                for (int i = 0; i < submeshCount; i++)
                    _submeshes[i] = new List<int>(4096);
            }

            public int VertexCount => _vertices.Count;

            public void Append(float[] positions, float[] colors, int[] indices, int vertexCount, int indexCount,
                float lift, bool splitByColour)
            {
                int baseVertex = _vertices.Count;
                int stride = KerbStripMesh.FloatsPerPosition;
                for (int v = 0; v < vertexCount; v++)
                {
                    int p = v * stride;
                    _vertices.Add(new Vector3(positions[p], positions[p + 1] + lift, positions[p + 2]));
                }

                for (int t = 0; t + 2 < indexCount; t += 3)
                {
                    int submesh = 0;
                    if (splitByColour)
                    {
                        // Every quad belongs to one kerb block, so the first vertex's colour is the
                        // triangle's: red blocks go to submesh 0, white ones to submesh 1.
                        int c = indices[t] * KerbStripMesh.FloatsPerColor;
                        submesh = colors[c + 1] > 0.5f ? 1 : 0;
                    }

                    List<int> target = _submeshes[submesh];
                    target.Add(baseVertex + indices[t]);
                    target.Add(baseVertex + indices[t + 1]);
                    target.Add(baseVertex + indices[t + 2]);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = "Trackside " + name + " (runtime)" };
                if (_vertices.Count > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.subMeshCount = _submeshes.Length;
                for (int i = 0; i < _submeshes.Length; i++)
                    mesh.SetTriangles(_submeshes[i], i, false);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ------------------------------------------------------------------ props

        private void BuildProps(BakedPath path, TracksidePlacement[] placements, int count, TracksideKit kit)
        {
            var groups = new Dictionary<TracksideKind, Transform>();
            for (int i = 0; i < count; i++)
            {
                TracksidePlacement placement = placements[i];
                // Tyre walls are the one colliding kind kept: they stand beyond the run-off, outside the
                // baked walls, where no car can reach the missing collider.
                if (TracksideKindInfo.IsStrip(placement.Kind)
                    || (TracksideKindInfo.Collides(placement.Kind) && placement.Kind != TracksideKind.TyreWall))
                    continue;

                TracksideKit.Entry entry = kit.Find(placement.Kind);
                if (entry == null)
                    continue;

                if (!groups.TryGetValue(placement.Kind, out Transform group))
                {
                    group = new GameObject(placement.Kind + " props").transform;
                    group.SetParent(_root.transform, false);
                    groups.Add(placement.Kind, group);
                }

                // Deterministic per placement, so every client dresses the circuit the same way.
                int variant = (int)(Hash((uint)i, (uint)placement.Kind) % (uint)entry.Variants.Length);
                GameObject prefab = entry.Variants[variant];
                if (prefab == null)
                    continue;

                float y = path.Sample(placement.DistanceAlong, out PathSample sample) ? sample.Position.y : 0f;
                var rotation = Quaternion.Euler(0f, placement.YawDegrees + entry.YawOffsetDegrees, 0f);
                GameObject instance = Object.Instantiate(prefab, new Vector3(placement.X, y, placement.Z), rotation,
                    group);
                instance.transform.localScale = Vector3.one * entry.Scale;
                StripColliders(instance);
                PropCount++;
            }
        }

        private static void StripColliders(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                Object.Destroy(colliders[i]);
        }

        private static uint Hash(uint a, uint b)
        {
            uint h = 2166136261u;
            h = (h ^ a) * 16777619u;
            h = (h ^ b) * 16777619u;
            h ^= h >> 15;
            return h;
        }

        /// <summary>Destroys everything the dresser created. Idempotent.</summary>
        public void Dispose()
        {
            if (_root != null)
                Destroy(_root);
            _root = null;

            for (int i = 0; i < _owned.Count; i++)
                Destroy(_owned[i]);
            _owned.Clear();
        }

        private static void Destroy(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
