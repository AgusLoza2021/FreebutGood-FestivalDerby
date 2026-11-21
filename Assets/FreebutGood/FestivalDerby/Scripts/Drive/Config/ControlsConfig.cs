using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// How the player talks to the car: the on-screen stick and pedals, the keyboard, and the reset key.
    /// The stick's dead zone and the drive reading (heading vs. car-relative) are car tuning and stay in
    /// the DrivingFeatureConfig asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControlsConfig : MonoBehaviour
    {
        [Tooltip("Build the on-screen stick and the BRAKE / NITRO / RESET buttons. Phones need it; in the " +
            "Editor the keyboard works either way.")]
        [SerializeField] private bool _touchControls = true;

        [Tooltip("WASD / arrows to steer, Space brake, Left Shift nitro, Left Ctrl reverse.")]
        [SerializeField] private bool _keyboard = true;

        [Tooltip("Puts the car back on the track at once.")]
        [SerializeField] private KeyCode _resetKey = KeyCode.R;

        public bool TouchControls
        {
            get { return _touchControls; }
            set { _touchControls = value; }
        }

        public bool Keyboard
        {
            get { return _keyboard; }
            set { _keyboard = value; }
        }

        public KeyCode ResetKey
        {
            get { return _resetKey; }
            set { _resetKey = value; }
        }
    }
}
