using System;

namespace FestivalDerby.World
{
    /// <summary>Snaps a ground-plane point to a grid of Step around Origin; Step &lt;= 0 disables snapping.</summary>
    public struct GridSnap
    {
        public float Step;
        public PathVector3 Origin;

        public GridSnap(float step, PathVector3 origin)
        {
            Step = step;
            Origin = origin;
        }

        public PathVector3 Snap(PathVector3 p)
        {
            if (Step <= 0f)
                return p;
            return new PathVector3(SnapOne(p.x, Origin.x), p.y, SnapOne(p.z, Origin.z));
        }

        /// <summary>Away from zero so a point halfway to the next cell lands on the same side of Origin.</summary>
        private float SnapOne(float x, float origin)
        {
            return origin + (float)Math.Round((x - origin) / Step, MidpointRounding.AwayFromZero) * Step;
        }
    }
}
