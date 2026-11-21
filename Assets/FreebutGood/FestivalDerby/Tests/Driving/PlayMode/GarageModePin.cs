using System.Text.RegularExpressions;
using UnityEngine;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Pins the garage mode a booted Gameplay run will play. The menu's PLAY button starts the mode saved in
    /// the game model (PlayerPrefs "model", field GarageMode), so a TIMING or derby left there by the owner's
    /// own play would start a run with no bots, or hunting bots, and fail tests that never asked for it.
    /// Only that one field is rewritten; every other saved value is kept, and the callers' TearDown puts the
    /// original save back. A test assembly cannot see GameModel or ModeChoice, so the field is edited as text.
    /// </summary>
    internal static class GarageModePin
    {
        /// <summary>ModeChoice.Lap, the garage's own default: a three-lap race with the bots on the grid.</summary>
        public const int LapRace = 1;

        private static readonly Regex Field = new Regex("\"GarageMode\"\\s*:\\s*-?\\d+");

        /// <summary>
        /// Writes the save under <paramref name="key"/> with the garage mode set to <paramref name="mode"/>,
        /// starting from <paramref name="seed"/> when there is no save yet.
        /// </summary>
        public static void Apply(string key, string seed, int mode = LapRace)
        {
            string json = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : seed;
            PlayerPrefs.SetString(key, WithGarageMode(json, mode));
        }

        public static string WithGarageMode(string json, int mode)
        {
            string value = "\"GarageMode\":" + mode;
            if (string.IsNullOrEmpty(json))
                return "{" + value + "}";

            if (Field.IsMatch(json))
                return Field.Replace(json, value, 1);

            int open = json.IndexOf('{');
            if (open < 0)
                return json;

            string rest = json.Substring(open + 1);
            string separator = rest.TrimStart().StartsWith("}") ? "" : ",";
            return json.Substring(0, open + 1) + value + separator + rest;
        }
    }
}
