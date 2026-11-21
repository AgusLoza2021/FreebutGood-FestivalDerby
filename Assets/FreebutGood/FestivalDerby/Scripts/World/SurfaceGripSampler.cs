using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// Turns a car's world position into the grip multiplier its tyres should feel. It owns a cached
    /// nearest-path hint, so one instance per car keeps the query O(1) per tick instead of scanning
    /// the whole loop; the hint is the only mutable state and nothing here allocates.
    ///
    /// This is the bridge between the baked surface data and the handling model. The World assembly
    /// knows grip, the driving assembly knows tyres, and neither references the other: the scene
    /// hands the driving driver a delegate that calls this, so the authoritative car reads the same
    /// number the server would derive from the same path.
    /// </summary>
    public sealed class SurfaceGripSampler
    {
        private readonly BakedPath _path;
        private readonly SurfaceTable _table;
        private int _hint = -1;

        public SurfaceGripSampler(BakedPath path, SurfaceTable table)
        {
            _path = path;
            _table = table ?? SurfaceTable.Defaults;
        }

        /// <summary>Grip multiplier at the path point nearest <paramref name="worldPosition"/>.</summary>
        public float Multiplier(Vector3 worldPosition)
        {
            if (_path == null || !_path.IsUsable)
                return 1f;

            int hint = _hint;
            float distance = _path.NearestDistance(PathConvert.ToPathVector(worldPosition), ref hint);
            _hint = hint;
            return MultiplierAt(distance);
        }

        /// <summary>
        /// Grip multiplier at one arc length: the two surface ids the blend names, lerped by the
        /// blend byte. A single-surface sample skips the second lookup entirely.
        /// </summary>
        public float MultiplierAt(float distance)
        {
            if (_path == null)
                return 1f;

            SurfaceBlend blend = _path.SurfaceAt(distance);
            float primary = GripOf(blend.Primary);
            if (blend.Blend == 0 || blend.Primary == blend.Secondary)
                return primary;

            float secondary = GripOf(blend.Secondary);
            return primary + (secondary - primary) * (blend.Blend / 255f);
        }

        private float GripOf(SurfaceId id)
        {
            SurfaceDefinition definition;
            return _table.TryGet(id, out definition) ? definition.GripMultiplier : 1f;
        }

        /// <summary>
        /// The per-car delegate for a loaded map, or null when the map has no usable path. It lives
        /// here so the scene bootstrap can wire grip through world types alone: the engine layer does
        /// not reference the engine-free path assembly, and naming a baked path type out there would
        /// force that reference for no gain.
        /// </summary>
        public static System.Func<Vector3, float> ProviderFor(LoadedMap map)
        {
            if (map == null)
                return null;
            BakedPath path = map.MainPath;
            if (path == null || !path.IsUsable)
                return null;
            return new SurfaceGripSampler(path, SurfaceTable.Defaults).Multiplier;
        }
    }
}
