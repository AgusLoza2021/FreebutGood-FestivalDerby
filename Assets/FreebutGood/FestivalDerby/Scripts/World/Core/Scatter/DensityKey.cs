using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One sampled key of a scatter density curve: Value at Position in normalized [0, 1] path
    /// space. Sampled keys instead of an animation curve keep the runtime engine-free.
    /// </summary>
    [Serializable]
    public struct DensityKey
    {
        public float Position;
        public float Value;

        public DensityKey(float position, float value)
        {
            Position = position;
            Value = value;
        }
    }
}
