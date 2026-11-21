using FestivalDerby.Driving.Handling;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Which handling model a car runs. Declared so the legacy model is 0: an asset authored before this
    /// field existed, or one whose value is corrupt, reads as Legacy and the shipped balance is untouched.
    /// V2 is the model the demo config selects, and it is a one-field rollback.
    /// </summary>
    public enum HandlingModel
    {
        /// <summary>The raycast-and-grip model that shipped with the template.</summary>
        Legacy = 0,

        /// <summary>Driving Model V2: the engine-free handling core behind <see cref="ArcadeCarMotor"/>.</summary>
        V2 = 1
    }

    /// <summary>
    /// Everything a motor needs that is not the body or the car definition: which model, which preset, how
    /// the stick is read, which camera owns the bearing, and the rest ring. A value type so it is copied
    /// into the driver at bind time and a mutated config can never change a car mid-run.
    /// </summary>
    public readonly struct MotorSetup
    {
        public readonly HandlingModel Model;
        public readonly HandlingPreset Preset;
        public readonly HandlingParams Handling;
        public readonly JoystickDriveMode DriveMode;
        public readonly Transform Camera;
        public readonly float StickDeadZone;

        public MotorSetup(
            HandlingModel model,
            HandlingPreset preset,
            in HandlingParams handling,
            JoystickDriveMode driveMode,
            Transform camera,
            float stickDeadZone)
        {
            Model = model;
            Preset = preset;
            Handling = handling;
            DriveMode = driveMode;
            Camera = camera;
            StickDeadZone = stickDeadZone;
        }

        /// <summary>
        /// The same model, preset and handling read through another stick mode and bearing. Exists so a
        /// caller that cannot name the handling table's assembly (the scene code) can still give a LAN
        /// peer's car the player's exact handling with the stick read as a screen direction.
        /// </summary>
        public MotorSetup WithDriveMode(JoystickDriveMode driveMode, Transform camera)
        {
            return new MotorSetup(Model, Preset, Handling, driveMode, camera, StickDeadZone);
        }

        /// <summary>The legacy model, which needs neither a preset nor a camera.</summary>
        public static MotorSetup Legacy()
        {
            return new MotorSetup(
                HandlingModel.Legacy,
                HandlingPreset.Balanced,
                HandlingParams.Balanced,
                JoystickDriveMode.Direction,
                null,
                0f);
        }

        /// <summary>
        /// A V2 setup at one named preset, without going through an asset. Exists so a test or an editor
        /// tool can drive the model directly; the run always resolves the setup from the config instead.
        /// </summary>
        public static MotorSetup ForPreset(
            HandlingPreset preset, JoystickDriveMode driveMode, Transform camera, float stickDeadZone)
        {
            return new MotorSetup(
                HandlingModel.V2,
                preset,
                HandlingProfile.ResolvePreset(preset),
                driveMode,
                camera,
                stickDeadZone);
        }

        /// <summary>
        /// A V2 setup at one named preset with a HandlingProfile's steering overrides applied on top.
        /// Exists for the A/B preset switch: the preset is chosen at runtime, but the profile's V2e.2
        /// overrides still belong to the car. A null profile is exactly the bare-preset overload.
        /// </summary>
        public static MotorSetup ForPreset(
            HandlingPreset preset, JoystickDriveMode driveMode, Transform camera, float stickDeadZone,
            HandlingProfile profile)
        {
            return new MotorSetup(
                HandlingModel.V2,
                preset,
                HandlingProfile.ResolveForPreset(preset, profile),
                driveMode,
                camera,
                stickDeadZone);
        }

        /// <summary>
        /// A V2 setup resolved through a HandlingProfile asset: the profile's preset with its steering
        /// overrides applied. A null profile degrades to the bare Balanced preset, the same value
        /// <see cref="MotorSetupFactory.Resolve"/> yields without an asset.
        /// </summary>
        public static MotorSetup ForProfile(
            HandlingProfile profile, JoystickDriveMode driveMode, Transform camera, float stickDeadZone)
        {
            return new MotorSetup(
                HandlingModel.V2,
                profile == null ? HandlingPreset.Balanced : profile.Preset,
                HandlingProfile.Resolve(profile),
                driveMode,
                camera,
                stickDeadZone);
        }
    }

    /// <summary>
    /// The one place that turns a <see cref="DrivingFeatureConfig"/> into a <see cref="MotorSetup"/>.
    ///
    /// It is a single static entry point on purpose: the player, every bot and every future spawner must
    /// agree on which model is running, and a second copy of this decision is exactly how one car ends up
    /// on the old model while the match is scored on the new one.
    /// </summary>
    public static class MotorSetupFactory
    {
        public static MotorSetup Resolve(DrivingFeatureConfig config, JoystickDriveMode driveMode, Transform camera)
        {
            if (config == null || config.HandlingModel != HandlingModel.V2)
                return MotorSetup.Legacy();

            HandlingPreset preset = ProfilePreset(config.Handling);
            return new MotorSetup(
                HandlingModel.V2,
                preset,
                HandlingProfile.ResolvePreset(preset),
                driveMode,
                camera,
                DrivingFeatureConfig.ResolveStickDeadZone(config));
        }

        /// <summary>
        /// The car a run actually drives: <see cref="Resolve"/> with the config's HandlingProfile steering
        /// overrides applied on top when the model is V2 and an asset is present. Every other case is the
        /// <see cref="Resolve"/> value unchanged, so a null profile, the legacy model or a missing config
        /// behave exactly as before the overrides existed.
        /// </summary>
        public static MotorSetup ResolveWithProfile(DrivingFeatureConfig config, JoystickDriveMode driveMode,
            Transform camera)
        {
            MotorSetup setup = Resolve(config, driveMode, camera);
            if (setup.Model != HandlingModel.V2 || config.Handling == null)
                return setup;

            return MotorSetup.ForProfile(config.Handling, setup.DriveMode, setup.Camera, setup.StickDeadZone);
        }

        /// <summary>
        /// A bot's car. It is the player's car — same preset, same profile overrides, same FEEL2 catch-up —
        /// read through a car-relative axis because a bot has no stick and no screen. INT3: bots used to
        /// get the bare preset (steering budget 16 m/s^2, heading gain 4 against the player's 30 and 5),
        /// so a bot could never hold the player's corner speed whatever its skill. The car is now equal
        /// and <c>BotProfile.paceSkill</c> is the only thing that makes a bot slower.
        /// </summary>
        public static MotorSetup ResolveBot(DrivingFeatureConfig config)
        {
            return ResolveWithProfile(config, JoystickDriveMode.CarRelative, null);
        }

        /// <summary>
        /// The model a configuration selects, with the same fail-safe as the setup itself: a null config is
        /// the legacy model, which is the one that shipped.
        /// </summary>
        public static HandlingModel ResolveModel(DrivingFeatureConfig config)
        {
            return config != null && config.HandlingModel == HandlingModel.V2 ? HandlingModel.V2 : HandlingModel.Legacy;
        }

        private static HandlingPreset ProfilePreset(HandlingProfile profile)
        {
            return profile == null ? HandlingPreset.Balanced : profile.Preset;
        }
    }
}
