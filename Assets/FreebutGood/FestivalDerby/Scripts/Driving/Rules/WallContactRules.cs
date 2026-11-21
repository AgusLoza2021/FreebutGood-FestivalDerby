using System;

namespace FestivalDerby.Driving
{
    /// <summary>
    /// What a barrier contact did to the car's velocity. Every field is the rule's own output, so a
    /// caller (or a test) can read the decision without recomputing it.
    /// </summary>
    public readonly struct WallGlanceResult
    {
        public readonly float VelocityX;
        public readonly float VelocityY;
        public readonly float VelocityZ;

        /// <summary>Speed the car had into the wall (positive), before the glance.</summary>
        public readonly float NormalSpeedMetersPerSecond;

        /// <summary>
        /// Share of the incoming speed that still travels along the wall: 1 for a tangent kiss, 0 for a
        /// head-on stop. The bounce is reported separately, in the velocity fields.
        /// </summary>
        public readonly float RetainedFraction;

        /// <summary>True below <see cref="WallContactRules.GrazeNormalSpeedMetersPerSecond"/>.</summary>
        public readonly bool IsGraze;

        internal WallGlanceResult(float velocityX, float velocityY, float velocityZ, float normalSpeed,
            float retainedFraction, bool isGraze)
        {
            VelocityX = velocityX;
            VelocityY = velocityY;
            VelocityZ = velocityZ;
            NormalSpeedMetersPerSecond = normalSpeed;
            RetainedFraction = retainedFraction;
            IsGraze = isGraze;
        }
    }

    /// <summary>
    /// WALL1: a barrier contact is a glance, not a brake.
    ///
    /// The owner's playtest: "when you crash into the wall it slows you down a LOT. That should be much,
    /// much less." The measured cause is not the contact solver (friction already resolves to 0.1 through
    /// the chassis material's Minimum combine) but what happens after it: the contact removes the
    /// normal component, the car keeps pointing INTO the wall, and the tyre model then scrubs the
    /// wall-tangential speed because that speed is lateral to the car's own heading. Against a barrier the
    /// car cannot deflect, so the grip that would have turned into a slide is spent as a brake, and the
    /// next contact starts from a car that is still pointing at the wall. That repeats every tick: the
    /// speed decays by roughly cos^2(incidence) per contact until the car is grinding along at a crawl.
    ///
    /// This rule replaces that outcome with the arcade one the game wants (Circuit Superstars): keep the
    /// tangential direction, keep a planned share of the speed, and hand back only a small outward bounce.
    /// It is engine-free and allocation-free because it runs inside a physics callback on the authoritative
    /// fixed step; the Unity side only reads and writes velocities.
    /// </summary>
    public static class WallContactRules
    {
        /// <summary>
        /// Contact normal Y above which the surface is the ground, not a barrier. The floor, the road
        /// chunks and every ramp report a mostly-vertical normal, and treating one as a wall would stop the
        /// car dead on every landing.
        /// </summary>
        public const float MaxWallNormalY = 0.5f;

        /// <summary>
        /// Below this into-wall speed the contact is already resolved: no rewrite. It keeps a resting or
        /// already-sliding car from being corrected every physics tick, which is what would jitter.
        /// </summary>
        public const float MinNormalSpeedMetersPerSecond = 0.4f;

        /// <summary>Below this total speed there is nothing worth redecorating.</summary>
        public const float MinSpeedMetersPerSecond = 0.2f;

        /// <summary>Share of the normal speed handed back as an outward bounce.</summary>
        public const float Restitution = 0.15f;

        /// <summary>
        /// Cap on that bounce. At 60 m/s into a wall the uncapped restitution would fire the car backwards
        /// faster than any hit-stop reads right, and a hard stop must stay a stop.
        /// </summary>
        public const float MaxBounceSpeedMetersPerSecond = 3f;

        /// <summary>
        /// Into-wall speed at and above which the contact is a real hit rather than a graze. A graze costs
        /// no damage and no hit-stop, and it is realistic enough to stay quiet. Alignment is no longer part
        /// of this split: a real hit needs it too (see <see cref="ShouldAlignToWall"/>).
        /// </summary>
        public const float GrazeNormalSpeedMetersPerSecond = 6f;

