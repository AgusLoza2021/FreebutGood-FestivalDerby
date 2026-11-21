using System;
using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// One card of a <see cref="ChoiceList"/>. Three looks: normal, selected, locked. Every reference is
    /// optional, so a card restyled by hand keeps working with whatever parts it still has.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class SelectableCard : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private LocalizedText _title;
        [SerializeField] private LocalizedText _subtitle;
        [SerializeField] private GameObject _lockBadge;

        [SerializeField] private Color _normalColor = new Color(0.13f, 0.19f, 0.30f, 0.92f);
        [SerializeField] private Color _selectedColor = new Color(0.20f, 0.42f, 0.86f, 1f);
        [SerializeField] private Color _lockedColor = new Color(0.16f, 0.17f, 0.20f, 0.75f);

        private Button _button;
        private bool _selected;

        public event Action<SelectableCard> Clicked;

        public MenuOption Option { get; private set; }
        public int Index { get; private set; }

        public void Bind(int index, MenuOption option)
        {
            Index = index;
            Option = option;
            gameObject.name = "Card_" + (option != null && !string.IsNullOrEmpty(option.Id) ? option.Id : index.ToString());

            if (_title != null)
                _title.SetKey(option != null ? option.TitleKey : "");

            bool hasSubtitle = option != null && !string.IsNullOrEmpty(option.SubtitleKey);
            if (_subtitle != null)
            {
                _subtitle.gameObject.SetActive(hasSubtitle);
                if (hasSubtitle)
                    _subtitle.SetKey(option.SubtitleKey);
            }

            bool hasIcon = option != null && option.Icon != null;
            if (_icon != null)
            {
                _icon.gameObject.SetActive(hasIcon);
                if (hasIcon)
                    _icon.sprite = option.Icon;
            }

            if (_lockBadge != null)
                _lockBadge.SetActive(option != null && option.Locked);

            ApplyColor();
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            ApplyColor();
        }

        private void OnEnable()
        {
            if (_button == null)
                _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
        }

        private void OnDisable()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (Option == null || Option.Locked)
                return;

            Action<SelectableCard> clicked = Clicked;
            if (clicked != null)
                clicked(this);
        }

        private void ApplyColor()
        {
            if (_background == null)
                return;

            if (Option != null && Option.Locked)
                _background.color = _lockedColor;
            else
                _background.color = _selected ? _selectedColor : _normalColor;
        }
    }
}
