using System;
using FestivalDerby.Driving;
using FestivalDerby.Driving.Handling;

namespace FestivalDerby.Driving.Handling.Tests
{
    /// <summary>
    /// A 2D rigid body that runs the Handling core at a fixed 1/50 s tick, so the acceptance scenarios
    /// can be closed-loop without Unity, without a Rigidbody and without the Editor.
    ///
    /// It deliberately mirrors what the Unity adapter will do: gather the axle loads, call
    /// <see cref="HandlingStep.Step"/> once, then apply the returned force at each axle point and the
    /// returned assist torque about the centre of mass. Anything the sim has to fake (no suspension,
    /// no roll axis, no gravity axis) is listed here rather than hidden:
    /// - axle loads come from a static split plus longitudinal transfer, not from raycasts;
    /// - the body never leaves the ground plane, so the air model is exercised by its own unit test;
    /// - tyre grip is a plain multiplier, not a surface query.
    ///
    /// Semi-implicit Euler on the body-frame equations of motion:
    ///     m (v' - w*vs) = Fx,  m (vs' + w*v) = Fy,  Iyy * w' = tau
    /// </summary>
    public sealed class PlanarCarSim
    {
        /// <summary>1/50 s, the fixed tick the spec asks the acceptance scenarios to run at.</summary>
        public const float DtSeconds = 0.02f;

        public const float MassKilograms = 1200f;
        public const float WheelbaseMeters = 2.5f;
        public const float FrontAxleX = 1.25f;
        public const float RearAxleX = -1.25f;

        /// <summary>Centre of mass height, only used for the longitudinal load transfer.</summary>
        public const float CogHeightMeters = 0.6f;

        public const float BodyWidthMeters = 1.8f;
        public const float BodyLengthMeters = 4.2f;

        /// <summary>Yaw inertia of a solid box of the body dimensions: 1200 * (1.8^2 + 4.2^2) / 12.</summary>
        public static readonly float YawInertiaKilogramMetersSquared =
            MassKilograms * (BodyWidthMeters * BodyWidthMeters + BodyLengthMeters * BodyLengthMeters) / 12f;

        // --- body state -------------------------------------------------------------------
        public float Time;
        public float PositionX;
        public float PositionY;
        public float YawRadians;
        public float ForwardSpeed;
        public float LateralSpeed;
        public float YawRate;

        public HandlingState State;
        public HandlingOutput Output;

        // --- scripted input ---------------------------------------------------------------
        public float StickX;
        public float StickY;
        public float CameraYawRadians;

        /// <summary>
        /// CTRL1: the control scheme under test. RingThrottle is the rig's default so every V2/FP0
        /// scenario keeps measuring the shipped rule; the scheme tests set it explicitly.
        /// </summary>
        public ControlScheme Scheme = ControlScheme.RingThrottle;

        /// <summary>CTRL1: the GAS input, 0..1. Only GasButton reads it.</summary>
        public float Gas;

        /// <summary>
        /// Wall model for the auto-unstick test: forward speed is clamped to <= 0 every tick, so a car
        /// at full pedal cannot move forward and the only way off the wall is the reverse pulse.
        /// </summary>
        public bool WallClampForwardSpeed;

        /// <summary>Negative means "let the stick decide", which is how the player path works.</summary>
        public float ThrottleOverride = -1f;

        public bool Brake;
        public bool Reverse;
        public float NitroScale = 1f;
        public float SurfaceGrip = 1f;
        public float ZoneWheelGrip = 1f;
        public float ZoneTopSpeed = 1f;
        public int GroundedWheels = 4;

        private readonly HandlingParams _params;
        private float _lastLongitudinalForce;

        public PlanarCarSim(in HandlingParams parameters)
        {
            _params = parameters;
        }

        public float BetaDegrees
        {
            get { return Output.BetaRadians * HandlingParams.DegreesPerRadian; }
        }

        public float HeadingErrorDegrees
        {
            get { return Output.HeadingErrorRadians * HandlingParams.DegreesPerRadian; }
        }

        public void Reset(float yawRadians, float forwardSpeed)
        {
            Time = 0f;
            PositionX = 0f;
            PositionY = 0f;
            YawRadians = yawRadians;
            ForwardSpeed = forwardSpeed;
            LateralSpeed = 0f;
            YawRate = 0f;
            State.Reset();
            StickX = 0f;
            StickY = 0f;
            _lastLongitudinalForce = 0f;
            Output = default;
        }

        public void Tick()
        {
            Tick(DtSeconds);
        }

        public void Tick(float deltaTime)
        {
            float staticLoad = MassKilograms * HandlingParams.StandardGravity * 0.5f;
            float transfer = _lastLongitudinalForce * CogHeightMeters / WheelbaseMeters;
            float frontLoad = HandlingMath.Max(staticLoad - transfer, 0f);
            float rearLoad = HandlingMath.Max(staticLoad + transfer, 0f);

            var input = new HandlingInput(
                stickX: StickX,
                stickY: StickY,
                cameraYawRadians: CameraYawRadians,
                carYawRadians: YawRadians,
                throttleOverride: ThrottleOverride,
                brake: Brake,
                reverse: Reverse,
                nitroScale: NitroScale,
                forwardSpeed: ForwardSpeed,
                lateralSpeed: LateralSpeed,
                yawRateRadiansPerSecond: YawRate,
                massKilograms: MassKilograms,
                yawInertiaKilogramMetersSquared: YawInertiaKilogramMetersSquared,
                axleFrontX: FrontAxleX,
                axleRearX: RearAxleX,
                axleLoadFrontNewton: frontLoad,
                axleLoadRearNewton: rearLoad,
                surfaceGripScale: SurfaceGrip,
                zoneWheelGripScale: ZoneWheelGrip,
                zoneTopSpeedScale: ZoneTopSpeed,
                groundedWheels: GroundedWheels,
                deltaTime: deltaTime,
                parameters: _params,
                scheme: Scheme,
                gas: Gas);

            Output = HandlingStep.Step(input, ref State);

            float forceX = Output.LongitudinalForceNewton + Output.FrontForceXNewton + Output.RearForceXNewton;
            float forceY = Output.FrontForceYNewton + Output.RearForceYNewton;
            float torque = FrontAxleX * Output.FrontForceYNewton
                + RearAxleX * Output.RearForceYNewton
                + Output.TotalYawTorqueNewtonMeters;

            float forward = ForwardSpeed;
            float lateral = LateralSpeed;
            float yawRate = YawRate;

            forward += (forceX / MassKilograms + yawRate * lateral) * deltaTime;

            // The wall model clamps before the position integration, so the body never advances into
            // the wall and the reverse pulse is the only thing that can move it.
            if (WallClampForwardSpeed && forward > 0f)
            {
                forward = 0f;
            }

            lateral += (forceY / MassKilograms - yawRate * forward) * deltaTime;
            yawRate += torque / YawInertiaKilogramMetersSquared * deltaTime;

            float cos = (float)Math.Cos(YawRadians);
            float sin = (float)Math.Sin(YawRadians);
            PositionX += (forward * cos - lateral * sin) * deltaTime;
            PositionY += (forward * sin + lateral * cos) * deltaTime;
            YawRadians = HandlingMath.WrapPi(YawRadians + yawRate * deltaTime);

            ForwardSpeed = forward;
            LateralSpeed = lateral;
            YawRate = yawRate;
            _lastLongitudinalForce = Output.LongitudinalForceNewton;
            Time += deltaTime;
        }
    }
}
