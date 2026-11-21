using FestivalDerby.Menu;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// Carries the main menu's choice into the Gameplay configs before the drive is built. It does
    /// nothing unless Play was pressed in the menu, so a Gameplay scene opened on its own keeps exactly
    /// what is authored in its Config objects.
    /// </summary>
    public static class MenuLaunch
    {
        // Same folder the menu builder lists maps from.
        private const string MapsResources = "Maps";

        /// <summary>The applied selection, or null when this run did not come from the menu.</summary>
        public static MenuSelection Apply(CarConfig car, TrackConfig track, BotsConfig bots)
        {
            if (!MenuSession.HasLaunch)
                return null;

            MenuSelection selection = MenuSession.Current;

            if (car != null && !string.IsNullOrEmpty(selection.CarId))
                car.UseModel(selection.CarId);

            if (track != null && !string.IsNullOrEmpty(selection.MapId))
            {
                MapAsset map = FindMap(selection.MapId);
                if (map != null)
                    track.Map = map;
                else
                    Debug.LogWarning("[menu] map '" + selection.MapId + "' is not in Resources/" + MapsResources +
                        "; keeping the Track config's map.");
            }

            if (bots != null)
            {
                bots.Count = selection.EffectiveBotCount;
                float slowest, fastest;
                BotLevelRules.Pace(selection.BotLevel, out slowest, out fastest);
                bots.SetPace(slowest, fastest);
            }

            return selection;
        }

        private static MapAsset FindMap(string id)
        {
            foreach (MapAsset map in Resources.LoadAll<MapAsset>(MapsResources))
            {
                string key = string.IsNullOrEmpty(map.MapId) ? map.name : map.MapId;
                if (key == id)
                    return map;
            }

            return null;
        }
    }
}
