using System;
using FestivalDerby.World;

namespace FestivalDerby.Match
{
    /// <summary>
    /// The truck's motion as a pure function of elapsed match time: a constant speed along the closed
    /// racing line. No state, so the pose is the same wherever it is asked.
    /// </summary>
    public static class TruckMotion
    {
        private const float MinHorizontalLength = 1e-6f;

        /// <summary>Line distance after <paramref name="elapsedSeconds"/>, wrapped into [0, lineLength).</summary>
        public static float DistanceAt(in TruckSettings settings, double elapsedSeconds, float lineLength)
        {
            if (!(lineLength > 0f) || float.IsInfinity(lineLength))
                return 0f;

            double elapsed = elapsedSeconds > 0.0 ? elapsedSeconds : 0.0;

            // Double on purpose: a float accumulator loses centimetres after a long match, and the wrap
            // must stay exact across many laps.
            double distance = settings.StartDistance + (double)settings.Speed * elapsed;
            distance %= lineLength;
            if (distance < 0.0)
                distance += lineLength;

            float wrapped = (float)distance;

            // Narrowing to float can round a value just under the length up to the length itself.
            return wrapped >= lineLength ? 0f : wrapped;
        }

        public static bool TryPoseAt(BakedPath line, in TruckSettings settings, double elapsedSeconds,
            out TruckPose pose)
        {
            pose = default;
            if (line == null || !line.IsUsable)
                return false;

            float distance = DistanceAt(in settings, elapsedSeconds, line.TotalLength);
            if (!line.Sample(distance, out PathSample sample))
                return false;

            // Flatten the tangent: a sloping line would tilt the bed test's frame, and the bed's own
            // height band already covers the climb.
            float x = sample.Tangent.x;
            float z = sample.Tangent.z;
            float length = (float)Math.Sqrt(x * x + z * z);
            PathVector3 forward = length > MinHorizontalLength
                ? new PathVector3(x / length, 0f, z / length)
                : new PathVector3(0f, 0f, 1f);

            pose = new TruckPose(sample.Position, forward, forward * settings.Speed, distance);
            return true;
        }

        /// <summary>The foot of the ramp: where a car lines up before it can drive onto the bed.</summary>
        public static PathVector3 RampEntry(in TruckPose pose, in TruckSettings settings)
        {
            float behind = settings.BedCenterOffset - settings.BedLength * 0.5f - settings.RampLength;
            return pose.Position + pose.Forward * behind;
        }
    }
}
