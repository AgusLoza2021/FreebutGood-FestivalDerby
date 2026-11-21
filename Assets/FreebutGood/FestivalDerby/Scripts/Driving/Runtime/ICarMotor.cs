using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The surface every handling model behind <see cref="LocalCarFixedStepDriver"/> must offer: one step,
    /// the presentation reads the VFX and HUD want, and the authoritative writes that only the owner of a
    /// match may make.
    ///
    /// It exists so the driver can hold either model without a single call site knowing which one it got.
    /// That is the whole point of the V2b switch: the legacy <see cref="CarMotor"/> and
    /// <see cref="ArcadeCarMotor"/> are interchangeable behind this interface, and rolling back is a
    /// config flag rather than a revert.
    ///
    /// <see cref="Step"/> takes <see cref="CarInput"/> by value, exactly like the legacy motor, so
    /// implementing this adds no boxing and no allocation to the hot tick.
    /// </summary>
    public interface ICarMotor
    {
        /// <summary>Last control mode a step that actually ran resolved; Forward until the first such step.</summary>
        CarControlMode ResolvedMode { get; }

        /// <summary>Any wheel touching a surface in the last step. Read-only presentation view.</summary>
        bool IsGrounded { get; }

        /// <summary>True while the nitro boost still has time left. Read-only presentation view.</summary>
        bool IsBoosting { get; }

        /// <summary>Absolute lateral speed of the rear axle in m/s; drives drift smoke and skid marks.</summary>
        float RearSlipSpeed { get; }

        /// <summary>Applies one tick of handling forces; invalid state is a silent no-op.</summary>
        void Step(CarInput input, float deltaTime);

        /// <summary>
        /// The grip multiplier the surface under the car resolved to. Clamped to a sane band, idempotent,
        /// and written only by the authority that owns the surface query.
        /// </summary>
        void ApplySurfaceGrip(float multiplier);

        /// <summary>World pose of one wheel anchor (0=FL, 1=FR, 2=RL, 3=RR), for placing debris.</summary>
        bool TryGetWheelWorldPosition(int wheel, out Vector3 position);
    }

    /// <summary>
    /// Optional extension a motor implements when it has an auto-roll assist that a ram should be allowed
    /// to defeat. Kept out of <see cref="ICarMotor"/> so the legacy motor stays untouched and so the
    /// driver can test for it with a cast that allocates nothing.
    /// </summary>
    public interface IRamRollMute
    {
        /// <summary>Suppresses the roll assist for the next <paramref name="seconds"/> of simulated time.</summary>
        void MuteAutoRoll(float seconds);
    }
}
