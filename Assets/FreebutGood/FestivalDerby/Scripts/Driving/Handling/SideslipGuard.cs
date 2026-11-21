namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// The "never spin" guarantee. The slip angle of the whole car (velocity against the nose) is not
    /// allowed to grow past the preset's beta max while the car is grounded and moving: past it, a yaw
    /// torque pulls the nose towards the direction the car is actually travelling, and the rear tyres
    /// get a grip boost so the correction has something to push against.
    ///
    /// Both the entry threshold and the boost ramp are smooth, so the guard cannot be felt as a
    /// switch. The rear boost is what keeps the correction from simply lifting the rear off the road.
    /// </summary>
    public static class SideslipGuard
    {
        public static float Evaluate(
            float forwardSpeed,
            float lateralSpeed,
            float yawInertiaKilogramMetersSquared,
            int groundedWheels,
            in HandlingParams p,
            out float betaRadians,
            out float excessRadians,
            out float rearMuBoost)
        {
            return Evaluate(
                forwardSpeed,
                lateralSpeed,
                yawInertiaKilogramMetersSquared,
                groundedWheels,
                p.BetaMaxRadians,
                p,
                out betaRadians,
                out excessRadians,
                out rearMuBoost);
        }

        /// <summary>
        /// The guard against an explicit cap. FP0 brake-to-rotate raises the cap for the tick while
        /// the brake is held, so the rotation it asks for is not immediately clawed back; every other
        /// caller goes through the <see cref="HandlingParams.BetaMaxRadians"/> overload above.
        /// </summary>
        public static float Evaluate(
            float forwardSpeed,
            float lateralSpeed,
            float yawInertiaKilogramMetersSquared,
            int groundedWheels,
            float betaMaxRadians,
            in HandlingParams p,
            out float betaRadians,
            out float excessRadians,
            out float rearMuBoost)
        {
            // Positive beta means the car is travelling to its own left of the nose, so the nose has
            // to rotate left (positive yaw) to line up with the velocity vector.
            betaRadians = (float)System.Math.Atan2(lateralSpeed, HandlingMath.Abs(forwardSpeed));

            excessRadians = HandlingMath.Abs(betaRadians) - betaMaxRadians;
            if (excessRadians < 0f)
            {
                excessRadians = 0f;
            }

            bool active = groundedWheels > 0
                && forwardSpeed > p.GuardMinSpeedMetersPerSecond
                && excessRadians > 0f;

            if (!active)
            {
                rearMuBoost = 1f;
                return 0f;
            }

            float torque = HandlingMath.Sign(betaRadians)
                * yawInertiaKilogramMetersSquared
                * p.GuardYawGain
                * excessRadians;

            rearMuBoost = 1f + p.GuardRearMuBoost
                * HandlingMath.Smoothstep(0f, p.GuardRearMuBoostRangeRadians, excessRadians);

            return torque;
        }
    }
}
