using System;
using UnityEngine;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// The one place menu code asks for text. Loads <c>Resources/Menu/MenuStrings</c> on first use, picks
    /// the saved language (else the device's) and tells listeners when it changes.
    /// </summary>
    public static class Loc
    {
        public const string TablePath = "Menu/MenuStrings";
        private const string LanguagePref = "fd.menu.language";

        private static LocalizationTable _table;
        private static Language _language;
        private static bool _ready;

        public static event Action Changed;

        public static Language Current
        {
            get
            {
                EnsureReady();
                return _language;
            }
        }

        public static void SetLanguage(Language language)
        {
            EnsureReady();
            if (language == _language)
                return;

            _language = language;
            PlayerPrefs.SetInt(LanguagePref, (int)language);
            PlayerPrefs.Save();
            Action changed = Changed;
            if (changed != null)
                changed();
        }

        /// <summary>Text for the key in the current language; the key itself when it is missing.</summary>
        public static string Get(string key)
        {
            EnsureReady();
            return Resolve(_table, key, _language);
        }

        /// <summary>Same, then string.Format with the arguments. A bad format returns the unformatted text.</summary>
        public static string Get(string key, params object[] args)
        {
            string text = Get(key);
            if (args == null || args.Length == 0)
                return text;

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }

        /// <summary>Pure lookup, so tests can resolve against a table they built.</summary>
        public static string Resolve(LocalizationTable table, string key, Language language)
        {
            string text;
            if (table != null && table.TryGet(key, language, out text))
                return text;

            // A visible key beats an empty label: a missing translation is found on the first screenshot.
            return key ?? string.Empty;
        }

        /// <summary>Saved language when valid, else Spanish for a Spanish device, else English.</summary>
        public static Language Detect(int saved, SystemLanguage device)
        {
            if (saved == (int)Language.English || saved == (int)Language.Spanish)
                return (Language)saved;

            return device == SystemLanguage.Spanish ? Language.Spanish : Language.English;
        }

        private static void EnsureReady()
        {
            if (_ready)
                return;

            _ready = true;
            _language = Detect(PlayerPrefs.GetInt(LanguagePref, -1), Application.systemLanguage);
            if (_table == null)
                _table = Resources.Load<LocalizationTable>(TablePath);
        }

        // Enter Play Mode without a domain reload keeps statics alive; stale listeners would point at
        // destroyed labels.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _table = null;
            _ready = false;
            Changed = null;
        }
    }
}
