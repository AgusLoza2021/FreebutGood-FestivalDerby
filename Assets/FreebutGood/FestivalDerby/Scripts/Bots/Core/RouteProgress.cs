using FestivalDerby.World;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// How far along the racing line a car has actually travelled, measured by projecting the car
    /// onto the line instead of by reaching waypoints. A car with a limited turn rate overshoots a
    /// corner and comes back inside it, and a goal-radius follower reads that whole manoeuvre as "no
    /// progress" even while the car is moving along the route; a projection cannot, because it
    /// advances with the line under the car rather than waiting for a radius to be entered.
    ///
    /// The projection reuses the path's own bracketed search (a bounded window around the last
    /// sample, with a full scan only when that hint proves wrong), so a tick stays O(1) and a car
    /// that is merely off the line still banks the distance it really covered. Progress only ever
    /// moves forwards: rolling back down the line buys nothing, which is what lets a caller read
    /// <see cref="ForwardMeters"/> as an honest stall signal.
    /// </summary>
    public struct RouteProgress
    {
        /// <summary>Distance along the line under the car right now, in metres.</summary>
        public float Distance;

        /// <summary>Metres of line covered so far, forwards only and unwrapped past one lap.</summary>
        public float Travelled;

        /// <summary>Metres the last <see cref="Advance"/> call banked, never negative.</summary>
        public float ForwardMeters;

        /// <summary>Sample index the next projection starts from.</summary>
        public int HintIndex;

        private bool _seeded;

        public void Reset()
        {
            Distance = 0f;
            Travelled = 0f;
            ForwardMeters = 0f;
            HintIndex = 0;
            _seeded = false;
        }

        /// <summary>
        /// Projects <paramref name="position"/> onto the line and banks the forward part of the move.
        /// The first call only seeds the projection: a car on the starting grid must not bank the
        /// distance from the line's origin to its own slot as travel it never did.
        /// </summary>
        public float Advance(BakedPath path, PathVector3 position)
        {
            ForwardMeters = 0f;
            if (path == null || !path.IsUsable)
                return 0f;

            float distance = path.NearestDistance(position, ref HintIndex);
            if (!_seeded)
            {
                _seeded = true;
                Distance = distance;
                return 0f;
            }

            float step = distance - Distance;
            if (path.Closed && path.TotalLength > 0f)
            {
                // Crossing the line is a short forward step, not a step backwards by a whole lap.
                float half = path.TotalLength * 0.5f;
                if (step > half)
                    step -= path.TotalLength;
                else if (step < -half)
                    step += path.TotalLength;
            }

            Distance = distance;
            if (step > 0f)
            {
                Travelled += step;
                ForwardMeters = step;
            }
            return ForwardMeters;
        }
    }
}
