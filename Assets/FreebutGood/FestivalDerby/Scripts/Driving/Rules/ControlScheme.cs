namespace FestivalDerby.Driving
{
    /// <summary>
    /// How the player's throttle is produced. The owner's 2026-10-15 decision ("utilizamos el modo
    /// que ya la industria usa, copiemos de la competencia") turned a hard-wired rule into a
    /// selectable, A/B-able one, because the shipped rule (stick distance = pedal) has no precedent:
    /// every screen-relative mobile racer keeps the stick for direction and puts the throttle on its
    /// own control (references section 3).
    /// </summary>
    public enum ControlScheme
    {
        /// <summary>
        /// The V2 ring rule: stick magnitude past the dead zone is the pedal. Kept because it is
        /// already shipped and because the references table still wants it measured, not because any
        /// competitor ships it. The default for any caller that never chooses.
        /// </summary>
        RingThrottle = 0,

        /// <summary>
        /// Circuit Superstars "screen space steering": the stick sets the target heading and only has
        /// to leave the dead zone to be heard; a separate GAS input is the pedal. Releasing the stick
        /// while holding gas drives straight on the heading the car already has.
        /// </summary>
        GasButton = 1,

        /// <summary>
        /// Death Rally: asking for a direction is asking to go there, so the throttle is on while the
        /// stick is out of the dead zone and the car coasts when it is released. Brake and reverse
        /// still override. The only scheme that auto-unsticks itself.
        /// </summary>
        AutoAccelerate = 2
    }

    /// <summary>
    /// The shipped default and the validity gate. GasButton is the default because it is the scheme
    /// the references table actually describes (a heading stick plus a gas input) and the one the
    /// owner asked to copy; RingThrottle and AutoAccelerate stay selectable for the A/B.
    /// </summary>
    public static class ControlSchemeRules
    {
        /// <summary>The scheme a match or a player uses when nothing chose one.</summary>
        public const ControlScheme Default = ControlScheme.GasButton;

        public static bool IsDefined(ControlScheme scheme)
        {
            return scheme == ControlScheme.RingThrottle
                || scheme == ControlScheme.GasButton
                || scheme == ControlScheme.AutoAccelerate;
        }

        /// <summary>
        /// A wire value that is not a known scheme degrades to the default instead of throwing: a
        /// peer that does not know a future scheme must still be able to race, not fail to join.
        /// </summary>
        public static ControlScheme Sanitize(ControlScheme scheme)
        {
            return IsDefined(scheme) ? scheme : Default;
        }
    }
}
