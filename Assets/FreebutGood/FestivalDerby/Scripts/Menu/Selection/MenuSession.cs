using System;
using UnityEngine;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// The menu's choice, kept between scenes and between launches. Gameplay reads <see cref="Current"/>;
    /// the menu writes it and calls <see cref="Save"/>.
    /// </summary>
    public static class MenuSession
    {
        private const string Pref = "fd.menu.selection";

        private static MenuSelection _current;

        /// <summary>
        /// True from the moment Play is pressed until <see cref="ResetLaunch"/>. Gameplay applies the
        /// selection only then, so opening the Gameplay scene directly keeps its authored configuration.
        /// </summary>
        public static bool HasLaunch { get; private set; }

        public static void MarkLaunched()
        {
            HasLaunch = true;
        }

        public static void ResetLaunch()
        {
            HasLaunch = false;
        }

        public static MenuSelection Current
        {
            get
            {
                if (_current == null)
                    _current = Parse(PlayerPrefs.GetString(Pref, ""));
                return _current;
            }
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Pref, Serialize(Current));
            PlayerPrefs.Save();
        }

        public static string Serialize(MenuSelection selection)
        {
            return JsonUtility.ToJson(selection ?? new MenuSelection());
        }

        /// <summary>Defaults for empty or unreadable text, so a corrupt save never blocks the menu.</summary>
        public static MenuSelection Parse(string json)
        {
            MenuSelection selection = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    selection = JsonUtility.FromJson<MenuSelection>(json);
                }
                catch (ArgumentException)
                {
                    selection = null;
                }
            }

            if (selection == null)
                selection = new MenuSelection();
            selection.Clamp();
            return selection;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            HasLaunch = false;
        }
    }
}
