using FestivalDerby.Driving.Handling;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// The V2e.2 steering feel override contract: the four steering knobs a HandlingProfile asset
    /// overrides are applied as a pure copy, zero means "keep the preset value", and applying an
    /// override never accumulates into the base the next call starts from.
    /// </summary>
    [TestFixture]
    public class HandlingSteeringOverrideTests
    {
        private const float HeadingGainOverride = 7f;
        private const float MaxYawRateOverride = 2.4f;
        private const float LateralBudgetOverride = 21f;
        private const float SteerRateOverride = 5.5f;

        [Test]
        public void NonZeroOverride_ChangesTheCorrespondingSteeringField()
        {
            HandlingParams overridden = HandlingParams.Balanced.WithSteeringOverrides(
                HeadingGainOverride, MaxYawRateOverride, LateralBudgetOverride, SteerRateOverride);

            Assert.AreEqual(HeadingGainOverride, overridden.HeadingGain);
            Assert.AreEqual(MaxYawRateOverride, overridden.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(LateralBudgetOverride, overridden.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(SteerRateOverride, overridden.SteerRateRadiansPerSecond);
        }

        [Test]
        public void NonZeroOverride_LeavesEveryOtherSteeringFieldAtThePresetValue()
        {
            HandlingParams preset = HandlingParams.Balanced;
            HandlingParams overridden = preset.WithSteeringOverrides(
                HeadingGainOverride, 0f, LateralBudgetOverride, 0f);

            Assert.AreEqual(HeadingGainOverride, overridden.HeadingGain);
            Assert.AreEqual(preset.MaxYawRateRadiansPerSecond, overridden.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(LateralBudgetOverride, overridden.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(preset.SteerRateRadiansPerSecond, overridden.SteerRateRadiansPerSecond);
        }

        [Test]
        public void ZeroOverride_PreservesEveryPresetSteeringValue()
        {
            HandlingParams preset = HandlingParams.Balanced;
            HandlingParams overridden = preset.WithSteeringOverrides(0f, 0f, 0f, 0f);

            Assert.AreEqual(preset.HeadingGain, overridden.HeadingGain);
            Assert.AreEqual(preset.MaxYawRateRadiansPerSecond, overridden.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(preset.LateralAccelBudgetMetersPerSecondSquared,
                overridden.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(preset.SteerRateRadiansPerSecond, overridden.SteerRateRadiansPerSecond);
        }

        [Test]
        public void TwoIndependentCalls_FromTheSameBase_DoNotAccumulate()
        {
            HandlingParams preset = HandlingParams.Balanced;

            HandlingParams first = preset.WithSteeringOverrides(
                HeadingGainOverride, MaxYawRateOverride, LateralBudgetOverride, SteerRateOverride);
            HandlingParams second = preset.WithSteeringOverrides(
                HeadingGainOverride, MaxYawRateOverride, LateralBudgetOverride, SteerRateOverride);

            Assert.AreEqual(first.HeadingGain, second.HeadingGain);
            Assert.AreEqual(first.MaxYawRateRadiansPerSecond, second.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(first.LateralAccelBudgetMetersPerSecondSquared,
                second.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(first.SteerRateRadiansPerSecond, second.SteerRateRadiansPerSecond);
        }

        [Test]
        public void ChainedCall_StartingFromAnOverriddenCopy_DoesNotCompoundTheValues()
        {
            HandlingParams first = HandlingParams.Balanced.WithSteeringOverrides(
                HeadingGainOverride, MaxYawRateOverride, LateralBudgetOverride, SteerRateOverride);
            HandlingParams second = first.WithSteeringOverrides(0f, 0f, 0f, 0f);

            Assert.AreEqual(HeadingGainOverride, second.HeadingGain);
            Assert.AreEqual(MaxYawRateOverride, second.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(LateralBudgetOverride, second.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(SteerRateOverride, second.SteerRateRadiansPerSecond);
        }

        [Test]
        public void Presets_AreUnaffectedByAnOverrideCall()
        {
            HandlingParams balancedBefore = HandlingParams.Balanced;
            HandlingParams.Balanced.WithSteeringOverrides(
                HeadingGainOverride, MaxYawRateOverride, LateralBudgetOverride, SteerRateOverride);

            Assert.AreEqual(balancedBefore.HeadingGain, HandlingParams.Balanced.HeadingGain);
            Assert.AreEqual(balancedBefore.MaxYawRateRadiansPerSecond,
                HandlingParams.Balanced.MaxYawRateRadiansPerSecond);
            Assert.AreEqual(balancedBefore.LateralAccelBudgetMetersPerSecondSquared,
                HandlingParams.Balanced.LateralAccelBudgetMetersPerSecondSquared);
            Assert.AreEqual(balancedBefore.SteerRateRadiansPerSecond,
                HandlingParams.Balanced.SteerRateRadiansPerSecond);
        }
    }
}
