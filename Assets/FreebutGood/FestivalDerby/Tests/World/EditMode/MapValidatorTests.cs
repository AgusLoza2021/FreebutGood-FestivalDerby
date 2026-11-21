using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Every validation rule fires on a broken map and stays quiet on a correct one, plus a valid
    /// oval that must produce no issues at all.
    /// </summary>
    [TestFixture]
    public sealed class MapValidatorTests
    {
        [Test]
        public void TooFewKnots_FiresBelowThreeAndPassesOnAValidSquare()
        {
            MapValidationInput input = ValidSquare();
            input.Knots = new[]
            {
                WorldTestFixtures.Knot(0f, 0f),
                WorldTestFixtures.Knot(5f, 0f),
            };

            AssertHas(input, MapIssueCode.TooFewKnots);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.TooFewKnots);
        }

        [Test]
        public void DegenerateLength_FiresOnACollapsedPathAndPassesOnARealLength()
        {
            MapValidationInput input = ValidSquare();
            input.Knots = new[]
            {
                WorldTestFixtures.Knot(0f, 0f),
                WorldTestFixtures.Knot(0f, 0f),
                WorldTestFixtures.Knot(0f, 0f),
            };
            input.PathLength = 0f;

            AssertHas(input, MapIssueCode.DegenerateLength);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.DegenerateLength);
        }

        [Test]
        public void LoopNotClosed_FiresOnAGapAndPassesWhenClosed()
        {
            MapValidationInput input = ValidSquare();
            input.Knots = new[]
            {
                WorldTestFixtures.Knot(0f, 0f),
                WorldTestFixtures.Knot(5f, 0f),
                WorldTestFixtures.Knot(5f, 5f),
            };

            AssertHas(input, MapIssueCode.LoopNotClosed);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.LoopNotClosed);
        }

        [Test]
        public void TightCurvature_FiresBelowMinimumRadiusAndPassesAbove()
        {
            MapValidationInput input = ValidSquare();
            input.Knots = new[]
            {
                WorldTestFixtures.Knot(0f, 0f),
                WorldTestFixtures.Knot(0.1f, 0f),
                WorldTestFixtures.Knot(0.1f, 0.1f),
            };
            input.MinimumRadius = 100f;

            AssertHas(input, MapIssueCode.TightCurvature);
            input.MinimumRadius = 0.01f;
            AssertDoesNotHave(input, MapIssueCode.TightCurvature);
        }

        [Test]
        public void SegmentOverlap_FiresOnCrossingSegmentsAndPassesOnAConvexLoop()
        {
            MapValidationInput input = ValidSquare();
            input.Knots = new[]
            {
                WorldTestFixtures.Knot(-2f, 0f),
                WorldTestFixtures.Knot(2f, 0f),
                WorldTestFixtures.Knot(2f, 2f),
                WorldTestFixtures.Knot(0f, -2f),
                WorldTestFixtures.Knot(0f, 2f),
                WorldTestFixtures.Knot(-2f, 2f),
            };

            AssertHas(input, MapIssueCode.SegmentOverlap);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.SegmentOverlap);
        }

        [Test]
        public void SurfaceCoverage_FiresOnAPartialProfileAndPassesWhenCovered()
        {
            MapValidationInput input = ValidSquare();
            input.SurfaceSpans = new[] { new SurfaceSpan(0f, 2f, SurfaceId.Dirt) };

            AssertHas(input, MapIssueCode.SurfaceCoverage);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.SurfaceCoverage);
        }

        [Test]
        public void SurfaceTransitionTooLong_FiresOnALongFadeAndPassesOnAShortOne()
        {
            MapValidationInput input = ValidSquare();
            input.PathLength = 10f;
            input.SurfaceSpans = new[] { new SurfaceSpan(0f, 10f, SurfaceId.Dirt, 11f) };

            AssertHas(input, MapIssueCode.SurfaceTransitionTooLong);
            input.SurfaceSpans = new[] { new SurfaceSpan(0f, 10f, SurfaceId.Dirt, 5f) };
            AssertDoesNotHave(input, MapIssueCode.SurfaceTransitionTooLong);
        }

        [Test]
        public void CheckpointOrder_FiresWhenDistanceDropsAndPassesWhenItRises()
        {
            MapValidationInput input = ValidSquare();
            input.Checkpoints = new[] { 4f, 3f };

            AssertHas(input, MapIssueCode.CheckpointOrder);
            AssertDoesNotHave(ValidSquare(), MapIssueCode.CheckpointOrder);
        }

        [Test]
        public void ValidOval_HasNoIssues()
        {
            const int segments = 24;
            var knots = new SymmetryKnot[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * (float)Math.PI * (i % segments) / segments;
                knots[i] = WorldTestFixtures.Knot(
                    20f * (float)Math.Cos(angle), 12f * (float)Math.Sin(angle));
            }

            float length = 0f;
            for (int i = 0; i < segments; i++)
                length += (knots[i + 1].Position - knots[i].Position).Magnitude;

            var input = new MapValidationInput
            {
                Knots = knots,
                ClosedExpected = true,
                ClosureTolerance = 0.001f,
                MinimumRadius = 1f,
                MinimumLength = 1f,
                OverlapTolerance = 0.05f,
                PathLength = length,
                SurfaceSpans = new[] { new SurfaceSpan(0f, length, SurfaceId.Street) },
                Checkpoints = new[] { length * 0.25f, length * 0.5f, length * 0.75f },
            };

            var issues = new MapIssue[16];
            int count = MapValidator.Validate(input, issues);

            Assert.That(count, Is.Zero, count > 0 ? issues[0].Code.ToString() : string.Empty);
        }

        private static MapValidationInput ValidSquare()
        {
            return new MapValidationInput
            {
                Knots = new[]
                {
                    WorldTestFixtures.Knot(0f, 0f),
                    WorldTestFixtures.Knot(5f, 0f),
                    WorldTestFixtures.Knot(5f, 5f),
                    WorldTestFixtures.Knot(0f, 5f),
                    WorldTestFixtures.Knot(0f, 0f),
                },
                ClosedExpected = true,
                ClosureTolerance = 0.01f,
                MinimumRadius = 1f,
                MinimumLength = 1f,
                OverlapTolerance = 0.1f,
                PathLength = 20f,
                SurfaceSpans = new[] { new SurfaceSpan(0f, 20f, SurfaceId.Street) },
                Checkpoints = new[] { 4f, 9f, 14f },
            };
        }

        private static void AssertHas(MapValidationInput input, MapIssueCode expected)
        {
            var issues = new MapIssue[32];
            int count = MapValidator.Validate(input, issues);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (issues[i].Code == expected)
                    found = true;
            }

            Assert.That(found, Is.True, "expected " + expected);
        }

        private static void AssertDoesNotHave(MapValidationInput input, MapIssueCode unexpected)
        {
            var issues = new MapIssue[32];
            int count = MapValidator.Validate(input, issues);
            for (int i = 0; i < count; i++)
                Assert.That(issues[i].Code, Is.Not.EqualTo(unexpected), "unexpected " + unexpected);
        }
    }
}
