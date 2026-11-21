using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Edit-mode contract for the config-driven big track: the numbers that decide the map, with no
    /// GameObjects involved. The PlayMode fixture proves the geometry those numbers produce, and
    /// that the null-track path still builds the inherited figure-eight.
    /// </summary>
    public sealed class TrackLayoutDefinitionEditModeTests
    {
        // The task states the lap as "approximately 437 m"; one metre is the tolerance it asked for.
        private const float LapTolerance = 1f;
        private const float ExpectedLap = 437.0796f;

        [Test]
        public void Defaults_DescribeTheShippedBigOval()
        {
            var settings = TrackLayoutSettings.Defaults;

            Assert.That(settings.Kind, Is.EqualTo(TrackLayoutKind.RoundedOval));
            Assert.That(settings.OuterWidth, Is.EqualTo(90f));
            Assert.That(settings.OuterLength, Is.EqualTo(150f));
            Assert.That(settings.TrackWidth, Is.EqualTo(16f));
            Assert.That(settings.CornerRadius, Is.EqualTo(25f));
            Assert.That(settings.WallHeight, Is.EqualTo(1.2f));
            Assert.That(settings.WallThickness, Is.EqualTo(0.5f));
            Assert.That(settings.RunOff, Is.EqualTo(5f));
            Assert.That(settings.RouteSampleCount, Is.EqualTo(48));
            Assert.That(settings.SpawnPoseCount, Is.EqualTo(6), "A 3x2 grid suits the six-car field.");
            Assert.That(settings.IsValid, Is.True);

            // A 90x150 centerline bounding box with a 25 m corner radius: two 100 m straights, two
            // 40 m straights and four quarter arcs, i.e. 2*(150-50) + 2*(90-50) + 2*pi*25.
            Assert.That(settings.CenterlineLapLength, Is.EqualTo(ExpectedLap).Within(LapTolerance));
            Assert.That(settings.CenterlineSize, Is.EqualTo(new Vector2(90f, 150f)));
            // Run-off grows the floor beyond the asphalt on every side, so the outer bounds fit.
            Assert.That(settings.SurfaceSize, Is.EqualTo(new Vector2(116f, 176f)));
        }

        [Test]
        public void Resolve_NullOrEmptyAssetFallsBackToTheShippedOval()
        {
            AssertShippedDefaults(TrackLayoutDefinition.Resolve(null));

            var asset = ScriptableObject.CreateInstance<TrackLayoutDefinition>();
            try
            {
                AssertShippedDefaults(TrackLayoutDefinition.Resolve(asset));
                AssertShippedDefaults(asset.ToSettings());
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Resolve_SanitizesBrokenNumbersToDefaultsNeverZero()
        {
            var asset = ScriptableObject.CreateInstance<TrackLayoutDefinition>();
            try
            {
                asset.OuterLength = 0f;
                asset.OuterWidth = float.NaN;
                asset.TrackWidth = -3f;
                asset.CornerRadius = float.PositiveInfinity;
                asset.WallHeight = 0f;
                asset.WallThickness = -1f;
                asset.RunOff = float.NaN;
                asset.RouteSampleCount = 0;
                asset.SpawnGrid = new TrackSpawnGrid(0, 0, float.NaN);
                asset.CameraOffset = new Vector3(float.NaN, 0f, 0f);

                var settings = TrackLayoutDefinition.Resolve(asset);
                Assert.That(settings.IsValid, Is.True, "A broken asset must still resolve to buildable numbers.");
                Assert.That(settings.OuterLength, Is.GreaterThan(0f));
                Assert.That(settings.OuterWidth, Is.GreaterThan(0f));
                Assert.That(settings.TrackWidth, Is.GreaterThan(0f));
                Assert.That(settings.CornerRadius, Is.GreaterThan(0f));
                Assert.That(settings.WallHeight, Is.GreaterThan(0f));
                Assert.That(settings.WallThickness, Is.GreaterThan(0f));
                Assert.That(settings.RouteSampleCount, Is.GreaterThanOrEqualTo(8));
                Assert.That(settings.SpawnGrid.IsValid, Is.True);
                Assert.That(settings.SpawnPoseCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Sanitize_RewritesInvalidSerializedFieldsInPlace()
        {
            var asset = ScriptableObject.CreateInstance<TrackLayoutDefinition>();
            try
            {
                asset.TrackWidth = 0f;
                asset.OuterWidth = -4f;
                asset.RouteSampleCount = -7;
                asset.Sanitize();

                Assert.That(asset.TrackWidth, Is.EqualTo(TrackLayoutDefinition.DefaultTrackWidth));
                Assert.That(asset.OuterWidth, Is.EqualTo(TrackLayoutDefinition.DefaultOuterWidth));
                Assert.That(asset.RouteSampleCount, Is.EqualTo(TrackLayoutDefinition.DefaultRouteSampleCount));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Resolve_ClampsCornerRadiusBetweenTheInnerFoldAndTheShortStraight()
        {
            var asset = ScriptableObject.CreateInstance<TrackLayoutDefinition>();
            try
            {
                asset.OuterWidth = 80f;
                asset.OuterLength = 140f;
                asset.TrackWidth = 16f;

                // A radius wider than the short side would collapse both straights into the corners.
                asset.CornerRadius = 500f;
                var wide = TrackLayoutDefinition.Resolve(asset);
                Assert.That(wide.CornerRadius, Is.GreaterThan(0f));
                Assert.That(wide.CornerRadius, Is.LessThanOrEqualTo(40f));

                // A radius below half the track width would fold the inner edge through itself.
                asset.CornerRadius = 0.5f;
                var tight = TrackLayoutDefinition.Resolve(asset);
                Assert.That(tight.CornerRadius, Is.GreaterThan(0.5f * tight.TrackWidth));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RouteSamples_AreOrderedAndEvenlySpacedAroundTheLap()
        {
            var settings = TrackLayoutSettings.Defaults;
            int count = settings.RouteSampleCount;
            float step = settings.CenterlineLapLength / count;
            float min = float.MaxValue;
            float max = 0f;
            var previous = SampleAt(settings, 0f);
            for (int i = 1; i <= count; i++)
            {
                var current = SampleAt(settings, i * step);
                float gap = (current - previous).magnitude;
                min = Mathf.Min(min, gap);
                max = Mathf.Max(max, gap);
                previous = current;
            }

            Assert.That(min, Is.GreaterThan(0f));
            // Straights and 25 m corners produce chords that differ by well under ten percent.
            Assert.That(max, Is.LessThan(min * 1.1f), $"Route chords are uneven: {min}..{max}");
            // The sample after a full lap is the first one again: the route is a closed loop.
            Assert.That((SampleAt(settings, settings.CenterlineLapLength) - SampleAt(settings, 0f)).magnitude,
                Is.LessThan(1e-3f));
        }

        [Test]
        public void SpawnGrid_HoldsTheSixCarGridOnTheTrackWithoutOverlap()
        {
            var settings = TrackLayoutSettings.Defaults;
            var center = new Vector2(10f, -7f);
            const float surfaceY = 0.25f;
            var poses = new Pose[settings.SpawnPoseCount];
            for (int i = 0; i < poses.Length; i++)
            {
                Assert.That(settings.TryGetSpawnPose(i, center, surfaceY, out poses[i]), Is.True);
                Assert.That(poses[i].position.y, Is.EqualTo(surfaceY).Within(1e-4f));
                Assert.That(Vector3.Dot(poses[i].forward, Vector3.forward), Is.GreaterThan(0.999f),
                    "Every grid slot faces the driving direction at the start line.");
                float lateral = DistanceToCenterline(settings, center,
                    new Vector2(poses[i].position.x, poses[i].position.z));
                Assert.That(lateral, Is.LessThan(0.5f * settings.TrackWidth),
                    $"Slot {i} must sit inside the asphalt ribbon.");
            }

            // Six slots with a quarter-car gap: nothing overlaps, and none is duplicated.
            for (int i = 0; i < poses.Length; i++)
                for (int j = i + 1; j < poses.Length; j++)
                {
                    var a = new Vector2(poses[i].position.x, poses[i].position.z);
                    var b = new Vector2(poses[j].position.x, poses[j].position.z);
                    Assert.That((a - b).magnitude, Is.GreaterThan(4f), $"Slots {i} and {j} overlap.");
                }

            Assert.That(settings.TryGetSpawnPose(poses.Length, center, surfaceY, out _), Is.False);
            Assert.That(settings.TryGetSpawnPose(-1, center, surfaceY, out _), Is.False);
        }

        [Test]
        public void CameraOverride_DefaultsToTheBigOvalPullBackAndCanBeDisabled()
        {
            var defaults = TrackLayoutSettings.Defaults;
            Assert.That(defaults.HasCameraOverride, Is.True);
            Assert.That(defaults.CameraOffset, Is.EqualTo(new Vector3(-14f, 24f, -12f)));
            Assert.That(defaults.CameraLookHeight, Is.EqualTo(2f));

            var asset = ScriptableObject.CreateInstance<TrackLayoutDefinition>();
            try
            {
                asset.HasCameraOverride = false;
                asset.CameraOffset = new Vector3(1f, 2f, 3f);
                var resolved = TrackLayoutDefinition.Resolve(asset);
                Assert.That(resolved.HasCameraOverride, Is.False);
                // A disabled override never leaks its stale numbers into the resolved settings.
                Assert.That(resolved.CameraOffset, Is.EqualTo(TrackLayoutDefinition.DefaultCameraOffset));
                Assert.That(resolved.CameraLookHeight, Is.EqualTo(TrackLayoutDefinition.DefaultCameraLookHeight));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ShippedBigOvalAsset_ImportsAndResolvesToTheDocumentedDimensions()
        {
            var asset = Resources.Load<TrackLayoutDefinition>("TrackLayoutDefinition_BigOval");
            Assert.That(asset, Is.Not.Null, "The shipped big-oval asset must import and resolve.");
            var settings = TrackLayoutDefinition.Resolve(asset);
            Assert.That(settings.Kind, Is.EqualTo(TrackLayoutKind.RoundedOval));
            Assert.That(settings.CenterlineLapLength, Is.EqualTo(ExpectedLap).Within(LapTolerance));
            Assert.That(settings.SurfaceSize, Is.EqualTo(new Vector2(116f, 176f)));
            Assert.That(settings.CameraOffset, Is.EqualTo(new Vector3(-14f, 24f, -12f)));
            // The lane clamp must not quietly shrink an authored grid that already fits.
            Assert.That(settings.SpawnGrid.Spacing, Is.EqualTo(4.5f),
                "The shipped 4.5 m grid spacing is inside the track, so resolving keeps it.");
            Assert.That(settings.SpawnPoseCount, Is.EqualTo(6));
        }

        private static void AssertShippedDefaults(TrackLayoutSettings settings)
        {
            var defaults = TrackLayoutSettings.Defaults;
            Assert.That(settings.Kind, Is.EqualTo(defaults.Kind));
            Assert.That(settings.OuterWidth, Is.EqualTo(defaults.OuterWidth));
            Assert.That(settings.OuterLength, Is.EqualTo(defaults.OuterLength));
            Assert.That(settings.TrackWidth, Is.EqualTo(defaults.TrackWidth));
            Assert.That(settings.CornerRadius, Is.EqualTo(defaults.CornerRadius));
            Assert.That(settings.WallHeight, Is.EqualTo(defaults.WallHeight));
            Assert.That(settings.WallThickness, Is.EqualTo(defaults.WallThickness));
            Assert.That(settings.RunOff, Is.EqualTo(defaults.RunOff));
            Assert.That(settings.RouteSampleCount, Is.EqualTo(defaults.RouteSampleCount));
            Assert.That(settings.SpawnGrid.Count, Is.EqualTo(defaults.SpawnGrid.Count));
            Assert.That(settings.SpawnGrid.Spacing, Is.EqualTo(defaults.SpawnGrid.Spacing));
            Assert.That(settings.CenterlineLapLength, Is.EqualTo(defaults.CenterlineLapLength).Within(1e-3f));
        }

        private static Vector2 SampleAt(in TrackLayoutSettings settings, float distance)
        {
            settings.CenterlineSample(distance, out var point, out _);
            return point;
        }

        private static float DistanceToCenterline(in TrackLayoutSettings settings, Vector2 center, Vector2 world)
        {
            const int probes = 2048;
            float lap = settings.CenterlineLapLength;
            float best = float.MaxValue;
            for (int i = 0; i < probes; i++)
            {
                settings.CenterlineSample(lap * i / probes, out var point, out _);
                best = Mathf.Min(best, (center + point - world).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }
    }
}
