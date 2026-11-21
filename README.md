# Festival Derby — Car Core (FreebutGood)

A small, free Unity 6 template: **a toy car you can drive around a circuit, against bots, with two ways to
win or lose**. It is the car core of a bigger mobile racing game, cut down so it can be read in one
sitting and used as the starting point of the FreebutGood YouTube course.

- Unity **6000.0.81f1**, URP (mobile renderer), works in the Editor with keyboard or touch, and builds for Android.
- A main menu, one gameplay scene, one composition-root script, no services, no paid assets. MIT licensed.

## Game modes

| Mode | How it is won or lost |
|---|---|
| **Race** | Classic laps race against bots. First to drive the chosen number of laps wins. |
| **Truck Chase** | A real truck drives the circuit. Drive up its rear ramp and stay on the bed for **3 seconds** to win. Pass the truck and you must go round another lap before you can board. A countdown knocks out the car furthest from the truck and restarts after every knock-out; the player eliminated loses. |
| **Free Drive** | No rules, just the car and the circuit. |

Tuning for the modes lives in the `Match` config object (`MatchConfig`): board seconds, knock-out
interval, truck speed, laps.

## Open it

1. Install Unity **6000.0.81f1** (Unity Hub).
2. Open this folder as a project. The first import takes a few minutes.
3. Open `Assets/FreebutGood/FestivalDerby/Scenes/MainMenu.unity` and press **Play**, pick a mode and start.
   `Gameplay.unity` can also be opened on its own: it then uses whatever its `Config` objects say.

If a scene is missing, use the **Festival Derby → Menu** and **Festival Derby → Build Drive Scene**
items in the menu bar.

### Controls

| Action | Keyboard | Touch |
|---|---|---|
| Drive (stick points where the car should go) | WASD / arrows | left stick |
| Brake / drift | Space | BRAKE |
| Nitro | Left Shift | NITRO |
| Reverse | Left Ctrl | pull the stick back |
| Back on the track | R | RESET |
| Pause, restart or back to the main menu | Esc (Android: Back) | II |

A car that is stuck, flipped or fallen off the world is put back on the track by itself.

## How it starts

`Gameplay.unity` holds a single object, `Drive`, with `DriveBootstrap`
(`Assets/FreebutGood/FestivalDerby/Scripts/Drive/DriveBootstrap.cs`). In `Start` it:

1. loads the circuit (`MapLoader` → baked `Map_FestivalCircuit`),
2. spawns the car on the start grid (`CarGeometryFactory` + `LocalCarFixedStepDriver` + the Kenney toy model),
3. wires the controls (`MobileDriveControls` fed by `TouchHud` and `KeyboardSteer`),
4. points the chase camera at the car (`LocalChaseCamera`),
5. applies the look (`LookApplier` + `LookProfile_Cozy`),
6. dresses the track (`TracksideDresser` + `TracksideKit`),
7. arms the respawn (`PlayerRespawn`),
8. spawns the bots (`BotField`) and starts the chosen mode (`TruckChaseSession` or `LapRaceSession`).

Everything is tuned from `Resources/DrivingFeatureConfig.asset`.

## What is inside

| Folder (under `Assets/FreebutGood/FestivalDerby/Scripts`) | What it does |
|---|---|
| `Driving/Rules`, `Driving/Handling` | Engine-free driving math: input rules, the V2 handling model, chase framing |
| `Driving/Runtime` | The car in Unity: motor, fixed-step driver, wheel rig, body roll, chase camera, respawn |
| `World` | Baked maps, road mesh, surface grip, trackside dressing, map editor tools |
| `Look` | Lighting, palette, camera and post-processing profile |
| `Bots` | AI cars that follow the racing line |
| `Match` | The rules of the modes: truck chase (truck, boarding, knock-outs) and laps race, with their HUD |
| `Menu` | Main menu, mode/car/map selection and localization |
| `Drive` | The composition root, configs, touch/keyboard controls and the scene builder |

Tests live next to it in `Assets/FreebutGood/FestivalDerby/Tests` (EditMode and PlayMode).

## What was removed from the full game

This branch deliberately drops everything that is not the car: the boot flow and state machine, the
HUD/menu framework, garage, shop, in-app purchases, ads (AdMob, Unity Ads, LevelPlay), analytics and
consent, cloud accounts and cloud save, missions, ranked, championship, the netcode
(LAN, Unity Relay, match host), car damage and deformation, audio and haptics,
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
not listed there is by FreebutGood, under the [MIT license](LICENSE). The audit of what was kept and
what was removed is in `odd/tasks/template-core.md`.
