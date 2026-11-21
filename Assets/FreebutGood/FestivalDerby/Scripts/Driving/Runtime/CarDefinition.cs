using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// Arcade raycast-suspension seeds for the graybox car. These are starting points for owner
    /// tuning, not final medium-car balance: they are picked so a joystick handling feel stays
    /// responsive while rival cars can still launch and flip each other. The owner complaint this
    /// answers is "the car movement feels terrible"; the knobs most worth trying first are documented
    /// in the field comments below.
    /// </summary>
    [CreateAssetMenu(fileName = "CarDefinition", menuName = "Config/CarDefinition")]
    public sealed class CarDefinition : ScriptableObject
    {
        // Dimensions in metres and mass in kilograms; provisional local chassis tuning.
        public Vector3 bodySize = new Vector3(1.6f, 0.7f, 3f);
        public float mass = 1300f;

        // Longitudinal: force in newtons at zero speed, speeds in m/s, deceleration in m/s^2.
        // driveForce is the single biggest feel knob: raise it for a snappier car, lower it for weight.
        public float driveForce = 26000f;
        public float maxForwardSpeed = 26f;
        public float maxReverseSpeed = 6f;
        public float brakeDeceleration = 25f; // ~1.25x the zero-speed drive acceleration (20 m/s^2).
        public float reverseEngageSpeed = 1f;
        public float coastDrag = 0.3f; // Above-cap bleed in 1/s; never clamps momentum in a single tick.

        // Steering: angles in degrees, smoothing in inverse seconds.
        // The 30 -> 9 degree falloff is what keeps a joystick steering feel stable at speed.
        public float maxSteerAngleDegrees = 30f;
        public float highSpeedSteerAngleDegrees = 9f;
        public float steeringSmoothing = 10f;
        public float maxYawRateDegrees = 90f; // Safety cap on the yaw axis, not the assist's target.
        public float yawAssist = 0.5f; // Direct yaw help, scaled by mass; 0 disables it.
        public float steeringDeadZone = 0.12f; // Dimensionless steering fraction.

        // Grip fractions are the share of the axle's lateral velocity cancelled per 50 Hz tick. The
        // motor converts them to a rate, so the same numbers feel the same at any fixed step. Front
        // gripping more than rear is the drift bias; rearDriftGrip replaces rearGrip under braking.
        public float frontGrip = 0.9f;
        public float rearGrip = 0.65f;
        public float rearDriftGrip = 0.2f;
        public float maxLateralForce = 20000f; // Per-axle newtons; stops grip from spinning the car.

        // Suspension in metres, newtons per metre, newton-seconds per metre and newtons.
        // Spring/damper is the Wreckfest weight knob: softer feels floatier, stiffer feels planted.
        // The project runs gravity at -30 m/s^2 (ProjectSettings/DynamicsManager), so the spring must
        // carry mass*30/4 per wheel inside the rest length: 100000 N/m settles a 1300 kg car at
        // ~0.1 m of the 0.18 m travel. A 9.81-based spring bottoms out on the chassis collider.
        public float suspensionRestLength = 0.18f;
        public float suspensionSpring = 100000f;
        public float suspensionDamper = 6000f;
        public float maxSuspensionForce = 60000f;

        // Stability: anti-roll in newtons per metre of compression difference, downforce in
        // newtons per (m/s)^2 of speed, torque in newton-metres and angular speed in rad/s.
        public float antiRoll = 30000f;
        public float downforceCoefficient = 8f;
        public float airControlTorque = 300f;
        public float uprightTorque = 24000f; // Self-righting torque once flipped for more than a second.
        // angularDamping applies on the ground, where it calms suspension wobble; airAngularDamping
        // replaces it while no wheel touches, so a launched car keeps the spin that flips it.
        // maxAngularVelocity must stay at or above ramLiftMaxSpin or it would cut a ram roll short.
        public float angularDamping = 2.5f;
        public float airAngularDamping = 0.2f;
        public float maxAngularVelocity = 8f;

        // Nitro: seconds, dimensionless multipliers. The charge recharges after the cooldown.
        public float nitroDuration = 1.5f;
        public float nitroAccelerationMultiplier = 1.5f;
        public float nitroTopSpeedMultiplier = 1.25f;
        public float nitroCooldown = 6f;

        // Ram lift: closing speed in m/s, lift in m/s of upward velocity change on the victim. The
        // launch is applied at the contact point, so a corner hit also pitches and rolls the victim:
        // ramLiftRollGain adds an explicit tumble away from the aggressor (rad/s per m/s of lift) and
        // ramLiftMaxSpin caps the victim's spin. A side hit rolls the victim at the full rate; a nose
        // or tail hit pitches it at ramLiftPitchShare of that, so rear-ending launches without endos.
        // At the defaults a hard hit spins the victim near the cap, which flips it within its airtime.
        public float ramLiftFactor = 0.5f;
        public float ramLiftThreshold = 5f;
        public float ramLiftMax = 10f;
        public float ramLiftRollGain = 1.2f; // rad/s of tumble per m/s of lift; 0 disables the tumble.
        public float ramLiftPitchShare = 0.5f; // 0..1 share of the tumble kept for nose/tail hits.
        public float ramLiftMaxSpin = 8f; // rad/s cap on the victim's post-ram angular velocity.

        public void Sanitize()
        {
            bodySize.x = FiniteMinimum(bodySize.x, 0.1f);
            bodySize.y = FiniteMinimum(bodySize.y, 0.1f);
            bodySize.z = FiniteMinimum(bodySize.z, 0.1f);
            mass = FiniteMinimum(mass, 1f);

            driveForce = FiniteMinimum(driveForce, 0f);
            maxForwardSpeed = FiniteMinimum(maxForwardSpeed, 0f);
            maxReverseSpeed = FiniteMinimum(maxReverseSpeed, 0f);
            brakeDeceleration = FiniteMinimum(brakeDeceleration, 0f);
            reverseEngageSpeed = FiniteMinimum(reverseEngageSpeed, 0f);
            coastDrag = FiniteMinimum(coastDrag, 0f);
            maxSteerAngleDegrees = FiniteMinimum(maxSteerAngleDegrees, 0f);
            highSpeedSteerAngleDegrees = FiniteMinimum(highSpeedSteerAngleDegrees, 0f);
            steeringSmoothing = FiniteMinimum(steeringSmoothing, 0f);
            maxYawRateDegrees = FiniteMinimum(maxYawRateDegrees, 0f);
            yawAssist = FiniteMinimum(yawAssist, 0f);
            frontGrip = FiniteMinimum(frontGrip, 0f);
            rearGrip = FiniteMinimum(rearGrip, 0f);
            rearDriftGrip = FiniteMinimum(rearDriftGrip, 0f);
            maxLateralForce = FiniteMinimum(maxLateralForce, 0f);
            suspensionRestLength = FiniteMinimum(suspensionRestLength, 0f);
            suspensionSpring = FiniteMinimum(suspensionSpring, 0f);
            suspensionDamper = FiniteMinimum(suspensionDamper, 0f);
            maxSuspensionForce = FiniteMinimum(maxSuspensionForce, 0f);
            antiRoll = FiniteMinimum(antiRoll, 0f);
            downforceCoefficient = FiniteMinimum(downforceCoefficient, 0f);
            airControlTorque = FiniteMinimum(airControlTorque, 0f);
            uprightTorque = FiniteMinimum(uprightTorque, 0f);
            angularDamping = FiniteMinimum(angularDamping, 0f);
            airAngularDamping = FiniteMinimum(airAngularDamping, 0f);
            maxAngularVelocity = FiniteMinimum(maxAngularVelocity, 0f);
            nitroDuration = FiniteMinimum(nitroDuration, 0f);
            nitroCooldown = FiniteMinimum(nitroCooldown, 0f);
            ramLiftFactor = FiniteMinimum(ramLiftFactor, 0f);
            ramLiftThreshold = FiniteMinimum(ramLiftThreshold, 0f);
            ramLiftMax = FiniteMinimum(ramLiftMax, 0f);
            ramLiftRollGain = FiniteMinimum(ramLiftRollGain, 0f);
            ramLiftPitchShare = Mathf.Clamp01(FiniteMinimum(ramLiftPitchShare, 0f));
            ramLiftMaxSpin = FiniteMinimum(ramLiftMaxSpin, 0f);
            steeringDeadZone = Mathf.Clamp01(FiniteMinimum(steeringDeadZone, 0f));
            nitroAccelerationMultiplier = FiniteMinimum(nitroAccelerationMultiplier, 1f);
            nitroTopSpeedMultiplier = FiniteMinimum(nitroTopSpeedMultiplier, 1f);
        }

        private static float FiniteMinimum(float value, float minimum)
        {
            // Reject non-finite serialized data before Mathf can propagate it into tuning.
            if (float.IsNaN(value) || float.IsInfinity(value))
                return minimum;

            return Mathf.Max(value, minimum);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
