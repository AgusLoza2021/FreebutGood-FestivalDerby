using System;
using FestivalDerby.Driving.Handling;
using NUnit.Framework;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// The seven acceptance scenarios from Documentation/Design/DRIVING_MODEL_V2.md, run closed-loop
    /// against <see cref="PlanarCarSim"/> at a fixed 1/50 s tick. Every test prints the metric it is
    /// judged on, so a rerun after a retune produces comparable numbers instead of a bare pass/fail.
    /// </summary>
    [TestFixture]
    public class HandlingAcceptanceTests
    {
        /// <summary>
        /// A yaw rate below this is the car sitting still, not a turn direction. The "sign never flips"
        /// criteria are checked against it so a numerical zero crossing is not reported as a spin.
        /// </summary>
        private const float YawRateSignEpsilon = 0.05f;

        private static float Deg(float radians)
        {
            return radians * HandlingParams.DegreesPerRadian;
        }

        private static float Rad(float degrees)
        {
            return degrees * HandlingParams.RadiansPerDegree;
        }

        // ------------------------------------------------------------------ 1. the 360 degree sweep

        [Test]
        public void Acceptance1_FullRingSweep_At20_PedalNeverReversesAndSpeedHolds()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Reset(0f, 20f);

            const float sweepSeconds = 4f;
            int ticks = (int)(sweepSeconds / PlanarCarSim.DtSeconds);

            float minThrottle = 1f;
            float maxBeta = 0f;
            float maxYawRatio = 0f;
            float minSpeed = sim.ForwardSpeed;
            float maxSpeed = sim.ForwardSpeed;

            for (int i = 0; i < ticks; i++)
            {
                float phase = HandlingMath.TwoPi * sim.Time / sweepSeconds;
                sim.StickX = (float)Math.Sin(phase);
                sim.StickY = (float)Math.Cos(phase);
                sim.Tick();

                HandlingOutput o = sim.Output;
                if (o.Throttle < minThrottle)
                {
                    minThrottle = o.Throttle;
                }

                if (Math.Abs(o.BetaRadians) > maxBeta)
                {
                    maxBeta = Math.Abs(o.BetaRadians);
                }

                if (o.YawRateMaxRadiansPerSecond > 0f)
                {
                    float ratio = Math.Abs(sim.YawRate) / o.YawRateMaxRadiansPerSecond;
                    if (ratio > maxYawRatio)
                    {
                        maxYawRatio = ratio;
                    }
                }

                if (sim.ForwardSpeed < minSpeed)
                {
                    minSpeed = sim.ForwardSpeed;
                }

                if (sim.ForwardSpeed > maxSpeed)
                {
                    maxSpeed = sim.ForwardSpeed;
                }

                Assert.That(o.Throttle, Is.GreaterThanOrEqualTo(0f), "throttle went negative");
            }

            Console.WriteLine(
                "metric test1 sweep: minThrottle=" + minThrottle.ToString("F3")
                + " max|beta|=" + Deg(maxBeta).ToString("F1") + "deg (limit " + (Deg(p.BetaMaxRadians) + 3f).ToString("F1") + ")"
                + " max|w|/yawMax=" + maxYawRatio.ToString("F3")
                + " speed=" + minSpeed.ToString("F1") + ".." + maxSpeed.ToString("F1") + " m/s");

            Assert.That(Deg(maxBeta), Is.LessThanOrEqualTo(Deg(p.BetaMaxRadians) + 3f), "sideslip past betaMax + 3 deg");
            Assert.That(maxYawRatio, Is.LessThanOrEqualTo(1.2f), "yaw rate past 1.2 * yawMax");
            Assert.That(minSpeed, Is.GreaterThanOrEqualTo(6f), "speed fell below 6 m/s");
            Assert.That(minThrottle, Is.GreaterThanOrEqualTo(0f), "throttle went negative");
        }

        // ---------------------------------------------- 2. stick dead astern with +/-3 degrees of jitter

        [Test]
        public void Acceptance2_StickDeadAsternWithJitter_CommitHoldsAndUTurnCompletes()
        {
            HandlingParams p = HandlingParams.Balanced;
            float jitter = 3f;
            HeldStickMetrics m = RunHeldStick(p, 20f, 180f, jitter, 4f, 20261013);

            Console.WriteLine(
                "metric test2 jitter: committed=" + m.Committed + " turnSign=" + m.CommitSign
                + " settle(|e|<20deg)=" + m.SettleTime.ToString("F2") + "s"
                + " worstOppositeYaw=" + Deg(m.WorstOppositeYawRate).ToString("F2") + "deg/s"
                + " max|beta|=" + Deg(m.MaxBeta).ToString("F1") + "deg (limit " + (Deg(p.BetaMaxRadians) + 3f).ToString("F1") + ")"
                + " speed=" + m.MinSpeed.ToString("F1") + ".." + m.MaxSpeed.ToString("F1") + " m/s");

            Assert.That(m.Committed, Is.True, "the turn never committed past 150 deg");
            Assert.That(m.WorstOppositeYawRate, Is.LessThanOrEqualTo(YawRateSignEpsilon),
                "the yaw rate flipped sign after the commit");
            Assert.That(m.SettleTime, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(3.5f),
                "the U-turn did not complete (|e| < 20 deg) within 3.5 s");
            Assert.That(Deg(m.MaxBeta), Is.LessThanOrEqualTo(Deg(p.BetaMaxRadians) + 3f), "sideslip past betaMax + 3 deg");
        }

        // ------------------------------------------------------------- 3. the owner's down-left case

        [Test]
        public void Acceptance3_OwnerDownLeftStick_HoldsOneDirectionAndCompletes()
        {
            HandlingParams p = HandlingParams.Balanced;
            HeldStickMetrics m = RunHeldStick(p, 20f, 225f, 0f, 4f, 1);

            Console.WriteLine(
                "metric test3 down-left: settle(|e|<20deg)=" + m.SettleTime.ToString("F2") + "s"
                + " worstOppositeYaw=" + Deg(m.WorstOppositeYawRate).ToString("F2") + "deg/s"
                + " max|beta|=" + Deg(m.MaxBeta).ToString("F1") + "deg (limit " + (Deg(p.BetaMaxRadians) + 3f).ToString("F1") + ")"
                + " speed=" + m.MinSpeed.ToString("F1") + ".." + m.MaxSpeed.ToString("F1") + " m/s");

            Assert.That(m.WorstOppositeYawRate, Is.LessThanOrEqualTo(YawRateSignEpsilon),
                "the yaw rate flipped sign while the stick was held down-left");
            Assert.That(m.SettleTime, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(3.5f),
                "the turn did not complete (|e| < 20 deg) within 3.5 s");
            Assert.That(Deg(m.MaxBeta), Is.LessThanOrEqualTo(Deg(p.BetaMaxRadians) + 3f), "sideslip past betaMax + 3 deg");
        }

        // ------------------------------------------------------------------------ 4. the 90 degree step

        [Test]
        public void Acceptance4_SteeringStep_10ms_SettlesWithin1200ms()
        {
            StepMetrics m = RunStep(HandlingParams.Balanced, 10f);
            Console.WriteLine("metric test4 step 10 m/s: settle=" + m.SettleTime.ToString("F3") + "s overshoot="
                + m.OvershootDegrees.ToString("F2") + "deg speed=" + m.MinSpeed.ToString("F1") + ".." + m.MaxSpeed.ToString("F1")
                + " max|w|/yawMax=" + m.MaxYawRatio.ToString("F3"));

            Assert.That(m.SettleTime, Is.LessThanOrEqualTo(1.2f), "a 90 deg step at 10 m/s did not settle within 1.2 s");
            Assert.That(m.OvershootDegrees, Is.LessThan(10f), "overshoot past the requested heading");
        }

        /// <summary>
        /// 20 and 25 m/s are held to the bound the spec's own steering block implies, not to 1.2 s:
        /// at 20 m/s yawMax is 16/20 = 0.8 rad/s, so 85 degrees of heading (90 minus the 5 degree
        /// tolerance) needs 1.85 s even at a constant yawMax with zero lag, and 25 m/s needs 2.22 s.
        /// The 1.2 s of test 4 can only hold at 10 m/s, where yawMax is the 150 deg/s ceiling. Making
        /// 1.2 s true at the top speed would need about 39 m/s^2 of lateral acceleration, four times
        /// gravity and more than double the top of the spec's own aLatBudget range. This is an
        /// arithmetic inconsistency between section 4 and test 4 in the spec, reported rather than
        /// papered over.
        ///
        /// The upper case was 30 m/s while the Balanced top speed was ~33; the 2026-10-13 playtest
        /// retune (owner: "acelera una banda") dropped the free top speed to ~28, so 30 would have
        /// measured a speed the car can no longer reach. 25 m/s keeps the case inside the new envelope.
        /// </summary>
        [TestCase(20f)]
        [TestCase(25f)]
        public void Acceptance4_SteeringStep_HighSpeed_MeetsTheSpecsOwnYawMaxBound(float speed)
        {
            StepMetrics m = RunStep(HandlingParams.Balanced, speed);
            float yawMax = Min(HandlingParams.Balanced.MaxYawRateRadiansPerSecond,
                HandlingParams.Balanced.LateralAccelBudgetMetersPerSecondSquared / Math.Max(speed, 3f));
            float physicalMinimum = Rad(85f) / yawMax;

            Console.WriteLine("metric test4 step " + speed.ToString("F0") + " m/s: settle=" + m.SettleTime.ToString("F2")
                + "s (spec asks 1.20, yawMax bound " + physicalMinimum.ToString("F2") + "s) overshoot="
                + m.OvershootDegrees.ToString("F2") + "deg max|w|/yawMax=" + m.MaxYawRatio.ToString("F3"));

            Assert.That(m.MaxYawRatio, Is.LessThanOrEqualTo(1.2f),
                "the yaw rate exceeded 1.2 * yawMax while following a 90 deg step");
            Assert.That(m.SettleTime, Is.GreaterThanOrEqualTo(physicalMinimum - 0.05f),
                "settled faster than the spec's own yawMax allows, which would mean the cap leaked");
            Assert.That(m.SettleTime, Is.LessThanOrEqualTo(physicalMinimum + 0.7f),
                "settled slower than the yawMax bound plus control lag");
            Assert.That(m.OvershootDegrees, Is.LessThan(10f), "overshoot past the requested heading");
        }

        // --------------------------------------------------------------------------------- 5. launch

        [Test]
        public void Acceptance5_Launch_ZeroToTwentyInTheSpecWindow()
        {
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Reset(0f, 0f);
            sim.StickX = 0f;
            sim.StickY = 1f;

            float launchTime = -1f;
            for (int i = 0; i < 500; i++)
            {
                sim.Tick();
                if (launchTime < 0f && sim.ForwardSpeed >= 20f)
                {
                    launchTime = sim.Time;
                }
            }

            Console.WriteLine("metric test5 launch: t(0->20)=" + launchTime.ToString("F2")
                + "s terminal=" + sim.ForwardSpeed.ToString("F1") + " m/s at "
                + (PlanarCarSim.DtSeconds * 500f).ToString("F0") + "s");

            // The window has moved twice with the owner's pace calls: 2.3..3.0 s at accelMax 11, then
            // 3.0..3.7 s after the 2026-10-13 "acelera una banda" retune that lowered accelMax to 9.
            // The 2026-10-15 playtest asked for the opposite ("mientras mas rapido pase mas dopamina
            // mejor"), so FEEL2 took accelMax to 19 and the window follows the car: 0->20 m/s is ~1.05 s
            // by design. The bound is the FEEL2 spec value, not a re-measured number, so the spec stays
            // the contract. See DRIVING_MODEL_V2.md, "FEEL2 pace".
            Assert.That(launchTime, Is.GreaterThanOrEqualTo(0.9f).And.LessThanOrEqualTo(1.3f),
                "0->20 m/s outside the FEEL2 0.9..1.3 s window on Balanced");
        }

        // ---------------------------------------------------------------------------- 6. determinism

        [Test]
        public void Acceptance6_SameInputsGiveBitIdenticalStateAfter600Ticks()
        {
            HandlingParams p = HandlingParams.Balanced;
            var a = new PlanarCarSim(p);
            var b = new PlanarCarSim(p);
            a.Reset(0f, 20f);
            b.Reset(0f, 20f);

            var noiseA = new Lcg(7);
            var noiseB = new Lcg(7);

            for (int i = 0; i < 600; i++)
            {
                float phase = HandlingMath.TwoPi * a.Time / 4f;
                float jitter = noiseA.NextRange(-3f, 3f);
                float jitterB = noiseB.NextRange(-3f, 3f);
                a.StickX = (float)Math.Sin(phase);
                a.StickY = (float)Math.Cos(phase) * (1f + jitter * Rad(1f));
                b.StickX = (float)Math.Sin(phase);
                b.StickY = (float)Math.Cos(phase) * (1f + jitterB * Rad(1f));

                // Same inputs, two independent bodies: any divergence is a hidden state leak.
                if (jitter != jitterB)
                {
                    Assert.Fail("the scripted input generator was not reproducible");
                }
                if (i == 250)
                {
                    a.Brake = true;
                    b.Brake = true;
                }

                if (i == 260)
                {
                    a.Brake = false;
                    b.Brake = false;
                }

                a.Tick();
                b.Tick();
            }

            Assert.That(BitConverter.SingleToInt32Bits(a.ForwardSpeed), Is.EqualTo(BitConverter.SingleToInt32Bits(b.ForwardSpeed)), "forward speed diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.LateralSpeed), Is.EqualTo(BitConverter.SingleToInt32Bits(b.LateralSpeed)), "lateral speed diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.YawRate), Is.EqualTo(BitConverter.SingleToInt32Bits(b.YawRate)), "yaw rate diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.YawRadians), Is.EqualTo(BitConverter.SingleToInt32Bits(b.YawRadians)), "yaw diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.PositionX), Is.EqualTo(BitConverter.SingleToInt32Bits(b.PositionX)), "x diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.PositionY), Is.EqualTo(BitConverter.SingleToInt32Bits(b.PositionY)), "y diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.State.SteerAngleRadians), Is.EqualTo(BitConverter.SingleToInt32Bits(b.State.SteerAngleRadians)), "steer angle diverged");
            Assert.That(BitConverter.SingleToInt32Bits(a.State.SmoothedThrottle), Is.EqualTo(BitConverter.SingleToInt32Bits(b.State.SmoothedThrottle)), "smoothed throttle diverged");
            Assert.That(a.State.Intent.TurnSign, Is.EqualTo(b.State.Intent.TurnSign), "turn sign diverged");

            Console.WriteLine("metric test6 determinism: 600 ticks, identical bits; yaw=" + a.YawRadians.ToString("F6")
                + " v=" + a.ForwardSpeed.ToString("F6") + " w=" + a.YawRate.ToString("F6"));
        }

        // -------------------------------------------------------------------------- 7. zero allocations

        [Test]
        public void Acceptance7_NoAllocationOnTheTickPath()
        {
#if NET5_0_OR_GREATER
            HandlingParams p = HandlingParams.Balanced;
            var sim = new PlanarCarSim(p);
            sim.Reset(0f, 20f);

            // Warm up so JIT and any first-call allocation happen outside the measured region.
            for (int i = 0; i < 400; i++)
            {
                sim.StickX = i % 2 == 0 ? 0.4f : -0.4f;
                sim.StickY = 0.9f;
                sim.Tick();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                sim.StickX = i % 2 == 0 ? 0.4f : -0.4f;
                sim.StickY = 0.9f;
                sim.Tick();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("metric test7 allocations: " + allocated + " bytes over 1000 ticks");
            Assert.That(allocated, Is.EqualTo(0L), "the handling tick allocated " + allocated + " bytes");
#endif
        }

        // --------------------------------------------------------------------------------- helpers

        private struct HeldStickMetrics
        {
            public bool Committed;
            public float CommitSign;
            public float InitialSign;
            public float SettleTime;
            public float WorstOppositeYawRate;
            public float MaxBeta;
            public float MinSpeed;
            public float MaxSpeed;
        }

        /// <summary>
        /// Holds the stick at one screen bearing for the whole run. Bearing is measured clockwise from
        /// screen-up, so 180 is dead astern and 225 is the owner's down-left.
        /// </summary>
        private static HeldStickMetrics RunHeldStick(
            HandlingParams p,
            float startSpeed,
            float bearingDegrees,
            float jitterDegrees,
            float seconds,
            int seed)
        {
            var sim = new PlanarCarSim(p);
            sim.Reset(0f, startSpeed);
            var noise = new Lcg(seed);

            var metrics = new HeldStickMetrics
            {
                SettleTime = -1f,
                MinSpeed = startSpeed,
                MaxSpeed = startSpeed,
                CommitSign = 0f,
            };

            int ticks = (int)(seconds / PlanarCarSim.DtSeconds);
            for (int i = 0; i < ticks; i++)
            {
                float jitter = jitterDegrees > 0f ? noise.NextRange(-jitterDegrees, jitterDegrees) : 0f;
                float bearing = Rad(bearingDegrees + jitter);
                sim.StickX = (float)Math.Sin(bearing);
                sim.StickY = (float)Math.Cos(bearing);
                sim.Tick();

                HandlingOutput o = sim.Output;
                if (o.TurnCommitted && !metrics.Committed)
                {
                    metrics.Committed = true;
                    metrics.CommitSign = o.TurnSign;
                }

                // The reference direction for the run is the committed one when there is a commit and
                // the initial request otherwise (the down-left case never reaches 150 deg).
                if (metrics.InitialSign == 0f)
                {
                    metrics.InitialSign = Math.Sign(o.HeadingErrorRadians);
                }

                // Only a rate above the epsilon counts as turning, and the criterion is about the
                // manoeuvre: once the heading is inside the settle tolerance the turn is over, so a
                // later damping counter-oscillation is not a sign flip of the turn.
                float absoluteError = Math.Abs(o.HeadingErrorRadians);
                float committedSign = metrics.Committed ? metrics.CommitSign : metrics.InitialSign;
                if (absoluteError > Rad(20f) && committedSign != 0f && Math.Abs(sim.YawRate) > YawRateSignEpsilon)
                {
                    float opposite = -committedSign * sim.YawRate;
                    if (opposite > metrics.WorstOppositeYawRate)
                    {
                        metrics.WorstOppositeYawRate = opposite;
                    }
                }

                if (metrics.SettleTime < 0f && absoluteError < Rad(20f))
                {
                    metrics.SettleTime = sim.Time;
                }

                if (Math.Abs(o.BetaRadians) > metrics.MaxBeta)
                {
                    metrics.MaxBeta = Math.Abs(o.BetaRadians);
                }

                if (sim.ForwardSpeed < metrics.MinSpeed)
                {
                    metrics.MinSpeed = sim.ForwardSpeed;
                }

                if (sim.ForwardSpeed > metrics.MaxSpeed)
                {
                    metrics.MaxSpeed = sim.ForwardSpeed;
                }
            }

            return metrics;
        }

        private struct StepMetrics
        {
            public float SettleTime;
            public float OvershootDegrees;
            public float MaxYawRatio;
            public float MinSpeed;
            public float MaxSpeed;
        }

        /// <summary>
        /// Holds the pedal that balances drag at the requested speed, then steps the stick 90 degrees
        /// to the left and measures the heading response. The pedal is held rather than driven by the
        /// stick so the step is measured at the speed the criterion names, and the achieved speed band
        /// is printed with the result.
        /// </summary>
        private static StepMetrics RunStep(HandlingParams p, float speed)
        {
            var sim = new PlanarCarSim(p);
            sim.Reset(0f, speed);
            sim.ThrottleOverride = HoldPedal(speed, p);
            sim.StickX = -1f;
            sim.StickY = 0f;

            var metrics = new StepMetrics
            {
                SettleTime = -1f,
                MinSpeed = speed,
                MaxSpeed = speed,
            };

            for (int i = 0; i < 400; i++)
            {
                sim.Tick();
                HandlingOutput o = sim.Output;

                float pastTarget = -Deg(o.HeadingErrorRadians);
                if (pastTarget > metrics.OvershootDegrees)
                {
                    metrics.OvershootDegrees = pastTarget;
                }

                if (o.YawRateMaxRadiansPerSecond > 0f)
                {
                    float ratio = Math.Abs(sim.YawRate) / o.YawRateMaxRadiansPerSecond;
                    if (ratio > metrics.MaxYawRatio)
                    {
                        metrics.MaxYawRatio = ratio;
                    }
                }

                if (metrics.SettleTime < 0f && Math.Abs(o.HeadingErrorRadians) <= Rad(5f))
                {
                    metrics.SettleTime = sim.Time;
                }

                if (sim.ForwardSpeed < metrics.MinSpeed)
                {
                    metrics.MinSpeed = sim.ForwardSpeed;
                }

                if (sim.ForwardSpeed > metrics.MaxSpeed)
                {
                    metrics.MaxSpeed = sim.ForwardSpeed;
                }
            }

            return metrics;
        }

        /// <summary>
        /// The pedal that balances drag at the requested speed — the sim's mirror of
        /// <see cref="LongitudinalModel"/> at equilibrium: accelMax * pedal * curve(v) equals
        /// engineBrake * (1 - Smoothstep(0, fade, pedal)) + coastDrag * v, with the smoothed pedal
        /// settled on the held value. The smoothstep makes the pedal term non-analytic, so the root
        /// is bisected (g is strictly increasing in pedal: drive grows, the faded engine brake
        /// shrinks). A speed the car cannot hold returns full pedal.
        /// </summary>
        private static float HoldPedal(float speed, in HandlingParams p)
        {
            float fraction = HandlingMath.Clamp01(speed / p.TopSpeedMetersPerSecond);
            float curve = p.ThrottleCurve.Evaluate(fraction);
            if (curve <= 0f)
            {
                return 0f;
            }

            float lo = 0f;
            float hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float fade = 1f - HandlingMath.Smoothstep(0f, p.EngineBrakeFadePedal, mid);
                float balance = (p.AccelMaxMetersPerSecondSquared * mid * curve)
                    - (p.EngineBrakeMetersPerSecondSquared * fade)
                    - (p.CoastDragPerSecond * speed);
                if (balance > 0f)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return (lo + hi) * 0.5f;
        }

        private static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        /// <summary>A tiny deterministic generator: the jitter has to be the same on every run.</summary>
        private struct Lcg
        {
            private uint _state;

            public Lcg(int seed)
            {
                _state = (uint)seed * 2654435761u + 1u;
            }

            public uint Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return _state;
            }

            public float NextRange(float min, float max)
            {
                float unit = (Next() >> 8) * (1f / 16777216f);
                return min + (max - min) * unit;
            }
        }
    }
}
