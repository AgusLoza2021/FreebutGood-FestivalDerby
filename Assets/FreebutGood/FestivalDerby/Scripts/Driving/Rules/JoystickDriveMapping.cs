using System;

namespace FestivalDerby.Driving
{
    /// <summary>
    /// Engine-free translation of one joystick sample into the drive axis and the steering axis.
    /// It lives in the rules assembly on purpose: it is pure float math over the camera and car
    /// axes the caller already has, so it can be tested without a scene and without Unity, and the
    /// adapter above it only has to supply those two axes.
    ///
    /// Three modes. CarRelative reads the stick as the car's own pedals and wheel: up drives, down
    /// reverses, sideways steers. Direction reads the stick as a heading on screen and derives the
    /// pedal: the car accelerates while the requested heading is in front of it, and backs up when
    /// the requested heading is behind it, which is what a kart racer does when the player throws
    /// the stick the other way. Heading Drive also reads the stick as a heading, but keeps the
    /// pedal forward and lets the heading loop turn the car, which is the reading the owner asked
    /// for: the finger follows the road, and the brake stays a button.
    /// </summary>
    public static class JoystickDriveMapping
    {
        /// <summary>
        /// Past this heading error the target is behind the car and the stick means "back up"
        /// instead of "turn and go".
        /// </summary>
        public const float ReverseAngleDegrees = 120f;

        /// <summary>The heading error at which the steering axis is already fully deflected.</summary>
        public const float FullSteerAngleDegrees = 45f;

        /// <summary>
        /// How deep a reversed pedal has to go before it is a reverse request and not a stick on its
        /// way back to centre. Past the dead zone a shallow pull is already a small negative pedal,
        /// and reversing on that would make every turn-in creep backwards.
        /// </summary>
        public const float ReversePedalThreshold = 0.6f;

        /// <summary>
        /// Whether a composed pedal asks for reverse. Both stick readings already put a pull behind
        /// the car in the pedal's sign, but the V2 motor reads the stick itself, never lets a pedal go
        /// negative, and reaches reverse only through the reverse flag: this is the one rule that
        /// carries the gesture from the reading that did understand it to the flag the motor does read.
        /// </summary>
        public static bool IsReversePedal(float throttleFromStick)
        {
            return Finite(throttleFromStick) <= -ReversePedalThreshold;
        }

        /// <summary>Below this squared length an axis carries no usable direction.</summary>
        private const float MinAxisSqrMagnitude = 1e-8f;

        /// <summary>
        /// Stick as pedals: up is forward, down is reverse, sideways steers. The two axes stay
        /// independent, so a sideways push holds the wheel with the foot off the pedal, and the
        /// pedal depth comes from the vertical travel alone. Unlike <see cref="Direction"/> the
        /// stick's length is not the pedal, because here the axes are a pedal and a wheel and not a
        /// single heading.
        /// </summary>
        public static void CarRelative(
            float stickX, float stickY, float deadZone, out float throttle, out float steer)
        {
            throttle = 0f;
            steer = 0f;

            float x = Finite(stickX);
            float y = Finite(stickY);
            float pedal = ThrottleFromMagnitude(y < 0f ? -y : y, deadZone);
            throttle = y < 0f ? -pedal : pedal;
            // The steering dead zone belongs to the rules, which apply it to this value next.
            steer = Clamp(x, -1f, 1f);
        }

        /// <summary>
        /// Stick as a heading. The requested direction comes from the camera's own axes, so up on
        /// the stick means "away from the camera" no matter which way the car is pointing, and the
        /// pedal follows the heading error: in front, throttle; behind and slow enough, reverse.
        /// While reversing the steering is measured from the car's tail, which both keeps the sign
        /// the car needs and removes the full-lock spike a dead-astern target would otherwise get.
        /// Degenerate axes (a camera looking straight down, a car on its nose) keep the drive axis
        /// and drop the steering, so a broken frame cannot spin the car.
        /// </summary>
        public static void Direction(
            float stickX, float stickY,
            float cameraForwardX, float cameraForwardZ,
            float cameraRightX, float cameraRightZ,
            float carForwardX, float carForwardZ,
            float deadZone,
            out float throttle, out float steer)
        {
            throttle = 0f;
            steer = 0f;

            float x = Finite(stickX);
            float y = Finite(stickY);
            float response = ThrottleFromMagnitude(Magnitude(x, y), deadZone);
            if (response <= 0f)
                return;

            float desiredX = Finite(cameraForwardX) * y + Finite(cameraRightX) * x;
            float desiredZ = Finite(cameraForwardZ) * y + Finite(cameraRightZ) * x;
            float desiredSqr = desiredX * desiredX + desiredZ * desiredZ;
            float carForwardSqr = carForwardX * carForwardX + carForwardZ * carForwardZ;
            if (!(desiredSqr > MinAxisSqrMagnitude) || !(carForwardSqr > MinAxisSqrMagnitude))
            {
                throttle = response;
                return;
            }

            float inverseDesired = 1f / (float)Math.Sqrt(desiredSqr);
            float inverseForward = 1f / (float)Math.Sqrt(carForwardSqr);
            float forwardX = carForwardX * inverseForward;
            float forwardZ = carForwardZ * inverseForward;
            float directionX = desiredX * inverseDesired;
            float directionZ = desiredZ * inverseDesired;

            // Signed heading error around +Y: positive means the target is to the car's right, which
            // is the sign the steering axis already uses.
            float cross = forwardZ * directionX - forwardX * directionZ;
            float dot = forwardX * directionX + forwardZ * directionZ;
            float angle = (float)(Math.Atan2(cross, dot) * (180.0 / Math.PI));

            if (angle > -ReverseAngleDegrees && angle < ReverseAngleDegrees)
            {
                throttle = response;
                steer = Clamp(angle / FullSteerAngleDegrees, -1f, 1f);
                return;
            }

            float rearError = angle - (angle < 0f ? -180f : 180f);
            throttle = -response;
            steer = -Clamp(rearError / FullSteerAngleDegrees, -1f, 1f);
        }

