using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FestivalDerby.Look
{
    /// <summary>
    /// The scene facts the applier needs but cannot discover by itself: which geometry tree to paint,
    /// which light is the key, which camera renders the run, and where the ground is.
    /// </summary>
    /// <remarks>
    /// Passed in rather than found by the applier so the applier never searches the scene: the
    /// bootstrap already owns every one of these objects, and a second finder is a second opinion
    /// about which light or camera is the run's.
    /// </remarks>
    public readonly struct LookScene
    {
        /// <summary>Root of the built circuit. Every renderer below it is a palette candidate.</summary>
        public readonly Transform GeometryRoot;

        /// <summary>The directional light the look re-colours. Null leaves the light group alone.</summary>
        public readonly Light Sun;

        /// <summary>The camera the run renders through. Null leaves the camera group alone.</summary>
        public readonly Camera RenderCamera;

        /// <summary>
        /// A material whose shader the ground fill clones. Reusing the circuit's own shader is what
        /// keeps the fill working in a stripped player: the shader is already referenced by an
        /// asset in the build instead of being found by name at runtime.
        /// </summary>
        public readonly Material ShaderSource;

        /// <summary>World XZ centre of the racing surface.</summary>
        public readonly Vector2 GroundCenter;

        /// <summary>Racing surface extent, X is width and Y is length.</summary>
        public readonly Vector2 GroundSize;

        /// <summary>World Y of the face the cars drive on.</summary>
        public readonly float GroundY;

        public LookScene(Transform geometryRoot, Light sun, Camera renderCamera, Material shaderSource,
            Vector2 groundCenter, Vector2 groundSize, float groundY)
        {
            GeometryRoot = geometryRoot;
            Sun = sun;
            RenderCamera = renderCamera;
            ShaderSource = shaderSource;
            GroundCenter = groundCenter;
            GroundSize = groundSize;
            GroundY = groundY;
        }
    }

    /// <summary>
    /// Applies a <see cref="LookSettings"/> to one live scene and puts every touched global back on
    /// demand. It never writes an asset: the palette goes through per-renderer property blocks, so
    /// the map, the circuit material and the project's saved assets stay byte for byte as authored.
    /// </summary>
    /// <remarks>
    /// The applier is an object with a lifetime rather than a static Apply/Revert pair because the
    /// previous values belong to the run that changed them: keeping them in fields means one run's
    /// restore can never read a value another run already overwrote. <see cref="Dispose"/> is
    /// idempotent and safe against renderers that were destroyed first, which is why the bootstrap
    /// disposes the look before it tears the circuit down.
    /// </remarks>
    public sealed class LookApplier
    {
        /// <summary>The name the runtime ground fill is created with, so a test can find it by name.</summary>
        public const string GroundFillName = "Look ground fill (runtime)";

        /// <summary>Prefix every runtime palette material shares, so a test can tell them apart.</summary>
        public const string RuntimeMaterialPrefix = "Look runtime material (";

        /// <summary>The name the runtime global post volume is created with, so a test can find it.</summary>
        public const string PostVolumeName = "Look post volume (runtime)";

        // Cached ids: Shader.PropertyToID allocates a string lookup the first time and every
        // palette write would otherwise pay it again inside the run's first frame.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");

        private enum LookSurface
        {
            Asphalt,
            Ground,
            Wall,
            Kerb
        }

        private sealed class BlockRestore
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Previous;
        }

        private sealed class MaterialSwap
        {
            public Renderer Renderer;
            public Material Original;
        }

        private sealed class FlatMaterial
        {
            public Material Source;
            public LookSurface Surface;
            public Material Runtime;
        }

        private readonly LookSettings _settings;
        private readonly List<BlockRestore> _blocks = new List<BlockRestore>(32);
        private readonly List<MaterialSwap> _swaps = new List<MaterialSwap>(32);
        private readonly List<FlatMaterial> _flatMaterials = new List<FlatMaterial>(8);

        private Light _sun;
        private Color _sunColor;
        private float _sunIntensity;
        private Quaternion _sunRotation;
        private float _sunShadowStrength;

        private Camera _camera;
        private CameraClearFlags _cameraClearFlags;
        private Color _cameraBackground;
        private float _cameraFov;

        private AmbientMode _ambientMode;
        private Color _ambientSkyColor;
        private Color _ambientEquatorColor;
        private Color _ambientGroundColor;
        private Color _ambientLight;
        private float _ambientIntensity;

        private GameObject _groundFill;
        private Material _groundFillMaterial;

        private GameObject _postVolume;
        private UniversalAdditionalCameraData _postCameraData;
        private bool _postWasRendering;
        private bool _disposed;

        private LookApplier(in LookSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Applies the look and returns the handle that restores it. A disabled setting returns null
        /// instead of a no-op handle, so the caller's "did the look run?" question is one null check
        /// rather than a guess from the settings.
        /// </summary>
        public static LookApplier Apply(in LookSettings settings, in LookScene scene)
        {
            if (!settings.Enabled)
                return null;

            var applier = new LookApplier(settings);
            applier.ApplyGroups(scene);
            return applier;
        }

        private void ApplyGroups(in LookScene scene)
        {
            ApplySun(scene.Sun);
            ApplyAmbient();
            ApplyCamera(scene.RenderCamera);
            if (_settings.PostProfile != null)
                ApplyPost(scene.RenderCamera);
            if (_settings.PaletteOverrideEnabled)
                ApplyPalette(scene);
            if (_settings.GroundFillEnabled)
                ApplyGroundFill(scene);
        }

        private void ApplySun(Light sun)
        {
            if (sun == null)
                return;

            _sun = sun;
            _sunColor = sun.color;
            _sunIntensity = sun.intensity;
            _sunRotation = sun.transform.rotation;
            // Shadow strength lives on the light rather than on QualitySettings on purpose: a look
            // asset that reached into global quality would change every scene in the project.
            _sunShadowStrength = sun.shadowStrength;

            sun.color = _settings.SunColor;
            sun.intensity = _settings.SunIntensity;
            sun.transform.rotation = Quaternion.Euler(_settings.SunEulerAngles);
            sun.shadowStrength = _settings.SunShadowStrength;
        }

        private void ApplyAmbient()
        {
            _ambientMode = RenderSettings.ambientMode;
            _ambientSkyColor = RenderSettings.ambientSkyColor;
            _ambientEquatorColor = RenderSettings.ambientEquatorColor;
            _ambientGroundColor = RenderSettings.ambientGroundColor;
            _ambientLight = RenderSettings.ambientLight;
            _ambientIntensity = RenderSettings.ambientIntensity;

            if (_settings.AmbientTrilight)
            {
                // Trilight is the only ambient mode that gives the ground a different fill from the
                // sky, which is what stops the shadows reading as flat holes.
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = _settings.AmbientSky;
                RenderSettings.ambientEquatorColor = _settings.AmbientEquator;
                RenderSettings.ambientGroundColor = _settings.AmbientGround;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = _settings.AmbientSky;
            }

            RenderSettings.ambientIntensity = 1f;
        }

        private void ApplyCamera(Camera camera)
        {
            if (camera == null)
                return;

            _camera = camera;
            _cameraClearFlags = camera.clearFlags;
            _cameraBackground = camera.backgroundColor;
            _cameraFov = camera.fieldOfView;

            // Solid colour rather than Skybox: the project owns no skybox material, so the inherited
            // Skybox clear renders the grey editor default and reads as a hole in the world.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = _settings.CameraBackground;
            camera.fieldOfView = _settings.CameraFov;
        }

        /// <summary>
        /// Grades the run through a global volume this applier owns. The volume references the shared
        /// profile asset and never edits it, so the asset on disk stays the single source of the grade.
        /// The camera's post flag is part of the camera's authored state, which is why it is restored.
        /// </summary>
        private void ApplyPost(Camera camera)
        {
            if (camera == null)
                return;

            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null)
            {
                _postCameraData = cameraData;
                _postWasRendering = cameraData.renderPostProcessing;
                cameraData.renderPostProcessing = true;
            }

            var volumeObject = new GameObject(PostVolumeName);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            // Above any authored scene volume, so the run's grade is the one that reads.
            volume.priority = 100f;
            volume.sharedProfile = _settings.PostProfile;
            _postVolume = volumeObject;
        }

        private void ApplyPalette(in LookScene scene)
        {
            Transform root = scene.GeometryRoot;
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                LookSurface surface;
                if (!TryClassify(renderer.gameObject.name, out surface))
                    continue;

                if (SwapToRuntimeMaterial(renderer, surface, scene))
                {
                    // The shared runtime material carries the colour. Any per-renderer property block is
                    // cleared, because a renderer with a block is not SRP-Batcher compatible: on the baked
                    // map that turned ~500 wall boxes into ~500 draw calls. The old block is kept so
                    // Dispose can put it back exactly.
                    if (renderer.HasPropertyBlock())
                    {
                        var previous = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(previous);
                        renderer.SetPropertyBlock(null);
                        _blocks.Add(new BlockRestore { Renderer = renderer, Previous = previous });
                    }

                    continue;
                }

                // No runtime material could be built (no lit shader at all): a property block is the
                // only way left to paint this renderer, so it keeps the old, unbatched path.
                var fallbackPrevious = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(fallbackPrevious);

                var applied = new MaterialPropertyBlock();
                WriteBlock(applied, surface, ColorFor(surface), renderer.sharedMaterial);
                renderer.SetPropertyBlock(applied);
                _blocks.Add(new BlockRestore { Renderer = renderer, Previous = fallbackPrevious });
            }
        }

        /// <summary>
        /// Repaints one renderer with a runtime clone of its material. A property block is written too,
        /// but it cannot be the mechanism the palette depends on: with the SRP Batcher the shader's own
        /// per-material constant buffer wins and the override is dropped without a word, which is
        /// exactly what happens to the baked map's road — the tile-array albedo stayed dark and blotchy
        /// with a correct block on the renderer. A material instance is read by both paths.
        /// </summary>
        private bool SwapToRuntimeMaterial(Renderer renderer, LookSurface surface, in LookScene scene)
        {
            // A renderer with no material draws nothing, so there is no surface to repaint and no
            // original to hand back on dispose.
            Material source = renderer.sharedMaterial;
            if (source == null)
                return false;

            Material runtime = ResolveRuntimeMaterial(source, surface, scene);
            if (runtime == null)
                return false;

            _swaps.Add(new MaterialSwap { Renderer = renderer, Original = source });
            renderer.sharedMaterial = runtime;
            return true;
        }

        /// <summary>
        /// The material a classified renderer is repainted with, cached per source material and
        /// surface: a ribbon of hundreds of segments costs one runtime material per surface, not one
        /// per renderer.
        /// </summary>
        private Material ResolveRuntimeMaterial(Material source, LookSurface surface, in LookScene scene)
        {
            for (int i = 0; i < _flatMaterials.Count; i++)
            {
                FlatMaterial cached = _flatMaterials[i];
                if (cached.Source == source && cached.Surface == surface)
                    return cached.Runtime;
            }

            // A source shader that carries a stock albedo map can be tinted through its own clone.
            // Anything else reads its albedo from somewhere a material property cannot reach (the
            // baked map's road samples a texture array), so it is replaced by a stock lit shader
            // instead of cloning a shader whose map stays dark.
            bool stock = source.HasProperty(BaseMapId) || source.HasProperty(MainTexId);
            Shader shader = stock ? source.shader : FindLitShader();
            if (shader == null && scene.ShaderSource != null)
                shader = scene.ShaderSource.shader;
            if (shader == null)
                return null;

            // The road is the one surface whose texture is deliberately thrown away: the baked
            // blotches are what a flat light grey road must not show.
            bool flatten = _settings.FlatAsphaltTexture && surface == LookSurface.Asphalt;

            var runtime = new Material(shader) { name = RuntimeMaterialPrefix + surface + ")" };
            Color color = ColorFor(surface);
            if (runtime.HasProperty(BaseColorId))
                runtime.SetColor(BaseColorId, color);
            if (runtime.HasProperty(ColorId))
                runtime.SetColor(ColorId, color);

            CopyMap(source, runtime, BaseMapId, flatten);
            CopyMap(source, runtime, MainTexId, flatten);

            // Asphalt is matte: without these the stock lit shader adds a specular highlight the
            // baked surface had none of, and the road reads as wet.
            if (runtime.HasProperty(SmoothnessId))
                runtime.SetFloat(SmoothnessId, 0.05f);
            if (runtime.HasProperty(GlossinessId))
                runtime.SetFloat(GlossinessId, 0.05f);
            if (runtime.HasProperty(SpecColorId))
                runtime.SetColor(SpecColorId, Color.black);

            _flatMaterials.Add(new FlatMaterial { Source = source, Surface = surface, Runtime = runtime });
            return runtime;
        }

        private static void CopyMap(Material source, Material runtime, int id, bool flatten)
        {
            if (!runtime.HasProperty(id))
                return;

            if (flatten)
            {
                runtime.SetTexture(id, Texture2D.whiteTexture);
                return;
            }

            if (source.HasProperty(id))
                runtime.SetTexture(id, source.GetTexture(id));
        }

        /// <summary>
        /// The two stock lit shaders the project's own runtime geometry already requires. Shader.Find only
        /// sees shaders a build includes, and no authored material used Simple Lit, so the player silently
        /// fell back to Lit while the Editor drew Simple Lit. Resources/Look/SimpleLitShaderAnchor.mat
        /// exists only to pull Simple Lit into every build; do not delete it as unused.
        /// </summary>
        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Simple Lit")
                   ?? Shader.Find("Universal Render Pipeline/Lit");
        }

        private Color ColorFor(LookSurface surface)
        {
            switch (surface)
            {
                case LookSurface.Asphalt:
                    return _settings.AsphaltColor;
                case LookSurface.Ground:
                    return _settings.GroundColor;
                case LookSurface.Kerb:
                    return _settings.KerbColor;
                default:
                    return _settings.WallColor;
            }
        }

        /// <summary>
        /// Maps a built object's name to the surface it represents. Naming is the only signal the
        /// built geometry carries, so the match is deliberately generous on the words the factories
        /// use and returns false for anything else rather than repainting an unrecognized object.
        /// </summary>
        private static bool TryClassify(string objectName, out LookSurface surface)
        {
            surface = LookSurface.Asphalt;
            if (string.IsNullOrEmpty(objectName))
                return false;

            string name = objectName.ToLowerInvariant();

            // Kerbs win over walls: both factories name the striped ribbon "kerb", and it must not
            // fall into the generic "wall" bucket that also catches barriers and route markers.
            if (name.Contains("kerb") || name.Contains("curb"))
            {
                surface = LookSurface.Kerb;
                return true;
            }

            if (name.Contains("asphalt") || name.Contains("road"))
            {
                surface = LookSurface.Asphalt;
                return true;
            }

            if (name.Contains("wall") || name.Contains("barrier") || name.Contains("edge")
                || name.Contains("gate") || name.Contains("segment"))
            {
                surface = LookSurface.Wall;
                return true;
            }

            if (name.Contains("ground") || name.Contains("floor") || name.Contains("grass"))
            {
                surface = LookSurface.Ground;
                return true;
            }

            return false;
        }

        private void WriteBlock(MaterialPropertyBlock block, LookSurface surface, Color color, Material material)
        {
            if (material != null)
            {
                // Written by property name so the same look works on URP Lit, URP Simple Lit and any
                // future shader that keeps one of the two conventional colour properties.
                if (material.HasProperty(BaseColorId))
                    block.SetColor(BaseColorId, color);
                if (material.HasProperty(ColorId))
                    block.SetColor(ColorId, color);
            }
            else
            {
                block.SetColor(BaseColorId, color);
            }

            if (surface != LookSurface.Asphalt || !_settings.FlatAsphaltTexture)
                return;

            // A baked blotch map darkens the road no matter what base colour is written, so the only
            // way to a flat light grey is to hand the shader a white texture for one run.
            if (material != null && material.HasProperty(BaseMapId))
                block.SetTexture(BaseMapId, Texture2D.whiteTexture);
            if (material != null && material.HasProperty(MainTexId))
                block.SetTexture(MainTexId, Texture2D.whiteTexture);
        }

        private void ApplyGroundFill(in LookScene scene)
        {
            float size = Mathf.Max(scene.GroundSize.x, scene.GroundSize.y) + _settings.GroundFillMargin * 2f;
            if (!LookDefaults.IsFinite(size) || size <= 0f)
                size = 200f;

            Shader shader = scene.ShaderSource != null ? scene.ShaderSource.shader : null;
            if (shader == null)
                shader = FindLitShader();
            if (shader == null)
            {
                // A fill with no shader would be an invisible object that still costs a draw call,
                // so the whole feature is skipped rather than half-created.
                return;
            }

            var fill = GameObject.CreatePrimitive(PrimitiveType.Plane);
            fill.name = GroundFillName;

            // Visual only: the racing surface's collider is the ground every car drives on, and a
            // second coplanar collider under it would just be a surface nothing may ever hit.
            var collider = fill.GetComponent<Collider>();
            if (collider != null)
                DestroyObject(collider);

            fill.transform.position = new Vector3(scene.GroundCenter.x,
                scene.GroundY - _settings.GroundFillDepth, scene.GroundCenter.y);
            // Unity's plane primitive is 10 units across, so the scale is the wanted size over ten.
            fill.transform.localScale = new Vector3(size * 0.1f, 1f, size * 0.1f);

            _groundFillMaterial = new Material(shader);
            _groundFillMaterial.name = "Look ground fill material (runtime)";
            if (_groundFillMaterial.HasProperty(BaseColorId))
                _groundFillMaterial.SetColor(BaseColorId, _settings.GroundColor);
            if (_groundFillMaterial.HasProperty(ColorId))
                _groundFillMaterial.SetColor(ColorId, _settings.GroundColor);

            var fillRenderer = fill.GetComponent<Renderer>();
            if (fillRenderer != null)
                fillRenderer.sharedMaterial = _groundFillMaterial;

            _groundFill = fill;
        }

        /// <summary>
        /// Puts every global back where it was found and destroys the runtime fill with its material.
        /// Idempotent, and tolerant of a renderer that was destroyed before the look: a destroyed
        /// object compares equal to null, so the restore simply skips it.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                BlockRestore restore = _blocks[i];
                if (restore.Renderer == null)
                    continue;

                // An empty block is the renderer's "no override" state; passing null is how Unity
                // clears it, and passing the empty block instead would leave a live empty block.
                restore.Renderer.SetPropertyBlock(restore.Previous != null && !restore.Previous.isEmpty
                    ? restore.Previous
                    : null);
            }
            _blocks.Clear();

            // Materials go back before the ground fill is torn down: the flat material is the last
            // live reference to a shader the map no longer uses, and a renderer left pointing at a
            // destroyed material draws magenta for the rest of the frame.
            for (int i = _swaps.Count - 1; i >= 0; i--)
            {
                MaterialSwap swap = _swaps[i];
                if (swap.Renderer == null || swap.Original == null)
                    continue;

                swap.Renderer.sharedMaterial = swap.Original;
            }
            _swaps.Clear();

            if (_sun != null)
            {
                _sun.color = _sunColor;
                _sun.intensity = _sunIntensity;
                _sun.transform.rotation = _sunRotation;
                _sun.shadowStrength = _sunShadowStrength;
                _sun = null;
            }

            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientSkyColor = _ambientSkyColor;
            RenderSettings.ambientEquatorColor = _ambientEquatorColor;
            RenderSettings.ambientGroundColor = _ambientGroundColor;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientIntensity = _ambientIntensity;

            if (_camera != null)
            {
                _camera.clearFlags = _cameraClearFlags;
                _camera.backgroundColor = _cameraBackground;
                _camera.fieldOfView = _cameraFov;
                _camera = null;
            }

            if (_postCameraData != null)
                _postCameraData.renderPostProcessing = _postWasRendering;
            _postCameraData = null;
            DestroyObject(_postVolume);
            _postVolume = null;

            // The fill and its cloned material are created here, so nothing else in the scene owns
            // them and nothing else may destroy them.
            DestroyObject(_groundFill);
            _groundFill = null;
            DestroyObject(_groundFillMaterial);
            _groundFillMaterial = null;

            for (int i = 0; i < _flatMaterials.Count; i++)
                DestroyObject(_flatMaterials[i].Runtime);
            _flatMaterials.Clear();
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
