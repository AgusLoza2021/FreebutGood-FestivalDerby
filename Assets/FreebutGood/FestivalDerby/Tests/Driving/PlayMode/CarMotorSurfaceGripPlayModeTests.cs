using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Play-mode coverage for terrain grip reaching the handling model: the multiplier the surface
    /// sampler reads on dirt must make the tyres scrub lateral speed more slowly than on asphalt.
    /// The car is a bare rigidbody with no chassis collider — the suspension is raycast based, and a
    /// real contact would add ground friction this test does not control, hiding the difference.
    /// The rig parks the chassis (frozen Y and rotation, no gravity) so the springs are not also on
    /// trial: without wheels a raycast suspension can bounce out of its own ray length, and a car
    /// that tumbles before the slide starts measures nothing.
    /// </summary>
    public class CarMotorSurfaceGripPlayModeTests
    {
        private const float StepSeconds = 0.02f;
        private const int SettleSteps = 40;
        private const int ScrubSteps = 10;
        private const float InitialLateralSpeed = 8f;

        private CarDefinition _definition;
        private SimulationMode _previousSimulationMode;
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            _definition = Resources.Load<CarDefinition>("CarDefinition");
            Assert.IsNotNull(_definition, "Resources/CarDefinition.asset must exist for the handling tests.");
            _previousSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Grip test ground";
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _previousSimulationMode;
            if (_ground != null)
                Object.Destroy(_ground);
        }

        [UnityTest]
        public IEnumerator DirtGrip_ScrubsLessLateralSpeedThanAsphalt()
        {
            BakedPath path = BuildAllDirtPath();
            var sampler = new SurfaceGripSampler(path, SurfaceTable.Defaults);
            float dirtGrip = sampler.MultiplierAt(5f);
            Assert.AreEqual(0.82f, dirtGrip, 1e-3f, "Dirt's stock grip is the multiplier under test.");

            float streetRemaining = MeasureRemainingLateralSpeed(1f);
            float dirtRemaining = MeasureRemainingLateralSpeed(dirtGrip);

            Assert.Greater(streetRemaining, 0f, "The asphalt car should still be sliding somewhere.");
            Assert.Greater(dirtRemaining, streetRemaining,
                "Dirt must retain more lateral speed than asphalt, or the surface does nothing. " +
                "Street=" + streetRemaining.ToString("0.00") + " Dirt=" + dirtRemaining.ToString("0.00"));
            Assert.Greater(dirtRemaining, streetRemaining * 1.15f,
                "The difference must be clear, not a rounding artefact. Street=" +
                streetRemaining.ToString("0.00") + " Dirt=" + dirtRemaining.ToString("0.00"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator NeutralGrip_IsTheUntouchedModel()
        {
            float neutral = MeasureRemainingLateralSpeed(1f);
            float explicitNeutral = MeasureRemainingLateralSpeed(1f);

            Assert.AreEqual(neutral, explicitNeutral, 1e-4f,
                "Applying the neutral multiplier twice must reproduce the same step, so a run with " +
                "surfaces off is unchanged.");
            yield return null;
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// Slides one car sideways under a fixed grip multiplier and returns the lateral speed left
        /// after a window too short for either surface to finish scrubbing it off, so the two
        /// multipliers are compared on the way down rather than both at zero.
        /// </summary>
        private float MeasureRemainingLateralSpeed(float grip)
        {
            var go = new GameObject("Grip test car");
            var body = go.AddComponent<Rigidbody>();
            body.mass = _definition.mass;
            body.sleepThreshold = 0f;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            // Parked just inside the ray, so every wheel finds the ground on every tick and the only
            // force left on the lateral axis is the tyre the surface multiplier scales.
            go.transform.position = new Vector3(0f,
                _definition.bodySize.y * 0.5f + _definition.suspensionRestLength - 0.06f, 0f);
            Physics.SyncTransforms();

            var motor = new CarMotor(body, _definition);
            var noInput = default(CarInput);

            try
            {
                for (int i = 0; i < SettleSteps; i++)
                {
                    motor.Step(noInput, StepSeconds);
                    Physics.Simulate(StepSeconds);
                }

                // A slide large enough to sit on the tyre force cap, which is the regime a real
                // corner puts the car in and the one the surface multiplier scales.
                body.linearVelocity = Vector3.right * InitialLateralSpeed;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();

                for (int i = 0; i < ScrubSteps; i++)
                {
                    motor.ApplySurfaceGrip(grip);
                    motor.Step(noInput, StepSeconds);
                    Physics.Simulate(StepSeconds);
                }

                return Mathf.Abs(Vector3.Dot(body.linearVelocity, body.transform.right));
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        private static BakedPath BuildAllDirtPath()
        {
            const int rings = 8;
            const float length = 100f;
            var path = new BakedPath
            {
                Positions = new PathVector3[rings],
                Tangents = new PathVector3[rings],
                Ups = new PathVector3[rings],
                WidthLeft = new float[rings],
                WidthRight = new float[rings],
                Banking = new float[rings],
                Distances = new float[rings],
                SurfacePrimary = new byte[rings],
                SurfaceSecondary = new byte[rings],
                SurfaceBlend = new byte[rings],
                Closed = true,
            };

            for (int i = 0; i < rings; i++)
            {
                path.Positions[i] = new PathVector3(0f, 0f, length * i / rings);
                path.Tangents[i] = new PathVector3(0f, 0f, 1f);
                path.Ups[i] = PathVector3.Up;
                path.WidthLeft[i] = 4f;
                path.WidthRight[i] = 4f;
                path.Distances[i] = length * i / rings;
                path.SurfacePrimary[i] = (byte)SurfaceId.Dirt;
                path.SurfaceSecondary[i] = (byte)SurfaceId.Dirt;
            }
            path.TotalLength = length;
            path.ComputeCurvature();
            return path;
        }
    }
}
