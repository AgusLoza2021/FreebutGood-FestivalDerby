using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// What the breakable-prop rule needs to know about one car this tick: where it is and how it is
    /// moving, in the plane the props live in. It is deliberately not a net type: the rule stays
    /// resimulable from recorded state alone and never reads a snapshot.
    /// </summary>
    public readonly struct BreakableCarSample
    {
        public readonly float X;
        public readonly float Z;
        public readonly float VelocityX;
        public readonly float VelocityZ;

        /// <summary>Collision radius in metres, planar; the car is a circle for this test.</summary>
        public readonly float Radius;

        public BreakableCarSample(float x, float z, float velocityX, float velocityZ, float radius)
        {
            if (!IsFinite(x) || !IsFinite(z) || !IsFinite(velocityX) || !IsFinite(velocityZ))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "car sample values must be finite");
            }

            // A zero or negative radius would make the circle test never overlap, silently disabling
            // the feature for that car instead of reporting a bad sample.
            if (!IsFinite(radius) || radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            X = x;
            Z = z;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
            Radius = radius;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
