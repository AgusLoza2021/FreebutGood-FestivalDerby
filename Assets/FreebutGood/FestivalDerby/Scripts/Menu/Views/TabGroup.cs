using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>Tab buttons that each show one panel and hide the others (Map, Laps, Bots).</summary>
    public sealed class TabGroup : MonoBehaviour
    {
        [SerializeField] private Button[] _tabs = new Button[0];
        [SerializeField] private GameObject[] _panels = new GameObject[0];
        [SerializeField] private Color _activeColor = new Color(0.20f, 0.42f, 0.86f, 1f);
        [SerializeField] private Color _inactiveColor = new Color(0.13f, 0.19f, 0.30f, 0.92f);

        private UnityAction[] _handlers;
        private int _index = -1;

        public int Index
        {
            get { return _index; }
        }

        public void Select(int index)
        {
            int count = Mathf.Min(_tabs.Length, _panels.Length);
            if (count == 0)
                return;

            _index = Mathf.Clamp(index, 0, count - 1);
            for (int i = 0; i < count; i++)
            {
                if (_panels[i] != null)
                    _panels[i].SetActive(i == _index);

                if (_tabs[i] != null && _tabs[i].targetGraphic != null)
                    _tabs[i].targetGraphic.color = i == _index ? _activeColor : _inactiveColor;
            }
        }

        private void OnEnable()
        {
            _handlers = new UnityAction[_tabs.Length];
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                    continue;

                int captured = i;
                _handlers[i] = () => Select(captured);
                _tabs[i].onClick.AddListener(_handlers[i]);
            }

            Select(_index < 0 ? 0 : _index);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _tabs.Length && _handlers != null; i++)
                if (_tabs[i] != null && _handlers[i] != null)
                    _tabs[i].onClick.RemoveListener(_handlers[i]);
        }
    }
}
