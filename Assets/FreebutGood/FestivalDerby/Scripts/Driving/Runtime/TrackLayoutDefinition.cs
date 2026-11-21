using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>Which centerline a track layout describes.</summary>
    public enum TrackLayoutKind
    {
        /// <summary>Two straights joined by two semicircles: the big oval racer and derby arena.</summary>
        RoundedOval = 0,

        /// <summary>The flat crossing figure-eight the run shipped with; kept for the null layout.</summary>
        FigureEight = 1,
    }

    /// <summary>
    /// The car grid that starts behind the start line. Serialized by value on the layout asset;
    /// Columns run across the track, Rows stack backwards behind the line.
    /// </summary>
    [Serializable]
    public struct TrackSpawnGrid
    {
        public int Columns;
        public int Rows;
        public float Spacing;

        public TrackSpawnGrid(int columns, int rows, float spacing)
        {
            Columns = columns;
            Rows = rows;
            Spacing = spacing;
        }

        public bool IsValid => Columns > 0 && Rows > 0 && IsFinite(Spacing) && Spacing > 0f;

        public int Count => IsValid ? Columns * Rows : 0;

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// The sanitized, immutable numbers a run builds a track from. Produced only by
    /// <see cref="TrackLayoutDefinition.Resolve(TrackLayoutDefinition)"/>, so a broken asset can
    /// yield the shipped defaults but never a zero-sized surface.
    ///
    /// <see cref="OuterWidth"/> and <see cref="OuterLength"/> measure the centerline path's
    /// bounding box, not the asphalt edge: the ribbon grows by half <see cref="TrackWidth"/> on
    /// each side and the driving floor by <see cref="RunOff"/> beyond that. That is why the
    /// default 90 x 150 m centerline oval laps at ~437 m while its floor is 116 x 176 m.
    /// </summary>
    public readonly struct TrackLayoutSettings
    {
        public readonly TrackLayoutKind Kind;
        public readonly float OuterLength;
        public readonly float OuterWidth;
        public readonly float TrackWidth;
        public readonly float CornerRadius;
        public readonly float WallHeight;
        public readonly float WallThickness;
        public readonly float RunOff;
        public readonly int RouteSampleCount;
        public readonly TrackSpawnGrid SpawnGrid;
        public readonly bool HasCameraOverride;
        public readonly Vector3 CameraOffset;
        public readonly float CameraLookHeight;

        public TrackLayoutSettings(TrackLayoutKind kind, float outerLength, float outerWidth,
            float trackWidth, float cornerRadius, float wallHeight, float wallThickness, float runOff,
            int routeSampleCount, TrackSpawnGrid spawnGrid, bool hasCameraOverride, Vector3 cameraOffset,
            float cameraLookHeight)
        {
            Kind = kind;
            OuterLength = outerLength;
            OuterWidth = outerWidth;
            TrackWidth = trackWidth;
            CornerRadius = cornerRadius;
            WallHeight = wallHeight;
            WallThickness = wallThickness;
            RunOff = runOff;
            RouteSampleCount = routeSampleCount;
            SpawnGrid = spawnGrid;
            HasCameraOverride = hasCameraOverride;
            CameraOffset = cameraOffset;
            CameraLookHeight = cameraLookHeight;
        }

        /// <summary>The shipped big-oval layout: everything is configurable, this is just the seed.</summary>
        public static TrackLayoutSettings Defaults => new TrackLayoutSettings(
            TrackLayoutKind.RoundedOval,
            TrackLayoutDefinition.DefaultOuterLength,
            TrackLayoutDefinition.DefaultOuterWidth,
            TrackLayoutDefinition.DefaultTrackWidth,
            TrackLayoutDefinition.DefaultCornerRadius,
            TrackLayoutDefinition.DefaultWallHeight,
            TrackLayoutDefinition.DefaultWallThickness,
            TrackLayoutDefinition.DefaultRunOff,
            TrackLayoutDefinition.DefaultRouteSampleCount,
            new TrackSpawnGrid(TrackLayoutDefinition.DefaultSpawnColumns,
                TrackLayoutDefinition.DefaultSpawnRows, TrackLayoutDefinition.DefaultSpawnSpacing),
            true,
            TrackLayoutDefinition.DefaultCameraOffset,
            TrackLayoutDefinition.DefaultCameraLookHeight);

        public bool IsRoundedOval => Kind == TrackLayoutKind.RoundedOval;

        public bool IsValid =>
            IsFinite(OuterLength) && OuterLength > 0f
            && IsFinite(OuterWidth) && OuterWidth > 0f
            && IsFinite(TrackWidth) && TrackWidth > 0f
            && IsFinite(CornerRadius) && CornerRadius > 0f
            && IsFinite(WallHeight) && WallHeight > 0f
            && IsFinite(WallThickness) && WallThickness > 0f
            && IsFinite(RunOff) && RunOff >= 0f
            && RouteSampleCount >= 3
            && SpawnGrid.IsValid
            && (!HasCameraOverride || (IsFinite(CameraOffset) && IsFinite(CameraLookHeight)));

        /// <summary>Bounding box of the centerline path; the stadium the route rides on.</summary>
        public Vector2 CenterlineSize => new Vector2(OuterWidth, OuterLength);

        /// <summary>
        /// The driving floor the car factory builds: the asphalt outer edge plus run-off on every
        /// side. It also covers the open infield, so a derby fight has somewhere to happen.
        /// </summary>
        public Vector2 SurfaceSize => new Vector2(
            OuterWidth + TrackWidth + 2f * RunOff,
            OuterLength + TrackWidth + 2f * RunOff);

        /// <summary>Centerline lap length of the stadium: two straights plus two semicircles.</summary>
        public float CenterlineLapLength
        {
            get
            {
                float straightX = Mathf.Max(0f, OuterWidth - 2f * CornerRadius);
                float straightZ = Mathf.Max(0f, OuterLength - 2f * CornerRadius);
                return 2f * straightX + 2f * straightZ + 2f * Mathf.PI * CornerRadius;
            }
        }

        /// <summary>How many grid slots this layout offers; the player owns slot 0.</summary>
        public int SpawnPoseCount => SpawnGrid.Count;

        /// <summary>
        /// The centerline point and unit tangent at <paramref name="distance"/> metres into the
        /// lap, in floor-local XZ (add the floor center for world). Distance is wrapped into the
        /// lap, so the route is a closed loop with no special-cased end cap.
        /// </summary>
        public void CenterlineSample(float distance, out Vector2 point, out Vector2 tangent)
        {
            // A zero radius would divide the corner arc length by zero; the caller is sanitized,
            // but the guard keeps a hand-built struct from producing NaN geometry.
            float radius = Mathf.Max(CornerRadius, 1e-3f);
            float halfWidth = 0.5f * OuterWidth;
            float halfLength = 0.5f * OuterLength;
            float hx = Mathf.Max(0f, halfWidth - radius);
            float hz = Mathf.Max(0f, halfLength - radius);
            float quarter = 0.5f * Mathf.PI * radius;

            // Cumulative arc lengths of the nine segments, starting on the +X straight.
            float c1 = hz;
            float c2 = c1 + quarter;
            float c3 = c2 + 2f * hx;
            float c4 = c3 + quarter;
            float c5 = c4 + 2f * hz;
            float c6 = c5 + quarter;
            float c7 = c6 + 2f * hx;
            float c8 = c7 + quarter;
            float lap = c8 + hz;

            float s = distance;
            if (lap > 0f)
            {
                s %= lap;
                if (s < 0f)
                    s += lap;
            }

            if (s < c1)
            {
                point = new Vector2(halfWidth, s);
                tangent = new Vector2(0f, 1f);
            }
            else if (s < c2)
            {
                float a = (s - c1) / radius;
                point = new Vector2(hx + radius * Mathf.Cos(a), hz + radius * Mathf.Sin(a));
                tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            }
            else if (s < c3)
            {
                point = new Vector2(hx - (s - c2), halfLength);
                tangent = new Vector2(-1f, 0f);
            }
            else if (s < c4)
            {
                float a = 0.5f * Mathf.PI + (s - c3) / radius;
                point = new Vector2(-hx + radius * Mathf.Cos(a), hz + radius * Mathf.Sin(a));
                tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            }
            else if (s < c5)
            {
                point = new Vector2(-halfWidth, hz - (s - c4));
                tangent = new Vector2(0f, -1f);
            }
            else if (s < c6)
            {
                float a = Mathf.PI + (s - c5) / radius;
                point = new Vector2(-hx + radius * Mathf.Cos(a), -hz + radius * Mathf.Sin(a));
                tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            }
            else if (s < c7)
            {
                point = new Vector2(-hx + (s - c6), -halfLength);
                tangent = new Vector2(1f, 0f);
            }
            else if (s < c8)
            {
                float a = 1.5f * Mathf.PI + (s - c7) / radius;
                point = new Vector2(hx + radius * Mathf.Cos(a), -hz + radius * Mathf.Sin(a));
                tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            }
            else
            {
                point = new Vector2(halfWidth, -hz + (s - c8));
                tangent = new Vector2(0f, 1f);
            }
        }

        /// <summary>
        /// One grid slot, in world space, facing the driving direction (+Z at the start line).
        /// Slots are ordered row by row, front row first; slot 0 sits closest to the line.
        /// </summary>
        public bool TryGetSpawnPose(int index, Vector2 center, float surfaceY, out Pose pose)
        {
            int columns = SpawnGrid.Columns;
            int rows = SpawnGrid.Rows;
            pose = default;
            if (index < 0 || index >= SpawnPoseCount || columns <= 0 || rows <= 0)
                return false;

            CenterlineSample(0f, out var start, out var tangent);
            // The unity-space right of a heading is (t.z, -t.x); the lanes are laid across it.
            var right = new Vector2(tangent.y, -tangent.x);
            int row = index / columns;
            int column = index % columns;

            float maxLateral = Mathf.Max(0f, 0.5f * TrackWidth - WallThickness);
            float lateral = Mathf.Clamp((column - (columns - 1) * 0.5f) * SpawnGrid.Spacing,
                -maxLateral, maxLateral);
            float back = (row + 1) * SpawnGrid.Spacing;
            Vector2 position = start - tangent * back + right * lateral;

            pose = new Pose(
                new Vector3(center.x + position.x, surfaceY, center.y + position.y),
                Quaternion.LookRotation(new Vector3(tangent.x, 0f, tangent.y), Vector3.up));
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Big-track dimensions as an asset, so the map is a data decision rather than a floor
    /// constant. A missing or broken asset resolves to <see cref="TrackLayoutSettings.Defaults"/>:
    /// the run only builds this shape when the DrivingFeatureConfig references it, which is how the
    /// feature ships off and the inherited 24x48 figure-eight stays exactly as it was.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackLayoutDefinition", menuName = "Config/TrackLayoutDefinition")]
    public sealed class TrackLayoutDefinition : ScriptableObject
    {
        public const float DefaultOuterLength = 150f;
        public const float DefaultOuterWidth = 90f;
        public const float DefaultTrackWidth = 16f;
        public const float DefaultCornerRadius = 25f;
        public const float DefaultWallHeight = 1.2f;
        public const float DefaultWallThickness = 0.5f;
        public const float DefaultRunOff = 5f;
        public const int DefaultRouteSampleCount = 48;
        public const int DefaultSpawnColumns = 3;
        public const int DefaultSpawnRows = 2;
        public const float DefaultSpawnSpacing = 4.5f;

        public static readonly Vector3 DefaultCameraOffset = new Vector3(-14f, 24f, -12f);
        public const float DefaultCameraLookHeight = 2f;

        // Floors below these fold the ribbon or starve the route; a non-finite value falls back.
        private const float MinOuter = 10f;
        private const float MinTrackWidth = 4f;
        private const float MinWallHeight = 0.1f;
        private const float MinWallThickness = 0.05f;
        private const int MinRouteSamples = 8;
        private const int MaxRouteSamples = 64;
        // The outermost grid lane keeps this much air between the car centre and the wall face.
        // It is a floor, not a tunable: the asset decides the spacing, this only stops an absurd
        // one from parking a car inside a barrier.
        private const float SpawnLaneClearance = 1.5f;

        public TrackLayoutKind Kind = TrackLayoutKind.RoundedOval;
        [Tooltip("Centerline bounding box along Z, in metres. Asphalt and floor grow from it.")]
        public float OuterLength = DefaultOuterLength;
        [Tooltip("Centerline bounding box along X, in metres.")]
        public float OuterWidth = DefaultOuterWidth;
        public float TrackWidth = DefaultTrackWidth;
        [Tooltip("Centerline corner radius, clamped so it never folds the inner edge.")]
        public float CornerRadius = DefaultCornerRadius;
        public float WallHeight = DefaultWallHeight;
        public float WallThickness = DefaultWallThickness;
        [Tooltip("Paved margin beyond the outer asphalt edge and around the open infield.")]
        public float RunOff = DefaultRunOff;
        public int RouteSampleCount = DefaultRouteSampleCount;
        public TrackSpawnGrid SpawnGrid =
            new TrackSpawnGrid(DefaultSpawnColumns, DefaultSpawnRows, DefaultSpawnSpacing);
        public bool HasCameraOverride = true;
        public Vector3 CameraOffset = new Vector3(-14f, 24f, -12f);
        public float CameraLookHeight = DefaultCameraLookHeight;

        /// <summary>The sanitized numbers, or the shipped defaults when there is no asset.</summary>
        public static TrackLayoutSettings Resolve(TrackLayoutDefinition definition)
        {
            if (definition == null)
                return TrackLayoutSettings.Defaults;

            float outerWidth = PositiveOrDefault(definition.OuterWidth, DefaultOuterWidth, MinOuter);
            float outerLength = PositiveOrDefault(definition.OuterLength, DefaultOuterLength, MinOuter);
            float trackWidth = PositiveOrDefault(definition.TrackWidth, DefaultTrackWidth, MinTrackWidth);
            float wallHeight = PositiveOrDefault(definition.WallHeight, DefaultWallHeight, MinWallHeight);
            float wallThickness = PositiveOrDefault(definition.WallThickness, DefaultWallThickness,
                MinWallThickness);
            float runOff = NonNegativeOrDefault(definition.RunOff, DefaultRunOff);

            // The centerline radius has to keep the inner edge open and leave a straight to drive:
            // below half the track width the inside folds, above half the short side it collapses.
            float minRadius = 0.5f * trackWidth + 1f;
            float maxRadius = 0.5f * Mathf.Min(outerWidth, outerLength) - wallThickness;
            if (maxRadius < minRadius)
                maxRadius = minRadius;
            float cornerRadius = Mathf.Clamp(
                PositiveOrDefault(definition.CornerRadius, DefaultCornerRadius, 1f), minRadius, maxRadius);

            int samples = definition.RouteSampleCount > 0 ? definition.RouteSampleCount : DefaultRouteSampleCount;
            samples = Mathf.Clamp(samples, MinRouteSamples, MaxRouteSamples);

            var grid = SanitizeGrid(definition.SpawnGrid,
                0.5f * trackWidth - wallThickness - SpawnLaneClearance);

            TrackLayoutKind kind = definition.Kind == TrackLayoutKind.FigureEight
                ? TrackLayoutKind.FigureEight
                : TrackLayoutKind.RoundedOval;
            bool camera = definition.HasCameraOverride && IsFinite(definition.CameraOffset)
                && IsFinite(definition.CameraLookHeight);
            Vector3 offset = camera ? definition.CameraOffset : DefaultCameraOffset;
            float lookHeight = camera ? definition.CameraLookHeight : DefaultCameraLookHeight;

            return new TrackLayoutSettings(kind, outerLength, outerWidth, trackWidth, cornerRadius,
                wallHeight, wallThickness, runOff, samples, grid, camera, offset, lookHeight);
        }

        /// <summary>Convenience wrapper so a caller holding the asset does not repeat itself.</summary>
        public TrackLayoutSettings ToSettings()
        {
            return Resolve(this);
        }

        /// <summary>
        /// Rewrites invalid serialized numbers to the shipped defaults. Runs on the asset, never
        /// per tick, and never leaves a zero behind: an empty Inspector slot must mean "default",
        /// not a track that cannot be built.
        /// </summary>
        public void Sanitize()
        {
            var resolved = Resolve(this);
            Kind = resolved.Kind;
            OuterLength = resolved.OuterLength;
            OuterWidth = resolved.OuterWidth;
            TrackWidth = resolved.TrackWidth;
            CornerRadius = resolved.CornerRadius;
            WallHeight = resolved.WallHeight;
            WallThickness = resolved.WallThickness;
            RunOff = resolved.RunOff;
            RouteSampleCount = resolved.RouteSampleCount;
            SpawnGrid = resolved.SpawnGrid;
            HasCameraOverride = resolved.HasCameraOverride;
            CameraOffset = resolved.CameraOffset;
            CameraLookHeight = resolved.CameraLookHeight;
        }

        /// <summary>
        /// Keeps an authored grid inside the ribbon. The outermost lane sits
        /// ((columns - 1) / 2) * spacing off the centreline, so the spacing is capped by the lane
        /// offset the caller allows and not by the wall itself: a lane centred on the wall face
        /// would already have the car inside it, and capping the half-width instead of the offset
        /// silently shrank a legitimate 3x2 grid.
        /// </summary>
        private static TrackSpawnGrid SanitizeGrid(TrackSpawnGrid grid, float maxLaneOffset)
        {
            int columns = grid.Columns > 0 ? grid.Columns : DefaultSpawnColumns;
            int rows = grid.Rows > 0 ? grid.Rows : DefaultSpawnRows;
            float spacing = TrackSpawnGrid.IsFinite(grid.Spacing) && grid.Spacing > 0f
                ? grid.Spacing
                : DefaultSpawnSpacing;
            // Never let the outermost lane grow through a wall; the grid stays inside the ribbon.
            if (maxLaneOffset > 0f && columns > 1)
                spacing = Mathf.Min(spacing, 2f * maxLaneOffset / (columns - 1));
            return new TrackSpawnGrid(columns, rows, Mathf.Max(spacing, 0.1f));
        }

        private static float PositiveOrDefault(float value, float fallback, float minimum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum)
                return fallback;
            return value;
        }

        private static float NonNegativeOrDefault(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return fallback;
            return value;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
