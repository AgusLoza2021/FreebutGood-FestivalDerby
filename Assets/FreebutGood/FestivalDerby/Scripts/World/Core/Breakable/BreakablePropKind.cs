namespace FestivalDerby.World
{
    /// <summary>
    /// The three light props the GDD allows to break (cones, boxes and signs). Structural barriers
    /// and route-defining geometry are deliberately absent: breaking them would move the racing line,
    /// which the GDD forbids. The numbering is part of the map data and the debris VFX selection, so
    /// existing values are never renumbered.
    /// </summary>
    public enum BreakablePropKind : byte
    {
        Cone = 0,
        Box = 1,
        Sign = 2,
    }
}
