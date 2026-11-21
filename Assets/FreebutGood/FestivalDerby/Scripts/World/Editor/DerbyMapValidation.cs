using System;
using FestivalDerby.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// Runs Core's MapValidator over what is currently authored and keeps the result so a Scene View
    /// overlay can list it and frame the offending point. Nothing here repairs or bakes; the overlay
    /// decides what to do with the answer, which is what keeps the validator the only place that
    /// knows the rules.
    /// </summary>
    public static class DerbyMapValidation
    {
        private const int IssueCapacity = 64;
        private static readonly MapIssue[] Buffer = new MapIssue[IssueCapacity];
        private static readonly MapIssue[] PinchBuffer = new MapIssue[IssueCapacity];

        /// <summary>
        /// Warn fraction before the exact cusp. A cusp that only just folds still barely shows on
        /// screen, so the warning has to lead the geometry instead of matching it.
        /// </summary>
        private const float PinchWidthMargin = 0.1f;

        /// <summary>Issues from the last Validate call, trimmed to the count the validator reported.</summary>
        public static MapIssue[] LastIssues { get; private set; } = new MapIssue[0];

        /// <summary>Baked length the last validation used, so a distance issue can be mapped back to t.</summary>
        public static float LastLength { get; private set; }

        public static int Validate(DerbyMapAuthoring authoring)
        {
            LastIssues = new MapIssue[0];
            LastLength = 0f;
            if (authoring == null)
            {
                return 0;
            }

            authoring.Sanitize();
            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            if (container == null || container.Splines.Count == 0)
            {
                return 0;
            }

            Vector3[] world = DerbyMapGeometry.ReadWorldKnots(container);
            Vector3[] tangents = DerbyMapGeometry.ReadWorldTangents(world, authoring.ClosedLoop);
            var knots = new SymmetryKnot[world.Length];
            for (int i = 0; i < world.Length; i++)
            {
                knots[i] = new SymmetryKnot(DerbyMapGeometry.ToPath(world[i]), DerbyMapGeometry.ToPath(tangents[i]));
            }

            // A pinch only exists once the ribbon is sampled with real curvature and widths, so the
            // knot-only validator can never see one. Baking here produces the same path the Bake
            // button would, which keeps the warning the author sees honest about what will be built.
            BakedPath path;
            float length;
            try
            {
                path = MapBaker.BakePath(container, container.Splines[0], DerbyMapBake.SettingsFrom(authoring));
                length = path != null ? path.TotalLength : 0f;
            }
            catch (ArgumentException)
            {
                // A degenerate spline is a validation result, not an exception an overlay button throws.
                path = null;
                length = 0f;
            }

            var input = new MapValidationInput
            {
                Knots = knots,
                ClosedExpected = authoring.ClosedLoop,
                SurfaceSpans = authoring.SurfaceSpans,
                PathLength = length,
                PathClosed = authoring.ClosedLoop,
            };

            int count = MapValidator.Validate(input, Buffer);

            // PathPinch already returns 0 for a null or unusable path, so the refused bake above
            // needs no second guard here.
            int pinchCount = PathPinch.Scan(path, PinchWidthMargin, PinchBuffer);

            var issues = new MapIssue[count + pinchCount];
            Array.Copy(Buffer, issues, count);
            Array.Copy(PinchBuffer, 0, issues, count, pinchCount);
            LastIssues = issues;
            LastLength = length;
            return count + pinchCount;
        }

        /// <summary>Short label for the overlay row. The message key is what a localized UI would show.</summary>
        public static string Describe(MapIssue issue)
        {
            string where = issue.Distance > 0f
                ? issue.Distance.ToString("0.0") + " m"
                : "knot " + issue.Index;
            return issue.Severity + ": " + issue.Code + " at " + where;
        }

        /// <summary>
        /// Points the Scene View at the issue: the sampled position when the issue carries a distance
        /// along the baked path, otherwise the knot it names, otherwise the whole loop.
        /// </summary>
        public static bool Frame(MapIssue issue, DerbyMapAuthoring authoring)
        {
            if (authoring == null)
            {
                return false;
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return false;
            }

            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            bool framed = false;
            if (issue.Distance > 0f && LastLength > 0f && container != null && container.Splines.Count > 0)
            {
                Vector3 point = container.EvaluatePosition(Mathf.Clamp01(issue.Distance / LastLength));
                view.Frame(new Bounds(point, Vector3.one * 12f), false);
                framed = true;
            }

            if (!framed)
            {
                Vector3[] knots = DerbyMapGeometry.ReadWorldKnots(container);
                if (issue.Index >= 0 && issue.Index < knots.Length)
                {
                    view.Frame(new Bounds(knots[issue.Index], Vector3.one * 12f), false);
                    framed = true;
                }
            }

            if (!framed && DerbyMapGeometry.TryGetBounds(container, out Bounds bounds))
            {
                view.Frame(bounds, false);
                framed = true;
            }

            if (framed)
            {
                view.Repaint();
            }

            return framed;
        }
    }
}
