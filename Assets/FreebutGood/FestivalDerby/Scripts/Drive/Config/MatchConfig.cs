using FestivalDerby.Match;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// How the run is won or lost. With the mode on FreeDrive nothing here is read and the drive behaves
    /// as it always did. The menu's choice overrides the mode when "Use Menu Selection" is on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchConfig : MonoBehaviour
    {
        [Tooltip("Free drive has no rules. Truck chase: catch the truck, last car is eliminated. Laps: classic race.")]
        [SerializeField] private MatchMode _mode = MatchMode.FreeDrive;

        [Tooltip("Take the mode from the main menu's choice when one was made. Off = always use the mode above.")]
        [SerializeField] private bool _useMenuSelection = true;

        [Header("Truck chase")]
        [Tooltip("Seconds a car must stay on the truck bed, without leaving it, to win.")]
        [Min(0.5f)]
        [SerializeField] private float _boardSeconds = 3f;

        [Tooltip("Seconds before the car furthest from the truck is eliminated. It restarts after every elimination.")]
        [Min(2f)]
        [SerializeField] private float _eliminationSeconds = 20f;

        [Tooltip("Seconds the truck and the bots hold on the grid at the start.")]
        [Min(0f)]
        [SerializeField] private float _startDelay = 3f;

        [Tooltip("How fast the truck drives, in m/s. Keep it well under the car's top speed so a car can catch it.")]
        [Min(2f)]
        [SerializeField] private float _truckSpeed = 11f;

        [Tooltip("How far ahead of the player's grid slot the truck waits, in metres of racing line.")]
        [Min(10f)]
        [SerializeField] private float _truckLeadMeters = 45f;

        public MatchMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        public bool UseMenuSelection
        {
            get { return _useMenuSelection; }
        }

        public float BoardSeconds
        {
            get { return _boardSeconds; }
        }

        public float EliminationSeconds
        {
            get { return _eliminationSeconds; }
        }

        public float StartDelay
        {
            get { return _startDelay; }
        }

        public float TruckSpeed
        {
            get { return _truckSpeed; }
        }

        public float TruckLeadMeters
        {
            get { return _truckLeadMeters; }
        }

        private void OnValidate()
        {
            _boardSeconds = Mathf.Max(0.5f, _boardSeconds);
            _eliminationSeconds = Mathf.Max(2f, _eliminationSeconds);
            _startDelay = Mathf.Max(0f, _startDelay);
            _truckSpeed = Mathf.Max(2f, _truckSpeed);
            _truckLeadMeters = Mathf.Max(10f, _truckLeadMeters);
        }
    }
}
