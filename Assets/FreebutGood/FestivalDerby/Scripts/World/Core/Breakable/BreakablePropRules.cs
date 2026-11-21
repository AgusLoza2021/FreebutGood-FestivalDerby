using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// The server rule for breakable light props. It runs inside the fixed-step tick, tests each car
    /// against the map's unbroken props through the prebuilt grid, and marks a prop broken exactly
    /// once. Only the server calls it: clients may predict the debris VFX from the emitted
    /// <see cref="PropBrokenEvent"/>, but the prop's state (and therefore the collider that disappears)
    /// is decided here.
    ///
    /// It allocates nothing per tick: the candidate buffer and the event buffer are sized once in the
    /// constructor. Breaking a prop removes its collision, so a route can never be changed by the
    /// feature: a broken prop leaves the track more open, never blocked.
    /// </summary>
    public sealed class BreakablePropRules
    {
        private readonly BreakablePropSet _props;
        private readonly BreakablePropGrid _grid;
        private readonly PropBrokenEvent[] _events;
        private readonly int[] _near;
        private int _eventCount;
        private bool _enabled;

        public BreakablePropRules(BreakablePropSet props, BreakablePropGrid grid, int eventCapacity)
        {
            if (props == null)
            {
                throw new ArgumentNullException(nameof(props));
            }

            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (eventCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            }

            _props = props;
            _grid = grid;
            _events = new PropBrokenEvent[eventCapacity];
            // Sized to every prop so a query can never be truncated into a missed break.
            _near = new int[props.Capacity];
            _eventCount = 0;
            // The feature is born off; the match turns it on from its config.
            _enabled = false;
        }

        /// <summary>Kill switch. Off means Step does nothing at all, not even a broken flag.</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>Number of breaks produced by the last Step.</summary>
        public int EventCount
        {
            get { return _eventCount; }
        }

        public PropBrokenEvent Event(int index)
        {
            if (index < 0 || index >= _eventCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _events[index];
        }

        /// <summary>Drops the last tick's events; a caller that keeps them must copy before stepping again.</summary>
        public void ClearEvents()
        {
            _eventCount = 0;
        }

        /// <summary>
        /// Advances the rule by one tick over the given cars and returns how many props broke. Replaying
        /// the same tick is a no-op: a prop that is already broken is never broken again, so the same
        /// state produces the same (empty) result.
        /// </summary>
        public int Step(int tick, BreakableCarSample[] cars)
        {
            _eventCount = 0;

            // Kill switch first: with the feature off the tick must not even look at the props, so a
            // disabled match behaves exactly as one whose map never had a breakable prop.
            if (!_enabled)
            {
                return 0;
            }

            if (cars == null)
            {
                throw new ArgumentNullException(nameof(cars));
            }

            for (int carIndex = 0; carIndex < cars.Length; carIndex++)
            {
                BreakableCarSample car = cars[carIndex];
                int candidates = _grid.Query(car.X, car.Z, car.Radius, _near);
                for (int candidate = 0; candidate < candidates; candidate++)
                {
                    int slot = _near[candidate];
                    // One copy of the struct, read once: a per-candidate read of the indexer would
                    // repeat the bounds check several times for nothing.
                    BreakableProp prop = _props[slot];
                    if (!prop.Blocks)
                    {
                        continue;
                    }

                    float toPropX = prop.X - car.X;
                    float toPropZ = prop.Z - car.Z;
                    float touching = car.Radius + prop.Radius;
                    float distanceSquared = toPropX * toPropX + toPropZ * toPropZ;
                    if (distanceSquared > touching * touching)
                    {
                        continue;
                    }

                    float normalX;
                    float normalZ;
                    if (distanceSquared > 0f)
                    {
                        float inverseDistance = 1f / (float)Math.Sqrt(distanceSquared);
                        normalX = toPropX * inverseDistance;
                        normalZ = toPropZ * inverseDistance;
                    }
                    else
                    {
                        // The car centre sits exactly on the prop centre, so there is no contact normal
                        // to derive. A fixed axis keeps the sim deterministic instead of producing NaN.
                        normalX = 1f;
                        normalZ = 0f;
                    }

                    // Only the closing component counts: a car sliding alongside a prop, or reversing
                    // away from it, must not break it however fast it is going.
                    float normalSpeed = car.VelocityX * normalX + car.VelocityZ * normalZ;
                    BreakablePropSpec spec = BreakablePropSpecTable.Get(prop.Kind);
                    if (normalSpeed < spec.BreakSpeed)
                    {
                        continue;
                    }

                    // Reserve the event slot before mutating the prop, so a broken prop always has an
                    // event and a full buffer never leaves the prop broken without one.
                    if (_eventCount == _events.Length)
                    {
                        continue;
                    }

                    if (!_props.TryBreak(slot, tick))
                    {
                        continue;
                    }

                    _events[_eventCount] = new PropBrokenEvent(slot, tick, carIndex, normalX, normalZ);
                    _eventCount++;
                }
            }

            return _eventCount;
        }

        /// <summary>
        /// The optional speed penalty of a prop kind, in the form the caller applies: a speed multiplier
        /// and how many ticks it lasts. False means the penalty is off (the shipped default) or the id
        /// is unknown, and the caller should leave the car's speed alone.
        /// </summary>
        public bool TryGetSpeedPenalty(int propId, out float speedMultiplier, out int ticks)
        {
            speedMultiplier = 1f;
            ticks = 0;

            if (propId < 0 || propId >= _props.Count)
            {
                return false;
            }

            BreakablePropSpec spec = BreakablePropSpecTable.Get(_props[propId].Kind);
            if (!spec.HasSpeedPenalty)
            {
                return false;
            }

            speedMultiplier = spec.SpeedMultiplier;
            ticks = spec.SpeedPenaltyTicks;
            return true;
        }
    }
}
