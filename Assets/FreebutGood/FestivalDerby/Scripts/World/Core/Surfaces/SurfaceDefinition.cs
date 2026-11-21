using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Gameplay-facing data for one surface: the grip multiplier the car reads and the dust/sound
    /// ids the runtime plays. Authored once per surface in the editor and baked, never looked up by
    /// name at runtime.
    /// </summary>
    [Serializable]
    public struct SurfaceDefinition
    {
        public SurfaceId Id;
        public float GripMultiplier;
        public byte DustKindId;
        public byte SoundId;

        public SurfaceDefinition(SurfaceId id, float gripMultiplier, byte dustKindId, byte soundId)
        {
            Id = id;
            GripMultiplier = gripMultiplier;
            DustKindId = dustKindId;
            SoundId = soundId;
        }
    }
}
