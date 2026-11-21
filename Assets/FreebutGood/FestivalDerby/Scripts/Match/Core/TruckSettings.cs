namespace FestivalDerby.Match
{
    /// <summary>
    /// The truck's body and motion. Every value is a config knob, never a constant of the rules.
    /// Units are metres, seconds and metres per second. Defaults are the values the earlier CATCH mode
    /// settled on after its first playtest: a 0.75 m bed over a 5 m ramp (about 8.5 degrees) so a climbing
    /// car does not scrape its chassis, and a truck slow enough for a car to catch it.
    /// </summary>
    public readonly struct TruckSettings
    {
        /// <summary>How fast the truck travels along the racing line.</summary>
        public readonly float Speed;

        /// <summary>Line distance the truck holds until the match starts.</summary>
        public readonly float StartDistance;

        public readonly float BedLength;
        public readonly float BedWidth;

        /// <summary>Height of the bed top above the truck origin.</summary>
        public readonly float BedHeight;

        /// <summary>How far above or below the bed top a wheel contact may sit and still count.</summary>
        public readonly float BedHeightTolerance;

        /// <summary>Bed centre along the truck's forward axis; negative is behind the origin.</summary>
        public readonly float BedCenterOffset;

        /// <summary>Ramp reach behind the bed's rear edge.</summary>
        public readonly float RampLength;

        /// <summary>Largest speed difference to the truck at which a car still counts as riding it.</summary>
        public readonly float MaxRelativeSpeed;

        public TruckSettings(float speed, float startDistance, float bedLength, float bedWidth, float bedHeight,
            float bedHeightTolerance, float bedCenterOffset, float rampLength, float maxRelativeSpeed)
        {
            Speed = speed;
            StartDistance = startDistance;
            BedLength = bedLength;
            BedWidth = bedWidth;
            BedHeight = bedHeight;
            BedHeightTolerance = bedHeightTolerance;
            BedCenterOffset = bedCenterOffset;
            RampLength = rampLength;
            MaxRelativeSpeed = maxRelativeSpeed;
        }

        public static TruckSettings Default
        {
            get { return new TruckSettings(11f, 0f, 6f, 2.4f, 0.75f, 0.35f, -2.25f, 5f, 3f); }
        }

        /// <summary>The same truck driving at a different speed.</summary>
        public TruckSettings WithSpeed(float speed)
        {
            return new TruckSettings(speed, StartDistance, BedLength, BedWidth, BedHeight, BedHeightTolerance,
                BedCenterOffset, RampLength, MaxRelativeSpeed);
        }

        /// <summary>The same truck holding a different start distance, e.g. ahead of this run's grid.</summary>
        public TruckSettings WithStartDistance(float startDistance)
        {
            return new TruckSettings(Speed, startDistance, BedLength, BedWidth, BedHeight, BedHeightTolerance,
                BedCenterOffset, RampLength, MaxRelativeSpeed);
        }

        /// <summary>Half the bed: what the overtake rule treats as "level with the truck".</summary>
        public float BedHalfLength
        {
            get { return BedLength * 0.5f; }
        }

        /// <summary>Lengths, speeds and times are finite and positive; a default struct is invalid.</summary>
        public bool IsValid
        {
            get
            {
                return Positive(Speed)
                    && Finite(StartDistance)
                    && Positive(BedLength)
                    && Positive(BedWidth)
                    && Positive(BedHeight)
                    && Finite(BedHeightTolerance) && BedHeightTolerance >= 0f
                    && Finite(BedCenterOffset)
                    && Positive(RampLength)
                    && Positive(MaxRelativeSpeed);
            }
        }

        // NaN fails every relational test, so "> 0" already rejects it; infinity does not.
        private static bool Positive(float value)
        {
            return value > 0f && !float.IsInfinity(value);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
