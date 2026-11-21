namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// What the car does with no wheels on the ground. The planar sim never needs it (a top-down 2D
    /// body has no air), but the Unity adapter does, and keeping it in the engine-free core means the
    /// air behaviour is unit-tested without an Editor.
    ///
    /// Descent pulls harder than a static fall so a jump reads as weight; yaw is driven by the same
    /// stick that steers on the ground, with input-scaled damping (Rocket League's rule: a held stick
    /// spins freely, a released stick stops); the roll axis auto-levels so a landing is not a coin
    /// toss. Auto-level is deliberately roll-only, because pitch and yaw corrections were "really
    /// unpredictable" in Rocket League's experience.
    /// </summary>
    public static class AirModel
    {
        public static void Evaluate(
            bool airborne,
            bool descending,
            float yawInput,
            float rollAngleRadians,
            float yawRateRadiansPerSecond,
            float rollRateRadiansPerSecond,
            float yawInertiaKilogramMetersSquared,
            float rollInertiaKilogramMetersSquared,
            in HandlingParams p,
            out float gravityScale,
            out float yawTorque,
            out float rollTorque)
        {
            if (!airborne)
            {
                gravityScale = 1f;
                yawTorque = 0f;
                rollTorque = 0f;
                return;
            }

            gravityScale = descending ? p.AirGravityScale : 1f;

            float yawAccel = p.AirYawAccelRadiansPerSecondSquared * HandlingMath.Clamp(yawInput, -1f, 1f);
            float yawDamping = p.AirYawDamping * (1f - HandlingMath.Abs(HandlingMath.Clamp(yawInput, -1f, 1f)));
            yawTorque = yawInertiaKilogramMetersSquared * (yawAccel - yawDamping * yawRateRadiansPerSecond);

            rollTorque = rollInertiaKilogramMetersSquared
                * (-p.AirLevelGain * rollAngleRadians - p.AirYawDamping * rollRateRadiansPerSecond);
        }
    }
}
