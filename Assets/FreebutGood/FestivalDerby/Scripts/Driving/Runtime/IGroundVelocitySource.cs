using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The motion of a drivable surface whose Rigidbody cannot report it. A kinematic body moved with
    /// MovePosition reads back zero velocity, so a moving platform (the CATCH carrier) has to state its own
    /// motion, or a car riding it would have its tyres braking it toward world rest and slide off the back.
    /// The motor finds it on the GameObject that owns the platform's Rigidbody.
    /// </summary>
    public interface IGroundVelocitySource
    {
        /// <summary>World velocity, in m/s, of the surface point at <paramref name="worldPoint"/>.</summary>
        Vector3 GetPointVelocity(Vector3 worldPoint);

        /// <summary>World angular velocity of the surface, in rad/s.</summary>
        Vector3 AngularVelocity { get; }
    }
}
