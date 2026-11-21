namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// PF.2 stuck detector for the player's escape respawn: a deterministic, engine-free timer that
    /// fires once when the car has been unable to move while the pedal was asking for motion for a
    /// continuous <c>stuckSeconds</c> window. Any real movement (speed at or above the limit), any
    /// release of the demand, or a <see cref="Reset"/> restarts the count, so a car that briefly
    /// rocks free never earns a teleport by averaging across attempts.
    ///
    /// The armed guard is the respawn's cooldown: the wiring disarms the clock for the whole ghost
    /// window after a respawn and re-arms it afterwards, so the car cannot chain respawns while it
    /// is still being made solid again. A disarm clears the banked count; re-arming starts fresh.
    ///
    /// The caller supplies the demand: the pedal readout is compared against
    /// <see cref="DemandThrottle"/> (a resolved pedal at or above it counts as "asking for motion"),
    /// because that mapping is the wiring's job and the threshold belongs next to the clock it
    /// guards. Deliberately pure: no Unity API, no allocation, so the fixed tick can call it and the
    /// engine-free harness can test it.
    /// </summary>
    public sealed class StuckRespawnClock
    {
        /// <summary>
        /// The resolved pedal at or above which the car counts as demanding motion. A floor rather
        /// than any input at all, because a thumb resting on the stick's dead-zone shoulder is not
        /// an escape request.
        /// </summary>
        public const float DemandThrottle = 0.25f;

        private readonly float _stuckSeconds;
        private readonly float _speedLimit;
        private float _heldSeconds;
        private bool _due;
        private bool _armed;

        public StuckRespawnClock(float stuckSeconds, float speedLimit)
        {
            _stuckSeconds = stuckSeconds;
            _speedLimit = speedLimit;
            _armed = true;
        }

        /// <summary>False only while the ghost window owns the car; the wiring flips this, not the timer.</summary>
        public bool IsArmed => _armed;

        /// <summary>
        /// Arms or disarms the clock. Disarming clears the banked count and any latched due state,
        /// so a re-arm always starts a fresh window instead of firing on the ghost window's own
        /// stillness.
        /// </summary>
        public void SetArmed(bool armed)
        {
            _armed = armed;
            if (!armed)
            {
                _heldSeconds = 0f;
                _due = false;
            }
        }

        /// <summary>Clears the count, the latch and re-arms. Called by the wiring after a respawn and by tests.</summary>
        public void Reset()
        {
            // SetArmed(true) deliberately preserves state, so Reset clears it explicitly: a respawn
            // must hand the next window a genuinely fresh count.
            _heldSeconds = 0f;
            _due = false;
            _armed = true;
        }

        /// <summary>
        /// Feeds one tick. Returns true from the first tick where the continuous demand-and-immobility
        /// window has elapsed, and keeps returning true until <see cref="Reset"/> or
        /// <see cref="SetArmed"/> — the latch is what makes "exactly once" a property of the clock
        /// rather than of the caller's discipline.
        /// </summary>
        public bool Update(float deltaTime, float speedMetersPerSecond, bool throttleDemanded)
        {
            return Update(deltaTime, speedMetersPerSecond, throttleDemanded, false);
        }

        /// <summary>
        /// <see cref="Update(float, float, bool)"/> with a pause. While <paramref name="paused"/> the count
        /// neither grows nor clears: a car riding a moving carrier reads its speed against the carrier, so a
        /// car parked against the cab or crawling up the ramp looks "stuck at full pedal" when it is in fact
        /// exactly where the mode wants it (catch playtest 1).
        /// </summary>
        public bool Update(float deltaTime, float speedMetersPerSecond, bool throttleDemanded, bool paused)
        {
            if (!_armed)
                return false;

            if (paused)
                return _due;

            if (!_due)
            {
                // Strictly below the limit: at exactly the limit the car is moving, so a rolling
                // stop never counts as stuck.
                if (throttleDemanded && speedMetersPerSecond < _speedLimit)
                    _heldSeconds += deltaTime;
                else
                    _heldSeconds = 0f;

                _due = _heldSeconds >= _stuckSeconds;
            }

            return _due;
        }
    }

    /// <summary>
    /// Catch playtest 1: a flipped car never recovered. Three engine-free rules decide a flip, kept beside
    /// the stuck clock because both are the automatic ways a car is handed back to the player. Tilt is the
    /// angle between the car's up and world up, in degrees (0 upright, 180 on its roof).
    /// </summary>
    public static class FlipRecoveryRules
    {
        /// <summary>
        /// Past this tilt no drive, brake or reverse force is applied. The tilt, not wheel contact, is the
        /// gate: a car on its side aims its chassis-down rays sideways, they hit the floor and read as
        /// grounded, and the pedal then slides the car along on its roof — which also keeps resetting the
        /// self-right and stuck timers that need it at rest.
        /// </summary>
        public const float DriveCutoffTiltDegrees = 50f;

        /// <summary>Past this tilt the roof is lower than the wheels.</summary>
        public const float UpsideDownTiltDegrees = 90f;

        /// <summary>How long a car may stay upside down before it is respawned, whatever the pedal says.</summary>
        public const float UpsideDownRespawnSeconds = 3f;

        /// <summary>
        /// Margin of the self-right torque over the roof's tip moment (weight times half the track). At
        /// exactly 1 the torque only balances the car on its edge; 1.5 rolls it over with room for a
        /// cushioned roof or a slope.
        /// </summary>
        public const float SelfRightTorqueMargin = 1.5f;

        public static bool DriveAllowed(float tiltDegrees)
        {
            // NaN fails the comparison, which is the safe reading: no force from a car whose pose is bad.
            return tiltDegrees <= DriveCutoffTiltDegrees;
        }

        public static bool IsUpsideDown(float tiltDegrees)
        {
            return tiltDegrees > UpsideDownTiltDegrees;
        }

        /// <summary>
        /// The torque the self-right assist applies: the configured value, raised to
        /// <see cref="SelfRightTorqueMargin"/> times the tip moment m·|g|·halfWidth. Computed rather than
        /// tuned so a heavier car or a stronger gravity cannot quietly put the roof moment back above the
        /// assist (23 kN·m configured against 26.4 kN·m for 1100 kg at 30 m/s² and 0.8 m was the playtest
        /// bug). Bad inputs keep the configured value.
        /// </summary>
        public static float SelfRightTorque(float configuredNewtonMeters, float massKilograms,
            float gravityMetersPerSecondSquared, float halfWidthMeters)
        {
            float configured = IsFinite(configuredNewtonMeters) && configuredNewtonMeters > 0f
                ? configuredNewtonMeters
                : 0f;
            float floor = SelfRightTorqueMargin * massKilograms
                * HandlingMath.Abs(gravityMetersPerSecondSquared) * halfWidthMeters;
            if (!IsFinite(floor) || floor <= 0f)
                return configured;
            return floor > configured ? floor : configured;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// The guaranteed way back for a car on its roof: <see cref="FlipRecoveryRules.UpsideDownRespawnSeconds"/>
    /// of continuous upside-down time fires, regardless of the pedal and the speed, so a car sliding or
    /// rocking on its roof cannot dodge it the way it dodges the stuck clock. Latched until
    /// <see cref="Reset"/>, like the stuck clock. Pure and allocation-free.
    /// </summary>
    public sealed class UpsideDownRespawnClock
    {
        private readonly float _seconds;
        private float _heldSeconds;
        private bool _due;

        public UpsideDownRespawnClock()
            : this(FlipRecoveryRules.UpsideDownRespawnSeconds)
        {
        }

        public UpsideDownRespawnClock(float seconds)
        {
            _seconds = seconds;
        }

        /// <summary>Continuous upside-down seconds banked so far.</summary>
        public float HeldSeconds => _heldSeconds;

        public bool Update(float deltaTime, float tiltDegrees)
        {
            if (_due)
                return true;

            if (FlipRecoveryRules.IsUpsideDown(tiltDegrees) && deltaTime > 0f && !float.IsInfinity(deltaTime))
                _heldSeconds += deltaTime;
            else
                _heldSeconds = 0f;

            _due = _heldSeconds >= _seconds;
            return _due;
        }

        public void Reset()
        {
            _heldSeconds = 0f;
            _due = false;
        }
    }
}
