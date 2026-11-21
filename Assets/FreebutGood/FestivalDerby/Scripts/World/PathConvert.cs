using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// The one place that translates between the engine-free baked vectors and UnityEngine types.
    /// The runtime never needs the spline package, only this narrow conversion at its border.
    /// </summary>
    public static class PathConvert
    {
        public static Vector3 ToVector3(PathVector3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        public static PathVector3 ToPathVector(Vector3 value)
        {
            return new PathVector3(value.x, value.y, value.z);
        }

        public static Vector3[] ToVector3Array(PathVector3[] values)
        {
            if (values == null)
                return null;
            var result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = ToVector3(values[i]);
            return result;
        }

        /// <summary>
        /// Yaw-only rotation that faces a planar heading. Paths are authored on a level surface, so
        /// dropping the tangent's Y keeps a car's up axis vertical even on a sloped tangent.
        /// </summary>
        public static Quaternion HeadingRotation(PathVector3 tangent)
        {
            var heading = new Vector3(tangent.x, 0f, tangent.z);
            if (heading.sqrMagnitude < 1e-8f)
                heading = Vector3.forward;
            return Quaternion.LookRotation(heading, Vector3.up);
        }

        /// <summary>
        /// Unity's right of a planar heading (x, z) is (z, -x); the baked path stores a
        /// right-handed Y-up tangent, so this is the lateral axis a road edge is offset along.
        /// </summary>
        public static Vector3 Right(PathVector3 tangent)
        {
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            return right.sqrMagnitude < 1e-8f ? Vector3.right : right.normalized;
        }
    }
}
