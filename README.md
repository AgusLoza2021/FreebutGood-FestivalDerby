# Festival Derby — Car Core (FreebutGood)

A small, free Unity 6 project with one thing in it: **a toy car you can drive around a circuit**.
It is the car core of a bigger mobile racing game, cut down so it can be read in one sitting and used
as the starting point of the FreebutGood YouTube course.

- Unity **6000.0.81f1**, URP (mobile renderer), works in the Editor with keyboard or touch.
- One scene, one composition-root script, no menus, no services, no paid assets.

## Open it

1. Install Unity **6000.0.81f1** (Unity Hub).
2. Open this folder as a project. The first import takes a few minutes.
3. Open `Assets/FreebutGood/FestivalDerby/Scenes/Drive.unity` and press **Play**.

If the scene is missing, run **Festival Derby → Build Drive Scene** from the menu bar. It writes the
scene and adds it to the build settings.

### Controls

| Action | Keyboard | Touch |
|---|---|---|
| Drive (stick points where the car should go) | WASD / arrows | left stick |
| Brake / drift | Space | BRAKE |
| Nitro | Left Shift | NITRO |
| Reverse | Left Ctrl | pull the stick back |
| Back on the track | R | RESET |

A car that is stuck, flipped or fallen off the world is put back on the track by itself.

## How it starts

`Drive.unity` holds a single object, `Drive`, with `DriveBootstrap`
(`Assets/FreebutGood/FestivalDerby/Scripts/Drive/DriveBootstrap.cs`). In `Start` it:

1. loads the circuit (`MapLoader` → baked `Map_FestivalCircuit`),
2. spawns the car on the start grid (`CarGeometryFactory` + `LocalCarFixedStepDriver` + the Kenney toy model),
3. wires the controls (`MobileDriveControls` fed by `TouchHud` and `KeyboardSteer`),
4. points the chase camera at the car (`LocalChaseCamera`),
5. applies the look (`LookApplier` + `LookProfile_Cozy`),
6. dresses the track (`TracksideDresser` + `TracksideKit`),
7. arms the respawn (`PlayerRespawn`).

Everything is tuned from `Resources/DrivingFeatureConfig.asset`.

## What is inside

| Folder (under `Assets/FreebutGood/FestivalDerby/Scripts`) | What it does |
|---|---|
| `Driving/Rules`, `Driving/Handling` | Engine-free driving math: input rules, the V2 handling model, chase framing |
| `Driving/Runtime` | The car in Unity: motor, fixed-step driver, wheel rig, body roll, chase camera, respawn |
| `World` | Baked maps, road mesh, surface grip, trackside dressing, map editor tools |
| `Look` | Lighting, palette, camera and post-processing profile |
| `Drive` | The composition root, touch/keyboard controls and the scene builder |

Tests live next to it in `Assets/FreebutGood/FestivalDerby/Tests` (EditMode and PlayMode).

## What was removed from the full game

This branch deliberately drops everything that is not the car: the boot flow and state machine, the
HUD/menu framework, garage, shop, in-app purchases, ads (AdMob, Unity Ads, LevelPlay), analytics and
consent, cloud accounts and cloud save, localization, missions, ranked, championship, the netcode
(LAN, Unity Relay, match host), bots and race modes, car damage and deformation, audio and haptics,
and every paid or unlicensed third-party asset.

## Running Unity from the command line

`tools/unity.ps1` compiles, runs tests or calls an Editor method in batchmode and prints one result
line. See `tools/UNITY_AGENT.md`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action compile
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform PlayMode
```

## Credits and licenses

Third-party content and its licenses are listed in [`CREDITS.md`](CREDITS.md). Code and content
not listed there is by FreebutGood. Before publishing, read the "Publication blockers" section of
`odd/tasks/template-core.md`.
