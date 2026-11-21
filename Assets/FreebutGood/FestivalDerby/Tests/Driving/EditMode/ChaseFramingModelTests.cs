using System;
using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The chase framing is arithmetic, so its contract belongs in a measurement, not in an opinion
    /// about a frame: a camera that lets the car drift off centre is a defect with a number.
    /// The Unity-side fixture (<c>ChaseFramingContractEditModeTests</c>) pins the shipped look asset
    /// to the geometry constants projected here, because the harness cannot open a Unity asset.
    /// </summary>
    [TestFixture]
    public sealed class ChaseFramingModelTests
    {
        private const float FrameRate30 = 1f / 30f;
        private const float FrameRate60 = 1f / 60f;
        private const float FrameRate120 = 1f / 120f;
        private const float TopSpeed = 30f;
        private const float CarAcceleration = 10f;

        // Shipped framing geometry: LookDefaults plus the look profile asset.
        private const float FramingOffsetX = -16.5f;
        private const float FramingOffsetY = 31f;
        private const float FramingOffsetZ = -14.5f;
        private const float FramingLookHeight = 2f;
        private const float FramingBaseFovDegrees = 30f;
        private const float FramingWidenDegrees = 2f;
        private const float FramingWidenAtSpeed = 26f;
        private const float ScreenAspect = 16f / 9f;

        // What "centred" means on a phone screen: the car may not wander more than this fraction of
        // the screen's own height or width between rest and top speed.
        private const float ScreenOffsetBar = 0.08f;

        // The CS1 framing the owner rejected. Kept only to print the before/after table, never asserted.
        private const float LegacyOffsetX = -20f;
        private const float LegacyOffsetY = 37.5f;
        private const float LegacyOffsetZ = -17.5f;
        private const float LegacyWidenDegrees = 3f;
        private const float LegacyLeadMetresPerSpeed = 0.5f;
        private const float LegacyLeadMaxMetres = 14f;
        private const float LegacyFollowSharpness = 6f;

        [Test]
        public void Step_StraightAtTopSpeed_HoldsTheCarWithNoSteadyStateLag()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            const float topSpeed = 28f;
            const int frames = 300;
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f), in settings);

            float x = 0f;
            float speed = 0f;
            float peakLag = 0f;
            float steadyLag = 0f;
            for (int frame = 0; frame < frames; frame++)
            {
                // A 1 g car: 28 m/s in 2.8 s, then held. The last two seconds are the steady state.
                speed = Math.Min(topSpeed, speed + (CarAcceleration * FrameRate60));
                x += speed * FrameRate60;
                model.Step(FrameRate60, new ChaseFramingTarget(x, 0f, 0f, speed, 0f), in settings);

                float lag = Math.Abs(model.PositionX - x);
                if (lag > peakLag)
                    peakLag = lag;
                if (frame >= frames - 120 && lag > steadyLag)
                    steadyLag = lag;
            }

            Console.WriteLine("metric straight 28 m/s: peakLag=" + peakLag.ToString("0.000") +
                "m steadyLag=" + steadyLag.ToString("0.000") + "m");
            Assert.That(speed, Is.EqualTo(topSpeed).Within(1e-3f), "setup proof: the run reaches 28 m/s");
            Assert.That(steadyLag, Is.LessThanOrEqualTo(0.2f),
                "steady-state lag straight at 28 m/s, metres");
            Assert.That(peakLag, Is.LessThanOrEqualTo(0.5f),
                "worst lag during the 1 g ramp, metres");
        }

        [Test]
        public void Step_ThroughATightCorner_KeepsTheFramingOnTheCarInBothAxes()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            const float radius = 40f;
            const float speed = 20f;
            const int frames = 300;
            float turnRate = speed / radius;

            float angle = 0f;
            float px = radius;
            float pz = 0f;
            model.Step(FrameRate60, new ChaseFramingTarget(px, 0f, pz, 0f, speed), in settings);

            float peakError = 0f;
            float steadyError = 0f;
            float steadyTangential = 0f;
            for (int frame = 0; frame < frames; frame++)
            {
                angle += turnRate * FrameRate60;
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                px = radius * cos;
                pz = radius * sin;
                model.Step(FrameRate60,
                    new ChaseFramingTarget(px, 0f, pz, -speed * sin, speed * cos), in settings);

                float errorX = model.PositionX - px;
                float errorZ = model.PositionZ - pz;
                // Outward radial unit vector (cos, sin) and tangential unit vector (-sin, cos).
                float radial = (errorX * cos) + (errorZ * sin);
                float tangential = (errorZ * cos) - (errorX * sin);
                float error = (float)Math.Sqrt((errorX * errorX) + (errorZ * errorZ));
                if (error > peakError)
                    peakError = error;
                if (frame >= frames - 180)
                {
                    float cornerError = (float)Math.Sqrt((radial * radial) + (tangential * tangential));
                    if (cornerError > steadyError)
                        steadyError = cornerError;
                    if (Math.Abs(tangential) > steadyTangential)
                        steadyTangential = Math.Abs(tangential);
                }
            }

            Console.WriteLine("metric corner r=40 @20 m/s: peakError=" + peakError.ToString("0.000") +
                "m steadyError=" + steadyError.ToString("0.000") +
                "m steadyTangential=" + steadyTangential.ToString("0.000") + "m");
            Assert.That(steadyError, Is.LessThanOrEqualTo(0.5f),
                "framing error through the steady corner, metres");
            Assert.That(steadyTangential, Is.LessThanOrEqualTo(0.5f),
                "tangential part of that error (a follower lag shows up here), metres");
        }

        [Test]
        public void Step_AfterASixtyMetreTeleport_SnapsOnTheSameFrame()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            const float speed = TopSpeed;
            float x = 0f;
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, speed, 0f), in settings);
            for (int frame = 0; frame < 60; frame++)
            {
                x += speed * FrameRate60;
                model.Step(FrameRate60, new ChaseFramingTarget(x, 0f, 0f, speed, 0f), in settings);
            }

            Assert.That(x, Is.EqualTo(30f).Within(1e-3f), "setup proof: the run covered 30 m before the teleport");

            x += 60f;
            model.Step(FrameRate60, new ChaseFramingTarget(x, 5f, 12f, speed, 0f), in settings);
            Assert.That(Math.Abs(model.PositionX - x), Is.LessThanOrEqualTo(1e-3f),
                "a 60 m teleport must be framed on the frame it happens");
            Assert.That(Math.Abs(model.PositionZ - 12f), Is.LessThanOrEqualTo(1e-3f),
                "so must the lateral part of it");
            Assert.That(model.LeadX, Is.EqualTo(0f).Within(1e-4f),
                "a teleport must not inherit the previous lead");
        }

        [Test]
        public void Step_AfterAOneMetreVerticalStep_EasesWithoutOvershooting()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f), in settings);

            float firstStep = 0f;
            float overshoot = 0f;
            for (int frame = 0; frame < 300; frame++)
            {
                model.Step(FrameRate60, new ChaseFramingTarget(0f, 1f, 0f, 0f, 0f), in settings);
                if (frame == 0)
                    firstStep = model.PositionY;
                if (model.PositionY > 1f)
                    overshoot = Math.Max(overshoot, model.PositionY - 1f);
            }

            Console.WriteLine("metric vertical 1 m step: firstFrame=" + firstStep.ToString("0.000") +
                "m overshoot=" + overshoot.ToString("0.000") + "m settled=" + model.PositionY.ToString("0.000") + "m");
            Assert.That(firstStep, Is.LessThanOrEqualTo(0.25f),
                "the vertical step must be eased, not snapped, metres moved on frame one");
            Assert.That(overshoot, Is.LessThanOrEqualTo(0.1f),
                "overshoot of a 1 m vertical step must stay under 10 per cent, metres");
            Assert.That(model.PositionY, Is.EqualTo(1f).Within(0.02f),
                "and it must still reach the car, metres");
        }

        [Test]
        public void Step_VerticalSharpness_MovesOnlyTheVerticalTrace()
        {
            var soft = new ChaseFramingSettings(6f, 3f, 0f, 0f, 0f, 0f, 0f);
            var stiff = new ChaseFramingSettings(6f, 30f, 0f, 0f, 0f, 0f, 0f);
            var softModel = new ChaseFramingModel();
            var stiffModel = new ChaseFramingModel();

            float worstVerticalGap = 0f;
            float worstPlanarGap = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                // A climb that both bends and bobs, so neither trace can agree by standing still.
                float y = frame == 0 ? 1f : 0.5f + (float)Math.Sin(frame * 0.2f);
                float x = frame * 0.4f;
                var target = new ChaseFramingTarget(x, y, 0f, 0f, 0f);
                softModel.Step(FrameRate60, in target, in soft);
                stiffModel.Step(FrameRate60, in target, in stiff);

                worstVerticalGap = Math.Max(worstVerticalGap, Math.Abs(softModel.PositionY - stiffModel.PositionY));
                worstPlanarGap = Math.Max(worstPlanarGap, Math.Abs(softModel.PositionX - stiffModel.PositionX));
            }

            Console.WriteLine("metric vertical rate split: verticalGap=" + worstVerticalGap.ToString("0.000") +
                "m planarGap=" + worstPlanarGap.ToString("0.000000") + "m");
            Assert.That(worstVerticalGap, Is.GreaterThanOrEqualTo(0.05f),
                "the vertical rate must actually drive the vertical trace, metres");
            Assert.That(worstPlanarGap, Is.LessThanOrEqualTo(1e-5f),
                "and it must not touch the planar trace, metres");
        }

        [Test]
        public void Step_AtThirtySixtyAndOneHundredTwentyFramesPerSecond_AgreesWithinFiveCentimetres()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            float x30 = RunSpeedStep(FrameRate30, in settings);
            float x60 = RunSpeedStep(FrameRate60, in settings);
            float x120 = RunSpeedStep(FrameRate120, in settings);

            float worst = Math.Max(Math.Abs(x30 - x60), Math.Abs(x60 - x120));
            Console.WriteLine("metric frame rate 30/60/120 fps: x30=" + x30.ToString("0.000") +
                " x60=" + x60.ToString("0.000") + " x120=" + x120.ToString("0.000") +
                " worstGap=" + worst.ToString("0.000") + "m");
            Assert.That(x30, Is.GreaterThan(100f), "setup proof: the run covers real distance");
            Assert.That(worst, Is.LessThanOrEqualTo(0.05f),
                "the settled framing must not depend on the frame rate, metres");
        }

        [Test]
        public void Step_InSteadyState_AllocatesNothing()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            float x = 0f;
            var target = new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f);
            model.Step(FrameRate60, in target, in settings);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 10000; frame++)
            {
                x += TopSpeed * FrameRate60;
                target = new ChaseFramingTarget(x, 1f, 0f, TopSpeed, 0f);
                model.Step(FrameRate60, in target, in settings);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L),
                "bytes allocated by 10 000 framing steps");
        }

        [Test]
        public void Step_WithAnImpossibleVelocity_DoesNotLetTheFramingRunAway()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f), in settings);

            // A position-delta velocity source reports 5 000 m/s when one frame spans a teleport of
            // the measured object. The framing may not chase that number.
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 5000f), in settings);
            Assert.That(Math.Abs(model.PositionZ), Is.LessThanOrEqualTo(2.5f),
                "metres moved by one frame of a bogus velocity feed-forward");
        }

        [Test]
        public void Step_BeforeItsFirstFrame_SnapsToTheCarWithNoLead()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            model.Step(FrameRate60, new ChaseFramingTarget(5f, 0f, 7f, 0f, 12f), in settings);

            Assert.That(model.HasFrame, Is.True, "the first frame must frame the car");
            Assert.That(model.PositionX, Is.EqualTo(5f).Within(1e-4f), "X on the first frame");
            Assert.That(model.PositionZ, Is.EqualTo(7f).Within(1e-4f), "Z on the first frame");
            Assert.That(model.LeadX, Is.EqualTo(0f).Within(1e-6f), "no lead frame one, X");
            Assert.That(model.LeadZ, Is.EqualTo(0f).Within(1e-6f), "no lead frame one, Z");
        }

        [Test]
        public void Step_WithANonPositiveOrNonFiniteDelta_LeavesTheFramingAlone()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            var model = new ChaseFramingModel();
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 20f), in settings);

            float x = model.PositionX;
            float z = model.PositionZ;
            float leadZ = model.LeadZ;
            var moved = new ChaseFramingTarget(50f, 9f, 50f, 0f, 20f);
            model.Step(0f, in moved, in settings);
            model.Step(-1f, in moved, in settings);
            model.Step(float.NaN, in moved, in settings);

            Assert.That(model.PositionX, Is.EqualTo(x).Within(1e-6f), "X after a paused and a NaN frame");
            Assert.That(model.PositionZ, Is.EqualTo(z).Within(1e-6f), "Z after a paused and a NaN frame");
            Assert.That(model.LeadZ, Is.EqualTo(leadZ).Within(1e-6f), "lead after a paused and a NaN frame");
        }

        [Test]
        public void Step_WithTheSnapTurnedOff_EasesThroughATeleportInstead()
        {
            // The snap distance is the only reason a teleport is instant; a camera configured without
            // one must keep the old easing rather than acquire a snap from a default.
            var settings = new ChaseFramingSettings(6f, 3f, 0f, 0f, 0f, 0f, 60f);
            var model = new ChaseFramingModel();
            model.Step(FrameRate60, new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f), in settings);
            model.Step(FrameRate60, new ChaseFramingTarget(60f, 0f, 0f, 0f, 0f), in settings);

            Assert.That(Math.Abs(model.PositionX - 60f), Is.GreaterThan(1f),
                "metres the framing is still short with the snap off");
        }

        [Test]
        public void ChaseFraming_AtTopSpeed_HoldsTheCarWithinEightPercentOfTheCentre()
        {
            ChaseFramingSettings settings = ChaseFramingSettings.Default;
            Assert.That(settings.LookAheadMetresPerSpeed, Is.GreaterThan(0f),
                "setup proof: the lead must be on, or this test measures nothing");

            float restX = ScreenOffsetX(FramingOffsetX, FramingOffsetY, FramingOffsetZ,
                FramingLookHeight, FramingBaseFovDegrees, 0f);
            float restY = ScreenOffsetY(FramingOffsetX, FramingOffsetY, FramingOffsetZ,
                FramingLookHeight, FramingBaseFovDegrees, 0f);
            float worstX = 0f;
            float worstY = 0f;
            float atTopSpeedX = 0f;
            float atTopSpeedY = 0f;

            for (int speed = 0; speed <= 30; speed++)
            {
                float lead = LeadMetres(settings, speed);
                float fov = FovDegrees(FramingBaseFovDegrees, FramingWidenDegrees, FramingWidenAtSpeed, speed);
                float x = ScreenOffsetX(FramingOffsetX, FramingOffsetY, FramingOffsetZ, FramingLookHeight, fov, lead);
                float y = ScreenOffsetY(FramingOffsetX, FramingOffsetY, FramingOffsetZ, FramingLookHeight, fov, lead);
                worstX = Math.Max(worstX, Math.Abs(x - restX));
                worstY = Math.Max(worstY, Math.Abs(y - restY));
                if (speed == 30)
                {
                    atTopSpeedX = x;
                    atTopSpeedY = y;
                }
            }

            Console.WriteLine("metric screen wander 0..30 m/s: worstX=" + worstX.ToString("0.0000") +
                " worstY=" + worstY.ToString("0.0000") +
                " offsetAtRest=(" + restX.ToString("0.0000") + "," + restY.ToString("0.0000") + ")" +
                " offsetAtTopSpeed=(" + atTopSpeedX.ToString("0.0000") + "," + atTopSpeedY.ToString("0.0000") + ")");
            Assert.That(worstY, Is.LessThanOrEqualTo(ScreenOffsetBar),
                "fraction of the screen height the car may wander off centre, vertical");
            Assert.That(worstX, Is.LessThanOrEqualTo(ScreenOffsetBar),
                "fraction of the screen width the car may wander off centre, horizontal");
        }

        [Test]
        public void ChaseFraming_RetunedGeometry_MovesTheCarCloserToTheCentreThanCS1()
        {
            // Evidence, not taste: this prints the table the design note quotes and guards it against
            // drifting. A row is (speed, lead, screen offset from centre).
            float legacyWorst = 0f;
            float shippedWorst = 0f;
            int[] speeds = { 0, 10, 20, 30 };
            for (int i = 0; i < speeds.Length; i++)
            {
                int speed = speeds[i];

                float legacyLead = Math.Min(LegacyLeadMetresPerSpeed * speed, LegacyLeadMaxMetres);
                float legacyNet = legacyLead - (speed / LegacyFollowSharpness);
                float legacyFov = FovDegrees(FramingBaseFovDegrees, LegacyWidenDegrees,
                    FramingWidenAtSpeed, speed);
                float beforeX = ScreenOffsetX(LegacyOffsetX, LegacyOffsetY, LegacyOffsetZ,
                    FramingLookHeight, legacyFov, legacyNet);
                float beforeY = ScreenOffsetY(LegacyOffsetX, LegacyOffsetY, LegacyOffsetZ,
                    FramingLookHeight, legacyFov, legacyNet);

                ChaseFramingSettings settings = ChaseFramingSettings.Default;
                float shippedLead = LeadMetres(settings, speed);
                float shippedFov = FovDegrees(FramingBaseFovDegrees, FramingWidenDegrees,
                    FramingWidenAtSpeed, speed);
                float afterX = ScreenOffsetX(FramingOffsetX, FramingOffsetY, FramingOffsetZ,
                    FramingLookHeight, shippedFov, shippedLead);
                float afterY = ScreenOffsetY(FramingOffsetX, FramingOffsetY, FramingOffsetZ,
                    FramingLookHeight, shippedFov, shippedLead);

                Console.WriteLine("table v=" + speed + " m/s: before lead=" + legacyLead.ToString("0.0") +
                    "m net=" + legacyNet.ToString("0.0") + "m offset=(" + beforeX.ToString("0.000") +
                    "," + beforeY.ToString("0.000") + ") after lead=" + shippedLead.ToString("0.00") +
                    "m offset=(" + afterX.ToString("0.000") + "," + afterY.ToString("0.000") + ")");

                if (speed > 0)
                {
                    legacyWorst = Math.Max(legacyWorst, Math.Abs(beforeY));
                    shippedWorst = Math.Max(shippedWorst, Math.Abs(afterY));
                }
            }

            Assert.That(shippedWorst, Is.LessThan(legacyWorst),
                "the shipped framing must sit the car closer to the centre than CS1 did, fraction of screen height");
        }

        private static float RunSpeedStep(float frameRate, in ChaseFramingSettings settings)
        {
            var model = new ChaseFramingModel();
            float elapsed = 0f;
            float x = 0f;
            float speed = 0f;
            model.Step(frameRate, new ChaseFramingTarget(0f, 0f, 0f, 0f, 0f), in settings);
            // The same five seconds at every rate, counted in frames: stopping on an accumulated total
            // gives the slowest rate one extra step and compares two different instants.
            int frames = (int)Math.Round(5.0 / frameRate);
            for (int frame = 0; frame < frames; frame++)
            {
                elapsed += frameRate;
                // Speed profile in time, position integrated from the same profile, so every frame
                // rate is asked about the same trajectory.
                speed = Math.Min(TopSpeed, 20f * elapsed);
                x = elapsed < 1.5f ? 10f * elapsed * elapsed : 22.5f + (TopSpeed * (elapsed - 1.5f));
                model.Step(frameRate, new ChaseFramingTarget(x, 0f, 0f, speed, 0f), in settings);
            }

            return model.PositionX;
        }

        private static float LeadMetres(in ChaseFramingSettings settings, float speed)
        {
            return Math.Min(settings.LookAheadMetresPerSpeed * speed, settings.LookAheadMaxMetres);
        }

        private static float FovDegrees(float baseFov, float widen, float widenAtSpeed, float speed)
        {
            if (widen <= 0f || widenAtSpeed <= 0f)
                return baseFov;
            float fraction = speed / widenAtSpeed;
            if (fraction > 1f)
                fraction = 1f;
            return baseFov + (widen * fraction);
        }

        /// <summary>
        /// The car's screen offset on the X axis, as a fraction of the screen's own width, when the
        /// camera sits <paramref name="lead"/> metres ahead of it along the heading. Plain maths: the
        /// framing is a fixed world bearing, so the heading only moves where the car lands.
        /// </summary>
        private static float ScreenOffsetX(float offsetX, float offsetY, float offsetZ, float lookHeight,
            float fovDegrees, float lead)
        {
            float x;
            float y;
            Project(offsetX, offsetY, offsetZ, lookHeight, fovDegrees, lead, out x, out y);
            return x;
        }

        private static float ScreenOffsetY(float offsetX, float offsetY, float offsetZ, float lookHeight,
            float fovDegrees, float lead)
        {
            float x;
            float y;
            Project(offsetX, offsetY, offsetZ, lookHeight, fovDegrees, lead, out x, out y);
            return y;
        }

        private static void Project(float offsetX, float offsetY, float offsetZ, float lookHeight,
            float fovDegrees, float lead, out float offsetScreenX, out float offsetScreenY)
        {
            var offset = new Vec3(offsetX, offsetY, offsetZ);
            Vec3 forward = (new Vec3(0f, lookHeight, 0f) - offset).Normalized();
            Vec3 right = Vec3.Cross(new Vec3(0f, 1f, 0f), forward).Normalized();
            Vec3 up = Vec3.Cross(forward, right);

            // Worst heading for the lead: the car is displaced along its own heading, and every
            // heading is equally possible on a circuit, so the sweep takes the extreme.
            float worstX = 0f;
            float worstY = 0f;
            for (int heading = 0; heading < 360; heading += 5)
            {
                float radians = heading * ((float)Math.PI / 180f);
                var displacement = new Vec3((float)Math.Cos(radians) * lead, 0f, (float)Math.Sin(radians) * lead);
                Vec3 toCar = (new Vec3(0f, 0f, 0f) - offset) - displacement;
                float depth = Vec3.Dot(toCar, forward);
                if (depth <= 0.001f)
                    continue;

                float tanHalf = (float)Math.Tan(fovDegrees * 0.5f * ((float)Math.PI / 180f));
                float ndcX = Vec3.Dot(toCar, right) / depth / (ScreenAspect * tanHalf);
                float ndcY = Vec3.Dot(toCar, up) / depth / tanHalf;
                float screenX = ndcX * 0.5f;
                float screenY = ndcY * 0.5f;
                if (Math.Abs(screenX) > Math.Abs(worstX))
                    worstX = screenX;
                if (Math.Abs(screenY) > Math.Abs(worstY))
                    worstY = screenY;
            }

            offsetScreenX = worstX;
            offsetScreenY = worstY;
        }

        /// <summary>Three floats and the four operations the projection needs. No engine, no allocation.</summary>
        private readonly struct Vec3
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public Vec3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static Vec3 operator -(Vec3 a, Vec3 b)
            {
                return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            }

            public static float Dot(Vec3 a, Vec3 b)
            {
                return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
            }

            public static Vec3 Cross(Vec3 a, Vec3 b)
            {
                return new Vec3(
                    (a.Y * b.Z) - (a.Z * b.Y),
                    (a.Z * b.X) - (a.X * b.Z),
                    (a.X * b.Y) - (a.Y * b.X));
            }

            public Vec3 Normalized()
            {
                float magnitude = (float)Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
                return new Vec3(X / magnitude, Y / magnitude, Z / magnitude);
            }
        }
    }
}
