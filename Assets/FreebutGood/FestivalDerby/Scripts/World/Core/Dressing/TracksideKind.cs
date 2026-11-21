namespace FestivalDerby.World
{
    /// <summary>
    /// The vocabulary of trackside pieces the dressing planner can place. This is the engine-free
    /// contract CS4b maps onto the Kenney Racing Kit models; the order is map data, so existing
    /// values are appended to, never renumbered. A barrier is listed even though the planner does
    /// not emit one today: the baked <c>EdgeAsset</c> boxes already are the barriers, and CS4b needs
    /// the same id to render them consistently with the rest of the kit.
    /// </summary>
    public enum TracksideKind : byte
    {
        Kerb = 0,
        EdgeLine = 1,
        GravelTrap = 2,
        TyreWall = 3,
        Barrier = 4,
        Fence = 5,
        Grandstand = 6,
        Tree = 7,
        Tent = 8,
        Flag = 9,
        BannerTower = 10,
        LightPost = 11,
        Billboard = 12,
        Cone = 13,
        Box = 14,
    }

    /// <summary>
    /// Per-kind facts the planner and the loader both need, kept next to the enum so a new kind
    /// cannot be added without deciding whether it blocks a car and whether it is a strip.
    /// </summary>
    public static class TracksideKindInfo
    {
        /// <summary>
        /// Physical props a car can hit. Flat markings (lines, kerbs, gravel) never collide: they
        /// are surfaces, not obstacles. Far-band dressing is deliberately non-colliding so a
        /// decorative prop can never alter a racing line.
        /// </summary>
        public static bool Collides(TracksideKind kind)
        {
            switch (kind)
            {
                case TracksideKind.TyreWall:
                case TracksideKind.Barrier:
                case TracksideKind.Fence:
                case TracksideKind.Cone:
                case TracksideKind.Box:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A strip runs along the path and is built by <see cref="KerbStripMesh"/>.</summary>
        public static bool IsStrip(TracksideKind kind)
        {
            return kind == TracksideKind.Kerb
                || kind == TracksideKind.EdgeLine
                || kind == TracksideKind.GravelTrap;
        }

        /// <summary>Dressing placed in the far band, isolated by the occupancy grid.</summary>
        public static bool IsFarBand(TracksideKind kind)
        {
            switch (kind)
            {
                case TracksideKind.Grandstand:
                case TracksideKind.Tree:
                case TracksideKind.Tent:
                case TracksideKind.Flag:
                case TracksideKind.BannerTower:
                case TracksideKind.LightPost:
                case TracksideKind.Billboard:
                    return true;
                default:
                    return false;
            }
        }
    }
}
