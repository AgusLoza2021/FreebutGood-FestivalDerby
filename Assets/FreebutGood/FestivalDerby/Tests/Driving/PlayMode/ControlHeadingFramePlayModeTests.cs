using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The control frame is the heading the stick is read against while the visible camera lags the car:
    /// it must start on the car's heading and follow it with the look's own smoothing.
    /// </summary>
    public sealed class ControlHeadingFramePlayModeTests
    {
        private sealed class FixedHeading : IChaseHeadingSource
        {
            public float Degrees;

            public bool TryGetHeading(Vector3 position, out float yawDegrees)
            {
                yawDegrees = Degrees;
                return true;
            }
        }

        private GameObject _frameObject;
        private GameObject _carObject;

        [TearDown]
        public void TearDown()
        {
            if (_frameObject != null) Object.Destroy(_frameObject);
            if (_carObject != null) Object.Destroy(_carObject);
        }

        private ControlHeadingFrame Frame(FixedHeading heading, float sharpness, float maxDegPerSec)
        {
            _frameObject = new GameObject("Frame");
            _carObject = new GameObject("Car");
            var frame = _frameObject.AddComponent<ControlHeadingFrame>();
            frame.Bind(heading, _carObject.transform, sharpness, maxDegPerSec);
            return frame;
        }

        [Test]
        public void Bind_SnapsToTheCurrentHeading_WithNoSwingIn()
        {
            var heading = new FixedHeading { Degrees = 90f };
            var frame = Frame(heading, 8f, 120f);

            Assert.That(frame.YawDegrees, Is.EqualTo(90f).Within(0.001f));
            Assert.That(Vector3.Dot(_frameObject.transform.forward, Vector3.right), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Tick_FollowsTheHeading_ButNeverFasterThanTheCap()
        {
            var heading = new FixedHeading { Degrees = 0f };
            var frame = Frame(heading, 1000f, 120f);

            heading.Degrees = 90f;
            frame.Tick(0.1f);
            Assert.That(frame.YawDegrees, Is.EqualTo(12f).Within(0.01f), "120 deg/s over 0.1 s.");

            for (int i = 0; i < 60; i++)
                frame.Tick(0.1f);
            Assert.That(frame.YawDegrees, Is.EqualTo(90f).Within(0.01f), "It arrives and stops.");
        }

        [Test]
        public void Tick_TakesTheShortWayRound_AcrossPlusMinus180()
        {
            var heading = new FixedHeading { Degrees = 170f };
            var frame = Frame(heading, 1000f, 0f);

            heading.Degrees = -170f;
            frame.Tick(0.1f);

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(frame.YawDegrees, -170f)), Is.LessThan(0.01f),
                "20 degrees the short way, not 340 the long way.");
        }
    }
}
