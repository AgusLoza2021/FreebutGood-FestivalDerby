using FestivalDerby.Bots;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FestivalDerby.Match
{
    /// <summary>What a truck chase needs from the scene. Filled by the composition root.</summary>
    public sealed class TruckChaseSetup
    {
        public BakedPath Line;
        public TruckSettings Truck = TruckSettings.Default;
        public TruckChaseSettings Rules = new TruckChaseSettings(3f, 20f, 3f);
        public Material TruckMaterial;
        public Transform Parent;

        /// <summary>Seconds the truck and the bots hold on the grid before the chase starts.</summary>
        public float StartDelay = 3f;

        /// <summary>How far ahead of the player's grid slot the truck waits, in metres of racing line.</summary>
        public float LeadMeters = 45f;

        public Vector3 PlayerGridPosition;
        public Rigidbody PlayerBody;
        public LocalCarFixedStepDriver PlayerDriver;
        public RacingLineBot[] Bots;
    }

    /// <summary>
    /// Runs one truck chase: owns the truck, feeds the rules once per physics step with where every car is
    /// and whether it rides the bed, takes over bots that are chasing the truck, removes eliminated cars
    /// and tells the HUD what to show. Participant 0 is the player, bot <c>i</c> is participant <c>i + 1</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TruckChaseSession : MonoBehaviour
    {
        public enum Phase
        {
            Countdown,
            Running,
            Finished
        }

        private const float MinLoopFractionForLead = 1f / 3f;
        private const float EndTapDelay = 1.5f;

        private TruckChaseSetup _setup;
        private TruckView _truck;
        private TruckChaseMatch _match;
        private TruckChaseHud _hud;
        private TruckBoardingBrain[] _brains;
        private float[] _progress;
        private bool[] _onBed;
        private int[] _hints;
        private int _handledEliminations;
        private float _countdown;
        private float _finishedFor;
        private Phase _phase;

        public Phase State
        {
            get { return _phase; }
        }

        public TruckChaseMatch Match
        {
            get { return _match; }
        }

        public TruckView Truck
        {
            get { return _truck; }
        }

        public MatchOutcome PlayerOutcome
        {
            get { return _match != null ? _match.OutcomeFor(0) : MatchOutcome.Ongoing; }
        }

        /// <summary>Null when the line or the truck settings cannot support a chase.</summary>
        public static TruckChaseSession Create(TruckChaseSetup setup)
        {
            if (setup == null || setup.Line == null || !setup.Line.IsUsable || setup.PlayerBody == null
                || !setup.Truck.IsValid)
                return null;

            var host = new GameObject("TruckChase");
            if (setup.Parent != null)
                host.transform.SetParent(setup.Parent, false);

            TruckChaseSession session = host.AddComponent<TruckChaseSession>();
            if (!session.Build(setup))
            {
                Destroy(host);
                return null;
            }

            return session;
        }

        private bool Build(TruckChaseSetup setup)
        {
            _setup = setup;
            BakedPath line = setup.Line;

            int hint = -1;
            float gridDistance = line.NearestDistance(new PathVector3(
                setup.PlayerGridPosition.x, setup.PlayerGridPosition.y, setup.PlayerGridPosition.z), ref hint);
            float lead = Mathf.Min(setup.LeadMeters, line.TotalLength * MinLoopFractionForLead);
            TruckSettings truck = setup.Truck.WithStartDistance(gridDistance + lead);

            _truck = TruckView.Spawn(line, in truck, null, transform);
            if (_truck == null)
                return false;

            int bots = setup.Bots != null ? setup.Bots.Length : 0;
            int participants = bots + 1;
            _match = new TruckChaseMatch(participants, new TruckChaseSettings(
                setup.Rules.RequiredSeconds, setup.Rules.EliminationInterval, truck.BedHalfLength));
            _progress = new float[participants];
            _onBed = new bool[participants];
            _hints = new int[participants];
            for (int i = 0; i < participants; i++)
                _hints[i] = -1;

            _brains = new TruckBoardingBrain[bots];
            for (int i = 0; i < bots; i++)
            {
                RacingLineBot bot = setup.Bots[i];
                _brains[i] = new TruckBoardingBrain(bot != null ? bot.Pace : 1f);
                if (bot != null)
                    bot.InputOverride = new Pilot(this, i).Decide;
            }

            _countdown = Mathf.Max(0f, setup.StartDelay);
            _phase = _countdown > 0f ? Phase.Countdown : Phase.Running;
            if (_phase == Phase.Running)
                _truck.Begin();

            _hud = TruckChaseHud.Build(transform);
            return true;
        }

        private void FixedUpdate()
        {
            if (_match == null)
                return;

            float dt = Time.fixedDeltaTime;
            SamplePositions();

            if (_phase == Phase.Countdown)
            {
                _countdown -= dt;
                if (_countdown <= 0f)
                {
                    _phase = Phase.Running;
                    _truck.Begin();
                }

                return;
            }

            if (_phase != Phase.Running)
                return;

            _match.Tick(dt, _progress, _onBed, _truck.Distance, _truck.LoopLength);
            ApplyEliminations();

            if (_match.IsFinished)
                Finish();
        }

        private void Update()
        {
            if (_match == null)
                return;

            _hud.Show(this);

            if (_phase != Phase.Finished)
                return;

            _finishedFor += Time.unscaledDeltaTime;
            if (_finishedFor < EndTapDelay)
                return;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)
                || Input.touchCount > 0)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void SamplePositions()
        {
            BakedPath line = _setup.Line;
            _progress[0] = Progress(_setup.PlayerBody.position, 0, line);
            _onBed[0] = _match.IsAlive(0) && _truck.IsRiding(_setup.PlayerDriver, _setup.PlayerBody);

            for (int i = 0; i < _brains.Length; i++)
            {
                RacingLineBot bot = _setup.Bots[i];
                Rigidbody body = bot != null ? bot.Body : null;
                int p = i + 1;
                if (body == null || !_match.IsAlive(p))
                {
                    _onBed[p] = false;
                    continue;
                }

                _progress[p] = Progress(body.position, p, line);
                _onBed[p] = _truck.IsRiding(bot.Driver, body);
            }
        }

        private float Progress(Vector3 position, int participant, BakedPath line)
        {
            return line.NearestDistance(new PathVector3(position.x, position.y, position.z), ref _hints[participant]);
        }

        private void ApplyEliminations()
        {
            if (_match.EliminationCount == _handledEliminations)
                return;

            _handledEliminations = _match.EliminationCount;
            int eliminated = _match.LastEliminated;
            if (eliminated == 0)
            {
                // The player stays on the map but cannot drive any more: a locked, braking car.
                _setup.PlayerDriver.SetInputSampler(LockedInput);
                return;
            }

            RacingLineBot bot = _setup.Bots[eliminated - 1];
            if (bot != null)
                bot.Dispose();
        }

        private void Finish()
        {
            _phase = Phase.Finished;
            _truck.Stop();
            if (_match.IsAlive(0) && _match.Winner != 0)
                _setup.PlayerDriver.SetInputSampler(LockedInput);
        }

        private static CarInput LockedInput(float dt)
        {
            return new CarInput(0f, 0f, true, false, false);
        }

        private void OnDestroy()
        {
            if (_setup != null && _setup.Bots != null)
                for (int i = 0; i < _setup.Bots.Length; i++)
                    if (_setup.Bots[i] != null)
                        _setup.Bots[i].InputOverride = null;
        }

        // ------------------------------------------------------------------ HUD readouts

        /// <summary>Seconds left before the grid is released, 0 once the chase runs.</summary>
        public float CountdownSeconds
        {
            get { return _phase == Phase.Countdown ? Mathf.Max(0f, _countdown) : 0f; }
        }

        public float RequiredSeconds
        {
            get { return _setup.Rules.RequiredSeconds; }
        }

        public string NameOf(int participant)
        {
            return participant == 0 ? "YOU" : "BOT " + participant;
        }

        // ------------------------------------------------------------------ bots

        /// <summary>One closure per bot, built once at setup and called from the bot's physics step.</summary>
        private sealed class Pilot
        {
            private readonly TruckChaseSession _session;
            private readonly int _index;

            public Pilot(TruckChaseSession session, int index)
            {
                _session = session;
                _index = index;
            }

            public bool Decide(in BotSelfState self, float dt, out CarInput input)
            {
                input = default;
                TruckChaseSession s = _session;
                if (s._match == null)
                    return false;

                // The grid is held until the countdown ends: brake, do not race.
                if (s._phase == Phase.Countdown)
                {
                    input = new CarInput(0f, 0f, true, false, false);
                    return true;
                }

                int p = _index + 1;
                if (s._phase != Phase.Running || !s._match.IsAlive(p) || !s._truck.TryGetPose(out TruckPose pose))
                    return false;

                RacingLineBot bot = s._setup.Bots[_index];
                if (bot == null)
                    return false;

                TruckSettings truck = s._truck.Settings;
                float tail = ChaseGeometry.DistanceToTail(s._progress[p], s._truck.Distance, s._truck.LoopLength,
                    truck.BedHalfLength);
                var boarding = new BoardingSelf(self.Position, self.Forward, self.Speed, self.TopSpeed);
                if (!s._brains[_index].TryDecide(in boarding, in pose, in truck, s._match.IsArmed(p), tail,
                        bot.FullSteerDegrees(self.Speed), dt, out BoardingCommand command))
                    return false;

                input = new CarInput(command.Steer, command.Throttle, command.Brake, command.Reverse, false);
                return true;
            }
        }
    }
}
