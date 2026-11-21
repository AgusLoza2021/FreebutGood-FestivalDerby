using System;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// CTRL1 A/B: the same doc feel-protocol shapes run with the pedal coming from the scheme instead of
    /// from the acceptance rig's throttle override. The FP0 protocol tests hold the pedal at the value
    /// that balances drag at the entry speed, which is the right way to isolate a handling change but
    /// hides a control change: it removes the only thing CTRL1 moves.
    ///
    /// Every protocol shape holds the stick at a full-deflection bearing, so RingThrottle reads magnitude
    /// 1 and GasButton reads gas 1 and the two schemes have to produce the same car. That is the claim
    /// this fixture measures, and it is the reason the schemes can be A/B'd at all: the pedal source is
    /// the only difference. Where they diverge is the partial stick, measured at the bottom of this file.
    ///
    /// The jitter is a fixed sine instead of the FP0 fixture's LCG: the sequence only has to be
    /// deterministic and reproducible here, not the same object.
    /// </summary>
    [TestFixture]
    public sealed class HandlingControlSchemeProtocolTests
    {
        private static readonly ControlScheme[] Schemes =
        {
            ControlScheme.RingThrottle,
            ControlScheme.GasButton
        };

        private struct Metrics
        {
            public float Settle;
            public float MaxBetaDegrees;
            public float MinSpeed;
            public float TerminalSpeed;
            public int Flips;
            public int Alternations;
            public int LastYawSign;
        }

        // The same epsilon the FP0 fixture uses: below it the yaw rate is numerical noise, not a turn.
        private const float YawRateSignEpsilon = 0.05f;

        // ---------------------------------------------------------------------------- protocol 1

        [Test]
        public void Protocol_Launch_And_Straight_And_UTurn_AreTheSameCarInBothSchemes()
        {
            AssertProtocolPair("launch 0->20 m/s @0", RunLaunch,
                settleTolerance: 0.02f, betaTolerance: 0.5f, speedTolerance: 0.2f);
            AssertProtocolPair("straight @25 m/s, 3deg jitter", RunStraight25,
                settleTolerance: 0.02f, betaTolerance: 0.5f, speedTolerance: 0.2f);
            AssertProtocolPair("U-turn 180deg @20 m/s", RunUTurn180,
                settleTolerance: 0.02f, betaTolerance: 0.5f, speedTolerance: 0.2f);
        }

        // ----------------------------------------------------------------------- protocols 3, 6, 9

        [Test]
        public void Protocol_Slalom_Tightening_And_SpinRecovery_KeepTheirSteeringShape()
        {
            AssertProtocolPair("slalom @18 m/s", RunSlalom18,
                settleTolerance: 0.02f, betaTolerance: 0.5f, speedTolerance: 0.2f);
            AssertProtocolPair("tightening 0->90deg over 4 s @20 m/s", RunTightening,
                settleTolerance: 0.02f, betaTolerance: 0.5f, speedTolerance: 0.2f);
            AssertProtocolPair("spin recovery 90deg kick @15 m/s", RunSpinRecovery,
                settleTolerance: 0.05f, betaTolerance: 0.5f, speedTolerance: 0.2f);
        }

        // ---------------------------------------------------------------- where the schemes differ

        /// <summary>
        /// The pedal source, as a table. The two schemes agree at a full ring and part company below it:
        /// RingThrottle's pedal is the stick magnitude, GasButton's is the GAS input and the stick only
        /// has to clear the dead zone. That difference is the whole point of CTRL1, and it is the number
        /// the DriveLab A/B has to judge, not a defect.
        /// </summary>
        [Test]
        public void PartialStick_ThePedalSourceIsTheDifference()
        {
            HandlingParams p = HandlingParams.Balanced;

            float[] magnitudes = { 0.2f, 0.5f, 0.8f, 1f };
            float[] ringThrottle = new float[magnitudes.Length];
            float[] gasHalf = new float[magnitudes.Length];
            float[] gasFull = new float[magnitudes.Length];

            for (int i = 0; i < magnitudes.Length; i++)
            {
                ringThrottle[i] = SteadyPedal(p, ControlScheme.RingThrottle, magnitudes[i], 0f);
                gasHalf[i] = SteadyPedal(p, ControlScheme.GasButton, magnitudes[i], 0.5f);
                gasFull[i] = SteadyPedal(p, ControlScheme.GasButton, magnitudes[i], 1f);
            }

            Console.WriteLine("metric ctrl1 pedal table: ");
            for (int i = 0; i < magnitudes.Length; i++)
            {
                Console.WriteLine("metric ctrl1 pedal |s|=" + magnitudes[i].ToString("F1")
                    + " ring=" + ringThrottle[i].ToString("F3")
                    + " gas0.5=" + gasHalf[i].ToString("F3")
                    + " gas1=" + gasFull[i].ToString("F3"));
            }

            // RingThrottle has to rise with the stick (that is the shipped rule) and meet GasButton at
            // the ring, where both ask for a full pedal.
            for (int i = 1; i < magnitudes.Length; i++)
            {
                Assert.That(ringThrottle[i], Is.GreaterThan(ringThrottle[i - 1]),
                    "the ring pedal stopped rising with the stick");
            }

            Assert.That(ringThrottle[magnitudes.Length - 1], Is.EqualTo(gasFull[magnitudes.Length - 1]).Within(0.01f),
                "the schemes disagree at a full ring, so the protocol A/B would not be comparing one car");
            Assert.That(gasFull[0], Is.EqualTo(1f).Within(0.001f), "GasButton ignored the gas input");
            Assert.That(gasHalf[0], Is.EqualTo(0.5f).Within(0.001f), "GasButton ignored a partial gas input");
            Assert.That(gasFull[0], Is.GreaterThan(ringThrottle[0]),
                "a full GAS request off a barely-out stick must beat the ring's near-zero pedal");
        }

        // ------------------------------------------------------------------------------ the runs

        private static void AssertProtocolPair(string name, Func<ControlScheme, float, Metrics> run,
            float settleTolerance, float betaTolerance, float speedTolerance)
        {
            Metrics ring = run(ControlScheme.RingThrottle, 0f);
            Metrics gas = run(ControlScheme.GasButton, 1f);

            Console.WriteLine("metric ctrl1 protocol " + name + ": ring[settle=" + ring.Settle.ToString("F2")
                + "s max|beta|=" + ring.MaxBetaDegrees.ToString("F1") + "deg flips=" + ring.Flips
                + " minV=" + ring.MinSpeed.ToString("F1") + " alt=" + ring.Alternations
                + " vEnd=" + ring.TerminalSpeed.ToString("F1") + "] gas[settle=" + gas.Settle.ToString("F2")
                + "s max|beta|=" + gas.MaxBetaDegrees.ToString("F1") + "deg flips=" + gas.Flips
                + " minV=" + gas.MinSpeed.ToString("F1") + " alt=" + gas.Alternations
                + " vEnd=" + gas.TerminalSpeed.ToString("F1") + "]");

            Assert.That(gas.Flips, Is.EqualTo(ring.Flips), name + ": the schemes disagree on yaw flips");
            Assert.That(gas.Alternations, Is.EqualTo(ring.Alternations),
                name + ": the schemes disagree on how the car weaves");
            Assert.That(Math.Abs(gas.Settle - ring.Settle), Is.LessThanOrEqualTo(settleTolerance),
                name + ": the schemes disagree on how long the manoeuvre takes");
            Assert.That(Math.Abs(gas.MaxBetaDegrees - ring.MaxBetaDegrees), Is.LessThanOrEqualTo(betaTolerance),
                name + ": the schemes disagree on sideslip");
            Assert.That(Math.Abs(gas.TerminalSpeed - ring.TerminalSpeed), Is.LessThanOrEqualTo(speedTolerance),
                name + ": the schemes disagree on speed");
        }

        private static Metrics RunLaunch(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 0f);
            sim.StickX = 0f;
            sim.StickY = 1f;

            var m = EmptyMetrics();
            for (int i = 0; i < 500; i++)
            {
                sim.Tick();
                if (m.Settle < 0f && sim.ForwardSpeed >= 20f)
                {
                    m.Settle = sim.Time;
                }
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        private static Metrics RunStraight25(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 25f);
            var m = EmptyMetrics();

            for (int i = 0; i < 100; i++)
            {
                float jitter = Rad(3f * (float)Math.Sin(i * 0.37f));
                sim.StickX = (float)Math.Sin(jitter);
                sim.StickY = (float)Math.Cos(jitter);
                sim.Tick();
                CountYawFlip(sim, ref m);
                CountSideslip(sim, ref m);
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        private static Metrics RunUTurn180(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 20f);
            var m = EmptyMetrics();

            for (int i = 0; i < 400; i++)
            {
                float jitter = Rad(180f + 3f * (float)Math.Sin(i * 0.29f));
                sim.StickX = (float)Math.Sin(jitter);
                sim.StickY = (float)Math.Cos(jitter);
                sim.Tick();

                float error = Math.Abs(sim.Output.HeadingErrorRadians);
                if (m.Settle < 0f && error < Rad(20f))
                {
                    m.Settle = sim.Time;
                }

                if (error > Rad(20f))
                {
                    CountYawFlip(sim, ref m);
                }

                CountSideslip(sim, ref m);
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        private static Metrics RunSlalom18(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 18f);
            var m = EmptyMetrics();
            m.MinSpeed = 18f;

            float previousY = 0f;
            int lastSign = 0;
            for (int i = 0; i < 600; i++)
            {
                int gate = (int)(sim.Time / 0.6f);
                float bearing = Rad(18f) * (gate % 2 == 0 ? -1f : 1f);
                sim.StickX = (float)Math.Sin(bearing);
                sim.StickY = (float)Math.Cos(bearing);
                sim.Tick();

                CountSideslip(sim, ref m);
                if (sim.ForwardSpeed < m.MinSpeed)
                {
                    m.MinSpeed = sim.ForwardSpeed;
                }

                int sign = Math.Sign(sim.PositionY - previousY);
                if (sign != 0 && lastSign != 0 && sign != lastSign)
                {
                    m.Alternations++;
                }

                if (sign != 0)
                {
                    lastSign = sign;
                }

                previousY = sim.PositionY;
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        private static Metrics RunTightening(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 20f);
            var m = EmptyMetrics();
            m.MinSpeed = 20f;

            for (int i = 0; i < 400; i++)
            {
                float bearing = Rad(HandlingMath.Clamp01(sim.Time / 4f) * 90f);
                sim.StickX = (float)Math.Sin(bearing);
                sim.StickY = (float)Math.Cos(bearing);
                sim.Tick();

                CountYawFlip(sim, ref m);
                CountSideslip(sim, ref m);
                if (sim.ForwardSpeed < m.MinSpeed)
                {
                    m.MinSpeed = sim.ForwardSpeed;
                }
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        private static Metrics RunSpinRecovery(ControlScheme scheme, float gas)
        {
            var sim = NewSim(scheme, gas, 0f, 15f);
            sim.YawRadians = Rad(90f);
            sim.StickX = 0f;
            sim.StickY = 1f;

            var m = EmptyMetrics();
            for (int i = 0; i < 200; i++)
            {
                sim.Tick();
                if (m.Settle < 0f && Math.Abs(sim.Output.HeadingErrorRadians) < Rad(20f))
                {
                    m.Settle = sim.Time;
                }

                CountYawFlip(sim, ref m);
                CountSideslip(sim, ref m);
            }

            m.TerminalSpeed = sim.ForwardSpeed;
            return m;
        }

        // ----------------------------------------------------------------------------- helpers

        /// <summary>
        /// The pedal a scheme settles on for a held stick after the low-pass has converged. ThrottleOverride
        /// stays at -1 on purpose: the override would answer the question for the ring scheme.
        /// </summary>
        private static float SteadyPedal(HandlingParams p, ControlScheme scheme, float magnitude, float gas)
        {
            var sim = new PlanarCarSim(p);
            sim.Scheme = scheme;
            sim.Gas = gas;
            float bearing = Rad(25f);
            sim.Reset(0f, 0f);
            sim.StickX = (float)Math.Sin(bearing) * magnitude;
            sim.StickY = (float)Math.Cos(bearing) * magnitude;

            for (int i = 0; i < 40; i++)
            {
                sim.Tick();
            }

            return sim.Output.Throttle;
        }

        private static PlanarCarSim NewSim(ControlScheme scheme, float gas, float yawRadians, float speed)
        {
            var sim = new PlanarCarSim(HandlingParams.Balanced);
            sim.Scheme = scheme;
            sim.Gas = gas;
            sim.Reset(yawRadians, speed);
            return sim;
        }

        private static Metrics EmptyMetrics()
        {
            return new Metrics
            {
                Settle = -1f,
                TerminalSpeed = 0f,
            };
        }

        private static void CountYawFlip(PlanarCarSim sim, ref Metrics m)
        {
            if (Math.Abs(sim.YawRate) <= YawRateSignEpsilon)
            {
                return;
            }

            int sign = Math.Sign(sim.YawRate);
            if (m.LastYawSign != 0 && sign != m.LastYawSign)
            {
                m.Flips++;
            }

            m.LastYawSign = sign;
        }

        private static void CountSideslip(PlanarCarSim sim, ref Metrics m)
        {
            float beta = Math.Abs(sim.BetaDegrees);
            if (beta > m.MaxBetaDegrees)
            {
                m.MaxBetaDegrees = beta;
            }
        }

        private static float Rad(float degrees)
        {
            return degrees * HandlingParams.RadiansPerDegree;
        }
    }
}
