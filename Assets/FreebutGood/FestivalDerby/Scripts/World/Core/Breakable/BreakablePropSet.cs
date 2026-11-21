using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One prop of a loaded map. The set is mutated by the server tick, so the struct is copied out
    /// for reading and mutated in place for breaking. <see cref="BrokenTick"/> is a wire sentinel
    /// (<see cref="BreakablePropSet.IntactTick"/>) while the prop is intact.
    /// </summary>
    public struct BreakableProp
    {
        public int Id;
        public BreakablePropKind Kind;
        public float X;
        public float Z;
        public float Radius;
        public bool Broken;
        public int BrokenTick;

        /// <summary>A broken prop keeps nothing to collide with, which is what keeps routes unchanged.</summary>
        public bool Blocks
        {
            get { return !Broken; }
        }
    }

    /// <summary>
    /// The fixed, preallocated prop array of one map, built once at load and then only mutated by the
    /// server tick. Capacity is bounded by <see cref="MaxProps"/> so the same props fit the wire
    /// bitset a late joiner reads; ids are assigned in authored order, so the server and every client
    /// agree on index-to-prop mapping without sending it.
    /// </summary>
    public sealed class BreakablePropSet
    {
        /// <summary>Sentinel for "never broken", shared with the wire's broken-prop bitset length.</summary>
        public const int IntactTick = -1;

        /// <summary>
        /// Hard cap, mirrored by <c>BrokenPropBitset.MaxProps</c> in the net core. The two live in
        /// different assemblies that do not reference each other, so the coupling is a documented
        /// constant: raising one without the other would leave a broken prop a late joiner cannot see.
        /// </summary>
        public const int MaxProps = 256;

        private readonly BreakableProp[] _props;
        private int _count;

        public BreakablePropSet(int capacity)
        {
            if (capacity <= 0 || capacity > MaxProps)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _props = new BreakableProp[capacity];
            _count = 0;
        }

        public int Capacity
        {
            get { return _props.Length; }
        }

        public int Count
        {
            get { return _count; }
        }

        public int BrokenCount
        {
            get
            {
                int broken = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (_props[i].Broken)
                    {
                        broken++;
                    }
                }

                return broken;
            }
        }

        /// <summary>Read-only access by slot index; the returned struct is a copy.</summary>
        public BreakableProp this[int index]
        {
            get
            {
                RequireIndex(index);
                return _props[index];
            }
        }

        /// <summary>
        /// Builds the set from a map's authored edge props, keeping only the ones flagged breakable.
        /// A prop that was never flagged stays solid and is not in the set at all, which is what leaves
        /// every map authored before this feature unchanged.
        /// </summary>
        public static BreakablePropSet Build(AuthoredEdgeProp[] authored, int capacity)
        {
            if (authored == null)
            {
                throw new ArgumentNullException(nameof(authored));
            }

            // Count first so an oversized map fails before anything is built, instead of leaving a
            // half-filled set behind for the caller to reason about.
            int flagged = 0;
            for (int i = 0; i < authored.Length; i++)
            {
                if (authored[i].Breakable)
                {
                    flagged++;
                }
            }

            if (flagged > capacity)
            {
                throw new ArgumentException(
                    "the map flags " + flagged + " breakable props but the set holds " + capacity, nameof(capacity));
            }

            var set = new BreakablePropSet(capacity);
            for (int i = 0; i < authored.Length; i++)
            {
                AuthoredEdgeProp prop = authored[i];
                if (!prop.Breakable)
                {
                    continue;
                }

                set.Add(prop.Kind, prop.X, prop.Z, prop.Radius);
            }

            return set;
        }

        public int Add(BreakablePropKind kind, float x, float z, float radius)
        {
            if (_count == _props.Length)
            {
                throw new InvalidOperationException("the breakable prop set is full");
            }

            if (!IsFinite(x) || !IsFinite(z) || !IsFinite(radius) || radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            // Ids follow insertion order, so the authored order is the shared server/client identity.
            int id = _count;
            _props[_count] = new BreakableProp
            {
                Id = id,
                Kind = kind,
                X = x,
                Z = z,
                Radius = radius,
                Broken = false,
                BrokenTick = IntactTick,
            };
            _count++;
            return id;
        }

        /// <summary>Clears every prop back to intact for a new match; the layout itself is untouched.</summary>
        public void Reset()
        {
            for (int i = 0; i < _count; i++)
            {
                _props[i].Broken = false;
                _props[i].BrokenTick = IntactTick;
            }
        }

        /// <summary>
        /// Breaks one prop exactly once. False means the prop was already broken (or the id is out of
        /// range), which is what makes a resimulated or replayed tick a no-op instead of a second event.
        /// </summary>
        public bool TryBreak(int index, int tick)
        {
            if (index < 0 || index >= _count)
            {
                return false;
            }

            if (_props[index].Broken)
            {
                return false;
            }

            _props[index].Broken = true;
            _props[index].BrokenTick = tick;
            return true;
        }

        private void RequireIndex(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