        /// <summary>Yaw rate the alignment asks for per radian of heading error, before the cap.</summary>
        public const float AlignGainPerSecond = 3f;

        /// <summary>Hard cap on the alignment's target yaw rate, so it can never spin the car.</summary>
        public const float MaxAlignYawRateRadiansPerSecond = 2.5f;

        /// <summary>
        /// How much one contact may change the yaw rate, so the nudge is never a snap. Catch playtest 1 raised
        /// it from 0.35 to 1.0: the glance now runs once per fresh hit instead of every step of contact, so a
        /// single nudge has to do the turning that used to be spread over the whole scrape.
        /// </summary>
        public const float AlignRateStepRadiansPerSecond = 1f;

        /// <summary>
        /// A barrier contact that begins less than this long after the last one is the same scrape, not a
        /// fresh hit. The solver can drop and re-make a grinding contact between steps, and each re-make
        /// would otherwise re-charge the retention cost.
        /// </summary>
        public const float FreshHitSeparationSeconds = 0.15f;

        /// <summary>
        /// Yaw error above which the alignment stands down. The tangent then disagrees with the nose by more
        /// than any slide into the wall could explain, which means the car is travelling backwards along the
        /// barrier; auto-turning that would spin it.
        /// </summary>
        public const float MaxAlignErrorRadians = 1.4f;

        /// <summary>Heading error below which an aligned car is left alone.</summary>
        public const float AlignDeadbandRadians = 0.02f;

        // The retention curve, keyed on the share of the speed that pointed into the wall, i.e. sin of the
        // incidence angle measured from the barrier plane. It is authored as data because these are the
        // owner's numbers (25 m/s): 15 deg keeps ~95%, 30 deg ~88%, 60 deg ~60%, and 80 deg or more is a
        // stop. Linear between the keys; a couple of hundredths either way is invisible in the car.
        //
        // Second playtest (owner: "the wall collisions still slow me down a lot"): the first curve was
        // tuned against the contact solver alone and underestimated how much a barrier costs AFTER it. A hit
        // also leaves the nose pointing into the wall, so the tyre model spends the wall-tangential speed as
        // grip on every following tick unless the car is aligned (see ShouldAlignToWall). Measured on the
        // 24-car oval, the mid-angle keys are what made a scrape read as a brake, so 30-60 deg now keep
        // noticeably more and only a near head-on contact is still a stop.
        //
        // Catch playtest 1 ("walls scrub too much"): the 30 and 60 degree keys rise to 0.95 and 0.75. A
        // scrape now keeps its speed and a solid hit costs a quarter, not two fifths.
        private const float Share0 = 0f;
        private const float Share15 = 0.2588f;
        private const float Share30 = 0.5f;
        private const float Share60 = 0.8660f;
        private const float Share90 = 1f;
        private const float Retain0 = 1f;
        private const float Retain15 = 0.95f;
        private const float Retain30 = 0.95f;
        private const float Retain60 = 0.75f;
        private const float Retain90 = 0f;

        /// <summary>
        /// Whether a contact is with a barrier. Two gates, both load-bearing: the other side must be
        /// static (a Rigidbody on it means another car or a prop, which keeps its own ram/damage path) and
        /// the surface must not be the ground.
        /// </summary>
        public static bool IsWallContact(bool otherHasRigidbody, float normalX, float normalY, float normalZ)
        {
            if (otherHasRigidbody)
            {
                return false;
            }

            if (normalX * normalX + normalY * normalY + normalZ * normalZ < 1e-8f)
            {
                return false;
            }

            return Math.Abs(normalY) <= MaxWallNormalY;
        }

