using UnityEngine;
using UnityEngine.Rendering;

namespace FestivalDerby.Look
{
    /// <summary>
    /// The values the shipped look was tuned to. It lives here instead of as magic numbers inside
    /// the sanitizer so a corrupt or missing field can only ever fall back to something a human
    /// actually tuned, never to a value invented at read time.
    /// </summary>
    public static class LookDefaults
    {
        // Camera. The oval's baked data already asked for this offset; the look owns it now so the
        // framing is one decision rather than a map asset and a feature asset disagreeing.
        // CAM1 pulled the chase in from the CS1 (-20, 37.5, -17.5) by 17% along its own line of
        // sight, which keeps the bearing and the pitch and only shortens the distance: at 30 m/s a
        // nearer camera makes the car read bigger against the same tarmac, which is what a chase
        // camera on a phone needs when the car is a few millimetres of glass wide.
        public static readonly Vector3 CameraOffset = new Vector3(-16.5f, 31f, -14.5f);
        public const float CameraLookHeight = 2f;
        public const float CameraFov = 30f;
        // A critically damped spring rate in rad/s, not the exponential blend coefficient it used to
        // be: the framing is solved as a second-order system with the car's own velocity fed forward,
        // so this is the frequency it settles at and the steady-state lag is zero at any speed.
        public const float CameraFollowSharpness = 6f;
        // A nudge ahead of the car, not a hundred metres of scenery. CS1's half second of travel
        // (0.5 m per m/s, capped at 14 m) put the car itself near the edge of the frame at speed; a
        // fraction of a car length is enough to open the corner without moving the subject.
        public const float LookAheadMetresPerSpeed = 0.08f;
        public const float LookAheadMaxMetres = 1.8f;
        public const float LookAheadSharpness = 5f;
        // A phone held at arm's length reads a 30 degree glass as flat once the car covers ground
        // faster than the framing shows it. A couple of degrees at speed restores the horizon; more
        // than that reads as a zoom, which is why the widen is capped here and not at CS1's 3.
        public const float SpeedFovWidenDegrees = 2f;
        public const float SpeedFovAtMetresPerSecond = 26f;
        public const float SpeedFovSharpness = 1.6f;

        // A warm key with a cool sky is what reads as "sunny midday" rather than as the template's
        // two neutral directional lights fighting for the same job.
        public static readonly Color SunColor = new Color(1f, 0.87f, 0.68f, 1f);
        public const float SunIntensity = 1.15f;
        public static readonly Vector3 SunEulerAngles = new Vector3(52f, 36f, 0f);
        public const float SunShadowStrength = 0.45f;
        public static readonly Color AmbientSky = new Color(0.50f, 0.65f, 0.82f, 1f);
        public static readonly Color AmbientEquator = new Color(0.44f, 0.54f, 0.62f, 1f);
        public static readonly Color AmbientGround = new Color(0.30f, 0.38f, 0.24f, 1f);
        // Solid colour instead of a skybox: the project has no skybox material, so the inherited
        // skybox clear renders the grey editor backdrop and reads as "the world ends here".
        public static readonly Color CameraBackground = new Color(0.62f, 0.76f, 0.90f, 1f);

        // Light grey asphalt, saturated grass, near-white walls: the Circuit Superstars read is
        // high-key, not the template's near-black road on a dark grey floor.
        public static readonly Color AsphaltColor = new Color(0.42f, 0.43f, 0.45f, 1f);
        public static readonly Color GroundColor = new Color(0.38f, 0.56f, 0.27f, 1f);
        public static readonly Color WallColor = new Color(0.88f, 0.90f, 0.92f, 1f);
        public static readonly Color KerbColor = new Color(0.92f, 0.28f, 0.24f, 1f);
        // A margin wide enough that the horizon never shows the plane's edge at any camera pitch.
        public const float GroundFillMargin = 40f;
        // Slightly under the racing surface so the two never z-fight, small enough that the fill
        // still reads as the same ground plane.
        public const float GroundFillDepth = 0.06f;

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static float Finite(float value, float fallback)
        {
            return IsFinite(value) ? value : fallback;
        }

        public static float Range(float value, float min, float max, float fallback)
        {
            return IsFinite(value) && value >= min && value <= max ? value : fallback;
        }

        public static float NonNegative(float value, float fallback)
        {
            return IsFinite(value) && value >= 0f ? value : fallback;
        }

