using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// The one authoritative consequence of a car hitting a breakable prop. It is emitted exactly once
    /// per prop per match, by the server, and the client turns it into debris VFX (a prediction may
    /// play the effect early, but the state change only happens here). The impulse direction is planar
    /// because props live on the ground plane and the broken prop keeps no collider afterwards, so it
    /// never alters a competitive route.
    /// </summary>
    public readonly struct PropBrokenEvent : IEquatable<PropBrokenEvent>
    {
        public readonly int PropId;
        public readonly int Tick;

        /// <summary>Index of the car that delivered the breaking blow, in the server's roster.</summary>
        public readonly int CarIndex;

        /// <summary>Unit direction from the car towards the prop; the impulse the debris should take.</summary>
        public readonly float ImpulseX;
        public readonly float ImpulseZ;

        public PropBrokenEvent(int propId, int tick, int carIndex, float impulseX, float impulseZ)
        {
            PropId = propId;
            Tick = tick;
            CarIndex = carIndex;
            ImpulseX = impulseX;
            ImpulseZ = impulseZ;
        }

        public bool Equals(PropBrokenEvent other)
        {
            return PropId == other.PropId
                && Tick == other.Tick
                && CarIndex == other.CarIndex
                && ImpulseX == other.ImpulseX
                && ImpulseZ == other.ImpulseZ;
        }

        public override bool Equals(object obj)
        {
            return obj is PropBrokenEvent other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PropId;
                hash = (hash * 397) ^ Tick;
                hash = (hash * 397) ^ CarIndex;
                hash = (hash * 397) ^ ImpulseX.GetHashCode();
                hash = (hash * 397) ^ ImpulseZ.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(PropBrokenEvent left, PropBrokenEvent right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PropBrokenEvent left, PropBrokenEvent right)
        {
            return !left.Equals(right);
        }
    }
}
