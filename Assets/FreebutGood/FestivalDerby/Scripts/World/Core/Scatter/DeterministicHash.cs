namespace FestivalDerby.World
{
    /// <summary>
    /// Stable integer hash for authored scatter. It must never be System.Random or object
    /// GetHashCode: the same (seed, layer, index) has to give the same placement on every machine
    /// and runtime, so re-baking an untouched road stays byte-identical.
    /// </summary>
    public static class DeterministicHash
    {
        public static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352dU;
            value ^= value >> 15;
            value *= 0x846ca68bU;
            value ^= value >> 16;
            return value;
        }

        public static uint Of(int seed, int layer, int index, uint channel = 0U)
        {
            uint hash = Mix(unchecked((uint)seed) ^ 0x9e3779b9U);
            hash = Mix(hash ^ unchecked((uint)layer));
            hash = Mix(hash ^ unchecked((uint)index));
            return Mix(hash ^ channel);
        }

        /// <summary>Uniform float in [0, 1) from the top 24 bits; enough for placement jitter.</summary>
        public static float Unit01(int seed, int layer, int index, uint channel = 0U)
        {
            return (Of(seed, layer, index, channel) >> 8) * (1f / 16777216f);
        }
    }
}
