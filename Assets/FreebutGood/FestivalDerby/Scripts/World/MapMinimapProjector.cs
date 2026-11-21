using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// Facade over <see cref="MinimapProjection"/>. The engine-free projector and <c>BakedPath</c> live
    /// in FestivalDerby.World.Core, which Assembly-CSharp does not reference, so the HUD cannot name them
    /// directly. This auto-referenced assembly can: it reads the map's racing line, builds the projection
    /// once and exposes only normalized points, so the UI layer stays free of the core assemblies.
    /// </summary>
    public sealed class MapMinimapProjector
    {
        private readonly MinimapProjection _projection;

        private MapMinimapProjector(MinimapProjection projection)
        {
            _projection = projection;
        }

        /// <summary>
        /// Builds one projector for a map, or returns null when there is no usable racing line or the
        /// rect is degenerate. Null is the normal "no minimap this run" answer, never an exception.
        /// </summary>
        public static MapMinimapProjector Create(MapAsset map, float rectWidth, float rectHeight, float padding, bool rotateToFit)
        {
            if (map == null)
                return null;

            BakedPath path = map.RacingLine;
            if (path == null || path.Count < 2)
                return null;

            if (rectWidth <= 0f || rectHeight <= 0f || padding < 0f)
                return null;
            if (padding * 2f >= Mathf.Min(rectWidth, rectHeight))
                return null;

            return new MapMinimapProjector(new MinimapProjection(path, rectWidth, rectHeight, padding, rotateToFit));
        }

        /// <summary>Number of points on the racing-line polyline, for an optional path overlay.</summary>
        public int PolylineCount
        {
            get { return _projection.PolylineCount; }
        }

        /// <summary>A polyline point in normalized minimap space (0..1 on both axes).</summary>
        public Vector2 PolylineAt(int index)
        {
            MinimapPoint point = _projection.PolylineAt(index);
            return new Vector2(point.X, point.Y);
        }

        /// <summary>
        /// Projects a world XZ position to normalized minimap space. Returns false only for a null
        /// projector, so callers can use it as a single guard.
        /// </summary>
        public bool TryProject(float worldX, float worldZ, out Vector2 normalized)
        {
            if (_projection == null)
            {
                normalized = default;
                return false;
            }

            MinimapPoint point = _projection.Project(worldX, worldZ);
            normalized = new Vector2(point.X, point.Y);
            return true;
        }
    }
}
