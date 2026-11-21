namespace FestivalDerby.World
{
    /// <summary>
    /// Everything the validator reads for one map, in one bag: knots, tolerances, the baked
    /// distances and checkpoints, the authored surface spans and the path totals. The validator
    /// mutates none of it.
    /// </summary>
    public sealed class MapValidationInput
    {
        public SymmetryKnot[] Knots;
        public bool ClosedExpected;
        public float ClosureTolerance = 0.1f;
        public float MinimumRadius = 5f;
        public float MinimumLength = 0.1f;
        public float OverlapTolerance = 0.05f;
        public float[] Distances;
        public float[] Checkpoints;
        public SurfaceSpan[] SurfaceSpans;
        public float PathLength;
        public bool PathClosed;
    }
}
