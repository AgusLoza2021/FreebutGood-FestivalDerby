using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// PF.2 ghost window for the player's respawned car: for the window the car cannot be hit and
    /// cannot hit (every bound collider off — ram damage rides OnCollision callbacks, so this also
    /// silences it, which is exactly what ghosting means) and its cosmetic model renders
    /// semi-transparent. When the window ends the colliders come back and the rendering is restored
    /// to the exact materials and property blocks it had, so the car is pixel-identical to before.
    ///
    /// The transparency cannot ride a MaterialPropertyBlock alone: the visible car's materials are
    /// shared opaque URP/Lit instances, and alpha does not blend on opaque. So each renderer's
    /// shared materials are swapped for per-material transparent clones (created once, on the first
    /// ghost-on, and reused for every later window) and the alpha itself rides a property block,
    /// which is how the garage's paint tint survives: the block carries the renderer's effective
    /// base colour — its own paint block when it has one, the material colour otherwise — faded to
    /// the ghost alpha. On ghost-off the original sharedMaterial references are written back and
    /// the pre-ghost property-block state is restored, not merely cleared, because clearing would
    /// erase the garage paint.
    ///
    /// The cache is built lazily on the first ghost-on rather than at Bind: the garage paint (a
    /// property block) is applied after the monitor binds, and only a first ghost-on is guaranteed
    /// to run after it. Idempotent and exactly reversible: SetGhost is a no-op when the state
    /// already matches, so a double ghost-on can never double-swap or lose the originals. Nothing
    /// here runs on the hot tick — a ghost window starts and ends once per respawn.
    /// </summary>
    public sealed class CarGhost
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        // URP transparent-surface constants: SrcAlpha/OneMinusSrcAlpha blending, no depth write,
        // and the Transparent queue where alpha blending sorts correctly.
        private const float SurfaceTypeTransparent = 1f;
        private const float BlendSrcAlpha = 5f;
        private const float BlendOneMinusSrcAlpha = 10f;
        private const float ZWriteOff = 0f;
        private const int TransparentRenderQueue = 3000;

        /// <summary>One renderer's ghost state: what it was, what it became, and how to give it back.</summary>
        private struct RendererGhost
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Clones;
            // The effective base colour faded to the ghost alpha, written through the block.
            public Color GhostColor;
            // The block the renderer carried before the ghost; null means none was set.
            public Color? PriorBlockColor;
        }

        private Transform _visualRoot;
        private Collider[] _colliders;
        private RendererGhost[] _renderers;
        private bool _active;
        private MaterialPropertyBlock _block;

        // The body the ghosted colliders belong to, and whether this ghost pinned its mass properties.
        // Disabling the chassis box leaves the body with no shape, and an automatic inertia tensor is then
        // recomputed from nothing: PhysX falls back to a unit tensor, so an 1100 kg car turns with the
        // rotational inertia of a 1 kg brick. The suspension's corner forces (tens of kN at ~0.7 m) spin it
        // over inside a few steps, its chassis-down rays leave the road, and with no collider it drops
        // through the world ("al resetear se pierde collider y me caigo del mapa", catch playtest 1).
        private Rigidbody _body;
        private bool _pinnedInertia;
        private bool _pinnedCenterOfMass;

        /// <summary>
        /// Binds the car this ghost operates on. Null references degrade honestly: no visual root
        /// means a collider-only ghost (the gray box is the only thing visible anyway), no colliders
        /// means a visual-only one. Called once per run, before any ghost window.
        /// </summary>
        public void Bind(Transform visualRoot, Collider[] colliders)
        {
            _visualRoot = visualRoot;
            _colliders = colliders;
            _body = null;
            for (int i = 0; colliders != null && i < colliders.Length && _body == null; i++)
            {
                if (colliders[i] != null)
                    _body = colliders[i].attachedRigidbody;
            }
        }

        /// <summary>
        /// Turns the ghost on (colliders off, transparent clones at <paramref name="alpha"/>) or off
        /// (everything back exactly as it was). Idempotent in both directions.
        /// </summary>
        public void SetGhost(bool active, float alpha)
        {
            if (active == _active)
                return;

            if (active && (_visualRoot != null || _colliders != null) && !EnsureCached())
            {
                // Nothing to ghost and nothing to disable: the car would be unreachable, so keep
                // the state machine consistent by refusing the transition.
                return;
            }

            _active = active;
            SetCollidersEnabled(!active);

            if (_renderers == null)
                return;

            if (active)
                ApplyGhost(alpha);
            else
                Restore();
        }

        /// <summary>
        /// Ends any active ghost and releases the cloned materials. Called at teardown, because the
        /// clones are runtime objects that no GameObject destruction reclaims.
        /// </summary>
        public void Teardown()
        {
            if (_active)
                SetGhost(false, 0f);

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Material[] clones = _renderers[i].Clones;
                    if (clones == null)
                        continue;
                    for (int m = 0; m < clones.Length; m++)
                    {
                        if (clones[m] != null)
                            Object.Destroy(clones[m]);
                    }
                }
            }

            _renderers = null;
            _visualRoot = null;
            _colliders = null;
            _body = null;
        }

        /// <summary>True when the ghost window is on.</summary>
        public bool IsActive => _active;

        /// <summary>
        /// Collects the renderers under the visual root and their pre-ghost state. Built once, on
        /// the first ghost-on, so the garage paint (applied after Bind) is already in place and is
        /// captured as part of the state the ghost must restore.
        /// </summary>
        private bool EnsureCached()
        {
            if (_renderers != null)
                return true;

            if (_visualRoot == null)
                return _colliders != null && _colliders.Length > 0;

            Renderer[] found = _visualRoot.GetComponentsInChildren<Renderer>();
            if (found.Length == 0)
                return _colliders != null && _colliders.Length > 0;

            _renderers = new RendererGhost[found.Length];
            for (int i = 0; i < found.Length; i++)
            {
                Renderer renderer = found[i];
                _renderers[i].Renderer = renderer;
                // Read the pre-ghost property block before anything overwrites it: the paint tint
                // lives there, and ghost-off has to give it back rather than clear it.
                if (_block == null)
                    _block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(_block);
                Color blockColor = _block.GetColor(BaseColorId);
                _renderers[i].PriorBlockColor = blockColor.a > 0f ? blockColor : (Color?)null;

                Material[] originals = renderer.sharedMaterials;
                _renderers[i].Originals = originals;

                Color materialColor = Color.white;
                if (originals.Length > 0 && originals[0] != null)
                {
                    if (originals[0].HasProperty(BaseColorId))
                        materialColor = originals[0].GetColor(BaseColorId);
                    else if (originals[0].HasProperty(ColorId))
                        materialColor = originals[0].GetColor(ColorId);
                }
                // A paint block overrides the material colour, so it is the effective base colour.
                if (_renderers[i].PriorBlockColor.HasValue)
                    materialColor = _renderers[i].PriorBlockColor.Value;

                _renderers[i].GhostColor = materialColor;
            }

            return true;
        }

        private void SetCollidersEnabled(bool enabled)
        {
            if (_colliders == null)
                return;

            // Pin before the shapes go and release only after they are back, so the body never steps
            // with mass properties derived from a shape set it does not really have.
            if (!enabled)
                PinMassProperties();

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = enabled;
            }

            if (enabled)
                ReleaseMassProperties();
        }

        /// <summary>
        /// Writes the live inertia tensor (and centre of mass, if automatic) back as explicit values, which
        /// turns the automatic recompute off for the window. Only what was automatic is pinned, so an
        /// explicitly authored value is left exactly as its author set it.
        /// </summary>
        private void PinMassProperties()
        {
            if (_body == null)
                return;

            if (_body.automaticInertiaTensor)
            {
                Vector3 tensor = _body.inertiaTensor;
                Quaternion tensorRotation = _body.inertiaTensorRotation;
                _body.inertiaTensor = tensor;
                _body.inertiaTensorRotation = tensorRotation;
                _pinnedInertia = true;
            }

            if (_body.automaticCenterOfMass)
            {
                _body.centerOfMass = _body.centerOfMass;
                _pinnedCenterOfMass = true;
            }
        }

        /// <summary>Hands the mass properties back to the colliders, which recompute the same values.</summary>
        private void ReleaseMassProperties()
        {
            if (_body == null)
                return;

            if (_pinnedInertia)
            {
                _body.ResetInertiaTensor();
                _pinnedInertia = false;
            }

            if (_pinnedCenterOfMass)
            {
                _body.ResetCenterOfMass();
                _pinnedCenterOfMass = false;
            }
        }

        private void ApplyGhost(float alpha)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i].Renderer;
                if (renderer == null)
                    continue;

                Material[] originals = _renderers[i].Originals;
                if (_renderers[i].Clones == null)
                    _renderers[i].Clones = CreateClones(originals);

                renderer.sharedMaterials = _renderers[i].Clones;

                Color faded = _renderers[i].GhostColor;
                faded.a = alpha;
                if (_block == null)
                    _block = new MaterialPropertyBlock();
                // Write both colour ids like the paint applier does: whichever the shader reads wins.
                _block.SetColor(BaseColorId, faded);
                _block.SetColor(ColorId, faded);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void Restore()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i].Renderer;
                if (renderer == null)
                    continue;

                // The exact references captured before the ghost, not look-alikes.
                renderer.sharedMaterials = _renderers[i].Originals;

                Color? prior = _renderers[i].PriorBlockColor;
                if (prior.HasValue)
                {
                    if (_block == null)
                        _block = new MaterialPropertyBlock();
                    _block.SetColor(BaseColorId, prior.Value);
                    _block.SetColor(ColorId, prior.Value);
                    renderer.SetPropertyBlock(_block);
                }
                else
                {
                    // No block before the ghost, so none after it either.
                    renderer.SetPropertyBlock(null);
                }
            }
        }

        /// <summary>One transparent clone per original material, configured once and reused.</summary>
        private static Material[] CreateClones(Material[] originals)
        {
            var clones = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material original = originals[i];
                if (original == null)
                    continue;

                var clone = new Material(original);
                clone.SetFloat(SurfaceId, SurfaceTypeTransparent);
                clone.SetFloat(SrcBlendId, BlendSrcAlpha);
                clone.SetFloat(DstBlendId, BlendOneMinusSrcAlpha);
                clone.SetFloat(ZWriteId, ZWriteOff);
                clone.renderQueue = TransparentRenderQueue;
                clone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                clone.DisableKeyword("_SURFACE_TYPE_OPAQUE");
                clones[i] = clone;
            }

            return clones;
        }
    }
}
