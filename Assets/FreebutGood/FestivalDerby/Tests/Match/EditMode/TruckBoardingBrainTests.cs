using System;
using FestivalDerby.World;
using NUnit.Framework;

namespace FestivalDerby.Match.Tests
{
    [TestFixture]
    public sealed class TruckBoardingBrainTests
    {
        private static readonly TruckSettings Settings = TruckSettings.Default;
        private const float FullSteer = 30f;
        private const float Step = 0.02f;

        private static TruckPose Truck(float z = 0f)
        {
            var forward = new PathVector3(0f, 0f, 1f);
            return new TruckPose(new PathVector3(0f, 0f, z), forward, forward * Settings.Speed, 0f);
        }

        private static BoardingSelf Self(float x, float z, float speed)
        {
            return new BoardingSelf(new PathVector3(x, 0f, z), new PathVector3(0f, 0f, 1f), speed, 26f);
        }

        private static float TailDistance(float along)
        {
            return ChaseGeometry.DistanceToTail(5000f + along, 5000f, 10000f, Settings.BedHalfLength);
        }

        private static bool Decide(TruckBoardingBrain brain, BoardingSelf self, bool armed, float along,
            out BoardingCommand command)
        {
            return brain.TryDecide(self, Truck(), Settings, armed, TailDistance(along), FullSteer, Step, out command);
        }

        // ---- when it claims the bot

        [Test]
        public void ADisarmedBot_IsLeftToItsRacingLine()
        {
            var brain = new TruckBoardingBrain(1f);

            Assert.That(Decide(brain, Self(0f, -20f, 15f), false, -20f, out _), Is.False);
        }

        [Test]
        public void ABotFarBehindTheTruck_IsLeftToItsRacingLine()
        {
            var brain = new TruckBoardingBrain(1f);
            float far = -(TruckBoardingBrain.ApproachMeters + 30f);

            Assert.That(Decide(brain, Self(0f, far, 20f), true, far, out _), Is.False);
        }

        [Test]
        public void ABotPastTheTruckFront_IsLeftToItsRacingLine()
        {
            var brain = new TruckBoardingBrain(1f);

            Assert.That(Decide(brain, Self(0f, 6f, 15f), true, 6f, out _), Is.False);
        }

        // ---- what it asks for

        [Test]
        public void AlignedAndBehind_DrivesStraightAtThrottle()
        {
            var brain = new TruckBoardingBrain(1f);

            Assert.That(Decide(brain, Self(0f, -30f, 15f), true, -30f, out BoardingCommand command), Is.True);
            Assert.That(Math.Abs(command.Steer), Is.LessThan(0.05f));
            Assert.That(command.Throttle, Is.GreaterThan(0f));
            Assert.That(command.Brake, Is.False);
        }

        [Test]
        public void ClosingTooFastNearTheRampFoot_Brakes()
        {
            var brain = new TruckBoardingBrain(1f);

            Decide(brain, Self(0f, -12f, 22f), true, -12f, out BoardingCommand command);

            Assert.That(command.Brake, Is.True);
            Assert.That(command.Throttle, Is.EqualTo(0f));
        }

        [Test]
        public void OffsetToTheRight_SteersLeftBackOntoTheCentreline()
        {
            var brain = new TruckBoardingBrain(1f);

            Decide(brain, Self(3f, -30f, 15f), true, -30f, out BoardingCommand command);

            Assert.That(command.Steer, Is.LessThan(-0.05f));
        }

        [Test]
        public void OnTheBedAheadOfTheHoldPoint_EasesOffToFallBack()
        {
            var brain = new TruckBoardingBrain(1f);

            Decide(brain, Self(0f, -0.5f, Settings.Speed), true, -0.5f, out BoardingCommand command);

            Assert.That(command.Brake, Is.True);
        }

        [Test]
        public void OnTheBedBehindTheHoldPoint_EdgesForward()
        {
            var brain = new TruckBoardingBrain(1f);

            Decide(brain, Self(0f, -4f, Settings.Speed), true, -4f, out BoardingCommand command);

            Assert.That(command.Brake, Is.False);
            Assert.That(command.Throttle, Is.GreaterThan(Settings.Speed / 26f));
        }

