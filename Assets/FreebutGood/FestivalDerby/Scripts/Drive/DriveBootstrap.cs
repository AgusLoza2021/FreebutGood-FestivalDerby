using FestivalDerby.Driving.Runtime;
using FestivalDerby.Look;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The whole game in one composition root: load the circuit, put the car on the start grid, hand it
    /// the controls, point the chase camera at it, apply the look and dress the track. Every system it
    /// wires is plain code in its own module; this class only decides the order.
    ///
    /// It builds in <see cref="Start"/> rather than Awake so a test (or a tool) can add the component,
    /// set what it wants and only then let the first frame build.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DriveBootstrap : MonoBehaviour
    {
        [Tooltip("Car, handling, map, visuals and look. Everything the drive is tuned with lives here.")]
        [SerializeField] private DrivingFeatureConfig _config;

        [Tooltip("Kerbs, edge lines and trackside props. Empty leaves the circuit bare.")]
        [SerializeField] private TracksideKit _trackside;

        [Tooltip("Seed for the trackside props, so the same circuit is always dressed the same way.")]
        [SerializeField] private uint _tracksideSeed = 7u;

        [Tooltip("Build the on-screen stick and pedals. The keyboard works either way.")]
        [SerializeField] private bool _touchControls = true;

        [SerializeField] private KeyCode _resetKey = KeyCode.R;

        private LoadedMap _map;
        private OwnedGrayboxCircuit _circuit;
        private OwnedCarGeometry _car;
        private Camera _camera;
        private Light _sun;
        private LocalChaseCamera _chase;
        private MobileDriveControls _controls;
        private KeyboardSteer _keyboard;
        private TouchHud _hud;
        private LookApplier _look;
        private TracksideDresser _dresser;
        private PlayerRespawn _respawn;
        private CarGhostMonitor _ghost;

        private bool _brake;
        private bool _reverse;
        private bool _nitro;

        public bool Built { get; private set; }
        public LocalCarFixedStepDriver Driver { get; private set; }
        public Rigidbody CarBody => _car != null ? _car.Body : null;
        public Camera Camera => _camera;
        public MobileDriveControls Controls => _controls;

        public DrivingFeatureConfig Config
        {
            get { return _config; }
            set { _config = value; }
        }

        public TracksideKit Trackside
        {
            get { return _trackside; }
            set { _trackside = value; }
        }

        public bool TouchControls
        {
            get { return _touchControls; }
            set { _touchControls = value; }
        }

        private void Start()
        {
            Build();
        }

        /// <summary>Builds the drive once. Safe to call again: a second call does nothing.</summary>
        public void Build()
        {
            if (Built)
                return;

            if (_config == null)
            {
                Debug.LogError("[drive] DriveBootstrap has no DrivingFeatureConfig assigned.", this);
                return;
            }

            var map = _config.Map as MapAsset;
            if (map == null)
            {
                Debug.LogError("[drive] DrivingFeatureConfig.Map must reference a MapAsset.", this);
                return;
            }

            Built = true;
            Application.targetFrameRate = 60;

            LookSettings look = LookProfile.Resolve(_config.Look as LookProfile);
            CreateSunAndCamera(look);
            LoadCircuit(map);
            SpawnCar();
            WireControls();
            WireCamera(look);
            ApplyLook(look);
            DressTrack();
            ArmRespawn();
        }

        private void CreateSunAndCamera(in LookSettings look)
        {
            var sunObject = new GameObject("Sun", typeof(Light));
            sunObject.transform.SetParent(transform, false);
            _sun = sunObject.GetComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("DriveCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.fieldOfView = look.CameraFov;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 900f;
            _chase = cameraObject.AddComponent<LocalChaseCamera>();
        }

        /// <summary>
        /// The baked map is the ground and the walls. The loader builds it, then the circuit takes the
        /// pieces over so one object owns (and later destroys) the whole track.
        /// </summary>
        private void LoadCircuit(MapAsset map)
        {
            _map = MapLoader.Load(map, false, transform);
            var layout = new GrayboxCircuitLayout(_map.Center, _map.Size, _map.SurfaceY, _map.SpawnHeight,
                _map.WallHeight, _map.WallThickness);
            _circuit = DrivingFeatureConfig.CreateOwnedCircuit(_map.Root, _map.Material, layout, _map.Walls,
                _map.StartGate, _map.SpawnPoses, _map.RoutePoses, _map.RoadMesh);
            _map.DetachOwnership();
        }

        /// <summary>
        /// A physics box with the motor on it, then the cosmetic model on top: the box drives, the model
        /// only looks like a car. The visual is optional; without a catalog the box is what you see.
        /// </summary>
        private void SpawnCar()
        {
            CarDefinition definition = DrivingFeatureConfig.ResolveCar(_config, null);
            _car = CarGeometryFactory.CreateWithoutFloor(definition, _circuit.SpawnPose, transform);

            Driver = _car.Body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            Driver.Bind(_car.Body, definition,
                MotorSetupFactory.ResolveWithProfile(_config, DrivingFeatureConfig.ResolveDriveMode(_config),
                    _camera.transform));
            if (_config.SurfaceGripEnabled)
                Driver.SetSurfaceGripProvider(SurfaceGripSampler.ProviderFor(_map));

            string visualId = DrivingFeatureConfig.ResolvePlayerVisualId(_config);
            if (!CarVisualCatalog.TryResolve(_config.Visuals, visualId, out CarVisualEntry entry))
            {
                Debug.LogWarning("[drive] visual '" + visualId + "' is not in the catalog; the car stays a box.", this);
                return;
            }

            GameObject visual = CarVisualAttacher.Attach(_car.Body.transform, _car.Chassis, entry);
            if (visual == null)
                return;

            if (DrivingFeatureConfig.ResolveWheelSpinEnabled(_config))
                _car.Body.gameObject.AddComponent<CarWheelRig>()
                    .Build(visual.transform, Driver, CarVisualCatalog.ResolveWheelSpinSign(entry));

            Transform pivot = visual.transform.parent;
            if (DrivingFeatureConfig.ResolveBodyJuiceEnabled(_config) && pivot != null
                && pivot.name == CarVisualAttacher.VisualPivotName)
                _car.Body.gameObject.AddComponent<CarBodyJuice>()
                    .Bind(pivot, _car.Body, Driver, CarBodyJuice.ResolveSettings(_config));
        }

        private void WireControls()
        {
            _controls = gameObject.AddComponent<MobileDriveControls>();
            _controls.Configure(DrivingFeatureConfig.ResolveDriveMode(_config),
                DrivingFeatureConfig.ResolveStickDeadZone(_config), _camera.transform);

            _keyboard = gameObject.AddComponent<KeyboardSteer>();
            if (_touchControls)
                _hud = TouchHud.Build(transform);

            // Touch first, keyboard second: whichever is actually being used drives the car.
            _controls.Bind(Driver, new SteerRouter(_hud != null ? _hud.Stick : null, _keyboard));
        }

        private void WireCamera(in LookSettings look)
        {
            // FollowCar is a chase camera with its own car-local offset; the other modes use the
            // world-space offset the look was tuned with.
            Vector3 offset = look.YawMode == CameraYawMode.FollowCar ? look.ChaseOffset : look.CameraOffset;
            _chase.Configure(offset, look.CameraLookHeight, look.CameraFollowSharpness);
            _chase.ConfigureLookAhead(look.LookAheadMetresPerSpeed, look.LookAheadMaxMetres,
                look.LookAheadSharpness, _car.Body);
            _chase.ConfigureSpeedFov(_camera, look.CameraFov, look.SpeedFovWidenDegrees,
                look.SpeedFovAtMetresPerSecond, look.SpeedFovSharpness);
            _chase.Follow(_car.Body.transform);

            // After Follow on purpose: Follow clears the previous heading source, and track mode measures
            // the road's turn from where this run starts.
            if (look.YawMode == CameraYawMode.FollowTrack)
                _chase.ConfigureYaw(TrackHeadingSource.FromMap(_map, look.TrackLookAheadMetres), true,
                    look.YawSharpness, look.YawMaxDegreesPerSecond);
            else if (look.YawMode == CameraYawMode.FollowCar)
            {
                _chase.ConfigureYaw(new CarHeadingSource(_car.Body), false, look.YawSharpness,
                    look.YawMaxDegreesPerSecond);
                _chase.ConfigurePivotAhead(look.PivotAheadMetres);
            }
        }

        /// <summary>Sun, ambient, camera background, palette and post volume, all from the look profile.</summary>
        private void ApplyLook(in LookSettings look)
        {
            if (!look.Enabled)
                return;

            GrayboxCircuitLayout layout = _circuit.Layout;
            _look = LookApplier.Apply(look, new LookScene(_circuit.Root.transform, _sun, _camera,
                _circuit.Material, layout.Center, layout.Size, layout.SurfaceY));
        }

        /// <summary>After the look on purpose: the palette repaints the circuit, the dressing has its own colours.</summary>
        private void DressTrack()
        {
            if (_trackside != null)
                _dresser = TracksideDresser.Spawn(_map, _circuit.Root.transform, _trackside, _tracksideSeed);
        }

        /// <summary>
        /// Stuck for a few seconds, flipped, or fallen off the world: the car goes back to the nearest
        /// point of the route and is briefly a ghost so it cannot land inside something.
        /// </summary>
        private void ArmRespawn()
        {
            if (DrivingFeatureConfig.ResolveStuckRespawnEnabled(_config))
            {
                _ghost = new CarGhostMonitor(
                    DrivingFeatureConfig.ResolveStuckSeconds(_config),
                    DrivingFeatureConfig.ResolveStuckSpeedLimit(_config),
                    DrivingFeatureConfig.ResolveGhostSeconds(_config),
                    DrivingFeatureConfig.ResolveGhostAlpha(_config));
                Transform visualRoot = _car.Body.transform.Find(CarVisualAttacher.VisualPivotName);
                _ghost.Bind(visualRoot, new Collider[] { _car.Chassis });
            }

            _respawn = new PlayerRespawn(_car.Body, _circuit, _ghost);
        }

        private void Update()
        {
            if (!Built || Driver == null)
                return;

            SyncPedals();

            CarHandlingReadout readout = Driver.ReadHandlingDebug();
            _respawn.Tick(Time.deltaTime, readout.SpeedMetersPerSecond, readout.Throttle, Driver.IsOnMovingGround);

            bool resetPressed = Input.GetKeyDown(_resetKey) || (_hud != null && _hud.Reset.Held);
            if (resetPressed)
                ResetCar();
        }

        /// <summary>The pedal slots have one writer, here, fed by the touch buttons and the keyboard alike.</summary>
        private void SyncPedals()
        {
            bool brake = (_hud != null && _hud.Brake.Held) || _keyboard.BrakeHeld;
            bool reverse = _keyboard.ReverseHeld;
            bool nitro = (_hud != null && _hud.Nitro.Held) || _keyboard.NitroHeld;

            if (brake != _brake)
                _controls.Set(DriveControlSlot.Brake, _brake = brake);
            if (reverse != _reverse)
                _controls.Set(DriveControlSlot.Reverse, _reverse = reverse);
            if (nitro != _nitro)
                _controls.Set(DriveControlSlot.Nitro, _nitro = nitro);
        }

        /// <summary>Puts the car back on the route at once. Ignored while a respawn ghost window runs.</summary>
        public void ResetCar()
        {
            if (_respawn != null)
                _respawn.RequestReset();
        }

        private void OnDestroy()
        {
            if (_chase != null)
                _chase.Stop();
            _ghost?.Teardown();
            _look?.Dispose();
            if (_dresser != null)
                _dresser.Dispose();
            _hud?.Destroy();
            _car?.Dispose();
            _circuit?.Dispose();
            _map?.Dispose();
        }
    }
}
