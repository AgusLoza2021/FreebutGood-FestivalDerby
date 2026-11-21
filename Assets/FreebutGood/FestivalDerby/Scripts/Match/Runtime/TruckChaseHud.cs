using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Match
{
    /// <summary>
    /// The truck chase's on-screen text, built in code like the touch controls: the time to the next
    /// elimination and cars left, a one-line hint about what to do now, and the end-of-match banner.
    /// Text is only rebuilt when the number it shows changes, so a steady frame allocates nothing.
    /// </summary>
    public sealed class TruckChaseHud
    {
        private enum Hint
        {
            None,
            GetReady,
            Chase,
            Holding,
            Passed,
            Eliminated
        }

        private Canvas _canvas;
        private Text _status;
        private Text _hint;
        private Text _banner;
        private Text _tap;

        private int _clock = -1;
        private int _alive = -1;
        private Hint _shownHint = Hint.None;
        private int _shownTenths = -1;
        private int _shownReady = -1;
        private bool _bannerShown;

        public static TruckChaseHud Build(Transform parent)
        {
            var hud = new TruckChaseHud();
            hud.BuildCanvas(parent);
            return hud;
        }

        public void Show(TruckChaseSession session)
        {
            TruckChaseMatch match = session.Match;
            if (match == null || _canvas == null)
                return;

            int clock = Mathf.CeilToInt(Mathf.Max(0f, match.ClockRemaining));
            int alive = match.AliveCount;
            if (clock != _clock || alive != _alive)
            {
                _clock = clock;
                _alive = alive;
                _status.text = "OUT IN " + clock + "    " + alive + " LEFT";
            }

            ShowHint(session, match);
            ShowBanner(session, match);
        }

        private void ShowHint(TruckChaseSession session, TruckChaseMatch match)
        {
            if (session.State == TruckChaseSession.Phase.Finished)
            {
                SetHint(Hint.None, "");
                return;
            }

            if (session.State == TruckChaseSession.Phase.Countdown)
            {
                int ready = Mathf.CeilToInt(session.CountdownSeconds);
                if (_shownHint != Hint.GetReady || ready != _shownReady)
                {
                    _shownReady = ready;
                    _shownHint = Hint.GetReady;
                    _hint.text = "GET READY  " + ready;
                }

                return;
            }

            if (!match.IsAlive(0))
            {
                SetHint(Hint.Eliminated, "ELIMINATED - WATCH THE REST");
                return;
            }

            if (!match.IsArmed(0))
            {
                SetHint(Hint.Passed, "YOU PASSED THE TRUCK - GO ROUND AND COME BACK BEHIND IT");
                return;
            }

            float seconds = match.BoardSeconds(0);
            if (seconds > 0f)
            {
                int tenths = Mathf.FloorToInt(seconds * 10f);
                if (_shownHint != Hint.Holding || tenths != _shownTenths)
                {
                    _shownHint = Hint.Holding;
                    _shownTenths = tenths;
                    _hint.text = "HOLD  " + (tenths / 10) + "." + (tenths % 10) + " / " + session.RequiredSeconds.ToString("0.#");
                }

                return;
            }

            SetHint(Hint.Chase, "CATCH THE TRUCK - DRIVE UP THE RAMP");
        }

        private void SetHint(Hint hint, string text)
        {
            if (_shownHint == hint)
                return;

            _shownHint = hint;
            _hint.text = text;
        }

        private void ShowBanner(TruckChaseSession session, TruckChaseMatch match)
        {
            bool finished = session.State == TruckChaseSession.Phase.Finished;
            if (finished == _bannerShown)
                return;

            _bannerShown = finished;
            _banner.gameObject.SetActive(finished);
            _tap.gameObject.SetActive(finished);
            if (!finished)
                return;

            _banner.text = match.OutcomeFor(0) == MatchOutcome.Won ? "YOU WIN!" : session.NameOf(match.Winner) + " WINS";
            _tap.text = "TAP TO RESTART";
        }

        private void BuildCanvas(Transform parent)
        {
            _canvas = MatchUi.CreateCanvas(parent, "TruckChaseHud");

            _status = MatchUi.CreateLabel(_canvas, "Status", 54, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 90f));
            _hint = MatchUi.CreateLabel(_canvas, "Hint", 42, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1000f, 120f));
            _banner = MatchUi.CreateLabel(_canvas, "Banner", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1000f, 200f));
            _tap = MatchUi.CreateLabel(_canvas, "Tap", 48, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1000f, 90f));
            _banner.gameObject.SetActive(false);
            _tap.gameObject.SetActive(false);
        }
    }
}
