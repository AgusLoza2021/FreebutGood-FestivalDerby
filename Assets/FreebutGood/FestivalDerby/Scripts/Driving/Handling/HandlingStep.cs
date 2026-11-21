namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Everything the handling core needs to advance one car by one fixed tick. Plain floats only: the
    /// caller owns the suspension raycasts and the surface lookups, so the core stays engine-free and
    /// the same numbers flow on the client and on the server.
    /// </summary>
    public readonly struct HandlingInput
    {
        // Intent, raw. The low-pass lives in the mapper's state, not here.
        public readonly float StickX;
        public readonly float StickY;
        public readonly float CameraYawRadians;
        public readonly float CarYawRadians;

        /// <summary>Non-intent pedal, 0..1. Negative means "use the stick".</summary>
        public readonly float ThrottleOverride;

        // Buttons. Brake and reverse override the intent entirely, they are not part of it.
        public readonly bool Brake;
        public readonly bool Reverse;
        public readonly float NitroScale;

        /// <summary>
        /// CTRL1: which control scheme produces the pedal. Defaults to RingThrottle, the scheme whose
        /// pedal lives on the stick, so a caller written before CTRL1 (the Unity motor and its step
        /// loop) keeps the exact behaviour it had and cannot be left unmoving by a scheme that expects
        /// a GAS input nobody sends yet. The product default is ControlSchemeRules.Default; CTRL1b
        /// passes it here together with the gas value once the GAS button exists in the HUD.
        /// </summary>
        public readonly ControlScheme Scheme;

        /// <summary>CTRL1: the GAS input, 0..1. Only GasButton reads it; 0 or 1 from a button today.</summary>
        public readonly float Gas;

        // Car geometry and body state.
        public readonly float ForwardSpeed;
        public readonly float LateralSpeed;
        public readonly float YawRateRadiansPerSecond;
        public readonly float MassKilograms;
        public readonly float YawInertiaKilogramMetersSquared;
        public readonly float AxleFrontX;
        public readonly float AxleRearX;

        // Contact and surfaces, owned by the Unity adapter.
        public readonly float AxleLoadFrontNewton;
        public readonly float AxleLoadRearNewton;
        public readonly float SurfaceGripScale;
        public readonly float ZoneWheelGripScale;
        public readonly float ZoneTopSpeedScale;
        public readonly int GroundedWheels;

        public readonly float DeltaTime;
        public readonly HandlingParams Params;

        /// <summary>
        /// A grounded wheel stands on a body that reports its own motion (the CATCH carrier). Appended
        /// last with a default so every positional call site keeps its old behaviour; it only switches
        /// the auto-unstick off (see <see cref="IntentMapper"/>).
        /// </summary>
        public readonly bool OnMovingGround;

        public HandlingInput(
            float stickX,
            float stickY,
            float cameraYawRadians,
            float carYawRadians,
            float throttleOverride,
            bool brake,
            bool reverse,
            float nitroScale,
            float forwardSpeed,
            float lateralSpeed,
            float yawRateRadiansPerSecond,
            float massKilograms,
            float yawInertiaKilogramMetersSquared,
            float axleFrontX,
            float axleRearX,
            float axleLoadFrontNewton,
            float axleLoadRearNewton,
            float surfaceGripScale,
            float zoneWheelGripScale,
            float zoneTopSpeedScale,
            int groundedWheels,
            float deltaTime,
            in HandlingParams parameters,
            ControlScheme scheme = ControlScheme.RingThrottle,
            float gas = 0f,
            bool onMovingGround = false)
        {
            StickX = stickX;
            StickY = stickY;
            CameraYawRadians = cameraYawRadians;
            CarYawRadians = carYawRadians;
            ThrottleOverride = throttleOverride;
            Brake = brake;
            Reverse = reverse;
            NitroScale = nitroScale;
            ForwardSpeed = forwardSpeed;
            LateralSpeed = lateralSpeed;
            YawRateRadiansPerSecond = yawRateRadiansPerSecond;
            MassKilograms = massKilograms;
            YawInertiaKilogramMetersSquared = yawInertiaKilogramMetersSquared;
            AxleFrontX = axleFrontX;
            AxleRearX = axleRearX;
            AxleLoadFrontNewton = axleLoadFrontNewton;
            AxleLoadRearNewton = axleLoadRearNewton;
            SurfaceGripScale = surfaceGripScale;
            ZoneWheelGripScale = zoneWheelGripScale;
            ZoneTopSpeedScale = zoneTopSpeedScale;
            GroundedWheels = groundedWheels;
            DeltaTime = deltaTime;
            Params = parameters;
            Scheme = scheme;
            Gas = gas;
            OnMovingGround = onMovingGround;
        }
    }

    /// <summary>
    /// The arithmetic that has to survive from one tick to the next: the filtered stick, the turn
    /// commitment, the smoothed pedal and the wheel angle. Kept next to the car, handed back by ref.
    /// </summary>
    public struct HandlingState
    {
        public IntentState Intent;
        public float SmoothedThrottle;
        public float SteerAngleRadians;

        /// <summary>
        /// 0..1 brake-to-rotate engagement. Held at 1 while the brake is down and the car has heading
        /// to gather, then bled out over the tuning's fade, so releasing the brake does not hand the
        /// rear grip back in a single tick (FP0).
        /// </summary>
        public float BrakeRotateBlend;

        /// <summary>
        /// FEEL2 catch-up torque: seconds left of the post-impact peak, the severity that peaked it
        /// (0..1) and the flag a Unity contact hook arms through <see cref="NotifyImpact"/>. The window
        /// length lives in the tuning, not here, so the tick turns the flag into the timer.
        /// </summary>
        public float ImpactTimer;
        public float ImpactSeverity;
        public bool ImpactPending;

        /// <summary>
        /// Report a contact that cost the driver speed. <paramref name="severity"/> is 0..1 (a scrape
        /// is 0.2, a wall head-on is 1) and is clamped. The tick turns it into the tuning's impact
        /// window; calling it with the feature off is harmless.
        /// </summary>
        public void NotifyImpact(float severity)
        {
            if (severity <= 0f)
            {
                return;
            }

            if (severity > 1f)
            {
                severity = 1f;
            }

            if (severity > ImpactSeverity)
            {
                ImpactSeverity = severity;
            }

            ImpactPending = true;
        }

        public void Reset()
        {
            Intent.Reset();
            SmoothedThrottle = 0f;
            SteerAngleRadians = 0f;
            BrakeRotateBlend = 0f;
            ImpactTimer = 0f;
            ImpactSeverity = 0f;
            ImpactPending = false;
        }
    }

    /// <summary>
    /// Result of one tick: forces the caller applies to the rigid body, plus the debug surface the
    /// telemetry overlay and the acceptance tests read. The yaw torque here is the <em>assist</em>
    /// torque only; the moments the tyre forces make about the centre of mass belong to the caller,
    /// which applies each force at its own axle point. Splitting them this way is what keeps the
    /// adapter a dumb applier and makes the sim's yaw balance auditable.
    ///
    /// A readonly struct with every field set by <see cref="HandlingStep"/>: no allocation, no way to
    /// read a half-written tick.
    /// </summary>
    public readonly struct HandlingOutput
    {
        public readonly float Throttle;
        public readonly bool HasIntent;
        public readonly float TargetHeadingRadians;
        public readonly float HeadingErrorRadians;
        public readonly sbyte TurnSign;
        public readonly bool TurnCommitted;
        public readonly float UTurnFactor;

        public readonly float SteerAngleRadians;
        public readonly float SteerCommandRadians;
        public readonly float SteerMaxRadians;
        public readonly float YawRateTargetRadiansPerSecond;
        public readonly float YawRateMaxRadiansPerSecond;

        public readonly float DriveForceNewton;
        public readonly float LongitudinalForceNewton;
        public readonly float ThrottleSmoothed;
        public readonly float DriveCurveFactor;

        public readonly float FrontForceXNewton;
        public readonly float FrontForceYNewton;
        public readonly float RearForceXNewton;
        public readonly float RearForceYNewton;
        public readonly float SlipRatioFront;
        public readonly float SlipRatioRear;
        public readonly float MuFront;
        public readonly float MuRear;

        public readonly float AssistTorqueNewtonMeters;
        public readonly float YawDampingTorqueNewtonMeters;
        public readonly float SideslipTorqueNewtonMeters;
        public readonly float TotalYawTorqueNewtonMeters;
        public readonly float AssistFade;
        public readonly float BetaRadians;
        public readonly float BetaMaxRadians;
        public readonly float BetaExcessRadians;
        public readonly float RearMuBoost;

        /// <summary>
        /// Multiplier applied to the rear grip this tick: 1 when brake-to-rotate is idle, down to
        /// (1 - drop) while the brake is held and the car has heading to gather (FP0).
        /// </summary>
        public readonly float BrakeRotateFactor;

        /// <summary>
        /// CTRL1: true while the AutoAccelerate auto-unstick is pulsing reverse. The steering and
        /// longitudinal force already include the pulse; this is for telemetry and the tests.
        /// </summary>
        public readonly bool AutoReverse;

        /// <summary>
        /// FEEL2: drive demand over the rear axle's grip budget, 0..1 (saturating at 1). The Unity
        /// layer raises rear-wheel smoke from this; the mechanic itself is the clamp that produced it.
        /// 0 whenever the car is airborne, the feature is off or nobody is asking for drive.
        /// </summary>
        public readonly float RearWheelspin01;

        public HandlingOutput(
            float throttle,
            bool hasIntent,
            float targetHeadingRadians,
            float headingErrorRadians,
            sbyte turnSign,
            bool turnCommitted,
            float uTurnFactor,
            float steerAngleRadians,
            float steerCommandRadians,
            float steerMaxRadians,
            float yawRateTargetRadiansPerSecond,
            float yawRateMaxRadiansPerSecond,
            float driveForceNewton,
            float longitudinalForceNewton,
            float throttleSmoothed,
            float driveCurveFactor,
            float frontForceXNewton,
            float frontForceYNewton,
            float rearForceXNewton,
            float rearForceYNewton,
            float slipRatioFront,
            float slipRatioRear,
            float muFront,
            float muRear,
            float assistTorqueNewtonMeters,
            float yawDampingTorqueNewtonMeters,
            float sideslipTorqueNewtonMeters,
            float totalYawTorqueNewtonMeters,
            float assistFade,
            float betaRadians,
            float betaMaxRadians,
            float betaExcessRadians,
            float rearMuBoost,
            float brakeRotateFactor,
            bool autoReverse,
            float rearWheelspin01)
        {
            Throttle = throttle;
            HasIntent = hasIntent;
            TargetHeadingRadians = targetHeadingRadians;
            HeadingErrorRadians = headingErrorRadians;
            TurnSign = turnSign;
            TurnCommitted = turnCommitted;
            UTurnFactor = uTurnFactor;
            SteerAngleRadians = steerAngleRadians;
            SteerCommandRadians = steerCommandRadians;
            SteerMaxRadians = steerMaxRadians;
            YawRateTargetRadiansPerSecond = yawRateTargetRadiansPerSecond;
            YawRateMaxRadiansPerSecond = yawRateMaxRadiansPerSecond;
            DriveForceNewton = driveForceNewton;
            LongitudinalForceNewton = longitudinalForceNewton;
            ThrottleSmoothed = throttleSmoothed;
            DriveCurveFactor = driveCurveFactor;
            FrontForceXNewton = frontForceXNewton;
            FrontForceYNewton = frontForceYNewton;
            RearForceXNewton = rearForceXNewton;
            RearForceYNewton = rearForceYNewton;
            SlipRatioFront = slipRatioFront;
            SlipRatioRear = slipRatioRear;
            MuFront = muFront;
            MuRear = muRear;
            AssistTorqueNewtonMeters = assistTorqueNewtonMeters;
            YawDampingTorqueNewtonMeters = yawDampingTorqueNewtonMeters;
            SideslipTorqueNewtonMeters = sideslipTorqueNewtonMeters;
            TotalYawTorqueNewtonMeters = totalYawTorqueNewtonMeters;
            AssistFade = assistFade;
            BetaRadians = betaRadians;
            BetaMaxRadians = betaMaxRadians;
            BetaExcessRadians = betaExcessRadians;
            RearMuBoost = rearMuBoost;
            BrakeRotateFactor = brakeRotateFactor;
            AutoReverse = autoReverse;
            RearWheelspin01 = rearWheelspin01;
        }
    }

    /// <summary>
    /// One fixed tick of the V2 handling model, in order: intent, pedal, wheel angle, sideslip guard,
    /// per-axle grip, yaw assist.
    ///
    /// The order is not arbitrary. The guard has to run before the rear axle, because its grip boost is
    /// part of that axle's force. The assist has to run after the axles, because the fade that keeps it
    /// out of a slide is a function of the rear slip ratio the axles just produced.
    /// </summary>
    public static class HandlingStep
    {
        public static HandlingOutput Step(in HandlingInput input, ref HandlingState state)
        {
            // A ref readonly local, not a copy: HandlingParams has grown past fifty floats and this is the
            // tick path, so the whole tuning table would otherwise be memcpy'd once per car per tick.
            ref readonly HandlingParams p = ref input.Params;
            bool grounded = input.GroundedWheels > 0;

            IntentOutput intent = IntentMapper.Evaluate(
                input.StickX,
                input.StickY,
                input.CameraYawRadians,
                input.CarYawRadians,
                input.YawRateRadiansPerSecond,
                input.DeltaTime,
                input.ForwardSpeed,
                input.Scheme,
                input.Gas,
                input.OnMovingGround,
                p,
                ref state.Intent);

            bool buttonsOverride = input.Brake || input.Reverse;
            float throttle = buttonsOverride
                ? 0f
                : (input.ThrottleOverride >= 0f ? input.ThrottleOverride : intent.Throttle);

            // CTRL1: while the auto-unstick pulses reverse the pedal is not real, so report a lifted
            // throttle with it. The longitudinal model's reverse branch wins over the pedal anyway.
            if (intent.AutoReverse)
            {
                throttle = 0f;
            }

            float driveCurve;
            float resistanceForce;

            // --- FEEL2 catch-up torque ------------------------------------------
            // Below a fraction of top speed a driven, grounded car gets extra drive, and a recent
            // NotifyImpact lifts it to the peak for a short window. The factor is computed before the
            // model runs so the surge is part of the force the rear-axle clamp below will judge: the
            // clamp is what decides how much of it is acceleration and how much is wheelspin.
            CatchUpTuning catchUp = p.CatchUp;
            float impactSeverity = 0f;
            if (catchUp.IsEnabled)
            {
                if (state.ImpactPending)
                {
                    state.ImpactTimer = catchUp.ImpactWindowSeconds;
                    state.ImpactPending = false;
                }

                if (state.ImpactTimer > 0f)
                {
                    state.ImpactTimer -= input.DeltaTime;
                    if (state.ImpactTimer < 0f)
                    {
                        state.ImpactTimer = 0f;
                    }

                    impactSeverity = state.ImpactSeverity;
                }
                else
                {
                    state.ImpactSeverity = 0f;
                }
            }

            // Forward speed only: a car rolling backwards is not recovering a crash.
            float catchUpSpeedFraction = input.ForwardSpeed > 0f
                ? HandlingMath.Clamp01(input.ForwardSpeed / p.TopSpeedMetersPerSecond)
                : 0f;
            float catchUpFactor = catchUp.FactorFor(catchUpSpeedFraction, throttle, impactSeverity);

            float longitudinalForce = LongitudinalModel.Evaluate(
                throttle,
                input.ForwardSpeed,
                input.DeltaTime,
                input.Brake,
                input.Reverse || intent.AutoReverse,
                input.NitroScale,
                input.ZoneTopSpeedScale,
                input.GroundedWheels,
                input.MassKilograms,
                p,
                ref state.SmoothedThrottle,
                catchUpFactor,
                out driveCurve,
                out resistanceForce);

            // --- brake-to-rotate (FP0) ------------------------------------------
            // Held down the brake while the nose still has heading to gather: the rear grip is
            // scaled down for the tick and the sideslip cap lifted, so the car rotates on the brake
            // instead of understeering wide. The blend bleeds out over the fade so releasing the
            // brake does not snap the rear back in one tick. Forward speed only: a brake below
            // MinSpeed is a stop, and a car rolling backwards never satisfies it, so this cannot
            // turn into a reverse aid. Reverse is excluded outright: a reverse manoeuvre is not a
            // corner, and rotating the car mid-reverse reads as the game steering itself.
            BrakeRotateTuning brakeRotate = p.BrakeRotate;
            float brakeRotateBlend = state.BrakeRotateBlend;
            if (brakeRotate.IsEnabled
                && grounded
                && input.Brake
                && !input.Reverse
                && input.ForwardSpeed > brakeRotate.MinSpeedMetersPerSecond
                && HandlingMath.Abs(intent.ErrorRadians) > brakeRotate.MinErrorRadians)
            {
                brakeRotateBlend = 1f;
            }
            else if (brakeRotate.FadeSeconds > 0f)
            {
                brakeRotateBlend = HandlingMath.MoveTowards(
                    brakeRotateBlend,
                    0f,
                    input.DeltaTime / brakeRotate.FadeSeconds);
            }
            else
            {
                brakeRotateBlend = 0f;
            }

            state.BrakeRotateBlend = brakeRotateBlend;
            float brakeRotateFactor = 1f - brakeRotate.RearGripDrop * brakeRotateBlend;
            float betaMaxRadians = p.BetaMaxRadians + brakeRotate.BetaBoostRadians * brakeRotateBlend;

            // --- wheel angle ----------------------------------------------------
            float steerMax;
            float yawTarget;
            float steerCommand;
            if (grounded)
            {
                state.SteerAngleRadians = SteeringModel.Evaluate(
                    intent.ErrorRadians,
                    input.ForwardSpeed,
                    input.YawRateRadiansPerSecond,
                    state.SteerAngleRadians,
                    input.DeltaTime,
                    p,
                    out yawTarget,
                    out steerMax,
                    out steerCommand);
            }
            else
            {
                steerMax = SteeringModel.SteerMaxFor(input.ForwardSpeed, p);
                yawTarget = 0f;
                steerCommand = state.SteerAngleRadians;
            }

            // --- sideslip guard -------------------------------------------------
            float beta;
            float betaExcess;
            float rearMuBoost;
            float sideslipTorque = grounded
                ? SideslipGuard.Evaluate(
                    input.ForwardSpeed,
                    input.LateralSpeed,
                    input.YawInertiaKilogramMetersSquared,
                    input.GroundedWheels,
                    betaMaxRadians,
                    p,
                    out beta,
                    out betaExcess,
                    out rearMuBoost)
                : SideslipGuard.Evaluate(
                    input.ForwardSpeed,
                    input.LateralSpeed,
                    input.YawInertiaKilogramMetersSquared,
                    0,
                    betaMaxRadians,
                    p,
                    out beta,
                    out betaExcess,
                    out rearMuBoost);

            // --- per-axle grip --------------------------------------------------
            float frontForceX;
            float frontForceY;
            float frontSlip;
            float frontMu;
            float rearForceX;
            float rearForceY;
            float rearSlip;
            float rearMu;

            if (grounded)
            {
                float grip = input.SurfaceGripScale * input.ZoneWheelGripScale;
                LateralModel.Evaluate(
                    input.ForwardSpeed,
                    input.LateralSpeed,
                    input.YawRateRadiansPerSecond,
                    input.AxleFrontX,
                    state.SteerAngleRadians,
                    input.AxleLoadFrontNewton,
                    grip,
                    input.DeltaTime,
                    p.FrontAxleCurve,
                    p,
                    out frontForceX,
                    out frontForceY,
                    out frontSlip,
                    out frontMu);

                LateralModel.Evaluate(
                    input.ForwardSpeed,
                    input.LateralSpeed,
                    input.YawRateRadiansPerSecond,
                    input.AxleRearX,
                    input.AxleLoadRearNewton,
                    grip * rearMuBoost * brakeRotateFactor,
                    input.DeltaTime,
                    p.RearAxleCurve,
                    p,
                    out rearForceX,
                    out rearForceY,
                    out rearSlip,
                    out rearMu);
            }
            else
            {
                frontForceX = 0f;
                frontForceY = 0f;
                rearForceX = 0f;
                rearForceY = 0f;
                frontSlip = 0f;
                rearSlip = 0f;
                frontMu = 0f;
                rearMu = 0f;
            }

            // --- FEEL2 drive grip clamp ------------------------------------------
            // The catch-up surge is torque at the wheel, not acceleration: what the rear tyre can
            // actually put down is its own mu * N (the same cap the lateral model uses), scaled by
            // DriveGripScale. Eat the surplus here and report it as wheelspin so the Unity layer can
            // raise smoke, instead of letting extra torque bend the car's numbers on paper. A lifted
            // pedal or a braking car keeps the resistance it asked for.
            float appliedLongitudinal = longitudinalForce;
            float rearWheelspin01 = 0f;
            if (grounded && catchUp.IsEnabled && catchUp.DriveGripScale > 0f)
            {
                float driveDemand = longitudinalForce - resistanceForce;
                if (driveDemand > 0f)
                {
                    float gripCap = p.MuPeak * rearMu * input.AxleLoadRearNewton * catchUp.DriveGripScale;
                    if (gripCap > 0f)
                    {
                        rearWheelspin01 = HandlingMath.Clamp01(driveDemand / gripCap);
                        if (driveDemand > gripCap)
                        {
                            appliedLongitudinal = gripCap + resistanceForce;
                        }
                    }
                }
            }

            // --- yaw assist -----------------------------------------------------
            float assistFade;
            float assistTorque;
            float dampingTorque;
            if (grounded)
            {
                YawStabilizer.Evaluate(
                    input.ForwardSpeed,
                    input.YawRateRadiansPerSecond,
                    yawTarget,
                    rearSlip,
                    input.YawInertiaKilogramMetersSquared,
                    p,
                    out assistFade,
                    out assistTorque,
                    out dampingTorque);
            }
            else
            {
                assistFade = 0f;
                assistTorque = 0f;
                dampingTorque = 0f;
            }

            return new HandlingOutput(
                throttle: throttle,
                hasIntent: intent.HasIntent,
                targetHeadingRadians: intent.TargetHeadingRadians,
                headingErrorRadians: intent.ErrorRadians,
                turnSign: intent.TurnSign,
                turnCommitted: intent.Committed,
                uTurnFactor: intent.UTurnFactor,
                steerAngleRadians: state.SteerAngleRadians,
                steerCommandRadians: steerCommand,
                steerMaxRadians: steerMax,
                yawRateTargetRadiansPerSecond: yawTarget,
                yawRateMaxRadiansPerSecond: HandlingMath.Min(
                    p.MaxYawRateRadiansPerSecond,
                    p.LateralAccelBudgetMetersPerSecondSquared
                        / HandlingMath.Max(input.ForwardSpeed, p.MinReferenceSpeedMetersPerSecond)),
                driveForceNewton: appliedLongitudinal - resistanceForce,
                longitudinalForceNewton: appliedLongitudinal,
                throttleSmoothed: state.SmoothedThrottle,
                driveCurveFactor: driveCurve,
                frontForceXNewton: frontForceX,
                frontForceYNewton: frontForceY,
                rearForceXNewton: rearForceX,
                rearForceYNewton: rearForceY,
                slipRatioFront: frontSlip,
                slipRatioRear: rearSlip,
                muFront: frontMu,
                muRear: rearMu,
                assistTorqueNewtonMeters: assistTorque,
                yawDampingTorqueNewtonMeters: dampingTorque,
                sideslipTorqueNewtonMeters: sideslipTorque,
                totalYawTorqueNewtonMeters: assistTorque + dampingTorque + sideslipTorque,
                assistFade: assistFade,
                betaRadians: beta,
                betaMaxRadians: betaMaxRadians,
                betaExcessRadians: betaExcess,
                rearMuBoost: rearMuBoost,
                brakeRotateFactor: brakeRotateFactor,
                autoReverse: intent.AutoReverse,
                rearWheelspin01: rearWheelspin01);
        }
    }
}
