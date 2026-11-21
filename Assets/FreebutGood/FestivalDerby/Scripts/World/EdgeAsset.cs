using System;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>Which side of a road an edge follows, or a free-standing rail.</summary>
    public enum EdgeSide
    {
        Left = 0,
        Right = 1,
        Free = 2,
    }

    /// <summary>
    /// One box segment of a barrier line, pre-rotated and pre-scaled by the baker. Baking segments
    /// rather than a spline is what lets the runtime place plain BoxColliders with no curve math.
    /// </summary>
    [Serializable]
    public struct BakedEdgeSegment
    {
        public PathVector3 Center;
        public PathVector3 Size;
        public float YawDegrees;
    }

    /// <summary>
    /// A baked barrier line: a run of overlapping box segments. The overlap is the whole point of
    /// the asset — separate boxes leave a seam a car nose can catch, so each segment is authored
    /// slightly longer than its spacing.
    /// </summary>
    [CreateAssetMenu(fileName = "EdgeAsset", menuName = "Festival Derby/World/Edge Asset")]
    public sealed class EdgeAsset : ScriptableObject
    {
        public string EdgeId;
        public EdgeSide Side = EdgeSide.Left;

        /// <summary>Road this edge follows when <see cref="Side"/> is Left or Right.</summary>
        public int RoadIndex;

        /// <summary>Gap in metres from the asphalt edge to the inner face of the barrier.</summary>
        public float Offset = 0.5f;
        public float Height = 1.2f;

        /// <summary>Cross-section thickness; at least 1 m so a fast car cannot tunnel through.</summary>
        public float Thickness = 1f;
        public float SegmentLength = 3f;

        /// <summary>Extra length per segment so consecutive boxes overlap instead of butting.</summary>
        public float Overlap = 0.3f;
        public int Layer;

        public BakedEdgeSegment[] Segments;

        public int SegmentCount
        {
            get { return Segments == null ? 0 : Segments.Length; }
        }
    }
}
