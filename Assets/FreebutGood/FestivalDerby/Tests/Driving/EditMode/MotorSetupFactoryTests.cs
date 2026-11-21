using System.Collections.Generic;
using FestivalDerby.Driving.Handling;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The model switch: one factory decides which handling model every car in the run gets, so this is where
    /// the rollback has to be pinned. The two failure modes that matter are a corrupt or missing asset quietly
    /// selecting the new model on a shipped build (the enum is declared so the legacy model is the zero value)
    /// and a config that names a preset no car actually receives.
    /// </summary>
    public sealed class MotorSetupFactoryTests
    {
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _owned.Count; i++)
                Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void Resolve_WithoutAConfig_IsLegacy()
        {
            MotorSetup setup = MotorSetupFactory.Resolve(null, JoystickDriveMode.Direction, null);
            Assert.That(setup.Model, Is.EqualTo(HandlingModel.Legacy));
            Assert.That(setup.DriveMode, Is.EqualTo(JoystickDriveMode.Direction));
        }

        [Test]
        public void Resolve_OnALegacyConfig_IsLegacy()
        {
            DrivingFeatureConfig config = Config();
            config.HandlingModel = HandlingModel.Legacy;
            config.Handling = Profile(HandlingPreset.Drifty);

            MotorSetup setup = MotorSetupFactory.Resolve(config, JoystickDriveMode.Direction, null);

            Assert.That(setup.Model, Is.EqualTo(HandlingModel.Legacy),
                "a legacy build must not pick up the new model from a preset left in the asset");
            Assert.That(MotorSetupFactory.ResolveModel(config), Is.EqualTo(HandlingModel.Legacy));
        }

        [Test]
        public void Resolve_OnAV2Config_CarriesTheModelThePresetAndTheStickRing()
        {
            var bearing = new GameObject("factory bearing");
            _owned.Add(bearing);
            DrivingFeatureConfig config = Config();
            config.HandlingModel = HandlingModel.V2;
            config.Handling = Profile(HandlingPreset.Drifty);
            config.DriveMode = JoystickDriveMode.CarRelative;
            config.StickDeadZone = 0.21f;

            MotorSetup setup = MotorSetupFactory.Resolve(config, config.DriveMode, bearing.transform);

            Assert.That(setup.Model, Is.EqualTo(HandlingModel.V2));
            Assert.That(setup.Preset, Is.EqualTo(HandlingPreset.Drifty));
            Assert.That(setup.DriveMode, Is.EqualTo(JoystickDriveMode.CarRelative));
            Assert.That(setup.Camera, Is.SameAs(bearing.transform));
            Assert.That(setup.StickDeadZone, Is.EqualTo(0.21f));
            Assert.That(MotorSetupFactory.ResolveModel(config), Is.EqualTo(HandlingModel.V2));
            Assert.That(setup.Handling.MuPeak,
                Is.EqualTo(HandlingProfile.ResolvePreset(HandlingPreset.Drifty).MuPeak),
                "the asset must select the preset the model reads, not a copy of the table");
        }

        [Test]
        public void Resolve_OnAV2ConfigWithoutAProfile_FallsBackToBalanced()
        {
            DrivingFeatureConfig config = Config();
            config.HandlingModel = HandlingModel.V2;
            config.Handling = null;

            MotorSetup setup = MotorSetupFactory.Resolve(config, JoystickDriveMode.Direction, null);

            Assert.That(setup.Model, Is.EqualTo(HandlingModel.V2));
            Assert.That(setup.Preset, Is.EqualTo(HandlingPreset.Balanced));
        }

        [Test]
        public void Presets_AreTheThreeAuthoredOnesAndTheyDiffer()
        {
            HandlingParams grippy = HandlingProfile.ResolvePreset(HandlingPreset.Grippy);
            HandlingParams balanced = HandlingProfile.ResolvePreset(HandlingPreset.Balanced);
            HandlingParams drifty = HandlingProfile.ResolvePreset(HandlingPreset.Drifty);

            Assert.That(grippy.TopSpeedMetersPerSecond, Is.EqualTo(balanced.TopSpeedMetersPerSecond),
                "the presets change grip and yaw authority, never the engine");
            Assert.That(drifty.TopSpeedMetersPerSecond, Is.EqualTo(balanced.TopSpeedMetersPerSecond));
            Assert.That(grippy.MuPeak, Is.GreaterThan(drifty.MuPeak), "grippy must grip harder than drifty");
            Assert.That(drifty.BetaMaxRadians, Is.GreaterThan(grippy.BetaMaxRadians),
                "drifty must allow more sideslip than grippy");
        }

        /// <summary>The demo asset the run loads must be on the new model: that is what the switch is for.</summary>
        [Test]
        public void ShippedConfig_SelectsV2AtBalanced()
        {
            DrivingFeatureConfig config = Resources.Load<DrivingFeatureConfig>("DrivingFeatureConfig");
            Assert.That(config, Is.Not.Null, "the run's config asset is missing from Resources");

            MotorSetup setup = MotorSetupFactory.Resolve(config, config.DriveMode, null);

            Assert.That(setup.Model, Is.EqualTo(HandlingModel.V2), "the shipped config still runs the legacy motor");
            Assert.That(setup.Preset, Is.EqualTo(HandlingPreset.Balanced));
        }

        /// <summary>
        /// INT3: a bot drives the player's car. Before, it got the bare preset (budget 16, gain 4) while
        /// the player's profile asset raised both, so no paceSkill could close the corner-speed gap.
        /// </summary>
        [Test]
        public void ResolveBot_OnAProfiledV2Config_IsThePlayersCarReadCarRelative()
        {
            DrivingFeatureConfig config = Config();
            config.HandlingModel = HandlingModel.V2;
            config.Handling = Profile(HandlingPreset.Balanced);
            config.Handling.LateralAccelBudgetMetersPerSecondSquaredOverride = 30f;
            config.Handling.HeadingGainOverride = 5f;
            config.DriveMode = JoystickDriveMode.Direction;

            MotorSetup player = MotorSetupFactory.ResolveWithProfile(config, config.DriveMode, null);
            MotorSetup bot = MotorSetupFactory.ResolveBot(config);

            Assert.That(bot.Model, Is.EqualTo(HandlingModel.V2));
            Assert.That(bot.DriveMode, Is.EqualTo(JoystickDriveMode.CarRelative), "a bot has no stick to read");
            Assert.That(bot.Handling.LateralAccelBudgetMetersPerSecondSquared, Is.EqualTo(30f));
            Assert.That(bot.Handling.HeadingGain, Is.EqualTo(5f));
            Assert.That(bot.Handling.LateralAccelBudgetMetersPerSecondSquared,
                Is.EqualTo(player.Handling.LateralAccelBudgetMetersPerSecondSquared));
            Assert.That(bot.Handling.TopSpeedMetersPerSecond, Is.EqualTo(player.Handling.TopSpeedMetersPerSecond));
            Assert.That(bot.Handling.AccelMaxMetersPerSecondSquared,
                Is.EqualTo(player.Handling.AccelMaxMetersPerSecondSquared));
            Assert.That(bot.Handling.CatchUp.BaseFactor, Is.EqualTo(player.Handling.CatchUp.BaseFactor),
                "the FEEL2 catch-up rides with the car, not with who drives it");
        }

        [Test]
        public void ResolveBot_OnALegacyOrMissingConfig_IsLegacy()
        {
            Assert.That(MotorSetupFactory.ResolveBot(null).Model, Is.EqualTo(HandlingModel.Legacy));
            DrivingFeatureConfig config = Config();
            config.HandlingModel = HandlingModel.Legacy;
            config.Handling = Profile(HandlingPreset.Balanced);
            Assert.That(MotorSetupFactory.ResolveBot(config).Model, Is.EqualTo(HandlingModel.Legacy));
        }

        private DrivingFeatureConfig Config()
        {
            var config = ScriptableObject.CreateInstance<DrivingFeatureConfig>();
            _owned.Add(config);
            return config;
        }

        private HandlingProfile Profile(HandlingPreset preset)
        {
            var profile = ScriptableObject.CreateInstance<HandlingProfile>();
            profile.Preset = preset;
            _owned.Add(profile);
            return profile;
        }
    }
}
