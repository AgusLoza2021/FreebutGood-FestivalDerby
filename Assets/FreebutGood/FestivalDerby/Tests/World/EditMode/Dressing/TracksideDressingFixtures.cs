using System;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Engine-free baked paths for the trackside dressing tests. The stadium oval mirrors the shape
    /// of Map_BigOval (long straights, two 25 m ends) and the compact circuit carries a tight
    /// 180 degree hairpin, so the planner is exercised on both a fast oval and a technical loop.
    /// </summary>
    internal static class TracksideDressingFixtures
    {
        private const float HalfWidth = 8f;

        /// <summary>
        /// A closed stadium: two straights of <paramref name="straight"/> metres joined by
        /// semicircles of <paramref name="radius"/>. <paramref name="wrapStart"/> shifts the sample
        /// phase so sample 0 lands in the middle of the left arc, which is what forces the corner
        /// run to cross the loop seam.
        /// </summary>
        public static BakedPath Oval(float straight, float radius, float step, bool wrapStart)
        {
            float arc = (float)Math.PI * radius;
            float total = 2f * straight + 2f * arc;
            int count = Math.Max(4, (int)Math.Round(total / step));
            var path = Allocate(count, total, true);

            float phase = wrapStart ? 2f * straight + 1.5f * arc : 0f;
            for (int i = 0; i < count; i++)
            {
                StadiumPose(i * step + phase, straight, radius,
                    out float x, out float z, out float tx, out float tz, out float k);
                path.Positions[i] = new PathVector3(x, 0f, z);
                path.Tangents[i] = new PathVector3(tx, 0f, tz);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = HalfWidth;
                path.WidthRight[i] = HalfWidth;
                path.Distances[i] = i * step;
                path.Curvature[i] = k;
            }

            return path;
        }

        /// <summary>
        /// An open loop with a proper hairpin: 180 degrees at R = 8 m, plus a fast sweeper and a
        /// gentle corner. Built by integrating a curvature profile, so the geometry and the
        /// curvature the planner reads are the same numbers.
        /// </summary>
        public static BakedPath Compact()
        {
            float[] lengths = { 60f, 31.4159f, 25f, 25.1327f, 25f, 23.5619f, 40f, 39.2699f, 50f };
            float[] curvature = { 0f, 0.05f, 0f, -0.125f, 0f, 0.0667f, 0f, 0.04f, 0f };
            return FromSegments(lengths, curvature, 0.5f);
        }

        /// <summary>
        /// One corner hard enough for a gravel trap (R = 25 m) and one that is still a corner for the
        /// kerbs but too gentle for gravel (R = 45 m), so the gravel rule can be checked against a
        /// control that does get kerbs.
        /// </summary>
        public static BakedPath TwoCorners()
        {
            float[] lengths = { 40f, 39.2699f, 40f, 78.5398f, 40f };
            float[] curvature = { 0f, 0.04f, 0f, 0.022f, 0f };
            return FromSegments(lengths, curvature, 0.5f);
        }

        /// <summary>A straight path along +X with the road half-widths the strip builder reads.</summary>
        public static BakedPath Straight(float length, float halfWidth, float step)
        {
            int count = Math.Max(2, (int)Math.Round(length / step) + 1);
            var path = Allocate(count, length, false);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                path.Positions[i] = new PathVector3(length * t, 0f, 0f);
                path.Tangents[i] = new PathVector3(1f, 0f, 0f);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = halfWidth;
                path.WidthRight[i] = halfWidth;
                path.Distances[i] = length * t;
                path.Curvature[i] = 0f;
            }

            return path;
        }

        // ------------------------------------------------------------------ internals

        private static BakedPath Allocate(int count, float totalLength, bool closed)
        {
            return new BakedPath
            {
                Closed = closed,
                TotalLength = totalLength,
                DataVersion = 2,
                Positions = new PathVector3[count],
                Tangents = new PathVector3[count],
                Ups = new PathVector3[count],
                WidthLeft = new float[count],
                WidthRight = new float[count],
                Banking = new float[count],
                Distances = new float[count],
                Curvature = new float[count],
            };
        }

        private static BakedPath FromSegments(float[] lengths, float[] curvature, float step)
        {
            float total = 0f;
            var cumulative = new float[lengths.Length];
            for (int i = 0; i < lengths.Length; i++)
            {
                cumulative[i] = total;
                total += lengths[i];
            }

            int count = Math.Max(4, (int)Math.Round(total / step));
            float pathLength = count * step;
            var path = Allocate(count, pathLength, false);

            float x = 0f;
            float z = 0f;
            float heading = 0f;
            for (int i = 0; i < count; i++)
            {
                float s = i * step;
                int segment = SegmentAt(cumulative, s);
                float k = curvature[segment];
                path.Positions[i] = new PathVector3(x, 0f, z);
                path.Tangents[i] = new PathVector3((float)Math.Cos(heading), 0f, (float)Math.Sin(heading));
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = HalfWidth;
                path.WidthRight[i] = HalfWidth;
                path.Distances[i] = s;
                path.Curvature[i] = k;

                x += (float)Math.Cos(heading) * step;
                z += (float)Math.Sin(heading) * step;
                heading += k * step;
            }

            return path;
        }

        private static int SegmentAt(float[] cumulative, float distance)
        {
            int segment = 0;
            for (int i = 0; i < cumulative.Length; i++)
            {
                if (cumulative[i] <= distance)
                    segment = i;
                else
                    break;
            }

            return segment;
        }

        /// <summary>
        /// Position, tangent and curvature of the stadium at arc length s: bottom straight, right
        /// arc, top straight, left arc, then the loop closes.
        /// </summary>
        private static void StadiumPose(float s, float straight, float radius,
            out float x, out float z, out float tx, out float tz, out float k)
        {
            float arc = (float)Math.PI * radius;
            float total = 2f * straight + 2f * arc;
            s %= total;
            if (s < 0f)
                s += total;

            if (s < straight)
            {
                x = -straight * 0.5f + s;
                z = -radius;
                tx = 1f;
                tz = 0f;
                k = 0f;
                return;
            }

            if (s < straight + arc)
            {
                float angle = (s - straight) / radius - (float)Math.PI * 0.5f;
                x = straight * 0.5f + radius * (float)Math.Cos(angle);
                z = radius * (float)Math.Sin(angle);
                tx = -(float)Math.Sin(angle);
                tz = (float)Math.Cos(angle);
                k = 1f / radius;
                return;
            }

            if (s < 2f * straight + arc)
            {
                float u = s - (straight + arc);
                x = straight * 0.5f - u;
                z = radius;
                tx = -1f;
                tz = 0f;
                k = 0f;
                return;
            }

            float leftAngle = (s - (2f * straight + arc)) / radius + (float)Math.PI * 0.5f;
            x = -straight * 0.5f + radius * (float)Math.Cos(leftAngle);
            z = radius * (float)Math.Sin(leftAngle);
            tx = -(float)Math.Sin(leftAngle);
            tz = (float)Math.Cos(leftAngle);
            k = 1f / radius;
        }
    }
}
