namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Brake-to-rotate knobs (FP0). While the brake is held at speed with the nose pointed away from
    /// the requested heading the rear grip is scaled down and the sideslip guard's cap is lifted, so
    /// the car rotates on the brake instead of understeering wide. A value of zero (the whole struct
    /// at default) disables the feature, so a preset that does not want it ships it inert.
    /// </summary>
    public readonly struct BrakeRotateTuning
    {
        /// <summary>Fraction of the rear grip removed at full engagement (0..1). 0 disables.</summary>
        public readonly float RearGripDrop;

        /// <summary>Extra sideslip allowed for the tick while rotating, radians. 0 disables.</summary>
        public readonly float BetaBoostRadians;

        /// <summary>Below this speed the brake is a stop, not a rotation aid.</summary>
        public readonly float MinSpeedMetersPerSecond;

        /// <summary>Below this heading error there is nothing to rotate towards.</summary>
        public readonly float MinErrorRadians;

        /// <summary>Seconds the effect takes to blend back out after the brake is released.</summary>
        public readonly float FadeSeconds;

        public BrakeRotateTuning(
            float rearGripDrop,
            float betaBoostRadians,
            float minSpeedMetersPerSecond,
            float minErrorRadians,
            float fadeSeconds)
        {
            RearGripDrop = rearGripDrop;
            BetaBoostRadians = betaBoostRadians;
            MinSpeedMetersPerSecond = minSpeedMetersPerSecond;
            MinErrorRadians = minErrorRadians;
            FadeSeconds = fadeSeconds;
        }

        /// <summary>All zeros: the feature off.</summary>
        public static BrakeRotateTuning Off
        {
            get { return default(BrakeRotateTuning); }
        }

        public bool IsEnabled
        {
            get { return RearGripDrop > 0f || BetaBoostRadians > 0f; }
        }
    }

    /// <summary>
    /// Straight-line dead band (FP0). At speed, a stick bearing within a small angle of the car's
    /// own heading is thumb noise, not a request: the error is scaled to zero so the wheel does not
    /// twitch. The band grows with speed and vanishes at a standstill, and a zero band disables it.
    /// </summary>
    public readonly struct StraightDeadBandTuning
    {
        /// <summary>Band half-width at full speed, radians. 0 disables.</summary>
        public readonly float Radians;

        /// <summary>Speed at which the band starts to open.</summary>
        public readonly float MinSpeedMetersPerSecond;

        /// <summary>Speed at which the band reaches its full width.</summary>
        public readonly float FullSpeedMetersPerSecond;

        public StraightDeadBandTuning(float radians, float minSpeedMetersPerSecond, float fullSpeedMetersPerSecond)
        {
            Radians = radians;
            MinSpeedMetersPerSecond = minSpeedMetersPerSecond;
            FullSpeedMetersPerSecond = fullSpeedMetersPerSecond;
        }

        /// <summary>All zeros: the feature off.</summary>
        public static StraightDeadBandTuning Off
        {
            get { return default(StraightDeadBandTuning); }
        }

        public bool IsEnabled
        {
            get { return Radians > 0f; }
        }
    }

    /// <summary>
    /// Auto-unstick (CTRL1). AutoAccelerate keeps the throttle on while the stick is out, so a car
    /// nosed into a wall can sit at full pedal forever. A short reverse pulse, earned by a full second
    /// of asking to move and not moving, is the escape the reference notes ask for (recommendation 11)
    /// without giving the player a hidden reverse button. All zeros disables the pulse.
    /// </summary>
    public readonly struct AutoUnstickTuning
    {
        /// <summary>Speed magnitude below which the car counts as not moving, m/s.</summary>
        public readonly float TriggerSpeedMetersPerSecond;

        /// <summary>Pedal the trigger needs; keeps a coasting car from pulsing.</summary>
        public readonly float TriggerPedal;

        /// <summary>Seconds of stuck-at-full-pedal before the pulse fires.</summary>
        public readonly float HoldSeconds;

        /// <summary>Seconds the reverse pulse lasts.</summary>
        public readonly float PulseSeconds;

        public AutoUnstickTuning(
            float triggerSpeedMetersPerSecond,
            float triggerPedal,
            float holdSeconds,
            float pulseSeconds)
        {
            TriggerSpeedMetersPerSecond = triggerSpeedMetersPerSecond;
            TriggerPedal = triggerPedal;
            HoldSeconds = holdSeconds;
            PulseSeconds = pulseSeconds;
        }

        /// <summary>All zeros: the feature off.</summary>
        public static AutoUnstickTuning Off
        {
            get { return default(AutoUnstickTuning); }
        }

        public bool IsEnabled
        {
            get { return HoldSeconds > 0f && PulseSeconds > 0f; }
        }
    }

    /// <summary>
    /// Catch-up torque (FEEL2). A crash costs the player the whole run and the owner's playtest wants
    /// the car back on the power immediately ("the faster it happens, the more dopamine, the better").
    /// Below a fraction of top speed the drive force is multiplied by a catch-up factor that fades
    /// linearly back to 1 by the fade fraction; a heavy impact, reported by the Unity contact hook
    /// through <see cref="HandlingState.NotifyImpact"/>, lifts that factor towards its peak for a short
    /// window.
    ///
    /// The extra torque is still clamped by the rear axle grip: the surge shows up in
    /// <see cref="HandlingOutput.RearWheelspin01"/> as wheelspin (the smoke cue) instead of as
    /// impossible acceleration. The struct at default (all zeros) is the feature off, so a preset that
    /// does not want it ships it inert.
    /// </summary>
    public readonly struct CatchUpTuning
    {
        /// <summary>Speed below which the catch-up applies, as a fraction of top speed.</summary>
        public readonly float TriggerSpeedFraction;

        /// <summary>Speed at which the catch-up has faded back to 1, as a fraction of top speed.</summary>
        public readonly float FadeEndSpeedFraction;

        /// <summary>Drive multiplier below the trigger speed. 1 or less disables the base catch-up.</summary>
        public readonly float BaseFactor;

        /// <summary>Drive multiplier reached at full impact severity inside the window.</summary>
        public readonly float ImpactFactor;

        /// <summary>Seconds a <see cref="HandlingState.NotifyImpact"/> keeps the peak factor available.</summary>
        public readonly float ImpactWindowSeconds;

        /// <summary>Pedal the driver must ask for; a lifted car does not get free torque.</summary>
        public readonly float MinThrottle;

        /// <summary>
        /// Rear grip budget used to clamp the drive force, as a multiple of the axle's own
        /// <c>mu * N</c>. The lateral cap is sized for a lateral force; at 1.0 the drive demand at a
        /// standing launch already saturates it, so every extra newton of catch-up torque would turn
        /// into smoke and never reach the road. 1.5 leaves the surge a place to go while still
        /// reporting wheelspin at a full-pedal launch.
        /// </summary>
        public readonly float DriveGripScale;

        public CatchUpTuning(
            float triggerSpeedFraction,
            float fadeEndSpeedFraction,
            float baseFactor,
            float impactFactor,
            float impactWindowSeconds,
            float minThrottle,
            float driveGripScale)
        {
            TriggerSpeedFraction = triggerSpeedFraction;
            FadeEndSpeedFraction = fadeEndSpeedFraction;
            BaseFactor = baseFactor;
            ImpactFactor = impactFactor;
            ImpactWindowSeconds = impactWindowSeconds;
            MinThrottle = minThrottle;
            DriveGripScale = driveGripScale;
        }

        /// <summary>All zeros: the feature off.</summary>
        public static CatchUpTuning Off
        {
            get { return default(CatchUpTuning); }
        }

        public bool IsEnabled
        {
            get { return (BaseFactor > 1f || ImpactFactor > 1f) && FadeEndSpeedFraction > TriggerSpeedFraction; }
        }

        /// <summary>
        /// The drive multiplier for this tick. Pure and allocation-free so the tick can call it in
        /// place; the impact severity is clamped, never trusted.
        /// </summary>
        public float FactorFor(float speedFraction, float throttle, float impactSeverity)
        {
            if (!IsEnabled || throttle < MinThrottle || speedFraction >= FadeEndSpeedFraction)
            {
                return 1f;
            }

            float fade;
            if (speedFraction <= TriggerSpeedFraction)
            {
                fade = 1f;
            }
            else
            {
                fade = 1f - (speedFraction - TriggerSpeedFraction) / (FadeEndSpeedFraction - TriggerSpeedFraction);
            }

            float severity = impactSeverity > 1f ? 1f : (impactSeverity > 0f ? impactSeverity : 0f);
            float peak = BaseFactor + (ImpactFactor - BaseFactor) * severity;
            if (peak < 1f)
            {
                peak = 1f;
            }

            return 1f + (peak - 1f) * fade;
        }
    }

    /// <summary>
    /// Every tunable of the V2 handling model in one value type, plus the three shipped presets.
    /// It lives in the engine-free assembly on purpose: the client and the server must run the same
    /// numbers, and neither may reference a ScriptableObject to do it (the Unity-side
    /// <c>HandlingProfile</c> asset copies into this struct, it is not read from the tick).
    ///
    /// Values come from Documentation/Design/DRIVING_MODEL_V2.md; where that document is silent the
    /// value is marked as an assumption in the field comment so a retune can find it. The struct is
    /// readonly: a preset is a value the caller can copy, adjust and hand back without a rebuild.
    /// </summary>
    public readonly struct HandlingParams
    {
        /// <summary>Reference gravity for turning an axle load in newtons into an axle mass in kg.</summary>
        public const float StandardGravity = 9.80665f;

        public const float DegreesPerRadian = 57.29578f;
        public const float RadiansPerDegree = 0.017453292f;

        /// <summary>
        /// Floor under every heading gain (FP0). Below 4/s the heading loop is slow enough that the
        /// car feels unresponsive on the way into a corner, and a Drifty slide must not cost the
        /// player the ability to point the car. The clamp lives on the constructor so no preset and
        /// no asset override can ship below it.
        /// </summary>
        public const float MinimumHeadingGain = 4f;

        public static float SanitizeHeadingGain(float headingGain)
        {
            // Written as a negated >= so a NaN from a hand-edited profile asset lands on the floor
            // instead of poisoning every heading term downstream (NaN < floor is false).
            return headingGain >= MinimumHeadingGain ? headingGain : MinimumHeadingGain;
        }

        // ------------------------------------------------------------------ presets (V2 spec table)

        private static readonly HandlingParams GrippyPreset = Create(
            muPeak: 2.6f,
            rearCurveEnd: 0.5f,
            launchTorqueFactor: 0.8f,
            assistGain: 8f,
            headingGain: 5f,
            betaMaxRadians: 15f * RadiansPerDegree,
            lateralAccelBudget: 18f);

        private static readonly HandlingParams BalancedPreset = Create(
            muPeak: 2.0f,
            rearCurveEnd: 0.35f,
            launchTorqueFactor: 0.85f,
            assistGain: 6f,
            headingGain: 4f,
            betaMaxRadians: 22f * RadiansPerDegree,
            lateralAccelBudget: 16f);

        private static readonly HandlingParams DriftyPreset = Create(
            muPeak: 1.5f,
            rearCurveEnd: 0.22f,
            launchTorqueFactor: 0.9f,
            assistGain: 3f,
            headingGain: 4f,
            betaMaxRadians: 30f * RadiansPerDegree,
            lateralAccelBudget: 13f);

        public static HandlingParams Grippy
        {
            get { return GrippyPreset; }
        }

        public static HandlingParams Balanced
        {
            get { return BalancedPreset; }
        }

        public static HandlingParams Drifty
        {
            get { return DriftyPreset; }
        }

        // ------------------------------------------------------------------------------- the fields

        // Intent.
        public readonly float StickLowPassSeconds;
        public readonly float DeadZone;
        public readonly float ThrottleFloor;
        public readonly float ThrottleAtRing;

        /// <summary>
        /// Shape of the pedal across the ring, a 0..1 input to the throttle-floor/at-ring lerp. The
        /// spec writes this as <c>x^1.4</c>; it is baked as a four-sample curve at 0, 1/3, 2/3 and 1
        /// (values 0, 0.208, 0.552, 1) because a piecewise linear read is the only curve evaluation
        /// that is bit-identical everywhere; System.Math.Pow is not.
        /// </summary>
        public readonly Piecewise4 ThrottleResponseCurve;

        public readonly float CommitEnterRadians;
        public readonly float CommitExitRadians;
        public readonly float UTurnStartRadians;
        public readonly float UTurnFullRadians;
        public readonly float UTurnThrottleScale;

        /// <summary>
        /// Error below which a held U-turn lift releases. The spec has no release angle (its ramp
        /// fades with the error); this one exists because the lift has to survive the second half of
        /// the turn for the arc to stay inside the spec's own 3.5 s. Assumption, 20 degrees.
        /// </summary>
        public readonly float UTurnReleaseRadians;

        /// <summary>
        /// Drive rescue S1/S3: the lift only tightens arcs at speed; at or below this forward speed
        /// it is skipped, so a car that just hit a wall (slow, facing the wrong way) keeps its
        /// throttle while the heading loop rotates it out.
        /// </summary>
        public readonly float UTurnMinSpeedMetersPerSecond;

        // Longitudinal.
        public readonly float TopSpeedMetersPerSecond;
        public readonly float AccelMaxMetersPerSecondSquared;
        public readonly float ThrottleRiseRate;
        public readonly float ThrottleFallRate;
        public readonly float EngineBrakeMetersPerSecondSquared;

        /// <summary>
        /// Smoothed pedal at which the engine brake has fully faded out. Engine braking is what
        /// happens when you lift, so it belongs to the release: below this pedal it bites in
        /// proportionally and a held throttle never pays for it.
        ///
        /// The band is narrow on purpose. The response curve is flat near the dead zone (x^1.4 puts
        /// a 20% stick at a 0.019 pedal), so a wide band would keep the brake on while the driver is
        /// already asking for power, and a barely-out stick would slow the car. 0.02 is the pedal a
        /// 20% stick produces: by the time the stick is a fifth out, the brake is gone
        /// (V2e.1 feel retune).
        /// </summary>
        public readonly float EngineBrakeFadePedal;
        public readonly float CoastDragPerSecond;
        public readonly float BrakeDecelMetersPerSecondSquared;
        public readonly float ReverseAccelMetersPerSecondSquared;
        public readonly float ReverseTopSpeedMetersPerSecond;
        public readonly Piecewise4 ThrottleCurve;

        // Lateral grip per axle.
        public readonly Piecewise4 FrontAxleCurve;
        public readonly Piecewise4 RearAxleCurve;
        public readonly float MuPeak;
        public readonly float SlipRatioFloorMetersPerSecond;
        public readonly float SlipCancelFraction;

        // Steering.
        public readonly float HeadingGain;
        public readonly float MaxYawRateRadiansPerSecond;
        public readonly float LateralAccelBudgetMetersPerSecondSquared;

        /// <summary>
        /// Gain on the yaw-rate error in the steer command (`feedForward + k * (omegaTarget - omega)`).
        /// FP0 dropped this from 0.15 to 0.05: at 0.15 the front axle (a single-axle force that
        /// saturates almost immediately at 25 m/s) closed a discrete-time loop around 1 and the car
        /// limit-cycled at the end of a corner, flipping yaw rate 156 times in one corner-exit
        /// measurement. The direct yaw damping the stabilizer already applies is what holds the car
        /// now; this term only has to sharpen turn-in.
        /// </summary>
        public readonly float YawRateDamping;
        public readonly float SteerRateRadiansPerSecond;
        public readonly float SteerMinRadians;
        public readonly float SteerMaxRadians;
        public readonly float MinReferenceSpeedMetersPerSecond;
        public readonly float MinSteerSpeedMetersPerSecond;
        public readonly float WheelbaseMeters;

        // Yaw stabilizer.
        public readonly float AssistGain;
        public readonly float AssistEngageSpeedLow;
        public readonly float AssistEngageSpeedHigh;
        public readonly float AssistFadeSlipLow;
        public readonly float AssistFadeSlipHigh;
        public readonly float YawDampingRate;

        // Sideslip guard.
        public readonly float BetaMaxRadians;
        public readonly float GuardMinSpeedMetersPerSecond;
        public readonly float GuardYawGain;
        public readonly float GuardRearMuBoost;
        public readonly float GuardRearMuBoostRangeRadians;

        // Air.
        public readonly float AirGravityScale;
        public readonly float AirYawAccelRadiansPerSecondSquared;
        public readonly float AirYawDamping;
        public readonly float AirLevelGain;

        // FP0 assists. Both ship enabled for the presets; a struct at default is the feature off.
        public readonly BrakeRotateTuning BrakeRotate;
        public readonly StraightDeadBandTuning StraightDeadBand;

        // CTRL1. Off for RingThrottle and GasButton (the mapper gates it to AutoAccelerate); shipped
        // on for the presets because AutoAccelerate is an option, not the default scheme.
        public readonly AutoUnstickTuning AutoUnstick;

        // FEEL2. Shipped on for the presets; a struct at default is the feature off.
        public readonly CatchUpTuning CatchUp;

        private HandlingParams(
            float stickLowPassSeconds,
            float deadZone,
            float throttleFloor,
            float throttleAtRing,
            Piecewise4 throttleResponseCurve,
            float commitEnterRadians,
            float commitExitRadians,
            float uTurnStartRadians,
            float uTurnFullRadians,
            float uTurnThrottleScale,
            float uTurnReleaseRadians,
            float uTurnMinSpeedMetersPerSecond,
            float topSpeedMetersPerSecond,
            float accelMaxMetersPerSecondSquared,
            float throttleRiseRate,
            float throttleFallRate,
            float engineBrakeMetersPerSecondSquared,
            float engineBrakeFadePedal,
            float coastDragPerSecond,
            float brakeDecelMetersPerSecondSquared,
            float reverseAccelMetersPerSecondSquared,
            float reverseTopSpeedMetersPerSecond,
            Piecewise4 throttleCurve,
            Piecewise4 frontAxleCurve,
            Piecewise4 rearAxleCurve,
            float muPeak,
            float slipRatioFloorMetersPerSecond,
            float slipCancelFraction,
            float headingGain,
            float maxYawRateRadiansPerSecond,
            float lateralAccelBudgetMetersPerSecondSquared,
            float yawRateDamping,
            float steerRateRadiansPerSecond,
            float steerMinRadians,
            float steerMaxRadians,
            float minReferenceSpeedMetersPerSecond,
            float minSteerSpeedMetersPerSecond,
            float wheelbaseMeters,
            float assistGain,
            float assistEngageSpeedLow,
            float assistEngageSpeedHigh,
            float assistFadeSlipLow,
            float assistFadeSlipHigh,
            float yawDampingRate,
            float betaMaxRadians,
            float guardMinSpeedMetersPerSecond,
            float guardYawGain,
            float guardRearMuBoost,
            float guardRearMuBoostRangeRadians,
            float airGravityScale,
            float airYawAccelRadiansPerSecondSquared,
            float airYawDamping,
            float airLevelGain,
            BrakeRotateTuning brakeRotate,
            StraightDeadBandTuning straightDeadBand,
            AutoUnstickTuning autoUnstick,
            CatchUpTuning catchUp)
        {
            StickLowPassSeconds = stickLowPassSeconds;
            DeadZone = deadZone;
            ThrottleFloor = throttleFloor;
            ThrottleAtRing = throttleAtRing;
            ThrottleResponseCurve = throttleResponseCurve;
            CommitEnterRadians = commitEnterRadians;
            CommitExitRadians = commitExitRadians;
            UTurnStartRadians = uTurnStartRadians;
            UTurnFullRadians = uTurnFullRadians;
            UTurnThrottleScale = uTurnThrottleScale;
            UTurnReleaseRadians = uTurnReleaseRadians;
            UTurnMinSpeedMetersPerSecond = uTurnMinSpeedMetersPerSecond;
            TopSpeedMetersPerSecond = topSpeedMetersPerSecond;
            AccelMaxMetersPerSecondSquared = accelMaxMetersPerSecondSquared;
            ThrottleRiseRate = throttleRiseRate;
            ThrottleFallRate = throttleFallRate;
            EngineBrakeMetersPerSecondSquared = engineBrakeMetersPerSecondSquared;
            EngineBrakeFadePedal = engineBrakeFadePedal;
            CoastDragPerSecond = coastDragPerSecond;
            BrakeDecelMetersPerSecondSquared = brakeDecelMetersPerSecondSquared;
            ReverseAccelMetersPerSecondSquared = reverseAccelMetersPerSecondSquared;
            ReverseTopSpeedMetersPerSecond = reverseTopSpeedMetersPerSecond;
            ThrottleCurve = throttleCurve;
            FrontAxleCurve = frontAxleCurve;
            RearAxleCurve = rearAxleCurve;
            MuPeak = muPeak;
            SlipRatioFloorMetersPerSecond = slipRatioFloorMetersPerSecond;
            SlipCancelFraction = slipCancelFraction;
            HeadingGain = SanitizeHeadingGain(headingGain);
            MaxYawRateRadiansPerSecond = maxYawRateRadiansPerSecond;
            LateralAccelBudgetMetersPerSecondSquared = lateralAccelBudgetMetersPerSecondSquared;
            YawRateDamping = yawRateDamping;
            SteerRateRadiansPerSecond = steerRateRadiansPerSecond;
            SteerMinRadians = steerMinRadians;
            SteerMaxRadians = steerMaxRadians;
            MinReferenceSpeedMetersPerSecond = minReferenceSpeedMetersPerSecond;
            MinSteerSpeedMetersPerSecond = minSteerSpeedMetersPerSecond;
            WheelbaseMeters = wheelbaseMeters;
            AssistGain = assistGain;
            AssistEngageSpeedLow = assistEngageSpeedLow;
            AssistEngageSpeedHigh = assistEngageSpeedHigh;
            AssistFadeSlipLow = assistFadeSlipLow;
            AssistFadeSlipHigh = assistFadeSlipHigh;
            YawDampingRate = yawDampingRate;
            BetaMaxRadians = betaMaxRadians;
            GuardMinSpeedMetersPerSecond = guardMinSpeedMetersPerSecond;
            GuardYawGain = guardYawGain;
            GuardRearMuBoost = guardRearMuBoost;
            GuardRearMuBoostRangeRadians = guardRearMuBoostRangeRadians;
            AirGravityScale = airGravityScale;
            AirYawAccelRadiansPerSecondSquared = airYawAccelRadiansPerSecondSquared;
            AirYawDamping = airYawDamping;
            AirLevelGain = airLevelGain;
            BrakeRotate = brakeRotate;
            StraightDeadBand = straightDeadBand;
            AutoUnstick = autoUnstick;
            CatchUp = catchUp;
        }

        /// <summary>
        /// A copy of this handling with the four steering knobs a <c>HandlingProfile</c> asset can
        /// override applied on top. An override of 0 (or negative) keeps this value untouched, so a
        /// shipped asset with every field at its default is behaviour-preserving by construction
        /// (V2e.2: the owner tunes the values in the Inspector, the mechanism ships inert).
        ///
        /// Pure by contract: the presets are readonly statics, the returned value is a fresh copy, and
        /// two calls from the same base produce the same result — nothing accumulates.
        /// </summary>
        public HandlingParams WithSteeringOverrides(
            float headingGain,
            float maxYawRateRadiansPerSecond,
            float lateralAccelBudgetMetersPerSecondSquared,
            float steerRateRadiansPerSecond)
        {
            return Clone(
                headingGain > 0f ? headingGain : HeadingGain,
                maxYawRateRadiansPerSecond > 0f ? maxYawRateRadiansPerSecond : MaxYawRateRadiansPerSecond,
                lateralAccelBudgetMetersPerSecondSquared > 0f
                    ? lateralAccelBudgetMetersPerSecondSquared
                    : LateralAccelBudgetMetersPerSecondSquared,
                steerRateRadiansPerSecond > 0f ? steerRateRadiansPerSecond : SteerRateRadiansPerSecond,
                BrakeRotate,
                StraightDeadBand,
                AutoUnstick,
                CatchUp,
                TopSpeedMetersPerSecond,
                AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// A copy of this handling with the top speed and the full-throttle acceleration multiplied. A
        /// scale of 0 (or negative, or not finite) keeps that value, the same inert-by-default rule as
        /// <see cref="WithSteeringOverrides"/>. The cornering budget is left alone on purpose: a faster car
        /// asks the same tyres for the same grip, so it takes wider arcs at speed rather than a stiffer car.
        /// </summary>
        public HandlingParams WithLongitudinalScale(float topSpeedScale, float accelScale)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                BrakeRotate,
                StraightDeadBand,
                AutoUnstick,
                CatchUp,
                IsUsableScale(topSpeedScale) ? TopSpeedMetersPerSecond * topSpeedScale : TopSpeedMetersPerSecond,
                IsUsableScale(accelScale)
                    ? AccelMaxMetersPerSecondSquared * accelScale
                    : AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        private static bool IsUsableScale(float scale)
        {
            return scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale);
        }

        /// <summary>
        /// A copy of this handling with the FP0 brake-to-rotate tuning replaced. Pass
        /// <see cref="BrakeRotateTuning.Off"/> to ship the feature inert.
        /// </summary>
        public HandlingParams WithBrakeRotate(BrakeRotateTuning tuning)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                tuning,
                StraightDeadBand,
                AutoUnstick,
                CatchUp,
                TopSpeedMetersPerSecond,
                AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// A copy of this handling with the FP0 straight-line dead band replaced. Pass
        /// <see cref="StraightDeadBandTuning.Off"/> to ship the feature inert.
        /// </summary>
        public HandlingParams WithStraightDeadBand(StraightDeadBandTuning tuning)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                BrakeRotate,
                tuning,
                AutoUnstick,
                CatchUp,
                TopSpeedMetersPerSecond,
                AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// A copy of this handling with the CTRL1 auto-unstick replaced. Pass
        /// <see cref="AutoUnstickTuning.Off"/> to ship the pulse inert.
        /// </summary>
        public HandlingParams WithAutoUnstick(AutoUnstickTuning tuning)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                BrakeRotate,
                StraightDeadBand,
                tuning,
                CatchUp,
                TopSpeedMetersPerSecond,
                AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// A copy of this handling with the FEEL2 catch-up torque replaced. Pass
        /// <see cref="CatchUpTuning.Off"/> to ship the feature inert.
        /// </summary>
        public HandlingParams WithCatchUp(CatchUpTuning tuning)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                BrakeRotate,
                StraightDeadBand,
                AutoUnstick,
                tuning,
                TopSpeedMetersPerSecond,
                AccelMaxMetersPerSecondSquared,
                ThrottleCurve,
                EngineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// A copy of this handling with the FEEL2 longitudinal pace knobs replaced in one call. Unlike
        /// the steering knobs there is no keep-if-zero convention here: these are the values, so a test
        /// can rebuild the pre-FEEL2 car to print a genuine before/after table without a second preset.
        /// </summary>
        public HandlingParams WithLongitudinalPace(
            float topSpeedMetersPerSecond,
            float accelMaxMetersPerSecondSquared,
            Piecewise4 throttleCurve,
            float engineBrakeMetersPerSecondSquared)
        {
            return Clone(
                HeadingGain,
                MaxYawRateRadiansPerSecond,
                LateralAccelBudgetMetersPerSecondSquared,
                SteerRateRadiansPerSecond,
                BrakeRotate,
                StraightDeadBand,
                AutoUnstick,
                CatchUp,
                topSpeedMetersPerSecond,
                accelMaxMetersPerSecondSquared,
                throttleCurve,
                engineBrakeMetersPerSecondSquared);
        }

        /// <summary>
        /// One construction site for every copy of these params. The four steering knobs arrive
        /// already resolved (an override of 0 was turned into "keep the preset" by the caller);
        /// everything else is copied from the instance. Keeping it in one place is what stops a new
        /// field from being forgotten in one of the copies.
        /// </summary>
        private HandlingParams Clone(
            float headingGain,
            float maxYawRateRadiansPerSecond,
            float lateralAccelBudgetMetersPerSecondSquared,
            float steerRateRadiansPerSecond,
            BrakeRotateTuning brakeRotate,
            StraightDeadBandTuning straightDeadBand,
            AutoUnstickTuning autoUnstick,
            CatchUpTuning catchUp,
            float topSpeedMetersPerSecond,
            float accelMaxMetersPerSecondSquared,
            Piecewise4 throttleCurve,
            float engineBrakeMetersPerSecondSquared)
        {
            return new HandlingParams(
                StickLowPassSeconds,
                DeadZone,
                ThrottleFloor,
                ThrottleAtRing,
                ThrottleResponseCurve,
                CommitEnterRadians,
                CommitExitRadians,
                UTurnStartRadians,
                UTurnFullRadians,
                UTurnThrottleScale,
                UTurnReleaseRadians,
                UTurnMinSpeedMetersPerSecond,
                topSpeedMetersPerSecond,
                accelMaxMetersPerSecondSquared,
                ThrottleRiseRate,
                ThrottleFallRate,
                engineBrakeMetersPerSecondSquared,
                EngineBrakeFadePedal,
                CoastDragPerSecond,
                BrakeDecelMetersPerSecondSquared,
                ReverseAccelMetersPerSecondSquared,
                ReverseTopSpeedMetersPerSecond,
                throttleCurve,
                FrontAxleCurve,
                RearAxleCurve,
                MuPeak,
                SlipRatioFloorMetersPerSecond,
                SlipCancelFraction,
                headingGain,
                maxYawRateRadiansPerSecond,
                lateralAccelBudgetMetersPerSecondSquared,
                YawRateDamping,
                steerRateRadiansPerSecond,
                SteerMinRadians,
                SteerMaxRadians,
                MinReferenceSpeedMetersPerSecond,
                MinSteerSpeedMetersPerSecond,
                WheelbaseMeters,
                AssistGain,
                AssistEngageSpeedLow,
                AssistEngageSpeedHigh,
                AssistFadeSlipLow,
                AssistFadeSlipHigh,
                YawDampingRate,
                BetaMaxRadians,
                GuardMinSpeedMetersPerSecond,
                GuardYawGain,
                GuardRearMuBoost,
                GuardRearMuBoostRangeRadians,
                AirGravityScale,
                AirYawAccelRadiansPerSecondSquared,
                AirYawDamping,
                AirLevelGain,
                brakeRotate,
                straightDeadBand,
                autoUnstick,
                catchUp);
        }

        /// <summary>
        /// The shared body of the three presets. Only the seven fields the spec table varies are
        /// arguments; everything else is the V2 default and lives here once.
        /// </summary>
        private static HandlingParams Create(
            float muPeak,
            float rearCurveEnd,
            float launchTorqueFactor,
            float assistGain,
            float headingGain,
            float betaMaxRadians,
            float lateralAccelBudget)
        {
            return new HandlingParams(
                // The V2e.1 feel retune: 30 ms so the stick reaches the mapper in half the old
                // time, and a pedal that starts at zero at the dead-zone edge — partial stick is
                // partial pedal, with no 35% step at the edge of intent.
                stickLowPassSeconds: 0.03f,
                deadZone: 0.12f,
                throttleFloor: 0f,
                throttleAtRing: 1f,
                // x^1.4 sampled at 0, 1/3, 2/3 and 1.
                throttleResponseCurve: new Piecewise4(0f, 0f, 0.3333333f, 0.208f, 0.6666667f, 0.552f, 1f, 1f),
                // Commit past 150 degrees, release under 110: the gap is what stops the flip-flop
                // that spun the car when the stick sat near dead astern.
                commitEnterRadians: 150f * RadiansPerDegree,
                commitExitRadians: 110f * RadiansPerDegree,
                uTurnStartRadians: 100f * RadiansPerDegree,
                uTurnFullRadians: 170f * RadiansPerDegree,
                // Spec proposes 0.55; that value cannot satisfy the spec's own test 2. At 0.55 the drive
                // force in a U-turn is still 6 m/s^2 against 1.9 m/s^2 of coast, so the car accelerates
                // to ~32 m/s while it turns, yawMax collapses to 16/32, and the 180 degree arc needs
                // 5 s instead of the required 3.5 s. A lift this deep makes the U-turn an actual lift
                // (the car coasts down to ~14 m/s) which is what tightens the radius and what the
                // design note means by "lift to tighten the radius, never brake".
                // FEEL2 deepens it from 0.1 to 0.04: the old depth was calibrated at accelMax 9, where
                // a tenth of the pedal left 0.74 m/s^2 of drive. At accelMax 19 the same tenth is
                // 1.75 m/s^2, which holds the arc's speed and makes yawMax collapse exactly like the
                // 0.55 case above (measured: the drifty U-turn went from 3.4 s to 3.98 s). 0.04 restores
                // the old coast-down without changing any speed parameter.
                uTurnThrottleScale: 0.04f,
                uTurnReleaseRadians: 20f * RadiansPerDegree,
                uTurnMinSpeedMetersPerSecond: 3f,
                // FEEL2: the curve end returns to 31 (the owner's 2026-10-13 cut is reverted by the
                // 2026-10-15 playtest: "the faster it happens, the more dopamine, the better"). The
                // knee at 0.85 below keeps the free top speed near 30 instead of running to the
                // ceiling, so the car pulls hard to 85% and then tails off.
                topSpeedMetersPerSecond: 31f,
                // FEEL2: 9 to 19 m/s^2. The driver asked for a punchier ramp and an earlier ceiling
                // arrival; the knee in throttleCurve is what keeps the car from pinning the pedal.
                accelMaxMetersPerSecondSquared: 19f,
                // V2e.1 response retune: 111 ms to full pedal and 71 ms to release. Reaction is
                // latency, not punch — accelMax stays 9.
                throttleRiseRate: 9f,
                throttleFallRate: 14f,
                // V2e.1: engine braking is what happens when you lift, fading to nothing across
                // engineBrakeFadePedal so a held throttle does not pay for it. FEEL2 trims 5 to 4.5:
                // the car now builds speed hard, and a lift should bleed it gently rather than act as
                // a second brake (the FP0 brake-to-rotate still owns corner entry). Catch playtest 1
                // raises it to 7: the REVERSE/BRAKE buttons are gone (touch-to-drive), so letting go of
                // the stick is the only way to slow down and has to read as braking.
                engineBrakeMetersPerSecondSquared: 7f,
                engineBrakeFadePedal: 0.02f,
                // Tuned with the engine brake above so the free top speed does not rise above the
                // shipped measurement (28 m/s, metric test5 before this retune): the drive curve
                // no longer fights the old flat 1.5 at full pedal, so drag alone carries the
                // ceiling. It settles ~26 m/s — below the shipped value, and the owner has cut the
                // top speed twice.
                coastDragPerSecond: 0.14f,
                brakeDecelMetersPerSecondSquared: 18f,
                reverseAccelMetersPerSecondSquared: 8f,
                reverseTopSpeedMetersPerSecond: 8f,
                // FEEL2: flat from the 45% pedal knee to the 85% speed knee. The old curve drooped to
                // 0.85 at 65% of the pedal and then to 0, so acceleration tailed off exactly where the
                // driver was asking for the most; the new shape holds full torque until the speed knee.
                throttleCurve: new Piecewise4(0f, launchTorqueFactor, 0.45f, 1f, 0.85f, 1f, 1f, 0f),
                frontAxleCurve: new Piecewise4(0f, 1f, 0.15f, 1f, 0.4f, 0.75f, 1f, 0.45f),
                rearAxleCurve: new Piecewise4(0f, 1f, 0.12f, 1f, 0.35f, 0.65f, 1f, rearCurveEnd),
                muPeak: muPeak,
                slipRatioFloorMetersPerSecond: 0.25f,
                slipCancelFraction: 0.9f,
                headingGain: headingGain,
                maxYawRateRadiansPerSecond: 150f * RadiansPerDegree,
                lateralAccelBudgetMetersPerSecondSquared: lateralAccelBudget,
                yawRateDamping: 0.05f,
                steerRateRadiansPerSecond: 180f * RadiansPerDegree,
                steerMinRadians: 5f * RadiansPerDegree,
                steerMaxRadians: 32f * RadiansPerDegree,
                minReferenceSpeedMetersPerSecond: 3f,
                minSteerSpeedMetersPerSecond: 4f,
                wheelbaseMeters: 2.5f,
                assistGain: assistGain,
                assistEngageSpeedLow: 1f,
                assistEngageSpeedHigh: 4f,
                assistFadeSlipLow: 0.25f,
                assistFadeSlipHigh: 0.5f,
                yawDampingRate: 0.5f,
                betaMaxRadians: betaMaxRadians,
                guardMinSpeedMetersPerSecond: 5f,
                guardYawGain: 10f,
                guardRearMuBoost: 2f,
                guardRearMuBoostRangeRadians: 10f * RadiansPerDegree,
                airGravityScale: 1.3f,
                airYawAccelRadiansPerSecondSquared: 2.5f,
                airYawDamping: 3f,
                airLevelGain: 3f,
                // FP0 brake-to-rotate: 15% off the rear at full engagement and 8 degrees onto the
                // sideslip cap, but only above 6 m/s and with more than 15 degrees to gather, and
                // blended back over 250 ms so releasing the brake does not snap the rear loose.
                brakeRotate: new BrakeRotateTuning(
                    rearGripDrop: 0.15f,
                    betaBoostRadians: 8f * RadiansPerDegree,
                    minSpeedMetersPerSecond: 6f,
                    minErrorRadians: 15f * RadiansPerDegree,
                    fadeSeconds: 0.25f),
                // FP0 straight-line dead band: 4 degrees of thumb slop ignored once the car is at
                // 20 m/s, opening progressively from 4 m/s so a slow manoeuvre keeps full authority.
                straightDeadBand: new StraightDeadBandTuning(
                    radians: 4f * RadiansPerDegree,
                    minSpeedMetersPerSecond: 4f,
                    fullSpeedMetersPerSecond: 20f),
                // CTRL1 auto-unstick, per the references' recommendation 11: a full second of full
                // pedal without moving earns a 0.7 s reverse pulse. Every scheme reads it (drive
                // rescue S2); AutoUnstickTuning.Off ships it inert.
                autoUnstick: new AutoUnstickTuning(
                    triggerSpeedMetersPerSecond: 1f,
                    triggerPedal: 0.5f,
                    holdSeconds: 1f,
                    pulseSeconds: 0.7f),
                // FEEL2 catch-up torque. Below 45% of top speed a driven car gets 25% more drive,
                // fading out by 60%; a heavy impact (NotifyImpact) lifts it to 2x for 1.5 s. The base
                // factor is 1.25 rather than the 1.6 first sketched because the rear grip clamp
                // saturates across the whole 0->15 m/s window at 1.6, which would make the impact
                // factor (2.0) and the base factor deliver the same speed: wheelspin cannot be
                // converted back into acceleration. 1.25/2.0 is the pair that actually delivers the
                // 30% faster recovery the acceptance test asks for, with the surplus reported as
                // wheelspin.
                catchUp: new CatchUpTuning(
                    triggerSpeedFraction: 0.45f,
                    fadeEndSpeedFraction: 0.60f,
                    baseFactor: 1.25f,
                    impactFactor: 2f,
                    impactWindowSeconds: 1.5f,
                    minThrottle: 0.5f,
                    driveGripScale: 1.5f));
        }
    }
}
