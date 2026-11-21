namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// The pedal side of the model: a torque curve over speed instead of a constant force, plus the
    /// coast, brake and reverse cases. Rocket League and Very Very Valet both drop drive at the ends
    /// of the speed range and keep it in the middle, which is what removes the "too much straight-line
    /// punch" the owner complained about: the old model was at its most violent at zero speed.
    ///
    /// Returns a force along the car's forward axis in newtons. Negative means the car is being slowed
    /// or pushed backwards (brake and reverse are buttons and live outside the intent mapping).
    /// </summary>
    public static class LongitudinalModel
    {
        /// <summary>
        /// <paramref name="driveScale"/> 1 overload, kept for callers that only need the base model
        /// (the unit tests and any non-FEEL2 tick). See the overload below for the catch-up scale.
        /// </summary>
        public static float Evaluate(
            float throttle,
            float forwardSpeed,
            float deltaTime,
            bool brake,
            bool reverse,
            float nitroScale,
            float zoneTopSpeedScale,
            int groundedWheels,
            float massKg,
            in HandlingParams p,
            ref float smoothedThrottle,
            out float curveFactor,
            out float resistanceForceNewton)
        {
            return Evaluate(
                throttle,
                forwardSpeed,
                deltaTime,
                brake,
                reverse,
                nitroScale,
                zoneTopSpeedScale,
                groundedWheels,
                massKg,
                in p,
                ref smoothedThrottle,
                1f,
                out curveFactor,
                out resistanceForceNewton);
        }

        /// <summary>
        /// The pedal side of the model. <paramref name="driveScale"/> is the FEEL2 catch-up factor
        /// (1 = no catch-up) and multiplies only the forward drive: a boost must never make the
        /// engine brake or drag push the car forwards.
        /// </summary>
        public static float Evaluate(
            float throttle,
            float forwardSpeed,
            float deltaTime,
            bool brake,
            bool reverse,
            float nitroScale,
            float zoneTopSpeedScale,
            int groundedWheels,
            float massKg,
            in HandlingParams p,
            ref float smoothedThrottle,
            float driveScale,
            out float curveFactor,
            out float resistanceForceNewton)
        {
            if (brake)
            {
                // A brake is a deceleration request, never a reverse: the stop term keeps it from
                // dragging the car back through zero and oscillating around it.
                curveFactor = 0f;
                smoothedThrottle = HandlingMath.MoveTowards(smoothedThrottle, 0f, p.ThrottleFallRate * deltaTime);
                float stopDecel = HandlingMath.Abs(forwardSpeed) / deltaTime * 0.99f;
                float decel = HandlingMath.Min(p.BrakeDecelMetersPerSecondSquared, stopDecel);
                resistanceForceNewton = -HandlingMath.Sign(forwardSpeed) * massKg * decel;
                return resistanceForceNewton;
            }

            if (reverse)
            {
                curveFactor = 0f;
                smoothedThrottle = HandlingMath.MoveTowards(smoothedThrottle, 0f, p.ThrottleFallRate * deltaTime);
                resistanceForceNewton = forwardSpeed <= -p.ReverseTopSpeedMetersPerSecond
                    ? 0f
                    : -massKg * p.ReverseAccelMetersPerSecondSquared;
                return resistanceForceNewton;
            }

            float speedFraction = forwardSpeed > 0f
                ? HandlingMath.Clamp01(forwardSpeed / p.TopSpeedMetersPerSecond)
                : 0f;
            curveFactor = p.ThrottleCurve.Evaluate(speedFraction);

            float rate = throttle > smoothedThrottle ? p.ThrottleRiseRate : p.ThrottleFallRate;
            smoothedThrottle = HandlingMath.MoveTowards(smoothedThrottle, throttle, rate * deltaTime);

            // Rocket League divides drive by four when fewer than three wheels touch; it keeps a
            // two-wheel-on-the-ground car from out-accelerating a planted one.
            float contactScale = groundedWheels < 3 ? 0.25f : 1f;
            float drive = massKg
                * p.AccelMaxMetersPerSecondSquared
                * smoothedThrottle
                * curveFactor
                * nitroScale
                * zoneTopSpeedScale
                * contactScale
                * driveScale;

            // Coasting: engine braking belongs to a lifted pedal — it fades out across
            // EngineBrakeFadePedal as the driver gets back on the power, so a held throttle never
            // pays for it — and drag grows with speed. Both oppose motion and neither may reverse
            // the car inside one tick.
            float engineBrakeFade = 1f - HandlingMath.Smoothstep(
                0f,
                p.EngineBrakeFadePedal,
                smoothedThrottle);
            float coastDecel = (p.EngineBrakeMetersPerSecondSquared * engineBrakeFade)
                + (p.CoastDragPerSecond * HandlingMath.Abs(forwardSpeed));
            float coastLimit = HandlingMath.Abs(forwardSpeed) / deltaTime * 0.99f;
            float coast = HandlingMath.Min(coastDecel, coastLimit);
            resistanceForceNewton = -HandlingMath.Sign(forwardSpeed) * massKg * coast;

            return drive + resistanceForceNewton;
        }
    }
}