        public static Vector3 FiniteVector(Vector3 value, Vector3 fallback)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) ? value : fallback;
        }

        public static Color FiniteColor(Color value, Color fallback)
        {
            return IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a)
                ? value
                : fallback;
        }
    }

    /// <summary>
    /// The whole visual identity of a run: chase framing, sun and ambient, the palette painted onto
    /// the built geometry, and the ground fill that hides the void behind the circuit.
    /// </summary>
    /// <remarks>
    /// It exists as one asset rather than as fields scattered across the feature configs because
    /// these values are one aesthetic decision: sun and asphalt colour that were tuned together
    /// must be able to ship, roll back and be A/B'd together. The kill switch is a serialized bool
    /// on the asset (<see cref="LookProfileEnabled"/>) and its C# default is false, so a config
    /// authored before this field existed keeps the inherited look byte for byte.
    /// </remarks>
    /// <summary>How the chase camera's bearing moves. Serialized by value: append only.</summary>
    public enum CameraYawMode
    {
        Fixed = 0,
        FollowTrack = 1,
        FollowCar = 2,
    }

    [CreateAssetMenu(fileName = "LookProfile", menuName = "Config/LookProfile")]
    public sealed class LookProfile : ScriptableObject
    {
        /// <summary>
        /// The single switch. Off means the applier is never constructed and every inherited value
        /// (camera, lights, materials, background) is left exactly as the scene and map authored it.
        /// </summary>
        [Tooltip("Off keeps the inherited look byte for byte. On applies every group below.")]
        public bool LookProfileEnabled;

        [Header("Camera")]
        [Tooltip("World-space offset from the car to the chase position. The camera keeps a fixed bearing.")]
        public Vector3 CameraOffset = LookDefaults.CameraOffset;

        [Tooltip("World Y the camera aims at, measured from the car's pivot.")]
        public float CameraLookHeight = LookDefaults.CameraLookHeight;

        [Tooltip("Vertical field of view. 30 reads as a wide, flat sprint camera on a phone.")]
        public float CameraFov = LookDefaults.CameraFov;

        [Tooltip("Higher reaches the framed position faster. A critically damped spring rate in rad/s; " +
            "6 closes a gap in about a second, with no spring-back past the car.")]
        public float CameraFollowSharpness = LookDefaults.CameraFollowSharpness;

        [Tooltip("Metres of lead per m/s of planar speed. 0 disables look-ahead.")]
        public float LookAheadMetresPerSpeed = LookDefaults.LookAheadMetresPerSpeed;

        [Tooltip("Hard cap on the lead, in metres. Half a car length keeps the subject framed.")]
        public float LookAheadMaxMetres = LookDefaults.LookAheadMaxMetres;

        [Tooltip("How fast the lead itself settles. Too high and a steering jitter shakes the framing.")]
        public float LookAheadSharpness = LookDefaults.LookAheadSharpness;

        [Header("Speed FOV")]
        [Tooltip("Extra degrees of field of view at full speed. 0 keeps a constant FOV. Keep it under about 2 so the widen reads as depth and not as a zoom.")]
        public float SpeedFovWidenDegrees = LookDefaults.SpeedFovWidenDegrees;

        [Tooltip("Planar speed, m/s, at which the whole widen is reached.")]
        public float SpeedFovAtMetresPerSecond = LookDefaults.SpeedFovAtMetresPerSecond;

        [Tooltip("How fast the widen settles. Low values are what stop the horizon pumping.")]
        public float SpeedFovSharpness = LookDefaults.SpeedFovSharpness;

        [Header("Camera yaw (cone)")]
        [Tooltip("Fixed: the offset keeps one world bearing. FollowTrack: swings with the road ahead, keeping the start's framing. FollowCar: a chase camera behind the car that lags into the outside of corners.")]
        public CameraYawMode YawMode = CameraYawMode.Fixed;

        [Tooltip("FollowCar only: car-local offset, -Z is behind the car.")]
        public Vector3 ChaseOffset = new Vector3(0f, 9f, -14f);

        [Tooltip("FollowCar only: metres ahead of the car the camera aims at, so the road ahead stays in frame.")]
        public float PivotAheadMetres = 6f;

        [Tooltip("FollowTrack only: how far ahead along the track the road's direction is read.")]
        public float TrackLookAheadMetres = 18f;

        [Tooltip("How fast the swing settles, 1/s. Lower lags more, which swings the camera wider on the outside of a corner.")]
        public float YawSharpness = 3f;

        [Tooltip("Cap on the swing speed, degrees per second, so a hairpin pans instead of whipping.")]
        public float YawMaxDegreesPerSecond = 120f;

        [Header("Light")]
        public Color SunColor = LookDefaults.SunColor;

        [Tooltip("Directional intensity of the key light.")]
        public float SunIntensity = LookDefaults.SunIntensity;

        [Tooltip("Sun rotation. A low pitch throws the long soft shadows that sell the scale.")]
        public Vector3 SunEulerAngles = LookDefaults.SunEulerAngles;

        [Tooltip("Light shadow strength. Lower softens the contact shadow without touching quality settings.")]
        public float SunShadowStrength = LookDefaults.SunShadowStrength;

        [Tooltip("On writes the sky/equator/ground trilight; off writes a single flat ambient colour.")]
        public bool AmbientTrilight = true;

        public Color AmbientSky = LookDefaults.AmbientSky;
        public Color AmbientEquator = LookDefaults.AmbientEquator;
        public Color AmbientGround = LookDefaults.AmbientGround;

        [Tooltip("Render camera background. A solid sky colour is what removes the empty void behind the circuit.")]
        public Color CameraBackground = LookDefaults.CameraBackground;

        [Header("Palette")]
        [Tooltip("Paint the built road, ground, walls and kerbs at run start. Never writes an asset on disk.")]
        public bool PaletteOverrideEnabled = true;

        [Tooltip("Replace the asphalt's base map with white, which neutralizes a baked blotch texture.")]
        public bool FlatAsphaltTexture = true;

        public Color AsphaltColor = LookDefaults.AsphaltColor;
        public Color GroundColor = LookDefaults.GroundColor;
        public Color WallColor = LookDefaults.WallColor;
        public Color KerbColor = LookDefaults.KerbColor;

        [Header("Ground fill")]
        [Tooltip("Draw a runtime plane under the circuit so no capture shows the world edge.")]
        public bool GroundFillEnabled = true;

        [Tooltip("Extra metres of fill beyond the largest circuit dimension.")]
        public float GroundFillMargin = LookDefaults.GroundFillMargin;

        [Tooltip("How far below the racing surface the fill sits, so the two never z-fight.")]
        public float GroundFillDepth = LookDefaults.GroundFillDepth;

        [Header("Post")]
        [Tooltip("Optional URP volume profile (colour grading, vignette) applied as a global volume for the run. " +
            "Empty leaves the camera's post-processing exactly as the scene authored it. Each effect is one " +
            "full-screen pass on a phone, so keep it to the uber-pass effects.")]
        public VolumeProfile PostProfile;

        /// <summary>
        /// Reads the asset into the value the camera wiring and the applier consume, sanitizing every
        /// field on the way out. A null or disabled profile resolves to a disabled setting rather
        /// than to a throw: a run that forgot to wire the asset must still drive.
        /// </summary>
        public static LookSettings Resolve(LookProfile profile)
        {
            return new LookSettings(profile);
        }

        private void OnValidate()
        {
            // A serialized asset is authored by hand, so a copy-pasted exponential or a negative
            // margin has to be repaired at the source instead of silently clamped at every read.
            Sanitize();
        }

        /// <summary>
        /// Replaces every non-finite or out-of-range field with the tuned default. Public so a test
        /// can exercise the repair path without going through Unity's asset pipeline.
        /// </summary>
        public void Sanitize()
        {
            CameraOffset = LookDefaults.FiniteVector(CameraOffset, LookDefaults.CameraOffset);
            CameraLookHeight = LookDefaults.Finite(CameraLookHeight, LookDefaults.CameraLookHeight);
            CameraFov = LookDefaults.Range(CameraFov, 1f, 179f, LookDefaults.CameraFov);
            CameraFollowSharpness = LookDefaults.NonNegative(CameraFollowSharpness, LookDefaults.CameraFollowSharpness);
            LookAheadMetresPerSpeed = LookDefaults.NonNegative(LookAheadMetresPerSpeed, LookDefaults.LookAheadMetresPerSpeed);
            LookAheadMaxMetres = LookDefaults.NonNegative(LookAheadMaxMetres, LookDefaults.LookAheadMaxMetres);
            LookAheadSharpness = LookDefaults.NonNegative(LookAheadSharpness, LookDefaults.LookAheadSharpness);
            SpeedFovWidenDegrees = LookDefaults.Range(SpeedFovWidenDegrees, 0f, 20f, LookDefaults.SpeedFovWidenDegrees);
            SpeedFovAtMetresPerSecond = LookDefaults.Range(SpeedFovAtMetresPerSecond, 1f, 80f,
                LookDefaults.SpeedFovAtMetresPerSecond);
            SpeedFovSharpness = LookDefaults.NonNegative(SpeedFovSharpness, LookDefaults.SpeedFovSharpness);

            SunColor = LookDefaults.FiniteColor(SunColor, LookDefaults.SunColor);
            SunIntensity = LookDefaults.Range(SunIntensity, 0f, 20f, LookDefaults.SunIntensity);
            SunEulerAngles = LookDefaults.FiniteVector(SunEulerAngles, LookDefaults.SunEulerAngles);
            SunShadowStrength = LookDefaults.Range(SunShadowStrength, 0f, 1f, LookDefaults.SunShadowStrength);
            AmbientSky = LookDefaults.FiniteColor(AmbientSky, LookDefaults.AmbientSky);
            AmbientEquator = LookDefaults.FiniteColor(AmbientEquator, LookDefaults.AmbientEquator);
            AmbientGround = LookDefaults.FiniteColor(AmbientGround, LookDefaults.AmbientGround);
            CameraBackground = LookDefaults.FiniteColor(CameraBackground, LookDefaults.CameraBackground);

            AsphaltColor = LookDefaults.FiniteColor(AsphaltColor, LookDefaults.AsphaltColor);
            GroundColor = LookDefaults.FiniteColor(GroundColor, LookDefaults.GroundColor);
            WallColor = LookDefaults.FiniteColor(WallColor, LookDefaults.WallColor);
            KerbColor = LookDefaults.FiniteColor(KerbColor, LookDefaults.KerbColor);

            GroundFillMargin = LookDefaults.NonNegative(GroundFillMargin, LookDefaults.GroundFillMargin);
            GroundFillDepth = LookDefaults.NonNegative(GroundFillDepth, LookDefaults.GroundFillDepth);
        }
    }
}
