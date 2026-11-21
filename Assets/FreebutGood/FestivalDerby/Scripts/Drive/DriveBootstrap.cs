using FestivalDerby.Bots;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.Look;
using FestivalDerby.Match;
using FestivalDerby.Menu;
using FestivalDerby.Menu;
using FestivalDerby.World;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The whole game in one composition root: load the circuit, put the car on the start grid, hand it
    /// the controls, point the chase camera at it, apply the look, dress the track and line up the bots.
    ///
    /// It holds no tuning of its own. Every choice lives on a config object under "Config" in the
    /// Hierarchy (Car, Controls, Camera, Look, Track, Bots), and the heavy presets those point at are
    /// ScriptableObject assets. This class only decides the order things are built in.
    ///
    /// It builds in <see cref="Start"/> rather than Awake so a test (or a tool) can change a config and
    /// only then let the first frame build.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DriveBootstrap : MonoBehaviour
    {
        private const int TargetFrameRate = 60;

        [Header("Config objects (children of Config)")]
        [SerializeField] private CarConfig _carConfig;
        [SerializeField] private ControlsConfig _controlsConfig;
        [SerializeField] private CameraConfig _cameraConfig;
        [SerializeField] private LookConfig _lookConfig;
        [SerializeField] private TrackConfig _trackConfig;
        [SerializeField] private BotsConfig _botsConfig;
        [SerializeField] private MatchConfig _matchConfig;

        [Tooltip("Everything built at runtime (track, cars, camera, touch controls) goes under this object.")]
        [SerializeField] private Transform _runtime;

        private DrivingFeatureConfig _driving;
        private LoadedMap _map;
        private OwnedGrayboxCircuit _circuit;
        private OwnedCarGeometry _car;
        private Camera _camera;
        private ControlHeadingFrame _controlFrame;
        private Light _sun;
        private LocalChaseCamera _chase;
        private MobileDriveControls _controls;
        private KeyboardSteer _keyboard;
        private TouchHud _hud;
        private LookApplier _look;
        private TracksideDresser _dresser;
        private PlayerRespawn _respawn;
        private CarGhostMonitor _ghost;
        private BotField _bots;
        private TruckChaseSession _truckChase;
        private LapRaceSession _lapRace;

        private bool _brake;
        private bool _reverse;
        private bool _nitro;

        public bool Built { get; private set; }

        /// <summary>What the main menu chose for this run; null when Gameplay was opened directly.</summary>
        public MenuSelection Launch { get; private set; }

        /// <summary>Laps chosen in the menu (0 without one). Nothing counts laps at runtime yet.</summary>
        public int TargetLaps => Launch != null ? Launch.Laps : 0;
        public LocalCarFixedStepDriver Driver { get; private set; }
        public Rigidbody CarBody => _car != null ? _car.Body : null;
        public Camera Camera => _camera;
        public MobileDriveControls Controls => _controls;
        public Transform RuntimeRoot => _runtime;

        /// <summary>AI cars spawned for this run (0 before the build).</summary>
        public int BotCount => _bots != null ? _bots.Count : 0;

        public Rigidbody GetBotBody(int index) => _bots[index].Body;

        /// <summary>The running truck chase, or null in any other mode.</summary>
        public TruckChaseSession TruckChase => _truckChase;

        /// <summary>The running lap race, or null in any other mode.</summary>
        public LapRaceSession LapRace => _lapRace;

        /// <summary>Metres of racing line bot <paramref name="index"/> has covered since the start.</summary>
        public float GetBotTravelledMeters(int index) => _bots[index].TravelledMeters;

        private void Start()
        {
            Build();
        }

        /// <summary>Builds the drive once. Safe to call again: a second call does nothing.</summary>
        public void Build()
        {
            if (Built)
                return;

            FindMissingConfigs();
            // Before anything reads a config: a run that came from the main menu carries its choices here.
            Launch = MenuLaunch.Apply(_carConfig, _trackConfig, _botsConfig);
            _driving = _carConfig != null ? _carConfig.Driving : null;
            if (_driving == null)
            {
                Debug.LogError("[drive] The Car config has no DrivingFeatureConfig assigned.", this);
                return;
            }

            MapAsset map = _trackConfig != null ? _trackConfig.Map : null;
            if (map == null)
            {
                Debug.LogError("[drive] The Track config has no MapAsset assigned.", this);
                return;
            }

            Built = true;
            Application.targetFrameRate = TargetFrameRate;
            EnsureRuntimeRoot();

            LookSettings look = LookProfile.Resolve(_lookConfig != null ? _lookConfig.Profile : null);
            LookSettings framing = _cameraConfig != null && _cameraConfig.Framing != null
                ? LookProfile.Resolve(_cameraConfig.Framing)
                : look;
            int botCount = _botsConfig != null ? _botsConfig.Count : 0;

            CreateSunAndCamera(framing);
            LoadCircuit(map, BotField.GridSlotsFor(botCount));
            SpawnCar();
            WireControls();
            WireCamera(framing);
            ApplyLook(look);
            DressTrack();
            ArmRespawn();
            SpawnBots(botCount);
            StartMatch();
        }

        /// <summary>
        /// The scene builder wires every reference, but a hand-made scene (or a test) may only have the
        /// config objects somewhere under this one, so they are looked up before giving up on them.
        /// </summary>
        private void FindMissingConfigs()
        {
            if (_carConfig == null) _carConfig = GetComponentInChildren<CarConfig>(true);
            if (_controlsConfig == null) _controlsConfig = GetComponentInChildren<ControlsConfig>(true);
            if (_cameraConfig == null) _cameraConfig = GetComponentInChildren<CameraConfig>(true);
            if (_lookConfig == null) _lookConfig = GetComponentInChildren<LookConfig>(true);
            if (_trackConfig == null) _trackConfig = GetComponentInChildren<TrackConfig>(true);
            if (_botsConfig == null) _botsConfig = GetComponentInChildren<BotsConfig>(true);
            if (_matchConfig == null) _matchConfig = GetComponentInChildren<MatchConfig>(true);
        }

        private void EnsureRuntimeRoot()
        {
            if (_runtime != null)
                return;

            _runtime = new GameObject("Runtime").transform;
            _runtime.SetParent(transform, false);
        }

        private void CreateSunAndCamera(in LookSettings framing)
        {
            var sunObject = new GameObject("Sun", typeof(Light));
            sunObject.transform.SetParent(_runtime, false);
            _sun = sunObject.GetComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("DriveCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(_runtime, false);
            _camera = cameraObject.GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.fieldOfView = framing.CameraFov;
            _camera.nearClipPlane = _cameraConfig != null ? _cameraConfig.NearClip : 0.3f;
            _camera.farClipPlane = _cameraConfig != null ? _cameraConfig.FarClip : 900f;
            _chase = cameraObject.AddComponent<LocalChaseCamera>();

            // With drift on, the visible camera lags the car's heading (see WireCamera). The stick is
            // read against the camera's heading, so it reads this frame instead: the look's own
            // smoothing, and a held direction steers exactly as it did before drift.
            if (framing.YawMode == CameraYawMode.FollowCar && CarBodyJuice.ResolveDriftSettings(_driving).Enabled)
            {
                _controlFrame = new GameObject("ControlFrame").AddComponent<ControlHeadingFrame>();
                _controlFrame.transform.SetParent(_runtime, false);
            }
        }

        /// <summary>What the stick and the motor read their heading from: the camera, or the drift control frame.</summary>
        private Transform ControlReference => _controlFrame != null ? _controlFrame.transform : _camera.transform;

        /// <summary>
        /// The baked map is the ground and the walls. The loader builds it, then the circuit takes the
        /// pieces over so one object owns (and later destroys) the whole track. <paramref name="gridSlots"/>
        /// grows the starting grid by whole rows when the field is bigger than the authored grid.
        /// </summary>
        private void LoadCircuit(MapAsset map, int gridSlots)
        {
            _map = MapLoader.Load(map, false, _runtime, gridSlots);
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
            CarDefinition definition = DrivingFeatureConfig.ResolveCar(_driving, null);
            _car = CarGeometryFactory.CreateWithoutFloor(definition, _circuit.SpawnPose, _runtime);
            _car.Root.name = "Player";

            Driver = _car.Body.gameObject.AddComponent<LocalCarFixedStepDriver>();
            Driver.Bind(_car.Body, definition,
                _carConfig.ResolveMotor(DrivingFeatureConfig.ResolveDriveMode(_driving), ControlReference));
            if (_driving.SurfaceGripEnabled)
                Driver.SetSurfaceGripProvider(SurfaceGripSampler.ProviderFor(_map));

            string visualId = _carConfig.ModelId;
            if (!CarVisualCatalog.TryResolve(_driving.Visuals, visualId, out CarVisualEntry entry))
            {
                Debug.LogWarning("[drive] visual '" + visualId + "' is not in the catalog; the car stays a box.", this);
                return;
            }

            GameObject visual = CarVisualAttacher.Attach(_car.Body.transform, _car.Chassis, entry);
            if (visual == null)
                return;

            if (_carConfig.Repaint)
                new CarPaintApplier(visual, entry).Apply(_carConfig.PaintColor);

            if (DrivingFeatureConfig.ResolveWheelSpinEnabled(_driving))
                _car.Body.gameObject.AddComponent<CarWheelRig>()
                    .Build(visual.transform, Driver, CarVisualCatalog.ResolveWheelSpinSign(entry));

            Transform pivot = visual.transform.parent;
            if (DrivingFeatureConfig.ResolveBodyJuiceEnabled(_driving) && pivot != null
                && pivot.name == CarVisualAttacher.VisualPivotName)
                _car.Body.gameObject.AddComponent<CarBodyJuice>()
                    .Bind(pivot, _car.Body, Driver, CarBodyJuice.ResolveSettings(_driving),
                        CarBodyJuice.ResolveDriftSettings(_driving));
        }

        private void WireControls()
        {
            GameObject host = _runtime.gameObject;
            _controls = host.AddComponent<MobileDriveControls>();
            _controls.Configure(DrivingFeatureConfig.ResolveDriveMode(_driving),
                DrivingFeatureConfig.ResolveStickDeadZone(_driving), ControlReference);

            bool keyboard = _controlsConfig == null || _controlsConfig.Keyboard;
            bool touch = _controlsConfig == null || _controlsConfig.TouchControls;
            if (keyboard)
                _keyboard = host.AddComponent<KeyboardSteer>();
            if (touch)
                _hud = TouchHud.Build(_runtime);

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
                // A drifting car needs a camera that trails its heading; with drift off these are the look's own values.
                _chase.ConfigureYaw(new CarHeadingSource(_car.Body), false,
                    DrivingFeatureConfig.ResolveDriftCameraYawSharpness(_driving, look.YawSharpness),
                    DrivingFeatureConfig.ResolveDriftCameraYawMaxDegPerSec(_driving, look.YawMaxDegreesPerSecond));
                _chase.ConfigurePivotAhead(look.PivotAheadMetres);
                if (_controlFrame != null)
                    _controlFrame.Bind(new CarHeadingSource(_car.Body), _car.Body.transform,
                        look.YawSharpness, look.YawMaxDegreesPerSecond);
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
            if (_trackConfig.Trackside != null)
                _dresser = TracksideDresser.Spawn(_map, _circuit.Root.transform, _trackConfig.Trackside,
                    _trackConfig.DressingSeed);
        }

        /// <summary>
        /// Stuck for a few seconds, flipped, or fallen off the world: the car goes back to the nearest
        /// point of the route and is briefly a ghost so it cannot land inside something.
        /// </summary>
        private void ArmRespawn()
        {
            if (DrivingFeatureConfig.ResolveStuckRespawnEnabled(_driving))
            {
                _ghost = new CarGhostMonitor(
                    DrivingFeatureConfig.ResolveStuckSeconds(_driving),
                    DrivingFeatureConfig.ResolveStuckSpeedLimit(_driving),
                    DrivingFeatureConfig.ResolveGhostSeconds(_driving),
                    DrivingFeatureConfig.ResolveGhostAlpha(_driving));
                Transform visualRoot = _car.Body.transform.Find(CarVisualAttacher.VisualPivotName);
                _ghost.Bind(visualRoot, new Collider[] { _car.Chassis });
            }

            _respawn = new PlayerRespawn(_car.Body, _circuit, _ghost);
        }

        /// <summary>
        /// The bots drive the player's car read through a car-relative axis (a bot has a steering wheel,
        /// not a screen-space stick), so only their pace makes them slower than the player.
        /// </summary>
        private void SpawnBots(int count)
        {
            MotorSetup setup = _carConfig.ResolveMotor(JoystickDriveMode.CarRelative, null);
            _bots = BotField.Spawn(count, _map, _circuit, _driving, setup,
                _botsConfig != null ? _botsConfig.Models : null,
                _botsConfig != null ? _botsConfig.Liveries : null,
                _botsConfig != null ? _botsConfig.SlowestPace : 1f,
                _botsConfig != null ? _botsConfig.FastestPace : 1f,
                _runtime);
        }

        /// <summary>
        /// The rules of this run. Free drive (the default, and what a scene without a Match config gets)
        /// adds nothing: no truck, no HUD, no eliminations.
        /// </summary>
        private void StartMatch()
        {
            MatchMode mode = ResolveMatchMode();
            if (mode != MatchMode.TruckChase && mode != MatchMode.Laps)
                return;

            var bots = new RacingLineBot[_bots.Count];
            for (int i = 0; i < bots.Length; i++)
                bots[i] = _bots[i];

            MatchConfig config = _matchConfig;
            if (mode == MatchMode.Laps)
            {
                _lapRace = LapRaceSession.Create(new LapRaceSetup
                {
                    Line = _map.RacingLine,
                    Laps = ResolveLaps(),
                    Parent = _runtime,
                    StartDelay = config != null ? config.StartDelay : 3f,
                    PlayerBody = _car.Body,
                    PlayerDriver = Driver,
                    Bots = bots,
                });
                if (_lapRace == null)
                    Debug.LogWarning("[drive] The lap race could not start: the circuit has no usable racing line.", this);
                return;
            }

            var setup = new TruckChaseSetup
            {
                Line = _map.RacingLine,
                Truck = TruckSettings.Default.WithSpeed(config != null ? config.TruckSpeed : TruckSettings.Default.Speed),
                Rules = new TruckChaseSettings(config != null ? config.BoardSeconds : 3f,
                    config != null ? config.EliminationSeconds : 20f, TruckSettings.Default.BedHalfLength),
                TruckMaterial = _circuit.Material,
                Parent = _runtime,
                StartDelay = config != null ? config.StartDelay : 3f,
                LeadMeters = config != null ? config.TruckLeadMeters : 45f,
                PlayerGridPosition = _circuit.SpawnPose.position,
                PlayerBody = _car.Body,
                PlayerDriver = Driver,
                Bots = bots,
            };

            _truckChase = TruckChaseSession.Create(setup);
            if (_truckChase == null)
                Debug.LogWarning("[drive] The truck chase could not start: the circuit has no usable racing line.", this);
        }

        /// <summary>The menu's choice wins when the config allows it; otherwise the config's own mode.</summary>
        private MatchMode ResolveMatchMode()
        {
            MatchMode mode = _matchConfig != null ? _matchConfig.Mode : MatchMode.FreeDrive;
            // Only a run that came from the menu's Play button carries a choice; a Gameplay scene opened on its
            // own must not inherit whatever was saved last time.
            if (MenuSession.HasLaunch && (_matchConfig == null || _matchConfig.UseMenuSelection))
            {
                switch (MenuSession.Current.ModeId)
                {
                    case MenuSelection.ModeTruckChase: return MatchMode.TruckChase;
                    case MenuSelection.ModeRace: return MatchMode.Laps;
                    case MenuSelection.ModeFreeDrive: return MatchMode.FreeDrive;
                }
            }

            return mode;
        }

        /// <summary>The menu's lap count when the menu chose the laps mode; otherwise the config's.</summary>
        private int ResolveLaps()
        {
            bool fromMenu = MenuSession.HasLaunch && (_matchConfig == null || _matchConfig.UseMenuSelection)
                && MenuSession.Current.ModeId == MenuSelection.ModeRace;
            return fromMenu ? MenuSession.Current.Laps : (_matchConfig != null ? _matchConfig.Laps : 3);
        }

        private void Update()
        {
            if (!Built || Driver == null)
                return;

            SyncPedals();

            CarHandlingReadout readout = Driver.ReadHandlingDebug();
            _respawn.Tick(Time.deltaTime, readout.SpeedMetersPerSecond, readout.Throttle, Driver.IsOnMovingGround);
            _bots.Tick(Time.deltaTime);

            KeyCode resetKey = _controlsConfig != null ? _controlsConfig.ResetKey : KeyCode.R;
            bool resetPressed = Input.GetKeyDown(resetKey) || (_hud != null && _hud.Reset.Held);
            if (resetPressed)
                ResetCar();
        }

        /// <summary>The pedal slots have one writer, here, fed by the touch buttons and the keyboard alike.</summary>
        private void SyncPedals()
        {
            bool brake = (_hud != null && _hud.Brake.Held) || (_keyboard != null && _keyboard.BrakeHeld);
            bool reverse = _keyboard != null && _keyboard.ReverseHeld;
            bool nitro = (_hud != null && _hud.Nitro.Held) || (_keyboard != null && _keyboard.NitroHeld);

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
            _bots?.Dispose();
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
