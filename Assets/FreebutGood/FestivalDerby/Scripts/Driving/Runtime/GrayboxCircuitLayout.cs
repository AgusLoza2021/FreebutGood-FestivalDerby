using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The graybox circuit's provisional layout. Every dimension is a graybox default: the GDD fixes
    /// no track measurements, so the run overrides these from config once the circuit is wired in.
    /// </summary>
    public readonly struct GrayboxCircuitLayout
    {
        /// <summary>Half the boxed car's provisional height, so the spawn pose clears the surface.</summary>
        public const float DefaultSpawnHeight = 0.35f;

        /// <summary>
        /// Static squat the raycast suspension holds the chassis at: a spawn placed just clear of
        /// the collision box would leave every spring preloaded and pop the car off the line, so
        /// the spawn clears the chassis by that squat as well.
        /// </summary>
        public const float SuspensionSpawnLift = 0.055f;
        public const float DefaultWallHeight = 1.2f;
        public const float DefaultWallThickness = 0.5f;

        /// <summary>World centre of the racing surface; Y is carried by <see cref="SurfaceY"/>.</summary>
        public readonly Vector2 Center;

        /// <summary>Racing surface extent: X is the width, Y is the length.</summary>
        public readonly Vector2 Size;

        /// <summary>World Y of the face the car drives on, which the circuit never creates itself.</summary>
        public readonly float SurfaceY;

        /// <summary>Half the vehicle's height plus its static squat, so the circuit only has to know
        /// the car's box, never its spring.</summary>
        public readonly float SpawnHeight;

        public readonly float WallHeight;
        public readonly float WallThickness;
        public readonly bool IsFigureEight;

        public GrayboxCircuitLayout(Vector2 center, Vector2 size, float surfaceY,
            float spawnHeight = DefaultSpawnHeight, float wallHeight = DefaultWallHeight,
            float wallThickness = DefaultWallThickness, bool figureEight = false)
        {
            Center = center;
            Size = size;
            SurfaceY = surfaceY;
            SpawnHeight = spawnHeight + SuspensionSpawnLift;
            WallHeight = wallHeight;
            WallThickness = wallThickness;
            IsFigureEight = figureEight;
        }

        /// <summary>
        /// A layout is a construction value rather than an asset a human edits, so an invalid one is
        /// rejected by the caller instead of sanitized here: silently repairing a caller's numbers
        /// would hide the bug in the numbers.
        /// </summary>
        public bool IsValid =>
            IsFinite(Center.x) && IsFinite(Center.y)
            && IsFinite(Size.x) && IsFinite(Size.y) && Size.x > 0f && Size.y > 0f
            && IsFinite(SurfaceY)
            && IsFinite(SpawnHeight)
            && IsFinite(WallHeight) && WallHeight > 0f
            && IsFinite(WallThickness) && WallThickness > 0f;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
