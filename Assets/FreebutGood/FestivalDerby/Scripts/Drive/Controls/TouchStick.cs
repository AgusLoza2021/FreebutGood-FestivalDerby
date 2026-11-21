using FestivalDerby.Driving.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The on-screen stick: a value in [-1, 1] per axis with y up the screen, on the same
    /// <see cref="ISteerSource"/> contract the keyboard uses.
    /// </summary>
    public sealed class TouchStick : MonoBehaviour, ISteerSource, IPointerDownHandler, IDragHandler,
        IPointerUpHandler
    {
        public RectTransform Area;
        public RectTransform Handle;
        public float Radius = 78f;

        private Vector2 _value;
        private bool _active;
        private int _pointerId = -1;

        public float Horizontal
        {
            get { return _value.x; }
        }

        public float Vertical
        {
            get { return _value.y; }
        }

        public bool HasInput
        {
            // A released or centred stick is no input, so the keyboard can take over.
            get { return _active && _value.sqrMagnitude > 0.0001f; }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_active)
                return;

            _active = true;
            _pointerId = eventData.pointerId;
            Track(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId)
                Track(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId)
                return;

            _active = false;
            _pointerId = -1;
            _value = Vector2.zero;
            if (Handle != null)
                Handle.anchoredPosition = Vector2.zero;
        }

        private void Track(PointerEventData eventData)
        {
            if (Area == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Area, eventData.position,
                    eventData.pressEventCamera, out Vector2 local))
                return;

            _value = Vector2.ClampMagnitude(local / Mathf.Max(Radius, 1f), 1f);
            if (Handle != null)
                Handle.anchoredPosition = _value * Radius;
        }
    }
}
