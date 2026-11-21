using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Fits one cosmetic model onto the physics box a car already has. It is deliberately static and
    /// one-shot: the visual becomes a child of a "VisualPivot" child of the body — an identity
    /// transform that exists so body-level presentation (the feel juice) can move the whole model
    /// without ever touching the fitted clone itself — so the visual follows every pose physics gives
    /// that body and is destroyed with it, and nothing here runs per tick. The body's colliders and
    /// rigidbody are untouched; only its flat box faces are hidden.
    /// </summary>
    public static class CarVisualAttacher
    {
        public const string VisualName = "Visual";
        public const string VisualPivotName = "VisualPivot";

        // Below this the model has no meaningful length and fitting it would divide by ~zero.
        private const float MinimumFitLength = 1e-4f;

        /// <summary>
        /// Instantiates <paramref name="entry"/>.Model under an identity "VisualPivot" child of
        /// <paramref name="body"/>, removes every component that could change the simulation, scales
        /// it so its length along the body's forward axis matches the box length, centres it on the box
        /// in x/z and drops its lowest point onto the box bottom. Returns the clone (never the pivot),
        /// or null when there is nothing to attach. A pivot left behind by an earlier attach on the
        /// same body is reused and cleared, so repeated fits (the garage carousel) cannot pile up
        /// stale clones under stale pivots.
        /// </summary>
        public static GameObject Attach(Transform body, BoxCollider box, CarVisualEntry entry)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (box == null)
                throw new ArgumentNullException(nameof(box));
            if (entry == null || entry.Model == null)
                return null;

            GameObject visual = null;
            try
            {
                visual = Object.Instantiate(entry.Model, ObtainPivot(body), false);
                visual.name = VisualName;
                // Measure from an unrotated, unscaled clone: the FBX may carry its own scale, and a
                // model scaled by that export correction would fit against the wrong length.
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(entry.EulerOffset);
                visual.transform.localScale = Vector3.one;

                StripNonCosmeticComponents(visual);
                ConfigureShadows(visual);
                Fit(visual.transform, body, box, entry.ScaleMultiplier);
                DisableBoxRenderers(body, visual.transform);
                return visual;
            }
            catch
            {
                // A half-fitted clone must never outlive the failure.
                if (visual != null)
                {
                    visual.SetActive(false);
                    DestroyObject(visual);
                }

                throw;
            }
        }

        /// <summary>
        /// The identity pivot every fitted clone hangs under, found or created on the body. Because
        /// the pivot is created lazily and cleared on reuse, a failed fit can at worst leave an empty
        /// pivot behind, never a half-fitted clone.
        /// </summary>
        private static Transform ObtainPivot(Transform body)
        {
            Transform existing = body.Find(VisualPivotName);
            if (existing != null)
            {
                // One pivot, one clone: whatever an earlier fit left under it must not survive.
                for (int i = existing.childCount - 1; i >= 0; i--)
                    DestroyObject(existing.GetChild(i).gameObject);
                return existing;
            }

            var pivot = new GameObject(VisualPivotName);
            pivot.transform.SetParent(body, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            return pivot.transform;
        }

        /// <summary>
        /// Removes what a mesh can bring with it: its own colliders and rigidbody would join the
        /// physics world, and an animator, camera or light would drive or light the scene. The
        /// components are disabled first because Destroy is deferred in play mode, so a collider
        /// must not be able to collide for even one frame.
        /// </summary>
        private static void StripNonCosmeticComponents(GameObject visual)
        {
            var colliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                DestroyObject(colliders[i]);
            }

            var rigidbodies = visual.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].detectCollisions = false;
                DestroyObject(rigidbodies[i]);
            }

            DestroyBehaviours<Animator>(visual);
            DestroyBehaviours<Camera>(visual);
            DestroyBehaviours<Light>(visual);
        }

        private static void DestroyBehaviours<T>(GameObject visual) where T : Behaviour
        {
            var behaviours = visual.GetComponentsInChildren<T>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = false;
                DestroyObject(behaviours[i]);
            }
        }

        /// <summary>Cars are read by their silhouette, so cast shadows but never receive them.</summary>
        private static void ConfigureShadows(GameObject visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = false;
            }
        }

        private static void Fit(Transform visual, Transform body, BoxCollider box, float scaleMultiplier)
        {
            Bounds boxBounds = BoxBoundsInBodySpace(body, box);
            if (!TryModelBoundsInBodySpace(body, visual, out Bounds modelBounds) ||
                !(modelBounds.size.z > MinimumFitLength))
            {
                // A model with no usable renderers stays attached at the body origin, unscaled.
                return;
            }

            // Sanitize here too: a catalog built in code never passed through OnValidate.
            float multiplier = float.IsNaN(scaleMultiplier) || float.IsInfinity(scaleMultiplier) ||
                scaleMultiplier <= 0f ? 1f : scaleMultiplier;
            float scale = boxBounds.size.z * multiplier / modelBounds.size.z;
            visual.localScale = Vector3.one * scale;

            // Scaling is around the visual root, which sits on the body origin, so the measured
            // bounds scale with it; the box bottom is the alignment line.
            Vector3 center = modelBounds.center * scale;
            Vector3 extents = modelBounds.extents * scale;
            visual.localPosition = new Vector3(
                boxBounds.center.x - center.x,
                boxBounds.min.y - (center.y - extents.y),
                boxBounds.center.z - center.z);
        }

        /// <summary>The collider's real extents in body space: the factory sizes it by transform scale.</summary>
        private static Bounds BoxBoundsInBodySpace(Transform body, BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            Vector3 min = PositiveInfinity;
            Vector3 max = NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = box.center + new Vector3(
                    (corner & 1) == 0 ? -half.x : half.x,
                    (corner & 2) == 0 ? -half.y : half.y,
                    (corner & 4) == 0 ? -half.z : half.z);
                Encapsulate(ref min, ref max, body.InverseTransformPoint(box.transform.TransformPoint(local)));
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static bool TryModelBoundsInBodySpace(Transform body, Transform visual, out Bounds bounds)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            Vector3 min = PositiveInfinity;
            Vector3 max = NegativeInfinity;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                // World-space corners, brought back into body space: a rotated mesh is then fitted
                // by the bounds it actually occupies on the car, not by its own axes.
                Bounds world = renderer.bounds;
                Vector3 center = world.center;
                Vector3 extents = world.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    Encapsulate(ref min, ref max, body.InverseTransformPoint(center + offset));
                }
            }

            if (min.x == float.PositiveInfinity)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        /// <summary>
        /// Hides the primitive faces the factory built but keeps every collider: the box is the
        /// physics, the model is the look. The visual's own subtree is skipped so a mesh can never
        /// hide itself.
        /// </summary>
        private static void DisableBoxRenderers(Transform body, Transform visual)
        {
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.transform == visual || renderer.transform.IsChildOf(visual))
                    continue;

                renderer.enabled = false;
            }
        }

        private static Vector3 PositiveInfinity =>
            new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        private static Vector3 NegativeInfinity =>
            new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        private static void Encapsulate(ref Vector3 min, ref Vector3 max, Vector3 point)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        private static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
