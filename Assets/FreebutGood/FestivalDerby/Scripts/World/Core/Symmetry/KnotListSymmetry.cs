namespace FestivalDerby.World
{
    /// <summary>
    /// Builds a full knot list from one authored half. The buffer is caller-owned, so an editor
    /// operation never allocates on the game thread, and the weld drops the seam duplicate.
    /// </summary>
    public static class KnotListSymmetry
    {
        public static int Apply(SymmetryKnot[] source, SymmetryKnot[] output, MirrorTransform mirror, float tolerance)
        {
            return mirror.Apply(source, output, tolerance);
        }

        public static int Apply(SymmetryKnot[] source, SymmetryKnot[] output, RotationalSymmetry symmetry, float tolerance)
        {
            if (source == null || output == null || symmetry.Count < 1)
                return 0;

            int count = 0;
            for (int sector = 0; sector < symmetry.Count && count + source.Length <= output.Length; sector++)
            {
                for (int i = 0; i < source.Length; i++)
                    output[count++] = new SymmetryKnot(
                        symmetry.Position(source[i].Position, sector),
                        symmetry.Tangent(source[i].Tangent, sector));
            }

            return MirrorTransform.Weld(output, count, tolerance);
        }
    }
}
