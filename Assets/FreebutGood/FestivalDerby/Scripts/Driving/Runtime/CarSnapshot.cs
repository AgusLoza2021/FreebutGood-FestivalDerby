using FestivalDerby.Driving;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Plain data capture of one driver's fixed-step state, read without touching the driver itself.
    /// <c>default(CarSnapshot)</c> means "no body is bound": HasBody is false and every field
    /// carries its zero value.
    /// </summary>
    public readonly struct CarSnapshot
    {
        public CarSnapshot(
            bool hasBody,
            Vector3 position,
            Quaternion rotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            CarControlMode resolvedMode,
            bool isPaused,
            int fixedStepCount,
            float yawRate = 0f)
        {
            HasBody = hasBody;
            Position = position;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
            ResolvedMode = resolvedMode;
            IsPaused = isPaused;
            FixedStepCount = fixedStepCount;
            YawRate = yawRate;
        }

        public bool HasBody { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 LinearVelocity { get; }
        public Vector3 AngularVelocity { get; }
        public CarControlMode ResolvedMode { get; }
        public bool IsPaused { get; }
        public int FixedStepCount { get; }

        /// <summary>
        /// Turn rate about the world up axis, in rad/s, positive to the car's left. Read from the body
        /// rather than from the motor so it is available for both models; the motor's own target lives on
        /// the V2 debug readout because the legacy model has no target to report.
        /// </summary>
        public float YawRate { get; }
    }
}
