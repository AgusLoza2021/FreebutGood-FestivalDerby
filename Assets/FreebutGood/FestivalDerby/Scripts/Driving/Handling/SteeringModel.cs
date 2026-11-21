namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Speed-sensitive steering with a rate limit, and the yaw-rate demand behind it.
    ///
    /// The heading error becomes a yaw-rate target clamped to the grip budget, the bicycle model
    /// converts that target into a feed-forward wheel angle, and a yaw-rate error term adds damping.
    /// The angle then moves towards its command at a fixed rate instead of snapping, which is
    /// Criterion's "steer rates" and the reason the legacy model twitched at speed.
    ///
    /// Because the target is a heading and not a wheel angle, the same controller countersteers on its
    /// own: when the tail swings past the requested heading the error changes sign and the wheel angle
    /// follows it.
    /// </summary>
    public static class SteeringModel
    {
        public static float Evaluate(
            float headingErrorRadians,
            float forwardSpeed,
            float yawRateRadiansPerSecond,
            float steerAngleRadians,
            float deltaTime,
            in HandlingParams p,
            out float yawTargetRadiansPerSecond,
            out float steerMaxRadians,
            out float steerCommandRadians)
        {
            float referenceSpeed = HandlingMath.Max(forwardSpeed, p.MinReferenceSpeedMetersPerSecond);

            // At 20 m/s the Balanced budget allows 16/20 = 0.8 rad/s (about 46 deg/s); the 150 deg/s
            // ceiling only matters when the car is nearly stopped.
            float yawMax = HandlingMath.Min(p.MaxYawRateRadiansPerSecond, p.LateralAccelBudgetMetersPerSecondSquared / referenceSpeed);
            yawTargetRadiansPerSecond = HandlingMath.Clamp(p.HeadingGain * headingErrorRadians, -yawMax, yawMax);

            float feedForward = (float)System.Math.Atan(yawTargetRadiansPerSecond * p.WheelbaseMeters / referenceSpeed);
            steerMaxRadians = SteerMaxFor(forwardSpeed, p);
            steerCommandRadians = HandlingMath.Clamp(
                feedForward + p.YawRateDamping * (yawTargetRadiansPerSecond - yawRateRadiansPerSecond),
                -steerMaxRadians,
                steerMaxRadians);

            return HandlingMath.MoveTowards(steerAngleRadians, steerCommandRadians, p.SteerRateRadiansPerSecond * deltaTime);
        }

        /// <summary>
        /// Lock available at this speed, from the same lateral budget that limits the yaw rate: the
        /// angle a car can hold without asking the tyres for more grip than they have. 32 degrees at a
        /// crawl, 5 at the top end.
        /// </summary>
        public static float SteerMaxFor(float forwardSpeed, in HandlingParams p)
        {
            float speed = HandlingMath.Max(forwardSpeed, p.MinSteerSpeedMetersPerSecond);
            float raw = (float)System.Math.Atan(p.WheelbaseMeters * p.LateralAccelBudgetMetersPerSecondSquared / (speed * speed));
            return HandlingMath.Clamp(raw, p.SteerMinRadians, p.SteerMaxRadians);
        }

        /// <summary>Steering command for a caller that already has a steer axis (bots, car-relative mode).</summary>
        public static float Direct(float steerAxis, float forwardSpeed, in HandlingParams p, out float steerMaxRadians)
        {
            steerMaxRadians = SteerMaxFor(forwardSpeed, p);
            return HandlingMath.Clamp(steerAxis, -1f, 1f) * steerMaxRadians;
        }
    }
}
