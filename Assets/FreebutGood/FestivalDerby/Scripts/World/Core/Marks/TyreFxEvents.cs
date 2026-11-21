namespace FestivalDerby.World
{
    /// <summary>Which particle a burst belongs to. Smoke on asphalt, dust on loose surfaces.</summary>
    public enum TyreFxKind : byte
    {
        Smoke = 0,
        Dust = 1,
    }

    /// <summary>
    /// One particle burst request for this tick: where it comes from, how hard, and on which surface.
    /// The Unity particle side converts the rate into emissions; the core never touches a ParticleSystem.
    /// </summary>
    public readonly struct TyreFxBurst
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Rate;
        public readonly SurfaceId Surface;
        public readonly TyreFxKind Kind;

        public TyreFxBurst(float x, float z, float rate, SurfaceId surface, TyreFxKind kind)
        {
            X = x;
            Z = z;
            Rate = rate;
            Surface = surface;
            Kind = kind;
        }
    }

    /// <summary>
    /// Per-tick list of smoke and dust bursts, preallocated at construction. A full list drops the extra
    /// burst rather than growing: the rates are advisory and a frame that saturates the cap is already
    /// throwing more particles than the mobile budget allows.
    /// </summary>
    public sealed class TyreFxEvents
    {
        public const int DefaultCapacity = 64;

        private readonly TyreFxBurst[] _bursts;

        public TyreFxEvents(int capacity = DefaultCapacity)
        {
            if (capacity < 1)
                capacity = 1;
            _bursts = new TyreFxBurst[capacity];
        }

        public int Capacity
        {
            get { return _bursts.Length; }
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        /// <summary>Appends one burst. Returns false when the list is full.</summary>
        public bool Add(float x, float z, float rate, SurfaceId surface, TyreFxKind kind)
        {
            if (rate <= 0f || Count >= _bursts.Length)
                return false;

            _bursts[Count] = new TyreFxBurst(x, z, rate, surface, kind);
            Count++;
            return true;
        }

        /// <summary>
        /// Turns one wheel's decision into bursts. Smoke and dust are mutually exclusive per surface in
        /// the rules, so at most one burst is added per call today; the shape leaves room for both.
        /// </summary>
        public int Submit(in TyreMarkDecision decision, float x, float z)
        {
            int before = Count;
            if (decision.SmokeRate > 0f)
                Add(x, z, decision.SmokeRate, decision.Surface, TyreFxKind.Smoke);
            if (decision.DustRate > 0f)
                Add(x, z, decision.DustRate, decision.Surface, TyreFxKind.Dust);
            return Count - before;
        }

        public TyreFxBurst At(int index)
        {
            return _bursts[index];
        }
    }
}
