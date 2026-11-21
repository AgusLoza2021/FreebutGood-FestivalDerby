using System.Collections;
using FestivalDerby.Driving.Handling;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// A car resting on a moving deck must ride it. The arcade motor floats on raycast suspension, so no
    /// collider friction carries the car along: only the tyre model can, and only if it measures the car's
    /// speed against the ground under the wheels instead of against the world. The deck is kinematic and
    /// moved with MovePosition, exactly like the CATCH carrier, so its Rigidbody reads back zero velocity and
    /// the motion has to come from the deck's own <see cref="IGroundVelocitySource"/>.
    ///
    /// The car is the bot reading (CarRelative) with no throttle and no steer, which is a driver that has let
    /// go of everything: the motor's drag, rolling resistance, lateral grip and yaw damping are all on trial.
    /// Auto-unstick is off because the mapper reads the CarRelative stick as full pedal, so a car standing
    /// still at zero pedal earns a reverse pulse after a second even on static ground; left on, that pulse
    /// and not the ground frame would decide where the car ends up.
    /// </summary>
    public sealed class CarRidesMovingGroundPlayModeTests
    {
        private const float DeckSpeed = 14f;
        private const float TurnDegreesPerSecond = 15f;
        private const float RideSeconds = 3f;
        private const int SettleTicks = 40;
        private const float MaxStraightDriftMetres = 0.5f;

        // High enough above the rig floor (top at y = 0) that a wheel ray can never reach past the deck to it.
        private const float DeckTop = 1.5f;
        private const float DeckX = 30f;
        private static readonly Vector3 DeckSize = new Vector3(6f, 0.3f, 12f);

        // The reference car of the V2 acceptance cases, so the inertia is the one the presets were tuned on.
        private const float BodyMass = 1200f;
        private static readonly Vector3 BodySize = new Vector3(1.8f, 0.7f, 4.2f);
        private const float SpawnHeightAboveDeck = 0.43f;

        private static readonly CarInput LetGo = new CarInput(0f, 0f, false, false, false);

        private CarMotorTestRig _rig;
        private GameObject _deckObject;
        private RideTestDeck _deck;
        private Rigidbody _body;
        private ArcadeCarMotor _motor;
        private Vector3 _startLocal;
        private float _startHeading;

        [SetUp]
        public void SetUp()
        {
            _rig = new CarMotorTestRig();
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
            if (_deckObject != null)
                Object.Destroy(_deckObject);
            _deckObject = null;
            _deck = null;
            _body = null;
            _motor = null;
        }

        [UnityTest]
        public IEnumerator StraightDeck_CarAtRestRidesWithoutSlidingOff()
        {
            yield return SettleOnDeck();
            StartRiding(0f);

            int ticks = Mathf.RoundToInt(RideSeconds / Time.fixedDeltaTime);
            for (int i = 0; i < ticks; i++)
                yield return StepOnce();

            float drift = HorizontalDrift();
            Debug.Log("metric c2b straight deck drift: " + drift.ToString("0.000") + " m after " + RideSeconds + " s");
            Assert.That(_motor.IsGrounded, Is.True, "the car left the deck");
            Assert.That(drift, Is.LessThan(MaxStraightDriftMetres),
                "a car at rest on a deck moving at " + DeckSpeed + " m/s drifted " + drift.ToString("0.00")
                + " m against it in " + RideSeconds + " s");
        }

        [UnityTest]
        public IEnumerator TurningDeck_CarAtRestStaysOnTheFootprint()
        {
            yield return SettleOnDeck();
            StartRiding(TurnDegreesPerSecond * Mathf.Deg2Rad);

            int ticks = Mathf.RoundToInt(RideSeconds / Time.fixedDeltaTime);
            float maxDrift = 0f;
            for (int i = 0; i < ticks; i++)
            {
                yield return StepOnce();
                Vector3 local = DeckLocal(_body.position);
                Assert.That(Mathf.Abs(local.x) < DeckSize.x * 0.5f && Mathf.Abs(local.z) < DeckSize.z * 0.5f, Is.True,
                    "the car left the turning deck's footprint at tick " + i + ", local position " + local);
                maxDrift = Mathf.Max(maxDrift, HorizontalDrift());
            }

            float headingDrift = Mathf.DeltaAngle(_startHeading, RelativeHeading());
            Debug.Log("metric c2b turning deck max drift: " + maxDrift.ToString("0.000") + " m, heading drift "
                + headingDrift.ToString("0.0") + " deg over " + RideSeconds + " s");
            Assert.That(_motor.IsGrounded, Is.True, "the car left the turning deck");
        }

        // ------------------------------------------------------------------ helpers

        private IEnumerator SettleOnDeck()
        {
            _deckObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _deckObject.name = "Ride test deck";
            _deckObject.transform.SetPositionAndRotation(
                new Vector3(DeckX, DeckTop - DeckSize.y * 0.5f, 0f), Quaternion.identity);
            _deckObject.transform.localScale = DeckSize;
            var deckBody = _deckObject.AddComponent<Rigidbody>();
            deckBody.isKinematic = true;
            deckBody.useGravity = false;
            _deck = _deckObject.AddComponent<RideTestDeck>();
            _deck.Bind(deckBody);

            CarDefinition definition = _rig.Definition();
            definition.bodySize = BodySize;
            _body = _rig.Body("ride test car", BodyMass, BodySize);
            // The shipped car factory hands the body the definition's ground damping; without it the case
            // would not see the world-frame angular damping a live car fights on a turning deck.
            _body.angularDamping = definition.angularDamping;
            _body.transform.SetPositionAndRotation(
                new Vector3(DeckX, DeckTop + SpawnHeightAboveDeck, 0f), Quaternion.identity);
            Physics.SyncTransforms();

            _motor = new ArcadeCarMotor(
                _body,
                definition,
                HandlingProfile.ResolvePreset(HandlingPreset.Balanced).WithAutoUnstick(AutoUnstickTuning.Off),
                HandlingPreset.Balanced,
                JoystickDriveMode.CarRelative,
                null);

            for (int i = 0; i < SettleTicks; i++)
                yield return StepOnce();

            Assert.That(_motor.IsGrounded, Is.True, "the car never settled on the deck");
        }

        private void StartRiding(float yawRateRadians)
        {
            _deck.Begin(DeckSpeed, yawRateRadians);
            // The car already moves with the deck, so any drift from here on is what the motor did to it.
            _body.linearVelocity = _deck.GetPointVelocity(_body.position);
            _body.angularVelocity = _deck.AngularVelocity;
            _startLocal = DeckLocal(_body.position);
            _startHeading = RelativeHeading();
        }

        private float RelativeHeading()
        {
            return Mathf.DeltaAngle(_deck.Body.rotation.eulerAngles.y, _body.rotation.eulerAngles.y);
        }

        private IEnumerator StepOnce()
        {
            yield return new WaitForFixedUpdate();
            _motor.Step(LetGo, Time.fixedDeltaTime);
            CarMotorTestRig.AssertFinite(_body.linearVelocity, "velocity must stay finite on the deck");
        }

        private Vector3 DeckLocal(Vector3 world)
        {
            Rigidbody deck = _deck.Body;
            return Quaternion.Inverse(deck.rotation) * (world - deck.position);
        }

        private float HorizontalDrift()
        {
            Vector3 delta = DeckLocal(_body.position) - _startLocal;
            delta.y = 0f;
            return delta.magnitude;
        }
    }
}
