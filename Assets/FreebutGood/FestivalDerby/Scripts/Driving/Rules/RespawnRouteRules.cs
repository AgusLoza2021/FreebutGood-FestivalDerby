namespace FestivalDerby.Driving
{
    /// <summary>
    /// Index arithmetic for a respawn on a closed route of poses. Engine-free, so the rule that keeps a
    /// checkpoint respawn behind the next gate is tested without a scene.
    /// </summary>
    public static class RespawnRouteRules
    {
        /// <summary>
        /// How many poses a respawn may walk forward from <paramref name="startIndex"/> (to get off a
        /// ramp) without reaching the pose nearest the next gate, capped at <paramref name="cap"/>.
        /// That pose can already sit past the gate's line, so the walk stops one short of it: a car
        /// put down past a gate it never drove through would either earn it for free or void its lap.
        /// A negative <paramref name="nextGateIndex"/> means no gate ahead is known, and only the cap
        /// applies. Never negative.
        /// </summary>
        public static int ForwardBudget(int startIndex, int nextGateIndex, int routeCount, int cap)
        {
            if (cap < 0 || routeCount <= 0 || startIndex < 0)
            {
                return 0;
            }

            if (nextGateIndex < 0)
            {
                return cap;
            }

            int ahead = ((nextGateIndex - startIndex) % routeCount + routeCount) % routeCount;
            int budget = ahead - 1;
            if (budget < 0)
            {
                return 0;
            }

            return budget < cap ? budget : cap;
        }
    }
}
