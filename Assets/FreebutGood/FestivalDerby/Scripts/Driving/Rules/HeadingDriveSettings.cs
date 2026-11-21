namespace FestivalDerby.Driving
{
    /// <summary>
    /// Tuning for <see cref="HeadingDriveController"/>, with the shipped defaults in one place so a
    /// tuning scene only has to hand over a different copy of the same struct. It lives in the
    /// engine-free rules assembly on purpose: the client and the server are meant to run the same
    /// mapping and neither may reference a ScriptableObject to do it.
    ///
    /// Angles are radians here because the controller works on wrapped heading errors; the degrees
    /// everyone reasons about are only a comment away. A struct, not a class: no allocation on the
    /// input path, and every field is a value the caller can override without a rebuild.
    /// </summary>
    public readonly struct HeadingDriveSettings
    {
        /// <summary>
        /// Rest ring. Bigger than the pedals' own dead zone because here the ring is also the
        /// pedal: a thumb that is not asking for anything must not keep the car at cruise.
        /// </summary>
        public const float DefaultDeadZone = 0.12f;

        /// <summary>Cruise the ring starts at, so a steering request is not an engine request.</summary>
        public const float DefaultThrottleFloor = 0.35f;

        /// <summary>Pedal at the ring. Never above 1, and the controller clamps it anyway.</summary>
        public const float DefaultThrottleAtRing = 1f;

        /// <summary>Shape of the pedal across the ring: gentle at the edge, steep at the ring.</summary>
        public const float DefaultThrottleResponseExponent = 1.4f;

        /// <summary>Heading gain in 1/s: about a 0.25 s time constant for the heading loop.</summary>
        public const float DefaultHeadingGain = 4f;

        /// <summary>
        /// Damping on the measured yaw rate, in units of the grip-limited rate: 0.35 means a full
        /// rate error asks for 35% of lock on top of the feed-forward.
        /// </summary>
        public const float DefaultYawRateDamping = 0.35f;

        /// <summary>Latency budget of the grip: 14 m/s^2 is a hard, planted derby car.</summary>
        public const float DefaultMaxLateralAcceleration = 14f;

        /// <summary>
        /// The lowest speed the grip limit and the bicycle model may divide by. Below it the car is
        /// pivoting rather than cornering, and dividing by a smaller speed would ask for a yaw rate
        /// no tyre could hold.
        /// </summary>
        public const float DefaultMinimumReferenceSpeed = 3f;

        /// <summary>Wheelbase of the reference car, for the feed-forward bicycle model.</summary>
        public const float DefaultWheelbase = 2.4f;

        /// <summary>
        /// Lock assumed when the caller has no usable maximum steer angle, about 28 degrees. The
        /// controller still needs a scale to turn radians of steer into the -1..1 axis.
        /// </summary>
        public const float DefaultSteerLimitRadians = 0.5f;

        /// <summary>Dead astern starts here: the ring keeps the pedal and starts a slide instead.</summary>
        public const float DefaultLargeAngleThresholdRadians = 2.0943951f;

        /// <summary>The lift past the threshold. A lift, never a brake: the gear stays forward.</summary>
        public const float DefaultLargeAngleThrottleScale = 0.8f;

        public readonly float DeadZone;
        public readonly float ThrottleFloor;
        public readonly float ThrottleAtRing;
        public readonly float ThrottleResponseExponent;
        public readonly float HeadingGain;
        public readonly float YawRateDamping;
        public readonly float MaxLateralAcceleration;
        public readonly float MinimumReferenceSpeed;
        public readonly float Wheelbase;
        public readonly float FallbackSteerLimitRadians;
        public readonly float LargeAngleThresholdRadians;
        public readonly float LargeAngleThrottleScale;

        public HeadingDriveSettings(
            float deadZone,
            float throttleFloor,
            float throttleAtRing,
            float throttleResponseExponent,
            float headingGain,
            float yawRateDamping,
            float maxLateralAcceleration,
            float minimumReferenceSpeed,
            float wheelbase,
            float fallbackSteerLimitRadians,
            float largeAngleThresholdRadians,
            float largeAngleThrottleScale)
        {
            DeadZone = deadZone;
            ThrottleFloor = throttleFloor;
            ThrottleAtRing = throttleAtRing;
            ThrottleResponseExponent = throttleResponseExponent;
            HeadingGain = headingGain;
            YawRateDamping = yawRateDamping;
            MaxLateralAcceleration = maxLateralAcceleration;
            MinimumReferenceSpeed = minimumReferenceSpeed;
            Wheelbase = wheelbase;
            FallbackSteerLimitRadians = fallbackSteerLimitRadians;
            LargeAngleThresholdRadians = largeAngleThresholdRadians;
            LargeAngleThrottleScale = largeAngleThrottleScale;
        }

        /// <summary>The shipped tuning: what a config asset without its own numbers resolves to.</summary>
        public static readonly HeadingDriveSettings Default = new HeadingDriveSettings(
            DefaultDeadZone,
            DefaultThrottleFloor,
            DefaultThrottleAtRing,
            DefaultThrottleResponseExponent,
            DefaultHeadingGain,
            DefaultYawRateDamping,
            DefaultMaxLateralAcceleration,
            DefaultMinimumReferenceSpeed,
            DefaultWheelbase,
            DefaultSteerLimitRadians,
            DefaultLargeAngleThresholdRadians,
            DefaultLargeAngleThrottleScale);
    }
}
