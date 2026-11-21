namespace FestivalDerby.World
{
    /// <summary>
    /// The validation rules. Each one maps to the localization key "world.validation." + name, so
    /// the core can report an issue without knowing anything about localization.
    /// </summary>
    public enum MapIssueCode : byte
    {
        TooFewKnots,
        DegenerateLength,
        LoopNotClosed,
        TightCurvature,
        SegmentOverlap,
        SurfaceCoverage,
        SurfaceTransitionTooLong,
        CheckpointOrder,

        /// <summary>The road ribbon self-folds: the inner edge reaches the local centre of curvature.</summary>
        Pinch,
    }
}
