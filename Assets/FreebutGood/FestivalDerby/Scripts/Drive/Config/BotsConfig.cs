using FestivalDerby.Bots;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The AI racers: how many, how fast, and what they look like. They drive the player's own car (see
    /// the Car object), start on the grid behind the player and follow the map's racing line.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotsConfig : MonoBehaviour
    {
        /// <summary>Same cap the spawner enforces; the slider cannot go past it either.</summary>
        public const int MaxBots = BotField.MaxBots;

        [Tooltip("How many AI cars race against you. 0 is a solo drive. Read when the race starts.")]
        [Range(0, MaxBots)]
        [SerializeField] private int _count = 5;

        [Tooltip("Share of the tyres' cornering grip the slowest bot dares to use. Lower = brakes earlier.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float _slowestPace = 0.8f;

        [Tooltip("Share of the tyres' cornering grip the fastest bot dares to use. 1 = a perfect driver.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float _fastestPace = 0.95f;

        [Tooltip("Toy model ids from the car catalog, used in turn: bot 1 gets the first, bot 2 the second...")]
        [SerializeField] private string[] _models =
        {
            "ToySedanSports", "ToyRace", "ToyHatchbackSports", "ToySedan", "ToySuv", "ToyVan",
        };

        [Tooltip("Body colours, used in turn. Saturated on purpose: a pack of toy cars must read as " +
            "different cars at a glance.")]
        [SerializeField] private Color[] _liveries =
        {
            new Color(0.90f, 0.12f, 0.12f, 1f), // red
            new Color(0.10f, 0.32f, 0.92f, 1f), // blue
            new Color(0.15f, 0.75f, 0.28f, 1f), // green
            new Color(0.98f, 0.50f, 0.05f, 1f), // orange
            new Color(0.60f, 0.20f, 0.90f, 1f), // purple
            new Color(0.05f, 0.80f, 0.85f, 1f), // cyan
            new Color(0.95f, 0.25f, 0.65f, 1f), // pink
            new Color(0.98f, 0.86f, 0.05f, 1f), // yellow
            new Color(0.55f, 0.85f, 0.10f, 1f), // lime
            new Color(0.10f, 0.15f, 0.55f, 1f), // navy
            new Color(0.95f, 0.95f, 0.95f, 1f), // white
            new Color(0.12f, 0.12f, 0.12f, 1f), // black
        };

        /// <summary>Bots to spawn, always within 0..<see cref="MaxBots"/>, whatever is assigned.</summary>
        public int Count
        {
            get { return Mathf.Clamp(_count, 0, MaxBots); }
            set { _count = Mathf.Clamp(value, 0, MaxBots); }
        }

        /// <summary>Sets the pace range for this run (the main menu's difficulty), inside the same limits as the Inspector.</summary>
        public void SetPace(float slowest, float fastest)
        {
            _slowestPace = Mathf.Clamp(slowest, 0.5f, 1f);
            _fastestPace = Mathf.Clamp(fastest, _slowestPace, 1f);
        }

        public float SlowestPace
        {
            get { return _slowestPace; }
        }

        public float FastestPace
        {
            get { return _fastestPace; }
        }

        public string[] Models
        {
            get { return _models; }
        }

        public Color[] Liveries
        {
            get { return _liveries; }
        }

        private void OnValidate()
        {
            _count = Mathf.Clamp(_count, 0, MaxBots);
            _slowestPace = Mathf.Clamp(_slowestPace, 0.5f, 1f);
            _fastestPace = Mathf.Clamp(_fastestPace, _slowestPace, 1f);
            // A colour added in the Inspector arrives as transparent black; make it a visible paint.
            if (_liveries != null)
                for (int i = 0; i < _liveries.Length; i++)
                    _liveries[i].a = 1f;
        }
    }
}
