using UnityEngine;
using UnityEngine.Rendering;

namespace FestivalDerby.World
{
    /// <summary>
    /// The ground-deformation mask: one R8 RenderTexture where the cars press tyre ruts into snow or
    /// sand. The road shader reads it in world space — 1 is intact ground, 0 is pressed to the floor.
    ///
    /// Ported from NinjaSurvivor's <c>GroundMaskDriver</c> by the same owner. The numbers and the
    /// scroll contract are that system's, tuned against a shipped game; what changed is the caller.
    /// There the driver lived on the level prefab and the level prefab WAS the gate; here nothing
    /// places a component, so the driver is configured from code by <c>SnowRutSystem</c> and an
    /// unconfigured driver costs nothing at all (no texture, no material, no branch per frame).
    ///
    /// ── Why a moving window ──────────────────────────────────────────────────────────────
    /// The mask covers <see cref="RutWindow.AreaSize"/> metres centred on the local car, not the whole
    /// world. That is RESOLUTION, not cost: the per-frame price is the texture size and the number of
    /// draws, and neither depends on the area. At 72 m over 1024 texels there are 14 texels per metre,
    /// so a 0.45 m rut is six texels wide and the shader's relief reads as a pressed groove. The same
    /// resolution spread over a 220 m map is one rut every seven texels and the relief disappears.
    ///
    /// The mask stays ANCHORED TO THE WORLD while the window moves: the origin snaps to a grid of
    /// <see cref="RutWindow.SnapTexels"/> texels, so every scroll is a whole number of texels and the
    /// copy is 1:1 with POINT sampling. That is the fine point of the whole system — the earlier
    /// version in Survival_Game reprojected with sub-texel offsets and bilinear filtering every frame,
    /// which RESAMPLES: the ruts smeared themselves away. Here not one bit is lost.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class GroundMaskDriver : MonoBehaviour
    {
        // ---- shader contract ------------------------------------------------------------
        // Shared with Art/World/RoadSurface.shader (the _RUTS path) and both mask shaders.
        private static readonly int MaskTextureId = Shader.PropertyToID("_GroundMaskRT");
        private static readonly int MaskOriginId = Shader.PropertyToID("_GroundMaskOrigin");
        private static readonly int MaskSizeId = Shader.PropertyToID("_GroundMaskSize");
        private static readonly int MaskTexelUvId = Shader.PropertyToID("_GroundMaskTexelUV");
        private static readonly int UvOffsetId = Shader.PropertyToID("_UVOffset");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int StampDepthId = Shader.PropertyToID("_Depth");

        private const string UpdateShaderName = "Hidden/FestivalDerby/GroundMaskUpdate";
        private const string StampShaderName = "FestivalDerby/GroundMaskStamp";

        /// <summary>
        /// The live driver, or null outside a run. There is at most one: two writers on one mask is a
        /// bug, so a second driver disables the first instead of fighting it for the same globals.
        /// </summary>
        public static GroundMaskDriver Active { get; private set; }

        private readonly RutStampQueue _queue = new RutStampQueue();
        private readonly Matrix4x4[] _matrices = new Matrix4x4[RutStampQueue.Capacity];
        // Per-instance depth, uploaded with the matrices. Both arrays are sized once, so a full frame of
        // stamps costs no allocation.
        private readonly float[] _depths = new float[RutStampQueue.Capacity];
        private MaterialPropertyBlock _stampProperties;

        private RutWindow _window;
        private RenderTexture _maskTexture;
        // The other half of the scroll/fade ping-pong; never bound to the shaders.
        private RenderTexture _spareTexture;
        private Mesh _quad;
        private CommandBuffer _command;
        private Material _stampMaterial;
        private Material _updateMaterial;
        private Transform _follow;

        private float _fadeAccumulator;
        private float _lastFadeTime;
        private float _nextFadeAt;
        private bool _updateShaderWarned;

        /// <summary>True while the mask is asked to hold still, e.g. across a pause or an app resume.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>How much ground the mask recovers per second, 0..1 of its range. 0 keeps ruts forever.</summary>
        public float FadePerSecond = 0.05f;

        /// <summary>How many times per second the fade pass runs. It is the only pass that touches the whole mask.</summary>
        public float FadeHz = 8f;

        /// <summary>The window this driver currently tracks. Read-only for diagnostics, the feed and tests.</summary>
        public RutWindow Window
        {
            get { return _window; }
        }

        /// <summary>The mask texture, or null while the driver is not configured. Exposed for tests.</summary>
        public RenderTexture MaskTexture
        {
            get { return _maskTexture; }
        }

        /// <summary>Stamps refused because the frame's queue was full. Cosmetic, counted, never logged per frame.</summary>
        public int DroppedStamps
        {
            get { return _queue.Dropped; }
        }

        /// <summary>
        /// Instanced stamp draws submitted since the driver was configured. Diagnostics for the cost
        /// contract (one draw per frame at most); plain ints, so reading them costs nothing.
        /// </summary>
        public int StampDrawCount { get; private set; }

        /// <summary>Scroll/fade passes run since the driver was configured (each is one full-mask blit pair).</summary>
        public int UpdatePassCount { get; private set; }

        /// <summary>Configured, texture alive and both materials present.</summary>
        public bool IsReady
        {
            get
            {
                return _stampMaterial != null
                    && _maskTexture != null
                    && _maskTexture.IsCreated()
                    && _command != null;
            }
        }

        // ---------------------------------------------------------------- configuration

        /// <summary>
        /// Arms the driver. Called once per run by <c>SnowRutSystem</c> with the local car as
        /// <paramref name="follow"/>; a null follow leaves the window where it is, which is what a
        /// test or a parked camera wants.
        ///
        /// A driver with no stamp material is not armed: it logs and disables itself BEFORE creating
        /// any resource, so a half-authored setup cannot evict a driver that is already working.
        /// </summary>
        public void Configure(
            Transform follow,
            float areaSize,
            int resolution,
            float fadeHz,
            Material stampMaterial,
            Material updateMaterial)
        {
            if (stampMaterial == null)
            {
                Debug.LogError(
                    "[GroundMaskDriver] No stamp material — ruts cannot be drawn. " +
                    "Run 'Festival Derby/World/Create Snow Rut Materials' and assign the result to the " +
                    "driving feature config.");
                enabled = false;
                return;
            }

            if (!stampMaterial.enableInstancing)
            {
                // Every stamp is one DrawMeshInstanced, which throws on a material without instancing,
                // every frame. Refuse to arm instead, like a missing material.
                Debug.LogError(
                    "[GroundMaskDriver] The stamp material '" + stampMaterial.name + "' does not have GPU " +
                    "instancing enabled — ruts cannot be drawn. Re-run 'Festival Derby/World/Create Snow Rut Materials'.");
                enabled = false;
                return;
            }

            // Last one wins, and the loser is the OLD driver: during a run-to-run transition the new
            // driver is configured in the same frame the old one is destroyed, and Destroy is
            // deferred. Switching off the new one would leave the run with no mask.
            if (Active != null && Active != this)
                Active.enabled = false;
            Active = this;

            _follow = follow;
            _window = RutWindow.Create(areaSize, resolution);
            _stampMaterial = stampMaterial;
            // The update pass writes _UVOffset and _Fade every pass, so it runs on a private copy. Run on
            // the authored asset (as first written) it rewrote GroundMaskUpdate.mat on disk in the Editor
            // and OnDestroy then tried to Destroy a project asset ("Destroying assets is not permitted").
            if (_updateMaterial != null)
                Release(_updateMaterial);
            _updateMaterial = updateMaterial != null
                ? new Material(updateMaterial) { hideFlags = HideFlags.HideAndDontSave }
                : null;
            FadeHz = fadeHz > 0f ? fadeHz : 8f;

            if (_follow != null)
                _window.SnapTo(_follow.position.x, _follow.position.z);

            EnsureResources();
            PushGlobals();
            ClearMask();
            _lastFadeTime = Time.time;
            _nextFadeAt = _lastFadeTime;
            IsPaused = false;
        }

        /// <summary>Moves the follow target without rebuilding the mask, for a driver handover mid-run.</summary>
        public void SetFollow(Transform follow)
        {
            if (follow == null || follow == _follow)
                return;

            _follow = follow;
            _window.SnapTo(follow.position.x, follow.position.z);
            PushGlobals();
        }

        /// <summary>
        /// Starts collecting stamps again after <see cref="Pause"/>. The mask content may be gone: a
        /// RenderTexture is not our memory, and an app resume can come back with the native resource
        /// freed. <see cref="EnsureMaskAlive"/> recreates it and clears it to white (= intact ground).
        /// </summary>
        public void Resume()
        {
            IsPaused = false;
            _lastFadeTime = Time.time;
            _nextFadeAt = _lastFadeTime;
        }

        /// <summary>
        /// Stops touching the mask and drops whatever was queued. Stamps are cosmetic: a lost rut is
        /// worth less than a frame submitted against a texture the platform may have taken away.
        /// </summary>
        public void Pause()
        {
            IsPaused = true;
            _queue.Clear();
        }

        // ---------------------------------------------------------------- submitting

        /// <summary>
        /// Queues one rut footprint. World XZ, metres, rotation about Y in radians.
        ///
        /// Anything outside the window is dropped here: the projection would clip it anyway, and this
        /// way the matrix is never even written. False when the stamp was refused (outside or full).
        /// </summary>
        public bool Submit(float worldX, float worldZ, float width, float length, float yawRadians, float depth)
        {
            if (!_window.Contains(worldX, worldZ))
                return false;

            return _queue.TryEnqueue(new RutStamp(worldX, worldZ, width, length, yawRadians, depth));
        }

        /// <summary>Drains the queue into one instanced draw. Public for tests, normally called by LateUpdate.</summary>
        public void FlushStamps()
        {
            int count = _queue.Count;
            if (count == 0)
                return;

            if (count > _matrices.Length)
                count = _matrices.Length;

            for (int i = 0; i < count; i++)
            {
                RutStamp stamp = _queue[i];
                _matrices[i] = Matrix4x4.TRS(
                    new Vector3(stamp.X, 0f, stamp.Z),
                    Quaternion.Euler(0f, stamp.YawRadians * Mathf.Rad2Deg, 0f),
                    new Vector3(stamp.Width, 1f, stamp.Length));
                _depths[i] = stamp.Depth;
            }

            _queue.Clear();

            if (!IsReady || IsPaused)
                return;

            _command.Clear();
            _command.SetRenderTarget(_maskTexture);
            _command.SetViewProjectionMatrices(BuildView(), BuildProjection());
            // One instanced draw: 24 cars times 4 wheels is ONE draw call, and the depth rides along as
            // a per-instance property instead of costing a draw per depth.
            _stampProperties.Clear();
            _stampProperties.SetFloatArray(StampDepthId, _depths);
            _command.DrawMeshInstanced(_quad, 0, _stampMaterial, 0, _matrices, count, _stampProperties);
            Graphics.ExecuteCommandBuffer(_command);
            StampDrawCount++;
        }

        /// <summary>Repaints the whole mask to "intact". The only full-surface write besides the fade pass.</summary>
        public void ClearMask()
        {
            if (_maskTexture == null || !_maskTexture.IsCreated())
                return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _maskTexture;
            GL.Clear(false, true, Color.white);
            RenderTexture.active = previous;
        }

        // ---------------------------------------------------------------- frame

        private void LateUpdate()
        {
            Tick();
        }

        /// <summary>
        /// One frame of the mask: recreate a lost texture, scroll and fade, then draw the queued stamps.
        /// Public so an EditMode test (no player loop, and SendMessage asserts outside play mode) drives
        /// the exact path the runtime runs.
        /// </summary>
        public void Tick()
        {
            if (IsPaused)
                return;

            // The texture can come back from a background with its native resource freed, and a blit
            // against that is a dead handle in the command queue. Content loss is acceptable (ruts are
            // cosmetic and a couple of seconds of driving rewrites them); a black ground is not, so a
            // recreated mask is cleared to white like a fresh one.
            if (!EnsureMaskAlive())
                return;

            // Order matters: scroll and fade read and rewrite the whole mask, so they run BEFORE the
            // stamps. The other way round the frame's fade would eat the ruts pressed this frame.
            TickUpdatePass();
            FlushStamps();
        }

        private bool EnsureMaskAlive()
        {
            if (_maskTexture == null)
                return false;

            if (_spareTexture != null && !_spareTexture.IsCreated())
                _spareTexture.Create();

            if (_maskTexture.IsCreated())
                return true;

            if (!_maskTexture.Create())
            {
                Debug.LogError("[GroundMaskDriver] The mask could not be recreated after a resume — no ruts for the rest of the run.");
                return false;
            }

            ClearMask();
            // The global still points at the same managed object, but re-binding is cheap and covers a
            // binding that was lost together with the native resource.
            Shader.SetGlobalTexture(MaskTextureId, _maskTexture);
            return true;
        }

        /// <summary>
        /// Combined SCROLL + FADE. Both are "read the mask, write it back transformed", so they share
        /// one blit: separating them would double the most expensive pass of the system for nothing.
        ///
        /// Runs when the window has to scroll (it cannot wait, the window already moved) or when the
        /// fade tick is due. The fade owed accumulates between passes, so the fade rate does not depend
        /// on how often this ran.
        /// </summary>
        private void TickUpdatePass()
        {
            if (_updateMaterial == null)
                return;

            bool scrolled = ScrollWindow();
            AccumulateFade();

            bool fadeDue = _fadeAccumulator > 0f && Time.time >= _nextFadeAt;
            if (!scrolled && !fadeDue)
                return;

            if (fadeDue)
                _nextFadeAt = Time.time + 1f / Mathf.Max(1f, FadeHz);

            _updateMaterial.SetVector(UvOffsetId, new Vector4(_scrollU, _scrollV, 0f, 0f));
            _updateMaterial.SetFloat(FadeId, _fadeAccumulator);
            _fadeAccumulator = 0f;

            // Ping-pong: the pass writes the spare mask and the two swap, so a pass is ONE full-mask blit.
            // The first version blitted into a temporary and copied it back, which is two full-mask
            // writes per pass for the same result.
            RenderTexture target = _spareTexture;
            if (target == null || !target.IsCreated())
                return;

            Graphics.Blit(_maskTexture, target, _updateMaterial);
            _spareTexture = _maskTexture;
            _maskTexture = target;
            Shader.SetGlobalTexture(MaskTextureId, _maskTexture);
            UpdatePassCount++;
        }

        private float _scrollU;
        private float _scrollV;

        /// <summary>Snaps the window onto the grid and stores the UV shift for the update pass.</summary>
        private bool ScrollWindow()
        {
            _scrollU = 0f;
            _scrollV = 0f;
            if (_follow == null)
                return false;

            Vector3 position = _follow.position;
            float deltaU;
            float deltaV;
            if (!_window.Scroll(position.x, position.z, out deltaU, out deltaV))
                return false;

            _scrollU = deltaU;
            _scrollV = deltaV;
            PushGlobals();
            return true;
        }

        private void AccumulateFade()
        {
            float now = Time.time;
            float deltaTime = now - _lastFadeTime;
            _lastFadeTime = now;

            if (FadePerSecond <= 0f || deltaTime <= 0f)
                return;

            _fadeAccumulator += FadePerSecond * deltaTime;
        }

        // ---------------------------------------------------------------- resources

        private void EnsureResources()
        {
            if (_command == null)
                _command = new CommandBuffer { name = "GroundMask" };

            if (_quad == null)
                _quad = CreateXzQuad();

            if (_stampProperties == null)
                _stampProperties = new MaterialPropertyBlock();

            if (_maskTexture == null)
                CreateMask();

            if (_updateMaterial == null)
            {
                Shader updateShader = Shader.Find(UpdateShaderName);
                if (updateShader != null)
                {
                    _updateMaterial = new Material(updateShader) { hideFlags = HideFlags.HideAndDontSave };
                }
                else if (!_updateShaderWarned)
                {
                    // Without it the mask does not scroll and the ruts stay pinned to the camera. Still
                    // not fatal: stamping works, so the run keeps going with a static window.
                    _updateShaderWarned = true;
                    Debug.LogError(
                        "[GroundMaskDriver] Shader '" + UpdateShaderName + "' is missing — the mask will " +
                        "not scroll or fade. Stamp a fresh window with 'Festival Derby/World/Create Snow Rut Materials'.");
                }
            }

            if (_stampMaterial.shader == null || _stampMaterial.shader.name != StampShaderName)
            {
                // Advisory only: the material is serialized, so a wrong one is an authoring mistake.
                Debug.LogWarning(
                    "[GroundMaskDriver] The stamp material does not use '" + StampShaderName + "' — " +
                    "the mask may be drawn with the wrong blend.");
            }
        }

        private void CreateMask()
        {
            int resolution = _window.Resolution;

            // R8 has no depth buffer and one byte per texel: 1 MB at 1024 squared. The ARGB32 fallback
            // is 4 MB, so it is logged instead of swallowed silently.
            RenderTextureFormat format = RenderTextureFormat.R8;
            if (!SystemInfo.SupportsRenderTextureFormat(format))
            {
                format = RenderTextureFormat.ARGB32;
                Debug.LogWarning("[GroundMaskDriver] R8 is not supported on this device — the mask falls back to ARGB32 (4x the video memory).");
            }

            _maskTexture = NewMask(resolution, format, "GroundMask");
            _spareTexture = NewMask(resolution, format, "GroundMaskSpare");
        }

        private static RenderTexture NewMask(int resolution, RenderTextureFormat format, string name)
        {
            var texture = new RenderTexture(resolution, resolution, 0, format)
            {
                name = name,
                // Bilinear on the ROAD shader's side of the contract: the copy that must not filter is
                // the update pass, and it binds its own point sampler.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            return texture;
        }

        /// <summary>Publishes the window to the shaders. Every material that reads the mask shares these.</summary>
        private void PushGlobals()
        {
            if (_maskTexture != null)
                Shader.SetGlobalTexture(MaskTextureId, _maskTexture);

            Shader.SetGlobalVector(MaskOriginId, new Vector4(_window.OriginX, 0f, _window.OriginZ, 0f));
            Shader.SetGlobalFloat(MaskSizeId, _window.AreaSize);
            // The shader needs one texel in UV to sample the gradient.
            Shader.SetGlobalFloat(MaskTexelUvId, _window.Resolution > 0 ? 1f / _window.Resolution : 0f);
        }

        /// <summary>
        /// View of a virtual orthographic camera looking straight down on the window. Rebuilt per
        /// scroll because the origin moves; the projection is fixed.
        /// </summary>
        private Matrix4x4 BuildView()
        {
            var eye = new Vector3(_window.OriginX + _window.AreaSize * 0.5f, 100f, _window.OriginZ + _window.AreaSize * 0.5f);
            Matrix4x4 view = Matrix4x4.TRS(eye, Quaternion.Euler(90f, 0f, 0f), Vector3.one).inverse;

            // Unity looks down -Z in view space, so the view above flips Z: compensate or the mask
            // comes out mirrored against the world.
            return Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view;
        }

        private Matrix4x4 BuildProjection()
        {
            // Unity convention, WITHOUT GL.GetGPUProjectionMatrix: SetViewProjectionMatrices converts to
            // the platform convention itself. Converting here would apply it twice and flip Y on
            // D3D/Vulkan.
            float half = _window.AreaSize * 0.5f;
            return Matrix4x4.Ortho(-half, half, -half, half, 0.1f, 200f);
        }

        /// <summary>Horizontal quad (XZ plane), centred, with no normals or tangents.</summary>
        private static Mesh CreateXzQuad()
        {
            var mesh = new Mesh
            {
                name = "GroundMaskStampQuad",
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                },
            };
            mesh.UploadMeshData(true); // no readable copy: it lives in GPU memory only
            return mesh;
        }

        /// <summary>
        /// Destroys a resource this driver created. Destroy outside play mode logs an error (the
        /// EditMode tests configure and destroy drivers), so edit mode takes the immediate path.
        /// </summary>
        private static void Release(Object owned)
        {
            if (owned == null)
                return;

            if (Application.isPlaying)
                Destroy(owned);
            else
                DestroyImmediate(owned);
        }

        private void OnDestroy()
        {
            bool wasActive = Active == this;
            if (wasActive)
                Active = null;

            if (_command != null)
            {
                _command.Release();
                _command = null;
            }

            if (_maskTexture != null)
            {
                // The global points at a released texture if it is not cleared: the next scene without a
                // mask would inherit a dead one. Only when this was the active driver — a displaced
                // driver dies AFTER the new one's Configure, and clearing here would wipe the mask the
                // new driver just bound.
                if (wasActive)
                {
                    Shader.SetGlobalTexture(MaskTextureId, Texture2D.whiteTexture);
                    // Size 0 is the road shader's "no driver" value: it zeroes the whole rut path.
                    Shader.SetGlobalFloat(MaskSizeId, 0f);
                }

                _maskTexture.Release();
                Release(_maskTexture);
                _maskTexture = null;
            }

            if (_spareTexture != null)
            {
                _spareTexture.Release();
                Release(_spareTexture);
                _spareTexture = null;
            }

            if (_quad != null)
                Release(_quad);
            if (_updateMaterial != null)
                Release(_updateMaterial);
        }
    }
}
