using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One planned trackside piece, in the engine-free form the World core and the headless server
    /// read. A strip (kerb, edge line, gravel) carries its arc-length span in
    /// <see cref="DistanceAlong"/> and <see cref="LengthMetres"/>; a discrete prop carries its
    /// centre in X/Z and its footprint in the two size fields. Everything a loader needs to spawn a
    /// prefab without re-running the planner is here.
    /// </summary>
    [Serializable]
    public struct TracksidePlacement
    {
        public TracksideKind Kind;

        /// <summary>Planar centre of the piece; strips report their midpoint, not their start.</summary>
        public float X;
        public float Z;
        public float YawDegrees;

        /// <summary>-1 left of the driving heading, +1 right, 0 centred (no planner output is 0 yet).</summary>
        public int Side;

        /// <summary>Arc length where a strip begins, or the position of a discrete prop.</summary>
        public float DistanceAlong;

        /// <summary>Along-path size in metres; zero for a prop with no meaningful extent.</summary>
        public float LengthMetres;

        /// <summary>Lateral size in metres.</summary>
        public float WidthMetres;

        public bool Collider;

        /// <summary>Opt-in for the breakable light props (cones, boxes).</summary>
        public bool Breakable;

        public TracksidePlacement(TracksideKind kind, float x, float z, float yawDegrees, int side,
            float distanceAlong, float lengthMetres, float widthMetres, bool collider, bool breakable)
        {
            Kind = kind;
            X = x;
            Z = z;
            YawDegrees = yawDegrees;
            Side = side;
            DistanceAlong = distanceAlong;
            LengthMetres = lengthMetres;
            WidthMetres = widthMetres;
            Collider = collider;
            Breakable = breakable;
        }
    }
}
