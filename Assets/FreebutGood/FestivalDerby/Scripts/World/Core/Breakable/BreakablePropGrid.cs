using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// A uniform grid over the breakable props of one map, built once at load. The per-tick query walks
    /// only the cells a car circle can touch, so the rule costs a handful of slots instead of the whole
    /// prop list, and both build and query allocate nothing after construction.
    ///
    /// The grid stores prop slot indices, not copies: a prop broken by an earlier car is still a
    /// candidate for a later one, and the rule re-checks its broken flag. That is what keeps the
    /// per-tick cost independent of the number of broken props.
    /// </summary>
    public sealed class BreakablePropGrid
    {
        /// <summary>Upper bound per axis, so a badly scaled map cannot allocate an absurd grid.</summary>
        public const int MaxCellsPerAxis = 512;

        private readonly int _columns;
        private readonly int _rows;
        private readonly float _originX;
        private readonly float _originZ;
        private readonly float _inverseCellSize;
        private readonly float _maxPropRadius;
        private readonly float _maxX;
        private readonly float _maxZ;

        /// <summary>Prefix sum over cells: cell c owns item slots [_cellStart[c], _cellStart[c + 1]).</summary>
        private readonly int[] _cellStart;

        /// <summary>Prop slots bucketed by cell, grouped by <see cref="_cellStart"/>.</summary>
        private readonly int[] _items;

        /// <summary>
        /// Builds the grid for a set. <paramref name="cellSize"/> is a hint in metres; a non-finite or
        /// non-positive value is replaced by a size derived from the largest prop, because a degenerate
        /// cell would silently collapse the grid to one bucket and lose the speed-up.
        /// </summary>
        public BreakablePropGrid(BreakablePropSet props, float cellSize, float padding)
        {
            if (props == null)
            {
                throw new ArgumentNullException(nameof(props));
            }

            if (!IsFinite(padding) || padding < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(padding));
            }

            int count = props.Count;
            float maxRadius = 0f;
            float minX = 0f;
            float maxX = 0f;
            float minZ = 0f;
            float maxZ = 0f;

            if (count > 0)
            {
                BreakableProp first = props[0];
                minX = first.X - first.Radius;
                maxX = first.X + first.Radius;
                minZ = first.Z - first.Radius;
                maxZ = first.Z + first.Radius;
                maxRadius = first.Radius;

                for (int i = 1; i < count; i++)
                {
                    BreakableProp prop = props[i];
                    float left = prop.X - prop.Radius;
                    float right = prop.X + prop.Radius;
                    float back = prop.Z - prop.Radius;
                    float front = prop.Z + prop.Radius;

                    if (left < minX) { minX = left; }
                    if (right > maxX) { maxX = right; }
                    if (back < minZ) { minZ = back; }
                    if (front > maxZ) { maxZ = front; }
                    if (prop.Radius > maxRadius) { maxRadius = prop.Radius; }
                }
            }

            _maxPropRadius = maxRadius;
            _originX = minX - padding;
            _originZ = minZ - padding;
            _maxX = maxX + padding;
            _maxZ = maxZ + padding;

            // A degenerate hint would collapse the grid to a single bucket and lose the whole point,
            // so fall back to a size the props themselves suggest.
            float size = cellSize;
            if (!IsFinite(size) || size <= 0f)
            {
                size = 2f * maxRadius;
            }

            if (!IsFinite(size) || size <= 0f)
            {
                size = 1f;
            }

            _inverseCellSize = 1f / size;
            _columns = AxisCells(_maxX - _originX, _inverseCellSize);
            _rows = AxisCells(_maxZ - _originZ, _inverseCellSize);

            int cellCount = _columns * _rows;
            _cellStart = new int[cellCount + 1];
            _items = new int[count];

            // Three passes over the props, and one build-time scratch array, so the hot path stays
            // allocation-free: count per cell, prefix-sum the starts, then place each prop once.
            for (int i = 0; i < count; i++)
            {
                BreakableProp prop = props[i];
                _cellStart[CellIndex(prop.X, prop.Z) + 1]++;
            }

            for (int cell = 0; cell < cellCount; cell++)
            {
                _cellStart[cell + 1] += _cellStart[cell];
            }

            var cursor = new int[cellCount];
            for (int cell = 0; cell < cellCount; cell++)
            {
                cursor[cell] = _cellStart[cell];
            }

            for (int i = 0; i < count; i++)
            {
                BreakableProp prop = props[i];
                int cell = CellIndex(prop.X, prop.Z);
                _items[cursor[cell]] = i;
                cursor[cell]++;
            }
        }

        public int Columns
        {
            get { return _columns; }
        }

        public int Rows
        {
            get { return _rows; }
        }

        public float MaxPropRadius
        {
            get { return _maxPropRadius; }
        }

        /// <summary>
        /// Collects the prop slots whose circle can overlap the car circle at (x, z) with the given
        /// radius, writing into a caller-owned buffer and returning how many were written. The query
        /// box is grown by the largest prop radius, so a prop centred in a neighbouring cell is still
        /// found; the buffer is filled up to its length and the count caps there.
        /// </summary>
        public int Query(float x, float z, float radius, int[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (_items.Length == 0 || results.Length == 0)
            {
                return 0;
            }

            // The car circle can only touch a prop whose centre is inside the query box grown by the
            // largest prop radius, so anything outside that box can be rejected without a cell walk.
            float reach = radius + _maxPropRadius;
            if (x + reach < _originX || x - reach > _maxX || z + reach < _originZ || z - reach > _maxZ)
            {
                return 0;
            }

            int firstColumn = Column(x - reach);
            int lastColumn = Column(x + reach);
            int firstRow = Row(z - reach);
            int lastRow = Row(z + reach);

            int written = 0;
            for (int row = firstRow; row <= lastRow; row++)
            {
                int rowStart = row * _columns;
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    int cell = rowStart + column;
                    int end = _cellStart[cell + 1];
                    for (int slot = _cellStart[cell]; slot < end; slot++)
                    {
                        if (written == results.Length)
                        {
                            return written;
                        }

                        results[written] = _items[slot];
                        written++;
                    }
                }
            }

            return written;
        }

        private static int AxisCells(float extent, float inverseCellSize)
        {
            float cells = extent * inverseCellSize;
            if (!(cells >= 1f))
            {
                return 1;
            }

            return cells >= MaxCellsPerAxis ? MaxCellsPerAxis : 1 + (int)cells;
        }

        private int CellIndex(float x, float z)
        {
            return Row(z) * _columns + Column(x);
        }

        private int Column(float x)
        {
            // The clamped float is converted only once it is inside [1, _columns), which keeps the
            // narrowing conversion exact and avoids depending on what a float above int.MaxValue does.
            float column = (x - _originX) * _inverseCellSize;
            if (!(column >= 1f))
            {
                return 0;
            }

            return column >= _columns ? _columns - 1 : (int)column;
        }

        private int Row(float z)
        {
            float row = (z - _originZ) * _inverseCellSize;
            if (!(row >= 1f))
            {
                return 0;
            }

            return row >= _rows ? _rows - 1 : (int)row;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
