using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Tunable balance of one breakable kind. Every number is a proposal, not a derived constant:
    /// <see cref="BreakSpeed"/> is the component of the car's velocity along the contact normal, in
    /// metres per second, at or above which the prop breaks. A car slower than this is simply pushed
    /// back by the static collider and nothing is emitted.
    /// </summary>
    public readonly struct BreakablePropSpec
    {
        public readonly BreakablePropKind Kind;

        /// <summary>Relative normal speed in m/s that breaks the prop. Provisional balance.</summary>
        public readonly float BreakSpeed;

        /// <summary>1 is a heavy prop, below 1 is lighter than a car. Drives debris feel, not the rule.</summary>
        public readonly float MassClass;

        /// <summary>Seconds the client keeps debris alive before it fades. Presentation only.</summary>
        public readonly float DebrisLifetime;

        /// <summary>
        /// Fraction of speed a breaking car loses, 0..1. Zero means the optional penalty is off, which
        /// is the default: the GDD makes the penalty optional, so the rule ships without it.
        /// </summary>
        public readonly float SpeedPenalty;

        /// <summary>Ticks <see cref="SpeedPenalty"/> lasts. Zero means off, whatever the fraction is.</summary>
        public readonly int SpeedPenaltyTicks;

        public BreakablePropSpec(
            BreakablePropKind kind, float breakSpeed, float massClass, float debrisLifetime, float speedPenalty, int speedPenaltyTicks)
        {
            if (!IsFinitePositive(breakSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(breakSpeed));
            }

            if (!IsFinitePositive(massClass))
            {
                throw new ArgumentOutOfRangeException(nameof(massClass));
            }

            if (!IsFinitePositive(debrisLifetime))
            {
                throw new ArgumentOutOfRangeException(nameof(debrisLifetime));
            }

            if (!IsFinite(speedPenalty) || speedPenalty < 0f || speedPenalty > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(speedPenalty));
            }

            if (speedPenaltyTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speedPenaltyTicks));
            }

            Kind = kind;
            BreakSpeed = breakSpeed;
            MassClass = massClass;
            DebrisLifetime = debrisLifetime;
            SpeedPenalty = speedPenalty;
            SpeedPenaltyTicks = speedPenaltyTicks;
        }

        /// <summary>True only when the penalty is actually configured: a fraction with no duration does nothing.</summary>
        public bool HasSpeedPenalty
        {
            get { return SpeedPenalty > 0f && SpeedPenaltyTicks > 0; }
        }

        /// <summary>Speed multiplier a penalised car carries while the penalty lasts; 1 when off.</summary>
        public float SpeedMultiplier
        {
            get { return HasSpeedPenalty ? 1f - SpeedPenalty : 1f; }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }
    }

    /// <summary>
    /// The single source of the breakable thresholds. The values are provisional and meant to be
    /// retuned after playtests (a cone should give way to a nudge, a sign should need a real hit);
    /// the shape of the table is what is stable, not the numbers.
    /// </summary>
    public static class BreakablePropSpecTable
    {
        /// <summary>Cone: light, breaks at a nudge. Provisional balance.</summary>
        public static readonly BreakablePropSpec Cone = new BreakablePropSpec(BreakablePropKind.Cone, 3.0f, 0.4f, 6f, 0f, 0);

        /// <summary>Box: needs a solid hit. Provisional balance.</summary>
        public static readonly BreakablePropSpec Box = new BreakablePropSpec(BreakablePropKind.Box, 5.0f, 1.0f, 8f, 0f, 0);

        /// <summary>Sign: the heaviest of the three, so the highest threshold. Provisional balance.</summary>
        public static readonly BreakablePropSpec Sign = new BreakablePropSpec(BreakablePropKind.Sign, 7.0f, 1.6f, 10f, 0f, 0);

        /// <summary>Spec of a kind. An unknown byte value falls back to the lightest prop, never to a throw.</summary>
        public static BreakablePropSpec Get(BreakablePropKind kind)
        {
            switch (kind)
            {
                case BreakablePropKind.Cone:
                    return Cone;
                case BreakablePropKind.Box:
                    return Box;
                case BreakablePropKind.Sign:
                    return Sign;
                default:
                    return Cone;
            }
        }
    }
}
