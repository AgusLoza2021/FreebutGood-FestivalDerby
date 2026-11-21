using System;

namespace FestivalDerby.World
{
    /// <summary>Rotates positions and tangents about Pivot in Count equal sectors; sector 0 is identity.</summary>
    public struct RotationalSymmetry
    {
        public int Count;
        public PathVector3 Pivot;

        public RotationalSymmetry(int count, PathVector3 pivot)
        {
            Count = count;
            Pivot = pivot;
        }

        public PathVector3 Position(PathVector3 p, int sector)
        {
            float angle = 2f * (float)Math.PI * sector / Count;
            float x = p.x - Pivot.x;
            float z = p.z - Pivot.z;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            return new PathVector3(Pivot.x + x * cos - z * sin, p.y, Pivot.z + x * sin + z * cos);
        }

        public PathVector3 Tangent(PathVector3 t, int sector)
        {
            float angle = 2f * (float)Math.PI * sector / Count;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            return new PathVector3(t.x * cos - t.z * sin, t.y, t.x * sin + t.z * cos);
        }
    }
}
