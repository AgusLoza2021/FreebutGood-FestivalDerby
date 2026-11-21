using FestivalDerby.Driving.Handling;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The profile's speed scale multiplies top speed and full-throttle acceleration and nothing else, and
    /// an unset scale keeps the preset exactly as it was.
    /// </summary>
    public sealed class HandlingSpeedScaleTests
    {
        [Test]
        public void Scale_MultipliesTopSpeedAndAcceleration_AndLeavesCorneringAlone()
        {
            HandlingParams baseline = HandlingParams.Balanced;
            HandlingParams scaled = baseline.WithLongitudinalScale(1.3f, 1.3f);

            Assert.That(scaled.TopSpeedMetersPerSecond, Is.EqualTo(baseline.TopSpeedMetersPerSecond * 1.3f).Within(1e-4f));
            Assert.That(scaled.AccelMaxMetersPerSecondSquared,
                Is.EqualTo(baseline.AccelMaxMetersPerSecondSquared * 1.3f).Within(1e-4f));
            Assert.That(scaled.LateralAccelBudgetMetersPerSecondSquared,
                Is.EqualTo(baseline.LateralAccelBudgetMetersPerSecondSquared));
            Assert.That(scaled.HeadingGain, Is.EqualTo(baseline.HeadingGain));
            Assert.That(scaled.BrakeDecelMetersPerSecondSquared, Is.EqualTo(baseline.BrakeDecelMetersPerSecondSquared));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UnusableScale_KeepsThePresetValue(float scale)
        {
            HandlingParams baseline = HandlingParams.Balanced;
            HandlingParams result = baseline.WithLongitudinalScale(scale, scale);

            Assert.That(result.TopSpeedMetersPerSecond, Is.EqualTo(baseline.TopSpeedMetersPerSecond));
            Assert.That(result.AccelMaxMetersPerSecondSquared, Is.EqualTo(baseline.AccelMaxMetersPerSecondSquared));
        }

        [Test]
        public void ProfileScale_AppliesThroughTheProfile_AndAnUnsetProfileIsUntouched()
        {
            var profile = ScriptableObject.CreateInstance<HandlingProfile>();
            try
            {
                HandlingParams bare = HandlingProfile.ResolvePreset(profile.Preset);
                HandlingParams unset = HandlingProfile.Resolve(profile);
                Assert.That(unset.TopSpeedMetersPerSecond, Is.EqualTo(bare.TopSpeedMetersPerSecond));
                Assert.That(unset.AccelMaxMetersPerSecondSquared, Is.EqualTo(bare.AccelMaxMetersPerSecondSquared));

                profile.TopSpeedScale = 1.3f;
                profile.AccelScale = 1.3f;
                HandlingParams scaled = HandlingProfile.Resolve(profile);
                Assert.That(scaled.TopSpeedMetersPerSecond, Is.EqualTo(bare.TopSpeedMetersPerSecond * 1.3f).Within(1e-4f));
                Assert.That(scaled.AccelMaxMetersPerSecondSquared,
                    Is.EqualTo(bare.AccelMaxMetersPerSecondSquared * 1.3f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
