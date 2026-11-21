namespace FestivalDerby.World
{
    /// <summary>One authored road knot: a ground-plane position and the path tangent there.</summary>
    public struct SymmetryKnot
    {
        public PathVector3 Position;
        public PathVector3 Tangent;

        public SymmetryKnot(PathVector3 position, PathVector3 tangent)
        {
            Position = position;
            Tangent = tangent;
        }
    }
}
