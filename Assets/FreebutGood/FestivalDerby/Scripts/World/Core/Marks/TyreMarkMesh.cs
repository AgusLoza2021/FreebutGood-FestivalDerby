using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// What a fill wrote, plus the sub-range of vertices it touched. The range lets the Unity side
    /// upload only the changed tail of a vertex buffer instead of the whole thing every tick.
    /// </summary>
    public readonly struct TyreMarkMeshFill
    {
        public readonly int VertexCount;
        public readonly int IndexCount;
        public readonly int DirtyVertexStart;
        public readonly int DirtyVertexCount;

        public TyreMarkMeshFill(int vertexCount, int indexCount, int dirtyVertexStart, int dirtyVertexCount)
        {
            VertexCount = vertexCount;
            IndexCount = indexCount;
            DirtyVertexStart = dirtyVertexStart;
            DirtyVertexCount = dirtyVertexCount;
        }
    }

    /// <summary>
    /// Turns the mark trail into mesh arrays. Same contract as <see cref="KerbStripMesh"/>: the caller
    /// owns the buffers, the fill writes into them, and nothing here touches UnityEngine.
    ///
    /// Consecutive segments of one strip share their joining edge vertices, so a bend has no gap and the
    /// vertex count is two per strip start plus two per segment, not four per segment. Winding is fixed
    /// per triangle afterwards because a car can lay a mark in either direction.
    /// </summary>
    public static class TyreMarkMesh
    {
        public const int FloatsPerPosition = 3;
        public const int FloatsPerColor = 4;
        public const int FloatsPerUv = 2;

        /// <summary>A little above the road so the mark does not z-fight with the asphalt.</summary>
        public const float LiftMeters = 0.02f;

        /// <summary>
        /// Vertices <see cref="Fill"/> will write: four for each strip start plus two per segment.
        /// </summary>
        public static int RequiredVertexCount(TyreMarkTrail trail)
        {
            if (trail == null)
                return 0;

            return 2 * (trail.Count + CountStrips(trail));
        }

        /// <summary>Indices <see cref="Fill"/> will write: six per segment.</summary>
        public static int RequiredIndexCount(TyreMarkTrail trail)
        {
            return trail == null ? 0 : trail.Count * 6;
        }

        /// <summary>
        /// Writes the live segments oldest-first. Returns a zeroed result and writes nothing when a
        /// buffer is too small, so an under-sized caller gets an empty draw instead of corrupted mesh
        /// data. Colours and uvs may be null when only geometry is wanted.
        /// </summary>
        public static TyreMarkMeshFill Fill(
            TyreMarkTrail trail,
            float y,
            float[] positions,
            float[] colors,
            int[] indices,
            float[] uvs)
        {
            var empty = new TyreMarkMeshFill(0, 0, 0, 0);
            if (trail == null || positions == null || indices == null)
                return empty;

            int segmentCount = trail.Count;
            int stripCount = CountStrips(trail);
            int neededVertices = 2 * (segmentCount + stripCount);
            int neededIndices = segmentCount * 6;

            if (positions.Length < neededVertices * FloatsPerPosition)
                return empty;
            if (indices.Length < neededIndices)
                return empty;
            if (colors != null && colors.Length < neededVertices * FloatsPerColor)
                return empty;
            if (uvs != null && uvs.Length < neededVertices * FloatsPerUv)
                return empty;

            int vertex = 0;
            int index = 0;
            int previousStrip = -1;
            int previousPair = 0;
            bool hasPrevious = false;
            float stripLength = 0f;

            for (int i = 0; i < segmentCount; i++)
            {
                TyreMarkSegment segment = trail.SegmentAt(i);

                float dx = segment.X1 - segment.X0;
                float dz = segment.Z1 - segment.Z0;
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                float dirX = length > 1e-6f ? dx / length : 1f;
                float dirZ = length > 1e-6f ? dz / length : 0f;
                float fade = trail.FadeFor(segment.Age);

                bool newStrip = !hasPrevious || segment.StripId != previousStrip;
                int aPair;
                if (newStrip)
                {
                    stripLength = 0f;
                    aPair = vertex;
                    WritePair(positions, colors, uvs, y, vertex, segment.X0, segment.Z0,
                        segment.Width, dirX, dirZ, in segment, fade, 0f);
                    vertex += 2;
                }
                else
                {
                    // Reuse the previous segment's trailing pair: the shared edge is what removes the
                    // gap at a bend.
                    aPair = previousPair;
                }

                int bPair = vertex;
                WritePair(positions, colors, uvs, y, vertex, segment.X1, segment.Z1,
                    segment.Width, dirX, dirZ, in segment, fade, stripLength + length);
                vertex += 2;

                indices[index++] = aPair;
                indices[index++] = aPair + 1;
                indices[index++] = bPair;
                indices[index++] = aPair + 1;
                indices[index++] = bPair + 1;
                indices[index++] = bPair;

                stripLength += length;
                previousPair = bPair;
                previousStrip = segment.StripId;
                hasPrevious = true;
            }

            FixWinding(positions, indices, index);

            int dirtyStart = trail.DirtyVertexStart;
            if (dirtyStart < 0)
                dirtyStart = 0;
            else if (dirtyStart > vertex)
                dirtyStart = vertex;
            return new TyreMarkMeshFill(vertex, index, dirtyStart, vertex - dirtyStart);
        }

        private static void WritePair(
            float[] positions,
            float[] colors,
            float[] uvs,
            float y,
            int vertex,
            float centerX,
            float centerZ,
            float width,
            float dirX,
            float dirZ,
            in TyreMarkSegment segment,
            float fade,
            float vCoord)
        {
            // Perpendicular of the segment direction in the x/z plane, so the pair straddles the
            // centreline of the wheel's path.
            float perpendicularX = dirZ;
            float perpendicularZ = -dirX;
            float halfWidth = width * 0.5f;
            float alpha = segment.Intensity * fade;

            int p = vertex * FloatsPerPosition;
            positions[p] = centerX - perpendicularX * halfWidth;
            positions[p + 1] = y;
            positions[p + 2] = centerZ - perpendicularZ * halfWidth;
            positions[p + 3] = centerX + perpendicularX * halfWidth;
            positions[p + 4] = y;
            positions[p + 5] = centerZ + perpendicularZ * halfWidth;

            if (colors != null)
            {
                int c = vertex * FloatsPerColor;
                colors[c] = segment.R;
                colors[c + 1] = segment.G;
                colors[c + 2] = segment.B;
                colors[c + 3] = alpha;
                colors[c + 4] = segment.R;
                colors[c + 5] = segment.G;
                colors[c + 6] = segment.B;
                colors[c + 7] = alpha;
            }

            if (uvs != null)
            {
                int u = vertex * FloatsPerUv;
                uvs[u] = 0f;
                uvs[u + 1] = vCoord;
                uvs[u + 2] = 1f;
                uvs[u + 3] = vCoord;
            }
        }

        /// <summary>
        /// A mark is laid along driving direction, which is not fixed, so a single triangle order is not
        /// reliably upward. Flip any triangle that would face down.
        /// </summary>
        private static void FixWinding(float[] positions, int[] indices, int indexCount)
        {
            for (int t = 0; t + 2 < indexCount; t += 3)
            {
                int i0 = indices[t] * FloatsPerPosition;
                int i1 = indices[t + 1] * FloatsPerPosition;
                int i2 = indices[t + 2] * FloatsPerPosition;
                float crossY = (positions[i1 + 2] - positions[i0 + 2]) * (positions[i2] - positions[i0])
                    - (positions[i1] - positions[i0]) * (positions[i2 + 2] - positions[i0 + 2]);
                if (crossY >= 0f)
                    continue;

                int swap = indices[t + 1];
                indices[t + 1] = indices[t + 2];
                indices[t + 2] = swap;
            }
        }

        private static int CountStrips(TyreMarkTrail trail)
        {
            int strips = 0;
            int previousStrip = -1;
            for (int i = 0; i < trail.Count; i++)
            {
                int strip = trail.SegmentAt(i).StripId;
                if (i == 0 || strip != previousStrip)
                    strips++;
                previousStrip = strip;
            }

            return strips;
        }
    }
}
