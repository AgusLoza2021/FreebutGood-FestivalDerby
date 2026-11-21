using System;
using UnityEngine;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// One choice in a list (a mode, a map, a car, a bot level). Edit these in the Inspector of the list:
    /// reorder them, lock one, point a title at another key, swap its icon or model.
    /// </summary>
    [Serializable]
    public sealed class MenuOption
    {
        [Tooltip("What the game reads back: MapAsset.MapId for maps, CarVisualEntry.Id for cars.")]
        public string Id = "";
        [Tooltip("Localization key of the name.")]
        public string TitleKey = "";
        [Tooltip("Localization key of the second line. Empty hides it.")]
        public string SubtitleKey = "";
        public Sprite Icon;
        [Tooltip("3D model shown in the garage preview. Only used by the car list.")]
        public GameObject Model;
        [Tooltip("Shown but not selectable.")]
        public bool Locked;
    }
}
