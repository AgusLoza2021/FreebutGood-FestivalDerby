using System;
using System.Collections.Generic;
using UnityEngine;

namespace FestivalDerby.Menu
{
    public enum Language
    {
        English = 0,
        Spanish = 1,
    }

    /// <summary>
    /// Every menu string in every language, as one editable asset. Add a language by adding a column to
    /// <see cref="Entry"/> and a case to <see cref="TryGet"/>; nothing else reads the fields directly.
    /// </summary>
    [CreateAssetMenu(fileName = "MenuStrings", menuName = "Festival Derby/Menu/Localization Table")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            [TextArea] public string English;
            [TextArea] public string Spanish;
        }

        [SerializeField] private Entry[] _entries = new Entry[0];

        // Built once on first lookup: a per-lookup scan would walk the whole table for every label.
        private Dictionary<string, int> _index;

        public Entry[] Entries
        {
            get { return _entries; }
        }

        /// <summary>
        /// Text for the key in the language, falling back to English when that language has no text yet.
        /// False when the key is unknown or both texts are empty.
        /// </summary>
        public bool TryGet(string key, Language language, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(key) || _entries == null)
                return false;

            if (_index == null)
                BuildIndex();

            int slot;
            if (!_index.TryGetValue(key, out slot))
                return false;

            Entry entry = _entries[slot];
            string primary = language == Language.Spanish ? entry.Spanish : entry.English;
            text = !string.IsNullOrEmpty(primary) ? primary : entry.English;
            return !string.IsNullOrEmpty(text);
        }

        private void BuildIndex()
        {
            _index = new Dictionary<string, int>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                string key = _entries[i].Key;
                // The first entry wins, so a duplicate added later cannot silently change a label.
                if (!string.IsNullOrEmpty(key) && !_index.ContainsKey(key))
                    _index.Add(key, i);
            }
        }

        private void OnEnable()
        {
            _index = null;
        }

        private void OnValidate()
        {
            _index = null;
        }
    }
}
