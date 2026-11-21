namespace FestivalDerby.World
{
    /// <summary>
    /// Surface materials a baked path can carry. Byte-valued so a baked sample stores one in a
    /// single byte and the runtime switches grip, dust and sound without a lookup allocation.
    /// </summary>
    public enum SurfaceId : byte
    {
        Street = 0,
        Dirt = 1,
        Snow = 2,
        Mud = 3,
        Gravel = 4,
    }
}
