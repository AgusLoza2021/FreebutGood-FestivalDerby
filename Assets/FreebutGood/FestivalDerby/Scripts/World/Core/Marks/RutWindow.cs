using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// The moving window the rut mask covers: an axis-aligned square in world XZ, its texel grid, and
    /// the snap step that keeps a scroll from resampling the mask.
    ///
    /// Ported from NinjaSurvivor's GroundMaskDriver (same owner), where the numbers were tuned against
    /// a shipped game. The window exists for RESOLUTION, not for cost: the per-frame price of a mask is
    /// the texture size and the number of draws, and neither depends on the area. At 72 m over 1024
    /// texels there are 14 texels per metre, so a 0.45 m rut is six texels wide and its gradient reads
    /// as a pressed groove instead of a staircase. Covering 220 m at the same resolution gave one rut
    /// seven texels across the whole map and the relief disappeared.
    ///
    /// Engine-free on purpose: the scroll and the snap are the parts that must be exact, so they are
    /// testable without a RenderTexture.
    /// </summary>
    public struct RutWindow
    {
        /// <summary>
        /// Origin snap step, in texels. Every scroll moves the origin by whole multiples of this, so
        /// the copy is 1:1 with POINT sampling and not one bit of the mask is averaged away. 32 texels
        /// at the default area is about 2.2 m: roughly three scrolls per second at race speed, against
        /// about sixty if the origin snapped per texel.
        /// </summary>
        public const int SnapTexels = 32;

        public const int MinResolution = 128;

        /// <summary>
        /// Ceiling for the resolution. 1024 squared in R8 is 1 MB of video memory; the ARGB32 fallback
        /// on a device without R8 is 4 MB, which is why the fallback is logged rather than silent.
        /// </summary>
        public const int MaxResolution = 1024;

        /// <summary>
        /// Ceiling for the area, in metres. Past this a rut stops resolving (96 m over 1024 texels is
        /// 0.09 m per texel, so a 0.45 m rut is five texels) and the memory ceiling above is pointless.
        /// </summary>
        public const float MaxAreaSize = 96f;

        public const float MinAreaSize = 24f;

        /// <summary>Side of the window in world units; zero means "not configured yet".</summary>
        public float AreaSize;

        public int Resolution;

        /// <summary>Lower-left corner of the window in world XZ, always on the snap grid.</summary>
        public float OriginX;

        public float OriginZ;

        public float TexelWorld
        {
            get { return Resolution > 0 ? AreaSize / Resolution : 0f; }
        }

        public float SnapWorld
        {
            get { return TexelWorld * SnapTexels; }
        }

        public bool IsUsable
        {
            get { return AreaSize > 0f && Resolution > 0; }
        }

        /// <summary>
        /// Builds a usable window from possibly hostile authored numbers: the area is clamped to the
        /// documented ceiling, the resolution is clamped and snapped to a power of two (a texture needs
        /// one), and the origin is placed so the window is centred on the world origin.
        /// </summary>
        public static RutWindow Create(float areaSize, int resolution)
        {
            float clampedArea = areaSize;
            if (!(clampedArea > 0f))
                clampedArea = MaxAreaSize;
            if (clampedArea < MinAreaSize)
                clampedArea = MinAreaSize;
            if (clampedArea > MaxAreaSize)
                clampedArea = MaxAreaSize;

            int clampedResolution = ClampResolution(resolution);

            var window = new RutWindow();
            window.AreaSize = clampedArea;
            window.Resolution = clampedResolution;
            window.SnapTo(0f, 0f);
            return window;
        }

        /// <summary>
        /// Nearest power of two inside the supported band. A caller that asks for 800 gets 1024, not a
        /// RenderTexture that Unity silently resizes on a device.
        /// </summary>
        public static int ClampResolution(int resolution)
        {
            if (resolution <= MinResolution)
                return MinResolution;

            int power = MinResolution;
            while (power < resolution && power < MaxResolution)
                power <<= 1;
            return power > MaxResolution ? MaxResolution : power;
        }

        /// <summary>
        /// Origin coordinate for a window centred on <paramref name="centre"/>, snapped down to the grid.
        /// Floor and not round: a snap that rounds can move the origin BACKWARDS, and a scroll that
        /// flips sign copies the mask the wrong way.
        /// </summary>
        public static float SnapOrigin(float centre, float areaSize, float snapWorld)
        {
            if (!(snapWorld > 0f))
                return centre - areaSize * 0.5f;

            float raw = (centre - areaSize * 0.5f) / snapWorld;
            return (float)Math.Floor(raw) * snapWorld;
        }

        /// <summary>Jumps the window onto the grid around a point, without producing a scroll.</summary>
        public void SnapTo(float centreX, float centreZ)
        {
            OriginX = SnapOrigin(centreX, AreaSize, SnapWorld);
            OriginZ = SnapOrigin(centreZ, AreaSize, SnapWorld);
        }

        /// <summary>
        /// True when the point falls inside the window. An unconfigured window (zero area) contains
        /// nothing: without that guard a zero size would accept exactly the origin and stamp into a
        /// texture that does not exist.
        /// </summary>
        public bool Contains(float x, float z)
        {
            if (!IsUsable)
                return false;

            float u = x - OriginX;
            float v = z - OriginZ;
            return u >= 0f && u <= AreaSize && v >= 0f && v <= AreaSize;
        }

        /// <summary>
        /// Moves the window to follow a point and reports the UV shift the update pass has to apply.
        /// Returns false when the point is still inside the same snap cell, which is the common case
        /// and costs no blit at all.
        ///
        /// The sign is the one the shader needs: the piece of world that was at uv is now at
        /// uv - delta / area, so the new mask at uv has to read the old one at uv + delta / area.
        /// </summary>
        public bool Scroll(float centreX, float centreZ, out float deltaU, out float deltaV)
        {
            deltaU = 0f;
            deltaV = 0f;
            if (!IsUsable)
                return false;

            float targetX = SnapOrigin(centreX, AreaSize, SnapWorld);
            float targetZ = SnapOrigin(centreZ, AreaSize, SnapWorld);

            float deltaX = targetX - OriginX;
            float deltaZ = targetZ - OriginZ;
            if (deltaX == 0f && deltaZ == 0f)
                return false;

            OriginX = targetX;
            OriginZ = targetZ;
            deltaU = deltaX / AreaSize;
            deltaV = deltaZ / AreaSize;
            return true;
        }

        /// <summary>
        /// Mask UV for a world point, matching the shader's own projection. False outside the window,
        /// where a stamp would be clipped away anyway and its matrix is not worth writing.
        /// </summary>
        public bool TryWorldToUv(float x, float z, out float u, out float v)
        {
            u = 0f;
            v = 0f;
            if (!IsUsable)
                return false;

            u = (x - OriginX) / AreaSize;
            v = (z - OriginZ) / AreaSize;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }
    }
}
