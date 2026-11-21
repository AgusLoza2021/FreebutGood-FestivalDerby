using FestivalDerby.Driving.Handling;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// PF.2 player escape: composes the engine-free <see cref="StuckRespawnClock"/> with the
    /// <see cref="CarGhost"/> so the scene code (Assembly-CSharp) never has to name the Handling
    /// assembly, which is deliberately not auto-referenced. One Tick per player step is the whole
    /// surface: feed the pedal readout in, get exactly one true out when the respawn is due, and do
    /// only the teleport on it — the monitor owns everything else, including starting and ending
    /// the ghost window and disarming the clock for its whole duration so a still car cannot chain
    /// respawns while it is being made solid again.
    ///
    /// Every number arrives through the constructor, already resolved by the caller from the
    /// feature config's Resolve* sanitizers: this class reads no config, so its behaviour is fully
    /// determined at construction and the PlayMode tests can build it without an asset. Tick is
    /// allocation-free and safe on the hot tick; the ghost's own material work happens once per
    /// respawn and once per window end.
    /// </summary>
    public sealed class CarGhostMonitor
    {
        private readonly StuckRespawnClock _clock;
        private readonly CarGhost _ghost;
        private readonly float _ghostSeconds;
        private readonly float _ghostAlpha;
        // Seconds left in the current ghost window; zero means the car is solid and opaque.
        private float _ghostRemaining;

        public CarGhostMonitor(float stuckSeconds, float stuckSpeedLimit, float ghostSeconds,
            float ghostAlpha)
        {
            _clock = new StuckRespawnClock(stuckSeconds, stuckSpeedLimit);
            _ghost = new CarGhost();
            _ghostSeconds = ghostSeconds;
            _ghostAlpha = ghostAlpha;
        }

        /// <summary>
        /// Binds the car this monitor ghosts. Same degradation contract as the ghost: no visual
        /// root means a collider-only ghost, no colliders means a visual-only one.
        /// </summary>
        public void Bind(Transform visualRoot, Collider[] colliders)
        {
            _ghost.Bind(visualRoot, colliders);
        }

        /// <summary>True while the ghost window owns the car (colliders off, semi-transparent).</summary>
        public bool IsGhosted => _ghostRemaining > 0f;

        /// <summary>True while the stuck clock is accepting a new window; false during the ghost cooldown.</summary>
        public bool IsArmed => _clock.IsArmed;

        /// <summary>
        /// Feeds one player step. Returns true exactly once per respawn, on the tick where
        /// <c>stuckSeconds</c> of continuous throttle-demanded immobility (pedal at or above
        /// <see cref="StuckRespawnClock.DemandThrottle"/>, speed below the limit) has elapsed; the
        /// caller must then perform the teleport and nothing else. While a ghost window is running
        /// this returns false and the clock stays disarmed.
        /// </summary>
        public bool Tick(float deltaTime, float speedMetersPerSecond, float throttle)
        {
            return Tick(deltaTime, speedMetersPerSecond, throttle, false);
        }

        /// <summary>
        /// <see cref="Tick(float, float, float)"/> for a car that may ride a moving carrier: while
        /// <paramref name="onMovingGround"/> the stuck count is held, because a carrier-relative speed of zero
        /// is a car parked on the bed, not a car pinned against a wall.
        /// </summary>
        public bool Tick(float deltaTime, float speedMetersPerSecond, float throttle, bool onMovingGround)
        {
            if (_ghostRemaining > 0f)
            {
                _ghostRemaining -= deltaTime;
                if (_ghostRemaining <= 0f)
                {
                    _ghostRemaining = 0f;
                    // Window over: solid and opaque again, and a fresh stuck window from here.
                    _ghost.SetGhost(false, _ghostAlpha);
                    _clock.Reset();
                }

                return false;
            }

            if (_clock.Update(deltaTime, speedMetersPerSecond,
                    throttle >= StuckRespawnClock.DemandThrottle, onMovingGround))
            {
                // Latched due: take the window now and hold the clock disarmed for its length, so
                // the stillness of being teleported cannot immediately re-fire the escape.
                _clock.SetArmed(false);
                _ghostRemaining = _ghostSeconds;
                _ghost.SetGhost(true, _ghostAlpha);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Forces the same respawn the stuck clock would earn, on demand: starts the ghost window
        /// and disarms the clock exactly as the latched path does, and returns true so the caller
        /// performs the teleport this frame. Returns false (and touches nothing) while a ghost
        /// window already owns the car, so a repeated or concurrent request can never chain
        /// teleports or double-swap the ghost materials. The automatic clock timing is untouched:
        /// this only enters the state the latch would have entered.
        /// </summary>
        public bool RequestForcedRespawn()
        {
            if (_ghostRemaining > 0f)
                return false;

            _clock.SetArmed(false);
            _ghostRemaining = _ghostSeconds;
            _ghost.SetGhost(true, _ghostAlpha);
            return true;
        }

        /// <summary>Ends any active ghost and releases the ghost's cloned materials. Called at teardown.</summary>
        public void Teardown()
        {
            _ghostRemaining = 0f;
            _ghost.Teardown();
        }
    }
}
