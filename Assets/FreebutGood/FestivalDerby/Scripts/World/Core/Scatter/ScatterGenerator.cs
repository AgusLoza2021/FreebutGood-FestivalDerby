using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Fills a caller-owned buffer with seeded placements along a baked path. Each bucket is keyed
    /// by (seed, layer, index), so editing one part of the road leaves every other bucket identical
    /// and the prop list never reshuffles.
    /// </summary>
    public static class ScatterGenerator
    {
        public static int Fill(BakedPath path, in ScatterRule rule, int seed, ScatterPoint[] buffer)
        {
            if (path == null || buffer == null || buffer.Length == 0 || rule.Spacing <= 0f || path.TotalLength <= 0f)
                return 0;

            int capacity = buffer.Length;
            int written = 0;
            int buckets = (int)Math.Ceiling(path.TotalLength / rule.Spacing);
            for (int i = 0; i < buckets && written < capacity; i++)
            {
                float center = i * rule.Spacing;
                float density = DensityAt(rule.Density, buckets <= 1 ? 0f : (float)i / (buckets - 1));
                float chance = Clamp(rule.Probability * density, 0f, 1f);
                if (DeterministicHash.Unit01(seed, rule.Layer, i, 0) >= chance)
                    continue;

                float distance = center
                    + (DeterministicHash.Unit01(seed, rule.Layer, i, 1) * 2f - 1f) * Math.Max(0f, rule.JitterAlong);
                if (path.Closed)
                    distance = path.Wrap(distance);
                else if (distance < 0f || distance > path.TotalLength)
                    continue;

                float lateral = Lerp(rule.LateralMin, rule.LateralMax, DeterministicHash.Unit01(seed, rule.Layer, i, 2))
                    + (DeterministicHash.Unit01(seed, rule.Layer, i, 3) * 2f - 1f) * Math.Max(0f, rule.JitterLateral);
                buffer[written] = new ScatterPoint
                {
                    Distance = distance,
                    Lateral = lateral,
                    Yaw = Lerp(rule.MinYaw, rule.MaxYaw, DeterministicHash.Unit01(seed, rule.Layer, i, 4)),
                    Scale = Lerp(rule.MinScale, rule.MaxScale, DeterministicHash.Unit01(seed, rule.Layer, i, 5)),
                    VariantIndex = rule.VariantCount > 0
                        ? (int)(DeterministicHash.Of(seed, rule.Layer, i, 6) % (uint)rule.VariantCount)
                        : 0,
                };
                written++;
            }

            return written;
        }

        /// <summary>Piecewise-linear density in normalized path space; a null curve is flat 1.</summary>
        private static float DensityAt(DensityKey[] keys, float t)
        {
            if (keys == null || keys.Length == 0)
                return 1f;
            if (t <= keys[0].Position)
                return keys[0].Value;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].Position)
                {
                    float span = keys[i].Position - keys[i - 1].Position;
                    return span > 0f
                        ? Lerp(keys[i - 1].Value, keys[i].Value, (t - keys[i - 1].Position) / span)
                        : keys[i].Value;
                }
            }

            return keys[keys.Length - 1].Value;
        }

        private static float Clamp(float x, float a, float b)
        {
            return x < a ? a : x > b ? b : x;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
