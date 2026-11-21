using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Warns and never repairs. It reports typed issues into a caller-owned buffer so the editor can
    /// list them and the harness can prove each rule; it holds no state and allocates nothing.
    /// </summary>
    public static class MapValidator
    {
        public static int Validate(MapValidationInput input, MapIssue[] issues)
        {
            if (input == null || issues == null)
                return 0;

            int count = 0;
            SymmetryKnot[] knots = input.Knots;
            int knotCount = knots == null ? 0 : knots.Length;
            if (knotCount < 3)
                Add(issues, ref count, MapIssueCode.TooFewKnots, 0, 0f);

            float length = 0f;
            for (int i = 1; i < knotCount; i++)
                length += (knots[i].Position - knots[i - 1].Position).Magnitude;
            if (input.PathLength > 0f)
                length = input.PathLength;
            if (length < input.MinimumLength)
                Add(issues, ref count, MapIssueCode.DegenerateLength, 0, 0f);

            if (input.ClosedExpected && knotCount > 1
                && (knots[0].Position - knots[knotCount - 1].Position).Magnitude > input.ClosureTolerance)
                Add(issues, ref count, MapIssueCode.LoopNotClosed, knotCount - 1, length);

            for (int i = 1; i + 1 < knotCount; i++)
            {
                float radius = Radius(knots[i - 1].Position, knots[i].Position, knots[i + 1].Position);
                if (radius > 0f && radius < input.MinimumRadius)
                    Add(issues, ref count, MapIssueCode.TightCurvature, i, 0f);
            }

            for (int i = 0; i + 1 < knotCount; i++)
            {
                for (int j = i + 2; j + 1 < knotCount; j++)
                {
                    // The first and the closing segment meet at the seam, so they are not an overlap.
                    if (input.ClosedExpected && i == 0 && j == knotCount - 2)
                        continue;
                    if (GridMayOverlap(knots[i].Position, knots[i + 1].Position, knots[j].Position, knots[j + 1].Position, input.OverlapTolerance)
                        && SegmentsNear(knots[i].Position, knots[i + 1].Position, knots[j].Position, knots[j + 1].Position, input.OverlapTolerance))
                    {
                        Add(issues, ref count, MapIssueCode.SegmentOverlap, i, 0f);
                        i = knotCount;
                        break;
                    }
                }
            }

            if (input.SurfaceSpans != null && input.PathLength > 0f)
            {
                float covered = 0f;
                for (int i = 0; i < input.SurfaceSpans.Length; i++)
                {
                    SurfaceSpan span = input.SurfaceSpans[i];
                    if (span.TransitionLength > span.EndDistance - span.StartDistance)
                        Add(issues, ref count, MapIssueCode.SurfaceTransitionTooLong, i, span.StartDistance);
                    covered += Math.Max(0f, span.EndDistance - span.StartDistance);
                }

                if (covered + 1e-4f < input.PathLength)
                    Add(issues, ref count, MapIssueCode.SurfaceCoverage, 0, 0f);
            }

            if (input.Checkpoints != null)
            {
                for (int i = 1; i < input.Checkpoints.Length; i++)
                {
                    if (input.Checkpoints[i] <= input.Checkpoints[i - 1])
                    {
                        Add(issues, ref count, MapIssueCode.CheckpointOrder, i, input.Checkpoints[i]);
                        break;
                    }
                }
            }

            return count;
        }

        private static void Add(MapIssue[] issues, ref int count, MapIssueCode code, int index, float distance)
        {
            if (count < issues.Length)
            {
                issues[count++] = new MapIssue
                {
                    Code = code,
                    Severity = MapIssueSeverity.Warning,
                    Index = index,
                    Distance = distance,
                    MessageKey = "world.validation." + code.ToString(),
                };
            }
        }

        /// <summary>Menger circumradius of three knots; infinite when they are collinear.</summary>
        private static float Radius(PathVector3 a, PathVector3 b, PathVector3 c)
        {
            float ab = (b - a).Magnitude;
            float bc = (c - b).Magnitude;
            float ca = (a - c).Magnitude;
            float area2 = PathVector3.Cross(b - a, c - a).Magnitude;
            return area2 > 1e-6f ? ab * bc * ca / (2f * area2) : float.PositiveInfinity;
        }

        // Expanded uniform-grid cell ranges reject distant segment pairs before the exact distance test.
        private static bool GridMayOverlap(PathVector3 a, PathVector3 b, PathVector3 c, PathVector3 d, float tolerance)
        {
            float cell = Math.Max(1f, tolerance * 2f);
            float pad = Math.Max(0f, tolerance);
            int ax0 = (int)Math.Floor((Math.Min(a.x, b.x) - pad) / cell);
            int ax1 = (int)Math.Floor((Math.Max(a.x, b.x) + pad) / cell);
            int az0 = (int)Math.Floor((Math.Min(a.z, b.z) - pad) / cell);
            int az1 = (int)Math.Floor((Math.Max(a.z, b.z) + pad) / cell);
            int bx0 = (int)Math.Floor((Math.Min(c.x, d.x) - pad) / cell);
            int bx1 = (int)Math.Floor((Math.Max(c.x, d.x) + pad) / cell);
            int bz0 = (int)Math.Floor((Math.Min(c.z, d.z) - pad) / cell);
            int bz1 = (int)Math.Floor((Math.Max(c.z, d.z) + pad) / cell);
            return ax0 <= bx1 && bx0 <= ax1 && az0 <= bz1 && bz0 <= az1;
        }

        private static bool SegmentsNear(PathVector3 a, PathVector3 b, PathVector3 c, PathVector3 d, float tolerance)
        {
            PathVector3 u = b - a;
            PathVector3 v = d - c;
            PathVector3 w = a - c;
            float aa = PathVector3.Dot(u, u);
            float bb = PathVector3.Dot(u, v);
            float cc = PathVector3.Dot(v, v);
            float dd = PathVector3.Dot(u, w);
            float ee = PathVector3.Dot(v, w);
            float denom = aa * cc - bb * bb;
            float sN;
            float sD = denom;
            float tN;
            float tD = denom;
            if (aa <= 1e-8f || cc <= 1e-8f)
                return DistanceSegment(a, c, d) <= tolerance || DistanceSegment(c, a, b) <= tolerance;
            if (denom < 1e-8f)
            {
                sN = 0f;
                sD = 1f;
                tN = ee;
                tD = cc;
            }
            else
            {
                sN = bb * ee - cc * dd;
                tN = aa * ee - bb * dd;
                if (sN < 0f)
                {
                    sN = 0f;
                    tN = ee;
                    tD = cc;
                }
                else if (sN > sD)
                {
                    sN = sD;
                    tN = ee + bb;
                    tD = cc;
                }
            }

            if (tN < 0f)
            {
                tN = 0f;
                if (-dd < 0f)
                    sN = 0f;
                else if (-dd > aa)
                    sN = sD;
                else
                {
                    sN = -dd;
                    sD = aa;
                }
            }
            else if (tN > tD)
            {
                tN = tD;
                if (-dd + bb < 0f)
                    sN = 0f;
                else if (-dd + bb > aa)
                    sN = sD;
                else
                {
                    sN = -dd + bb;
                    sD = aa;
                }
            }

            float sc = Math.Abs(sN) < 1e-8f ? 0f : sN / sD;
            float tc = Math.Abs(tN) < 1e-8f ? 0f : tN / tD;
            return (w + u * sc - v * tc).Magnitude <= tolerance;
        }

        private static float DistanceSegment(PathVector3 p, PathVector3 a, PathVector3 b)
        {
            PathVector3 ab = b - a;
            float q = ab.SqrMagnitude;
            float t = q > 1e-8f ? PathVector3.Dot(p - a, ab) / q : 0f;
            if (t < 0f)
                t = 0f;
            else if (t > 1f)
                t = 1f;
            return (p - (a + ab * t)).Magnitude;
        }
    }
}
