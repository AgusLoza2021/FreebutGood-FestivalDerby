namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// Per-car memory of the intent mapper: the filtered stick and the turn commitment. It is a plain
    /// struct so the caller (the sim, the Unity adapter, the server) can keep it inline next to the
    /// car and hand it back by ref every tick without an allocation.
    /// </summary>
    public struct IntentState
    {
        /// <summary>Low-passed stick vector. Filtering the vector keeps a sweep through the rear smooth.</summary>
        public float StickX;
        public float StickY;

        /// <summary>+1 or -1 while committed, 0 when free. Held, not recomputed, is the whole point.</summary>
        public sbyte TurnSign;

        /// <summary>1 while the turn direction is committed, 0 while free.</summary>
        public byte Locked;

        /// <summary>1 while the U-turn throttle lift is held, 0 otherwise.</summary>
        public byte Lifted;

        /// <summary>CTRL1 auto-unstick: seconds the car has been stuck at full pedal.</summary>
        public float UnstickSeconds;

        /// <summary>CTRL1 auto-unstick: seconds left on the reverse pulse, 0 when idle.</summary>
        public float ReversePulseSeconds;

        public void Reset()
        {
            StickX = 0f;
            StickY = 0f;
            TurnSign = 0;
            Locked = 0;
            Lifted = 0;
            UnstickSeconds = 0f;
            ReversePulseSeconds = 0f;
        }
    }

    /// <summary>What the intent mapper decided this tick. A value type: it is written every tick.</summary>
    public readonly struct IntentOutput
    {
        /// <summary>0..1 pedal request after the ramp shape and the U-turn lift. Never negative.</summary>
        public readonly float Throttle;

        /// <summary>World heading the stick is asking for, radians.</summary>
        public readonly float TargetHeadingRadians;

        /// <summary>
        /// Wrapped error target minus car. While the turn is committed this is forced onto the locked
        /// side, so a stick sitting near dead astern can no longer flip the requested turn direction.
        /// </summary>
        public readonly float ErrorRadians;

        /// <summary>0..1 how much of a U-turn the error represents (drives the throttle lift).</summary>
        public readonly float UTurnFactor;

        /// <summary>True when the stick is outside the dead zone, so there is a heading request at all.</summary>
        public readonly bool HasIntent;

        /// <summary>+1 / -1 turn direction, meaningful only while true is passed for committed.</summary>
        public readonly sbyte TurnSign;

        public readonly bool Committed;

        /// <summary>Filtered stick magnitude, for telemetry.</summary>
        public readonly float Magnitude;

        /// <summary>
        /// CTRL1: true while the AutoAccelerate auto-unstick is asking for reverse. The caller ORs it
        /// into the reverse button; it is reported here so telemetry and the acceptance rig can see
        /// the pulse without guessing from the speed.
        /// </summary>
        public readonly bool AutoReverse;

        public IntentOutput(
            float throttle,
            float targetHeadingRadians,
            float errorRadians,
            float uTurnFactor,
            bool hasIntent,
            sbyte turnSign,
            bool committed,
            float magnitude,
            bool autoReverse)
        {
            Throttle = throttle;
            TargetHeadingRadians = targetHeadingRadians;
            ErrorRadians = errorRadians;
            UTurnFactor = uTurnFactor;
            HasIntent = hasIntent;
            TurnSign = turnSign;
            Committed = committed;
            Magnitude = magnitude;
            AutoReverse = autoReverse;
        }
    }

    /// <summary>
    /// Turns the raw stick into a forward pedal and a heading request. This is where the owner's
    /// "down-left loses control" bug is fixed, and the fix is three rules:
    ///
    /// 1. The pedal is a function of stick magnitude only and can never go negative. Past the ring
    ///    there is no gear to reverse into; the old mapping sent a stick more than 120 degrees off the
    ///    nose to a 25 m/s^2 brake, and that brake is what "lost control" meant.
    /// 2. Turn direction is committed by hysteresis: lock past 150 degrees of error, release under
    ///    110. The chase camera keeps a fixed bearing, so with the stick near dead astern the wrapped
    ///    error jumps between +180 and -180 every frame; without the lock the controller answers a
    ///    left request and a right request on alternating ticks and the car spins.
    /// 3. Past 100 degrees the throttle lifts towards UTurnThrottleScale. A lift tightens the radius
    ///    (less speed, same grip) without ever asking for reverse.
    ///
    /// CTRL1 adds a selectable throttle source without touching any of the three rules above, so the
    /// three schemes are the same car with a different pedal (see <see cref="ControlScheme"/>).
    /// </summary>
    public static class IntentMapper
    {
        public static IntentOutput Evaluate(
            float rawStickX,
            float rawStickY,
            float cameraYawRadians,
            float carYawRadians,
            float yawRateRadiansPerSecond,
            float deltaTime,
            in HandlingParams p,
            ref IntentState state)
        {
            return Evaluate(
                rawStickX,
                rawStickY,
                cameraYawRadians,
                carYawRadians,
                yawRateRadiansPerSecond,
                deltaTime,
                0f,
                p,
                ref state);
        }

        /// <summary>
        /// Evaluate with the car's forward speed. The speed is only used by the FP0 straight-line
        /// dead band, whose width grows with speed; the older overload above passes 0 so its callers
        /// keep the historic behaviour (band off) unchanged.
        /// </summary>
        public static IntentOutput Evaluate(
            float rawStickX,
            float rawStickY,
            float cameraYawRadians,
            float carYawRadians,
            float yawRateRadiansPerSecond,
            float deltaTime,
            float forwardSpeedMetersPerSecond,
            in HandlingParams p,
            ref IntentState state)
        {
            return Evaluate(
                rawStickX,
                rawStickY,
                cameraYawRadians,
                carYawRadians,
                yawRateRadiansPerSecond,
                deltaTime,
                forwardSpeedMetersPerSecond,
                ControlScheme.RingThrottle,
                0f,
                p,
                ref state);
        }

        /// <summary>
        /// Evaluate with the CTRL1 control scheme and the gas input. The scheme only changes where the
        /// pedal comes from; the heading request, the turn commitment, the straight-line dead band and
        /// the U-turn lift are shared, which is what keeps the three schemes an A/B of one car instead
        /// of three different cars. gas is a 0..1 pedal (the GAS button writes 0 or 1; an analog
        /// trigger can write the middle) and only GasButton reads it.
        /// </summary>
        public static IntentOutput Evaluate(
            float rawStickX,
            float rawStickY,
            float cameraYawRadians,
            float carYawRadians,
            float yawRateRadiansPerSecond,
            float deltaTime,
            float forwardSpeedMetersPerSecond,
            ControlScheme scheme,
            float gas,
            in HandlingParams p,
            ref IntentState state)
        {
            return Evaluate(
                rawStickX,
                rawStickY,
                cameraYawRadians,
                carYawRadians,
                yawRateRadiansPerSecond,
                deltaTime,
                forwardSpeedMetersPerSecond,
                scheme,
                gas,
                false,
                p,
                ref state);
        }

        /// <summary>
        /// Evaluate for a car that may be standing on a moving body (the CATCH carrier). On moving ground
        /// the forward speed is measured against the ground, so a car pinned against the cab or crawling up
        /// the ramp reads as "full pedal, not moving" — and the auto-unstick's reverse pulse would throw it
        /// off the very bed it is trying to hold. There the unstick neither counts nor pulses, and a pulse
        /// already running is cancelled.
        /// </summary>
        public static IntentOutput Evaluate(
            float rawStickX,
            float rawStickY,
            float cameraYawRadians,
            float carYawRadians,
            float yawRateRadiansPerSecond,
            float deltaTime,
            float forwardSpeedMetersPerSecond,
            ControlScheme scheme,
            float gas,
            bool onMovingGround,
            in HandlingParams p,
            ref IntentState state)
        {
            if (onMovingGround)
            {
                state.UnstickSeconds = 0f;
                state.ReversePulseSeconds = 0f;
            }

            // Exponential low-pass on the vector. dt/(dt+tau) is the discrete step of a first order
            // filter and stays exact at any fixed rate the caller picks.
            float blend = deltaTime / (deltaTime + p.StickLowPassSeconds);
            state.StickX += (rawStickX - state.StickX) * blend;
            state.StickY += (rawStickY - state.StickY) * blend;

            float cx = state.StickX;
            float cy = state.StickY;
            float magnitude = (float)System.Math.Sqrt(cx * cx + cy * cy);

            if (magnitude < p.DeadZone)
            {
                // No intent: the stick is not asking for anything, so there is no heading to steer to.
                // The pedal now depends on the scheme: GasButton still obeys the GAS input with the
                // stick centred (the "hold gas and drive straight" case), AutoAccelerate coasts
                // because its throttle lived on the stick, and RingThrottle keeps the shipped release
                // rule. Releasing the stick must never keep feeding the engine.
                float idleThrottle = scheme == ControlScheme.GasButton ? HandlingMath.Clamp01(gas) : 0f;
                return new IntentOutput(
                    throttle: idleThrottle,
                    targetHeadingRadians: carYawRadians,
                    errorRadians: 0f,
                    uTurnFactor: 0f,
                    hasIntent: false,
                    turnSign: state.TurnSign,
                    committed: state.Locked != 0,
                    magnitude: magnitude,
                    autoReverse: false);
            }

            // Pedal. RingThrottle reads stick magnitude past the dead zone (zero at the edge, full at
            // the ring, so partial stick is partial pedal). GasButton ignores the magnitude: past the
            // dead zone the stick is a direction and the GAS input is the pedal, so a barely-out stick
            // with gas held still means full throttle. AutoAccelerate turns the throttle on with the
            // direction itself (Death Rally), coasting only when the stick is released.
            float throttle;
            if (scheme == ControlScheme.GasButton)
            {
                throttle = HandlingMath.Clamp01(gas);
            }
            else if (scheme == ControlScheme.AutoAccelerate)
            {
                throttle = 1f;
            }
            else
            {
                float span = 1f - p.DeadZone;
                float shaped = span > 0f ? HandlingMath.Clamp01((magnitude - p.DeadZone) / span) : 1f;
                float curve = p.ThrottleResponseCurve.Evaluate(shaped);
                throttle = HandlingMath.Clamp01(
                    HandlingMath.Lerp(p.ThrottleFloor, p.ThrottleAtRing, curve));
            }

            // The stick is a direction on the screen, and the camera up axis is the car's forward at
            // camera yaw. Screen right is a clockwise (negative) yaw turn in the CCW-positive frame.
            float stickAngleRadians = (float)System.Math.Atan2(cx, cy);
            float targetHeading = HandlingMath.WrapPi(cameraYawRadians - stickAngleRadians);
            float error = HandlingMath.WrapPi(targetHeading - carYawRadians);
            float absoluteError = HandlingMath.Abs(error);

            // --- turn commitment ------------------------------------------------
            if (state.Locked != 0)
            {
                if (absoluteError < p.CommitExitRadians)
                {
                    state.Locked = 0;
                    state.TurnSign = 0;
                }
            }
            else if (absoluteError > p.CommitEnterRadians)
            {
                state.Locked = 1;
                // Exactly astern the error has no sign of its own; fall back on the yaw rate so the
                // commitment still matches the way the car is actually rotating.
                if (absoluteError > HandlingMath.Pi - 0.001f && HandlingMath.Abs(yawRateRadiansPerSecond) > 0.0001f)
                {
                    state.TurnSign = yawRateRadiansPerSecond >= 0f ? (sbyte)1 : (sbyte)-1;
                }
                else
                {
                    state.TurnSign = error >= 0f ? (sbyte)1 : (sbyte)-1;
                }
            }

            if (state.Locked != 0)
            {
                error = state.TurnSign * absoluteError;
            }

            // --- straight-line dead band (FP0) ----------------------------------
            // At speed a stick bearing within a few degrees of the nose is thumb noise, not a request.
            // Zeroing it (rather than chasing it) is what stops the wheel twitching on a straight; the
            // band widens with speed and is exactly zero at a standstill, where a small stick angle is
            // a real request for a tight manoeuvre. A 1.5x ramp outside the band means the request
            // fades in rather than jumping at the edge.
            StraightDeadBandTuning deadBand = p.StraightDeadBand;
            if (deadBand.IsEnabled
                && state.Locked == 0
                && deadBand.FullSpeedMetersPerSecond > deadBand.MinSpeedMetersPerSecond)
            {
                float bandRadians = deadBand.Radians * HandlingMath.Smoothstep(
                    deadBand.MinSpeedMetersPerSecond,
                    deadBand.FullSpeedMetersPerSecond,
                    HandlingMath.Abs(forwardSpeedMetersPerSecond));

                if (absoluteError < bandRadians)
                {
                    error = 0f;
                    absoluteError = 0f;
                }
                else if (absoluteError < bandRadians * 1.5f)
                {
                    float scale = HandlingMath.Smoothstep(bandRadians, bandRadians * 1.5f, absoluteError);
                    error *= scale;
                    absoluteError *= scale;
                }
            }

            // --- U-turn lift ----------------------------------------------------
            float uTurn = HandlingMath.Smoothstep(p.UTurnStartRadians, p.UTurnFullRadians, absoluteError);

            // The ramp only shapes the entry to a U-turn; the lift itself is held until the car is
            // nearly on the target. A lift that fades with |e| lets the car accelerate through the
            // middle of the turn, and because yawMax falls as 1/v that turns a 180 degree arc into a
            // 5 second one. Holding the lift is the same hysteresis the turn commitment uses above.
            if (absoluteError > p.UTurnStartRadians)
            {
                state.Lifted = 1;
            }
            else if (absoluteError < p.UTurnReleaseRadians)
            {
                state.Lifted = 0;
            }

            if (state.Lifted != 0)
            {
                uTurn = 1f;
            }

            // Drive rescue S1/S3: the lift tightens fast arcs; at or below the floor speed it has no
            // rationale and only buries a car that is rotating out of a wall hit. The gate covers
            // both the smoothstep ramp and the held lift; the factor still reports the manoeuvre
            // so telemetry keeps seeing the U-turn.
            float liftGate = HandlingMath.Abs(forwardSpeedMetersPerSecond) > p.UTurnMinSpeedMetersPerSecond
                ? 1f
                : 0f;
            throttle *= HandlingMath.Lerp(1f, p.UTurnThrottleScale, uTurn * liftGate);

            // --- auto-unstick (drive rescue, S2) ----------------------------------
            // A wedged car (full pedal, no movement) earns a short reverse pulse under any pedal
            // source, not only AutoAccelerate: the heading loop cannot rotate a car pinned nose-first
            // into a wall, and RingThrottle never asks for reverse on its own. A full second of
            // full pedal without moving buys the pulse (references recommendation 11). The pulse
            // runs to its end even if the player lets go mid-way, so the car always clears the
            // wall. An explicit tuning Off keeps every scheme hands-off.
            bool autoReverse = false;
            AutoUnstickTuning unstick = p.AutoUnstick;
            if (unstick.IsEnabled && !onMovingGround)
            {
                if (state.ReversePulseSeconds > 0f)
                {
                    state.ReversePulseSeconds = HandlingMath.Max(0f, state.ReversePulseSeconds - deltaTime);
                    autoReverse = true;
                }
                else if (throttle > unstick.TriggerPedal
                    && HandlingMath.Abs(forwardSpeedMetersPerSecond) < unstick.TriggerSpeedMetersPerSecond)
                {
                    // Speed is a magnitude, not a signed value: a car already rolling backwards off the
                    // wall is moving, and counting that as "still stuck" would chain pulse after pulse
                    // into a permanent reverse instead of one nudge at a time.
                    state.UnstickSeconds += deltaTime;
                    if (state.UnstickSeconds >= unstick.HoldSeconds)
                    {
                        state.UnstickSeconds = 0f;
                        state.ReversePulseSeconds = unstick.PulseSeconds - deltaTime;
                        autoReverse = unstick.PulseSeconds > 0f;
                    }
                }
                else
                {
                    state.UnstickSeconds = 0f;
                }
            }

            return new IntentOutput(
                throttle: HandlingMath.Clamp01(throttle),
                targetHeadingRadians: targetHeading,
                errorRadians: error,
                uTurnFactor: uTurn,
                hasIntent: true,
                turnSign: state.TurnSign,
                committed: state.Locked != 0,
                magnitude: magnitude,
                autoReverse: autoReverse);
        }

    }
}
