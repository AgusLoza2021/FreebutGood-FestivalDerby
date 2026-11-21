using System;
using FestivalDerby.Bots;
using UnityEngine;

namespace FestivalDerby.Menu
{
    public enum BotLevel
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
    }

    /// <summary>
    /// What the player chose in the menu and the Gameplay scene reads at start: ids only, so the menu
    /// never holds a reference into the game and the game resolves each id against its own catalogs.
    /// </summary>
    [Serializable]
    public sealed class MenuSelection
    {
        public const int MinLaps = 1;
        public const int MaxLaps = 10;
        /// <summary>Same cap the bot spawner enforces.</summary>
        public const int MaxBots = BotField.MaxBots;

        public const string ModeRace = "Race";
        public const string ModeTruckChase = "TruckChase";
        public const string ModeFreeDrive = "FreeDrive";
        public const string ModeTimeTrial = "TimeTrial";

        public string ModeId = "";
        /// <summary>MapAsset.MapId of the chosen circuit.</summary>
        public string MapId = "";
        /// <summary>CarVisualEntry.Id of the chosen car.</summary>
        public string CarId = "";
        public int Laps = 3;
        public int BotCount = 5;
        public BotLevel BotLevel = BotLevel.Medium;

        /// <summary>Bots that actually race: Free Drive is a solo drive whatever the slider says.</summary>
        public int EffectiveBotCount
        {
            get { return ModeId == ModeFreeDrive ? 0 : Mathf.Clamp(BotCount, 0, MaxBots); }
        }

        public void Clamp()
        {
            ModeId = ModeId ?? "";
            MapId = MapId ?? "";
            CarId = CarId ?? "";
            Laps = Mathf.Clamp(Laps, MinLaps, MaxLaps);
            BotCount = Mathf.Clamp(BotCount, 0, MaxBots);
            if (!Enum.IsDefined(typeof(BotLevel), BotLevel))
                BotLevel = BotLevel.Medium;
        }

        public MenuSelection Clone()
        {
            return (MenuSelection)MemberwiseClone();
        }
    }

    /// <summary>
    /// The share of cornering grip the slowest and fastest bot dare to use at each level. Gameplay copies
    /// these into BotsConfig's pace range; they stay inside that range's 0.5..1 limits.
    /// </summary>
    public static class BotLevelRules
    {
        public static void Pace(BotLevel level, out float slowest, out float fastest)
        {
            switch (level)
            {
                case BotLevel.Easy:
                    slowest = 0.6f;
                    fastest = 0.8f;
                    break;
                case BotLevel.Hard:
                    slowest = 0.9f;
                    fastest = 1f;
                    break;
                default:
                    slowest = 0.8f;
                    fastest = 0.95f;
                    break;
            }
        }
    }
}
