namespace FestivalDerby.World
{
    /// <summary>
    /// What a stamped rut leaves behind: how wide it is and how deep. A value type, so the per-wheel
    /// decision allocates nothing.
    /// </summary>
    public readonly struct RutStampDecision
    {
        public readonly bool Stamping;

        /// <summary>Across the wheel, metres, from <see cref="TyreMarkRules.WidthFor"/>.</summary>
        public readonly float WidthMeters;

        /// <summary>0..1 dig depth. 1 is a wheel spinning hard, 0.5 a wheel that is only rolling.</summary>
        public readonly float Depth;

        public readonly SurfaceId Surface;

        public RutStampDecision(bool stamping, float widthMeters, float depth, SurfaceId surface)
        {
            Stamping = stamping;
            WidthMeters = widthMeters;
            Depth = depth;
            Surface = surface;
        }
    }

    /// <summary>
    /// When a wheel presses a rut into the ground, and how hard.
    ///
    /// This is deliberately NOT the same rule as <see cref="TyreMarkRules"/>, and the difference is
    /// physical: a rubber mark on asphalt needs the tyre to slide, because a rolling tyre on a hard
    /// surface leaves nothing behind. A loose surface is the opposite — a wheel that is merely rolling
    /// compacts the snow or pushes the sand aside, so a car driving in a straight line still cuts two
    /// grooves. Requiring slip there would mean ruts appear only during drifts, which is exactly the
    /// look the owner rejected.
    ///
    /// Slip is still used, as DEPTH: a wheel spinning or sliding digs deeper than one that is rolling.
    /// The route to that number is <see cref="TyreMarkRules.Evaluate"/>, so the hysteresis (a wheel
    /// hovering on the slip threshold holds one continuous mark instead of strobing) is the same code
    /// that already drives the tarmac marks.
    ///
    /// Engine-free and pure so the rule is testable without a scene.
    /// </summary>
    public static class RutStampRules
    {
        /// <summary>
        /// Depth of a rut under a wheel that is only rolling. The mask is white-is-intact, so this is
        /// the value the stamp blends the mask down to before the material's own shaping.
        /// </summary>
        public const float RollingDepth = 0.55f;

        /// <summary>Depth of a rut under a wheel that is spinning or sliding, as deep as the mask goes.</summary>
        public const float DiggingDepth = 0.25f;

        /// <summary>
        /// The surfaces that hold a visible rut. Sand is <see cref="SurfaceId.Dirt"/> here: the road
        /// bake has five layers and no sixth for sand, and dirt is the loose one it shares numbers with.
        /// </summary>
        public static bool IsRutSurface(SurfaceId surface)
        {
            return surface == SurfaceId.Snow || surface == SurfaceId.Dirt;
        }

        /// <summary>
        /// One wheel, one frame. <paramref name="mark"/> is what <see cref="TyreMarkRules.Evaluate"/>
        /// returned for the same contact, so its intensity and hysteresis carry over.
        ///
        /// The width comes from <see cref="TyreMarkRules.WidthFor"/> and nowhere else: a second width
        /// table would drift from the skid marks the moment either one was tuned.
        /// </summary>
        public static RutStampDecision Decide(in TyreMarkRules rules, in TyreContact contact, in TyreMarkDecision mark)
        {
            // A wheel in the air presses nothing. The strip is broken upstream as well; this keeps the
            // rule honest for a caller that does not track that.
            if (!contact.Grounded)
                return new RutStampDecision(false, 0f, 0f, contact.Surface);

            if (contact.GroundSpeed < rules.MinGroundSpeedMetersPerSecond)
                return new RutStampDecision(false, 0f, 0f, contact.Surface);

            if (!IsRutSurface(contact.Surface))
                return new RutStampDecision(false, 0f, 0f, contact.Surface);

            float intensity = mark.Marking ? mark.Intensity : 0f;
            if (intensity < 0f)
                intensity = 0f;
            else if (intensity > 1f)
                intensity = 1f;

            // Rolling digs the shallow rut; sliding digs it to the floor of the mask.
            float depth = RollingDepth + (DiggingDepth - RollingDepth) * intensity;

            return new RutStampDecision(true, rules.WidthFor(contact.Surface), depth, contact.Surface);
        }

        /// <summary>
        /// The surface a grip multiplier stands for, by nearest match in a table.
        ///
        /// The driving layer publishes the surface as ONE float — <c>CarHandlingReadout.SurfaceGripScale</c>
        /// — and not as an id, because the driving assembly deliberately does not know surface ids (the
        /// same reason the tyre smoke tints by ratio instead of by surface). Undoing that on the
        /// cosmetic side costs a five-entry scan per car per frame, which is cheaper than a second wire
        /// into the physics core and it cannot desync from the grip the tyres actually felt.
        /// </summary>
        public static SurfaceId SurfaceFromGrip(SurfaceTable table, float gripScale)
        {
            if (table == null)
                return SurfaceId.Street;

            // The five stock ids are the whole enum. TryGet rather than a cast keeps a table that
            // overrides one entry working: a missing id simply cannot win the nearest match.
            SurfaceId best = SurfaceId.Street;
            float bestDistance = float.MaxValue;
            for (int id = (int)SurfaceId.Street; id <= (int)SurfaceId.Gravel; id++)
            {
                SurfaceDefinition definition;
                if (!table.TryGet((SurfaceId)id, out definition))
                    continue;

                float distance = definition.GripMultiplier - gripScale;
                if (distance < 0f)
                    distance = -distance;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (SurfaceId)id;
                }
            }

            return best;
        }
    }
}
