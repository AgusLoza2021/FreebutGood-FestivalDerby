
namespace FestivalDerby.Bots
{
    /// <summary>
    /// What one full unit of a bot's steer axis means to the car it drives.
    ///
    /// The axis is a normalised command, but the two handling models normalise it differently. The
    /// legacy motor reads it as a share of the wheel angle and saturates the turn at a fixed 32
    /// degrees of heading error, so a constant scale is right. Driving Model V2's CarRelative adapter
    /// reads it as a share of the yaw rate the tires can still make at this speed, and that shrinks as
    /// 1/v: at 25 m/s a full axis is 9 degrees of heading error, not 32.
    ///
    /// A bot that keeps the legacy scale therefore asks for under a third of its authority exactly
    /// where it needs all of it (a corner at speed), understeers wide and never turns back. Turning
    /// the scale into a property of the car is the fix; the math stays engine-free so the harness can
    /// prove it without a Rigidbody.
    /// </summary>
    public struct BotSteeringLimits
    {
        private const float DegreesPerRadian = 57.29578f;
        private const float LegacyFullSteerDegrees = 32f;

        /// <summary>Lateral acceleration the tires can make, m/s^2. Zero means "not a V2 car".</summary>
        public float LateralAccelBudget;

        /// <summary>Heading gain, 1/s: the yaw rate a bot asks for per radian of aim error.</summary>
        public float HeadingGain;

        /// <summary>Absolute yaw-rate cap, rad/s. Zero or less means "no cap".</summary>
        public float MaxYawRate;

        /// <summary>Speed floor the law uses instead of a near-zero speed, m/s.</summary>
        public float MinReferenceSpeed;

        /// <summary>Full-axis heading error for a car with no V2 numbers.</summary>
        public float FixedFullSteerDegrees;

        /// <summary>The fixed wheel-angle scale the legacy motor reads.</summary>
        public static BotSteeringLimits Legacy
        {
            get { return new BotSteeringLimits { FixedFullSteerDegrees = LegacyFullSteerDegrees }; }
        }

        /// <summary>
        /// The V2 scale, from the same four numbers the CarRelative adapter uses. They are passed in
        /// rather than looked up so the engine-free core never has to know a handling asset exists.
        /// </summary>
        public static BotSteeringLimits V2(float lateralAccelBudget, float headingGain, float maxYawRate,
            float minReferenceSpeed)
        {
            return new BotSteeringLimits
            {
                LateralAccelBudget = lateralAccelBudget,
                HeadingGain = headingGain,
                MaxYawRate = maxYawRate,
                MinReferenceSpeed = minReferenceSpeed,
            };
        }

        public bool IsV2
        {
            get { return LateralAccelBudget > 0f && HeadingGain > 0f; }
        }

        /// <summary>
        /// The heading error, in degrees, that a full steer axis stands for at <paramref name="speed"/>.
        /// <see cref="PathFollower.SteerTowards"/> divides by it, so the axis ends up as the share of
        /// the yaw rate the car can still make: <c>omega = axis * yawMax(speed)</c>.
        /// </summary>
        public float FullSteerDegrees(float speed)
        {
            if (!IsV2)
                return FixedFullSteerDegrees > 0f ? FixedFullSteerDegrees : LegacyFullSteerDegrees;

            float absolute = BotMath.Abs(speed);
            float reference = absolute > MinReferenceSpeed ? absolute : MinReferenceSpeed;
            float yawMax = LateralAccelBudget / reference;
            if (MaxYawRate > 0f && MaxYawRate < yawMax)
                yawMax = MaxYawRate;
            return yawMax / HeadingGain * DegreesPerRadian;
        }
    }
}
