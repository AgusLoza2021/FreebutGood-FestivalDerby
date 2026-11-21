using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Engine-free ribbon builder for the flat trackside strips (kerbs, edge lines, gravel). For a
    /// placement it walks the baked path between the strip's arc-length span and writes a two-vertex
    /// ring per station into caller-owned arrays, so CS4b can upload a Unity Mesh without the core
    /// touching UnityEngine. Kerbs alternate red and white blocks through vertex colours; an edge
    /// line is white; the gravel is its own tan.
    ///
    /// Every array is the caller's, so the fill itself allocates nothing once the buffers exist.
    /// </summary>
    public static class KerbStripMesh
    {
        public const int FloatsPerPosition = 3;
        public const int FloatsPerColor = 4;

        private const float KerbRedR = 0.80f;
        private const float KerbRedG = 0.09f;
        private const float KerbRedB = 0.09f;
        private const float LineR = 0.96f;
        private const float LineG = 0.96f;
        private const float LineB = 0.94f;
        private const float GravelR = 0.78f;
        private const float GravelG = 0.72f;
        private const float GravelB = 0.55f;

        /// <summary>Vertices <see cref="Fill"/> will write for this placement (two per station).</summary>
        public static int RequiredVertexCount(in TracksidePlacement placement, in TracksideRuleSet rules)
        {
            return CountStations(placement.LengthMetres, rules.KerbBlockLength, rules.KerbSegmentLength) * 2;
        }

        /// <summary>Indices <see cref="Fill"/> will write (six per segment).</summary>
        public static int RequiredIndexCount(in TracksidePlacement placement, in TracksideRuleSet rules)
        {
            int stations = CountStations(placement.LengthMetres, rules.KerbBlockLength, rules.KerbSegmentLength);
            return stations > 1 ? (stations - 1) * 6 : 0;
        }

        /// <summary>
        /// Writes the ribbon. Returns quietly when a buffer is too small, so a caller that under-sized
        /// one simply gets nothing from this strip instead of a corrupted mesh. The caller-provided
        /// colours array may be null when only geometry is wanted.
        /// </summary>
        public static void Fill(BakedPath path, in TracksidePlacement placement, in TracksideRuleSet rules,
            float[] positions, float[] colors, int[] indices, out int vertexCount, out int indexCount)
        {
            vertexCount = 0;
            indexCount = 0;
            if (path == null || !path.IsUsable || positions == null || indices == null)
                return;

            // Copies because an `in` parameter cannot be captured by a local function.
            TracksidePlacement p = placement;
            TracksideRuleSet r = rules;

            float length = p.LengthMetres;
            if (length <= 0f)
                return;

            float blockLength = EffectiveBlockLength(length, r.KerbBlockLength);
            float maxStep = EffectiveMaxStep(r.KerbSegmentLength);
            int blocks = (int)Math.Ceiling(length / blockLength);
            if (blocks < 1)
                blocks = 1;

            int neededVertices = CountStations(length, r.KerbBlockLength, r.KerbSegmentLength) * 2;
            if (positions.Length < neededVertices * FloatsPerPosition)
                return;
            if (colors != null && colors.Length < neededVertices * FloatsPerColor)
                return;
            int neededIndices = neededVertices > 2 ? (neededVertices / 2 - 1) * 6 : 0;
            if (indices.Length < neededIndices)
                return;

            float d0 = p.DistanceAlong;
            int side = p.Side < 0 ? -1 : 1;
            float width = p.WidthMetres;
            int station = 0;

            void AddStation(float distance, int block)
            {
                float sampled = path.Closed ? distance : ClampOpen(distance, path.TotalLength);
                if (!path.Sample(sampled, out PathSample sample))
                    return;

                float inner = side < 0 ? sample.WidthLeft : sample.WidthRight;
                if (inner < 0f)
                    inner = 0f;
                PathVector3 right = RightOf(sample.Tangent);
                float direction = side < 0 ? -1f : 1f;
                int vertex = station * 2;
                WritePosition(positions, vertex, sample.Position, right, direction * inner);
                WritePosition(positions, vertex + 1, sample.Position, right, direction * (inner + width));
                if (colors != null)
                {
                    WriteColor(colors, vertex, p.Kind, block);
                    WriteColor(colors, vertex + 1, p.Kind, block);
                }
                station++;
            }

            AddStation(d0, 0);
            float d = d0;
            for (int b = 0; b < blocks; b++)
            {
                float blockEnd = d0 + Math.Min((b + 1) * blockLength, length);
                float span = blockEnd - d;
                int steps = Math.Max(1, (int)Math.Ceiling(span / maxStep));
                // A duplicate station at the block boundary lets the two colours meet with a hard
                // edge instead of interpolating across the seam.
                if (b > 0)
                    AddStation(d, b);
                for (int k = 1; k <= steps; k++)
                    AddStation(d + span * k / steps, b);
                d = blockEnd;
            }

            int written = 0;
            for (int seg = 0; seg < station - 1; seg++)
            {
                int baseVertex = seg * 2;
                indices[written++] = baseVertex;
                indices[written++] = baseVertex + 2;
                indices[written++] = baseVertex + 1;
                indices[written++] = baseVertex + 1;
                indices[written++] = baseVertex + 2;
                indices[written++] = baseVertex + 3;
            }

            FixWinding(positions, indices, written);
            vertexCount = station * 2;
            indexCount = written;
        }

        private static void WritePosition(float[] positions, int vertex, PathVector3 origin,
            PathVector3 right, float lateral)
        {
            int at = vertex * FloatsPerPosition;
            positions[at] = origin.x + right.x * lateral;
            positions[at + 1] = origin.y;
            positions[at + 2] = origin.z + right.z * lateral;
        }

        private static void WriteColor(float[] colors, int vertex, TracksideKind kind, int block)
        {
            float red;
            float green;
            float blue;
            if (kind == TracksideKind.EdgeLine)
            {
                red = LineR;
                green = LineG;
                blue = LineB;
            }
            else if (kind == TracksideKind.GravelTrap)
            {
                red = GravelR;
                green = GravelG;
                blue = GravelB;
            }
            else
            {
                bool isRed = (block & 1) == 0;
                red = isRed ? KerbRedR : LineR;
                green = isRed ? KerbRedG : LineG;
                blue = isRed ? KerbRedB : LineB;
            }

            int at = vertex * FloatsPerColor;
            colors[at] = red;
            colors[at + 1] = green;
            colors[at + 2] = blue;
            colors[at + 3] = 1f;
        }

        /// <summary>
        /// The strip follows a heading that turns through a loop, so one fixed triangle order is not
        /// reliably upward along the whole thing; never let a quad face down.
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

        private static int CountStations(float length, float blockLength, float maxStep)
        {
            if (length <= 0f)
                return 0;

            blockLength = EffectiveBlockLength(length, blockLength);
            maxStep = EffectiveMaxStep(maxStep);
            int blocks = (int)Math.Ceiling(length / blockLength);
            if (blocks < 1)
                blocks = 1;

            int stations = 1;
            float d = 0f;
            for (int b = 0; b < blocks; b++)
            {
                float blockEnd = Math.Min((b + 1) * blockLength, length);
                float span = blockEnd - d;
                int steps = Math.Max(1, (int)Math.Ceiling(span / maxStep));
                if (b > 0)
                    stations++;
                stations += steps;
                d = blockEnd;
            }

            return stations;
        }

        private static float EffectiveBlockLength(float length, float blockLength)
        {
            return blockLength > 1e-4f ? blockLength : length;
        }

        private static float EffectiveMaxStep(float maxStep)
        {
            return maxStep > 1e-4f ? maxStep : 0.5f;
        }

        /// <summary>
        /// BakedPath.Wrap folds whatever distance it is given, even on an open path, so the strip
        /// clamps to the path's end instead of jumping back to d = 0 mid-ribbon.
        /// </summary>
        private static float ClampOpen(float distance, float totalLength)
        {
            if (distance < 0f)
                return 0f;
            if (distance >= totalLength)
                return totalLength - 1e-3f;
            return distance;
        }

        private static PathVector3 RightOf(PathVector3 tangent)
        {
            var right = new PathVector3(tangent.z, 0f, -tangent.x);
            return right.Normalized;
        }
    }
}
