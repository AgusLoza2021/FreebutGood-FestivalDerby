using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Every tunable distance and density of the trackside dressing, in one struct. Keeping the
    /// numbers here instead of in the planner is what lets CS4b (and the level editor) retune the
    /// look of a circuit without touching placement logic, and lets a test build the exact rule set
    /// it needs. All distances are metres, angles are degrees.
    /// </summary>
    [Serializable]
    public struct TracksideRuleSet
    {
        // ------------------------------------------------------------- corners
        /// <summary>|curvature| above this is a corner; 0.02 is a 50 m radius.</summary>
        public float CornerCurvatureThreshold;

        /// <summary>Kerb run extends this far into the straight before a corner...</summary>
        public float KerbLeadIn;

        /// <summary>...and this far after it, so the paint does not stop dead at the apex exit.</summary>
        public float KerbLeadOut;

        public float KerbWidth;

        /// <summary>One red or white kerb block spans this many metres.</summary>
        public float KerbBlockLength;

        /// <summary>Longest kerb segment; a tighter step follows a tight corner more smoothly.</summary>
        public float KerbSegmentLength;

        // ------------------------------------------------------------- road markings
        public float EdgeLineWidth;
        public float EdgeLineSegmentLength;

        // ------------------------------------------------------------- run-off
        /// <summary>Only corners curving at least this hard get a gravel trap (fast corners).</summary>
        public float GravelCurvatureThreshold;

        /// <summary>Lateral depth of the gravel trap, measured outward from the asphalt edge.</summary>
        public float GravelDepth;

        // ------------------------------------------------------------- walls and fences
        public float TyreWallOffset;
        public float TyreWallSpacing;
        public float TyreWallWidth;

        public float FenceSpacing;
        public float FenceWidth;

        // ------------------------------------------------------------- far band dressing
        public float FarBandMinOffset;
        public float FarBandMaxOffset;
        public float FarBandSpacing;

        /// <summary>Along-path jitter applied to each far-band station; keeps rows from reading as a comb.</summary>
        public float FarBandJitter;

        /// <summary>Clearance the far band must keep from the asphalt edge.</summary>
        public float FarBandMargin;

        /// <summary>Reserved footprint radius used by the coarse occupancy grid.</summary>
        public float FarBandRadius;

        // ------------------------------------------------------------- start line
        public float FlagZoneDistance;
        public float FlagSpacing;

        /// <summary>Probability that a qualifying corner also gets a light breakable prop.</summary>
        public float CornerPropChance;

        /// <summary>
        /// The Circuit Superstars-leaning default: dense kerbs and edge lines, gravel on the quick
        /// corners, a walled run-off, fences on the straights and a populated far band.
        /// </summary>
        public static TracksideRuleSet CircuitDefault
        {
            get
            {
                return new TracksideRuleSet
                {
                    CornerCurvatureThreshold = 0.02f,
                    KerbLeadIn = 8f,
                    KerbLeadOut = 8f,
                    KerbWidth = 1.2f,
                    KerbBlockLength = 2f,
                    KerbSegmentLength = 1.5f,

                    EdgeLineWidth = 0.25f,
                    EdgeLineSegmentLength = 5f,

                    GravelCurvatureThreshold = 0.025f,
                    GravelDepth = 6f,

                    TyreWallOffset = 1.5f,
                    TyreWallSpacing = 2.5f,
                    TyreWallWidth = 0.6f,

                    FenceSpacing = 10f,
                    FenceWidth = 0.3f,

                    FarBandMinOffset = 8f,
                    FarBandMaxOffset = 26f,
                    FarBandSpacing = 12f,
                    FarBandJitter = 3f,
                    FarBandMargin = 1f,
                    FarBandRadius = 3.5f,

                    FlagZoneDistance = 14f,
                    FlagSpacing = 4f,

                    CornerPropChance = 0.5f,
                };
            }
        }
    }
}
