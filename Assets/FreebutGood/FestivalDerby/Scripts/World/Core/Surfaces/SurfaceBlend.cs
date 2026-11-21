namespace FestivalDerby.World
{
    /// <summary>
    /// The surface at one distance. A consumer lerps Primary toward Secondary by Blend/255, so a
    /// mobile road shader samples at most two layers and a third can never be expressed.
    /// </summary>
    public struct SurfaceBlend
    {
        public SurfaceId Primary;
        public SurfaceId Secondary;
        public byte Blend;

        public SurfaceBlend(SurfaceId primary, SurfaceId secondary, byte blend)
        {
            Primary = primary;
            Secondary = secondary;
            Blend = blend;
        }
    }
}
