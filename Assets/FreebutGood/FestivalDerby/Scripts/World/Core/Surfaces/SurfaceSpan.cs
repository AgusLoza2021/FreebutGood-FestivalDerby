using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One authored arc-length surface span. Distances are metres along the path; TransitionLength
    /// is the blend width at each edge. Order is the authoring index, set during normalization and
    /// used to resolve overlaps: the later span wins.
    /// </summary>
    [Serializable]
    public struct SurfaceSpan
    {
        public float StartDistance;
        public float EndDistance;
        public float TransitionLength;
        public SurfaceId SurfaceId;
        internal int Order;

        public SurfaceSpan(float start, float end, SurfaceId id, float transitionLength = 0f)
        {
            StartDistance = start;
            EndDistance = end;
            SurfaceId = id;
            TransitionLength = transitionLength;
            Order = 0;
        }
    }
}
