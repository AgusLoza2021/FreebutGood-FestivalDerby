namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Shared engine-free fixtures for the World.Core tests: a straight baked path and a knot
    /// factory. Kept in one place so every test builds the same shapes.
    /// </summary>
    internal static class WorldTestFixtures
    {
        /// <summary>A straight path along +X with <paramref name="count"/> samples and no surfaces.</summary>
        public static BakedPath Straight(float length, int count)
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[count],
                Tangents = new PathVector3[count],
                Ups = new PathVector3[count],
                WidthLeft = new float[count],
                WidthRight = new float[count],
                Banking = new float[count],
                Distances = new float[count],
                TotalLength = length,
            };

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                path.Positions[i] = new PathVector3(length * t, 0f, 0f);
                path.Tangents[i] = new PathVector3(1f, 0f, 0f);
                path.Ups[i] = PathVector3.Up;
                path.Distances[i] = length * t;
            }

            return path;
        }

        /// <summary>A ground-plane knot with an up tangent; only the position matters in most tests.</summary>
        public static SymmetryKnot Knot(float x, float z)
        {
            return new SymmetryKnot(new PathVector3(x, 0f, z), PathVector3.Up);
        }
    }
}
