using System;
using FestivalDerby.World;
using NUnit.Framework;

namespace FestivalDerby.Match.Tests
{
    [TestFixture]
    public sealed class TruckBodyRulesTests
    {
        private static readonly TruckSettings Settings = TruckSettings.Default;

        // The truck at the origin facing +Z, riding at its own speed.
        private static TruckPose Pose()
        {
            var forward = new PathVector3(0f, 0f, 1f);
            return new TruckPose(PathVector3.Zero, forward, forward * Settings.Speed, 0f);
        }

        private static PathVector3 OnBed(float lateral, float along)
        {
            return new PathVector3(lateral, Settings.BedHeight, Settings.BedCenterOffset + along);
        }

        private static WheelProbe Settled(PathVector3 velocity)
        {
            return new WheelProbe(OnBed(-0.8f, -1.2f), OnBed(0.8f, -1.2f), OnBed(-0.8f, 1.2f), OnBed(0.8f, 1.2f),
                0xF, velocity);
        }

        // ---- bed

        [Test]
        public void FourGroundedWheelsOnTheBed_RidingWithTheTruck_AreSettled()
        {
            Assert.That(TruckBed.IsSettled(Pose(), Settings, Settled(new PathVector3(0f, 0f, Settings.Speed))), Is.True);
        }

        [Test]
        public void OneWheelStillOnTheRamp_IsNotSettled()
        {
            TruckPose pose = Pose();
            float rampZ = Settings.BedCenterOffset - Settings.BedLength * 0.5f - 1f;
            var onRamp = new PathVector3(0.8f, 0.2f, rampZ);
            var probe = new WheelProbe(OnBed(-0.8f, -1.2f), onRamp, OnBed(-0.8f, 1.2f), OnBed(0.8f, 1.2f), 0xF,
                new PathVector3(0f, 0f, Settings.Speed));

            Assert.That(TruckBed.IsSettled(pose, Settings, probe), Is.False);
        }

        [TestCase((byte)0x7)]
        [TestCase((byte)0xB)]
        [TestCase((byte)0x0)]
        public void AWheelInTheAir_IsNotSettled(byte mask)
        {
            WheelProbe flying = Settled(new PathVector3(0f, 0f, Settings.Speed));
            var probe = new WheelProbe(flying.Wheel0, flying.Wheel1, flying.Wheel2, flying.Wheel3, mask, flying.Velocity);

            Assert.That(TruckBed.IsSettled(Pose(), Settings, probe), Is.False);
        }

        [Test]
        public void ARelativeSpeedOverTheLimit_IsNotSettled()
        {
            var tooFast = new PathVector3(0f, 0f, Settings.Speed + Settings.MaxRelativeSpeed + 1f);

            Assert.That(TruckBed.IsSettled(Pose(), Settings, Settled(tooFast)), Is.False);
        }

        [Test]
        public void ANaNWheelOrSpeed_IsNeverSettled()
        {
            WheelProbe ok = Settled(new PathVector3(0f, 0f, Settings.Speed));
            var nanWheel = new WheelProbe(new PathVector3(float.NaN, 0f, 0f), ok.Wheel1, ok.Wheel2, ok.Wheel3, 0xF,
                ok.Velocity);
            var nanSpeed = Settled(new PathVector3(float.NaN, 0f, 0f));

            Assert.That(TruckBed.IsSettled(Pose(), Settings, nanWheel), Is.False);
            Assert.That(TruckBed.IsSettled(Pose(), Settings, nanSpeed), Is.False);
        }

        [Test]
        public void TheBedFollowsTheTruckHeading()
        {
            // Truck turned to face +X: a wheel that was "behind" on Z is now behind on X.
            var forward = new PathVector3(1f, 0f, 0f);
            var turned = new TruckPose(new PathVector3(10f, 0f, 5f), forward, forward * Settings.Speed, 0f);
            var onBed = new PathVector3(10f + Settings.BedCenterOffset, Settings.BedHeight, 5f);

            Assert.That(TruckBed.IsWheelOnBed(turned, Settings, onBed), Is.True);
            Assert.That(TruckBed.IsWheelOnBed(Pose(), Settings, onBed), Is.False);
        }

        // ---- motion

