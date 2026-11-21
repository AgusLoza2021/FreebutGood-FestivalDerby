using FestivalDerby.Bots;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FestivalDerby.Match
{
    /// <summary>What a lap race needs from the scene. Filled by the composition root.</summary>
    public sealed class LapRaceSetup
    {
        public BakedPath Line;
        public int Laps = 3;
        public Transform Parent;

        /// <summary>Seconds the bots hold on the grid before the race starts.</summary>
        public float StartDelay = 3f;

        public Rigidbody PlayerBody;
        public LocalCarFixedStepDriver PlayerDriver;
        public RacingLineBot[] Bots;
    }

    /// <summary>
    /// Runs one classic lap race: counts every car's forward distance along the racing line once per
    /// physics step and ends the run when the first car has driven the full distance. Participant 0 is
    /// the player, bot <c>i</c> is participant <c>i + 1</c>. The bots just race: only the grid hold at the
    /// start takes the wheel from them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LapRaceSession : MonoBehaviour
    {
        public enum Phase
        {
            Countdown,
            Running,
            Finished
        }

        private const float EndTapDelay = 1.5f;

        private LapRaceSetup _setup;
        private LapRaceMatch _match;
        private float[] _progress;
        private int[] _hints;
        private float _countdown;
        private float _finishedFor;
        private Phase _phase;

        private Canvas _canvas;
        private Text _status;
        private Text _hint;
        private Text _banner;
        private Text _tap;
        private int _shownLap = -1;
        private int _shownPosition = -1;
        private int _shownReady = -1;
        private bool _bannerShown;

        public Phase State
        {
            get { return _phase; }
        }

        public LapRaceMatch Match
        {
            get { return _match; }
        }

        /// <summary>Null when the line cannot support a race.</summary>
        public static LapRaceSession Create(LapRaceSetup setup)
        {
            if (setup == null || setup.Line == null || !setup.Line.IsUsable || setup.PlayerBody == null)
                return null;

            var host = new GameObject("LapRace");
            if (setup.Parent != null)
                host.transform.SetParent(setup.Parent, false);

            LapRaceSession session = host.AddComponent<LapRaceSession>();
            session.Build(setup);
            return session;
        }

        private void Build(LapRaceSetup setup)
        {
            _setup = setup;
            int bots = setup.Bots != null ? setup.Bots.Length : 0;
            _match = new LapRaceMatch(bots + 1, setup.Laps, setup.Line.TotalLength);
            _progress = new float[bots + 1];
            _hints = new int[bots + 1];
            for (int i = 0; i < _hints.Length; i++)
                _hints[i] = -1;

            for (int i = 0; i < bots; i++)
                if (setup.Bots[i] != null)
                    setup.Bots[i].InputOverride = HoldGrid;

            _countdown = Mathf.Max(0f, setup.StartDelay);
            _phase = _countdown > 0f ? Phase.Countdown : Phase.Running;
            SamplePositions();
            _match.Rebase(_progress);
            BuildHud();
        }

        // One method group, so no closure is allocated per bot; it only ever answers "hold" or "not mine".
        private bool HoldGrid(in BotSelfState self, float dt, out CarInput input)
        {
            input = default;
            if (_phase != Phase.Countdown)
                return false;

            input = new CarInput(0f, 0f, true, false, false);
            return true;
        }

        private void FixedUpdate()
        {
            if (_match == null)
                return;

            SamplePositions();
            if (_phase == Phase.Countdown)
            {
                _countdown -= Time.fixedDeltaTime;
                _match.Rebase(_progress);
                if (_countdown <= 0f)
                    _phase = Phase.Running;
                return;
            }

            if (_phase != Phase.Running)
                return;

            _match.Tick(_progress);
            if (_match.IsFinished)
                Finish();
        }

        private void SamplePositions()
        {
            _progress[0] = Progress(_setup.PlayerBody.position, 0);
            for (int i = 0; i < _setup.Bots.Length; i++)
            {
                Rigidbody body = _setup.Bots[i] != null ? _setup.Bots[i].Body : null;
                if (body != null)
                    _progress[i + 1] = Progress(body.position, i + 1);
            }
        }

        private float Progress(Vector3 position, int participant)
        {
            return _setup.Line.NearestDistance(new PathVector3(position.x, position.y, position.z), ref _hints[participant]);
        }

        private void Finish()
        {
            _phase = Phase.Finished;
            if (_match.Winner != 0)
                _setup.PlayerDriver.SetInputSampler(LockedInput);
        }

        private static CarInput LockedInput(float dt)
        {
            return new CarInput(0f, 0f, true, false, false);
        }

        private void Update()
        {
            if (_match == null)
                return;

            ShowHud();
            if (_phase != Phase.Finished)
                return;

            _finishedFor += Time.unscaledDeltaTime;
            if (_finishedFor >= EndTapDelay && MatchUi.RestartRequested())
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnDestroy()
        {
            if (_setup != null && _setup.Bots != null)
                for (int i = 0; i < _setup.Bots.Length; i++)
                    if (_setup.Bots[i] != null)
                        _setup.Bots[i].InputOverride = null;
        }

        // ------------------------------------------------------------------ HUD

        private void BuildHud()
        {
            _canvas = MatchUi.CreateCanvas(transform, "LapRaceHud");
            _status = MatchUi.CreateLabel(_canvas, "Status", 54, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 90f));
            _hint = MatchUi.CreateLabel(_canvas, "Hint", 42, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1000f, 120f));
            _banner = MatchUi.CreateLabel(_canvas, "Banner", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1000f, 200f));
            _tap = MatchUi.CreateLabel(_canvas, "Tap", 48, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1000f, 90f));
            _banner.gameObject.SetActive(false);
            _tap.gameObject.SetActive(false);
        }

        private void ShowHud()
        {
            int lap = _match.LapOf(0);
            int position = _match.PositionOf(0);
            if (lap != _shownLap || position != _shownPosition)
            {
                _shownLap = lap;
                _shownPosition = position;
                _status.text = "LAP " + lap + "/" + _match.Laps + "    POS " + position + "/" + _match.ParticipantCount;
            }

            if (_phase == Phase.Countdown)
            {
                int ready = Mathf.CeilToInt(Mathf.Max(0f, _countdown));
                if (ready != _shownReady)
                {
                    _shownReady = ready;
                    _hint.text = "GET READY  " + ready;
                }
            }
            else if (_shownReady != 0)
            {
                _shownReady = 0;
                _hint.text = "";
            }

            bool finished = _phase == Phase.Finished;
            if (finished == _bannerShown)
                return;

            _bannerShown = finished;
            _banner.gameObject.SetActive(finished);
            _tap.gameObject.SetActive(finished);
            if (!finished)
                return;

            _banner.text = _match.Winner == 0 ? "YOU WIN!" : "BOT " + _match.Winner + " WINS";
            _tap.text = "TAP TO RESTART";
        }
    }
}
