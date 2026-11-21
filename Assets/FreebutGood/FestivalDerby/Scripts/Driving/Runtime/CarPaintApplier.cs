using System.Collections.Generic;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Paints a car's body panels through a property block and nothing else, so glass, lights and tyres
    /// keep their own colours. The body targets are resolved once per car and cached, so a later apply is
    /// one loop with no allocations. Resolution runs in three tiers and the first non-empty one wins:
    ///   1. material slots whose name ends in "_Body";
    ///   2. renderers or materials whose name contains "Body" or "Door";
    ///   3. the car's authored <see cref="CarVisualEntry.PaintTargets"/>.
    /// A car that matches nothing keeps its factory paint: tinting a windscreen is worse than not
    /// painting. In the Editor that case logs once per car id.
    /// </summary>
    public sealed class CarPaintApplier
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Renderer[] NoRenderers = new Renderer[0];
        private static readonly int[][] NoIndices = new int[0][];

        private readonly Renderer[] _renderers;
        private readonly int[][] _indices;
        private MaterialPropertyBlock _block;

        public CarPaintApplier(GameObject model, CarVisualEntry entry)
        {
            if (model == null)
            {
                _renderers = NoRenderers;
                _indices = NoIndices;
                return;
            }

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var targets = new List<Renderer>();
            var slots = new List<int[]>();

            // First non-empty tier wins, so an authored "*_Body" slot always beats a loose name and
            // the car is never painted by two rules at once.
            bool found = Collect(renderers, PaintRule.BodySlot, null, targets, slots);
            if (!found)
                found = Collect(renderers, PaintRule.NameContains, null, targets, slots);
            if (!found)
                found = Collect(renderers, PaintRule.Authored, TargetsOf(entry), targets, slots);
            if (!found)
                WarnNoTargets(entry, model);

            _renderers = targets.Count == 0 ? NoRenderers : targets.ToArray();
            _indices = slots.Count == 0 ? NoIndices : slots.ToArray();
        }

        /// <summary>False when the car has no body target; the caller then keeps factory paint.</summary>
        public bool HasTargets
        {
            get { return _renderers.Length > 0; }
        }

        /// <summary>
        /// Tints the cached body targets. The block is reused and each slot is written on its own
        /// material index, so a multi-material renderer only loses the body colour; the existing
        /// block on that index is preserved first so a later pass is not clobbered.
        /// </summary>
        public void Apply(Color color)
        {
            if (_renderers.Length == 0)
                return;

            if (_block == null)
                _block = new MaterialPropertyBlock();

            for (int r = 0; r < _renderers.Length; r++)
            {
                var renderer = _renderers[r];
                if (renderer == null)
                    continue;

                var indices = _indices[r];
                for (int i = 0; i < indices.Length; i++)
                {
                    int index = indices[i];
                    renderer.GetPropertyBlock(_block, index);
                    _block.SetColor(BaseColorId, color);
                    _block.SetColor(ColorId, color);
                    renderer.SetPropertyBlock(_block, index);
                }
            }
        }

        /// <summary>Restores factory paint by clearing the block on every touched slot.</summary>
        public void Clear()
        {
            for (int r = 0; r < _renderers.Length; r++)
            {
                var renderer = _renderers[r];
                if (renderer == null)
                    continue;

                var indices = _indices[r];
                for (int i = 0; i < indices.Length; i++)
                    renderer.SetPropertyBlock(null, indices[i]);
            }
        }

        private static string[] TargetsOf(CarVisualEntry entry)
        {
            return entry != null ? entry.PaintTargets : null;
        }

        private static bool Collect(Renderer[] renderers, PaintRule rule, string[] overrides,
            List<Renderer> outRenderers, List<int[]> outSlots)
        {
            bool found = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                    continue;

                var indices = ResolveSlots(renderer, rule, overrides);
                if (indices == null)
                    continue;

                outRenderers.Add(renderer);
                outSlots.Add(indices);
                found = true;
            }

            return found;
        }

        private static int[] ResolveSlots(Renderer renderer, PaintRule rule, string[] overrides)
        {
            var materials = renderer.sharedMaterials;
            int length = materials != null ? materials.Length : 0;
            if (length == 0)
                return null;

            // A renderer-level name match means the whole mesh is the body part, so every slot of
            // it is painted. The "*_Body" tier never matches by renderer name.
            if (rule != PaintRule.BodySlot && RendererMatches(renderer.name, rule, overrides))
            {
                var all = new int[length];
                for (int i = 0; i < length; i++)
                    all[i] = i;
                return all;
            }

            // Otherwise only the material slots whose own name matches are painted.
            int[] buffer = new int[length];
            int used = 0;
            for (int i = 0; i < length; i++)
            {
                var material = materials[i];
                if (material != null && MaterialMatches(material.name, rule, overrides))
                    buffer[used++] = i;
            }

            if (used == 0)
                return null;
            if (used == length)
                return buffer;

            var result = new int[used];
            System.Array.Copy(buffer, result, used);
            return result;
        }

        private static bool RendererMatches(string name, PaintRule rule, string[] overrides)
        {
            return rule == PaintRule.NameContains
                ? ContainsBodyOrDoor(name)
                : ContainsAny(name, overrides);
        }

        private static bool MaterialMatches(string name, PaintRule rule, string[] overrides)
        {
            switch (rule)
            {
                case PaintRule.BodySlot:
                    return EndsWithBody(name);
                case PaintRule.NameContains:
                    return ContainsBodyOrDoor(name);
                default:
                    return ContainsAny(name, overrides);
            }
        }

        /// <summary>The Blender contract: "<car>_Body", matched case-insensitively.</summary>
        private static bool EndsWithBody(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                name.EndsWith("_Body", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsBodyOrDoor(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                (name.IndexOf("Body", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsAny(string name, string[] overrides)
        {
            if (string.IsNullOrEmpty(name) || overrides == null)
                return false;

            for (int i = 0; i < overrides.Length; i++)
            {
                var candidate = overrides[i];
                if (!string.IsNullOrEmpty(candidate) &&
                    name.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private enum PaintRule
        {
            BodySlot,
            NameContains,
            Authored
        }

#if UNITY_EDITOR
        // Editor-only and per id: an unhandled model should be visible while it is being authored,
        // but switching cars back and forth must not repeat the same line.
        private static readonly HashSet<string> WarnedIds = new HashSet<string>();

        private static void WarnNoTargets(CarVisualEntry entry, GameObject model)
        {
            string id = entry != null && !string.IsNullOrEmpty(entry.Id) ? entry.Id : model.name;
            if (!WarnedIds.Add(id))
                return;

            Debug.LogWarning(
                "CarPaintApplier: no body target on '" + id + "'; it keeps factory paint. " +
                "Name a material slot '<car>_Body' or list the renderer/material in PaintTargets.",
                model);
        }
#else
        private static void WarnNoTargets(CarVisualEntry entry, GameObject model)
        {
        }
#endif
    }
}
