using System;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// The art the trackside dresser places for each planned kind. Kept as data so a prop can be swapped,
    /// re-scaled or dropped without touching the dresser: a kind with no entry (or no variants) is simply
    /// not drawn, which is how colliding kinds stay out of a visual-only pass.
    /// </summary>
    [CreateAssetMenu(fileName = "TracksideKit", menuName = "Config/TracksideKit")]
    public sealed class TracksideKit : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public TracksideKind Kind;

            [Tooltip("Prefabs whose root sits at the footprint's base centre, facing +Z. One is picked per placement.")]
            public GameObject[] Variants = Array.Empty<GameObject>();

            [Tooltip("Uniform scale applied to the placed root; the kit models are authored at different sizes.")]
            public float Scale = 1f;

            [Tooltip("Extra yaw added to the planner's heading, for models that face a different axis.")]
            public float YawOffsetDegrees;
        }

        [Tooltip("Lit material cloned for the kerb, edge-line and gravel strips. Referenced from an asset so " +
            "its shader is never stripped from a player build.")]
        public Material StripMaterialTemplate;

        public Color KerbRed = new Color(0.86f, 0.16f, 0.14f, 1f);
        public Color KerbWhite = new Color(0.95f, 0.95f, 0.93f, 1f);
        public Color EdgeLineColor = new Color(0.96f, 0.96f, 0.94f, 1f);
        public Color GravelColor = new Color(0.86f, 0.77f, 0.58f, 1f);

        [Tooltip("Planner rules for this kit: strip widths, prop density and how far from the asphalt props stand.")]
        public TracksideRuleSet Rules = TracksideRuleSet.CircuitDefault;

        public Entry[] Entries = Array.Empty<Entry>();

        /// <summary>The entry for a kind, or null when the kit does not draw it.</summary>
        public Entry Find(TracksideKind kind)
        {
            if (Entries == null)
                return null;

            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.Kind == kind && entry.Variants != null && entry.Variants.Length > 0)
                    return entry;
            }

            return null;
        }
    }
}
