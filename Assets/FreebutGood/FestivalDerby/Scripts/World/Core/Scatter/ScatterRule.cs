using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One authored scatter layer: where items sit along and across the path, how they are jittered
    /// and scaled, and the density curve that thins them out. Layer is part of the hash input, so
    /// two layers never share placements.
    /// </summary>
    [Serializable]
    public struct ScatterRule
    {
        public float Spacing;
        public float JitterAlong;
        public float JitterLateral;
        public float MinYaw;
        public float MaxYaw;
        public float MinScale;
        public float MaxScale;
        public float LateralMin;
        public float LateralMax;
        public float Probability;
        public int Layer;
        public int VariantCount;
        public DensityKey[] Density;
    }
}
