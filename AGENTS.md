# AGENTS.md — Festival Derby Car Core

Guide for AI coding agents (and people) working in this repository.

## What this is

A Unity 6000.0.81f1 URP project that contains only the car core of Festival Derby: one toy car, one
baked circuit, a chase camera, a look profile, touch/keyboard controls, bots, a main menu and two
match modes (truck chase, laps). See `README.md` for the
player-facing overview.

## The one flow

`Assets/FreebutGood/FestivalDerby/Scenes/MainMenu.unity` → `Gameplay.unity` → `DriveBootstrap.Start()` → load map → spawn
car → controls → chase camera → look → trackside dressing → respawn → bots → match mode. If a change breaks that flow
(the car cannot drive, the camera does not follow), it is a bug even if it compiles. The PlayMode test
`DriveSceneSmokeTests` checks exactly this.

## Assemblies

| Assembly | Folder | Depends on |
|---|---|---|
| `FestivalDerby.Driving.Rules` | `Scripts/Driving/Rules` | nothing (engine-free) |
| `FestivalDerby.Driving.Handling` | `Scripts/Driving/Handling` | Rules |
| `FestivalDerby.World.Core` | `Scripts/World/Core` | nothing |
| `FestivalDerby.World` | `Scripts/World` | World.Core |
| `FestivalDerby.Driving.Runtime` | `Scripts/Driving/Runtime` | Rules, Handling, World, World.Core |
| `FestivalDerby.Look` | `Scripts/Look` | URP |
| `FestivalDerby.Bots.Core`, `FestivalDerby.Bots` | `Scripts/Bots` | Rules, World |
| `FestivalDerby.Match.Core` (engine-free), `FestivalDerby.Match` | `Scripts/Match` | World.Core, Bots, Driving.Runtime |
| `FestivalDerby.Menu` | `Scripts/Menu` | Bots |
| `FestivalDerby.Drive` | `Scripts/Drive` | all of the above |
| `FestivalDerby.World.Editor`, `FestivalDerby.Drive.Editor` | `Editor` folders | Editor only |

Keep the arrows pointing this way: engine-free rules at the bottom, the composition root at the top.

## Rules

- Physics of the car runs in `FixedUpdate` through `LocalCarFixedStepDriver`; input is sampled in
  `Update` and handed over, never applied directly to the body.
- No allocations in per-frame or per-tick code (`new`, LINQ, string building, closures).
- Tuning lives in `Resources/DrivingFeatureConfig.asset` and the profile assets, not in code.
- Comments explain why, not what.
- Unity assets move together with their `.meta` files, or their GUIDs (and every reference) break.
- Render pipeline: only `Assets/Settings/Mobile_RPAsset` + `Mobile_Renderer`.

## Verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action compile
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform PlayMode
```

Close the Editor first: batchmode cannot open a project an Editor already has open.

## Editor traps

- An open Editor may not notice a brand-new `.cs` file until it recompiles; restart it if a new type
  is "missing".
- With the Editor in the background the player loop stops; tests and tools that need frames set
  `Application.runInBackground = true`.
