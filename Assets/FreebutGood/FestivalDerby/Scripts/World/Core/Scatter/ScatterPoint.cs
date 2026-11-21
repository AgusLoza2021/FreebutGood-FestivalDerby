namespace FestivalDerby.World
{
    /// <summary>
    /// One generated placement. A struct so a fill writes into a caller-owned buffer and the tick
    /// never allocates.
    /// </summary>
    public struct ScatterPoint
    {
        public float Distance;
        public float Lateral;
        public float Yaw;
        public float Scale;
        public int VariantIndex;
    }
}
