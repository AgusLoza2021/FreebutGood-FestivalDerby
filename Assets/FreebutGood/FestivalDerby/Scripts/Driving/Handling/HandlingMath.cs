namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// The handful of scalar helpers the model needs, all of them branch-only (no Math.Pow, no
    /// lerp-with-units, nothing whose rounding differs between a phone and the server). Kept in one
    /// place so the closed-loop tests and the Unity adapter cannot drift apart on the definition of
    /// "clamp" or "move towards".
    /// </summary>
    public static class HandlingMath
    {
        public const float TwoPi = 6.283185307179586f;
        public const float Pi = 3.141592653589793f;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Abs(float value)
        {
            return value < 0f ? -value : value;
        }

        public static float Sign(float value)
        {
            if (value > 0f)
            {
                return 1f;
            }

            return value < 0f ? -1f : 0f;
        }

        /// <summary>Moves a value towards a target by at most maxDelta. Never overshoots.</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (delta > maxDelta)
            {
                return current + maxDelta;
            }

            return delta < -maxDelta ? current - maxDelta : target;
        }

        public static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        /// <summary>Hermite ramp from 0 at edge0 to 1 at edge1, flat outside.</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float span = edge1 - edge0;
            if (span == 0f)
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Clamp01((x - edge0) / span);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Wraps any angle into (-pi, pi] with a bounded loop instead of Math.IEEERemainder, whose
        /// behaviour on large magnitudes is not worth depending on inside the tick.
        /// </summary>
        public static float WrapPi(float radians)
        {
            while (radians > Pi)
            {
                radians -= TwoPi;
            }

            while (radians <= -Pi)
            {
                radians += TwoPi;
            }

            return radians;
        }
    }
}
