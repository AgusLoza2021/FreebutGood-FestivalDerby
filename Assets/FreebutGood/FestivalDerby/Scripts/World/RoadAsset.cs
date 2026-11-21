using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// One baked road: a centerline path plus the asphalt width the baker used to fill it. The
    /// asset carries no spline reference, so a build, a server or a bot can read it without the
    /// spline package ever being loaded.
    /// </summary>
    [CreateAssetMenu(fileName = "RoadAsset", menuName = "Festival Derby/World/Road Asset")]
    public sealed class RoadAsset : ScriptableObject
    {
        public string RoadId;
        public BakedPath Path;

        /// <summary>Authored total asphalt width in metres, halved into the path's left/right widths.</summary>
        public float Width = 8f;
        public float BankingDegrees;

        /// <summary>
        /// Baking resolution: long steps on a straight, short steps through a corner, chosen by
        /// comparing |curvature| against <see cref="CornerCurvatureThreshold"/>. Rings this dense
        /// keep the chord error under a few centimetres at the tightest radius.
        /// </summary>
        public float StraightSampleStep = 3f;
        public float CornerSampleStep = 1.5f;
        public float CornerCurvatureThreshold = 0.02f;

        public int GroundLayer;

        /// <summary>
        /// The swept road pieces, one mesh per chunk, written by the bake and consumed by the loader.
        /// Null or empty means an older map: the loader falls back to building one ribbon mesh from
        /// the path, exactly as it did before chunks existed, so a pre-sweep asset still loads.
        /// </summary>
        public RoadMeshChunk[] Chunks;
    }
}
