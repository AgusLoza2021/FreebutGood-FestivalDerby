namespace FestivalDerby.World
{
    /// <summary>
    /// One tyre contact patch for a single fixed tick, exactly as the Unity adapter can read it from a
    /// wheel collider or a suspension raycast. Engine-free on purpose: the same numbers can be produced
    /// by the offline harness, by a replay or by the server, so the mark rules are testable without
    /// UnityEngine and the same code path runs on every machine.
    ///
    /// The slip arrives split into its two axes rather than as one magnitude. A sideways slide and a
    /// locked wheel are different events physically, and while both currently trip the same "sliding"
    /// decision, keeping the components separate lets the rule weight lock versus drift later without a
    /// new input struct on the Unity side.
    /// </summary>
    public readonly struct TyreContact
    {
        /// <summary>Wheel contact point on the ground plane. The trail joins consecutive ticks of this.</summary>
        public readonly float X;
        public readonly float Z;

        /// <summary>Planar sideways slip speed of the contact patch, m/s. Only the magnitude matters here.</summary>
        public readonly float LateralSlipSpeed;

        /// <summary>
        /// Rolling-versus-ground mismatch along the wheel plane, m/s: a brake lock turns the tyre slower
        /// than the ground, wheelspin faster. Only the magnitude matters here.
        /// </summary>
        public readonly float LongitudinalSlipSpeed;

        /// <summary>
        /// Planar speed of the contact patch, m/s. A stopped wheel leaves no mark however hard it slips,
        /// so this gates emission together with <see cref="Grounded"/>.
        /// </summary>
        public readonly float GroundSpeed;

        /// <summary>False while the wheel hangs in the air, which always breaks the strip.</summary>
        public readonly bool Grounded;

        /// <summary>Normal load, N. Carried for future use; the rule does not need it yet.</summary>
        public readonly float LoadNewton;

        public readonly SurfaceId Surface;

        public TyreContact(
            float x,
            float z,
            float lateralSlipSpeed,
            float longitudinalSlipSpeed,
            float groundSpeed,
            bool grounded,
            SurfaceId surface,
            float loadNewton = 0f)
        {
            X = x;
            Z = z;
            LateralSlipSpeed = lateralSlipSpeed;
            LongitudinalSlipSpeed = longitudinalSlipSpeed;
            GroundSpeed = groundSpeed;
            Grounded = grounded;
            Surface = surface;
            LoadNewton = loadNewton;
        }
    }
}
