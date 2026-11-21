using UnityEngine;
using UnityEngine.Rendering;

namespace FestivalDerby.Look
{
    /// <summary>
    /// The sanitized, immutable look that the camera wiring reads and the applier consumes. Built
    /// once per run from a <see cref="LookProfile"/> so nothing downstream has to re-check a field
    /// and no reader can disagree with another about what "unset" means.
    /// </summary>
    /// <remarks>
    /// Every field is non-null and in range: a null, disabled or hand-broken profile resolves to
    /// <see cref="Enabled"/> false with the tuned defaults behind it, which is why the applier's
    /// kill switch is this one bool and not a null check at each call site.
    /// </remarks>
    public readonly struct LookSettings
    {
        /// <summary>False leaves every inherited camera, light and material untouched.</summary>
        public readonly bool Enabled;

        public readonly Vector3 CameraOffset;
        public readonly float CameraLookHeight;
        public readonly float CameraFov;
        public readonly float CameraFollowSharpness;
        public readonly float LookAheadMetresPerSpeed;
        public readonly float LookAheadMaxMetres;
        public readonly float LookAheadSharpness;

        /// <summary>Extra field of view at full speed. 0 means the framing keeps one constant FOV.</summary>
        public readonly float SpeedFovWidenDegrees;
        public readonly float SpeedFovAtMetresPerSecond;
        public readonly float SpeedFovSharpness;

        /// <summary>How the camera bearing moves; Fixed keeps one world bearing.</summary>
        public readonly CameraYawMode YawMode;
        public readonly Vector3 ChaseOffset;
        public readonly float PivotAheadMetres;
        public readonly float TrackLookAheadMetres;
        public readonly float YawSharpness;
        public readonly float YawMaxDegreesPerSecond;

        public readonly Color SunColor;
        public readonly float SunIntensity;
        public readonly Vector3 SunEulerAngles;
        public readonly float SunShadowStrength;
        public readonly bool AmbientTrilight;
        public readonly Color AmbientSky;
        public readonly Color AmbientEquator;
        public readonly Color AmbientGround;
        public readonly Color CameraBackground;

        public readonly bool PaletteOverrideEnabled;
        public readonly bool FlatAsphaltTexture;
        public readonly Color AsphaltColor;
        public readonly Color GroundColor;
        public readonly Color WallColor;
        public readonly Color KerbColor;

        public readonly bool GroundFillEnabled;
        public readonly float GroundFillMargin;
        public readonly float GroundFillDepth;

        /// <summary>Global volume profile for the run; null means post-processing is left alone.</summary>
        public readonly VolumeProfile PostProfile;

        public LookSettings(LookProfile profile)
        {
            if (profile == null || !profile.LookProfileEnabled)
            {
                // Disabled is a complete value, not a hole: the camera wiring and the applier both
                // read the same defaults here, so a disabled run needs no separate code path.
                Enabled = false;
                CameraOffset = LookDefaults.CameraOffset;
                CameraLookHeight = LookDefaults.CameraLookHeight;
                CameraFov = LookDefaults.CameraFov;
                CameraFollowSharpness = LookDefaults.CameraFollowSharpness;
                LookAheadMetresPerSpeed = 0f;
                LookAheadMaxMetres = 0f;
                LookAheadSharpness = LookDefaults.LookAheadSharpness;
                // A disabled profile is a promise to leave the camera alone, and the field of view is
                // part of the camera: no widen, even though the widen is purely speed-driven.
                SpeedFovWidenDegrees = 0f;
                SpeedFovAtMetresPerSecond = LookDefaults.SpeedFovAtMetresPerSecond;
                SpeedFovSharpness = LookDefaults.SpeedFovSharpness;
                YawMode = CameraYawMode.Fixed;
                ChaseOffset = Vector3.zero;
                PivotAheadMetres = 0f;
                TrackLookAheadMetres = 0f;
                YawSharpness = 0f;
                YawMaxDegreesPerSecond = 0f;
                SunColor = LookDefaults.SunColor;
                SunIntensity = LookDefaults.SunIntensity;
                SunEulerAngles = LookDefaults.SunEulerAngles;
                SunShadowStrength = LookDefaults.SunShadowStrength;
                AmbientTrilight = true;
                AmbientSky = LookDefaults.AmbientSky;
                AmbientEquator = LookDefaults.AmbientEquator;
                AmbientGround = LookDefaults.AmbientGround;
                CameraBackground = LookDefaults.CameraBackground;
                PaletteOverrideEnabled = false;
                FlatAsphaltTexture = false;
                AsphaltColor = LookDefaults.AsphaltColor;
                GroundColor = LookDefaults.GroundColor;
                WallColor = LookDefaults.WallColor;
                KerbColor = LookDefaults.KerbColor;
                GroundFillEnabled = false;
                GroundFillMargin = LookDefaults.GroundFillMargin;
                GroundFillDepth = LookDefaults.GroundFillDepth;
                PostProfile = null;
                return;
            }

            Enabled = true;
            CameraOffset = LookDefaults.FiniteVector(profile.CameraOffset, LookDefaults.CameraOffset);
            CameraLookHeight = LookDefaults.Finite(profile.CameraLookHeight, LookDefaults.CameraLookHeight);
            CameraFov = LookDefaults.Range(profile.CameraFov, 1f, 179f, LookDefaults.CameraFov);
            CameraFollowSharpness = LookDefaults.NonNegative(profile.CameraFollowSharpness, LookDefaults.CameraFollowSharpness);
            // Look-ahead is the one group whose "off" is a legitimate value rather than a mistake:
            // an author who types 0 means "do not lead the car", so 0 survives instead of the
            // default, and only a negative or non-finite value falls back to 0.
            LookAheadMetresPerSpeed = LookDefaults.NonNegative(profile.LookAheadMetresPerSpeed, 0f);
            LookAheadMaxMetres = LookDefaults.NonNegative(profile.LookAheadMaxMetres, 0f);
            LookAheadSharpness = LookDefaults.NonNegative(profile.LookAheadSharpness, LookDefaults.LookAheadSharpness);
            // Same reasoning as the lead: a widen of 0 is an author saying "hold the FOV", not a
            // broken number, so only a negative or non-finite one falls back to 0.
            SpeedFovWidenDegrees = LookDefaults.NonNegative(profile.SpeedFovWidenDegrees, 0f);
            SpeedFovAtMetresPerSecond = LookDefaults.Range(profile.SpeedFovAtMetresPerSecond, 1f, 80f,
                LookDefaults.SpeedFovAtMetresPerSecond);
            SpeedFovSharpness = LookDefaults.NonNegative(profile.SpeedFovSharpness, LookDefaults.SpeedFovSharpness);

            YawMode = profile.YawMode;
            ChaseOffset = LookDefaults.FiniteVector(profile.ChaseOffset, new Vector3(0f, 9f, -14f));
            PivotAheadMetres = LookDefaults.NonNegative(profile.PivotAheadMetres, 6f);
            TrackLookAheadMetres = LookDefaults.NonNegative(profile.TrackLookAheadMetres, 18f);
            YawSharpness = LookDefaults.NonNegative(profile.YawSharpness, 3f);
            YawMaxDegreesPerSecond = LookDefaults.NonNegative(profile.YawMaxDegreesPerSecond, 120f);

            SunColor = LookDefaults.FiniteColor(profile.SunColor, LookDefaults.SunColor);
            SunIntensity = LookDefaults.Range(profile.SunIntensity, 0f, 20f, LookDefaults.SunIntensity);
            SunEulerAngles = LookDefaults.FiniteVector(profile.SunEulerAngles, LookDefaults.SunEulerAngles);
            SunShadowStrength = LookDefaults.Range(profile.SunShadowStrength, 0f, 1f, LookDefaults.SunShadowStrength);
            AmbientTrilight = profile.AmbientTrilight;
            AmbientSky = LookDefaults.FiniteColor(profile.AmbientSky, LookDefaults.AmbientSky);
            AmbientEquator = LookDefaults.FiniteColor(profile.AmbientEquator, LookDefaults.AmbientEquator);
            AmbientGround = LookDefaults.FiniteColor(profile.AmbientGround, LookDefaults.AmbientGround);
            CameraBackground = LookDefaults.FiniteColor(profile.CameraBackground, LookDefaults.CameraBackground);

            PaletteOverrideEnabled = profile.PaletteOverrideEnabled;
            FlatAsphaltTexture = profile.FlatAsphaltTexture;
            AsphaltColor = LookDefaults.FiniteColor(profile.AsphaltColor, LookDefaults.AsphaltColor);
            GroundColor = LookDefaults.FiniteColor(profile.GroundColor, LookDefaults.GroundColor);
            WallColor = LookDefaults.FiniteColor(profile.WallColor, LookDefaults.WallColor);
            KerbColor = LookDefaults.FiniteColor(profile.KerbColor, LookDefaults.KerbColor);

            GroundFillEnabled = profile.GroundFillEnabled;
            GroundFillMargin = LookDefaults.NonNegative(profile.GroundFillMargin, LookDefaults.GroundFillMargin);
            GroundFillDepth = LookDefaults.NonNegative(profile.GroundFillDepth, LookDefaults.GroundFillDepth);
            PostProfile = profile.PostProfile;
        }
    }
}
