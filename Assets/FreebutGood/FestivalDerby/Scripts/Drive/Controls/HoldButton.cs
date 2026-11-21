using UnityEngine;
using UnityEngine.EventSystems;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// A pedal that publishes whether it is held. uGUI's Button reports a click, and a brake is a state,
    /// so the controls need the state and not the event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>True while a finger is down on this pedal.</summary>
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            Held = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Held = false;
        }

        private void OnDisable()
        {
            // A disabled object never receives its pointer-up; a pedal stuck down is worse than a missed tap.
            Held = false;
        }
    }
}
