namespace FestivalDerby.Driving
{
    /// <summary>
    /// Control intention only; Forward, Brake and Reverse carry a fraction of their own force and
    /// Coast is the release that lets the car roll.
    /// </summary>
    public enum CarControlMode
    {
        Forward,
        Brake,
        Reverse,
        Coast
    }

    public readonly struct CarControlDecision
    {
        public readonly float Steer;
        public readonly CarControlMode Mode;

        /// <summary>
        /// How much of the mode's own force to apply, in [0,1]: the drive fraction, the brake
        /// fraction or the reverse fraction. Coast carries 0 and the buttons carry 1, because a
        /// held brake button is a full stop request and not a partial one.
        /// </summary>
        public readonly float DriveFraction;

        public readonly bool NitroPulse;

        public CarControlDecision(float steer, CarControlMode mode, float driveFraction, bool nitroPulse)
        {
            Steer = steer;
            Mode = mode;
            DriveFraction = driveFraction;
            NitroPulse = nitroPulse;
        }
    }

    public static class CarControlRules
    {
        /// <summary>
        /// signedForwardSpeed and reverseEngageSpeed use m/s; negative speed means backwards.
        /// steeringDeadZone is a configured normalized magnitude in [0,1]; its boundary is neutral.
        /// The throttle shares that same normalized dead zone, so a resting stick cannot creep the
        /// car and one number describes "no input" on both axes. Steering outside the zone is
        /// retained clamped, without rescaling, and the drive axis keeps its own magnitude as the
        /// force fraction. No tuning is selected here.
        /// Forward needs a positive throttle: the car no longer accelerates on its own.
        /// A negative throttle is the same gesture as a pedal: it brakes while the car is still too
        /// fast to reverse and backs up once the reverse gate opens. Brake and reverse override it.
        /// Non-finite speed requires fail-safe braking. Nitro is passed through, without budget or
        /// edge-generation logic.
        /// </summary>
        public static CarControlDecision Resolve(
            CarInput input, float signedForwardSpeed, float reverseEngageSpeed, float steeringDeadZone)
        {
            var steer = IsFinite(input.steer) ? Clamp(input.steer, -1f, 1f) : 0f;
            var deadZone = IsFinite(steeringDeadZone) ? Clamp(steeringDeadZone, 0f, 1f) : 0f;
            if (steer >= -deadZone && steer <= deadZone)
                steer = 0f;

            var throttle = IsFinite(input.throttle) ? Clamp(input.throttle, -1f, 1f) : 0f;
            if (throttle >= -deadZone && throttle <= deadZone)
                throttle = 0f;

            CarControlMode mode;
            float driveFraction;
            // Unknown speed cannot safely authorize acceleration or reversing.
            if (input.brake || !IsFinite(signedForwardSpeed))
            {
                mode = CarControlMode.Brake;
                driveFraction = 1f;
            }
            else if (input.reverse)
            {
                mode = signedForwardSpeed == 0f || signedForwardSpeed < ReverseThreshold(reverseEngageSpeed)
                    ? CarControlMode.Reverse
                    : CarControlMode.Brake;
                driveFraction = 1f;
            }
            else if (throttle > 0f)
            {
                mode = CarControlMode.Forward;
                driveFraction = throttle;
            }
            else if (throttle < 0f)
            {
                // One gesture, two meanings, and the gate decides which one is current: a car still
                // rolling forward past the reverse threshold brakes, and only once it is slow enough
                // does the same held-back stick start backing it up.
                mode = signedForwardSpeed == 0f || signedForwardSpeed < ReverseThreshold(reverseEngageSpeed)
                    ? CarControlMode.Reverse
                    : CarControlMode.Brake;
                driveFraction = -throttle;
            }
            else
            {
                mode = CarControlMode.Coast;
                driveFraction = 0f;
            }

            return new CarControlDecision(steer, mode, driveFraction, input.nitroPressed);
        }

        private static float ReverseThreshold(float reverseEngageSpeed)
        {
            return IsFinite(reverseEngageSpeed) && reverseEngageSpeed > 0f ? reverseEngageSpeed : 0f;
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