        /// <summary>
        /// Share of the incoming speed that survives as motion along the wall, for an incoming
        /// <paramref name="normalShare"/> = normal speed / total speed.
        /// </summary>
        public static float RetainedFractionFor(float normalShare)
        {
            if (float.IsNaN(normalShare) || normalShare <= Share0)
            {
                return Retain0;
            }

            if (normalShare >= Share90)
            {
                return Retain90;
            }

            if (normalShare <= Share15)
            {
                return Lerp(Retain0, Retain15, (normalShare - Share0) / (Share15 - Share0));
            }

            if (normalShare <= Share30)
            {
                return Lerp(Retain15, Retain30, (normalShare - Share15) / (Share30 - Share15));
            }

            if (normalShare <= Share60)
            {
                return Lerp(Retain30, Retain60, (normalShare - Share30) / (Share60 - Share30));
            }

            return Lerp(Retain60, Retain90, (normalShare - Share60) / (Share90 - Share60));
        }

        /// <summary>
        /// Whether the contact should also turn the car toward the barrier's tangent.
        ///
        /// This used to be grazes only, on the reasoning that a real hit is the player's to recover from.
        /// That is what kept the brake in the wall: the glance hands back the wall-tangential speed, but a
        /// car still pointing into the barrier spends it again as tyre grip on the next ticks, and the player
        /// feels the wall "braking" them for as long as the nose disagrees with the tangent. A real hit needs
        /// the alignment at least as much as a graze does; only the safety guards stay — a finite state and a
        /// yaw error small enough to be a slide rather than a car travelling backwards along the wall.
        /// </summary>
        public static bool ShouldAlignToWall(float normalSpeedMetersPerSecond, float yawErrorRadians)
        {
            if (!IsFinite(normalSpeedMetersPerSecond) || !IsFinite(yawErrorRadians))
            {
                return false;
            }

            if (normalSpeedMetersPerSecond < MinNormalSpeedMetersPerSecond)
            {
                return false;
            }

            return Math.Abs(yawErrorRadians) <= MaxAlignErrorRadians;
        }

        /// <summary>
        /// Turns an incoming velocity that is driving into a barrier into the velocity the car should leave
        /// the contact with: the tangential direction, a planned share of the speed, and a small outward
        /// bounce. Returns false (and the caller must leave the body alone) for anything that is not a
        /// barrier hit worth rewriting — moving away, already resolved, the ground, degenerate input.
        ///
        /// <paramref name="normalX"/> and friends point OUT of the barrier, toward the car, which is what
        /// Unity's <c>ContactPoint.normal</c> gives for the contact's collider.
        /// </summary>
        public static bool TryResolve(float velocityX, float velocityY, float velocityZ,
            float normalX, float normalY, float normalZ, out WallGlanceResult result)
        {
            result = default;
            if (!IsFinite(velocityX) || !IsFinite(velocityY) || !IsFinite(velocityZ))
            {
                return false;
            }

            float normalLengthSquared = normalX * normalX + normalY * normalY + normalZ * normalZ;
            if (!IsFinite(normalLengthSquared) || normalLengthSquared < 1e-8f)
            {
                return false;
            }

            float inverseNormalLength = 1f / (float)Math.Sqrt(normalLengthSquared);
            float nx = normalX * inverseNormalLength;
            float ny = normalY * inverseNormalLength;
            float nz = normalZ * inverseNormalLength;

            float speedSquared = velocityX * velocityX + velocityY * velocityY + velocityZ * velocityZ;
            if (speedSquared < MinSpeedMetersPerSecond * MinSpeedMetersPerSecond)
            {
                return false;
            }

            float speed = (float)Math.Sqrt(speedSquared);
            float intoWall = -(velocityX * nx + velocityY * ny + velocityZ * nz);
            if (!(intoWall >= MinNormalSpeedMetersPerSecond))
            {
                // Moving away, or pressed against the wall with nothing left to convert.
                return false;
            }

            float normalShare = intoWall / speed;
            if (normalShare > 1f)
            {
                normalShare = 1f;
            }

            float retained = RetainedFractionFor(normalShare);
            float bounce = Restitution * intoWall;
            if (bounce > MaxBounceSpeedMetersPerSecond)
            {
                bounce = MaxBounceSpeedMetersPerSecond;
            }

            // Tangent direction from the incoming velocity itself. A dead-on hit has no tangent, and that is
            // the one case that must be a stop, so the tangent share is zero and only the bounce survives.
            float tx = velocityX + nx * intoWall;
            float ty = velocityY + ny * intoWall;
            float tz = velocityZ + nz * intoWall;
            float tangentLengthSquared = tx * tx + ty * ty + tz * tz;
            float alongWall = speed * retained;
            if (tangentLengthSquared > 1e-8f && alongWall > 0f)
            {
                float inverseTangentLength = 1f / (float)Math.Sqrt(tangentLengthSquared);
                tx *= inverseTangentLength * alongWall;
                ty *= inverseTangentLength * alongWall;
                tz *= inverseTangentLength * alongWall;
            }
            else
            {
                tx = 0f;
                ty = 0f;
                tz = 0f;
            }

            result = new WallGlanceResult(
                tx + nx * bounce,
                ty + ny * bounce,
                tz + nz * bounce,
                intoWall,
                retained,
                intoWall < GrazeNormalSpeedMetersPerSecond);
            return true;
        }

