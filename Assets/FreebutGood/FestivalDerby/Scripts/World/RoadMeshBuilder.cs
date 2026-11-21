using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FestivalDerby.World
{
    /// <summary>
    /// Sweeps a <see cref="BakedPath"/> into road meshes at bake time. This is the only place the
    /// asphalt becomes triangles, and it is deliberately engine-side but spline-free: it reads the
    /// baked path alone, so the same code a build runs is the code a headless tool can run.
    ///
    /// The sweep is arc-length driven, not index driven. Rings are placed every straight or corner
    /// step the local curvature asks for, so a straight costs few vertices while a tight corner keeps
    /// its chord error small, and a loop closes on an explicit ring at TotalLength so there is no
    /// gap at the start line. A long loop is split into chunks that share their boundary ring, so the
    /// pieces meet exactly and each stays inside the 16-bit index budget.
    /// </summary>
    public static class RoadMeshBuilder
    {
        /// <summary>
        /// Builds the road as one mesh per chunk. The caller owns every returned mesh; a
        /// <see cref="RoadMeshOwner"/> or an asset writer is responsible for destroying it.
        /// </summary>
        public static RoadMeshChunk[] Build(BakedPath path, RoadMeshSettings settings)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (!path.IsUsable)
                return Array.Empty<RoadMeshChunk>();

            settings = settings.Sanitized();
            List<Ring> rings = SampleRings(path, settings);
            if (rings.Count < 2)
                return Array.Empty<RoadMeshChunk>();

            return Chunkify(rings, settings);
        }

        // ------------------------------------------------------------------ sampling

        private struct Ring
        {
            public float Distance;
            public PathSample Sample;
            public SurfaceBlend Surface;
        }

        /// <summary>
        /// Walks the path by arc length and keeps a ring wherever the curvature asks for one. The
        /// step is chosen from the sample under the previous ring, so the winding rate follows the
        /// road rather than averaging it: straights stay sparse, corners stay round.
        /// </summary>
        private static List<Ring> SampleRings(BakedPath path, RoadMeshSettings settings)
        {
            float length = path.TotalLength;
            var rings = new List<Ring>();
            float distance = 0f;

            // The guard is a safety net, not the loop condition: a degenerate path whose steps never
            // advance would otherwise spin forever. One ring per minimum step plus the closing ring
            // is a hard upper bound on a sane sweep.
            int maxRings = 2 + Mathf.CeilToInt(length / Mathf.Max(settings.CornerSpacing, 0.05f));
            for (int guard = 0; guard < maxRings; guard++)
            {
                AddRing(path, rings, distance);
                if (distance >= length - 1e-4f)
                    break;

                PathSample sample = rings[rings.Count - 1].Sample;
                float step = Mathf.Abs(sample.Curvature) > settings.CornerCurvatureThreshold
                    ? settings.CornerSpacing
                    : settings.StraightSpacing;
                distance = Mathf.Min(distance + Mathf.Max(step, 0.05f), length);
            }

            // A closed loop must end on an explicit ring at TotalLength so the last quad meets the
            // first one. Sampling at TotalLength wraps to the start position, which is what closes it.
            if (path.Closed && rings[rings.Count - 1].Distance < length - 1e-4f)
                AddRing(path, rings, length);
            return rings;
        }

        private static void AddRing(BakedPath path, List<Ring> rings, float distance)
        {
            if (!path.Sample(distance, out PathSample sample))
                return;

            SurfaceBlend surface = path.SurfaceAt(distance);
            rings.Add(new Ring { Distance = distance, Sample = sample, Surface = surface });
        }

        // ------------------------------------------------------------------ chunking

        /// <summary>
        /// Splits the ring list into meshes no longer than ChunkLength, letting two neighbouring
        /// chunks share their boundary ring so the geometry is watertight across the split.
        /// </summary>
        private static RoadMeshChunk[] Chunkify(List<Ring> rings, RoadMeshSettings settings)
        {
            var chunks = new List<RoadMeshChunk>();
            int start = 0;
            while (start < rings.Count - 1)
            {
                int end = start + 1;
                // Stop before the span would pass the cap; a single step longer than the cap still
                // yields a valid two-ring chunk rather than an empty one.
                while (end < rings.Count - 1
                    && rings[end + 1].Distance - rings[start].Distance <= settings.ChunkLength)
                {
                    end++;
                }

                chunks.Add(BuildChunk(rings, start, end, settings, chunks.Count));
                start = end; // The boundary ring is shared: this is what keeps the seam closed.
            }
            return chunks.ToArray();
        }

        private static RoadMeshChunk BuildChunk(List<Ring> rings, int first, int last,
            RoadMeshSettings settings, int index)
        {
            int ringCount = last - first + 1;
            int vertexCount = ringCount * 2;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color32[vertexCount];
            var triangles = new int[(ringCount - 1) * 6];
            float tile = settings.MetersPerTile;

            for (int i = 0; i < ringCount; i++)
            {
                Ring ring = rings[first + i];
                Vector3 position = PathConvert.ToVector3(ring.Sample.Position);
                Vector3 up = PathConvert.ToVector3(ring.Sample.Up);
                if (up.sqrMagnitude < 1e-6f)
                    up = Vector3.up;
                else
                    up.Normalize();

                Vector3 right = PathConvert.Right(ring.Sample.Tangent);
                // u is arc length and v is lateral metres, both divided by the same tile size, so the
                // texture never stretches between a long straight and a tight corner.
                float u = ring.Distance / tile;
                float v = (ring.Sample.WidthLeft + ring.Sample.WidthRight) / tile;

                int left = 2 * i;
                int rightVertex = left + 1;
                vertices[left] = position - right * ring.Sample.WidthLeft;
                vertices[rightVertex] = position + right * ring.Sample.WidthRight;
                normals[left] = normals[rightVertex] = up;
                uvs[left] = new Vector2(u, 0f);
                uvs[rightVertex] = new Vector2(u, v);

                // R/G carry the two surface ids and B the blend weight; the shader decodes them by
                // multiplying by 255, so one vertex colour encodes the whole two-layer blend with no
                // extra UV channel and no per-vertex texture lookups.
                var color = new Color32((byte)ring.Surface.Primary, (byte)ring.Surface.Secondary,
                    ring.Surface.Blend, 255);
                colors[left] = colors[rightVertex] = color;
            }

            for (int i = 0; i < ringCount - 1; i++)
            {
                int k = i * 6;
                int aLeft = 2 * i;
                int aRight = aLeft + 1;
                int bLeft = aLeft + 2;
                int bRight = aLeft + 3;
                triangles[k] = aLeft;
                triangles[k + 1] = bLeft;
                triangles[k + 2] = aRight;
                triangles[k + 3] = aRight;
                triangles[k + 4] = bLeft;
                triangles[k + 5] = bRight;
            }

            // The tangent turns through a loop, so a fixed winding order is not reliably upward along
            // the whole sweep; flip any triangle that ended up facing down instead of back-facing it.
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(
                    vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]);
                if (normal.y >= 0f)
                    continue;
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }

            var mesh = new Mesh { name = "Road mesh " + index };
            // Set the index format before the triangles: a chunk stays under 65k vertices by design,
            // so 16-bit indices are the normal case and the 32-bit branch is a guard, not a path.
            if (vertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return new RoadMeshChunk
            {
                Mesh = mesh,
                StartDistance = rings[first].Distance,
                EndDistance = rings[last].Distance,
            };
        }
    }
}
