using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One authored light prop beside the road, in the engine-free form the core reads: a planar
    /// position, a collision radius and a kind. It is the World core's view of a prop; the Unity
    /// assembly converts its authored <c>EdgeAsset</c>/scatter data into this on load, so the server
    /// never needs a ScriptableObject.
    ///
    /// <see cref="Breakable"/> is the opt-in flag the feature is built on and it defaults to false:
    /// a map authored before breakable props existed keeps every prop solid and unbreakable, so
    /// adding this field changed no existing map.
    /// </summary>
    [Serializable]
    public struct AuthoredEdgeProp
    {
        public BreakablePropKind Kind;
        public float X;
        public float Z;

        /// <summary>Collision radius in metres, planar; a prop's circle is what the server tests.</summary>
        public float Radius;

        /// <summary>Authored opt-in. False keeps a prop static and indestructible.</summary>
        public bool Breakable;

        public AuthoredEdgeProp(BreakablePropKind kind, float x, float z, float radius, bool breakable)
        {
            Kind = kind;
            X = x;
            Z = z;
            Radius = radius;
            Breakable = breakable;
        }
    }
}
