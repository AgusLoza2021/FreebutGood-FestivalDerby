using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Where a car goes when it has to be put back on the circuit: a route pose, raised by the circuit's
    /// own spawn clearance so the body centre sits above the surface exactly as at a grid spawn. The route
    /// is a small fixed array, so every scan here allocates nothing.
    /// </summary>
    public static class CircuitRespawn
    {
        /// <summary>
        /// Metres below the lowest route pose that count as "fell off the world". A car out there is
        /// airborne for good, so nothing else would ever bring it back.
        /// </summary>
        public const float KillPlaneDepth = 10f;

        /// <summary>
        /// The surface probe starts this far above the route pose. The jump kit's table-tops are centred
        /// on route poses and stand 2.2 m tall, so a pose can sit inside one; the probe finds its top.
        /// It stays below anything taller (a start gate arch) so a car is never put on top of that.
        /// </summary>
        private const float ProbeHeight = 3f;

        /// <summary>
        /// A route pose whose surface stands more than this above the road is on a ramp. A car dropped
        /// there at zero speed slides back down the slab into whatever trapped it, so the placement walks
        /// forward to the first flat pose instead (at most <see cref="MaxRampSkip"/> poses, a few metres).
        /// </summary>
        private const float RampLift = 0.3f;

        private const int MaxRampSkip = 8;

        private static readonly RaycastHit[] SurfaceHits = new RaycastHit[8];

        /// <summary>
        /// The route pose nearest to <paramref name="position"/>. For the player this is an escape, not a
        /// shortcut: no forward gain, no loss, heading along the route.
        /// </summary>
        public static Pose NearestRoutePose(OwnedGrayboxCircuit circuit, Vector3 position)
        {
            return RoutePoseAt(circuit, NearestRouteIndex(circuit, position));
        }

        /// <summary>The index of the route pose nearest to <paramref name="position"/>, or -1 with no route.</summary>
        public static int NearestRouteIndex(OwnedGrayboxCircuit circuit, Vector3 position)
        {
            int routeCount = circuit.RouteCount;
            int bestIndex = -1;
            float bestSqrDistance = float.MaxValue;
            for (int i = 0; i < routeCount; i++)
            {
                Vector3 routePosition = circuit.GetRoutePose(i).position;
                float sqrDistance = (routePosition - position).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// The route pose at <paramref name="index"/>, wrapped round the closed route, plus the spawn
        /// clearance. A circuit with no route (or index -1) falls back to the first grid slot.
        /// </summary>
        public static Pose RoutePoseAt(OwnedGrayboxCircuit circuit, int index)
        {
            int routeCount = circuit.RouteCount;
            Pose respawn = routeCount > 0 && index >= 0
                ? circuit.GetRoutePose(index % routeCount)
                : circuit.SpawnPose;
            respawn.position += Vector3.up * circuit.Layout.SpawnHeight;
            return respawn;
        }

        /// <summary>
        /// The height below which a car on this circuit has fallen off the world, or negative infinity
        /// with no route (nothing to measure from, so nothing ever counts as fallen).
        /// </summary>
        public static float KillPlaneY(OwnedGrayboxCircuit circuit)
        {
            if (circuit == null || circuit.RouteCount == 0)
                return float.NegativeInfinity;

            float lowest = float.PositiveInfinity;
            for (int i = 0; i < circuit.RouteCount; i++)
                lowest = Mathf.Min(lowest, circuit.GetRoutePose(i).position.y);
            return lowest - KillPlaneDepth;
        }

        /// <summary>
        /// The respawn pose nearest <paramref name="from"/>, upright and heading along the route. A pose on a
        /// ramp is lifted onto its top and walked forward to the first flat pose, so a car dropped at zero
        /// speed never slides back into whatever trapped it. Allocation-free: a few non-alloc raycasts and
        /// scans over the route.
        /// </summary>
        public static Pose Place(OwnedGrayboxCircuit circuit, Vector3 from)
        {
            int index = NearestRouteIndex(circuit, from);
            int budget = RespawnRouteRules.ForwardBudget(index, -1, circuit.RouteCount, MaxRampSkip);
            return PlaceFrom(circuit, index, budget, out _);
        }

        private static Pose PlaceFrom(OwnedGrayboxCircuit circuit, int index, int budget, out bool onRamp)
        {
            onRamp = false;
            float spawnHeight = circuit.Layout.SpawnHeight;
            Pose pose = RoutePoseAt(circuit, index);
            if (index < 0)
                return pose;

            float lift = LiftOntoSurface(ref pose, spawnHeight);
            for (int step = 1; step <= budget && lift > RampLift; step++)
            {
                pose = RoutePoseAt(circuit, index + step);
                lift = LiftOntoSurface(ref pose, spawnHeight);
            }

            onRamp = lift > RampLift;
            return pose;
        }

        /// <summary>
        /// Puts the body down on <paramref name="pose"/> at rest; the caller is the body's only writer.
        /// </summary>
        public static void Commit(Rigidbody body, in Pose pose)
        {
            body.position = pose.position;
            body.rotation = pose.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Raises the pose onto the highest static surface under it (a ramp plateau), never lowers it,
        /// and returns how far it was raised. Bodies are skipped, so a car under the probe is not
        /// mistaken for the road.
        /// </summary>
        private static float LiftOntoSurface(ref Pose pose, float spawnHeight)
        {
            Vector3 origin = pose.position + Vector3.up * ProbeHeight;
            int hits = Physics.RaycastNonAlloc(origin, Vector3.down, SurfaceHits,
                ProbeHeight + spawnHeight + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < hits; i++)
            {
                if (SurfaceHits[i].rigidbody != null)
                    continue;

                float y = SurfaceHits[i].point.y;
                if (y > best)
                    best = y;
            }

            float lift = best + spawnHeight - pose.position.y;
            if (!(best > float.NegativeInfinity) || lift <= 0f)
                return 0f;

            pose.position = new Vector3(pose.position.x, best + spawnHeight, pose.position.z);
            return lift;
        }
    }
}
