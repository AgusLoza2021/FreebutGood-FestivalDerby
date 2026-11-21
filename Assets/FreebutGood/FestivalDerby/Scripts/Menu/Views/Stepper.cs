using System;
using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>A number with a minus and a plus button, clamped to a range (laps, bot count).</summary>
    public sealed class Stepper : MonoBehaviour
    {
        [SerializeField] private Button _minus;
        [SerializeField] private Button _plus;
        [SerializeField] private Text _valueLabel;
        [SerializeField] private int _min;
        [SerializeField] private int _max = 10;

        private int _value;

        public event Action<int> Changed;

        public int Value
        {
            get { return _value; }
        }

        public void SetRange(int min, int max)
        {
            _min = min;
            _max = Mathf.Max(min, max);
            SetValue(_value, false);
        }

        public void SetValue(int value, bool notify)
        {
            int clamped = Mathf.Clamp(value, _min, _max);
            bool changed = clamped != _value;
            _value = clamped;
            Refresh();
            if (!changed || !notify)
                return;

            Action<int> handler = Changed;
            if (handler != null)
                handler(_value);
        }

        private void OnEnable()
        {
            if (_minus != null)
                _minus.onClick.AddListener(OnMinus);
            if (_plus != null)
                _plus.onClick.AddListener(OnPlus);
            Refresh();
        }

        private void OnDisable()
        {
            if (_minus != null)
                _minus.onClick.RemoveListener(OnMinus);
            if (_plus != null)
                _plus.onClick.RemoveListener(OnPlus);
        }

        private void OnMinus()
        {
            SetValue(_value - 1, true);
        }

        private void OnPlus()
        {
            SetValue(_value + 1, true);
        }

        private void Refresh()
        {
            if (_valueLabel != null)
                _valueLabel.text = _value.ToString();
            if (_minus != null)
                _minus.interactable = _value > _min;
            if (_plus != null)
                _plus.interactable = _value < _max;
        }
    }
}
