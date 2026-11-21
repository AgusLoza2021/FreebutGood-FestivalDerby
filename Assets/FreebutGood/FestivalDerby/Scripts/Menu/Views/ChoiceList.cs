using System;
using System.Collections.Generic;
using UnityEngine;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// A group of cards where exactly one is selected. The options are plain data in the Inspector; the
    /// cards are real objects in the scene, so you can move, restyle or remove them by hand. If there are
    /// more options than cards, the extra cards are cloned from the first one; if there are fewer, the
    /// leftovers are hidden.
    /// </summary>
    public sealed class ChoiceList : MonoBehaviour
    {
        [SerializeField] private SelectableCard[] _cards = new SelectableCard[0];
        [SerializeField] private MenuOption[] _options = new MenuOption[0];

        private readonly List<SelectableCard> _live = new List<SelectableCard>();
        private bool _bound;
        private int _selectedIndex = -1;

        /// <summary>Raised with the index and option when the player picks a card or code selects one.</summary>
        public event Action<int, MenuOption> Selected;

        public int Count
        {
            get { return _options != null ? _options.Length : 0; }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
        }

        public MenuOption SelectedOption
        {
            get { return _selectedIndex >= 0 && _selectedIndex < Count ? _options[_selectedIndex] : null; }
        }

        public MenuOption OptionAt(int index)
        {
            return index >= 0 && index < Count ? _options[index] : null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Count; i++)
                if (_options[i] != null && _options[i].Id == id)
                    return i;
            return -1;
        }

        /// <summary>Selects the option with this id, else the first one that is not locked.</summary>
        public void SelectById(string id, bool notify)
        {
            int index = string.IsNullOrEmpty(id) ? -1 : IndexOf(id);
            if (index < 0 || _options[index].Locked)
                index = FirstUnlocked();
            Select(index, notify);
        }

        /// <summary>Moves the selection by one, skipping locked options and wrapping around.</summary>
        public void SelectRelative(int step, bool notify)
        {
            int count = Count;
            if (count == 0 || step == 0)
                return;

            int index = _selectedIndex;
            for (int tries = 0; tries < count; tries++)
            {
                index = ((index + step) % count + count) % count;
                if (_options[index] != null && !_options[index].Locked)
                {
                    Select(index, notify);
                    return;
                }
            }
        }

        public void Select(int index, bool notify)
        {
            EnsureBound();
            if (index < 0 || index >= Count)
            {
                _selectedIndex = -1;
                Paint();
                return;
            }

            _selectedIndex = index;
            Paint();
            if (!notify)
                return;

            Action<int, MenuOption> selected = Selected;
            if (selected != null)
                selected(index, _options[index]);
        }

        private int FirstUnlocked()
        {
            for (int i = 0; i < Count; i++)
                if (_options[i] != null && !_options[i].Locked)
                    return i;
            return -1;
        }

        private void Awake()
        {
            EnsureBound();
        }

        private void OnEnable()
        {
            EnsureBound();
            for (int i = 0; i < _live.Count; i++)
                _live[i].Clicked += OnCardClicked;
            Paint();
        }

        private void OnDisable()
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null)
                    _live[i].Clicked -= OnCardClicked;
        }

        private void OnCardClicked(SelectableCard card)
        {
            Select(card.Index, true);
        }

        private void EnsureBound()
        {
            if (_bound)
                return;

            _bound = true;
            _live.Clear();
            int cardCount = _cards != null ? _cards.Length : 0;
            for (int i = 0; i < cardCount; i++)
                if (_cards[i] != null)
                    _live.Add(_cards[i]);

            if (_live.Count == 0 && Count > 0)
            {
                Debug.LogWarning("[menu] " + name + " has options but no card to show them. Run Festival Derby > Menu > Build Main Menu.", this);
                return;
            }

            for (int i = 0; i < _live.Count; i++)
                if (i >= Count)
                    _live[i].gameObject.SetActive(false);

            for (int i = _live.Count; i < Count; i++)
            {
                SelectableCard clone = Instantiate(_live[0], _live[0].transform.parent);
                clone.gameObject.SetActive(true);
                _live.Add(clone);
            }

            for (int i = 0; i < Count; i++)
                _live[i].Bind(i, _options[i]);
        }

        private void Paint()
        {
            for (int i = 0; i < _live.Count && i < Count; i++)
                _live[i].SetSelected(i == _selectedIndex);
        }
    }
}
