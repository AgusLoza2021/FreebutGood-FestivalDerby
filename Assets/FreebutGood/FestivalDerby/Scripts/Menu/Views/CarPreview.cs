using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// A turntable for the garage: a camera looks at a spinning car and renders into a texture that a
    /// RawImage shows. The rig lives outside the Canvas (a camera cannot sit under one) and far from the
    /// origin so nothing else is ever in frame.
    /// </summary>
    public sealed class CarPreview : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [Tooltip("The car is parented here and the turntable spins this.")]
        [SerializeField] private Transform _anchor;
        [SerializeField] private RawImage _target;
        [SerializeField] private float _spinDegreesPerSecond = 28f;
        [Tooltip("The car is scaled so its longest side is this many units.")]
        [SerializeField] private float _fitSize = 2.6f;
        [SerializeField] private int _textureSize = 1024;

        private RenderTexture _texture;
        private GameObject _instance;

        public void Show(GameObject model)
        {
            Clear();
            if (_anchor == null)
                return;

            _anchor.localRotation = Quaternion.identity;
            if (_target != null)
                _target.enabled = model != null;
            if (model == null)
                return;

            _instance = Instantiate(model, _anchor);
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
            Fit(_instance.transform);
        }

        private void Fit(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest <= 0.0001f)
                return;

            float scale = _fitSize / longest;
            Vector3 pivot = root.position;
            root.localScale *= scale;
            Vector3 centre = pivot + (bounds.center - pivot) * scale;
            root.position += _anchor.position - centre;
        }

        private void Clear()
        {
            if (_instance != null)
                Destroy(_instance);
            _instance = null;
        }

        private void OnEnable()
        {
            if (_texture == null && _camera != null)
            {
                _texture = new RenderTexture(_textureSize, _textureSize, 24, RenderTextureFormat.ARGB32)
                {
                    name = "MenuCarPreview",
                };
                _camera.targetTexture = _texture;
            }

            if (_target != null)
                _target.texture = _texture;
            if (_camera != null)
                _camera.enabled = true;
        }

        private void OnDisable()
        {
            if (_camera != null)
                _camera.enabled = false;
            if (_anchor != null)
                _anchor.localRotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            Clear();
            if (_camera != null)
                _camera.targetTexture = null;
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void Update()
        {
            if (_instance != null && _anchor != null)
                _anchor.Rotate(0f, _spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }
    }
}
