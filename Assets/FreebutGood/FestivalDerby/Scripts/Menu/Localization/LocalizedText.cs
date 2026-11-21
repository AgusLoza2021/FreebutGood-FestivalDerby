using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Keeps a Text on the string of its key and re-reads it when the language changes. It is the only
    /// writer of that label: a controller changes the text through <see cref="SetKey"/>, never by setting
    /// Text.text, so two writers never fight over one string.
    /// </summary>
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Tooltip("Key in Resources/Menu/MenuStrings. Edit the strings there, not on the label.")]
        [SerializeField] private string _key = "";

        private Text _text;
        private object[] _args;

        public string Key
        {
            get { return _key; }
        }

        public void SetKey(string key, params object[] args)
        {
            _key = key;
            _args = args;
            Refresh();
        }

        private void OnEnable()
        {
            Loc.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            Loc.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
                return;

            if (_text == null)
                _text = GetComponent<Text>();
            _text.text = Loc.Get(_key, _args);
        }
    }
}
