using System;
using NUnit.Framework;
using FestivalDerby.Driving.Runtime;
using UnityEngine;

namespace FestivalDerby.Look.Tests
{
    /// <summary>
    /// Pins the camera half of the look: the lead scales with planar speed, is capped, is off when the
    /// look says off, and the tick that produces it allocates nothing.
    /// </summary>
    public sealed class LocalChaseCameraLookAheadPlayModeTests
    {
        private static readonly Vector3 Offset = new Vector3(-16f, 30f, -14f);
        private const float LookHeight = 2f;
        // A very sharp follow makes the camera reach its desired position within one frame, so the
        // measurement is the lead itself and not the follower's ramp toward it. It is a critically
        // damped spring rate in rad/s: 1000 with a 60 Hz step decays the framing error by e^-16.7 in
        // one step, so the position catches the car before a test can observe the ramp.
        private const float Sharpness = 1000f;

        private GameObject _host;
        private GameObject _target;
        private LocalChaseCamera _chase;
        private Rigidbody _body;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("chase camera host");
            _chase = _host.AddComponent<LocalChaseCamera>();

            _target = new GameObject("chase camera target");
            _body = _target.AddComponent<Rigidbody>();
            // Gravity off and no drag: the body's velocity stays exactly the value the test set, so
            // the expected lead is a number the test computes rather than a physics outcome.
            _body.useGravity = false;

            _chase.Configure(Offset, LookHeight, Sharpness);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(_host);
            UnityEngine.Object.Destroy(_target);
        }

        [Test]
        public void Tick_SteadyState_AllocatesNothing()
        {
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);
            _chase.Follow(_target.transform);

            // Warm up so the measurement is the steady state: the first calls pay the JIT and the
            // first property lookups, and counting those would make the assertion about startup.
            for (int i = 0; i < 64; i++)
                _chase.Tick();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++)
                _chase.Tick();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0L), "The chase tick must not allocate once per frame.");
        }

        [Test]
        public void Follow_SnapsWithNoLeadEvenWhileTheCarIsMoving()
        {
            _body.linearVelocity = new Vector3(0f, 0f, 10f);
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);

            _chase.Follow(_target.transform);

            Vector3 lead = _host.transform.position - (_target.transform.position + Offset);
            Assert.AreEqual(0f, lead.magnitude, 0.001f,
                "The first frame of a run must land on the offset, not on the previous run's lead.");
        }

        [Test]
        public void Tick_WhileMoving_LeadsTheFramingByMetresPerSpeed()
        {
            _body.linearVelocity = new Vector3(0f, 0f, 10f);
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);
            _chase.Follow(_target.transform);
            Settle();

            Vector3 lead = _host.transform.position - (_target.transform.position + Offset);
            Assert.AreEqual(0f, lead.x, 0.02f);
            Assert.AreEqual(0f, lead.y, 0.001f, "The lead is planar: the framing must not rise with speed.");
            Assert.AreEqual(5f, lead.z, 0.01f, "10 m/s at 0.5 m per m/s is a five-metre lead.");
        }

        [Test]
        public void Tick_HighSpeed_ClampsTheLeadAtTheMaximum()
        {
            _body.linearVelocity = new Vector3(0f, 0f, 60f);
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);
            _chase.Follow(_target.transform);
            Settle();

            Vector3 lead = _host.transform.position - (_target.transform.position + Offset);
            Assert.AreEqual(8f, lead.z, 0.01f,
                "30 metres of lead would push the car off the screen, so the cap has to hold.");
        }

        [Test]
        public void Tick_WithZeroLead_LeavesTheFramingOnTheOffset()
        {
            _body.linearVelocity = new Vector3(0f, 0f, 10f);
            _chase.ConfigureLookAhead(0f, 0f, 8f, _body);
            _chase.Follow(_target.transform);
            Settle();

            Vector3 lead = _host.transform.position - (_target.transform.position + Offset);
            Assert.AreEqual(0f, lead.magnitude, 0.001f, "A zero lead is the author turning the feature off.");
        }

        [Test]
        public void Tick_ReverseVelocity_LeadsBehindTheCar()
        {
            _body.linearVelocity = new Vector3(0f, 0f, -10f);
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);
            _chase.Follow(_target.transform);
            Settle();

            // The lead follows the velocity's sign, so a car going backwards is framed the same way
            // it drives: the corner it is heading into, which is now behind its nose.
            Vector3 lead = _host.transform.position - (_target.transform.position + Offset);
            Assert.AreEqual(-5f, lead.z, 0.01f);
        }

        [Test]
        public void Stop_MakesFurtherTicksNoOps()
        {
            _body.linearVelocity = new Vector3(0f, 0f, 10f);
            _chase.ConfigureLookAhead(0.5f, 8f, 8f, _body);
            _chase.Follow(_target.transform);
            // One explicit tick races the potential player-loop tick, so the pose read after Stop is
            // the one the component settled on rather than one a later frame would have moved anyway.
            _chase.Tick(1f / 60f);

            _chase.Stop();
            Vector3 afterStop = _host.transform.position;

            for (int i = 0; i < 10; i++)
                _chase.Tick(1f / 60f);

            Assert.IsFalse(_chase.IsFollowing);
            Assert.AreEqual(afterStop, _host.transform.position,
                "A stopped camera must not keep following the car it was released from.");
        }

        /// <summary>
        /// Runs the framing to a fixed point with an explicit 60 Hz delta. The value under test is
        /// the settled lead, and with the wall clock the editor's frame rate decides how far along
        /// the exponential ramp the assertion lands.
        /// </summary>
        /// <remarks>
        /// The target is advanced by its own reported velocity on every step. The framing is solved
        /// from the car's position and its velocity together, so a body that reports 10 m/s while its
        /// transform stands still is one frame of motion the model cannot account for, and the
        /// framing would settle v*dt ahead of the car (16 cm at 10 m/s and 60 Hz). Moving the target
        /// the way a physics step would keeps the fixture and the model agreeing on what reality is.
        /// </remarks>
        private void Settle()
        {
            const float deltaTime = 1f / 60f;
            Vector3 velocity = _body.linearVelocity;
            for (int i = 0; i < 240; i++)
            {
                _target.transform.position += velocity * deltaTime;
                _chase.Tick(deltaTime);
            }
        }
    }
}
