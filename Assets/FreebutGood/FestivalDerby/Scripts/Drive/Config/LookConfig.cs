using FestivalDerby.Look;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The mood of the scene: sun, ambient light, sky colour, track palette and post-processing, all from
    /// one <see cref="LookProfile"/> asset. Swap the asset to restyle the whole game.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LookConfig : MonoBehaviour
    {
        [Tooltip("Lighting, palette and post-processing preset (LookProfile_Cozy, LookProfile_CircuitSuperstars). " +
            "Empty keeps Unity's plain defaults.")]
        [SerializeField] private LookProfile _profile;

        public LookProfile Profile
        {
            get { return _profile; }
            set { _profile = value; }
        }
    }
}
