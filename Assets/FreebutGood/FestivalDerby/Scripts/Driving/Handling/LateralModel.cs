namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Per-axle lateral grip: the Rocket League ratio curve feeding a Marco Monster <c>mu * N</c> cap,
    /// applied through the axle's own wheel plane so that a steered axle produces the yaw moment that
    /// turns the car. The three things the legacy model lacked are all here: the force scales with the
    /// load on the axle (so braking makes the rear light and the car rotates on its own), the grip
    /// falls off as the slip ratio grows (so slides start and end gradually instead of snapping), and
    /// the force is applied at the axle rather than at the centre of mass.
    /// </summary>
    public static class LateralModel
    {
        /// <summary>
        /// Lateral force of one axle, in the car's body frame (+x forward, +y left).
        /// </summary>
        /// <param name="forwardSpeed">Body forward speed at the centre of mass, m/s.</param>
        /// <param name="lateralSpeed">Body lateral speed at the centre of mass, m/s (positive left).</param>
        /// <param name="yawRate">Body yaw rate, rad/s (positive is a left turn).</param>
        /// <param name="axleX">Axle position along the body forward axis, m (positive ahead of the COM).</param>
        /// <param name="steerAngle">Wheel angle of this axle, rad (positive is left; 0 for the rear).</param>
        /// <param name="axleLoadNewton">Suspension load of this axle this tick, N.</param>
        /// <param name="gripScale">Surface and zone multipliers, and the sideslip guard's rear boost.</param>
        public static void Evaluate(
            float forwardSpeed,
            float lateralSpeed,
            float yawRate,
            float axleX,
            float steerAngle,
            float axleLoadNewton,
            float gripScale,
            float deltaTime,
            in Piecewise4 axleCurve,
            in HandlingParams p,
            out float forceX,
            out float forceY,
            out float slipRatio,
            out float mu)
        {
            float sinSteer = (float)System.Math.Sin(steerAngle);
            float cosSteer = (float)System.Math.Cos(steerAngle);

            // Velocity of the axle point, in the body frame, then projected onto the wheel's own
            // lateral axis. This is what makes a steered axle generate force at all: with the wheel
            // turned, the wheel's lateral axis is no longer the body's.
            float pointForward = forwardSpeed;
            float pointLateral = lateralSpeed + yawRate * axleX;
            float wheelLateralSpeed = -pointForward * sinSteer + pointLateral * cosSteer;
            float wheelForwardSpeed = pointForward * cosSteer + pointLateral * sinSteer;

            float absoluteLateral = HandlingMath.Abs(wheelLateralSpeed);
            if (absoluteLateral > p.SlipRatioFloorMetersPerSecond)
            {
                float denominator = absoluteLateral + HandlingMath.Abs(wheelForwardSpeed);
                slipRatio = denominator > 0f ? absoluteLateral / denominator : 0f;
            }
            else
            {
                // Below the floor the ratio is noise; treating a stopped wheel as fully rolling keeps
                // the grip curve from biting at parking speed.
                slipRatio = 0f;
            }

            mu = HandlingMath.Clamp01(axleCurve.Evaluate(slipRatio)) * gripScale;
            float cap = p.MuPeak * mu * axleLoadNewton;

            // Cancel most of the axle's side slip this tick. The 0.9 keeps a little slip alive so the
            // ratio (and therefore the curve) can actually see the slide it is supposed to react to.
            float axleMass = axleLoadNewton / HandlingParams.StandardGravity;
            float requested = -wheelLateralSpeed * axleMass * p.SlipCancelFraction / deltaTime;

            float lateral = HandlingMath.Clamp(requested, -cap, cap);

            forceX = -lateral * sinSteer;
            forceY = lateral * cosSteer;
        }

        /// <summary>Convenience overload for a non-steered axle (the rear).</summary>
        public static void Evaluate(
            float forwardSpeed,
            float lateralSpeed,
            float yawRate,
            float axleX,
            float axleLoadNewton,
            float gripScale,
            float deltaTime,
            in Piecewise4 axleCurve,
            in HandlingParams p,
            out float forceX,
            out float forceY,
            out float slipRatio,
            out float mu)
        {
            Evaluate(
                forwardSpeed,
                lateralSpeed,
                yawRate,
                axleX,
                0f,
                axleLoadNewton,
                gripScale,
                deltaTime,
                axleCurve,
                p,
                out forceX,
                out forceY,
                out slipRatio,
                out mu);
        }
    }
}
