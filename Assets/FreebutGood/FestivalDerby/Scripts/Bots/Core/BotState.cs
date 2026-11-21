using System;
using FestivalDerby.World;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// The bot's own car, reduced to the handful of numbers the planner needs. The Unity glue fills it
    /// from the Rigidbody once per physics step; the engine-free planner never sees Unity.
    /// </summary>
    public struct BotSelfState
    {
        public PathVector3 Position;

        /// <summary>Horizontal, normalised heading of the car.</summary>
        public PathVector3 Forward;

        /// <summary>Signed speed along the horizontal forward. Negative while reversing.</summary>
        public float Speed;

        /// <summary>The car's top speed, used to turn a target speed into a throttle fraction.</summary>
        public float TopSpeed;
    }

    /// <summary>Planar vector helpers, so no layer needs to know which axis is "up".</summary>
    public static class BotMath
    {
        public static PathVector3 Horizontal(PathVector3 v)
        {
            return new PathVector3(v.x, 0f, v.z);
        }

        /// <summary>Unity's right of a planar heading, matching World's PathConvert.Right.</summary>
        public static PathVector3 Right(PathVector3 forward)
        {
            var horizontal = Horizontal(forward);
            var right = new PathVector3(horizontal.z, 0f, -horizontal.x);
            return right.SqrMagnitude < 1e-8f ? new PathVector3(1f, 0f, 0f) : right.Normalized;
        }

        /// <summary>
        /// Signed turn in degrees, positive toward <see cref="Right"/>. A positive result is exactly
        /// the sign the motor's steer expects, so a follower can hand it straight to a CarInput.
        /// </summary>
        public static float SignedAngleDegrees(PathVector3 from, PathVector3 to)
        {
            var f = Horizontal(from).Normalized;
            var t = Horizontal(to).Normalized;
            float dot = f.x * t.x + f.z * t.z;
            float cross = f.z * t.x - f.x * t.z;
            return (float)(Math.Atan2(cross, dot) * (180.0 / Math.PI));
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Abs(float value)
        {
            return value < 0f ? -value : value;
        }
    }
}
