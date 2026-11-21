using FestivalDerby.Driving;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// How one joystick sample becomes a throttle and a steering axis. Direction ships: the owner
    /// asked for the stick to read as a heading, with the car accelerating toward it and backing up
    /// when it is behind. CarRelative is the plain pedal-and-wheel reading, kept as the alternative.
    /// </summary>
    public enum JoystickDriveMode
    {
        Direction,
        CarRelative
    }

    /// <summary>
    /// Which cosmetic car family a run defaults to. Toy is the CC0 low-poly Kenney set this project
    /// ships; Realistic is a slot for a second set of your own models (none ship with the template).
    /// </summary>
    public enum CarVisualSet
    {
        Realistic = 0,
        Toy = 1
    }

    [CreateAssetMenu(fileName = "DrivingFeatureConfig", menuName = "Config/DrivingFeatureConfig")]
    public sealed class DrivingFeatureConfig : ScriptableObject
    {
        private static readonly DrivingCameraSettings DefaultCameraSettings =
            new DrivingCameraSettings(new Vector3(-12f, 11f, -8f), 2f, 10f);
        // Match LocalChaseCamera's minimum usable look direction without changing its API.
        private const float ZeroLookVectorSqrMagnitude = 1e-8f;
        // The one model the owner has confirmed clean; also the id every fallback resolves to.
        private const string DefaultVisualId = "GreenVan";
        // A small rest ring, big enough to swallow a thumb that is not asking for anything.
        private const float DefaultStickDeadZone = 0.15f;
        // At 1 the dead zone would swallow the whole stick and the car would never move.
        private const float MaxStickDeadZone = 0.9f;

        public CarDefinition Car;

        // Zero would be Direction, which is the shipped behaviour, so a config asset authored
        // before this field existed reads as the default instead of as an unhandled mode.
        public JoystickDriveMode DriveMode = JoystickDriveMode.Direction;

        /// <summary>
        /// Which handling model every car in the run gets. Legacy is declared first, so an asset authored
        /// before this field existed reads as the model that shipped and the V2 switch is opt-in data
        /// rather than a new default that a stale asset would silently acquire.
        /// </summary>
        [Tooltip("Legacy keeps the template motor; V2 is Driving Model V2. Legacy is the safe value for a " +
            "config authored before this field existed.")]
        public HandlingModel HandlingModel = HandlingModel.Legacy;

        /// <summary>
        /// CTRL1: how the pedal is produced. GasButton is the product default (the scheme the mobile
        /// references table describes) and is listed here as a plain value, not as an enum's first
        /// member, so the ordering of <see cref="ControlScheme"/> can never move the default by accident.
        /// Resources/DrivingFeatureConfig.asset deliberately keeps the unset value: a config authored
        /// before this field existed must read as the product default rather than as whatever the
        /// serializer left behind, which is the same rule every other feature switch in this asset uses.
        /// </summary>
        [Tooltip("RingThrottle = stick magnitude is the pedal (shipped V2); GasButton = a GAS input is " +
            "the pedal and the stick only steers (default); AutoAccelerate = full pedal while the stick is out.")]
        public ControlScheme Scheme = ControlSchemeRules.Default;

        /// <summary>
        /// The V2 preset. Null selects Balanced, which is the preset the acceptance numbers were measured
        /// on, so a config that forgets to author a profile still drives the intended car.
        /// </summary>
        [Tooltip("A HandlingProfile asset. Empty means Balanced.")]
        public HandlingProfile Handling;

        public float StickDeadZone = DefaultStickDeadZone;
        // Optional baked map. Typed as a base ScriptableObject on purpose: the World assembly owns
        // MapAsset and this assembly must not reference it, so the bootstrap downcasts and a wrong
        // reference degrades to "no map" instead of forging an assembly dependency. When set, the
        // run builds its ground and walls from baked data and ignores Track entirely.
        [Tooltip("A FestivalDerby.World MapAsset. When set, the run builds from baked map data " +
            "instead of the inherited floor and track layout.")]
        public ScriptableObject Map;

        // V2e.3 feel switch. On in the C# initializer: a config asset authored before this field
        // existed never serialized a value, so it keeps the constructor's true and the wheels ship on
        // without touching Resources/DrivingFeatureConfig.asset. Authoring false is the rollback.
        [Tooltip("Spin and steer the cosmetic model's wheel nodes. Off keeps the visual exactly as attached.")]
        public bool WheelSpinEnabled = true;

        // V2e.4 feel switch. Same stale-asset rule as WheelSpinEnabled: the constructor's true is what
        // an asset without the serialized field keeps, and authoring false is the rollback.
        [Tooltip("Roll, pitch and squash the cosmetic model's VisualPivot. Off keeps the body rigid.")]
        public bool BodyJuiceEnabled = true;

        // V2e.4 feel values. The C# initializers are the shipped tuning; every Resolve* helper below
        // treats unset/0/out-of-range as "use the shipped default", so a stale asset keeps working
        // untouched.
        // Owner feedback 2026-10-16: the rear of the car leaned so far in a corner that the model
        // read as detached from its wheels, so the roll and the pitch are both cut to a fifth of the
        // V2e.4 numbers. The squash is untouched: it is a landing accent, not a cornering lean.
        // Lateral accel of ~10 m/s^2 (10 m/s in a 1 rad/s turn) now reads as a hint of lean.
        private const float DefaultBodyRollGain = 0.12f;
        private const float DefaultBodyRollMaxDeg = 1.6f;
        // Full-power longitudinal accel is ~7 m/s^2; that should squat the nose a hair.
        private const float DefaultBodyPitchGain = 0.1f;
        private const float DefaultBodyPitchMaxDeg = 1.2f;
        // A 12% height squeeze, felt for a quarter second, reads as a landing without looking rubbery.
        private const float DefaultBodySquashScale = 0.12f;
        private const float DefaultBodySquashSeconds = 0.25f;
        private const float DefaultBodyFilterTau = 0.12f;

        [Tooltip("Degrees of body roll per m/s^2 of lateral acceleration.")]
        public float BodyRollGain = DefaultBodyRollGain;
        [Tooltip("Hard clamp on body roll, in degrees.")]
        public float BodyRollMaxDeg = DefaultBodyRollMaxDeg;
        [Tooltip("Degrees of body pitch per m/s^2 of longitudinal acceleration.")]
        public float BodyPitchGain = DefaultBodyPitchGain;
        [Tooltip("Hard clamp on body pitch, in degrees.")]
        public float BodyPitchMaxDeg = DefaultBodyPitchMaxDeg;
        [Tooltip("Fraction of the visual's height squeezed out on landing (0.12 = 12%).")]
        public float BodySquashScale = DefaultBodySquashScale;
        [Tooltip("Seconds the landing squash takes to swell and recover.")]
        public float BodySquashSeconds = DefaultBodySquashSeconds;
        [Tooltip("Exponential filter time constant, in seconds, for the accelerations the juice reads.")]
        public float BodyFilterTau = DefaultBodyFilterTau;

        // Terrain grip from the baked surface under each car. On in the demo: the surface shader and
        // the grip read land together, so a map with a dirt span actually drives differently on it.
        // Off restores the pre-surface handling byte for byte, which is what a rollback wants.
        [Tooltip("Read terrain grip from the baked map surface. Off keeps every car on asphalt grip.")]
        public bool SurfaceGripEnabled = true;

        // PF.2 stuck respawn. The owner's escape for a pinned car: after StuckSeconds of pedal-down
        // immobility the player respawns on the nearest route point and ghosts for GhostSeconds
        // (colliders off, semi-transparent), then is made solid again. On in the C# initializer by
        // the same stale-asset rule as the feel switches: an asset that never serialized the field
        // keeps the constructor's true, and authoring false is the rollback.
        [Tooltip("Respawn the player back on the route after a stuck window, then ghost briefly. " +
            "Off removes the escape entirely.")]
        public bool StuckRespawnEnabled = true;

        // PF.2 feel values, resolved with the same unset/0/out-of-range rule as the body juice:
        // a stale asset keeps working untouched. StuckSeconds is the owner's number; the speed
        // limit is half a metre per second: slower than any deliberate crawl.
        private const float DefaultStuckSeconds = 5f;
        private const float DefaultStuckSpeedLimit = 0.5f;
        private const float DefaultGhostSeconds = 3f;
        // Faint enough to read as a ghost, strong enough to still steer by.
        private const float DefaultGhostAlpha = 0.45f;

        [Tooltip("Seconds of throttle-demanded immobility before the player respawns.")]
        public float StuckSeconds = DefaultStuckSeconds;
        [Tooltip("Ground speed in m/s at or above which the car counts as moving.")]
        public float StuckSpeedLimit = DefaultStuckSpeedLimit;
        [Tooltip("Seconds the respawned car stays collider-free and semi-transparent.")]
        public float GhostSeconds = DefaultGhostSeconds;
        [Tooltip("Render alpha of the ghosted car (0 invisible, 1 opaque).")]
        public float GhostAlpha = DefaultGhostAlpha;

        // WALL1. Owner feedback: "when you crash into the wall it slows you down a LOT. That should be much,
        // much less." Born OFF like every other new feature (project rule); Resources/DrivingFeatureConfig.asset
        // turns it on, so a missing or stale asset leaves the plain contact response instead of silently
        // changing how a barrier feels.
        [Tooltip("Glance off barriers: keep the wall-tangential speed and align to the barrier. Off restores " +
            "the stock contact response.")]
        public bool WallGlanceEnabled = false;

        public Vector3 CameraOffset = DefaultCameraSettings.Offset;
        public float LookHeight = DefaultCameraSettings.LookHeight;
        public float FollowSharpness = DefaultCameraSettings.FollowSharpness;

        // Cosmetic-only; a null catalog means every car keeps the factory's gray box.
        public CarVisualCatalog Visuals;

        // The cosmetic set the run defaults to. Realistic is declared first, so an asset authored
        // before this field existed reads as the family that shipped and Toy is opt-in data rather
        // than a new default a stale asset would silently acquire. The demo asset authors Toy.
        [Tooltip("Which default car family the player resolves to.")]
        public CarVisualSet VisualSet = CarVisualSet.Realistic;

        public string PlayerVisualId = DefaultVisualId;

        // The Toy defaults. Kept beside the realistic ones instead of replacing them so a run can
        // switch family from data alone and both sets are always resolvable from one asset.
        [Tooltip("Player visual id used when VisualSet is Toy.")]
        public string PlayerVisualIdToy = DefaultVisualId;

        // Optional look profile, the same seam as Map: the Look assembly owns LookProfile and
        // this one must not reference it, so the field is a base asset that the bootstrap downcasts.
        // Null or disabled keeps the inherited camera, lights and materials byte for byte.
        [Tooltip("A FestivalDerby.Look LookProfile. Leave empty to keep the inherited look.")]
        public ScriptableObject Look;

        public static CarDefinition ResolveCar(DrivingFeatureConfig config, CarDefinition fallback)
        {
            return config != null && config.Car != null ? config.Car : fallback;
        }

        /// <summary>
        /// The one explicit seam that lets baked map geometry become the same enclosure type the
        /// graybox factory builds. OwnedGrayboxCircuit's constructor is internal and the World
        /// baker lives in another assembly, so this factory is the only way a baked map can be
        /// handed to the enclosure's own ownership without duplicating that ownership.
        ///
        /// It performs no validation and takes ownership of the root, material and mesh, exactly
        /// like GrayboxCircuitFactory.Create: the caller must hand it fully built parts and must
        /// not touch them afterwards.
        /// </summary>
        public static OwnedGrayboxCircuit CreateOwnedCircuit(GameObject root, Material material,
            in GrayboxCircuitLayout layout, BoxCollider[] walls, Transform startGate, Pose[] spawnPoses,
            Pose[] route, Mesh roadMesh)
        {
            return new OwnedGrayboxCircuit(root, material, layout, walls, startGate, spawnPoses, route,
                roadMesh);
        }

        /// <summary>The cosmetic family the run defaults to; a null config keeps Realistic.</summary>
        public static CarVisualSet ResolveVisualSet(DrivingFeatureConfig config)
        {
            return config != null ? config.VisualSet : CarVisualSet.Realistic;
        }

        /// <summary>The player's cosmetic model id for the selected set, or the shared default when unset.</summary>
        public static string ResolvePlayerVisualId(DrivingFeatureConfig config)
        {
            if (config == null)
                return DefaultVisualId;

            string id = config.VisualSet == CarVisualSet.Toy ? config.PlayerVisualIdToy : config.PlayerVisualId;
            return !string.IsNullOrEmpty(id) ? id : DefaultVisualId;
        }

        /// <summary>The drive reading, defaulting to the shipped Direction for a null or corrupt config.</summary>
        public static JoystickDriveMode ResolveDriveMode(DrivingFeatureConfig config)
        {
            return config != null && config.DriveMode == JoystickDriveMode.CarRelative
                ? JoystickDriveMode.CarRelative
                : JoystickDriveMode.Direction;
        }

        /// <summary>
        /// The CTRL1 control scheme. A null config reads as the product default, and an unknown
        /// serialized value degrades to it through ControlSchemeRules.Sanitize instead of reaching the
        /// handling code as an undefined enum.
        /// </summary>
        public static ControlScheme ResolveControlScheme(DrivingFeatureConfig config)
        {
            return config == null ? ControlSchemeRules.Default : ControlSchemeRules.Sanitize(config.Scheme);
        }

        /// <summary>The cosmetic-wheel switch. A null config keeps the shipped-on default.</summary>
        public static bool ResolveWheelSpinEnabled(DrivingFeatureConfig config)
        {
            return config == null || config.WheelSpinEnabled;
        }

        /// <summary>The body-juice switch. A null config keeps the shipped-on default.</summary>
        public static bool ResolveBodyJuiceEnabled(DrivingFeatureConfig config)
        {
            return config == null || config.BodyJuiceEnabled;
        }

        /// <summary>The stuck-respawn switch. A null config keeps the shipped-on default.</summary>
        public static bool ResolveStuckRespawnEnabled(DrivingFeatureConfig config)
        {
            return config == null || config.StuckRespawnEnabled;
        }

        /// <summary>
        /// The wall-glance switch (WALL1). Born off: a null config, or an asset written before the field
        /// existed, keeps the stock barrier response; only an asset that authors it true (the demo config)
        /// turns the glance on.
        /// </summary>
        public static bool ResolveWallGlanceEnabled(DrivingFeatureConfig config)
        {
            return config != null && config.WallGlanceEnabled;
        }

        // Feel ranges: outside any of them the value is treated as unset, because a negative clamp or
        // a zero filter tau would silently invert or freeze the juice rather than break loudly.
        private const float MinFeelGain = 1e-4f;
        private const float MaxFeelGain = 10f;
        private const float MinFeelMaxDegrees = 1e-4f;
        private const float MaxFeelMaxDegrees = 45f;
        private const float MinSquashSeconds = 0.01f;
        private const float MaxSquashSeconds = 2f;
        private const float MinFilterTau = 0.005f;
        private const float MaxFilterTau = 2f;

        public static float ResolveBodyRollGain(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodyRollGain,
                MinFeelGain, MaxFeelGain, DefaultBodyRollGain);
        }

        public static float ResolveBodyRollMaxDeg(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodyRollMaxDeg,
                MinFeelMaxDegrees, MaxFeelMaxDegrees, DefaultBodyRollMaxDeg);
        }

        public static float ResolveBodyPitchGain(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodyPitchGain,
                MinFeelGain, MaxFeelGain, DefaultBodyPitchGain);
        }

        public static float ResolveBodyPitchMaxDeg(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodyPitchMaxDeg,
                MinFeelMaxDegrees, MaxFeelMaxDegrees, DefaultBodyPitchMaxDeg);
        }

        public static float ResolveBodySquashScale(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodySquashScale,
                MinFeelGain, 0.5f, DefaultBodySquashScale);
        }

        public static float ResolveBodySquashSeconds(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodySquashSeconds,
                MinSquashSeconds, MaxSquashSeconds, DefaultBodySquashSeconds);
        }

        public static float ResolveBodyFilterTau(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.BodyFilterTau,
                MinFilterTau, MaxFilterTau, DefaultBodyFilterTau);
        }

        private static float ResolveFeel(float value, float min, float max, float fallback)
        {
            return IsFinite(value) && value >= min && value <= max ? value : fallback;
        }

        // Stuck-respawn ranges. A zero or negative stuck window would respawn instantly, a huge one
        // would never fire, and an alpha outside (0,1) would either hide the car completely or
        // leave it fully opaque, which reads as the feature being broken rather than tuned off.
        private const float MinStuckSeconds = 0.5f;
        private const float MaxStuckSeconds = 30f;
        private const float MinStuckSpeedLimit = 0.01f;
        private const float MaxStuckSpeedLimit = 10f;
        private const float MinGhostSeconds = 0.1f;
        private const float MaxGhostSeconds = 30f;
        private const float MinGhostAlpha = 0.05f;
        private const float MaxGhostAlpha = 0.95f;

        public static float ResolveStuckSeconds(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.StuckSeconds,
                MinStuckSeconds, MaxStuckSeconds, DefaultStuckSeconds);
        }

        public static float ResolveStuckSpeedLimit(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.StuckSpeedLimit,
                MinStuckSpeedLimit, MaxStuckSpeedLimit, DefaultStuckSpeedLimit);
        }

        public static float ResolveGhostSeconds(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.GhostSeconds,
                MinGhostSeconds, MaxGhostSeconds, DefaultGhostSeconds);
        }

        public static float ResolveGhostAlpha(DrivingFeatureConfig config)
        {
            return ResolveFeel(config == null ? float.NaN : config.GhostAlpha,
                MinGhostAlpha, MaxGhostAlpha, DefaultGhostAlpha);
        }

        /// <summary>The normalized joystick rest ring, or the authored default when unset.</summary>
        public static float ResolveStickDeadZone(DrivingFeatureConfig config)
        {
            if (config == null || !IsFinite(config.StickDeadZone) ||
                config.StickDeadZone < 0f || config.StickDeadZone >= MaxStickDeadZone)
                return DefaultStickDeadZone;

            return config.StickDeadZone;
        }

        public static DrivingCameraSettings ResolveCamera(DrivingFeatureConfig config)
        {
            if (config == null || !IsFinite(config.CameraOffset) || !IsFinite(config.LookHeight) ||
                !IsFinite(config.FollowSharpness) || config.FollowSharpness < 0f)
                return DefaultCameraSettings;

            Vector3 lookDirection = Vector3.up * config.LookHeight - config.CameraOffset;
            float lookMagnitudeSquared = lookDirection.sqrMagnitude;
            if (!IsFinite(lookDirection) || !IsFinite(lookMagnitudeSquared) ||
                lookMagnitudeSquared <= ZeroLookVectorSqrMagnitude)
                return DefaultCameraSettings;

            return new DrivingCameraSettings(config.CameraOffset, config.LookHeight, config.FollowSharpness);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct DrivingCameraSettings
    {
        public readonly Vector3 Offset;
        public readonly float LookHeight;
        public readonly float FollowSharpness;

        public DrivingCameraSettings(Vector3 offset, float lookHeight, float followSharpness)
        {
            Offset = offset;
            LookHeight = lookHeight;
            FollowSharpness = followSharpness;
        }
    }
}
