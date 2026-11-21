using FestivalDerby.Driving.Handling;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The player's way back onto the circuit, in one place: the PF.2 stuck escape, the HUD's hold-to-reset
    /// and the fall below the track all end in the same <see cref="CircuitRespawn"/> placement, ramp lift
    /// included. Allocation-free per frame.
    /// </summary>
    public sealed class PlayerRespawn
    {
        private readonly Rigidbody _body;
        private readonly OwnedGrayboxCircuit _circuit;
        private readonly CarGhostMonitor _monitor;
        private readonly float _killPlaneY;
        private readonly UpsideDownRespawnClock _upsideDown = new UpsideDownRespawnClock();

        /// <param name="monitor">
        /// The stuck clock and ghost window, or null for a car with neither: then only a forced reset or a
        /// fall respawns it, and it arrives solid.
        /// </param>
        public PlayerRespawn(Rigidbody body, OwnedGrayboxCircuit circuit, CarGhostMonitor monitor)
        {
            _body = body;
            _circuit = circuit;
            _monitor = monitor;
            _killPlaneY = CircuitRespawn.KillPlaneY(circuit);
        }

        /// <summary>Respawns performed since this object was built, whatever triggered them.</summary>
        public int RespawnCount { get; private set; }

        /// <summary>The height below which the car counts as fallen off the world.</summary>
        public float KillPlaneY
        {
            get { return _killPlaneY; }
        }

        /// <summary>
        /// One player frame. Respawns when the stuck clock fires, or at once when the car has fallen below
        /// the track: a car out there is airborne for good and never reads as stuck, so the fall is its own
        /// trigger, and it still takes the ghost window when one can start. True when it respawned.
        /// </summary>
        public bool Tick(float deltaTime, float speedMetersPerSecond, float throttle)
        {
            return Tick(deltaTime, speedMetersPerSecond, throttle, false);
        }

        /// <summary>
        /// <see cref="Tick(float, float, float)"/> for a car that may ride the CATCH carrier: on moving ground
        /// the stuck clock is held (its speed is carrier-relative). On top of the stuck and fall triggers, a
        /// car upside down for <see cref="FlipRecoveryRules.UpsideDownRespawnSeconds"/> is respawned whatever
        /// the pedal says, because a car rocking or sliding on its roof never reads as "stuck".
        /// </summary>
        public bool Tick(float deltaTime, float speedMetersPerSecond, float throttle, bool onMovingGround)
        {
            if (!CanRespawn())
                return false;

            bool due = _monitor != null && _monitor.Tick(deltaTime, speedMetersPerSecond, throttle, onMovingGround);
            if (!due && _body.position.y < _killPlaneY)
            {
                // A refused window (one is already running) must not leave the car falling forever.
                if (_monitor != null)
                    _monitor.RequestForcedRespawn();
                due = true;
            }

            // The latch holds while a ghost window refuses the request, so the flip respawn lands as soon as
            // the window ends instead of being lost.
            if (_upsideDown.Update(deltaTime, TiltDegrees) && !due)
                due = _monitor == null || _monitor.RequestForcedRespawn();

            if (!due)
                return false;

            Respawn();
            return true;
        }

        /// <summary>
        /// The player-facing reset. The monitor refuses while a ghost window is already running, so a
        /// repeated hold inside one window cannot chain teleports. True when it respawned.
        /// </summary>
        public bool RequestReset()
        {
            if (!CanRespawn())
                return false;

            if (_monitor != null && !_monitor.RequestForcedRespawn())
                return false;

            Respawn();
            return true;
        }

        private bool CanRespawn()
        {
            // A circuit with no route still has its grid, which is where the placement falls back to.
            return _body != null && _circuit != null && !_circuit.IsDisposed;
        }

        private void Respawn()
        {
            Pose pose = CircuitRespawn.Place(_circuit, _body.position);
            CircuitRespawn.Commit(_body, in pose);
            _upsideDown.Reset();
            RespawnCount++;
        }

        /// <summary>Angle between the car's up and world up, in degrees; 0 with no body.</summary>
        public float TiltDegrees
        {
            get { return _body != null ? Vector3.Angle(_body.transform.up, Vector3.up) : 0f; }
        }

        /// <summary>
        /// Tilted past the point where the tyres can drive it (see
        /// <see cref="FlipRecoveryRules.DriveCutoffTiltDegrees"/>): the HUD pulses its reset button then.
        /// </summary>
        public bool IsFlipped
        {
            get { return !FlipRecoveryRules.DriveAllowed(TiltDegrees); }
        }
    }
}
