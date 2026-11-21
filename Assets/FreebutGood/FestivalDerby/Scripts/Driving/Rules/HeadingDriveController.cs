using System;

namespace FestivalDerby.Driving
{
    /// <summary>
    /// One stick sample and the car state it is read against, in the terms the heading loop needs.
    /// Angles are world yaw in radians, where a positive yaw is a turn to the car's right and
    /// forward is (sin yaw, cos yaw). ForwardSpeed is the signed forward speed in m/s and
    /// MaxSteerAngleRadians is the car's own lock at this speed, which the caller already knows.
    /// </summary>
    public readonly struct HeadingDriveInput
    {
        /// <summary>Stick travel across the screen, -1 left to 1 right.</summary>
        public readonly float StickX;

        /// <summary>Stick travel up the screen, -1 toward the camera to 1 away from it.</summary>
        public readonly float StickY;

        /// <summary>World yaw the camera reads as "stick straight up".</summary>
        public readonly float CameraYawRadians;

        public readonly float CarYawRadians;
        public readonly float CarYawRateRadiansPerSecond;
        public readonly float ForwardSpeed;
        public readonly float MaxSteerAngleRadians;

        public HeadingDriveInput(
            float stickX,
            float stickY,
            float cameraYawRadians,
            float carYawRadians,
            float carYawRateRadiansPerSecond,
            float forwardSpeed,
            float maxSteerAngleRadians)
        {
            StickX = stickX;
            StickY = stickY;
            CameraYawRadians = cameraYawRadians;
            CarYawRadians = carYawRadians;
            CarYawRateRadiansPerSecond = carYawRateRadiansPerSecond;
            ForwardSpeed = forwardSpeed;
            MaxSteerAngleRadians = maxSteerAngleRadians;
        }
    }

    /// <summary>
    /// The two axes the motor already eats plus the one flag the handling pass adds. Throttle is
    /// never negative in this mode: the gear is forward and the brake is a button.
    /// </summary>
    public readonly struct HeadingDriveOutput
    {
        public readonly float Steer;
        public readonly float Throttle;

        /// <summary>Set while the player is asking for more than the tyres can hold: slide, do not stop.</summary>
        public readonly bool DriftAssist;

        public HeadingDriveOutput(float steer, float throttle, bool driftAssist)
        {
            Steer = steer;
            Throttle = throttle;
            DriftAssist = driftAssist;
        }
    }

    /// <summary>
    /// Heading Drive: the stick asks where to go and the pedal is always forward. The car turns
    /// toward the requested world heading with a grip-limited yaw rate, so the finger can follow
    /// the road on the map instead of pedalling, and no stick angle ever turns into a brake. The
    /// old readings (a screen heading that reverses, and the car's own pedals) stay in
    /// <see cref="JoystickDriveMapping"/> beside it.
    ///
    /// Pure float math with no Unity types, because the client and the server are meant to run the
    /// same function, and no allocation: the output is a struct and nothing here boxes.
    /// </summary>
    public static class HeadingDriveController
    {
        /// <summary>Smallest lock the controller will use as a scale; anything less is not steering.</summary>
        private const float MinimumSteerLimitRadians = 0.05f;

        /// <summary>Largest lock any car can honestly have; also the guard against a bad definition.</summary>
        private const float MaximumSteerLimitRadians = 1.5707964f;

        /// <summary>The dead zone may never swallow the ring; past this the stick would never drive.</summary>
        private const float MaximumDeadZone = 0.9f;

