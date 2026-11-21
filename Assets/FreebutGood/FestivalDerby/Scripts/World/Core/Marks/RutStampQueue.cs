namespace FestivalDerby.World
{
    /// <summary>
    /// One rut footprint: a quad on the ground plane, sized in metres, rotated in the wheel's
    /// direction of travel. Same shape <see cref="TyreMarkTrail"/> uses for a mark, minus the colour:
    /// the mask is a single red channel, so tint and relief belong to the road shader that reads it.
    /// </summary>
    public struct RutStamp
    {
        public float X;
        public float Z;

        /// <summary>Across the wheel, metres.</summary>
        public float Width;

        /// <summary>
        /// Along the direction of travel, metres. The stamp is centred on the midpoint of the distance
        /// the wheel covered since the previous frame and is made one width longer than that distance,
        /// so consecutive stamps overlap and the strip has no pinch points.
        /// </summary>
        public float Length;

        /// <summary>Rotation about world Y, radians, from the direction of travel.</summary>
        public float YawRadians;

        /// <summary>
        /// How far down this stamp presses the mask, 0..1 of the mask range (1 = untouched). Comes from
        /// <see cref="RutStampRules.Decide"/>: a wheel that is spinning digs deeper than one that is
        /// only rolling. Per stamp and not per material, so the frame is still one instanced draw.
        /// </summary>
        public float Depth;

        public RutStamp(float x, float z, float width, float length, float yawRadians, float depth)
        {
            X = x;
            Z = z;
            Width = width;
            Length = length;
            YawRadians = yawRadians;
            Depth = depth;
        }
    }

    /// <summary>
    /// Fixed-capacity stamp queue drained once per frame by one instanced draw. Fixed and not a
    /// <c>List</c> because this is submitted from a per-frame path: the array is allocated once, the
    /// enqueue writes a struct into it, and a full queue drops the excess instead of growing.
    ///
    /// Dropping is correct here. A stamp is cosmetic, and the alternative — growing — would turn a
    /// pathological frame into an allocation on the very path that is supposed to be allocation-free.
    /// The overflow is counted rather than logged per stamp so a stuck frame cannot spam the log; the
    /// driver reports the count once.
    /// </summary>
    public sealed class RutStampQueue
    {
        /// <summary>
        /// 512 covers a full offline field with room to spare: 24 cars times 4 wheels is 96 stamps in
        /// the worst frame, and the same cap the source system used.
        /// </summary>
        public const int Capacity = 512;

        private readonly RutStamp[] _stamps;
        private int _count;
        private int _dropped;
        private int _droppedSinceReport;

        public RutStampQueue()
        {
            _stamps = new RutStamp[Capacity];
        }

        public int Count
        {
            get { return _count; }
        }

        /// <summary>Stamps refused since the queue was last cleared, for diagnostics and tests.</summary>
        public int Dropped
        {
            get { return _dropped; }
        }

        /// <summary>Stamps refused since the last <see cref="TakeDroppedSinceReport"/>, for one log line.</summary>
        public int TakeDroppedSinceReport()
        {
            int value = _droppedSinceReport;
            _droppedSinceReport = 0;
            return value;
        }

        public bool TryEnqueue(in RutStamp stamp)
        {
            if (_count >= Capacity)
            {
                _dropped++;
                _droppedSinceReport++;
                return false;
            }

            _stamps[_count++] = stamp;
            return true;
        }

        public RutStamp this[int index]
        {
            get { return _stamps[index]; }
        }

        /// <summary>Empties the queue, keeping the allocation and the drop tallies.</summary>
        public void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// Drops the queue and both tallies, for a fresh run. The mask content is cleared separately by
        /// the driver, so a caller that only wants to stop stamping calls <see cref="Clear"/>.
        /// </summary>
        public void Reset()
        {
            _count = 0;
            _dropped = 0;
            _droppedSinceReport = 0;
        }
    }
}
