using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace FestivalDerby.Look.Tests
{
    /// <summary>
    /// Pins the applier's promise: it changes global render state for exactly as long as it is alive,
    /// and disposing it puts every value back and leaves no runtime object behind.
    /// </summary>
    public sealed class LookApplierPlayModeTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private LookProfile _profile;
        private GameObject _root;
        private GameObject _lightObject;
        private GameObject _cameraObject;
        private Light _sun;
        private Camera _camera;
        private Renderer _roadRenderer;
        private Material _roadMaterial;
        private Material _shaderSource;

        private AmbientMode _ambientMode;
        private Color _ambientSky;
        private Color _ambientEquator;
        private Color _ambientGround;
        private Color _ambientLight;
        private float _ambientIntensity;
        private Color _sunColor;
        private float _sunIntensity;
        private Quaternion _sunRotation;
        private float _sunShadowStrength;
        private CameraClearFlags _cameraClearFlags;
        private Color _cameraBackground;
        private float _cameraFov;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<LookProfile>();
            _profile.LookProfileEnabled = true;

            _root = new GameObject("LookApplierTestRoot");

            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "Road test";
            road.transform.SetParent(_root.transform, false);
            _roadRenderer = road.GetComponent<Renderer>();
            _roadMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _roadRenderer.sharedMaterial = _roadMaterial;

            _lightObject = new GameObject("TestSun");
            _sun = _lightObject.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1.4f;

            _cameraObject = new GameObject("TestCamera");
            _camera = _cameraObject.AddComponent<Camera>();

            _shaderSource = new Material(_roadMaterial);

            // Every global the applier writes is captured here, so the restore assertion is written
            // against values this test chose rather than against the test scene's defaults.
            _ambientMode = RenderSettings.ambientMode;
            _ambientSky = RenderSettings.ambientSkyColor;
            _ambientEquator = RenderSettings.ambientEquatorColor;
            _ambientGround = RenderSettings.ambientGroundColor;
            _ambientLight = RenderSettings.ambientLight;
            _ambientIntensity = RenderSettings.ambientIntensity;
            _sunColor = _sun.color;
            _sunIntensity = _sun.intensity;
            _sunRotation = _sun.transform.rotation;
            _sunShadowStrength = _sun.shadowStrength;
            _cameraClearFlags = _camera.clearFlags;
            _cameraBackground = _camera.backgroundColor;
            _cameraFov = _camera.fieldOfView;

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientSkyColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.12f, 0.13f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.03f, 1f);
            RenderSettings.ambientIntensity = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientIntensity = _ambientIntensity;

            Destroy(_profile);
            Destroy(_root);
            Destroy(_lightObject);
            Destroy(_cameraObject);
            Destroy(_roadMaterial);
            Destroy(_shaderSource);
        }

        [Test]
        public void Apply_DisabledSettings_ReturnsNothing()
        {
            Assert.IsNull(LookApplier.Apply(new LookSettings(null),
                new LookScene(null, null, null, null, Vector2.zero, Vector2.zero, 0f)));
        }

        [UnityTest]
        public IEnumerator Apply_ThenDispose_RestoresEveryGlobalAndLeavesNoRuntimes()
        {
            LookSettings settings = LookProfile.Resolve(_profile);
            var scene = new LookScene(_root.transform, _sun, _camera, _shaderSource,
                new Vector2(0f, 0f), new Vector2(60f, 120f), 0f);

            var previousBlock = new MaterialPropertyBlock();
            _roadRenderer.GetPropertyBlock(previousBlock);
            Assert.IsTrue(previousBlock.isEmpty, "The fixture must start with no renderer override.");

            LookApplier applier = LookApplier.Apply(settings, scene);
            Assert.IsNotNull(applier);

            // Applied: light, ambient, camera, palette and the fill all moved.
            Assert.AreEqual(settings.SunColor, _sun.color);
            Assert.AreEqual(settings.SunIntensity, _sun.intensity);
            Assert.AreEqual(settings.SunShadowStrength, _sun.shadowStrength);
            // Rotation is compared by angle: a Quaternion round-trip through the transform is not
            // bit-identical, but a hundredth of a degree is far below anything a look can mean.
            Assert.That(Quaternion.Angle(_sun.transform.rotation, Quaternion.Euler(settings.SunEulerAngles)),
                Is.LessThan(0.01f));
            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode);
            Assert.AreEqual(settings.AmbientSky, RenderSettings.ambientSkyColor);
            Assert.AreEqual(settings.AmbientEquator, RenderSettings.ambientEquatorColor);
            Assert.AreEqual(settings.AmbientGround, RenderSettings.ambientGroundColor);
            Assert.AreEqual(CameraClearFlags.SolidColor, _camera.clearFlags);
            Assert.AreEqual(settings.CameraBackground, _camera.backgroundColor);
            Assert.AreEqual(settings.CameraFov, _camera.fieldOfView, 0.0001f);
            Assert.IsNotNull(GameObject.Find(LookApplier.GroundFillName), "The ground fill must exist while applied.");

            // No per-renderer block: a renderer carrying one is not SRP-Batcher compatible, which on the
            // baked map turned every wall box into its own draw call.
            Assert.IsFalse(_roadRenderer.HasPropertyBlock(),
                "The palette must colour through the shared runtime material, not a property block.");

            // The material instance is what carries the colour: with the SRP Batcher a per-material
            // property in the shader's constant buffer wins over a block anyway.
            Assert.AreNotSame(_roadMaterial, _roadRenderer.sharedMaterial);
            Assert.IsTrue(_roadRenderer.sharedMaterial.name.StartsWith(LookApplier.RuntimeMaterialPrefix),
                $"Expected a runtime palette material, got '{_roadRenderer.sharedMaterial.name}'.");
            Assert.AreEqual(settings.AsphaltColor, _roadRenderer.sharedMaterial.GetColor(BaseColorId));

            // Idempotent: a second dispose must not throw or double-restore anything.
            applier.Dispose();
            applier.Dispose();

            Assert.AreEqual(_sunColor, _sun.color);
            Assert.AreEqual(_sunIntensity, _sun.intensity);
            Assert.AreEqual(_sunShadowStrength, _sun.shadowStrength);
            Assert.That(Quaternion.Angle(_sun.transform.rotation, _sunRotation), Is.LessThan(0.001f));
            Assert.AreEqual(AmbientMode.Skybox, RenderSettings.ambientMode);
            Assert.AreEqual(new Color(0.9f, 0.9f, 0.9f, 1f), RenderSettings.ambientSkyColor);
            Assert.AreEqual(_cameraClearFlags, _camera.clearFlags);
            Assert.AreEqual(_cameraBackground, _camera.backgroundColor);
            Assert.AreEqual(_cameraFov, _camera.fieldOfView, 0.0001f);

            var restoredBlock = new MaterialPropertyBlock();
            _roadRenderer.GetPropertyBlock(restoredBlock);
            Assert.IsTrue(restoredBlock.isEmpty, "The renderer override must be gone, not left empty but live.");
            Assert.AreSame(_roadMaterial, _roadRenderer.sharedMaterial,
                "Dispose must hand the authored material back to every repainted renderer.");

            // Destroy is deferred to the end of the frame, so the absence is asserted after one.
            yield return null;

            Assert.IsNull(GameObject.Find(LookApplier.GroundFillName),
                "A disposed look must not leave its runtime ground plane in the scene.");
            Assert.IsNull(FindGroundFillMaterial(),
                "A disposed look must not leave its cloned ground material in the scene.");
        }

        /// <summary>
        /// A surface whose shader keeps its albedo somewhere a property block cannot reach (the baked
        /// map's road reads a texture array) is repainted with a runtime material built from a stock
        /// lit shader; a surface whose shader does carry a stock albedo map is repainted with a clone of
        /// itself. Either way the authored material comes back and the runtime materials are destroyed.
        /// </summary>
        [UnityTest]
        public IEnumerator Apply_PaletteOverrides_UseRuntimeMaterialsForEverySurface()
        {
            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "Road textured";
            road.transform.SetParent(_root.transform, false);
            Renderer roadRenderer = road.GetComponent<Renderer>();
            // Unlit/Color has neither _BaseMap nor _MainTex, which is the shape of the baked road
            // shader: a material whose albedo cannot be reached from a material property.
            var roadOriginal = new Material(Shader.Find("Unlit/Color")) { name = "TextureArrayRoad" };
            roadRenderer.sharedMaterial = roadOriginal;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Segment 0";
            wall.transform.SetParent(_root.transform, false);
            Renderer wallRenderer = wall.GetComponent<Renderer>();
            wallRenderer.sharedMaterial = roadOriginal;

            var wall2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall2.name = "Segment 1";
            wall2.transform.SetParent(_root.transform, false);
            wall2.GetComponent<Renderer>().sharedMaterial = roadOriginal;

            LookSettings settings = LookProfile.Resolve(_profile);
            var scene = new LookScene(_root.transform, _sun, _camera, _shaderSource,
                new Vector2(0f, 0f), new Vector2(60f, 120f), 0f);

            LookApplier applier = LookApplier.Apply(settings, scene);

            Material flatRoad = roadRenderer.sharedMaterial;
            Material flatWall = wallRenderer.sharedMaterial;
            Assert.AreNotSame(roadOriginal, flatRoad, "The tile-array road must not keep its own material.");
            Assert.AreNotSame(roadOriginal, flatWall, "A wall over a texture-array material must be repainted.");
            Assert.IsTrue(flatRoad.name.StartsWith(LookApplier.RuntimeMaterialPrefix));
            Assert.AreEqual(settings.AsphaltColor, flatRoad.GetColor(BaseColorId));
            Assert.AreEqual(Texture2D.whiteTexture, flatRoad.GetTexture(BaseMapId),
                "A flat road must not sample the baked blotch map.");
            Assert.AreEqual(settings.WallColor, flatWall.GetColor(BaseColorId));
            // One runtime material per surface and source material, not one per renderer.
            Assert.AreSame(flatWall, wall2.GetComponent<Renderer>().sharedMaterial,
                "Two renderers of the same surface and source must share one runtime material.");
            Assert.AreNotSame(flatRoad, flatWall, "Different surfaces must not share a runtime material.");

            applier.Dispose();
            yield return null;

            Assert.AreSame(roadOriginal, roadRenderer.sharedMaterial, "Dispose must give the material back.");
            Assert.AreSame(roadOriginal, wallRenderer.sharedMaterial, "Dispose must give the material back.");
            Assert.AreSame(_roadMaterial, _roadRenderer.sharedMaterial);
            Assert.IsTrue(flatRoad == null, "The runtime asphalt material must be destroyed with the look.");
            Assert.IsTrue(flatWall == null, "The runtime wall material must be destroyed with the look.");

            Destroy(roadOriginal);
        }

        [UnityTest]
        public IEnumerator Apply_WithoutPaletteOrFill_OnlyTouchesTheGlobalsItWasAskedFor()
        {
            _profile.PaletteOverrideEnabled = false;
            _profile.GroundFillEnabled = false;
            LookSettings settings = LookProfile.Resolve(_profile);
            var scene = new LookScene(_root.transform, _sun, _camera, _shaderSource,
                new Vector2(0f, 0f), new Vector2(60f, 120f), 0f);

            LookApplier applier = LookApplier.Apply(settings, scene);

            var block = new MaterialPropertyBlock();
            _roadRenderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "A disabled palette must not paint any renderer.");
            Assert.IsNull(GameObject.Find(LookApplier.GroundFillName), "A disabled fill must not create a plane.");
            Assert.AreEqual(settings.SunColor, _sun.color);

            applier.Dispose();
            yield return null;
        }

        /// <summary>
        /// A renderer that arrives with a property block (the old per-segment tint) leaves the palette with
        /// none, so the SRP Batcher can batch it with every other renderer of the same runtime material,
        /// and gets its own block back on dispose.
        /// </summary>
        [UnityTest]
        public IEnumerator Apply_ClearsAnExistingPropertyBlockAndDisposeRestoresIt()
        {
            var tint = new MaterialPropertyBlock();
            tint.SetColor(BaseColorId, Color.magenta);
            _roadRenderer.SetPropertyBlock(tint);

            LookSettings settings = LookProfile.Resolve(_profile);
            var scene = new LookScene(_root.transform, _sun, _camera, _shaderSource,
                new Vector2(0f, 0f), new Vector2(60f, 120f), 0f);
            LookApplier applier = LookApplier.Apply(settings, scene);

            Assert.IsFalse(_roadRenderer.HasPropertyBlock(),
                "A palette-painted renderer must carry no block, or the SRP Batcher skips it.");
            Assert.AreEqual(settings.AsphaltColor, _roadRenderer.sharedMaterial.GetColor(BaseColorId));

            applier.Dispose();
            yield return null;

            var restored = new MaterialPropertyBlock();
            _roadRenderer.GetPropertyBlock(restored);
            Assert.AreEqual(Color.magenta, restored.GetColor(BaseColorId), "Dispose must put the authored block back.");
            _roadRenderer.SetPropertyBlock(null);
        }

        [UnityTest]
        public IEnumerator Apply_WithoutPostProfile_LeavesPostProcessingAlone()
        {
            var cameraData = _cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;

            LookApplier applier = LookApplier.Apply(LookProfile.Resolve(_profile), new LookScene(_root.transform,
                _sun, _camera, _shaderSource, Vector2.zero, new Vector2(60f, 120f), 0f));

            Assert.IsFalse(cameraData.renderPostProcessing, "No post profile must not turn post on.");
            Assert.IsNull(GameObject.Find(LookApplier.PostVolumeName), "No post profile must not create a volume.");

            applier.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Apply_WithPostProfile_GradesThroughAGlobalVolumeAndDisposeRestores()
        {
            var cameraData = _cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            var post = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.PostProfile = post;

            LookApplier applier = LookApplier.Apply(LookProfile.Resolve(_profile), new LookScene(_root.transform,
                _sun, _camera, _shaderSource, Vector2.zero, new Vector2(60f, 120f), 0f));

            GameObject volumeObject = GameObject.Find(LookApplier.PostVolumeName);
            Assert.IsNotNull(volumeObject, "A post profile must create the run's volume.");
            var volume = volumeObject.GetComponent<Volume>();
            Assert.IsTrue(volume.isGlobal);
            Assert.AreSame(post, volume.sharedProfile, "The volume must reference the asset, never a copy.");
            Assert.IsTrue(cameraData.renderPostProcessing, "The run camera must render post to show the grade.");

            applier.Dispose();
            yield return null;

            Assert.IsFalse(cameraData.renderPostProcessing, "Dispose must restore the camera's post flag.");
            Assert.IsNull(GameObject.Find(LookApplier.PostVolumeName), "Dispose must destroy the volume.");
            Destroy(post);
        }

        private static Material FindGroundFillMaterial()
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i] != null ? renderers[i].sharedMaterial : null;
                if (material != null && material.name.StartsWith("Look ground fill material"))
                    return material;
            }

            return null;
        }

        private static void Destroy(Object target)
        {
            if (target != null)
                Object.Destroy(target);
        }
    }
}
