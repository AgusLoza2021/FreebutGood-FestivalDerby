using FestivalDerby.Look;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The chase camera. How it frames the car (yaw mode, offsets, pivot, sharpness, look-ahead, speed FOV)
    /// is the Camera group of a <see cref="LookProfile"/> asset, so a framing can be saved and reused; this
    /// component chooses which profile frames the car and owns the clip planes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraConfig : MonoBehaviour
    {
        [Tooltip("LookProfile whose Camera group frames the car. Empty uses the Look object's profile, so the " +
            "camera and the lighting come from one asset.")]
        [SerializeField] private LookProfile _framing;

        [Tooltip("Closest distance the camera draws, in metres.")]
        [SerializeField] private float _nearClip = 0.3f;

        [Tooltip("Farthest distance the camera draws, in metres. Lower it to save GPU on small tracks.")]
        [SerializeField] private float _farClip = 900f;

        public LookProfile Framing
        {
            get { return _framing; }
            set { _framing = value; }
        }

        public float NearClip
        {
            get { return _nearClip; }
        }

        public float FarClip
        {
            get { return _farClip; }
        }

        private void OnValidate()
        {
            _nearClip = Mathf.Clamp(_nearClip, 0.01f, 10f);
            _farClip = Mathf.Clamp(_farClip, _nearClip + 1f, 5000f);
        }
    }
}
