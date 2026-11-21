using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// One mark quad: the two wheel positions that bound it, its width, the tint and the age used for
    /// the fade. A plain struct in a preallocated array, so emitting never allocates.
    /// </summary>
    public struct TyreMarkSegment
    {
        public float X0;
        public float Z0;
        public float X1;
        public float Z1;
        public float Width;
        public float Intensity;
        public float R;
        public float G;
        public float B;

        /// <summary>
        /// Groups consecutive segments into one ribbon. The mesh joins segments of a strip by sharing
        /// their edge vertices, so the id is what keeps two different slides from welding together.
        /// </summary>
        public int StripId;

        /// <summary>Seconds since emission, advanced by <see cref="TyreMarkTrail.Advance"/>.</summary>
        public float Age;
    }

    /// <summary>
    /// Fixed-capacity ring of mark segments shared by every wheel of every car. The capacity is chosen
    /// once at construction; after that the trail overwrites its oldest segment instead of growing, so
    /// the whole per-tick path is allocation-free and the memory ceiling is known up front.
    ///
    /// A wheel does not emit on every tick. It records its last point and emits one segment once it has
    /// moved at least <see cref="MinSegmentLength"/>, which keeps run length predictable and stops a
    /// slow wheel from flooding the ring. A gap longer than <see cref="BreakLengthMeters"/>, a release
    /// of the slide, or a wheel leaving the ground all start a new strip.
    /// </summary>
    public sealed class TyreMarkTrail
    {
        /// <summary>Upper bound for the wheel slots; one Unity car has four, so this leaves room.</summary>
        public const int MaxWheels = 8;

        public const int DefaultCapacity = 2048;

        private struct WheelTrack
        {
            public bool HasLast;
            public bool WasMarking;
            public float LastX;
            public float LastZ;
            public int StripId;
        }

        private readonly TyreMarkSegment[] _segments;
        private readonly WheelTrack[] _wheels;

        /// <summary>Ring index of the oldest live segment.</summary>
        private int _head;

        private int _count;
        private int _nextStripId;
        private int _dirtyVertexStart;

        public TyreMarkTrail(int capacitySegments = DefaultCapacity, int wheelCount = 4)
        {
            if (capacitySegments < 1)
                capacitySegments = 1;
            if (wheelCount < 1)
                wheelCount = 1;
            if (wheelCount > MaxWheels)
                wheelCount = MaxWheels;

            Capacity = capacitySegments;
            WheelCount = wheelCount;
            _segments = new TyreMarkSegment[capacitySegments];
            _wheels = new WheelTrack[wheelCount];
            MinSegmentLength = 0.25f;
            BreakLengthMeters = 2.0f;
            LifetimeSeconds = 12f;
            _dirtyVertexStart = 0;
        }

        public int Capacity { get; }

        public int WheelCount { get; }

        public int Count
        {
            get { return _count; }
        }

        /// <summary>Minimum distance a marking wheel must travel before a new segment is emitted, m.</summary>
        public float MinSegmentLength { get; set; }

        /// <summary>
        /// A jump longer than this starts a new strip instead of drawing a quad across the map. It also
        /// catches a respawn or a replay seek.
        /// </summary>
        public float BreakLengthMeters { get; set; }

        /// <summary>Age at which a segment has faded to nothing, seconds.</summary>
        public float LifetimeSeconds { get; set; }

        /// <summary>
        /// Lowest vertex index the mark mesh changed since <see cref="ClearDirty"/>, so the Unity side
        /// can upload a sub-range. Conservative: it may be lower than the truly first changed vertex,
        /// never higher.
        /// </summary>
        public int DirtyVertexStart
        {
            get { return _dirtyVertexStart; }
        }

        /// <summary>
        /// Feeds one wheel for this tick, in emission order. Returns true when a segment was written.
        /// </summary>
        public bool Submit(int wheel, in TyreMarkDecision decision, float x, float z)
        {
            if (wheel < 0 || wheel >= WheelCount)
                return false;

            ref WheelTrack track = ref _wheels[wheel];

            if (!decision.Marking)
            {
                // Releasing the slide ends the strip; the next mark starts fresh so no quad spans it.
                track.HasLast = false;
                track.WasMarking = false;
                return false;
            }

            if (!track.HasLast || !track.WasMarking)
            {
                // First point of a strip: store it, emit nothing yet.
                track.HasLast = true;
                track.WasMarking = true;
                track.LastX = x;
                track.LastZ = z;
                track.StripId = _nextStripId++;
                return false;
            }

            float dx = x - track.LastX;
            float dz = z - track.LastZ;
            float length = (float)System.Math.Sqrt(dx * dx + dz * dz);

            if (length > BreakLengthMeters)
            {
                track.LastX = x;
                track.LastZ = z;
                track.StripId = _nextStripId++;
                return false;
            }

            if (length < MinSegmentLength)
                return false;

            Emit(track.StripId, track.LastX, track.LastZ, x, z, in decision);
            track.LastX = x;
            track.LastZ = z;
            return true;
        }

        /// <summary>Ages every live segment, which is what drives the fade in the mesh fill.</summary>
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;

            for (int i = 0; i < _count; i++)
            {
                int slot = (_head + i) % Capacity;
                _segments[slot].Age += deltaSeconds;
            }
        }

        /// <summary>Drops every segment and strip, for a run reset. Keeps all buffers.</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            _nextStripId = 0;
            _dirtyVertexStart = 0;
            for (int i = 0; i < _wheels.Length; i++)
            {
                _wheels[i].HasLast = false;
                _wheels[i].WasMarking = false;
                _wheels[i].LastX = 0f;
                _wheels[i].LastZ = 0f;
                _wheels[i].StripId = 0;
            }
        }

        /// <summary>Marks the current dirty range consumed, so a mesh upload may skip it next fill.</summary>
        public void ClearDirty()
        {
            _dirtyVertexStart = int.MaxValue;
        }

        /// <summary>Live segment by position, 0 being the oldest. No bounds check on the ring interior.</summary>
        public TyreMarkSegment SegmentAt(int index)
        {
            if (index < 0 || index >= _count)
                return default;

            return _segments[(_head + index) % Capacity];
        }

        /// <summary>Alpha multiplier for a segment's age, 1 while fresh down to 0 at the lifetime.</summary>
        public float FadeFor(float age)
        {
            if (LifetimeSeconds <= 1e-4f)
                return 1f;

            float fade = 1f - age / LifetimeSeconds;
            if (fade < 0f)
                return 0f;
            if (fade > 1f)
                return 1f;
            return fade;
        }

        private void Emit(int stripId, float x0, float z0, float x1, float z1, in TyreMarkDecision decision)
        {
            int beforeCount = _count;
            int slot;
            bool appended = _count < Capacity;
            if (appended)
            {
                slot = (_head + _count) % Capacity;
                _count++;
            }
            else
            {
                // Full: overwrite the oldest and slide the head, so every vertex index shifts.
                slot = _head;
                _head = (_head + 1) % Capacity;
            }

            ref TyreMarkSegment segment = ref _segments[slot];
            segment.X0 = x0;
            segment.Z0 = z0;
            segment.X1 = x1;
            segment.Z1 = z1;
            segment.Width = decision.WidthMeters;
            segment.Intensity = decision.Intensity;
            segment.R = decision.TintR;
            segment.G = decision.TintG;
            segment.B = decision.TintB;
            segment.StripId = stripId;
            segment.Age = 0f;

            // Conservative floor: the new geometry lands at or after 2 * beforeCount vertices (vertex
            // sharing only lowers the real index), and an overwrite moves everything.
            int floor = appended ? beforeCount * 2 : 0;
            if (floor < _dirtyVertexStart)
                _dirtyVertexStart = floor;
        }
    }
}
