using FestivalDerby.Driving.Runtime;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// Desktop input. WASD / arrows publish the same screen-direction stick the touch stick does, so the
    /// car reads one kind of input whatever device produced it. The pedal keys are plain flags: the
    /// bootstrap is the only writer of the control slots.
    /// </summary>
    public sealed class KeyboardSteer : MonoBehaviour, ISteerSource
    {
        public KeyCode BrakeKey = KeyCode.Space;
        public KeyCode ReverseKey = KeyCode.LeftControl;
        public KeyCode NitroKey = KeyCode.LeftShift;

        private float _horizontal;
        private float _vertical;

        public float Horizontal
        {
            get { return _horizontal; }
        }

        public float Vertical
        {
            get { return _vertical; }
        }

        public bool HasInput
        {
            get { return Mathf.Abs(_horizontal) > 0.01f || Mathf.Abs(_vertical) > 0.01f; }
        }

        public bool BrakeHeld
        {
            get { return Input.GetKey(BrakeKey); }
        }

        public bool ReverseHeld
        {
            get { return Input.GetKey(ReverseKey); }
        }

        public bool NitroHeld
        {
            get { return Input.GetKey(NitroKey); }
        }

        private void Update()
        {
            // Raw keys on top of the axes: a project whose InputManager lost its axis pair would
            // otherwise be undrivable with no visible reason.
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                horizontal -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                horizontal += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                vertical -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                vertical += 1f;

            _horizontal = Mathf.Clamp(horizontal, -1f, 1f);
            _vertical = Mathf.Clamp(vertical, -1f, 1f);
        }
    }
}
