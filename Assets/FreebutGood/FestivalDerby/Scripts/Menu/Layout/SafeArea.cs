using UnityEngine;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Fits this RectTransform inside Screen.safeArea so notches, rounded corners and gesture bars never
    /// cover a button. Put everything interactive under it; backgrounds stay outside to bleed to the edge.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class SafeArea : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _applied;
        private Vector2Int _appliedScreen;

        private void OnEnable()
        {
            _applied = default(Rect);
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void OnDisable()
        {
            // Back to the full parent: a disabled safe area must not leave the screen squeezed.
            if (_rect == null)
                _rect = (RectTransform)transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0 || (safe == _applied && screen == _appliedScreen))
                return;

            if (_rect == null)
                _rect = (RectTransform)transform;

            _applied = safe;
            _appliedScreen = screen;
            _rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            _rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
