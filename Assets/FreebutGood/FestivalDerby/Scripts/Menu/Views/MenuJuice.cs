using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Entrance, idle pulse and press squash for one element. It animates a window or a button, never the
    /// full-screen root. Whatever it was doing, <c>OnDisable</c> puts scale and alpha back to what the
    /// element had when it was first enabled, so a screen disabled mid-animation can never come back
    /// invisible or shrunk.
    /// </summary>
    [AddComponentMenu("Festival Derby/Menu/Menu Juice")]
    public sealed class MenuJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("What moves. Empty uses this object.")]
        [SerializeField] private RectTransform _target;

        [Header("Entrance")]
        [SerializeField] private bool _entrance = true;
        [SerializeField] private float _entranceDelay;
        [SerializeField] private float _entranceSeconds = 0.22f;
        [SerializeField] private float _entranceFromScale = 0.92f;

        [Header("Idle pulse")]
        [SerializeField] private bool _pulse;
        [SerializeField] private float _pulseAmount = 0.035f;
        [SerializeField] private float _pulseSpeed = 2.4f;

        [Header("Press")]
        [SerializeField] private bool _press;
        [SerializeField] private float _pressScale = 0.95f;

        private Vector3 _restScale = Vector3.one;
        private float _restAlpha = 1f;
        private CanvasGroup _group;
        private bool _captured;
        private bool _pressed;
        private Coroutine _routine;

        private void Awake()
        {
            Capture();
        }

        private void OnEnable()
        {
            Capture();
            Restore();
            if (_entrance)
                Apply(_entranceFromScale, 0f);

            if (_entrance || _pulse)
                _routine = StartCoroutine(Run());
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _pressed = false;
            Restore();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_press)
                return;

            _pressed = true;
            if (_routine == null)
                Apply(_pressScale, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void Release()
        {
            if (!_pressed)
                return;

            _pressed = false;
            if (_routine == null)
                Restore();
        }

        private IEnumerator Run()
        {
            if (_entrance)
            {
                float wait = _entranceDelay;
                while (wait > 0f)
                {
                    wait -= Time.unscaledDeltaTime;
                    yield return null;
                }

                float t = 0f;
                float seconds = Mathf.Max(0.01f, _entranceSeconds);
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / seconds;
                    float eased = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));
                    float scale = Mathf.Lerp(_entranceFromScale, 1f, eased);
                    Apply(_pressed ? scale * _pressScale : scale, eased);
                    yield return null;
                }

                Apply(_pressed ? _pressScale : 1f, 1f);
            }

            if (!_pulse)
            {
                _routine = null;
                yield break;
            }

            float clock = 0f;
            while (true)
            {
                clock += Time.unscaledDeltaTime * _pulseSpeed;
                float scale = 1f + _pulseAmount * Mathf.Sin(clock);
                Apply(_pressed ? _pressScale : scale, 1f);
                yield return null;
            }
        }

        private void Capture()
        {
            if (_captured)
                return;

            if (_target == null)
                _target = (RectTransform)transform;

            _restScale = _target.localScale;
            _group = _target.GetComponent<CanvasGroup>();
            _restAlpha = _group != null ? _group.alpha : 1f;
            _captured = true;
        }

        private void Apply(float scale, float alpha)
        {
            _target.localScale = _restScale * scale;
            if (_group != null)
                _group.alpha = _restAlpha * alpha;
        }

        private void Restore()
        {
            if (_target == null)
                return;

            _target.localScale = _restScale;
            if (_group != null)
                _group.alpha = _restAlpha;
        }
    }
}
