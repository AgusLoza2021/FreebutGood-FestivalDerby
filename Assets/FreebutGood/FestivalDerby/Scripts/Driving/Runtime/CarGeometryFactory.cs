using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Runtime
{
    public static class CarGeometryFactory
    {
        /// <summary>
        /// Builds the car over a floor the size the caller asked for. <paramref name="floorSize"/> is
        /// the driving surface's XZ extent; the zero default keeps the inherited 24x48 constant, so
        /// every existing caller and test is unchanged. The floor is what a big track sizes itself
        /// from, which is why it is the one line the track feature needs to vary.
        /// </summary>
        public static OwnedCarGeometry Create(CarDefinition definition, Pose spawnPose, Transform parent = null,
            Vector2 floorSize = default)
            => CreateCore(definition, spawnPose, parent, includeFloor: true, floorSize: floorSize);

        /// <summary>Creates boxed geometry using a caller-owned surface; Root/Material remain owned and Floor is null.</summary>
        public static OwnedCarGeometry CreateWithoutFloor(CarDefinition definition, Pose spawnPose, Transform parent = null)
            => CreateCore(definition, spawnPose, parent, includeFloor: false, floorSize: default);

        private static OwnedCarGeometry CreateCore(CarDefinition definition, Pose spawnPose, Transform parent,
            bool includeFloor, Vector2 floorSize)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            GrayboxGeometry.ValidateUnitParent(parent);

            var size = GrayboxGeometry.FiniteMinimum(definition.bodySize, 0.1f);
            float mass = GrayboxGeometry.FiniteMinimum(definition.mass, 1f);
            var shader = GrayboxGeometry.RequireShader();

            GameObject root = null;
            Material material = null;
            PhysicsMaterial floorMaterial = null;
            PhysicsMaterial chassisMaterial = null;
            try
            {
                material = new Material(shader) { name = "Owned boxed-car material" };
                root = new GameObject("Owned boxed-car geometry");
                // Keep partial construction inactive, including primitive colliders, until ownership is complete.
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var car = new GameObject("Boxed car");
                car.transform.SetParent(root.transform, false);
                car.transform.SetPositionAndRotation(spawnPose.position, spawnPose.rotation);
                var body = car.AddComponent<Rigidbody>();
                body.mass = mass;
                // The arcade motor owns the feel; the body only needs a stable chassis setup. A low
                // centre of mass resists the roll that lets rival rams flip the car too easily.
                body.centerOfMass = Vector3.down * (size.y * 0.45f);
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.maxDepenetrationVelocity = 4f;
                body.angularDamping = GrayboxGeometry.FiniteMinimum(definition.angularDamping, 0f);
                var chassis = GrayboxGeometry.CreateBox("Chassis", car.transform, size, Vector3.zero, material, true)
                    .GetComponent<BoxCollider>();
                // A slightly bouncy, low-friction chassis lets a roof scrape slide and keeps the
                // ram arcade rather than sticky. The suspension rays, not this collider, carry the car.
                chassisMaterial = new PhysicsMaterial("Owned boxed-car chassis")
                {
                    staticFriction = 0.1f,
                    dynamicFriction = 0.1f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0.15f,
                    bounceCombine = PhysicsMaterialCombine.Maximum
                };
                chassis.sharedMaterial = chassisMaterial;
                var ramLift = car.AddComponent<CarRamLift>();
                ramLift.Bind(body, definition);
                BoxCollider floor = null;
                if (includeFloor)
                {
                    var floorPosition = spawnPose.position - Vector3.up * (size.y * 0.5f + 0.25f);
                    // A zero or non-finite request means "the inherited floor", never a degenerate one.
                    Vector3 floorExtent = floorSize.x > 0f && floorSize.y > 0f
                        ? new Vector3(floorSize.x, 0.5f, floorSize.y)
                        : new Vector3(24f, 0.5f, 48f);
                    floor = GrayboxGeometry.CreateBox("Floor", root.transform, floorExtent, floorPosition, material, true)
                        .GetComponent<BoxCollider>();
                    floorMaterial = new PhysicsMaterial("Owned boxed-car floor friction")
                    { staticFriction = 0f, dynamicFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                    floor.sharedMaterial = floorMaterial;
                }

                GrayboxGeometry.CreateBox("Cabin", car.transform, new Vector3(size.x * 0.75f, size.y * 0.6f, size.z * 0.5f),
                    Vector3.up * (size.y * 0.8f), material, false);
                var wheelSize = new Vector3(size.x * 0.2f, size.y * 0.5f, size.z * 0.2f);
                for (int side = -1; side <= 1; side += 2)
                    for (int axle = -1; axle <= 1; axle += 2)
                    {
                        var position = new Vector3(side * size.x * 0.55f, -size.y * 0.25f, axle * size.z * 0.3f);
                        GrayboxGeometry.CreateBox("Wheel", car.transform, wheelSize, position, material, false);
                    }

                var owned = new OwnedCarGeometry(root, body, chassis, floor, material, floorMaterial, chassisMaterial);
                root.SetActive(true);
                // Set after activation: the physics body does not exist while the root is inactive and
                // Unity silently drops this one (it reads back as the 1e16 default), unlike the others.
                body.maxAngularVelocity = GrayboxGeometry.FiniteMinimum(definition.maxAngularVelocity, 0f);
                body.maxLinearVelocity =
                    GrayboxGeometry.FiniteMinimum(definition.maxForwardSpeed, 0f) * 1.3f;
                return owned;
            }
            catch
            {
                if (root != null)
                {
                    root.SetActive(false);
                    Object.Destroy(root);
                }
                if (material != null)
                    Object.Destroy(material);
                if (floorMaterial != null)
                    Object.Destroy(floorMaterial);
                if (chassisMaterial != null)
                    Object.Destroy(chassisMaterial);
                throw;
            }
        }
    }

    // CarInput-free API lets Assembly-CSharp use the bot without referencing Rules.
    // Cached sampling uses the driver's fixed-step pause gate, not a separate clock.
    public sealed class GrayboxRouteBot
    {
        private const float ReachRadius = 6f;
        private const float FigureEightReachRadius = 1.6f;
        private const float PassedMargin = 2f;
        private const float SteerGainDegrees = 30f;
        private const float CornerAngleDegrees = 35f;
        private const float CornerSpeedFraction = 0.35f;
        private const float SlowDistance = 10f;
        private const float SlowSpeedFraction = 0.5f;
        private const float BrakeSpeedMargin = 1.5f;
        private const float StallSpeed = 0.4f;
        private const float StallDuration = 1.5f;
        private const float ReverseDuration = 0.8f;
        private const float RecoverySteer = 0.65f;

        private readonly OwnedGrayboxCircuit _circuit;
        private readonly CarDefinition _definition;
        // Copied at construction: the bot's model is fixed for the life of the car, so a config edited
        // mid-run cannot give one bot a different handling from its peers.
        private readonly MotorSetup _setup;
        private Rigidbody _body;
        private LocalCarFixedStepDriver _driver;
        private int _goalIndex;
        private float _stallSeconds;
        private float _reverseRemaining;

        public GrayboxRouteBot(OwnedGrayboxCircuit circuit, CarDefinition definition)
            : this(circuit, definition, 0) { }

        public GrayboxRouteBot(OwnedGrayboxCircuit circuit, CarDefinition definition, int firstGoalIndex = 0,
            MotorSetup setup = default)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (firstGoalIndex < 0 || firstGoalIndex >= circuit.RouteCount)
                throw new ArgumentOutOfRangeException(nameof(firstGoalIndex));
            _goalIndex = firstGoalIndex;
            // A default setup is the legacy model with a zeroed preset, and the legacy branch never reads
            // the preset, so the default parameter is a safe "unchanged behaviour" value.
            _setup = setup;
        }

        public OwnedCarGeometry Geometry { get; private set; }

        public OwnedCarGeometry CreateBody(Pose spawnPose, Transform parent)
        {
            if (Geometry != null)
                throw new InvalidOperationException("The bot body was already created for this run.");
            try
            {
                Geometry = CarGeometryFactory.CreateWithoutFloor(_definition, spawnPose, parent);
                _body = Geometry.Body;
                // Bind clears callbacks, so install the sampler afterwards on the body's driver.
                var driver = _body.gameObject.AddComponent<LocalCarFixedStepDriver>();
                driver.Bind(_body, _definition, _setup);
                driver.SetInputSampler(SampleInput);
                _driver = driver;
                return Geometry;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void SetPaused(bool paused)
        {
            if (_driver != null)
                _driver.SetPaused(paused);
        }

        // Unbind before geometry/circuit teardown so no fixed step samples disposed resources.
        public void Dispose()
        {
            if (_driver != null)
            {
                _driver.Unbind();
                _driver = null;
            }
            if (Geometry != null)
            {
                Geometry.Dispose();
                Geometry = null;
            }
            _body = null;
            _stallSeconds = 0f;
            _reverseRemaining = 0f;
        }

        private CarInput SampleInput(float dt)
        {
            // Pause/binding are driver-owned; the default input is a full release, so a car with no
            // route or no body coasts instead of driving itself.
            if (_body == null || _circuit.RouteCount == 0)
                return default;

            var goal = _circuit.GetRoutePose(_goalIndex);
            Vector3 toGoal = goal.position - _body.position;
            toGoal.y = 0f;
            float sqrDist = toGoal.sqrMagnitude;
            // Passing follows the incoming segment; goal rotation points into the next segment.
            int previousIndex = (_goalIndex + _circuit.RouteCount - 1) % _circuit.RouteCount;
            Vector3 incoming = goal.position - _circuit.GetRoutePose(previousIndex).position;
            incoming.y = 0f;
            float incomingSqr = incoming.sqrMagnitude;
            // Dense curves must be reached: an incoming-plane pass alone can skip a crossing branch.
            bool passed = !_circuit.Layout.IsFigureEight && incomingSqr > 1e-6f &&
                Vector3.Dot(incoming * (1f / Mathf.Sqrt(incomingSqr)), -toGoal) > PassedMargin;
            float reachRadius = _circuit.Layout.IsFigureEight ? FigureEightReachRadius : ReachRadius;
            if (sqrDist <= reachRadius * reachRadius || passed)
            {
                _goalIndex = (_goalIndex + 1) % _circuit.RouteCount;
                toGoal = _circuit.GetRoutePose(_goalIndex).position - _body.position;
                toGoal.y = 0f;
                sqrDist = toGoal.sqrMagnitude;
            }

            if (sqrDist < 1e-6f)
                return default;

            Vector3 direction = toGoal * (1f / Mathf.Sqrt(sqrDist));
            Vector3 forward = _body.rotation * Vector3.forward;
            forward.y = 0f;
            float forwardSqr = forward.sqrMagnitude;
            if (forwardSqr < 1e-6f)
                return default;
            forward *= 1f / Mathf.Sqrt(forwardSqr);

            float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
            float steer = Mathf.Clamp(angle / SteerGainDegrees, -1f, 1f);

            Vector3 velocity = _body.linearVelocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;

            float sampleDt = dt > 0f && !float.IsInfinity(dt) ? dt : 0f;
            if (_reverseRemaining <= 0f)
            {
                _stallSeconds = speed < StallSpeed ? _stallSeconds + sampleDt : 0f;
                if (_stallSeconds >= StallDuration)
                {
                    _stallSeconds = 0f;
                    _reverseRemaining = ReverseDuration;
                }
            }
            if (_reverseRemaining > 0f)
            {
                // Steering while reversing separates opposing cars without overriding physics.
                _reverseRemaining = Mathf.Max(0f, _reverseRemaining - sampleDt);
                return new CarInput(RecoverySteer, 1f, false, true, false);
            }

            // Slow for corners/nearby goals; brake only above the conservative target.
            float target = _definition.maxForwardSpeed;
            if (Mathf.Abs(angle) > CornerAngleDegrees)
                target = Mathf.Min(target, _definition.maxForwardSpeed * CornerSpeedFraction);
            if (sqrDist < SlowDistance * SlowDistance)
                target = Mathf.Min(target, _definition.maxForwardSpeed * SlowSpeedFraction);

            // The target speed is asked for on the drive axis as a fraction of top speed, so a
            // corner is a lighter pedal rather than a car that keeps pushing until it brakes.
            float throttle = target / Mathf.Max(_definition.maxForwardSpeed, 1e-3f);
            return new CarInput(steer, throttle, speed > target + BrakeSpeedMargin, false, false);
        }
    }
}
