using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Engine-free 3D vector. The World core assembly sets noEngineReferences, so it cannot use
    /// UnityEngine.Vector3; this is the baked form instead. Every field is public so Unity
    /// serializes it inside a MapAsset, and the math mirrors NetVec3 so the shape of the project
    /// stays familiar.
    /// </summary>
    [Serializable]
    public struct PathVector3
    {
        public float x;
        public float y;
        public float z;

        public PathVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly PathVector3 Zero = new PathVector3(0f, 0f, 0f);
        public static readonly PathVector3 Up = new PathVector3(0f, 1f, 0f);

        public float SqrMagnitude
        {
            get { return x * x + y * y + z * z; }
        }

        public float Magnitude
        {
            get { return (float)Math.Sqrt(x * x + y * y + z * z); }
        }

        public static PathVector3 operator +(PathVector3 a, PathVector3 b)
        {
            return new PathVector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static PathVector3 operator -(PathVector3 a, PathVector3 b)
        {
            return new PathVector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static PathVector3 operator *(PathVector3 a, float scalar)
        {
            return new PathVector3(a.x * scalar, a.y * scalar, a.z * scalar);
        }

        public static float Dot(PathVector3 a, PathVector3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public static PathVector3 Cross(PathVector3 a, PathVector3 b)
        {
            return new PathVector3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);
        }

        public static PathVector3 Lerp(PathVector3 a, PathVector3 b, float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return new PathVector3(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t);
        }

        /// <summary>A zero vector normalises to zero instead of NaN, so a degenerate bake degrades.</summary>
        public PathVector3 Normalized
        {
            get
            {
                float length = Magnitude;
                return length > 1e-8f ? this * (1f / length) : Zero;
            }
        }
    }

    /// <summary>
    /// One interpolated point on a baked path. A struct so a query never allocates, which is what
    /// keeps the per-tick path followers off the GC.
    /// </summary>
    public struct PathSample
    {
        public PathVector3 Position;
        public PathVector3 Tangent;
        public PathVector3 Up;
        public float WidthLeft;
        public float WidthRight;
        public float Banking;
        public float Curvature;
        public float Distance;
        public float TargetSpeed;
    }

    /// <summary>
    /// The engine-free result of baking a spline: plain arrays sampled by arc length plus O(1)
    /// queries. The runtime, the server and the bots read only this; none of them ever touch the
    /// spline package, so a headless server needs no authoring dependency.
    ///
    /// Distances[0] is 0 and Distances[i] is the arc length from sample 0 to sample i. For a closed
    /// path the missing final segment wraps from the last sample back to sample 0, which is why
    /// TotalLength can exceed the last entry in Distances.
    /// </summary>
    [Serializable]
    public sealed class BakedPath
    {
        // Per sample, all arrays share one length (Count).
        public PathVector3[] Positions;
        public PathVector3[] Tangents;
        public PathVector3[] Ups;
        public float[] WidthLeft;
        public float[] WidthRight;
        public float[] Banking;
        public byte[] SurfacePrimary;
        public byte[] SurfaceSecondary;
        public byte[] SurfaceBlend;

        /// <summary>Signed curvature in 1/m: positive where the path turns left of its heading.</summary>
        public float[] Curvature;

        /// <summary>Cumulative arc length in metres, strictly increasing, Distances[0] == 0.</summary>
        public float[] Distances;

        /// <summary>Per-sample speed limit in m/s, or null until a racing line is planned.</summary>
        public float[] TargetSpeed;

        // Version 0 assets predate surface arrays; null arrays are interpreted as Street.
        public int DataVersion = 2;
        public bool Closed;
        public float TotalLength;

        public int Count
        {
            get { return Positions == null ? 0 : Positions.Length; }
        }

        public bool IsUsable
        {
            get
            {
                int count = Count;
                return count >= 2
                    && Tangents != null && Tangents.Length == count
                    && Ups != null && Ups.Length == count
                    && WidthLeft != null && WidthLeft.Length == count
                    && WidthRight != null && WidthRight.Length == count
                    && Banking != null && Banking.Length == count
                    && Distances != null && Distances.Length == count
                    && TotalLength > 0f;
            }
        }

        /// <summary>Folds any distance into [0, TotalLength); the loop has no special end case.</summary>
        public float Wrap(float distance)
        {
            if (TotalLength <= 0f)
                return 0f;

            float wrapped = distance % TotalLength;
            if (wrapped < 0f)
                wrapped += TotalLength;
            return wrapped;
        }

        /// <summary>Arc length of the segment that starts at <paramref name="index"/>.</summary>
        public float SegmentLength(int index)
        {
            int count = Count;
            if (count < 2 || index < 0 || index >= count)
                return 0f;
            if (index < count - 1)
                return Distances[index + 1] - Distances[index];
            return Closed ? TotalLength - Distances[count - 1] : 0f;
        }

        /// <summary>
        /// Interpolated sample at <paramref name="distance"/>, wrapped into the loop first. Returns
        /// false only for a path too short to sample, so callers can fall back instead of throwing.
        /// </summary>
        public bool Sample(float distance, out PathSample sample)
        {
            sample = default;
            int count = Count;
            if (count == 0)
                return false;
            if (count == 1)
            {
                sample = SampleAt(0, 0f, 0f);
                return true;
            }

            float wrapped = Wrap(distance);
            int index = FindSegment(wrapped);
            float segment = SegmentLength(index);
            float t = segment > 1e-6f ? (wrapped - Distances[index]) / segment : 0f;
            int next = (index + 1) % count;
            sample = Interpolate(index, next, t, wrapped);
            return true;
        }

        /// <summary>
        /// Squared-free nearest-path distance around a cached index. The search visits a fixed
        /// window, so a car that keeps its previous answer as its hint pays O(1) per tick; a stale
        /// or unset hint falls back to one full scan. Returns the arc length nearest to
        /// <paramref name="position"/> and stores the winning segment in <paramref name="hintIndex"/>.
        /// </summary>
        public SurfaceBlend SurfaceAt(float distance)
        {
            int count = Count;
            if (count == 0 || SurfacePrimary == null || SurfacePrimary.Length != count)
                return new SurfaceBlend(SurfaceId.Street, SurfaceId.Street, 0);
            int index = count == 1 ? 0 : FindSegment(Wrap(distance));
            byte primary = SurfacePrimary[index];
            byte secondary = SurfaceSecondary != null && SurfaceSecondary.Length == count ? SurfaceSecondary[index] : primary;
            byte blend = SurfaceBlend != null && SurfaceBlend.Length == count ? SurfaceBlend[index] : (byte)0;
            return new SurfaceBlend((SurfaceId)primary, (SurfaceId)secondary, blend);
        }

        public void ApplySurfaces(SurfaceSpanSet spans)
        {
            int count = Count;
            if (SurfacePrimary == null || SurfacePrimary.Length != count) SurfacePrimary = new byte[count];
            if (SurfaceSecondary == null || SurfaceSecondary.Length != count) SurfaceSecondary = new byte[count];
            if (SurfaceBlend == null || SurfaceBlend.Length != count) SurfaceBlend = new byte[count];
            DataVersion = 2;
            for (int i = 0; i < count; i++)
            {
                SurfaceBlend result = spans == null ? new SurfaceBlend(SurfaceId.Street, SurfaceId.Street, 0) : spans.Evaluate(Distances != null && Distances.Length == count ? Distances[i] : 0f);
                SurfacePrimary[i] = (byte)result.Primary;
                SurfaceSecondary[i] = (byte)result.Secondary;
                SurfaceBlend[i] = result.Blend;
            }
        }

        public float NearestDistance(PathVector3 position, ref int hintIndex)
        {
            int count = Count;
            if (count < 2)
                return 0f;

            const int window = 6;
            bool usedHint = hintIndex >= 0 && hintIndex < count;
            int start = usedHint ? hintIndex - window : 0;
            int end = usedHint ? hintIndex + window : count - 1;

            float bestDistance = SearchRange(position, start, end, count, out int bestIndex);

            // A hint is a guess that the car moved only a little since the last query. When the best
            // hit sits on the window edge the guess is wrong — a respawn, or a teleport — so widen to
            // a full scan instead of latching the surface the car just left behind. The common tick
            // stays O(1); only the rare jump pays for the scan.
            if (usedHint && (bestIndex == WrapIndex(start, count) || bestIndex == WrapIndex(end, count)))
                bestDistance = SearchRange(position, 0, count - 1, count, out bestIndex);

            hintIndex = bestIndex;
            return Closed ? Wrap(bestDistance) : bestDistance;
        }

        /// <summary>
        /// Nearest point over one window of segment indices; writes the winning segment to
        /// <paramref name="bestIndex"/> and returns its arc length. Shared by the hinted window and
        /// the full-scan fallback so both measure distance the same way.
        /// </summary>
        private float SearchRange(PathVector3 position, int start, int end, int count, out int bestIndex)
        {
            float bestSqr = float.MaxValue;
            float bestDistance = 0f;
            bestIndex = 0;
            for (int i = start; i <= end; i++)
            {
                int index = WrapIndex(i, count);
                if (index >= count - 1 && !Closed)
                    continue;

                float segment = SegmentLength(index);
                if (segment <= 0f)
                    continue;

                int next = (index + 1) % count;
                PathVector3 a = Positions[index];
                PathVector3 ab = Positions[next] - a;
                float abSqr = ab.SqrMagnitude;
                float t = abSqr > 1e-8f ? PathVector3.Dot(position - a, ab) / abSqr : 0f;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;

                float sqr = (position - (a + ab * t)).SqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestDistance = Distances[index] + t * segment;
                    bestIndex = index;
                }
            }

            return bestDistance;
        }

        /// <summary>
        /// Fills <see cref="Curvature"/> from the three consecutive samples around each point using
        /// Menger curvature: k = 2*|AB x BC| / (|AB||BC||CA|). That is exactly 1/r on a sampled
        /// circle and stays finite on a straight, where the interpolated spline curvature does not.
        /// </summary>
        public void ComputeCurvature()
        {
            int count = Count;
            if (Curvature == null || Curvature.Length != count)
                Curvature = new float[count];
            if (count < 3)
            {
                for (int i = 0; i < count; i++)
                    Curvature[i] = 0f;
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int previous = Closed ? (i - 1 + count) % count : (i > 0 ? i - 1 : i);
                int next = Closed ? (i + 1) % count : (i < count - 1 ? i + 1 : i);
                PathVector3 a = Positions[i] - Positions[previous];
                PathVector3 b = Positions[next] - Positions[i];
                PathVector3 c = Positions[next] - Positions[previous];
                float denominator = a.Magnitude * b.Magnitude * c.Magnitude;
                PathVector3 cross = PathVector3.Cross(a, b);
                float magnitude = denominator > 1e-8f ? 2f * cross.Magnitude / denominator : 0f;
                Curvature[i] = cross.y >= 0f ? magnitude : -magnitude;
            }
        }

        /// <summary>
        /// Plans <see cref="TargetSpeed"/> from curvature: v = sqrt(aLat / |k|) capped at
        /// <paramref name="topSpeed"/>, then a backward braking sweep so no sample asks for more
        /// speed than the next one plus what <paramref name="brakeDecel"/> can shed over the
        /// segment. A closed loop needs more than one sweep for the limit to cross the start index.
        /// </summary>
        public void BuildTargetSpeed(float maxLateralAccel, float topSpeed, float brakeDecel, int passes)
        {
            int count = Count;
            if (count == 0)
                return;
            if (TargetSpeed == null || TargetSpeed.Length != count)
                TargetSpeed = new float[count];
            if (maxLateralAccel <= 0f)
                maxLateralAccel = 1f;
            if (topSpeed < 0f)
                topSpeed = 0f;
            if (brakeDecel < 0f)
                brakeDecel = 0f;
            if (passes < 1)
                passes = 1;

            for (int i = 0; i < count; i++)
            {
                float k = Curvature != null && i < Curvature.Length ? Math.Abs(Curvature[i]) : 0f;
                float v = k > 1e-6f ? (float)Math.Sqrt(maxLateralAccel / k) : topSpeed;
                TargetSpeed[i] = Math.Min(v, topSpeed);
            }

            for (int sweep = 0; sweep < passes; sweep++)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    int next = i + 1;
                    if (next == count)
                    {
                        if (!Closed)
                            continue;
                        next = 0;
                    }

                    float segment = SegmentLength(i);
                    float limit = (float)Math.Sqrt(
                        TargetSpeed[next] * TargetSpeed[next] + 2f * brakeDecel * segment);
                    if (TargetSpeed[i] > limit)
                        TargetSpeed[i] = limit;
                }
            }
        }

        /// <summary>Deep copy, so a racing line can be offset without touching the road path.</summary>
        public BakedPath Clone()
        {
            int count = Count;
            return new BakedPath
            {
                Positions = Copy(Positions, count),
                Tangents = Copy(Tangents, count),
                Ups = Copy(Ups, count),
                WidthLeft = Copy(WidthLeft, count),
                WidthRight = Copy(WidthRight, count),
                Banking = Copy(Banking, count),
                SurfacePrimary = Copy(SurfacePrimary, count),
                SurfaceSecondary = Copy(SurfaceSecondary, count),
                SurfaceBlend = Copy(SurfaceBlend, count),
                Curvature = Copy(Curvature, count),
                Distances = Copy(Distances, count),
                TargetSpeed = Copy(TargetSpeed, count),
                DataVersion = DataVersion,
                Closed = Closed,
                TotalLength = TotalLength,
            };
        }

        // ------------------------------------------------------------------ helpers

        private int FindSegment(float distance)
        {
            // Largest index whose distance is not past the query; binary search costs no allocation.
            int low = 0;
            int high = Count - 1;
            while (low < high)
            {
                int mid = (low + high + 1) >> 1;
                if (Distances[mid] <= distance)
                    low = mid;
                else
                    high = mid - 1;
            }
            return low;
        }

        private PathSample SampleAt(int index, float distance, float targetSpeed)
        {
            return new PathSample
            {
                Position = Positions[index],
                Tangent = Tangents[index],
                Up = Ups[index],
                WidthLeft = WidthLeft[index],
                WidthRight = WidthRight[index],
                Banking = Banking[index],
                Curvature = Curvature != null ? Curvature[index] : 0f,
                Distance = distance,
                TargetSpeed = targetSpeed,
            };
        }

        private PathSample Interpolate(int index, int next, float t, float distance)
        {
            float targetSpeed = 0f;
            if (TargetSpeed != null && TargetSpeed.Length == Count)
                targetSpeed = TargetSpeed[index] + (TargetSpeed[next] - TargetSpeed[index]) * t;

            return new PathSample
            {
                Position = PathVector3.Lerp(Positions[index], Positions[next], t),
                Tangent = PathVector3.Lerp(Tangents[index], Tangents[next], t).Normalized,
                Up = PathVector3.Lerp(Ups[index], Ups[next], t).Normalized,
                WidthLeft = WidthLeft[index] + (WidthLeft[next] - WidthLeft[index]) * t,
                WidthRight = WidthRight[index] + (WidthRight[next] - WidthRight[index]) * t,
                Banking = Banking[index] + (Banking[next] - Banking[index]) * t,
                Curvature = Curvature != null && Curvature.Length == Count
                    ? Curvature[index] + (Curvature[next] - Curvature[index]) * t
                    : 0f,
                Distance = distance,
                TargetSpeed = targetSpeed,
            };
        }

        private int WrapIndex(int index, int count)
        {
            if (Closed)
            {
                int wrapped = index % count;
                return wrapped < 0 ? wrapped + count : wrapped;
            }
            if (index < 0) return 0;
            return index >= count ? count - 1 : index;
        }

        private static PathVector3[] Copy(PathVector3[] source, int count)
        {
            var result = new PathVector3[count];
            if (source != null)
                Array.Copy(source, result, Math.Min(source.Length, count));
            return result;
        }

        private static byte[] Copy(byte[] source, int count)
        {
            if (source == null) return null;
            var result = new byte[count];
            Array.Copy(source, result, Math.Min(source.Length, count));
            return result;
        }

        private static float[] Copy(float[] source, int count)
        {
            if (source == null)
                return null;
            var result = new float[count];
            Array.Copy(source, result, Math.Min(source.Length, count));
            return result;
        }
    }
}