        public static HeadingDriveOutput Evaluate(in HeadingDriveInput input, in HeadingDriveSettings settings)
        {
            float stickX = Finite(input.StickX);
            float stickY = Finite(input.StickY);
            float travel = (float)Math.Sqrt(stickX * stickX + stickY * stickY);
            float deadZone = Clamp(Finite(settings.DeadZone), 0f, MaximumDeadZone);
            if (!(travel > deadZone))
            {
                // Inside the rest ring the car coasts on purpose: the mode never accelerates a car
                // the player is not pointing anywhere, which is the D20 rule the pedals already had.
                return default;
            }

            // The only trigonometry the target needs. The stick bearing is measured from screen up,
            // so the world heading is the camera's bearing plus that angle: the camera axes never
            // have to be rotated here, and a yawing camera moves the target with it, which is what
            // makes "my finger follows the road" work on a camera-relative screen.
            float targetYaw = Finite(input.CameraYawRadians) + (float)Math.Atan2(stickX, stickY);
            float error = WrapPi(targetYaw - Finite(input.CarYawRadians));

            float speed = Finite(input.ForwardSpeed);
            if (speed < 0f)
                speed = -speed;
            float reference = speed > SafePositive(settings.MinimumReferenceSpeed, HeadingDriveSettings.DefaultMinimumReferenceSpeed)
                ? speed
                : SafePositive(settings.MinimumReferenceSpeed, HeadingDriveSettings.DefaultMinimumReferenceSpeed);
            float maxYawRate = SafePositive(settings.MaxLateralAcceleration, HeadingDriveSettings.DefaultMaxLateralAcceleration)
                / reference;

            float desired = Clamp(
                SafeNonNegative(settings.HeadingGain, HeadingDriveSettings.DefaultHeadingGain) * error,
                -maxYawRate, maxYawRate);

            // Feed-forward: the lock that would produce the desired yaw rate in the bicycle model,
            // so the car turns as soon as the stick moves instead of waiting for an error to build.
            // The reference speed floors the divisor, which is what keeps a stopped car from asking
            // for an impossible rate.
            float steerLimit = SanitizeSteerLimit(input.MaxSteerAngleRadians, settings);
            float wheelbase = SafePositive(settings.Wheelbase, HeadingDriveSettings.DefaultWheelbase);
            float feedForward = (float)Math.Atan(desired * wheelbase / reference) / steerLimit;

            // Damping: the measured yaw rate against the requested one, in units of the grip-limited
            // rate, so a car that is already turning needs less lock and a car that is not gets bite.
            float damping = SafeNonNegative(settings.YawRateDamping, HeadingDriveSettings.DefaultYawRateDamping)
                * (desired - Finite(input.CarYawRateRadiansPerSecond)) / maxYawRate;
            float steer = Clamp(feedForward + damping, -1f, 1f);

            // Pedal: the ring travel alone. Nothing here reads the heading error for the pedal, so
            // the gear stays forward whatever the stick says; the error can only ever trim it.
            float ring = (travel - deadZone) / (1f - deadZone);
            float shape = UnitPower(Clamp(ring, 0f, 1f),
                Clamp(Finite(settings.ThrottleResponseExponent), 1f, 2f));
            float floorThrottle = Clamp(Finite(settings.ThrottleFloor), 0f, 1f);
            float ringThrottle = Clamp(Finite(settings.ThrottleAtRing), floorThrottle, 1f);
            float throttle = floorThrottle + (ringThrottle - floorThrottle) * shape;

            float threshold = SafePositive(
                settings.LargeAngleThresholdRadians, HeadingDriveSettings.DefaultLargeAngleThresholdRadians);
            bool driftAssist = error > threshold || error < -threshold;
            if (driftAssist)
            {
                // Dead astern: keep the power and set the slide flag. Lifting the pedal is the only
                // slow-down this mode is allowed, because a brake here is the bug being fixed.
                throttle *= Clamp(Finite(settings.LargeAngleThrottleScale), 0f, 1f);
            }

            return new HeadingDriveOutput(steer, Clamp(throttle, 0f, 1f), driftAssist);
        }

        /// <summary>
        /// Wraps a heading error into (-pi, pi]. Floor is used instead of a loop so the mapping has
        /// no branch a runtime could reorder, and the result is exact for an angle already in range.
        /// </summary>
        private static float WrapPi(float angle)
        {
            const float TwoPi = 6.2831855f;
            const float Pi = 3.1415927f;
            float wrapped = Finite(angle);
            if (wrapped >= -Pi && wrapped <= Pi)
                return wrapped;

            return wrapped - TwoPi * (float)Math.Floor((wrapped + Pi) / TwoPi);
        }

        /// <summary>
        /// A power on the unit interval with a square-root expansion instead of Math.Pow: the
        /// platform pow is not bit-identical across runtimes and this mapping is meant to run on the
        /// client and on the server, while multiplication and square root are correctly rounded
        /// everywhere. Twelve bits leave the exponent within 0.0002 of the requested one, far under
        /// anything a thumb can feel. exponent must be in [1,2]; the caller clamps it.
        /// </summary>
        private static float UnitPower(float value, float exponent)
        {
            if (!(value > 0f))
                return 0f;
            if (value >= 1f || exponent <= 1f)
                return value;

            float result = value;
            float remainder = exponent - 1f;
            float root = value;
            float bit = 0.5f;
            for (int i = 0; i < 12 && remainder > 0f; i++)
            {
                root = (float)Math.Sqrt(root);
                if (remainder >= bit)
                {
                    result *= root;
                    remainder -= bit;
                }

                bit *= 0.5f;
            }

            return result;
        }

        /// <summary>
        /// The car's own lock when the caller has a usable one, clamped to a range that can be
        /// divided by, and the configured fallback when a broken definition would otherwise turn the
        /// steer axis into a spike or a division by zero.
        /// </summary>
        private static float SanitizeSteerLimit(float maxSteerAngleRadians, in HeadingDriveSettings settings)
        {
            float limit = Finite(maxSteerAngleRadians);
            if (!(limit > MinimumSteerLimitRadians))
            {
                limit = SafePositive(
                    settings.FallbackSteerLimitRadians, HeadingDriveSettings.DefaultSteerLimitRadians);
            }

            return Clamp(limit, MinimumSteerLimitRadians, MaximumSteerLimitRadians);
        }

        private static float SafePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static float SafeNonNegative(float value, float fallback)
        {
            return IsFinite(value) && value >= 0f ? value : fallback;
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
