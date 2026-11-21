namespace FestivalDerby.World
{
    /// <summary>
    /// Mirrors positions and tangents about a plane through Pivot on the X or Z axis. Apply reverses
    /// the knot order and negates the mirrored tangents, so the mirrored half is traversed back
    /// toward the seam and the loop keeps its direction instead of flipping it.
    /// </summary>
    public struct MirrorTransform
    {
        public GridAxis Axis;
        public PathVector3 Pivot;

        public MirrorTransform(GridAxis axis, PathVector3 pivot)
        {
            Axis = axis;
            Pivot = pivot;
        }

        public PathVector3 Position(PathVector3 p)
        {
            if (Axis == GridAxis.X)
                p.x = 2f * Pivot.x - p.x;
            else
                p.z = 2f * Pivot.z - p.z;
            return p;
        }

        public PathVector3 Tangent(PathVector3 t)
        {
            if (Axis == GridAxis.X)
                t.x = -t.x;
            else
                t.z = -t.z;
            return t;
        }

        public int Apply(SymmetryKnot[] source, SymmetryKnot[] output, float tolerance)
        {
            if (source == null || output == null)
                return 0;

            int length = source.Length;
            int offset = 0;
            for (int i = length - 1; i >= 0 && offset < output.Length; i--)
                output[offset++] = new SymmetryKnot(Position(source[i].Position), Tangent(source[i].Tangent) * -1f);
            return Weld(output, offset, tolerance);
        }

        /// <summary>Drops the closing duplicate when the mirrored end lands on the first knot.</summary>
        internal static int Weld(SymmetryKnot[] knots, int count, float tolerance)
        {
            if (count > 1 && (knots[0].Position - knots[count - 1].Position).SqrMagnitude <= tolerance * tolerance)
                return count - 1;
            return count;
        }
    }
}
