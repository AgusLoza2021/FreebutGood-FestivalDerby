using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Reports where the road ribbon self-folds: at a sample the inner edge curls past the center of
    /// curvature when innerHalfWidth &gt;= radius of curvature (radius = 1/|k|). Engine-free,
    /// allocation-free, warns and never repairs. One issue per contiguous pinch run, so a long fold
    /// is one report not N.
    /// </summary>
    public static class PathPinch
    {
        public static int Scan(BakedPath path, float widthMargin, MapIssue[] issues)
        {
            if (path == null || issues == null || !path.IsUsable)
                return 0;

            int count = path.Count;
            float[] curvature = path.Curvature;
            if (curvature == null || curvature.Length != count)
                return 0;
            if (path.WidthLeft == null || path.WidthLeft.Length != count
                || path.WidthRight == null || path.WidthRight.Length != count)
                return 0;

            // widthMargin is a safety fraction: 0 warns at the exact cusp, 0.1 warns 10% early. The
            // cap keeps a typo from collapsing the threshold to zero and flagging every sample.
            if (widthMargin < 0f)
                widthMargin = 0f;
            else if (widthMargin > 0.9f)
                widthMargin = 0.9f;
            float closing = 1f - widthMargin;

            int reported = 0;
            bool inRun = false;
            for (int i = 0; i < count; i++)
            {
                bool pinched = false;
                float k = Math.Abs(curvature[i]);
                if (k > 1e-6f)
                {
                    // Positive curvature turns left, so the centre of curvature sits left of the
                    // heading and the RIGHT edge is the inner one that folds; negative mirrors it.
                    float inner = curvature[i] > 0f ? path.WidthRight[i] : path.WidthLeft[i];
                    float radius = 1f / k;
                    pinched = inner >= radius * closing;
                }

                if (pinched)
                {
                    // Report only the first sample of a run; a fold that spans many samples is still
                    // one place the author has to widen, so one warning per run stays readable.
                    if (!inRun)
                    {
                        Add(issues, ref reported, i, path.Distances[i]);
                        inRun = true;
                    }
                }
                else
                {
                    inRun = false;
                }
            }

            return reported;
        }

        private static void Add(MapIssue[] issues, ref int count, int index, float distance)
        {
            if (count < issues.Length)
            {
                issues[count++] = new MapIssue
                {
                    Code = MapIssueCode.Pinch,
                    Severity = MapIssueSeverity.Warning,
                    Index = index,
                    Distance = distance,
                    MessageKey = "world.validation." + MapIssueCode.Pinch.ToString(),
                };
            }
        }
    }
}
