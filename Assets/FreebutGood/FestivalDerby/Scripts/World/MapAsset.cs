using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// One built map as data: the roads, their barrier lines and every spawn the runtime needs.
    /// It is what the editor baker writes and what <see cref="MapLoader"/> reads, and it never
    /// mentions the spline package, so a headless server can consume the same asset.
    /// </summary>
    [CreateAssetMenu(fileName = "MapAsset", menuName = "Festival Derby/World/Map Asset")]
    public sealed class MapAsset : ScriptableObject
    {
        public string MapId;

        /// <summary>Roads[0] is the main road: laps, checkpoints and the default racing line.</summary>
        public RoadAsset[] Roads;
        public EdgeAsset[] Edges;

        // ------------------------------------------------------------- starting grid
        public int SpawnRoadIndex;
        public int SpawnColumns = 2;
        public int SpawnRows = 2;
        public float SpawnSpacing = 3f;

        /// <summary>Metres behind the start line the first grid row sits.</summary>
        public float SpawnBackOffset = 4f;
        public float SpawnHeight = 0.35f;

        // ------------------------------------------------------------- racing surface
        public float WallHeight = 1.2f;
        public float WallThickness = 0.5f;

        /// <summary>Bots are routed by sampling the racing line every this many metres.</summary>
        public float RouteStep = 10f;

        /// <summary>
        /// Checkpoint arc lengths along the main road, ordered. An empty array means "just the
        /// start line": lap validity is a later concern than building a drivable map.
        /// </summary>
        public float[] CheckpointDistances;

        /// <summary>
        /// Optional racing line: a copy of the main road with per-sample target speeds. A null line
        /// tells the loader to use the main road itself, so speed planning stays optional.
        /// </summary>
        public BakedPath RacingLine;
        public float RacingLateralAccel = 9f;
        public float RacingTopSpeed = 40f;
        public float RacingBrakeDecel = 8f;

        /// <summary>
        /// Match modes this map supports, as <c>MatchModeId</c> integers. The type stays an int so
        /// the World assembly never has to reference the netcode assembly.
        /// </summary>
        public int[] SupportedModes = { 0, 2 };

        public Vector3 CameraOffset = new Vector3(-12f, 11f, -8f);
        public float CameraLookHeight = 2f;
        public bool HasCameraOverride;

        /// <summary>
        /// The surface-blend material the swept road chunks render with, written by the bake. Null on
        /// an older map or before the road art was built; the loader then falls back to a plain URP
        /// material, which still drives but loses the per-surface look.
        /// </summary>
        public Material RoadMaterial;

        public RoadAsset PrimaryRoad
        {
            get { return Roads != null && Roads.Length > 0 ? Roads[0] : null; }
        }

        /// <summary>
        /// Repairs an authored or generated asset into a state the loader can trust. Runs on every
        /// load and after every bake, so the runtime never has to defend against a half-filled
        /// array, a zero step or an unsorted checkpoint list.
        /// </summary>
        public void Sanitize()
        {
            if (string.IsNullOrEmpty(MapId))
                MapId = name;

            SpawnColumns = AtLeast(SpawnColumns, 1);
            SpawnRows = AtLeast(SpawnRows, 1);
            SpawnSpacing = Positive(SpawnSpacing, 3f);
            SpawnBackOffset = Finite(SpawnBackOffset, 4f);
            SpawnHeight = Positive(SpawnHeight, 0.35f);
            WallHeight = Positive(WallHeight, 1.2f);
            WallThickness = Positive(WallThickness, 0.5f);
            RouteStep = Positive(RouteStep, 10f);
            RacingLateralAccel = Positive(RacingLateralAccel, 9f);
            RacingTopSpeed = Positive(RacingTopSpeed, 40f);
            RacingBrakeDecel = Positive(RacingBrakeDecel, 8f);

            if (Roads == null)
                Roads = new RoadAsset[0];
            if (Edges == null)
                Edges = new EdgeAsset[0];
            if (SupportedModes == null || SupportedModes.Length == 0)
                SupportedModes = new[] { 0 };
            if (SpawnRoadIndex < 0 || SpawnRoadIndex >= Roads.Length)
                SpawnRoadIndex = 0;

            CheckpointDistances = SanitizeCheckpoints(CheckpointDistances, Roads);
        }

        private static float[] SanitizeCheckpoints(float[] distances, RoadAsset[] roads)
        {
            int count = distances == null ? 0 : distances.Length;
            var valid = new System.Collections.Generic.List<float>(count);
            for (int i = 0; i < count; i++)
            {
                float value = distances[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;
                valid.Add(value);
            }
            if (valid.Count == 0 || roads.Length == 0 || roads[0] == null || roads[0].Path == null)
                return valid.ToArray();

            float length = roads[0].Path.TotalLength;
            for (int i = 0; i < valid.Count; i++)
            {
                float wrapped = valid[i] % length;
                valid[i] = wrapped < 0f ? wrapped + length : wrapped;
            }
            // A lap's checkpoints are read in order, so a mis-ordered bake must not reorder the lap.
            valid.Sort();
            return valid.ToArray();
        }

        private static int AtLeast(int value, int minimum)
        {
            return value < minimum ? minimum : value;
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static float Positive(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value > 0f ? value : fallback;
        }
    }
}
