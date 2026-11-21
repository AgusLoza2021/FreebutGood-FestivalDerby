using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// The boxed-geometry primitives that both owners — the car and the circuit — build from.
    /// They are shared rather than copied because the rules that make this geometry safe are the
    /// same for both: one new material, unit-scale parenting, decorative colliders disabled
    /// before the deferred Destroy, and no leak when any step throws.
    /// </summary>
    internal static class GrayboxGeometry
    {
        internal const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";
        internal const string LitShader = "Universal Render Pipeline/Lit";

        /// <summary>The URP shader both owners accept: Simple Lit, with Lit as the fallback.</summary>
        internal static Shader FindShader()
        {
            return Shader.Find(SimpleLitShader) ?? Shader.Find(LitShader);
        }

        /// <summary>Resolves the shader or fails explicitly, so no owner ever renders pink.</summary>
        internal static Shader RequireShader()
        {
            var shader = FindShader();
            if (shader == null)
                throw new InvalidOperationException("Owner boxed geometry requires a URP Simple Lit or Lit shader.");
            return shader;
        }

        /// <summary>
        /// Rejects a parent whose world scale is not positive unit, or whose ancestry contains a
        /// reflected or zero axis: two reflected axes can disappear from lossyScale's signs.
        /// Translation and rotation are supported; the caller must keep the parent stationary.
        /// </summary>
        internal static void ValidateUnitParent(Transform parent)
        {
            if (parent == null)
                return;

            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                var local = ancestor.localScale;
                if (local.x <= 0f || local.y <= 0f || local.z <= 0f)
                    throw new ArgumentException("Geometry parent cannot have reflected or zero-scale ancestry.", nameof(parent));
            }

            var scale = parent.lossyScale;
            if (!IsUnit(scale.x) || !IsUnit(scale.y) || !IsUnit(scale.z))
                throw new ArgumentException("Geometry parent must have positive unit world scale.", nameof(parent));
        }

        internal static bool IsUnit(float value)
        {
            return value > 0f && Mathf.Approximately(value, 1f);
        }

        internal static float FiniteMinimum(float value, float minimum)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Max(value, minimum);
        }

        internal static Vector3 FiniteMinimum(Vector3 value, float minimum)
        {
            return new Vector3(FiniteMinimum(value.x, minimum), FiniteMinimum(value.y, minimum),
                FiniteMinimum(value.z, minimum));
        }

        /// <summary>
        /// Creates one owned cube. Decorative boxes get their collider disabled *before* the
        /// deferred Destroy so they never take part in physics for even a frame; a box that throws
        /// is hidden and destroyed before the exception continues.
        /// </summary>
        internal static GameObject CreateBox(string name, Transform parent, Vector3 size, Vector3 position,
            Material material, bool collidable)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                box.transform.SetParent(parent, false);
                box.name = name;
                box.transform.localPosition = position;
                box.transform.localScale = size;
                var collider = box.GetComponent<BoxCollider>();
                var renderer = box.GetComponent<Renderer>();
                if (collider == null || renderer == null)
                    throw new InvalidOperationException("Box primitive is missing its collider or renderer.");
                if (!collidable)
                {
                    // Destroy is deferred; disable first so decorative boxes never participate in physics.
                    collider.enabled = false;
                    Object.Destroy(collider);
                }
                renderer.sharedMaterial = material;
                return box;
            }
            catch
            {
                box.SetActive(false);
                Object.Destroy(box);
                throw;
            }
        }
    }
}
