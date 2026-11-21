using System;
using FestivalDerby.Driving.Handling;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// The V2e.1 feel retune contract: a lift is a real brake, the pedal starts at zero, engine
    /// braking belongs to a lifted pedal only. The FEEL2 pace section below re-opened the top speed on
    /// purpose (2026-10-15 playtest), which is why the ceiling assert is a band now, not a cap.
    /// </summary>
    [TestFixture]
    public class HandlingFeelTests
    {
        private const float Dt = 0.02f;

        /// <summary>The shipped sim terminal speed (metric test5) before the V2e.1 retune: the floor of
        /// the FEEL2 band. The owner cut the free top speed twice in 2026-10 and then asked for the fast
        /// car back, so the band says "at least the shipped 28, never past 30.5".</summary>
        private const float ShippedFreeTopSpeed = 28.0f;

        /// <summary>FEEL2 ceiling: top speed 31 with the curve tapering to 1.0 leaves ~30 m/s flat out.</summary>
        private const float Feel2TopSpeedCeiling = 30.5f;

        [Test]
        public void ReleasedCar_From30_IsUnder2After4Seconds()
        {
            var sim = new PlanarCarSim(HandlingParams.Balanced);
            sim.Reset(0f, 30f);
            for (int i = 0; i < 200; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric feel coast30: v(4s)=" + sim.ForwardSpeed.ToString("F2") + " m/s");
            // FEEL2 trimmed the engine brake 5 -> 4.5 on the owner's "less lift-off braking" note, which
            // moves a 4 s coast from 30 m/s to 3.3 m/s instead of 1.8. The contract is still "a lifted car
            // sheds almost all of it", so the bound follows the trim instead of the trim being undone.
            Assert.That(sim.ForwardSpeed, Is.LessThan(4f), "a released car must shed almost all of 30 m/s in 4 s");
        }

        /// <summary>
        /// Catch playtest 1 (touch-to-drive): with no BRAKE or REVERSE button on screen, lifting the thumb
        /// is the only way to slow down, so the lift has to bite like a brake. Pinned against every preset
        /// so a retune of one cannot quietly bring the soft 4.5 m/s^2 lift back.
        /// </summary>
        [Test]
        public void EngineBrake_ReleasingTheStickReadsAsBraking()
        {
            Assert.That(HandlingParams.Balanced.EngineBrakeMetersPerSecondSquared, Is.EqualTo(7f).Within(0.5f));
            Assert.That(HandlingParams.Grippy.EngineBrakeMetersPerSecondSquared, Is.EqualTo(7f).Within(0.5f));
            Assert.That(HandlingParams.Drifty.EngineBrakeMetersPerSecondSquared, Is.EqualTo(7f).Within(0.5f));
        }

        [Test]
        public void Pedal_JustAboveTheDeadZone_IsNearZero()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            float magnitude = p.DeadZone + 0.01f;
            IntentOutput o = default;
            for (int i = 0; i < 100; i++)
            {
                o = IntentMapper.Evaluate(magnitude, 0f, 0f, 0f, 0f, Dt, p, ref state);
            }

            Console.WriteLine("metric feel pedalEdge: throttle=" + o.Throttle.ToString("F3"));
            Assert.That(o.HasIntent, Is.True);
            Assert.That(o.Throttle, Is.LessThan(0.05f), "the pedal must start at zero at the dead-zone edge, not at a floor");
        }

        [Test]
        public void EngineBrake_IsGoneByTheFadePedal()
        {
            HandlingParams p = HandlingParams.Balanced;
            float curve;

            float resistanceFull = 0f;
            float smoothedFull = 1f;
            LongitudinalModel.Evaluate(1f, 20f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothedFull, out curve, out resistanceFull);

            float resistanceAtFade = 0f;
            float smoothedAtFade = p.EngineBrakeFadePedal;
            LongitudinalModel.Evaluate(p.EngineBrakeFadePedal, 20f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothedAtFade, out curve, out resistanceAtFade);

            float resistanceReleased = 0f;
            float smoothedReleased = 0f;
            LongitudinalModel.Evaluate(0f, 20f, Dt, false, false, 1f, 1f, 4, 1200f, p, ref smoothedReleased, out curve, out resistanceReleased);

            float dragOnly = -1200f * p.CoastDragPerSecond * 20f;
            float withEngineBrake = dragOnly - (1200f * p.EngineBrakeMetersPerSecondSquared);
            Console.WriteLine("metric feel fullPedalCoast: full=" + resistanceFull.ToString("F1")
                + " atFade=" + resistanceAtFade.ToString("F1")
                + " released=" + resistanceReleased.ToString("F1")
                + " dragOnly=" + dragOnly.ToString("F1"));
            Assert.That(resistanceFull, Is.EqualTo(dragOnly).Within(1f), "a held pedal must not pay the engine brake");
            Assert.That(resistanceAtFade, Is.EqualTo(dragOnly).Within(1f),
                "the engine brake must be fully gone at EngineBrakeFadePedal");
            Assert.That(resistanceReleased, Is.EqualTo(withEngineBrake).Within(1f),
                "a released pedal carries the whole engine brake");
        }

        /// <summary>
        /// The brake belongs to the release, not to the whole low pedal range: a fifth of stick travel
        /// is a real request for power, so it must not slow the car. This pins the behaviour, not the
        /// band, so a future response-curve change cannot silently re-brake a partial stick.
        /// </summary>
        [Test]
        public void EngineBrake_IsGoneByAFifthStickDeflection()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = new IntentState();
            IntentOutput o = default;
            for (int i = 0; i < 100; i++)
            {
                o = IntentMapper.Evaluate(0.2f, 0f, 0f, 0f, 0f, Dt, p, ref state);
            }

            float fade = 1f - HandlingMath.Smoothstep(0f, p.EngineBrakeFadePedal, o.Throttle);
            Console.WriteLine("metric feel partialStick: stick=0,200 throttle=" + o.Throttle.ToString("F3")
                + " engineBrakeLeft=" + (p.EngineBrakeMetersPerSecondSquared * fade).ToString("F2") + " m/s^2");
            Assert.That(o.HasIntent, Is.True);
            Assert.That(o.Throttle, Is.GreaterThan(0f), "a partial stick must still ask for power");
            Assert.That(p.EngineBrakeMetersPerSecondSquared * fade, Is.LessThan(0.5f),
                "a 20% stick is power, not a lift: the engine brake must be off");
        }

        [Test]
        public void FreeTopSpeed_SitsInTheFeel2Band()
        {
            var sim = new PlanarCarSim(HandlingParams.Balanced);
            sim.Reset(0f, 0f);
            sim.StickX = 0f;
            sim.StickY = 1f;
            for (int i = 0; i < 1000; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric feel topSpeed: terminal=" + sim.ForwardSpeed.ToString("F1")
                + " m/s (FEEL2 band " + ShippedFreeTopSpeed.ToString("F1")
                + ".." + Feel2TopSpeedCeiling.ToString("F1") + ")");
            Assert.That(sim.ForwardSpeed, Is.InRange(ShippedFreeTopSpeed, Feel2TopSpeedCeiling),
                "the free top speed left the FEEL2 band");
        }
    }
}
