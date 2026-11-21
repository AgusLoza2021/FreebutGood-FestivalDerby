using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// What one wheel decided this tick: whether it is laying a mark, how dark and wide that mark is
    /// (and its colour for the surface it is on), and how hard it should throw smoke or dust. A value
    /// type, so the per-tick call allocates nothing.
    /// </summary>
    public readonly struct TyreMarkDecision
    {
        public readonly bool Marking;

        /// <summary>0..1, ends up as the vertex alpha of the mark quad.</summary>
        public readonly float Intensity;

        public readonly float WidthMeters;
        public readonly float TintR;
        public readonly float TintG;
        public readonly float TintB;

        /// <summary>Particle emission rate on Street, particles per second.</summary>
        public readonly float SmokeRate;

        /// <summary>Particle emission rate on Dirt, Gravel or Mud, particles per second.</summary>
        public readonly float DustRate;

        public readonly SurfaceId Surface;

        public TyreMarkDecision(
            bool marking,
            float intensity,
            float widthMeters,
            float tintR,
            float tintG,
            float tintB,
            float smokeRate,
            float dustRate,
            SurfaceId surface)
        {
            Marking = marking;
            Intensity = intensity;
            WidthMeters = widthMeters;
            TintR = tintR;
            TintG = tintG;
            TintB = tintB;
            SmokeRate = smokeRate;
            DustRate = dustRate;
            Surface = surface;
        }
    }

    /// <summary>
    /// The bit that has to survive from tick to tick per wheel: whether the wheel was already laying a
    /// mark. Without it the decision flickers on and off around the slip threshold and every mark breaks
    /// into centimetre dashes.
    /// </summary>
    public struct TyreMarkWheelState
    {
        public bool Marking;

        public void Reset()
        {
            Marking = false;
        }
    }

    /// <summary>
    /// Tunables for the skid-mark and tyre-fx decision, plus the one pure function that turns a contact
    /// into a <see cref="TyreMarkDecision"/>. A readonly struct with a <see cref="Default"/> so callers
    /// can copy-with a single field (for a test or a feel tweak) without touching the others.
    ///
    /// The hysteresis matters more than the absolute numbers: a tyre crossing grip at a corner holds a
    /// slip right around S_on, and a bare threshold would strobe the mark on and off there. Starting at
    /// S_on and only stopping below S_off gives the trail one continuous strip through that band.
    /// </summary>
    public readonly struct TyreMarkRules
    {
        /// <summary>Slip magnitude at which a gripping wheel starts laying a mark, m/s.</summary>
        public readonly float SlipOnMetersPerSecond;

        /// <summary>Slip magnitude below which a marking wheel stops, m/s. Must be below SlipOn.</summary>
        public readonly float SlipOffMetersPerSecond;

        /// <summary>Slip magnitude at which the mark reaches full intensity.</summary>
        public readonly float FullIntensitySlipMetersPerSecond;

        /// <summary>Floor for the intensity of a mark that is being held, so a held mark never goes clear.</summary>
        public readonly float MinMarkIntensity;

        /// <summary>Below this ground speed nothing is laid, however hard the wheel slips.</summary>
        public readonly float MinGroundSpeedMetersPerSecond;

        /// <summary>Half-width-scaled mark width on Street, m. Other surfaces scale it.</summary>
        public readonly float MarkWidthMeters;

        public readonly float SmokeRatePerSecond;
        public readonly float DustRatePerSecond;

        /// <summary>
        /// The shipped defaults. The argument list is spelled out rather than left to the optional
        /// parameters: in C# 9 a struct always has an implicit parameterless constructor, so
        /// <c>new TyreMarkRules()</c> would bind to that and zero every field instead.
        /// </summary>
        public static readonly TyreMarkRules Default =
            new TyreMarkRules(2.0f, 1.4f, 6.0f, 0.2f, 0.5f, 0.30f, 14f, 18f);

        public TyreMarkRules(
            float slipOnMetersPerSecond = 2.0f,
            float slipOffMetersPerSecond = 1.4f,
            float fullIntensitySlipMetersPerSecond = 6.0f,
            float minMarkIntensity = 0.2f,
            float minGroundSpeedMetersPerSecond = 0.5f,
            float markWidthMeters = 0.30f,
            float smokeRatePerSecond = 14f,
            float dustRatePerSecond = 18f)
        {
            // A defaulted SlipOff above SlipOn would make the hysteresis band empty; clamp rather than
            // silently never hold a mark.
            SlipOnMetersPerSecond = slipOnMetersPerSecond;
            SlipOffMetersPerSecond = slipOffMetersPerSecond < slipOnMetersPerSecond
                ? slipOffMetersPerSecond
                : slipOnMetersPerSecond;
            FullIntensitySlipMetersPerSecond = fullIntensitySlipMetersPerSecond;
            MinMarkIntensity = minMarkIntensity;
            MinGroundSpeedMetersPerSecond = minGroundSpeedMetersPerSecond;
            MarkWidthMeters = markWidthMeters;
            SmokeRatePerSecond = smokeRatePerSecond;
            DustRatePerSecond = dustRatePerSecond;
        }

        /// <summary>
        /// Planar slip magnitude from the two components. No Math.Pow: a squared sum and one sqrt is
        /// both cheaper and exact enough for a threshold.
        /// </summary>
        public static float SlipMagnitude(in TyreContact contact)
        {
            float lateral = contact.LateralSlipSpeed;
            float longitudinal = contact.LongitudinalSlipSpeed;
            return (float)System.Math.Sqrt(lateral * lateral + longitudinal * longitudinal);
        }

        /// <summary>
        /// Wider, softer surfaces carve a wider rut than hot asphalt, so the mark width is the Street
        /// width times a per-surface scale instead of a second tunable per surface.
        /// </summary>
        public float WidthFor(SurfaceId surface)
        {
            switch (surface)
            {
                case SurfaceId.Dirt:
                    return MarkWidthMeters * 1.30f;
                case SurfaceId.Gravel:
                    return MarkWidthMeters * 1.15f;
                case SurfaceId.Mud:
                    return MarkWidthMeters * 1.45f;
                default:
                    return MarkWidthMeters;
            }
        }

        /// <summary>
        /// Mark colour per surface: dark rubber on asphalt, an earth rut on dirt, a pale scuff on gravel,
        /// a wet-dark line in mud and a faint compacted tread on snow.
        /// </summary>
        public void TintFor(SurfaceId surface, out float r, out float g, out float b)
        {
            switch (surface)
            {
                case SurfaceId.Dirt:
                    r = 0.34f;
                    g = 0.24f;
                    b = 0.15f;
                    return;
                case SurfaceId.Gravel:
                    r = 0.46f;
                    g = 0.43f;
                    b = 0.39f;
                    return;
                case SurfaceId.Mud:
                    r = 0.22f;
                    g = 0.17f;
                    b = 0.12f;
                    return;
                case SurfaceId.Snow:
                    r = 0.74f;
                    g = 0.78f;
                    b = 0.84f;
                    return;
                default:
                    r = 0.12f;
                    g = 0.12f;
                    b = 0.13f;
                    return;
            }
        }

        /// <summary>
        /// One wheel, one tick. Stateful only through <paramref name="state"/>, so the caller keeps one
        /// state per wheel and the function itself is pure otherwise.
        /// </summary>
        public static TyreMarkDecision Evaluate(
            in TyreMarkRules rules,
            in TyreContact contact,
            ref TyreMarkWheelState state)
        {
            bool canMark = contact.Grounded
                && contact.GroundSpeed >= rules.MinGroundSpeedMetersPerSecond;
            float slip = SlipMagnitude(contact);

            if (state.Marking)
            {
                if (!canMark || slip < rules.SlipOffMetersPerSecond)
                    state.Marking = false;
            }
            else if (canMark && slip >= rules.SlipOnMetersPerSecond)
            {
                state.Marking = true;
            }

            if (!state.Marking)
                return new TyreMarkDecision(false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, contact.Surface);

            float span = rules.FullIntensitySlipMetersPerSecond - rules.SlipOnMetersPerSecond;
            float t = span > 1e-4f ? (slip - rules.SlipOnMetersPerSecond) / span : 1f;
            if (t < 0f)
                t = 0f;
            else if (t > 1f)
                t = 1f;

            float intensity = rules.MinMarkIntensity + (1f - rules.MinMarkIntensity) * t;

            float r;
            float g;
            float b;
            rules.TintFor(contact.Surface, out r, out g, out b);

            // Rubble surfaces throw dust; asphalt throws a little smoke; snow does neither.
            float smoke = 0f;
            float dust = 0f;
            if (contact.Surface == SurfaceId.Street)
                smoke = rules.SmokeRatePerSecond * intensity;
            else if (contact.Surface == SurfaceId.Dirt
                || contact.Surface == SurfaceId.Gravel
                || contact.Surface == SurfaceId.Mud)
                dust = rules.DustRatePerSecond * intensity;

            return new TyreMarkDecision(
                true, intensity, rules.WidthFor(contact.Surface), r, g, b, smoke, dust, contact.Surface);
        }
    }
}
