using System;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Bots
{
    /// <summary>
    /// The AI racers of one run: spawns them on the starting grid behind the player, dresses each in a toy
    /// model and its own livery, and owns them until the run ends.
    /// </summary>
    public sealed class BotField : IDisposable
    {
        /// <summary>
        /// Hard cap, whatever a config or a caller asks for. Thirteen cars (twelve bots and the player) is
        /// what a mid-range phone keeps at a steady frame rate with full car physics on every one.
        /// </summary>
        public const int MaxBots = 12;

        private readonly RacingLineBot[] _bots;

        private BotField(int count)
        {
            _bots = new RacingLineBot[count];
        }

        public int Count
        {
            get { return _bots.Length; }
        }

        public RacingLineBot this[int index]
        {
            get { return _bots[index]; }
        }

        /// <summary>The grid slots a run needs for the player plus <paramref name="requestedBots"/>.</summary>
        public static int GridSlotsFor(int requestedBots)
        {
            int bots = Clamp(requestedBots);
            return bots > 0 ? bots + 1 : 0;
        }

        public static int Clamp(int requestedBots)
        {
            return Mathf.Clamp(requestedBots, 0, MaxBots);
        }

        /// <summary>
        /// Spawns up to <see cref="MaxBots"/> bots. Grid slot 0 is the player's, so bot <c>i</c> takes slot
        /// <c>i + 1</c>; the map was loaded with enough rows for the whole field. The front of the grid gets
        /// the fastest pace, so the field strings out instead of piling into the first corner.
        /// </summary>
        public static BotField Spawn(int requestedBots, LoadedMap map, OwnedGrayboxCircuit circuit,
            DrivingFeatureConfig driving, in MotorSetup setup, string[] visualIds, Color[] liveries,
            float slowestPace, float fastestPace, Transform parent)
        {
            int count = Clamp(requestedBots);
            CarDefinition definition = DrivingFeatureConfig.ResolveCar(driving, null);
            if (count == 0 || definition == null || map == null || circuit == null)
                return new BotField(0);

            var field = new BotField(count);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                var bot = new RacingLineBot(map.RacingLine, definition, Mathf.Lerp(fastestPace, slowestPace, t));
                field._bots[i] = bot;

                int slot = circuit.SpawnPoseCount > 0 ? (i + 1) % circuit.SpawnPoseCount : 0;
                bot.Spawn(definition, setup, circuit.GetSpawnPose(slot), parent);
                bot.Geometry.Root.name = "Bot " + (i + 1);

                if (driving.SurfaceGripEnabled)
                    bot.Driver.SetSurfaceGripProvider(SurfaceGripSampler.ProviderFor(map));

                Dress(bot, driving, Pick(visualIds, i), PickColor(liveries, i));
                bot.ArmRespawn(circuit, DrivingFeatureConfig.ResolveStuckRespawnEnabled(driving)
                    ? new CarGhostMonitor(
                        DrivingFeatureConfig.ResolveStuckSeconds(driving),
                        DrivingFeatureConfig.ResolveStuckSpeedLimit(driving),
                        DrivingFeatureConfig.ResolveGhostSeconds(driving),
                        DrivingFeatureConfig.ResolveGhostAlpha(driving))
                    : null);
            }

            return field;
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _bots.Length; i++)
                _bots[i]?.Tick(deltaTime);
        }

        public void Dispose()
        {
            for (int i = 0; i < _bots.Length; i++)
            {
                _bots[i]?.Dispose();
                _bots[i] = null;
            }
        }

        /// <summary>Toy model on top of the physics box, painted, with spinning wheels like the player's.</summary>
        private static void Dress(RacingLineBot bot, DrivingFeatureConfig driving, string visualId, Color livery)
        {
            if (!CarVisualCatalog.TryResolve(driving.Visuals, visualId, out CarVisualEntry entry))
                return;

            GameObject visual = CarVisualAttacher.Attach(bot.Body.transform, bot.Geometry.Chassis, entry);
            if (visual == null)
                return;

            new CarPaintApplier(visual, entry).Apply(livery);
            if (DrivingFeatureConfig.ResolveWheelSpinEnabled(driving))
                bot.Body.gameObject.AddComponent<CarWheelRig>()
                    .Build(visual.transform, bot.Driver, CarVisualCatalog.ResolveWheelSpinSign(entry));
        }

        private static string Pick(string[] ids, int index)
        {
            return ids != null && ids.Length > 0 ? ids[index % ids.Length] : null;
        }

        private static Color PickColor(Color[] colors, int index)
        {
            return colors != null && colors.Length > 0 ? colors[index % colors.Length] : Color.white;
        }
    }
}
