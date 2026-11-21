using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Turns a baked centreline into the deterministic dressing list a circuit needs to read like a
    /// cute low-poly racer: kerbs and edge lines on the road, gravel and tyre walls on the run-off,
    /// fences on the straights, and a populated far band. It is engine-free and single-pass, so the
    /// same call runs at bake time in the editor and in a headless server with no UnityEngine.
    ///
    /// Determinism is a feature, not a nicety: a seeded xorshift stream (never System.Random) and a
    /// fixed emission order mean the same inputs give byte-identical placements on every machine.
    /// The far band uses a preallocated coarse occupancy grid so no two props overlap and none of
    /// them reach the asphalt.
    /// </summary>
    public static class TracksidePlanner
    {
        // Scratch lives on the calling thread so a load-time Plan allocates its working arrays once
        // and every later call (a rebuild, a test loop) is zero-GC. Nothing here is shared across
        // threads, so a parallel bake stays safe.
        [ThreadStatic] private static Scratch _scratch;

        /// <summary>
        /// Fills <paramref name="output"/> with the trackside dressing and returns how many slots
        /// were written. A buffer too small truncates cleanly instead of throwing, so a caller that
        /// mistuned its capacity loses props, not the run.
        /// </summary>
        public static int Plan(BakedPath path, float roadWidth, in TracksideRuleSet rules, uint seed,
            TracksidePlacement[] output, out int count)
        {
            count = 0;
            if (path == null || output == null || output.Length == 0 || !path.IsUsable)
                return 0;

            // Copies because an `in` parameter cannot be captured by a local function.
            TracksideRuleSet r = rules;
            float halfWidth = roadWidth > 0f ? roadWidth * 0.5f : 1f;

            Scratch s = _scratch ?? (_scratch = new Scratch());
            int regionCount = DetectCorners(path, r, s);

            int written = 0;
            float total = path.TotalLength;

            // ------------------------------------------------------------------ kerbs
            for (int i = 0; i < regionCount; i++)
            {
                CornerRegion region = s.Regions[i];
                int inside = region.SignedCurvature > 0f ? -1 : 1;
                int outside = -inside;
                float start = region.Start - r.KerbLeadIn;
                float length = region.Length + r.KerbLeadIn + r.KerbLeadOut;
                EmitStrip(path, output, ref written, TracksideKind.Kerb, inside, start, length, r.KerbWidth, halfWidth);
                EmitStrip(path, output, ref written, TracksideKind.Kerb, outside, start, length, r.KerbWidth, halfWidth);
            }

            // ------------------------------------------------------------------ run-off
            for (int i = 0; i < regionCount; i++)
            {
                CornerRegion region = s.Regions[i];
                int outside = region.SignedCurvature > 0f ? 1 : -1;
                if (region.PeakAbsCurvature <= r.GravelCurvatureThreshold)
                    continue;
                EmitStrip(path, output, ref written, TracksideKind.GravelTrap, outside, region.Start,
                    region.Length, r.GravelDepth, halfWidth);
            }

            // ------------------------------------------------------------------ tyre walls
            for (int i = 0; i < regionCount; i++)
            {
                CornerRegion region = s.Regions[i];
                int outside = region.SignedCurvature > 0f ? 1 : -1;
                // The wall sits beyond the gravel when there is one, else just beyond the kerb.
                float runoff = region.PeakAbsCurvature > r.GravelCurvatureThreshold ? r.GravelDepth : 0f;
                float lateral = halfWidth + runoff + r.TyreWallOffset;
                float end = region.Start + region.Length;
                float spacing = r.TyreWallSpacing > 0.1f ? r.TyreWallSpacing : 2.5f;
                for (float d = region.Start; d < end; d += spacing)
                {
                    if (written >= output.Length)
                        break;
                    EmitProp(path, output, ref written, TracksideKind.TyreWall, outside, d, lateral, 0f,
                        spacing, r.TyreWallWidth, TracksideKindInfo.Collides(TracksideKind.TyreWall), false);
                }
            }

            // ------------------------------------------------------------------ edge lines
            float lineLength = r.EdgeLineSegmentLength > 0.1f ? r.EdgeLineSegmentLength : 5f;
            int lineSegments = (int)Math.Ceiling(total / lineLength);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                int side = sideIndex == 0 ? -1 : 1;
                for (int i = 0; i < lineSegments; i++)
                {
                    float start = i * lineLength;
                    if (start >= total)
                        break;
                    float length = Math.Min(lineLength, total - start);
                    EmitStrip(path, output, ref written, TracksideKind.EdgeLine, side, start, length,
                        r.EdgeLineWidth, halfWidth);
                }
            }

            // ------------------------------------------------------------------ fences
            float fenceSpacing = r.FenceSpacing > 0.5f ? r.FenceSpacing : 10f;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                int side = sideIndex == 0 ? -1 : 1;
                for (float d = 0f; d < total && written < output.Length; d += fenceSpacing)
                {
                    if (!IsStraight(path, d, r.CornerCurvatureThreshold))
                        continue;
                    EmitProp(path, output, ref written, TracksideKind.Fence, side, d,
                        halfWidth + r.FenceWidth * 0.5f, 0f, fenceSpacing, r.FenceWidth,
                        TracksideKindInfo.Collides(TracksideKind.Fence), false);
                }
            }

            // ------------------------------------------------------------------ far band grid
            // One grid serves both the far band and the start-line flags, so a flag and a tree can
            // never land in the same spot just because different rules emitted them.
            float cellSize = r.FarBandRadius * 2f;
            if (cellSize <= 0.1f)
                cellSize = 7f;
            BuildGrid(path, cellSize, s);

            // ------------------------------------------------------------------ start line flags
            // Flags go first: the start line is a focal point and should win its spots.
            float zone = r.FlagZoneDistance > 0f ? r.FlagZoneDistance : 0f;
            float flagSpacing = r.FlagSpacing > 0.5f ? r.FlagSpacing : 4f;
            float flagStart = path.Closed ? -zone * 0.5f : 0f;
            float flagEnd = path.Closed ? zone * 0.5f : zone;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                int side = sideIndex == 0 ? -1 : 1;
                for (float d = flagStart; d <= flagEnd && written < output.Length; d += flagSpacing)
                {
                    TryPlaceFar(path, side, path.Wrap(d), halfWidth + 1.5f, halfWidth + r.FarBandMargin,
                        TracksideKind.Flag, 0.6f, 0.6f, 0f, output, ref written, s);
                }
            }

            // ------------------------------------------------------------------ far band
            PlaceFarBand(path, halfWidth, r, seed, output, ref written, s);

            // ------------------------------------------------------------------ breakables
            for (int i = 0; i < regionCount && written < output.Length; i++)
            {
                CornerRegion region = s.Regions[i];
                int outside = region.SignedCurvature > 0f ? 1 : -1;
                float lateral = halfWidth + r.KerbWidth + 0.5f;
                var chance = new TracksideRandom(seed ^ (uint)(i * 2654435761u));
                if (chance.NextUnit() < r.CornerPropChance)
                {
                    EmitProp(path, output, ref written, TracksideKind.Cone, outside,
                        region.Start + region.Length * 0.5f, lateral, 0f, 0.5f, 0.5f, true, true);
                }

                if (chance.NextUnit() < r.CornerPropChance * 0.5f)
                {
                    EmitProp(path, output, ref written, TracksideKind.Box, outside,
                        region.Start + region.Length * 0.25f, lateral + 0.4f, 0f, 0.6f, 0.6f, true, true);
                }
            }

            count = written;
            return written;
        }

        /// <summary>
        /// Planar distance from a point to the nearest centreline sample segment. The far band uses
        /// it to prove a prop clears the road and its run-off even on a hairpin, where two distant
        /// parts of the centreline come close together.
        /// </summary>
        public static float DistanceToCentreline(BakedPath path, float x, float z)
        {
            if (path == null || path.Count < 2)
                return 0f;

            int count = path.Count;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int next = i + 1;
                if (next == count)
                {
                    if (!path.Closed)
                        break;
                    next = 0;
                }

                PathVector3 a = path.Positions[i];
                float abx = path.Positions[next].x - a.x;
                float abz = path.Positions[next].z - a.z;
                float abSqr = abx * abx + abz * abz;
                float t = abSqr > 1e-8f ? ((x - a.x) * abx + (z - a.z) * abz) / abSqr : 0f;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                float dx = a.x + abx * t - x;
                float dz = a.z + abz * t - z;
                float sqr = dx * dx + dz * dz;
                if (sqr < best)
                    best = sqr;
            }

            return (float)Math.Sqrt(best);
        }

        // ------------------------------------------------------------------ corner regions

        private struct CornerRegion
        {
            public float Start;
            public float Length;
            /// <summary>Signed mean curvature: its sign names the turn direction, hence inside/outside.</summary>
            public float SignedCurvature;
            public float PeakAbsCurvature;
        }

        /// <summary>
        /// Groups samples above the curvature threshold into corner regions and merges the run that
        /// crosses the loop seam, so a kerb is never split at d = 0. Runs are stored as
        /// (start, length) rather than (start, end) precisely because a wrapped run's end arc length
        /// can be smaller than its start.
        /// </summary>
        private static int DetectCorners(BakedPath path, in TracksideRuleSet rules, Scratch s)
        {
            int count = path.Count;
            s.EnsureCorners(count);
            bool[] corner = s.Corner;
            float threshold = rules.CornerCurvatureThreshold;

            bool anyStraight = false;
            for (int i = 0; i < count; i++)
            {
                float k = CurvatureAt(path, i);
                bool isCorner = Math.Abs(k) > threshold;
                corner[i] = isCorner;
                if (!isCorner)
                    anyStraight = true;
            }

            s.EnsureRegions(count);
            CornerRegion[] regions = s.Regions;
            int regionCount = 0;
            if (count < 2 || path.TotalLength <= 0f)
                return 0;

            if (!anyStraight)
            {
                float sum = 0f;
                float peak = 0f;
                for (int i = 0; i < count; i++)
                {
                    float k = CurvatureAt(path, i);
                    sum += k;
                    float ak = Math.Abs(k);
                    if (ak > peak)
                        peak = ak;
                }
                regions[0] = new CornerRegion
                {
                    Start = 0f,
                    Length = path.TotalLength,
                    SignedCurvature = sum,
                    PeakAbsCurvature = peak,
                };
                return 1;
            }

            int firstStraight = 0;
            for (int i = 0; i < count; i++)
            {
                if (!corner[i])
                {
                    firstStraight = i;
                    break;
                }
            }

            int runFirst = -1;
            int runLast = -1;
            float runSum = 0f;
            float runPeak = 0f;
            int runCount = 0;
            for (int step = 0; step < count; step++)
            {
                int index = (firstStraight + step) % count;
                if (corner[index])
                {
                    float k = CurvatureAt(path, index);
                    if (runFirst < 0)
                    {
                        runFirst = index;
                        runSum = 0f;
                        runPeak = 0f;
                        runCount = 0;
                    }
                    runLast = index;
                    runSum += k;
                    runCount++;
                    float ak = Math.Abs(k);
                    if (ak > runPeak)
                        runPeak = ak;
                }
                else if (runFirst >= 0)
                {
                    AddRegion(path, regions, ref regionCount, runFirst, runLast, runSum, runPeak, runCount);
                    runFirst = -1;
                }
            }

            if (runFirst >= 0)
                AddRegion(path, regions, ref regionCount, runFirst, runLast, runSum, runPeak, runCount);

            return regionCount;
        }

        private static void AddRegion(BakedPath path, CornerRegion[] regions, ref int regionCount,
            int first, int last, float sum, float peak, int count)
        {
            if (regionCount >= regions.Length || count <= 0)
                return;

            float start = path.Distances[first];
            float length;
            if (last >= first)
                length = path.Distances[last] - start;
            else
                length = path.TotalLength - start + path.Distances[last];

            regions[regionCount] = new CornerRegion
            {
                Start = start,
                Length = length,
                SignedCurvature = sum,
                PeakAbsCurvature = peak,
            };
            regionCount++;
        }

        private static float CurvatureAt(BakedPath path, int index)
        {
            return path.Curvature != null && index < path.Curvature.Length ? path.Curvature[index] : 0f;
        }

        private static bool IsStraight(BakedPath path, float distance, float threshold)
        {
            if (!path.Sample(distance, out PathSample sample))
                return true;
            return Math.Abs(sample.Curvature) <= threshold;
        }

        // ------------------------------------------------------------------ far band

        private static void PlaceFarBand(BakedPath path, float halfWidth, in TracksideRuleSet r, uint seed,
            TracksidePlacement[] output, ref int written, Scratch s)
        {
            float minLateral = halfWidth + r.FarBandMargin;
            float span = Math.Max(0.5f, r.FarBandSpacing);
            int steps = (int)Math.Ceiling(path.TotalLength / span);
            var rng = new TracksideRandom(seed ^ 0xB5297A4Du);
            float total = path.TotalLength;

            for (int i = 0; i < steps && written < output.Length; i++)
            {
                float baseDistance = i * r.FarBandSpacing;
                for (int sideIndex = 0; sideIndex < 2 && written < output.Length; sideIndex++)
                {
                    int side = sideIndex == 0 ? -1 : 1;
                    float jitter = (rng.NextUnit() * 2f - 1f) * r.FarBandJitter;
                    float distance = baseDistance + jitter;
                    if (path.Closed)
                        distance = path.Wrap(distance);
                    else if (distance < 0f || distance > total)
                        continue;

                    float lateral = halfWidth + r.FarBandMinOffset
                        + rng.NextUnit() * (r.FarBandMaxOffset - r.FarBandMinOffset);
                    if (lateral < minLateral)
                        lateral = minLateral;

                    // Both streams are drawn before the accept/reject branch so a rejected prop
                    // still advances the sequence: the layout stays deterministic either way.
                    TracksideKind kind = PickFarKind(ref rng);
                    float yawExtra = (rng.NextUnit() * 2f - 1f) * 30f;
                    TryPlaceFar(path, side, distance, lateral, minLateral, kind,
                        FarBandLength(kind), FarBandWidth(kind), yawExtra, output, ref written, s);
                }
            }
        }

        /// <summary>
        /// The one gate every far-band prop and start-line flag passes through: off the road, clear
        /// of the other far-band spots. Returns false when any check rejects the candidate.
        /// </summary>
        private static bool TryPlaceFar(BakedPath path, int side, float distance, float lateral,
            float minLateral, TracksideKind kind, float length, float width, float yawExtra,
            TracksidePlacement[] output, ref int written, Scratch s)
        {
            if (written >= output.Length)
                return false;
            if (!path.Sample(distance, out PathSample sample))
                return false;

            PathVector3 right = RightOf(sample.Tangent);
            float x = sample.Position.x + right.x * side * lateral;
            float z = sample.Position.z + right.z * side * lateral;
            if (DistanceToCentreline(path, x, z) < minLateral)
                return false;
            if (!TryOccupy(s, x, z))
                return false;

            output[written] = new TracksidePlacement(kind, x, z, YawDegrees(sample.Tangent) + yawExtra,
                side, distance, length, width, false, false);
            written++;
            return true;
        }

        private static TracksideKind PickFarKind(ref TracksideRandom rng)
        {
            float roll = rng.NextUnit();
            if (roll < 0.55f) return TracksideKind.Tree;
            if (roll < 0.73f) return TracksideKind.Tent;
            if (roll < 0.88f) return TracksideKind.Grandstand;
            if (roll < 0.94f) return TracksideKind.Flag;
            if (roll < 0.96f) return TracksideKind.BannerTower;
            if (roll < 0.98f) return TracksideKind.LightPost;
            return TracksideKind.Billboard;
        }

        private static float FarBandLength(TracksideKind kind)
        {
            switch (kind)
            {
                case TracksideKind.Grandstand: return 8f;
                case TracksideKind.Tree: return 3f;
                case TracksideKind.Tent: return 4f;
                case TracksideKind.Flag: return 0.6f;
                case TracksideKind.BannerTower: return 1.6f;
                case TracksideKind.LightPost: return 0.8f;
                case TracksideKind.Billboard: return 4f;
                default: return 1f;
            }
        }

        private static float FarBandWidth(TracksideKind kind)
        {
            switch (kind)
            {
                case TracksideKind.Grandstand: return 8f;
                case TracksideKind.Tree: return 3f;
                case TracksideKind.Tent: return 4f;
                case TracksideKind.Flag: return 0.6f;
                case TracksideKind.BannerTower: return 1.6f;
                case TracksideKind.LightPost: return 0.8f;
                case TracksideKind.Billboard: return 1.5f;
                default: return 1f;
            }
        }

        // ------------------------------------------------------------------ occupancy grid

        private static void BuildGrid(BakedPath path, float cellSize, Scratch s)
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                PathVector3 point = path.Positions[i];
                if (point.x < minX) minX = point.x;
                if (point.x > maxX) maxX = point.x;
                if (point.z < minZ) minZ = point.z;
                if (point.z > maxZ) maxZ = point.z;
            }

            if (minX > maxX)
            {
                minX = 0f;
                maxX = 0f;
                minZ = 0f;
                maxZ = 0f;
            }

            // Two cells of slack: every candidate the planner builds lies within the band, and the
            // 3x3 check needs a full cell of border to be inside the grid.
            minX -= cellSize * 2f;
            minZ -= cellSize * 2f;
            maxX += cellSize * 2f;
            maxZ += cellSize * 2f;
            int width = (int)Math.Floor((maxX - minX) / cellSize) + 1;
            int height = (int)Math.Floor((maxZ - minZ) / cellSize) + 1;
            if (width < 3) width = 3;
            if (height < 3) height = 3;

            int cells = width * height;
            s.EnsureGrid(cells);
            s.GridWidth = width;
            s.GridHeight = height;
            s.MinX = minX;
            s.MinZ = minZ;
            s.CellSize = cellSize;
            Array.Clear(s.Grid, 0, cells);
        }

        /// <summary>
        /// Reserves a prop's spot. A candidate sees any mark in its own 3x3 neighbourhood and is
        /// rejected, while acceptance marks only its own cell. With a cell at least twice the
        /// reserved footprint radius, two accepted neighbours are therefore at least one cell apart:
        /// the tightest packing that still keeps footprints clear. Coarse on purpose — the far band
        /// wants a planted look, not a perfect lattice.
        /// </summary>
        private static bool TryOccupy(Scratch s, float x, float z)
        {
            int[] grid = s.Grid;
            int width = s.GridWidth;
            int cx = (int)Math.Floor((x - s.MinX) / s.CellSize);
            int cz = (int)Math.Floor((z - s.MinZ) / s.CellSize);
            if (cx < 1 || cz < 1 || cx >= s.GridWidth - 1 || cz >= s.GridHeight - 1)
                return false;

            for (int dz = -1; dz <= 1; dz++)
            {
                int row = (cz + dz) * width + cx - 1;
                if (grid[row] != 0 || grid[row + 1] != 0 || grid[row + 2] != 0)
                    return false;
            }

            grid[cz * width + cx] = 1;
            return true;
        }

        // ------------------------------------------------------------------ emission

        private static void EmitStrip(BakedPath path, TracksidePlacement[] output, ref int written,
            TracksideKind kind, int side, float start, float length, float width, float halfWidth)
        {
            if (written >= output.Length || length <= 0f)
                return;
            if (!path.Sample(start + length * 0.5f, out PathSample sample))
                return;

            PathVector3 right = RightOf(sample.Tangent);
            float lateral = halfWidth + width * 0.5f;
            float x = sample.Position.x + right.x * side * lateral;
            float z = sample.Position.z + right.z * side * lateral;
            output[written] = new TracksidePlacement(kind, x, z, YawDegrees(sample.Tangent), side,
                start, length, width, TracksideKindInfo.Collides(kind), false);
            written++;
        }

        private static void EmitProp(BakedPath path, TracksidePlacement[] output, ref int written,
            TracksideKind kind, int side, float distance, float lateral, float yawJitter,
            float length, float width, bool collider, bool breakable)
        {
            if (written >= output.Length)
                return;
            if (!path.Sample(distance, out PathSample sample))
                return;

            PathVector3 right = RightOf(sample.Tangent);
            float x = sample.Position.x + right.x * side * lateral;
            float z = sample.Position.z + right.z * side * lateral;
            output[written] = new TracksidePlacement(kind, x, z, YawDegrees(sample.Tangent) + yawJitter,
                side, distance, length, width, collider, breakable);
            written++;
        }

        // ------------------------------------------------------------------ math helpers

        /// <summary>
        /// Unity's lateral axis for a planar heading (x, z) is (z, -x); mirrored here so the core
        /// offsets props the same way the runtime's <c>PathConvert.Right</c> does.
        /// </summary>
        private static PathVector3 RightOf(PathVector3 tangent)
        {
            var right = new PathVector3(tangent.z, 0f, -tangent.x);
            return right.Normalized;
        }

        private static float YawDegrees(PathVector3 tangent)
        {
            return (float)(Math.Atan2(tangent.x, tangent.z) * 180.0 / Math.PI);
        }

        // ------------------------------------------------------------------ scratch

        private sealed class Scratch
        {
            public bool[] Corner = new bool[0];
            public CornerRegion[] Regions = new CornerRegion[0];
            public int[] Grid = new int[0];
            public int GridWidth;
            public int GridHeight;
            public float MinX;
            public float MinZ;
            public float CellSize;

            public void EnsureCorners(int count)
            {
                if (Corner.Length < count)
                    Corner = new bool[count];
            }

            public void EnsureRegions(int count)
            {
                if (Regions.Length < count)
                    Regions = new CornerRegion[count];
            }

            public void EnsureGrid(int cells)
            {
                if (Grid.Length < cells)
                    Grid = new int[cells];
            }
        }
    }

    /// <summary>
    /// The planner's own xorshift32 stream. The point is reproducibility: re-baking an untouched
    /// road must give the identical dressing, which System.Random does not promise across runtimes.
    /// </summary>
    internal struct TracksideRandom
    {
        private uint _state;

        public TracksideRandom(uint seed)
        {
            // xorshift dies on a zero state, so map it to the golden-ratio constant.
            _state = seed != 0u ? seed : 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1) from the top 24 bits; enough for placement jitter.</summary>
        public float NextUnit()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }
    }
}