        private static BakedPath Circle(int points, float radius)
        {
            var path = new BakedPath
            {
                Positions = new PathVector3[points],
                Tangents = new PathVector3[points],
                Ups = new PathVector3[points],
                WidthLeft = new float[points],
                WidthRight = new float[points],
                Banking = new float[points],
                Distances = new float[points],
                Closed = true,
            };

            for (int i = 0; i < points; i++)
            {
                double a = 2.0 * Math.PI * i / points;
                path.Positions[i] = new PathVector3((float)(radius * Math.Cos(a)), 0f, (float)(radius * Math.Sin(a)));
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = 5f;
                path.WidthRight[i] = 5f;
            }

            float total = 0f;
            for (int i = 0; i < points; i++)
            {
                PathVector3 step = path.Positions[(i + 1) % points] - path.Positions[i];
                path.Tangents[i] = step.Normalized;
                path.Distances[i] = total;
                total += step.Magnitude;
            }

            path.TotalLength = total;
            return path;
        }

        [Test]
        public void Distance_StartsAtTheConfiguredPointAndAdvancesAtSpeed()
        {
            Assert.That(TruckMotion.DistanceAt(Settings, 0.0, 1000f), Is.EqualTo(0f));
            Assert.That(TruckMotion.DistanceAt(Settings, 10.0, 1000f), Is.EqualTo(Settings.Speed * 10f).Within(0.001f));
        }

        [Test]
        public void Distance_WrapsTheLoopAndWaitsBeforeTheStart()
        {
            Assert.That(TruckMotion.DistanceAt(Settings, 100.0, 1000f),
                Is.EqualTo(Settings.Speed * 100f % 1000f).Within(0.01f));
            Assert.That(TruckMotion.DistanceAt(Settings, -5.0, 1000f), Is.EqualTo(0f));
        }

        [TestCase(0f)]
        [TestCase(-3f)]
        [TestCase(float.PositiveInfinity)]
        public void Distance_WithABadLoopLength_IsZero(float length)
        {
            Assert.That(TruckMotion.DistanceAt(Settings, 5.0, length), Is.EqualTo(0f));
        }

        [Test]
        public void Pose_IsHorizontalUnitHeadingMovingAtTruckSpeed()
        {
            BakedPath line = Circle(64, 100f);

            Assert.That(TruckMotion.TryPoseAt(line, Settings, 3.0, out TruckPose pose), Is.True);
            Assert.That(pose.Forward.y, Is.EqualTo(0f));
            Assert.That(pose.Forward.Magnitude, Is.EqualTo(1f).Within(0.001f));
            Assert.That(pose.Velocity.Magnitude, Is.EqualTo(Settings.Speed).Within(0.001f));
        }

        [Test]
        public void Pose_WithoutAUsableLine_Fails()
        {
            Assert.That(TruckMotion.TryPoseAt(null, Settings, 1.0, out _), Is.False);
            Assert.That(TruckMotion.TryPoseAt(new BakedPath(), Settings, 1.0, out _), Is.False);
        }

        [Test]
        public void RampEntry_IsBehindTheTruckByTheBedAndRamp()
        {
            PathVector3 entry = TruckMotion.RampEntry(Pose(), Settings);
            float expected = Settings.BedCenterOffset - Settings.BedLength * 0.5f - Settings.RampLength;

            Assert.That(entry.z, Is.EqualTo(expected).Within(0.001f));
            Assert.That(entry.x, Is.EqualTo(0f));
        }

        [Test]
        public void WithStartDistance_ChangesOnlyTheStart()
        {
            TruckSettings moved = Settings.WithStartDistance(120f);

            Assert.That(moved.StartDistance, Is.EqualTo(120f));
            Assert.That(moved.Speed, Is.EqualTo(Settings.Speed));
            Assert.That(moved.BedLength, Is.EqualTo(Settings.BedLength));
            Assert.That(moved.IsValid, Is.True);
            Assert.That(TruckMotion.DistanceAt(moved, 0.0, 1000f), Is.EqualTo(120f));
        }

        [Test]
        public void WithSpeed_ChangesOnlyTheSpeed()
        {
            TruckSettings faster = Settings.WithSpeed(15f);

            Assert.That(faster.Speed, Is.EqualTo(15f));
            Assert.That(faster.StartDistance, Is.EqualTo(Settings.StartDistance));
            Assert.That(faster.BedLength, Is.EqualTo(Settings.BedLength));
            Assert.That(faster.IsValid, Is.True);
        }

        [Test]
        public void DefaultSettings_AreValid_AndADefaultStructIsNot()
        {
            Assert.That(TruckSettings.Default.IsValid, Is.True);
            Assert.That(default(TruckSettings).IsValid, Is.False);
        }
    }
}
