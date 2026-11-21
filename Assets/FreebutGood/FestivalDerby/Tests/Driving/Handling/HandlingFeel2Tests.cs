using System;
using FestivalDerby.Driving.Handling;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// FEEL2 pace contract. The 2026-10-15 playtest asked for the fast car back: "mientras mas rapido
    /// pase mas dopamina mejor" (the faster it goes, the more dopamine, the better). Three numbers carry
    /// that: 20 m/s in about a second, 90% of top speed in under three, and enough drive after a crash
    /// that the driver is back on the throttle instead of waiting for the car.
    ///
    /// Every "before" row here is the car FEEL2 replaces, built from the same preset with
    /// <see cref="HandlingParams.WithLongitudinalPace"/> and the catch-up feature off, so the comparison
    /// runs identical handling code and only the pace parameters differ. The numbers the tests print are
    /// the ones quoted in Documentation/Design/DRIVING_MODEL_V2.md.
    ///
    /// The pace change also moves recorded bounds in the older suites (the launch windows, the free top
    /// speed, the cruise decel and the FP0 spin recovery). Those are updated where they live and called
    /// out in the doc; this file is the contract that stops the pace drifting back.
    /// </summary>
    [TestFixture]
    public sealed class HandlingFeel2Tests
    {
        /// <summary>FEEL2 top speed, which the throttle curve tapers to.</summary>
        private const float TopSpeedMetersPerSecond = 31f;

        private const float NinetyPercentOfTopSpeed = TopSpeedMetersPerSecond * 0.9f;

        /// <summary>The recovery speed the crash boost is judged on: past the U-turn crawl, short of top.</summary>
        private const float RecoverySpeed = 15f;

        /// <summary>
        /// The pace FEEL2 replaces: accelMax 9, engine brake 5 and the 2026-10-13 launch curve, with the
        /// catch-up off (it did not exist). This is the car the owner called slow.
        /// </summary>
        private static HandlingParams ShippedPace()
        {
            HandlingParams p = HandlingParams.Balanced.WithLongitudinalPace(
                TopSpeedMetersPerSecond,
                9f,
                new Piecewise4(0f, 0.55f, 0.25f, 1f, 0.65f, 0.85f, 1f, 0f),
                5f);
            return p.WithCatchUp(CatchUpTuning.Off);
        }

        private static PlanarCarSim Launch(HandlingParams parameters, float startSpeed)
        {
            var sim = new PlanarCarSim(parameters);
            sim.Reset(0f, startSpeed);
            sim.StickX = 0f;
            sim.StickY = 1f;
            return sim;
        }

        private static float TimeToSpeed(PlanarCarSim sim, float target, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick();
                if (sim.ForwardSpeed >= target)
                {
                    return sim.Time;
                }
            }

            return -1f;
        }

        // ---------------------------------------------------------------------------------- the pace

        [Test]
        public void Launch_ZeroToTwentyLandsInTheFeel2Window()
        {
            float before = TimeToSpeed(Launch(ShippedPace(), 0f), 20f, 500);
            float after = TimeToSpeed(Launch(HandlingParams.Balanced, 0f), 20f, 500);

            Console.WriteLine("metric feel2 launch: t(0->20) shipped=" + before.ToString("F2")
                + "s feel2=" + after.ToString("F2")
                + "s (" + (before / after).ToString("F2") + "x sooner)");

            Assert.That(after, Is.GreaterThan(0f), "the FEEL2 car never reached 20 m/s");
            Assert.That(after, Is.LessThanOrEqualTo(1.3f), "0->20 m/s missed the FEEL2 1.3 s target");
        }

        [Test]
        public void Launch_ReachesNinetyPercentOfTopSpeedInsideTheFeel2Window()
        {
            float before = TimeToSpeed(Launch(ShippedPace(), 0f), NinetyPercentOfTopSpeed, 500);
            float after = TimeToSpeed(Launch(HandlingParams.Balanced, 0f), NinetyPercentOfTopSpeed, 500);

            Console.WriteLine("metric feel2 launch90: t(0->90% top) shipped=" + before.ToString("F2")
                + "s feel2=" + after.ToString("F2") + "s at " + NinetyPercentOfTopSpeed.ToString("F1") + " m/s");

            Assert.That(after, Is.GreaterThan(0f), "the FEEL2 car never reached 90% of top speed");
            Assert.That(after, Is.LessThanOrEqualTo(2.7f), "0->90% of top speed missed the FEEL2 2.7 s target");
        }

        [Test]
        public void FreeTopSpeed_SitsInTheFeel2Band()
        {
            var sim = Launch(HandlingParams.Balanced, 0f);
            for (int i = 0; i < 1000; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric feel2 topSpeed: terminal=" + sim.ForwardSpeed.ToString("F2")
                + " m/s (band 29.0..31.0)");

            Assert.That(sim.ForwardSpeed, Is.InRange(29f, 31f), "the FEEL2 terminal speed left its band");
        }

        // ------------------------------------------------------------------------- crash recovery

        [Test]
        public void CrashRecovery_ImpactBoostIsAtLeastThirtyPercentSoonerThanTheCarItReplaces()
        {
            float shipped = TimeToSpeed(Launch(ShippedPace(), 0f), RecoverySpeed, 400);
            float baseOnly = TimeToSpeed(Launch(HandlingParams.Balanced, 0f), RecoverySpeed, 400);

            var impacted = Launch(HandlingParams.Balanced, 0f);
            impacted.State.NotifyImpact(1f);
            float boosted = TimeToSpeed(impacted, RecoverySpeed, 400);

            float gainVsShipped = 1f - (boosted / shipped);
            float gainVsBase = 1f - (boosted / baseOnly);

            Console.WriteLine("metric feel2 recovery: t(0->" + RecoverySpeed.ToString("F0")
                + ") shipped=" + shipped.ToString("F2") + "s base=" + baseOnly.ToString("F2")
                + "s impact=" + boosted.ToString("F2")
                + "s (vs shipped " + (gainVsShipped * 100f).ToString("F1")
                + "%, vs base " + (gainVsBase * 100f).ToString("F1") + "%)");

            // The owner's requirement, stated against the car being replaced: get back up to speed at
            // least 30% faster after a hit.
            Assert.That(gainVsShipped, Is.GreaterThanOrEqualTo(0.30f),
                "the impact boost did not buy 30% of the recovery time back");

            // And the boost has to do something the always-on base factor does not, or the impact window
            // is decoration. The measured gap is small because the base factor is already strong by
            // design (see CatchUpTuning): the window is for the moment after a hit, not a second car.
            Assert.That(gainVsBase, Is.GreaterThan(0f),
                "the impact window did not help at all over the always-on catch-up");
        }

        [Test]
        public void CrashRecovery_ImpactBoostFadesBackToTheBaseFactor()
        {
            var sim = Launch(HandlingParams.Balanced, 0f);
            sim.State.NotifyImpact(1f);

            // Ride out the window at a crawl so the boost cannot be spent, then check the timer is done:
            // past ImpactWindowSeconds the tick must fall back to the base factor.
            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
            }

            float peak = HandlingParams.Balanced.CatchUp.ImpactFactor;
            float baseFactor = HandlingParams.Balanced.CatchUp.BaseFactor;
            float factor = HandlingParams.Balanced.CatchUp.FactorFor(0f, 1f, sim.State.ImpactSeverity);

            Console.WriteLine("metric feel2 impactWindow: after 2 s severity=" + sim.State.ImpactSeverity.ToString("F2")
                + " factor=" + factor.ToString("F2") + " (peak " + peak.ToString("F2")
                + ", base " + baseFactor.ToString("F2") + ")");

            Assert.That(factor, Is.EqualTo(baseFactor).Within(0.001f),
                "the impact factor outlived its window");
        }

        // --------------------------------------------------------------------------- rear wheelspin

        [Test]
        public void RearWheelspin01_IsLoudAtLaunchAndQuietAtCruise()
        {
            var launch = Launch(HandlingParams.Balanced, 0f);
            for (int i = 0; i < 10; i++)
            {
                launch.Tick();
            }

            float atLaunch = launch.Output.RearWheelspin01;

            var cruise = Launch(HandlingParams.Balanced, 25f);
            for (int i = 0; i < 100; i++)
            {
                cruise.Tick();
            }

            float atCruise = cruise.Output.RearWheelspin01;

            Console.WriteLine("metric feel2 wheelspin: launch=" + atLaunch.ToString("F2")
                + " cruise@25=" + atCruise.ToString("F2"));

            Assert.That(atLaunch, Is.GreaterThan(0.5f), "a full-pedal launch must ask the rear tyres for smoke");
            Assert.That(atCruise, Is.LessThan(0.35f), "a cruising car must not be smoking its tyres");
        }

        [Test]
        public void StraightLaunch_HoldsTheLineInsideTwoDegrees()
        {
            var sim = Launch(HandlingParams.Balanced, 0f);
            float worst = 0f;
            for (int i = 0; i < 200; i++)
            {
                sim.Tick();
                float beta = HandlingMath.Abs(sim.BetaDegrees);
                if (beta > worst)
                {
                    worst = beta;
                }
            }

            Console.WriteLine("metric feel2 launchSlip: max|beta|=" + worst.ToString("F2")
                + "deg over 4 s (" + sim.ForwardSpeed.ToString("F1") + " m/s)");

            Assert.That(worst, Is.LessThan(2f), "the doubled torque made the car pull sideways under power");
        }

        // ------------------------------------------------------------------------- zero allocations

        [Test]
        public void Tick_WithTheCatchUpAndTheClamp_AllocatesNothing()
        {
#if NET5_0_OR_GREATER
            var sim = Launch(HandlingParams.Balanced, 20f);

            for (int i = 0; i < 400; i++)
            {
                sim.StickX = i % 2 == 0 ? 0.4f : -0.4f;
                sim.Tick();
            }

            sim.State.NotifyImpact(0.5f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                sim.StickX = i % 2 == 0 ? 0.4f : -0.4f;
                sim.Tick();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("metric feel2 allocations: " + allocated + " bytes over 1000 ticks");
            Assert.That(allocated, Is.EqualTo(0L), "the FEEL2 tick allocated " + allocated + " bytes");
#endif
        }

        // ------------------------------------------------------------------------------ the record

        /// <summary>
        /// Prints both cars side by side on the metrics the doc table quotes. No bound beyond "the new car
        /// is the quicker one": this is the line that goes into DRIVING_MODEL_V2.md, and the individual
        /// tests above are where the contract lives.
        /// </summary>
        [Test]
        public void BeforeAfter_Table_IsTheDocumentedComparison()
        {
            HandlingParams before = ShippedPace();
            HandlingParams after = HandlingParams.Balanced;

            float beforeTwenty = TimeToSpeed(Launch(before, 0f), 20f, 500);
            float afterTwenty = TimeToSpeed(Launch(after, 0f), 20f, 500);
            float beforeNinety = TimeToSpeed(Launch(before, 0f), NinetyPercentOfTopSpeed, 500);
            float afterNinety = TimeToSpeed(Launch(after, 0f), NinetyPercentOfTopSpeed, 500);

            float beforeTop = Terminal(before);
            float afterTop = Terminal(after);

            var beforeRelease = Launch(before, 30f);
            beforeRelease.StickY = 0f;
            for (int i = 0; i < 200; i++)
            {
                beforeRelease.Tick();
            }

            var afterRelease = Launch(after, 30f);
            afterRelease.StickY = 0f;
            for (int i = 0; i < 200; i++)
            {
                afterRelease.Tick();
            }

            Console.WriteLine("metric feel2 table:"
                + " t(0->20) " + beforeTwenty.ToString("F2") + " -> " + afterTwenty.ToString("F2") + " s"
                + " | t(0->90%top) " + (beforeNinety > 0f ? beforeNinety.ToString("F2") : "never")
                + " -> " + afterNinety.ToString("F2") + " s"
                + " | terminal " + beforeTop.ToString("F1") + " -> " + afterTop.ToString("F1") + " m/s"
                + " | released30@4s " + beforeRelease.ForwardSpeed.ToString("F2")
                + " -> " + afterRelease.ForwardSpeed.ToString("F2") + " m/s");

            Assert.That(afterTwenty, Is.LessThan(beforeTwenty), "FEEL2 must launch quicker than the car it replaces");
            Assert.That(afterTop, Is.GreaterThan(beforeTop), "FEEL2 must be faster flat out");

            // The old car tops out under the FEEL2 90% mark (25.9 vs 27.9 m/s), so "quicker to 90%" is not
            // a comparison it can be in: reaching that band at all is already the FEEL2 result.
            if (beforeNinety > 0f)
            {
                Assert.That(afterNinety, Is.LessThan(beforeNinety), "FEEL2 must reach its top band quicker");
            }
            else
            {
                Assert.That(afterNinety, Is.GreaterThan(0f), "FEEL2 never reached 90% of top speed");
                Assert.That(beforeTop, Is.LessThan(NinetyPercentOfTopSpeed),
                    "the replaced car did reach 90% of the FEEL2 top speed, so it must win the comparison");
            }
        }

        private static float Terminal(HandlingParams parameters)
        {
            var sim = Launch(parameters, 0f);
            for (int i = 0; i < 1000; i++)
            {
                sim.Tick();
            }

            return sim.ForwardSpeed;
        }
    }
}
