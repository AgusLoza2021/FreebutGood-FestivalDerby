using FestivalDerby.Driving.Handling;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Which of the three shipped V2 presets a run drives with. An enum rather than a name string so a
    /// missing or mistyped asset degrades to the default instead of parsing.
    /// </summary>
    public enum HandlingPreset
    {
        /// <summary>Highest grip, slowest rotation. The safe teaching setup.</summary>
        Grippy = 0,

        /// <summary>The shipped default: the balance the V2a acceptance numbers were measured on.</summary>
        Balanced = 1,

        /// <summary>Low grip, high rotation. Slides early and holds them.</summary>
        Drifty = 2
    }

    /// <summary>
    /// The Unity-side selector for the engine-free <see cref="HandlingParams"/> presets. It is the seam
    /// the V2a core documented: "the Unity-side HandlingProfile asset copies into this struct, it is not
    /// read from the tick". The tick therefore only ever sees the plain value type, so the same numbers
    /// run on the client and on the server, and a mutated asset can never change a car mid-run.
    ///
    /// It selects a preset instead of exposing the forty-odd tunables as serialized fields on purpose:
    /// <see cref="HandlingParams"/> is a readonly struct with a private constructor, so a field-by-field
    /// copy would duplicate the whole table in a second place and drift from the core. A preset is
    /// exactly the granularity the V2b spec asks for.
    /// </summary>
    [CreateAssetMenu(fileName = "HandlingProfile", menuName = "Config/HandlingProfile")]
    public sealed class HandlingProfile : ScriptableObject
    {
        [Tooltip("Which V2 handling preset this profile selects.")]
        public HandlingPreset Preset = HandlingPreset.Balanced;

        // V2e.2 steering feel overrides. Convention is the same as DrivingFeatureConfig: 0 keeps the
        // preset value, so an asset that only names a preset behaves exactly as before the fields
        // existed. The owner tunes these in the Inspector; no tuning value ships from the mechanism.

        [Tooltip("Steering: how hard the demanded heading pulls the car. 0 keeps the preset value.")]
        public float HeadingGainOverride;

        [Tooltip("Steering: cap on yaw rate in radians per second. 0 keeps the preset value.")]
        public float MaxYawRateRadiansPerSecondOverride;

        [Tooltip("Steering: lateral acceleration budget in m/s^2 (how hard the car may corner at " +
                 "speed — the clamp that makes high-speed turns feel stiff). 0 keeps the preset value.")]
        public float LateralAccelBudgetMetersPerSecondSquaredOverride;

        [Tooltip("Steering: how fast the wheel angle tracks the stick, radians per second. " +
                 "0 keeps the preset value.")]
        public float SteerRateRadiansPerSecondOverride;

        [Tooltip("Longitudinal: multiplier on the preset's top speed (1.3 = 30% faster). 0 keeps the preset value. " +
                 "The bots drive this same profile, so it speeds them up too.")]
        public float TopSpeedScale;

        [Tooltip("Longitudinal: multiplier on the preset's full-throttle acceleration. 0 keeps the preset value.")]
        public float AccelScale;

        /// <summary>The preset this asset names.</summary>
        public HandlingParams Params
        {
            get { return Resolve(this); }
        }

        /// <summary>
        /// The preset an asset names, or Balanced for a null one. Never throws: a profile is content, and
        /// a run that cannot resolve its handling should still be drivable.
        /// </summary>
        public static HandlingParams Resolve(HandlingProfile profile)
        {
            return profile == null
                ? ResolvePreset(HandlingPreset.Balanced)
                : ResolveForPreset(profile.Preset, profile);
        }

        /// <summary>
        /// The named preset with this profile's steering overrides applied on top. Exists for the A/B
        /// preset switch, where the preset to run is chosen at runtime but the profile's overrides
        /// still belong to the car. A null profile is the bare preset, unchanged.
        /// </summary>
        public static HandlingParams ResolveForPreset(HandlingPreset preset, HandlingProfile overrides)
        {
            HandlingParams baseParams = ResolvePreset(preset);
            return overrides == null
                ? baseParams
                : baseParams.WithSteeringOverrides(
                        overrides.HeadingGainOverride,
                        overrides.MaxYawRateRadiansPerSecondOverride,
                        overrides.LateralAccelBudgetMetersPerSecondSquaredOverride,
                        overrides.SteerRateRadiansPerSecondOverride)
                    .WithLongitudinalScale(overrides.TopSpeedScale, overrides.AccelScale);
        }

        public static HandlingParams ResolvePreset(HandlingPreset preset)
        {
            switch (preset)
            {
                case HandlingPreset.Grippy:
                    return HandlingParams.Grippy;
                case HandlingPreset.Drifty:
                    return HandlingParams.Drifty;
                default:
                    return HandlingParams.Balanced;
            }
        }
    }
}
