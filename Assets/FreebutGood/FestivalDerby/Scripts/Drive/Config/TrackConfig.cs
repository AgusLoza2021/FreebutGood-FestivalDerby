using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// Where the race happens: the baked map (road, walls, starting grid, racing line) and the kit that
    /// dresses it with kerbs, edge lines and trackside props.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackConfig : MonoBehaviour
    {
        [Tooltip("The baked circuit to drive (Map_FestivalCircuit, Map_BigOval...). Bake your own with " +
            "Festival Derby > Bake Map From Selection.")]
        [SerializeField] private MapAsset _map;

        [Tooltip("Kerbs, edge lines and trackside props. Empty leaves the circuit bare.")]
        [SerializeField] private TracksideKit _trackside;

        [Tooltip("Seed for the trackside props: the same seed always dresses the circuit the same way.")]
        [SerializeField] private uint _dressingSeed = 7u;

        public MapAsset Map
        {
            get { return _map; }
            set { _map = value; }
        }

        public TracksideKit Trackside
        {
            get { return _trackside; }
            set { _trackside = value; }
        }

        public uint DressingSeed
        {
            get { return _dressingSeed; }
            set { _dressingSeed = value; }
        }
    }
}
