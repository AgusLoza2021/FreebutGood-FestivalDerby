namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// The assist that makes the car follow the requested yaw rate. It is scaled by the yaw inertia
    /// (so the same preset feels the same on a light and a heavy car), gated by speed (no assist while
    /// crawling, full from 4 m/s) and faded out by rear slip: once the rear tyres are sliding, the
    /// player is driving and the assist must not fight the slide. Criterion's rule is that assists are
    /// never binary, and this fade is that rule expressed in one multiply.
    ///
    /// Yaw angular damping is applied here as a separate, much lower term than the legacy 2.5/s: the
    /// gain of the heading loop is what should stop the rotation, not a global handbrake.
    /// </summary>
    public static class YawStabilizer
    {
        public static float Evaluate(
            float forwardSpeed,
            float yawRateRadiansPerSecond,
            float yawTargetRadiansPerSecond,
            float rearSlipRatio,
            float yawInertiaKilogramMetersSquared,
            in HandlingParams p,
            out float fade,
            out float assistTorque,
            out float dampingTorque)
        {
            float speedFade = HandlingMath.Smoothstep(p.AssistEngageSpeedLow, p.AssistEngageSpeedHigh, forwardSpeed);
            float slideFade = 1f - HandlingMath.Smoothstep(p.AssistFadeSlipLow, p.AssistFadeSlipHigh, rearSlipRatio);
            fade = speedFade * slideFade;

            assistTorque = yawInertiaKilogramMetersSquared
                * p.AssistGain
                * fade
                * (yawTargetRadiansPerSecond - yawRateRadiansPerSecond);

            dampingTorque = -yawInertiaKilogramMetersSquared * p.YawDampingRate * yawRateRadiansPerSecond;

            return assistTorque + dampingTorque;
        }
    }
}
