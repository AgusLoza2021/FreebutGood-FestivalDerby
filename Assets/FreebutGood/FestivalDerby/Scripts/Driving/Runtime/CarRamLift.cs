using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Arcade ram response: when this car drives into another car fast enough, the victim is launched
    /// upward and tumbled away from the aggressor (a side hit rolls it, a nose or tail hit pitches it less). The launch is a plain Rigidbody
    /// impulse applied at the contact point, so an off-centre hit spends part of the impulse as
    /// pitch/roll, which is what turns a hard hit into a flip. It uses no constraints, no global
    /// state and no allocation in the callback, so the same component runs on the host and on a
    /// predicting client. Only the aggressor lifts: of the two cars in one collision, the one whose
    /// velocity points into the other harder owns the hit, so a mutual hit is never applied twice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarRamLift : MonoBehaviour
    {
        private Rigidbody _body;
        // Velocity at the start of the current fixed tick, i.e. before this tick's collision solve.
        // OnCollisionEnter fires after the solve, when an inelastic hit has already equalised both
        // cars, so the pre-impact closing speed can only be read from the previous tick.
        private Vector3 _preStepVelocity;
        private float _factor;
        private float _threshold;
        private float _max;
        private float _rollGain;
        private float _pitchShare;
        private float _maxSpin;

        /// <summary>
        /// How long the victim's auto-roll stands down after a ram. Long enough to cover the airborne part of
        /// a big hit, short enough that a car flipped onto its roof by the landing still rights itself.
        /// </summary>
        private const float RamRollMuteSeconds = 0.6f;

        /// <summary>Copies the ram tuning once; a mutated asset must not change a running car.</summary>
        public void Bind(Rigidbody body, CarDefinition definition)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            _body = body;
            _preStepVelocity = body.linearVelocity;
            _factor = FiniteMinimum(definition.ramLiftFactor, 0f);
            _threshold = FiniteMinimum(definition.ramLiftThreshold, 0f);
            _max = FiniteMinimum(definition.ramLiftMax, 0f);
            _rollGain = FiniteMinimum(definition.ramLiftRollGain, 0f);
            _pitchShare = Mathf.Clamp01(FiniteMinimum(definition.ramLiftPitchShare, 0f));
            _maxSpin = FiniteMinimum(definition.ramLiftMaxSpin, 0f);
        }

        private void FixedUpdate()
        {
            if (_body != null)
                _preStepVelocity = _body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_body == null || _factor <= 0f || _max <= 0f || collision.contactCount == 0)
                return;
            Rigidbody other = collision.collider.attachedRigidbody;
            if (other == null || other == _body)
                return;
            // Only rival cars lift: static geometry and props carry no ram component.
            CarRamLift otherLift = other.GetComponent<CarRamLift>();
            if (otherLift == null)
                return;

            Vector3 toOther = other.worldCenterOfMass - _body.worldCenterOfMass;
            toOther.y = 0f;
            float squaredDistance = toOther.sqrMagnitude;
            if (squaredDistance < 1e-6f)
                return;
            Vector3 direction = toOther * (1f / Mathf.Sqrt(squaredDistance));

            float mine = Vector3.Dot(_preStepVelocity, direction);
            float theirs = Vector3.Dot(otherLift._preStepVelocity, -direction);
            if (mine <= theirs)
            {
                // A tie is deliberately a no-op: both cars are equally guilty, so neither is a rammer.
                return;
            }

            float closing = Vector3.Dot(_preStepVelocity - otherLift._preStepVelocity, direction);
            float above = closing - _threshold;
            if (above <= 0f)
                return;

            // VelocityChange is mass independent, so the lift is the same real-world hop on any car.
            // The impulse itself must be mass scaled, because it is applied at the contact point.
            float liftDeltaV = Mathf.Min(_factor * above, _max);
            Vector3 contactPoint = collision.GetContact(0).point;
            // The engine only enforces the victim's own angular limit, so tighten that limit to the ram
            // cap before the impulse lands: the strictest of the two always wins and the contact torque
            // can never spin the victim past ramLiftMaxSpin.
            if (_maxSpin > 0f && other.maxAngularVelocity > _maxSpin)
                other.maxAngularVelocity = _maxSpin;
            other.AddForceAtPosition(Vector3.up * (liftDeltaV * other.mass), contactPoint, ForceMode.Impulse);
            ApplyTumble(other, direction, liftDeltaV);
            MuteVictimAutoRoll(other);
        }

        // The V2 motor can level a rolled car, which is the right thing for a car that has come to rest on
        // its roof and the wrong thing for one that a ram just launched: the levelling torque would fight
        // the tumble and a hard hit would stop flipping cars. The victim's motor stands down for the length
        // of the flip. Read through the driver, because the motor itself is not a component.
        private static void MuteVictimAutoRoll(Rigidbody victim)
        {
            var driver = victim.GetComponent<LocalCarFixedStepDriver>();
            if (driver != null)
                driver.NotifyRam(RamRollMuteSeconds);
        }

        // The contact impulse alone tips the victim unpredictably: the floor and the aggressor soak up
        // most of its lever. This adds a deterministic tumble about the horizontal axis perpendicular to
        // the hit, so the near side always lifts and the roof tips away from the aggressor, whatever
        // angle it came from. A side hit rolls the victim in full; a nose or tail hit pitches it at
        // only _pitchShare of the rate, so rear-ending stays a launch rather than an automatic endo.
        private void ApplyTumble(Rigidbody other, Vector3 direction, float liftDeltaV)
        {
            if (_rollGain <= 0f || _maxSpin <= 0f)
                return;
            Vector3 victimUp = other.transform.up;
            Vector3 victimForward = other.transform.forward;
            Vector3 victimRight = Vector3.Cross(victimUp, victimForward);
            // direction is horizontal aggressor -> victim, so up x direction is the axis about which the
            // victim's aggressor-side lifts and its roof moves along the hit.
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            float rollPart = Vector3.Dot(axis, victimForward);
            float pitchPart = Vector3.Dot(axis, victimRight) * _pitchShare;
            // VelocityChange torque is already in rad/s, so the cap applies to the spin this adds.
            float rate = Mathf.Clamp(_rollGain * liftDeltaV, 0f, _maxSpin);
            if (rate <= 0f)
                return;
            other.AddTorque((victimForward * rollPart + victimRight * pitchPart) * rate, ForceMode.VelocityChange);
        }

        private static float FiniteMinimum(float value, float minimum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return minimum;
            return Mathf.Max(value, minimum);
        }
    }
}
