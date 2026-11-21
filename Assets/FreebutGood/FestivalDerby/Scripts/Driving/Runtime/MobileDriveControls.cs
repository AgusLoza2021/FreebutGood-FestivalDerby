using System;
using FestivalDerby.Driving;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>Which held control a scene button feeds. Drift gets no slot on purpose: the
    /// GDD defines it as braking while steering, which the motor already expresses.</summary>
    public enum DriveControlSlot
    {
        Brake,
        Reverse,
        Nitro
    }

    /// <summary>
    /// Scene-owned mobile driving controls: composes one steer source, the drive reading of the
    /// stick and the three held flags into the latest CarInput and hands it to the bound driver
    /// from Update, the only callback permitted for input. CurrentInput is the honest observation
    /// point for composition: the alternative would be reading physics side effects, which the
    /// motor's own yaw limit could flatten and turn a bound check into a vacuous pass.
    ///
    /// The stick drives both axes, so the scene no longer needs a brake button to move: the
    /// translation itself is engine-free and lives in JoystickDriveMapping, and this class only
    /// supplies it with the camera axes and the car's own heading.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileDriveControls : MonoBehaviour
    {
        [SerializeField] private LocalCarFixedStepDriver _driver;
        // The bearing the stick is read against. Left empty, the car's own heading is used, which
        // degrades Direction mode into "forward is where the car already points" instead of
        // refusing to drive at all.
        [SerializeField] private Transform _camera;

        private ISteerSource _steer;
        private JoystickDriveMode _mode = JoystickDriveMode.Direction;
        private float _stickDeadZone = 0.15f;
        private bool _brake;
        private bool _reverse;
        private bool _nitro;
        private bool _hasSent;
        private CarInput _lastSent;

        /// <summary>The input actually sent to the driver; starts at the neutral default and
        /// changes only when a composed input differs from the last one sent.</summary>
        public CarInput CurrentInput { get; private set; }

        /// <summary>Binds the control to its driver and steer source. Both are validated before
        /// any field changes, so a rejected bind cannot leave half-wired state behind.</summary>
        public void Bind(LocalCarFixedStepDriver driver, ISteerSource steer)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (steer == null) throw new ArgumentNullException(nameof(steer));

            _driver = driver;
            _steer = steer;
            // A driver clears its stored input when it binds, so the control's view of what the
            // driver holds must clear with it, or CurrentInput would describe a stale input.
            CurrentInput = default;
            _lastSent = default;
            _hasSent = false;
        }

        /// <summary>
        /// Sets how the stick is read and what its requested heading is measured against. The scene
        /// bootstrap owns this call because it is the only scene code that can read the feature
        /// config; the defaults above are already the shipped reading, so skipping it is safe.
        /// </summary>
        public void Configure(JoystickDriveMode mode, float stickDeadZone, Transform camera)
        {
            _mode = mode;
            _stickDeadZone = stickDeadZone;
            _camera = camera;
        }

        /// <summary>Null is a legal value: it means no steering input this frame.</summary>
        public void SetSteerSource(ISteerSource steer)
        {
            _steer = steer;
        }

        /// <summary>Stores one held control. Idempotent, so a duplicated pointer release and a
        /// release arriving after OnDisable are both harmless.</summary>
        public void Set(DriveControlSlot slot, bool value)
        {
            switch (slot)
            {
                case DriveControlSlot.Brake:
                    _brake = value;
                    break;
                case DriveControlSlot.Reverse:
                    _reverse = value;
                    break;
                default:
                    _nitro = value;
                    break;
            }
        }

        public void ReleaseAll()
        {
            _brake = false;
            _reverse = false;
            _nitro = false;
        }

        private void Update()
        {
            // No driver means no consumer; composing anyway would only allocate work with nowhere
            // to go, and CurrentInput must stay neutral while nothing is actually being driven.
            if (_driver == null) return;

            ResolveAxes(out float throttle, out float steer, out float stickX, out float stickY);
            // The raw stick rides along with the axes it produced. V2 steers by the heading the stick names,
            // and a heading cannot be recovered from the composed (throttle, steer) pair, so the sampled
            // gesture is published as-is for whoever needs it: the V2 motor locally, and the server through
            // the input command. Legacy model reads only the axes and is unaffected by the extra fields.
            // The stick owns the reverse slot too: the reading already puts a pull behind the car in the
            // pedal's sign, and carrying that across at the threshold is what lets the V2 motor, which
            // never sees a negative pedal, back the car up. A held button still works.
            bool reverse = _reverse || JoystickDriveMapping.IsReversePedal(throttle);
            var composed = new CarInput(steer, throttle, _brake, reverse, _nitro, stickX, stickY);

            // A steady finger must not cost a call per frame; only a real change is forwarded.
            if (_hasSent && Same(composed, _lastSent)) return;

            _lastSent = composed;
            _hasSent = true;
            CurrentInput = composed;
            _driver.SetInput(composed);
        }

        private void ResolveAxes(out float throttle, out float steer, out float stickX, out float stickY)
        {
            throttle = 0f;
            steer = 0f;
            stickX = 0f;
            stickY = 0f;

            var source = _steer;
            if (source == null || !source.HasInput) return;

            float horizontal = Sanitize(source.Horizontal);
            float vertical = Sanitize(source.Vertical);
            stickX = horizontal;
            stickY = vertical;

            if (_mode != JoystickDriveMode.CarRelative)
            {
                // The last step's pose, not this frame's: reading it costs nothing and a heading one
                // tick stale is a gesture-width error at worst.
                CarSnapshot snapshot = _driver.Snapshot();
                Vector3 carForward = snapshot.HasBody ? snapshot.Rotation * Vector3.forward : Vector3.zero;
                Vector3 forward;
                Vector3 right;
                if (_camera != null)
                {
                    forward = _camera.forward;
                    right = _camera.right;
                }
                else
                {
                    forward = carForward;
                    right = snapshot.HasBody ? snapshot.Rotation * Vector3.right : Vector3.zero;
                }

                JoystickDriveMapping.Direction(
                    horizontal, vertical,
                    forward.x, forward.z, right.x, right.z,
                    carForward.x, carForward.z,
                    _stickDeadZone, out throttle, out steer);
                return;
            }

            JoystickDriveMapping.CarRelative(horizontal, vertical, _stickDeadZone, out throttle, out steer);
        }

        private static float Sanitize(float value)
        {
            // A non-finite source value must not reach the motor at all; the rules only guard
            // the speed they are given, not the axes.
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool Same(in CarInput left, in CarInput right)
        {
            return left.steer == right.steer
                && left.throttle == right.throttle
                && left.brake == right.brake
                && left.reverse == right.reverse
                && left.nitroPressed == right.nitroPressed
                && left.stickX == right.stickX
                && left.stickY == right.stickY;
        }
    }
}