        [Test]
        public void StuckBelowTheBed_ReversesOutOfIt()
        {
            var brain = new TruckBoardingBrain(1f);
            BoardingCommand command = default;

            for (float t = 0f; t < 2f; t += Step)
                Decide(brain, Self(0f, -12f, 0f), true, -12f, out command);

            Assert.That(command.Reverse, Is.True);
        }

        [Test]
        public void ABolderPace_ClosesOnTheTruckFaster()
        {
            var timid = new TruckBoardingBrain(0.5f);
            var bold = new TruckBoardingBrain(1f);

            Decide(timid, Self(0f, -45f, 10f), true, -45f, out BoardingCommand slow);
            Decide(bold, Self(0f, -45f, 10f), true, -45f, out BoardingCommand fast);

            Assert.That(fast.Throttle, Is.GreaterThan(slow.Throttle));
        }

        // ---- closed loop: a point-mass car that obeys the command

        private sealed class Outcome
        {
            public float SecondsInZoneAtEnd;
            public float MaxAlongSeen = float.MinValue;
        }

        private static Outcome Drive(float startAlong, float startLateral, float startSpeed, float pace, float seconds)
        {
            var brain = new TruckBoardingBrain(pace);
            float x = startLateral;
            float z = startAlong;
            float heading = 0f; // radians, 0 = +Z, positive turns right
            float speed = startSpeed;
            float truckZ = 0f;
            float rearEdge = Settings.BedCenterOffset - Settings.BedLength * 0.5f;
            float front = Settings.BedCenterOffset + Settings.BedLength * 0.5f;
            var outcome = new Outcome();
            float inZone = 0f;

            for (float t = 0f; t < seconds; t += Step)
            {
                truckZ += Settings.Speed * Step;
                var self = new BoardingSelf(new PathVector3(x, 0f, z),
                    new PathVector3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading)), speed, 26f);
                float along = z - truckZ;
                if (!brain.TryDecide(self, Truck(truckZ), Settings, true, TailDistance(along), FullSteer, Step, out BoardingCommand c))
                    c = new BoardingCommand(0f, 1f, false, false);

                float targetSpeed = c.Throttle * 26f;
                speed += (targetSpeed - speed) / 0.5f * Step;
                if (c.Brake)
                    speed = Math.Max(0f, speed - 12f * Step);
                heading += c.Steer * 70f * (float)(Math.PI / 180.0) * Step;
                x += (float)Math.Sin(heading) * speed * Step;
                z += (float)Math.Cos(heading) * speed * Step;

                along = z - truckZ;
                float lateral = x;
                if (along > outcome.MaxAlongSeen)
                    outcome.MaxAlongSeen = along;

                bool inside = along >= rearEdge && along <= front && Math.Abs(lateral) < Settings.BedWidth * 0.5f - 0.3f;
                inZone = inside ? inZone + Step : 0f;
            }

            outcome.SecondsInZoneAtEnd = inZone;
            return outcome;
        }

        [TestCase(-45f, 0f, 20f, 1f)]
        [TestCase(-35f, 2.5f, 12f, 0.6f)]
        [TestCase(-20f, 0f, 26f, 1f)]
        [TestCase(-48f, -2f, 8f, 0.5f)]
        public void FromAnywhereBehind_TheBotEndsHoldingTheBed_AndNeverDrivesOffTheFront(
            float startAlong, float startLateral, float startSpeed, float pace)
        {
            Outcome outcome = Drive(startAlong, startLateral, startSpeed, pace, 40f);

            Assert.That(outcome.SecondsInZoneAtEnd, Is.GreaterThan(10f), "it must settle on the bed and stay");
            Assert.That(outcome.MaxAlongSeen, Is.LessThan(Settings.BedCenterOffset + Settings.BedLength * 0.5f + 1f),
                "it must not shoot past the front of the truck");
        }
    }
}