        /// <summary>
        /// The yaw rate to move toward, in radians per second, so the car ends up sliding along the barrier
        /// instead of pointing into it. <paramref name="maxDeltaRadiansPerSecond"/> is what one fixed tick
        /// may change the rate by, which is what keeps the nudge from arriving as a snap.
        ///
        /// Returns false when the car is already aligned; the caller then leaves the angular velocity alone.
        /// </summary>
        public static bool AlignYawRateRadiansPerSecond(float yawErrorRadians, float currentYawRate,
            float maxDeltaRadiansPerSecond, out float yawRate)
        {
            yawRate = currentYawRate;
            if (!IsFinite(yawErrorRadians) || !IsFinite(currentYawRate) || !IsFinite(maxDeltaRadiansPerSecond))
            {
                return false;
            }

            if (maxDeltaRadiansPerSecond <= 0f)
            {
                return false;
            }

            if (Math.Abs(yawErrorRadians) < AlignDeadbandRadians)
            {
                return false;
            }

            float target = AlignGainPerSecond * yawErrorRadians;
            if (target > MaxAlignYawRateRadiansPerSecond)
            {
                target = MaxAlignYawRateRadiansPerSecond;
            }
            else if (target < -MaxAlignYawRateRadiansPerSecond)
            {
                target = -MaxAlignYawRateRadiansPerSecond;
            }

            float delta = target - currentYawRate;
            if (delta > maxDeltaRadiansPerSecond)
            {
                delta = maxDeltaRadiansPerSecond;
            }
            else if (delta < -maxDeltaRadiansPerSecond)
            {
                delta = -maxDeltaRadiansPerSecond;
            }

            if (delta == 0f)
            {
                return false;
            }

            yawRate = currentYawRate + delta;
            return true;
        }

        /// <summary>
        /// Whether a barrier contact is a fresh hit that earns a glance. Catch playtest 1: the glance ran on
        /// every physics step of a contact, so a car grinding along a barrier paid the retention cost 50 times
        /// a second (20-40 % of its speed per second of contact). Only a contact that begins — and begins
        /// after a real separation from the last barrier contact — is a hit; staying in contact is not.
        /// <paramref name="secondsSinceLastWallContact"/> is infinite (or NaN) for a car that never touched one.
        /// </summary>
        public static bool IsFreshHit(bool contactBegan, float secondsSinceLastWallContact)
        {
            if (!contactBegan)
            {
                return false;
            }

            return float.IsNaN(secondsSinceLastWallContact)
                || secondsSinceLastWallContact >= FreshHitSeparationSeconds;
        }

        /// <summary>
        /// A light contact: no damage and no hit-stop. Purely informational now — the alignment is decided by
        /// <see cref="ShouldAlignToWall"/>, which applies to every barrier contact.
        /// </summary>
        public static bool IsGraze(float normalSpeedMetersPerSecond)
        {
            return normalSpeedMetersPerSecond >= 0f && normalSpeedMetersPerSecond < GrazeNormalSpeedMetersPerSecond;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
