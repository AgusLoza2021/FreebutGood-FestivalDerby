using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Sorted surface definitions with a binary-search lookup. The constructor clones and sorts the
    /// input, so one table is safe to share by reference.
    /// </summary>
    public sealed class SurfaceTable
    {
        private static readonly SurfaceTable s_defaults = BuildDefaults();

        private readonly SurfaceDefinition[] _items;

        public SurfaceTable(SurfaceDefinition[] definitions)
        {
            _items = definitions == null
                ? new SurfaceDefinition[0]
                : (SurfaceDefinition[])definitions.Clone();
            Array.Sort(_items, Compare);
        }

        /// <summary>
        /// The five stock surfaces. Cached rather than rebuilt per call: the table is immutable
        /// after construction, so a bake or a tick that asks for defaults must not allocate.
        /// </summary>
        public static SurfaceTable Defaults
        {
            get { return s_defaults; }
        }

        public bool TryGet(SurfaceId id, out SurfaceDefinition definition)
        {
            int low = 0;
            int high = _items.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                int comparison = ((byte)_items[mid].Id).CompareTo((byte)id);
                if (comparison == 0)
                {
                    definition = _items[mid];
                    return true;
                }

                if (comparison < 0)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            definition = default(SurfaceDefinition);
            return false;
        }

        private static SurfaceTable BuildDefaults()
        {
            return new SurfaceTable(new[]
            {
                new SurfaceDefinition(SurfaceId.Street, 1f, 0, 0),
                new SurfaceDefinition(SurfaceId.Dirt, 0.82f, 1, 1),
                new SurfaceDefinition(SurfaceId.Snow, 0.55f, 2, 2),
                new SurfaceDefinition(SurfaceId.Mud, 0.68f, 3, 3),
                new SurfaceDefinition(SurfaceId.Gravel, 0.76f, 4, 4),
            });
        }

        private static int Compare(SurfaceDefinition a, SurfaceDefinition b)
        {
            return ((byte)a.Id).CompareTo((byte)b.Id);
        }
    }
}