        /// <summary>
        /// Heading Drive, the shipped reading: the stick asks for a world heading and the pedal is
        /// always forward. The two forward vectors come in the same shape <see cref="Direction"/>
        /// takes, so the runtime adapter can swap readings without converting anything, and the
        /// math itself lives in <see cref="HeadingDriveController"/>.
        ///
        /// A degenerate camera axis falls back to the car's own heading, which keeps the stick
        /// meaningful when the scene has no camera bound, and a degenerate car axis drops the whole
        /// sample: without a heading there is no honest error to steer by.
        /// </summary>
        public static void HeadingDrive(
            float stickX, float stickY,
            float cameraForwardX, float cameraForwardZ,
            float carForwardX, float carForwardZ,
            float carYawRateRadiansPerSecond,
            float forwardSpeed,
            float maxSteerAngleRadians,
            in HeadingDriveSettings settings,
            out HeadingDriveOutput output)
        {
            float carYaw = BearingOrInvalid(carForwardX, carForwardZ, out bool hasCarYaw);
            if (!hasCarYaw)
            {
                // No car heading means no target for the stick to describe, and inventing one
                // would spin the car; the released sample is the only safe answer.
                output = default;
                return;
            }

            float cameraYaw = BearingOrInvalid(cameraForwardX, cameraForwardZ, out bool hasCameraYaw);
            if (!hasCameraYaw)
                cameraYaw = carYaw;

            output = HeadingDriveController.Evaluate(
                new HeadingDriveInput(
                    stickX, stickY, cameraYaw, carYaw, carYawRateRadiansPerSecond, forwardSpeed,
                    maxSteerAngleRadians),
                settings);
        }

        /// <summary>The shipped tuning, for a caller with no tuning scene of its own.</summary>
        public static void HeadingDrive(
            float stickX, float stickY,
            float cameraForwardX, float cameraForwardZ,
            float carForwardX, float carForwardZ,
            float carYawRateRadiansPerSecond,
            float forwardSpeed,
            float maxSteerAngleRadians,
            out HeadingDriveOutput output)
        {
            HeadingDrive(stickX, stickY, cameraForwardX, cameraForwardZ, carForwardX, carForwardZ,
                carYawRateRadiansPerSecond, forwardSpeed, maxSteerAngleRadians,
                HeadingDriveSettings.Default, out output);
        }

        /// <summary>
        /// Pedal depth for a stick travel: nothing inside the dead zone, and then a gentle curve so
        /// the first millimetres of travel are not a full-power request. The curve is one multiply
        /// and one add, not a power function, so it stays exact across runtimes.
        /// </summary>
        public static float ThrottleFromMagnitude(float magnitude, float deadZone)
        {
            float dead = IsFinite(deadZone) ? Clamp(deadZone, 0f, 1f) : 0f;
            float length = IsFinite(magnitude) ? Clamp(magnitude, 0f, 1f) : 0f;
            if (length <= dead || dead >= 1f)
                return 0f;

            float normalized = (length - dead) / (1f - dead);
            return normalized * (0.5f + 0.5f * normalized);
        }

        /// <summary>
        /// Bearing of a forward axis in the same yaw convention the controller uses, or a false
        /// validity flag when the axis carries no direction. The threshold is the one Direction
        /// already applies to a usable bearing, so a broken frame reads the same way in both modes.
        /// </summary>
        private static float BearingOrInvalid(float forwardX, float forwardZ, out bool valid)
        {
            float x = Finite(forwardX);
            float z = Finite(forwardZ);
            valid = x * x + z * z > MinAxisSqrMagnitude;
            return valid ? (float)Math.Atan2(x, z) : 0f;
        }

        private static float Magnitude(float x, float y)
        {
            return (float)Math.Sqrt(x * x + y * y);
        }

        private static float Finite(float value)
        {
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum)
                return minimum;
            if (value > maximum)
                return maximum;
            return value;
        }
    }
}
