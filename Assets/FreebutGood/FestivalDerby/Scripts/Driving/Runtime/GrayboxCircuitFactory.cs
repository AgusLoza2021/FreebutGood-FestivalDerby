using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Builds walls or a visual ribbon around the run-owned driving surface: the flat figure-eight
    /// and the big rounded-oval stadium. The existing ground remains the sole standing plane;
    /// asphalt never adds a collider.
    /// </summary>
    public static class GrayboxCircuitFactory
    {
        private const string RootName = "Graybox circuit";
        private const float GateLegSize = 0.35f;
        private const float GateBeamThickness = 0.2f;
        private const float GateHeight = 2.2f;
        // 65% of each racing half extent from the centre leaves a 35% margin to the inner wall face.
        private const float RouteCornerFraction = 0.65f;
        private const float MarkerBoxHeight = 0.12f;
        private const float MarkerWingLength = 0.5f;
        private const float MarkerWingWidth = 0.1f;
        // Wing offsets chosen so the two 45° bars nearly meet at the chevron's forward apex.
        private const float MarkerWingOffset = 0.18f;
        private static readonly Color CircuitColor = new Color(0.82f, 0.84f, 0.88f, 1f);
        private static readonly Color MarkerTint = new Color(1f, 0.92f, 0.35f, 1f);

        public static OwnedGrayboxCircuit Create(in GrayboxCircuitLayout layout, Transform parent = null)
        {
            // Validate before allocating anything: a rejected layout must leave no trace behind.
            if (!layout.IsValid)
                throw new ArgumentException("Graybox circuit layout must be finite with positive extents and walls.",
                    nameof(layout));
            if (parent != null)
                GrayboxGeometry.ValidateUnitParent(parent);

            var material = new Material(GrayboxGeometry.RequireShader())
            {
                name = RootName + " material",
                color = CircuitColor,
            };
            GameObject root = null;
            Mesh roadMesh = null;
            try
            {
                root = new GameObject(RootName);
                // Keep partial construction inactive until ownership is complete, like the car does.
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                // Parent first, then place: the root lands at world zero whatever the parent's pose is.
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var route = layout.IsFigureEight ? BuildFigureEightRoute(layout) : BuildRoute(layout);
                BoxCollider[] walls;
                if (layout.IsFigureEight)
                {
                    // Allocate at the ownership boundary, before any procedural builder can throw.
                    roadMesh = new Mesh();
                    walls = BuildFigureEight(root.transform, layout, material, roadMesh);
                }
                else
                    walls = BuildWalls(root.transform, layout, material);
                var gate = BuildGate(root.transform, layout, material, route);
                BuildRouteMarkers(root.transform, route, material);
                var spawn = layout.IsFigureEight ? route[route.Length - 1] : BuildSpawnPose(layout);
                if (layout.IsFigureEight)
                    spawn.position.y += layout.SpawnHeight;
                var single = new[] { spawn };
                var owned = new OwnedGrayboxCircuit(root, material, layout, walls, gate, single, route, roadMesh);
                root.SetActive(true);
                return owned;
            }
            catch
            {
                // Nothing was handed to the caller, so a half-built circuit must clean up after itself.
                if (root != null)
                {
                    root.SetActive(false);
                    Object.Destroy(root);
                }
                if (roadMesh != null)
                    Object.Destroy(roadMesh);
                Object.Destroy(material);
                throw;
            }
        }

        /// <summary>
        /// Builds the big rounded-oval stadium: an asphalt ribbon along the centerline, segmented
        /// barriers on both ribbon edges, a start gate on the line and one ordered route pose per
        /// 'settings.RouteSampleCount' along the loop. <paramref name="layout"/> supplies the floor
        /// centre, surface height and wall sizes; <paramref name="settings"/> supplies the shape.
        ///
        /// The floor stays the only collider: the asphalt is a mesh with no collider, exactly like
        /// the figure-eight's ribbon, so the ribbon can be re-laid without touching physics.
        /// </summary>
        public static OwnedGrayboxCircuit CreateOval(in GrayboxCircuitLayout layout,
            in TrackLayoutSettings settings, Transform parent = null)
        {
            if (!layout.IsValid)
                throw new ArgumentException("Graybox circuit layout must be finite with positive extents and walls.",
                    nameof(layout));
            if (!settings.IsValid || !settings.IsRoundedOval)
                throw new ArgumentException("A rounded-oval circuit needs a valid rounded-oval layout.",
                    nameof(settings));
            if (parent != null)
                GrayboxGeometry.ValidateUnitParent(parent);

            var material = new Material(GrayboxGeometry.RequireShader())
            {
                name = RootName + " material",
                color = CircuitColor,
            };
            GameObject root = null;
            Mesh roadMesh = null;
            try
            {
                root = new GameObject(RootName);
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                int samples = settings.RouteSampleCount;
                float halfWidth = settings.TrackWidth * 0.5f;
                float step = settings.CenterlineLapLength / samples;
                var inner = new Vector3[samples];
                var outer = new Vector3[samples];
                var route = new Pose[samples];
                for (int i = 0; i < samples; i++)
                {
                    settings.CenterlineSample(i * step, out var point, out var tangent);
                    var heading = new Vector3(tangent.x, 0f, tangent.y);
                    var position = new Vector3(layout.Center.x + point.x, layout.SurfaceY,
                        layout.Center.y + point.y);
                    // Unity's right of a heading is (t.z, -t.x); the ribbon is centred on the line.
                    var right = new Vector3(tangent.y, 0f, -tangent.x);
                    route[i] = new Pose(position, Quaternion.LookRotation(heading, Vector3.up));
                    inner[i] = position - right * halfWidth;
                    outer[i] = position + right * halfWidth;
                }

                roadMesh = new Mesh();
                BuildOvalAsphalt(root.transform, material, roadMesh, inner, outer);
                var walls = BuildOvalBarriers(root.transform, layout, material, inner, outer);
                var gate = BuildGate(root.transform, route[0].position, route[0].rotation,
                    settings.TrackWidth - settings.WallThickness, material);
                BuildRouteMarkers(root.transform, route, material);
                var grid = BuildSpawnGrid(settings, layout);

                var owned = new OwnedGrayboxCircuit(root, material, layout, walls, gate, grid, route, roadMesh);
                root.SetActive(true);
                return owned;
            }
            catch
            {
                // Nothing was handed to the caller, so a half-built circuit must clean up after itself.
                if (root != null)
                {
                    root.SetActive(false);
                    Object.Destroy(root);
                }
                if (roadMesh != null)
                    Object.Destroy(roadMesh);
                Object.Destroy(material);
                throw;
            }
        }

        /// <summary>
        /// The asphalt ribbon as a closed triangle strip between the two edge rings. It carries no
        /// collider: the floor owns the physics, and a re-laid ribbon must never change it.
        /// </summary>
        private static void BuildOvalAsphalt(Transform parent, Material material, Mesh mesh,
            Vector3[] inner, Vector3[] outer)
        {
            int samples = inner.Length;
            var vertices = new Vector3[samples * 2];
            var normals = new Vector3[samples * 2];
            var triangles = new int[samples * 6];
            for (int i = 0; i < samples; i++)
            {
                // A hair above the floor so the two coplanar surfaces never z-fight.
                vertices[2 * i] = inner[i] + Vector3.up * 0.01f;
                vertices[2 * i + 1] = outer[i] + Vector3.up * 0.01f;
                normals[2 * i] = normals[2 * i + 1] = Vector3.up;
                int next = 2 * ((i + 1) % samples);
                int k = i * 6;
                triangles[k] = 2 * i;
                triangles[k + 1] = next;
                triangles[k + 2] = 2 * i + 1;
                triangles[k + 3] = 2 * i + 1;
                triangles[k + 4] = next;
                triangles[k + 5] = next + 1;
            }

            // Never back-face a strip: the winding follows the tangent direction, which turns with
            // the loop, so a fixed order is not reliably upward on both straights.
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).y >= 0f) continue;
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            mesh.name = "Oval asphalt mesh";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            var road = new GameObject("Oval asphalt");
            road.transform.SetParent(parent, false);
            road.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = road.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            TintRenderer(renderer, material, new Color(0.13f, 0.15f, 0.17f, 1f));
        }

        /// <summary>
        /// One barrier box per sample step on each ribbon edge. The sample count is the caller's
        /// <see cref="TrackLayoutSettings.RouteSampleCount"/>, capped there at 64, so a side never
        /// costs more than 64 colliders; the chord error at 48 samples stays under half a metre.
        /// </summary>
        private static BoxCollider[] BuildOvalBarriers(Transform parent, in GrayboxCircuitLayout layout,
            Material material, Vector3[] inner, Vector3[] outer)
        {
            int samples = inner.Length;
            var walls = new List<BoxCollider>(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                int next = (i + 1) % samples;
                AddOvalBarrier(parent, material, layout, walls, inner[i], inner[next]);
                AddOvalBarrier(parent, material, layout, walls, outer[i], outer[next]);
            }
            return walls.ToArray();
        }

        private static void AddOvalBarrier(Transform parent, Material material, in GrayboxCircuitLayout layout,
            List<BoxCollider> walls, Vector3 from, Vector3 to)
        {
            var direction = to - from;
            float length = direction.magnitude;
            if (length < 1e-4f)
                return;
            // The wall centre sits half its height above the surface, so its base rests on it.
            var box = GrayboxGeometry.CreateBox("Oval barrier", parent,
                new Vector3(layout.WallThickness, layout.WallHeight, length + layout.WallThickness),
                (from + to) * 0.5f + Vector3.up * (layout.WallHeight * 0.5f), material, true);
            box.transform.localRotation = Quaternion.LookRotation(direction);
            walls.Add(box.GetComponent<BoxCollider>());
        }

        /// <summary>The full starting grid, front slot first, each pose clearing the surface.</summary>
        private static Pose[] BuildSpawnGrid(in TrackLayoutSettings settings, in GrayboxCircuitLayout layout)
        {
            int count = settings.SpawnPoseCount;
            var poses = new Pose[count];
            for (int i = 0; i < count; i++)
            {
                settings.TryGetSpawnPose(i, layout.Center, layout.SurfaceY, out var pose);
                pose.position += Vector3.up * layout.SpawnHeight;
                poses[i] = pose;
            }
            return poses;
        }

        private static Vector3 FigureEightPoint(in GrayboxCircuitLayout layout, int index, int count)
        {
            // Make both visits exactly coincident, rather than depending on sin(PI)'s roundoff.
            float t = 2f * Mathf.PI * index / count;
            if (index == 0 || index == count / 2)
                return new Vector3(layout.Center.x, layout.SurfaceY, layout.Center.y);
            return new Vector3(layout.Center.x + 0.32f * layout.Size.x * Mathf.Sin(2f * t),
                layout.SurfaceY, layout.Center.y + 0.375f * layout.Size.y * Mathf.Sin(t));
        }

        private static Pose[] BuildFigureEightRoute(in GrayboxCircuitLayout layout)
        {
            var route = new Pose[24];
            for (int i = 0; i < route.Length; i++)
            {
                var position = FigureEightPoint(layout, i, route.Length);
                var next = FigureEightPoint(layout, (i + 1) % route.Length, route.Length);
                route[i] = new Pose(position, Quaternion.LookRotation(next - position));
            }
            return route;
        }

        private static BoxCollider[] BuildFigureEight(Transform parent, in GrayboxCircuitLayout layout,
            Material material, Mesh mesh)
        {
            const int samples = 96;
            float minSize = Mathf.Min(layout.Size.x, layout.Size.y);
            float halfWidth = 0.1f * minSize;
            float thickness = Mathf.Min(layout.WallThickness * 0.7f, minSize * 0.01f);
            float height = Mathf.Min(layout.WallHeight * 0.5f, minSize * 0.05f);
            var vertices = new Vector3[samples * 2 + 4];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[samples * 6 + 6];
            var left = new Vector3[samples];
            var right = new Vector3[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = 2f * Mathf.PI * i / samples;
                var tangent = new Vector3(0.64f * layout.Size.x * Mathf.Cos(2f * t), 0f,
                    0.375f * layout.Size.y * Mathf.Cos(t)).normalized;
                var normal = new Vector3(-tangent.z, 0f, tangent.x);
                var point = FigureEightPoint(layout, i, samples);
                vertices[2 * i] = point + normal * halfWidth + Vector3.up * 0.01f;
                vertices[2 * i + 1] = point - normal * halfWidth + Vector3.up * 0.01f;
                normals[2 * i] = normals[2 * i + 1] = Vector3.up;
                left[i] = point + normal * (halfWidth + thickness * 0.5f);
                right[i] = point - normal * (halfWidth + thickness * 0.5f);
                int next = 2 * ((i + 1) % samples);
                int k = i * 6;
                triangles[k] = 2 * i;
                triangles[k + 1] = next;
                triangles[k + 2] = 2 * i + 1;
                triangles[k + 3] = 2 * i + 1;
                triangles[k + 4] = next;
                triangles[k + 5] = next + 1;
            }

            // The two strips share a plane. One raised diamond masks their overlap without a bridge
            // or another standing collider; its diagonals follow the crossing's strip intersection.
            var diagonal = new Vector2(0.64f * layout.Size.x, 0.375f * layout.Size.y).normalized;
            float crossX = halfWidth / diagonal.y * 1.05f;
            float crossZ = halfWidth / diagonal.x * 1.05f;
            var center = new Vector3(layout.Center.x, layout.SurfaceY + 0.015f, layout.Center.y);
            int patch = samples * 2;
            vertices[patch] = center + Vector3.left * crossX;
            vertices[patch + 1] = center + Vector3.forward * crossZ;
            vertices[patch + 2] = center + Vector3.right * crossX;
            vertices[patch + 3] = center + Vector3.back * crossZ;
            for (int i = patch; i < vertices.Length; i++) normals[i] = Vector3.up;
            int end = samples * 6;
            triangles[end] = patch;
            triangles[end + 1] = patch + 1;
            triangles[end + 2] = patch + 2;
            triangles[end + 3] = patch;
            triangles[end + 4] = patch + 2;
            triangles[end + 5] = patch + 3;
            // A wide layout can fold an inner offset at a tight bend; never back-face that strip.
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).y >= 0f) continue;
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            mesh.name = "Figure-eight asphalt mesh";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            var road = new GameObject("Figure-eight asphalt");
            road.transform.SetParent(parent, false);
            road.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = road.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            TintRenderer(renderer, material, new Color(0.13f, 0.15f, 0.17f, 1f));

            // Collision edges need fewer segments than the smooth visual ribbon on mobile.
            const int barrierStride = 4;
            var walls = new List<BoxCollider>(samples * 2 / barrierStride);
            for (int i = 0; i < samples; i += barrierStride)
            {
                int next = (i + barrierStride) % samples;
                BuildEdgeBarrier(parent, material, layout, left[i], left[next], thickness, height,
                    crossX, crossZ, walls);
                BuildEdgeBarrier(parent, material, layout, right[i], right[next], thickness, height,
                    crossX, crossZ, walls);
            }
            return walls.ToArray();
        }

        private static void BuildEdgeBarrier(Transform parent, Material material, in GrayboxCircuitLayout layout,
            Vector3 from, Vector3 to, float thickness, float height, float crossX, float crossZ,
            List<BoxCollider> walls)
        {
            // Omit every edge segment touching the central overlap, including its end caps. Both
            // diagonals then stay open to a dynamic car, not just to the route's point samples.
            if (TouchesCrossing(from, layout.Center, crossX + thickness, crossZ + thickness)
                || TouchesCrossing(to, layout.Center, crossX + thickness, crossZ + thickness))
                return;
            var direction = to - from;
            var box = GrayboxGeometry.CreateBox("Figure-eight barrier", parent,
                new Vector3(thickness, height, direction.magnitude + thickness),
                (from + to) * 0.5f + Vector3.up * (height * 0.5f), material, true);
            box.transform.localRotation = Quaternion.LookRotation(direction);
            walls.Add(box.GetComponent<BoxCollider>());
        }

        private static bool TouchesCrossing(Vector3 point, Vector2 center, float x, float z)
        {
            return Mathf.Abs(point.x - center.x) <= x && Mathf.Abs(point.z - center.y) <= z;
        }

        private static BoxCollider[] BuildWalls(Transform parent, in GrayboxCircuitLayout layout, Material material)
        {
            var center = layout.Center;
            float halfWidth = layout.Size.x * 0.5f;
            float halfLength = layout.Size.y * 0.5f;
            float thickness = layout.WallThickness;
            float height = layout.WallHeight;
            // The wall centre sits half its height above the surface, so its base rests on it.
            float y = layout.SurfaceY + height * 0.5f;
            // The long walls own the corners, so the enclosure has no gap on any of the four sides.
            float longSpan = layout.Size.y + thickness * 2f;

            return new[]
            {
                BuildWall(parent, "Circuit wall west", material, new Vector3(thickness, height, longSpan),
                    new Vector3(center.x - halfWidth - thickness * 0.5f, y, center.y)),
                BuildWall(parent, "Circuit wall east", material, new Vector3(thickness, height, longSpan),
                    new Vector3(center.x + halfWidth + thickness * 0.5f, y, center.y)),
                BuildWall(parent, "Circuit wall south", material, new Vector3(layout.Size.x, height, thickness),
                    new Vector3(center.x, y, center.y - halfLength - thickness * 0.5f)),
                BuildWall(parent, "Circuit wall north", material, new Vector3(layout.Size.x, height, thickness),
                    new Vector3(center.x, y, center.y + halfLength + thickness * 0.5f)),
            };
        }

        private static BoxCollider BuildWall(Transform parent, string name, Material material, Vector3 size,
            Vector3 position)
        {
            var box = GrayboxGeometry.CreateBox(name, parent, size, position, material, true);
            return (BoxCollider)box.GetComponent<Collider>();
        }

        /// <summary>
        /// The start/finish gate is decoration: it stands on the line but never collides, so it can
        /// mark the lap boundary without ever blocking a car. Detection lives elsewhere, on triggers.
        /// </summary>
        private static Transform BuildGate(Transform parent, in GrayboxCircuitLayout layout, Material material,
            Pose[] route)
        {
            var position = new Vector3(layout.Center.x, layout.SurfaceY, layout.Center.y);
            var rotation = layout.IsFigureEight ? route[0].rotation : Quaternion.identity;
            float span = layout.IsFigureEight ? 0.2f * Mathf.Min(layout.Size.x, layout.Size.y)
                : layout.Size.x - layout.WallThickness;
            return BuildGate(parent, position, rotation, span, material);
        }

        /// <summary>
        /// A gate at an explicit line pose: the shared shape behind both the floor-derived
        /// rectangle/figure-eight gate and the oval's start/finish line.
        /// </summary>
        private static Transform BuildGate(Transform parent, Vector3 position, Quaternion rotation, float span,
            Material material)
        {
            var gate = new GameObject("Start finish gate");
            gate.transform.SetParent(parent, false);
            gate.transform.localPosition = position;
            gate.transform.localRotation = rotation;
            float halfSpan = span * 0.5f;
            float legY = GateHeight * 0.5f;
            GrayboxGeometry.CreateBox("Gate leg west", gate.transform, new Vector3(GateLegSize, GateHeight, GateLegSize),
                new Vector3(-halfSpan, legY, 0f), material, false);
            GrayboxGeometry.CreateBox("Gate leg east", gate.transform, new Vector3(GateLegSize, GateHeight, GateLegSize),
                new Vector3(halfSpan, legY, 0f), material, false);
            GrayboxGeometry.CreateBox("Gate crossbar", gate.transform, new Vector3(span, GateBeamThickness, GateLegSize),
                new Vector3(0f, GateHeight - GateBeamThickness * 0.5f, 0f), material, false);
            return gate.transform;
        }

        private static Pose BuildSpawnPose(in GrayboxCircuitLayout layout)
        {
            // A quarter of the track behind the line, which leaves a clean approach to the gate.
            var position = new Vector3(layout.Center.x, layout.SurfaceY + layout.SpawnHeight,
                layout.Center.y - layout.Size.y * 0.25f);
            return new Pose(position, Quaternion.identity);
        }

        /// <summary>
        /// The cached world-space route. The walls' inner faces coincide with the racing bounds of
        /// the layout (see <see cref="BuildWalls"/>), so the goals derive from the same source of
        /// truth as the frozen wall geometry — never from duplicated floor dimensions.
        /// </summary>
        private static Pose[] BuildRoute(in GrayboxCircuitLayout layout)
        {
            var center = layout.Center;
            float halfWidth = layout.Size.x * 0.5f;
            float halfLength = layout.Size.y * 0.5f;
            float insetX = (1f - RouteCornerFraction) * halfWidth;
            float insetZ = (1f - RouteCornerFraction) * halfLength;

            var goals = new[]
            {
                new Vector2(center.x + halfWidth - insetX, center.y - halfLength + insetZ), // bottom-right
                new Vector2(center.x - halfWidth + insetX, center.y - halfLength + insetZ), // bottom-left
                new Vector2(center.x - halfWidth + insetX, center.y + halfLength - insetZ), // top-left
                new Vector2(center.x + halfWidth - insetX, center.y + halfLength - insetZ), // top-right
                new Vector2(center.x + halfWidth - insetX, center.y), // finish: right lane, gate plane
            };

            var route = new Pose[goals.Length];
            for (int i = 0; i < goals.Length; i++)
            {
                // Each pose faces the next goal; the finish wraps to bottom-right. All goals ride
                // on the surface, so every direction is yaw-only.
                var next = goals[(i + 1) % goals.Length];
                var direction = new Vector3(next.x - goals[i].x, 0f, next.y - goals[i].y).normalized;
                route[i] = new Pose(new Vector3(goals[i].x, layout.SurfaceY, goals[i].y),
                    Quaternion.LookRotation(direction));
            }
            return route;
        }

        /// <summary>
        /// One chevron marker per route goal, under 'Route markers' on the circuit root. The
        /// circuit root is pinned at world zero with identity rotation, so a route pose is also
        /// the marker's local pose under the root.
        /// </summary>
        private static void BuildRouteMarkers(Transform parent, Pose[] route, Material material)
        {
            var markers = new GameObject("Route markers");
            markers.transform.SetParent(parent, false);
            for (int i = 0; i < route.Length; i++)
            {
                var goal = new GameObject("Route goal " + i);
                goal.transform.SetParent(markers.transform, false);
                goal.transform.localPosition = route[i].position;
                goal.transform.localRotation = route[i].rotation;
                BuildMarkerWing(goal.transform, "Chevron wing left", material, -1f);
                BuildMarkerWing(goal.transform, "Chevron wing right", material, 1f);
            }
        }

        private static void BuildMarkerWing(Transform goal, string name, Material material, float side)
        {
            // Two angled bars form a chevron pointing along the goal's forward, i.e. toward the
            // next goal. Presentation only: CreateBox disables the box collider before the deferred
            // Destroy, so markers never take part in physics.
            var wing = GrayboxGeometry.CreateBox(name, goal,
                new Vector3(MarkerWingWidth, MarkerBoxHeight, MarkerWingLength),
                new Vector3(side * MarkerWingOffset, MarkerBoxHeight * 0.5f, 0f), material, false);
            wing.transform.localRotation = Quaternion.Euler(0f, -side * 45f, 0f);
            TintRenderer(wing.GetComponent<Renderer>(), material, MarkerTint);
        }

        private static void TintRenderer(Renderer renderer, Material material, Color tint)
        {
            // Initialization-only readability tint through a property block: no extra material is
            // created and every renderer still reports the circuit's owned shared material.
            var block = new MaterialPropertyBlock();
            int baseColor = Shader.PropertyToID("_BaseColor");
            if (material.HasProperty(baseColor))
                block.SetColor(baseColor, tint);
            else
                block.SetColor(Shader.PropertyToID("_Color"), tint);
            renderer.SetPropertyBlock(block);
        }
    }
}
