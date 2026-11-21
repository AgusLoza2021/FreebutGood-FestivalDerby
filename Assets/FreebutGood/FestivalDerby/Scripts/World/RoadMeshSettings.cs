using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Knobs for the road sweep. They are plain floats rather than a ScriptableObject because the
    /// sweep runs at bake time only: the runtime never needs to re-derive a mesh, so keeping the
    /// settings as a value lets the editor pass the authored bake resolution straight through and
    /// lets a test build a mesh with no asset at all.
    /// </summary>
    [Serializable]
    public struct RoadMeshSettings
    {
        /// <summary>Ring spacing on a straight, in metres. The upper bound on ring density.</summary>
        public float StraightSpacing;

        /// <summary>Ring spacing through a corner, in metres. Never larger than the straight step.</summary>
        public float CornerSpacing;

        /// <summary>|curvature| above which the corner step is used instead of the straight step.</summary>
        public float CornerCurvatureThreshold;

        /// <summary>Longest run of rings one mesh chunk may cover. Chunks split a long loop so no
        /// single mesh grows past the 16-bit index budget and each is culled on its own.</summary>
        public float ChunkLength;

        /// <summary>Metres of road one texture tile covers; turns arc length into UV.u and lateral
        /// metres into UV.v, so both axes share one physical scale and the texture does not stretch.</summary>
        public float MetersPerTile;

        public static RoadMeshSettings Defaults()
        {
            return new RoadMeshSettings
            {
                StraightSpacing = 3f,
                CornerSpacing = 1.5f,
                CornerCurvatureThreshold = 0.02f,
                ChunkLength = 220f,
                MetersPerTile = 4f,
            };
        }

        /// <summary>Replaces every value that would divide by zero or invert the density ordering.</summary>
        public RoadMeshSettings Sanitized()
        {
            RoadMeshSettings value = this;
            RoadMeshSettings fallback = Defaults();
            value.StraightSpacing = Positive(value.StraightSpacing, fallback.StraightSpacing);
            value.CornerSpacing = Positive(value.CornerSpacing, fallback.CornerSpacing);
            if (value.CornerSpacing > value.StraightSpacing)
                value.CornerSpacing = value.StraightSpacing;
            value.CornerCurvatureThreshold = Positive(value.CornerCurvatureThreshold, fallback.CornerCurvatureThreshold);
            value.ChunkLength = Positive(value.ChunkLength, fallback.ChunkLength);
            value.MetersPerTile = Positive(value.MetersPerTile, fallback.MetersPerTile);
            return value;
        }

        private static float Positive(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : value;
        }
    }
}
