namespace FestivalDerby.Driving
{
    /// <summary>
    /// Tuning for <see cref="ChaseFramingModel"/>. Kept as a plain value type with no engine
    /// reference so the framing can be measured by the offline harness instead of by frame captures.
    /// </summary>
    public readonly struct ChaseFramingSettings
    {
        /// <summary>Planar settling rate, in rad/s. A critically damped spring needs a lower rate than
        /// an exponential ease for the same feel, so this is not comparable to the old blend coefficient.</summary>
        public const float DefaultPositionSharpness = 6f;

        /// <summary>Vertical settling rate, in rad/s. Half the planar rate on purpose: a bump or a
        /// crest must read as a soft lift, never as the whole frame snapping up.</summary>
        public const float DefaultVerticalSharpness = 3f;

        /// <summary>Metres of lead per m/s of planar speed.</summary>
        public const float DefaultLookAheadMetresPerSpeed = 0.08f;

        /// <summary>Hard cap on the lead, in metres, reached at 22.5 m/s.</summary>
        public const float DefaultLookAheadMaxMetres = 1.8f;

        /// <summary>Settling rate of the lead.</summary>
        public const float DefaultLookAheadSharpness = 5f;

        /// <summary>Planar gap, in metres, above which the framing snaps: a respawn or a teleport is
        /// not something to ease through, because easing means the car leaves the screen.</summary>
        public const float DefaultSnapDistanceMetres = 8f;

        /// <summary>Planar speed, in m/s, above which a velocity is a broken reading rather than a
        /// speed. The car tops out near 30, so anything past this is a teleport artefact or a
        /// mid-frame hitch and must not be fed forward into the framing.</summary>
        public const float DefaultMaxFeedForwardSpeed = 60f;

        /// <summary>Planar settling rate, in rad/s.</summary>
        public readonly float PositionSharpness;

        /// <summary>Vertical settling rate, in rad/s.</summary>
        public readonly float VerticalSharpness;

        /// <summary>Metres of lead per m/s of planar speed.</summary>
        public readonly float LookAheadMetresPerSpeed;

        /// <summary>Hard cap on the lead, in metres.</summary>
        public readonly float LookAheadMaxMetres;

        /// <summary>Settling rate of the lead, in rad/s.</summary>
        public readonly float LookAheadSharpness;

        /// <summary>Planar gap, in metres, above which the framing snaps instead of easing. 0 never snaps.</summary>
        public readonly float SnapDistanceMetres;

        /// <summary>Planar speed, in m/s, above which a velocity is treated as a broken reading.</summary>
        public readonly float MaxFeedForwardSpeed;

        public ChaseFramingSettings(float positionSharpness, float verticalSharpness,
            float lookAheadMetresPerSpeed, float lookAheadMaxMetres, float lookAheadSharpness,
            float snapDistanceMetres, float maxFeedForwardSpeed)
        {
            PositionSharpness = positionSharpness;
            VerticalSharpness = verticalSharpness;
            LookAheadMetresPerSpeed = lookAheadMetresPerSpeed;
            LookAheadMaxMetres = lookAheadMaxMetres;
            LookAheadSharpness = lookAheadSharpness;
            SnapDistanceMetres = snapDistanceMetres;
            MaxFeedForwardSpeed = maxFeedForwardSpeed;
        }

        /// <summary>The shipped framing, as the look profile asset asks for it.</summary>
        public static ChaseFramingSettings Default
        {
            get
            {
                return new ChaseFramingSettings(
                    DefaultPositionSharpness,
                    DefaultVerticalSharpness,
                    DefaultLookAheadMetresPerSpeed,
                    DefaultLookAheadMaxMetres,
                    DefaultLookAheadSharpness,
                    DefaultSnapDistanceMetres,
                    DefaultMaxFeedForwardSpeed);
            }
        }
    }
}
