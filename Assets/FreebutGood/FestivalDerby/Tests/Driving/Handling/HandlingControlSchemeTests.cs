using System;
using FestivalDerby.Driving;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// CTRL1 contract for the selectable throttle source. The stick stays a direction and the pedal
    /// gets its own control, which is what every shipped screen-relative mobile racer does (references
    /// section 3): the V2 ring-throttle rule has no precedent and is the one the owner asked to A/B.
    ///
    /// All three schemes share the same heading loop, the turn commitment and the U-turn lift. Only the
    /// pedal changes:
    ///  - RingThrottle: stick magnitude past the dead zone (the shipped V2 behaviour).
    ///  - GasButton: a digital GAS input; the stick only has to leave the dead zone to steer.
    ///  - AutoAccelerate: on while the stick is out of the dead zone, coasts when it is released.
    ///
    /// Metric lines are printed the way the FP0 tests print theirs: the number the test is judged on.
    /// </summary>
    [TestFixture]
    public sealed class HandlingControlSchemeTests
    {
        private const float Dt = PlanarCarSim.DtSeconds;

        // ------------------------------------------------------------------ GasButton: the pedal

        [Test]
        public void GasButton_StickCentredWithGasHeld_ReachesTwentyInTheWindow()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.GasButton;
            sim.Gas = 1f;
            sim.Reset(0f, 0f);

            float launchTime = -1f;
            for (int i = 0; i < 500; i++)
            {
                sim.Tick();
                if (launchTime < 0f && sim.ForwardSpeed >= 20f)
                {
                    launchTime = sim.Time;
                }
            }

            Console.WriteLine("metric ctrl1 gasbutton launch: t(0->20)=" + launchTime.ToString("F2")
                + "s terminal=" + sim.ForwardSpeed.ToString("F1") + " m/s");

            // Acceptance 5's window, unchanged: the pedal source must not change how the car
            // accelerates. GasButton at full gas produces the same pedal a full-stick RingThrottle
            // launch does (ThrottleAtRing is 1), so the two schemes have to land on the same number.
            // FEEL2 moved the window to 0.9..1.3 s (see HandlingAcceptanceTests.Acceptance5).
            Assert.That(launchTime, Is.GreaterThanOrEqualTo(0.9f).And.LessThanOrEqualTo(1.3f),
                "GasButton 0->20 m/s outside the same window RingThrottle holds");
        }

        [Test]
        public void GasButton_StickWithoutGas_DoesNotMove()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.GasButton;
            sim.Gas = 0f;
            sim.Reset(0f, 0f);
            sim.StickY = 1f;

            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric ctrl1 gasbutton stick-only: v=" + sim.ForwardSpeed.ToString("F3")
                + " m/s after 2 s");

            // The stick is a direction now; without the gas input the car must not roll. This is the
            // failure mode the old "distance = throttle" rule hid: a heading-only drag of the stick.
            Assert.That(sim.ForwardSpeed, Is.LessThan(0.1f), "GasButton moved without a gas request");
        }

        [Test]
        public void GasButton_StickReleasedWithGasHeld_DrivesStraightOnTheCurrentHeading()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.GasButton;
            sim.Gas = 1f;
            sim.Reset(0.4f, 15f);

            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
            }

            float headingDrift = HandlingMath.Abs(HandlingMath.WrapPi(sim.YawRadians - 0.4f));
            Console.WriteLine("metric ctrl1 gasbutton free-straight: drift=" + headingDrift.ToString("F4")
                + " rad v=" + sim.ForwardSpeed.ToString("F1") + " m/s");

            Assert.That(headingDrift, Is.LessThan(0.05f), "holding gas with the stick centred steered the car");
            Assert.That(sim.ForwardSpeed, Is.GreaterThan(15f), "holding gas did not keep the car driving");
        }

        [Test]
        public void GasButton_StickSidewaysWithGas_TurnsTowardTheStick()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.GasButton;
            sim.Gas = 1f;
            sim.Reset(0f, 15f);
            sim.StickX = 1f;
            sim.StickY = 0f;

            for (int i = 0; i < 200; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric ctrl1 gasbutton sideways+gas: yaw="
                + sim.YawRadians.ToString("F3") + " rad error=" + sim.HeadingErrorDegrees.ToString("F1")
                + "deg throttle=" + sim.Output.Throttle.ToString("F2"));

            Assert.That(sim.YawRadians, Is.LessThan(-0.9f), "the car did not turn toward the stick");
            Assert.That(HandlingMath.Abs(sim.HeadingErrorDegrees), Is.LessThan(25f), "the car did not point at the stick");
            Assert.That(sim.Output.Throttle, Is.GreaterThan(0.5f), "gas was dropped while steering");
        }

        // ------------------------------------------------------------- AutoAccelerate: on the stick

        [Test]
        public void AutoAccelerate_StickWithoutGas_Moves()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.AutoAccelerate;
            sim.Gas = 0f;
            sim.Reset(0f, 0f);
            sim.StickY = 1f;

            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric ctrl1 auto-accelerate stick-only: v=" + sim.ForwardSpeed.ToString("F1")
                + " m/s after 2 s");

            // Death Rally's rule: asking for a direction is asking to go there.
            Assert.That(sim.ForwardSpeed, Is.GreaterThan(5f), "AutoAccelerate did not accelerate on the stick");
        }

        [Test]
        public void AutoAccelerate_StickReleased_CoastsWithoutReversing()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.AutoAccelerate;
            sim.Gas = 0f;
            sim.Reset(0f, 0f);
            sim.StickY = 1f;

            for (int i = 0; i < 60; i++)
            {
                sim.Tick();
            }

            float atRelease = sim.ForwardSpeed;
            sim.StickY = 0f;
            for (int i = 0; i < 150; i++)
            {
                sim.Tick();
            }

            Console.WriteLine("metric ctrl1 auto-accelerate coast: v@release=" + atRelease.ToString("F1")
                + " m/s v@+3s=" + sim.ForwardSpeed.ToString("F1") + " m/s");

            Assert.That(sim.ForwardSpeed, Is.LessThan(atRelease), "releasing the stick did not lift the throttle");
            Assert.That(sim.ForwardSpeed, Is.GreaterThanOrEqualTo(0f), "releasing the stick reversed the car");
        }

        // --------------------------------------------------- AutoAccelerate: the auto-unstick pulse

        [Test]
        public void AutoUnstick_FiresAfterOneSecond_AndPulsesForSevenTenths()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = default(IntentState);

            int firstReverseTick = -1;
            int reverseTicks = 0;
            int lastReverseTick = -1;

            // 2 s only: a second pulse would start after another full second of being stuck, so one
            // pulse is all that can fit and the measurement stays about this pulse.
            for (int i = 0; i < 100; i++)
            {
                IntentOutput output = IntentMapper.Evaluate(
                    0f, 1f, 0f, 0f, 0f, Dt, 0f, ControlScheme.AutoAccelerate, 0f, p, ref state);

                if (output.AutoReverse)
                {
                    if (firstReverseTick < 0)
                    {
                        firstReverseTick = i;
                    }

                    reverseTicks++;
                    lastReverseTick = i;
                }
            }

            Console.WriteLine("metric ctrl1 autounstick: firstTick=" + firstReverseTick
                + " (" + (firstReverseTick * Dt).ToString("F2") + "s) ticks=" + reverseTicks
                + " (" + (reverseTicks * Dt).ToString("F2") + "s) lastTick=" + lastReverseTick);

            Assert.That(firstReverseTick * Dt, Is.GreaterThanOrEqualTo(1.0f).And.LessThanOrEqualTo(1.2f),
                "the pulse did not wait a full second of being stuck");
            Assert.That(reverseTicks, Is.GreaterThanOrEqualTo(33).And.LessThanOrEqualTo(37),
                "the reverse pulse is not ~0.7 s");
        }

        /// <summary>
        /// Catch playtest 1: on the carrier the speed is carrier-relative, so a car parked against the cab at
        /// full pedal reads as wedged and the pulse threw it off the bed. On moving ground it never fires.
        /// </summary>
        [Test]
        public void AutoUnstick_OnMovingGround_NeverPulses()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = default(IntentState);
            for (int i = 0; i < 200; i++)
            {
                IntentOutput output = IntentMapper.Evaluate(
                    0f, 1f, 0f, 0f, 0f, Dt, 0f, ControlScheme.AutoAccelerate, 0f, true, p, ref state);
                Assert.That(output.AutoReverse, Is.False, "tick " + i + " pulsed reverse on the carrier");
            }
        }

        [Test]
        public void AutoUnstick_ReachingMovingGround_CancelsARunningPulse()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = default(IntentState);
            bool pulsing = false;
            for (int i = 0; i < 100 && !pulsing; i++)
            {
                pulsing = IntentMapper.Evaluate(
                    0f, 1f, 0f, 0f, 0f, Dt, 0f, ControlScheme.AutoAccelerate, 0f, false, p, ref state).AutoReverse;
            }

            Assert.That(pulsing, Is.True, "precondition: a wedged car on static ground pulses");
            IntentOutput onCarrier = IntentMapper.Evaluate(
                0f, 1f, 0f, 0f, 0f, Dt, 0f, ControlScheme.AutoAccelerate, 0f, true, p, ref state);
            Assert.That(onCarrier.AutoReverse, Is.False, "the pulse must stop the moment a wheel is on the carrier");
            Assert.That(state.ReversePulseSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void AutoUnstick_ThroughTheStep_ReadsTheMovingGroundFlag()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = default(HandlingState);
            for (int i = 0; i < 200; i++)
            {
                var input = new HandlingInput(
                    0f, 1f, 0f, 0f, -1f, false, false, 1f, 0f, 0f, 0f, 1200f, 1500f, 1.25f, -1.25f,
                    6000f, 6000f, 1f, 1f, 1f, 4, Dt, p, ControlScheme.AutoAccelerate, 0f, onMovingGround: true);
                HandlingOutput output = HandlingStep.Step(in input, ref state);
                Assert.That(output.AutoReverse, Is.False, "tick " + i + " pulsed reverse on the carrier");
            }
        }

        // The owner's drive-rescue spec (S2): a wedged car needs the reverse pulse no matter which
        // pedal source it runs, because the heading loop cannot rotate a pinned car. Only an
        // explicit tuning Off keeps it out.
        [Test]
        public void AutoUnstick_OffTuning_NeverPulses()
        {
            HandlingParams p = HandlingParams.Balanced.WithAutoUnstick(AutoUnstickTuning.Off);
            foreach (ControlScheme scheme in new[]
                     {
                         ControlScheme.RingThrottle, ControlScheme.GasButton,
                         ControlScheme.AutoAccelerate
                     })
            {
                var state = default(IntentState);

                for (int i = 0; i < 200; i++)
                {
                    IntentOutput output = IntentMapper.Evaluate(
                        0f, 1f, 0f, 0f, 0f, Dt, 0f, scheme, 1f, p, ref state);
                    Assert.That(output.AutoReverse, Is.False, scheme + " fired a disabled auto-unstick");
                }
            }
        }

        [Test]
        public void AutoUnstick_PulsesUnderRingThrottle()
        {
            HandlingParams p = HandlingParams.Balanced;
            var state = default(IntentState);

            int firstReverseTick = -1;
            int reverseTicks = 0;

            for (int i = 0; i < 100; i++)
            {
                IntentOutput output = IntentMapper.Evaluate(
                    0f, 1f, 0f, 0f, 0f, Dt, 0f, ControlScheme.RingThrottle, 0f, p, ref state);

                if (output.AutoReverse)
                {
                    if (firstReverseTick < 0)
                    {
                        firstReverseTick = i;
                    }

                    reverseTicks++;
                }
            }

            Assert.That(firstReverseTick * Dt, Is.GreaterThanOrEqualTo(1.0f).And.LessThanOrEqualTo(1.2f),
                "the pulse did not wait a full second of being stuck");
            Assert.That(reverseTicks, Is.GreaterThanOrEqualTo(33).And.LessThanOrEqualTo(37),
                "the reverse pulse is not ~0.7 s");
        }

        [Test]
        public void AutoAccelerate_AutoUnstick_BacksOffAWall_ThenStops()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = ControlScheme.AutoAccelerate;
            sim.Gas = 0f;

            // The wall model: speed is clamped to <= 0, so the only way off it is the reverse pulse.
            sim.WallClampForwardSpeed = true;
            sim.Reset(0f, 0f);
            sim.StickY = 1f;

            bool sawReverse = false;
            float speedBeforePulse = 0f;
            float minSpeed = 0f;
            int ticksReversing = 0;

            // 100 ticks = 2 s, which is one trigger window plus one pulse and its aftermath. A second
            // pulse would only start after another full stuck second, so the window isolates this one.
            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
                if (sim.Output.AutoReverse)
                {
                    sawReverse = true;
                    ticksReversing++;
                    if (sim.ForwardSpeed < minSpeed)
                    {
                        minSpeed = sim.ForwardSpeed;
                    }
                }

                if (i == 45)
                {
                    speedBeforePulse = sim.ForwardSpeed;
                }
            }

            Console.WriteLine("metric ctrl1 autounstick wall: v@0.9s=" + speedBeforePulse.ToString("F3")
                + " minV=" + minSpeed.ToString("F1") + " m/s reverseTicks=" + ticksReversing
                + " v@end=" + sim.ForwardSpeed.ToString("F1") + " m/s");

            // Nothing moved before the trigger window had elapsed, so the pulse cannot be an accidental
            // side effect of holding the stick forward against a wall.
            Assert.That(speedBeforePulse, Is.GreaterThanOrEqualTo(-0.01f), "the car reversed before the pulse");
            Assert.That(sawReverse, Is.True, "the auto-unstick never fired against the wall");
            Assert.That(ticksReversing, Is.GreaterThanOrEqualTo(33).And.LessThanOrEqualTo(37),
                "the wall pulse is not ~0.7 s");
            Assert.That(minSpeed, Is.LessThan(-0.3f), "the reverse pulse did not actually move the car");
            // A pulse, not a mode: it has to end while the player is still holding gas.
            Assert.That(sim.Output.AutoReverse, Is.False, "the pulse never ended");
        }

        // ------------------------------------------- the U-turn contract holds in every scheme

        [TestCase(ControlScheme.RingThrottle, 0f)]
        [TestCase(ControlScheme.GasButton, 1f)]
        [TestCase(ControlScheme.AutoAccelerate, 0f)]
        public void EveryScheme_HoldsTheUTurnCommitment(ControlScheme scheme, float gas)
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Scheme = scheme;
            sim.Gas = gas;
            sim.Reset(0f, 20f);
            sim.StickX = 0f;
            sim.StickY = -1f;

            bool committed = false;
            sbyte sign = 0;
            int flips = 0;
            float settleTime = -1f;
            float maxBetaDegrees = 0f;

            for (int i = 0; i < 250; i++)
            {
                sim.Tick();
                HandlingOutput o = sim.Output;

                if (o.TurnCommitted)
                {
                    committed = true;
                }

                if (o.TurnSign != 0)
                {
                    if (sign == 0)
                    {
                        sign = o.TurnSign;
                    }
                    else if (o.TurnSign != sign)
                    {
                        flips++;
                        sign = o.TurnSign;
                    }
                }

                if (settleTime < 0f && HandlingMath.Abs(o.HeadingErrorRadians) <= 20f * HandlingParams.RadiansPerDegree)
                {
                    settleTime = sim.Time;
                }

                float betaDegrees = HandlingMath.Abs(o.BetaRadians) * HandlingParams.DegreesPerRadian;
                if (betaDegrees > maxBetaDegrees)
                {
                    maxBetaDegrees = betaDegrees;
                }
            }

            Console.WriteLine("metric ctrl1 u-turn " + scheme + ": committed=" + committed
                + " flips=" + flips + " settle=" + settleTime.ToString("F2") + "s max|beta|="
                + maxBetaDegrees.ToString("F1") + "deg");

            Assert.That(committed, Is.True, scheme + " never committed the turn");
            Assert.That(flips, Is.EqualTo(0), scheme + " flipped the turn direction");
            Assert.That(settleTime, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(3.5f),
                scheme + " did not complete the U-turn inside the spec window");
            Assert.That(maxBetaDegrees, Is.LessThanOrEqualTo(p.BetaMaxRadians * HandlingParams.DegreesPerRadian + 3f),
                scheme + " pushed past the sideslip cap");
        }
    }
}
