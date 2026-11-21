# template-core — Festival Derby car core, public template (FreebutGood)

Objective: a clean, compilable Unity 6 branch with only the car core of Festival Derby, for a free public repo (FreebutGood YouTube course).
Branch: `template/core` (worktree `FestivalDerby-worktrees/template-core`). Delivery: single branch, the owner pushes. Test runner: Unity Test Framework in batchmode (`tools/unity.ps1`).

## Specs

- S1 (owner, verbatim): "NO me interesa,les voy a cambiar el nombre a todo, cambiar la aruqitectura por que saco muchas cosas,solo me quedo con el CORE. en este caso del auto, se va shop,se van menus, se va todo lo demas."
  Keep: car driving (Driving Rules/Handling/Runtime, mobile drive controls + keyboard), chase camera, world/track (World + World.Core, Map_FestivalCircuit, trackside dressing), Look (LookProfile_Cozy, VolumeProfile_Cozy), Kenney toy cars and Racing Kit, and the tests of those modules.
  Remove: the whole template framework (boot, states, HUDs, menus, garage, shop, IAP, ads, analytics, cloud, meta), netcode/match/bots, Relay, template art/fonts/audio, paid assets.
- S2 (owner, verbatim): "Este packete lo subire de template ,si necesitamos cambiar algo hacelo."
  Acceptance: compiles with only the committed packages (0 `error CS`); EditMode + PlayMode tests of the kept modules pass.
- S3 (architecture, from the coordinator): one scene with one small composition-root MonoBehaviour that loads the circuit, spawns the car on the grid, wires controls, chase camera, look and trackside dressing, and offers a reset; decoupled from removed framework types; no dead kill-switches. One PlayMode smoke test opens the scene, drives forward and asserts the car moved and the camera followed.
- S4 (owner, verbatim): "se va a llamar "FreebutGood" el canal no [owner's personal name]." (name redacted: the same request forbids writing it in the branch)
  Acceptance: root folder `Assets/FreebutGood/...` moved with `.meta` files; `companyName` = FreebutGood; README/CREDITS author FreebutGood; no personal name anywhere in the branch.
- S5 (owner, verbatim, earlier): "subir este mismo juego SIMPLE vamos a ir a una BRANCH y armar y hacer un clean-up para solo subir el CORE. EN REPO DE GIT free para el video"
- S6: license/provenance audit under `## Publication blockers`; paid content must not be in the branch.

## Tasks

| ID | Specs | Route | Status | Commit |
|----|-------|-------|--------|--------|
| T1 strip framework, services, netcode, paid assets | S1, S2, S6 | inline (bounded writer) | done | 896275aa |
| T2 rename to FreebutGood + ProjectSettings | S4 | inline | done | 896275aa |
| T3 new Drive composition root, controls, scene builder, smoke test | S3 | inline | done | ae8a02f2 |
| T4 verify: batchmode compile, EditMode, PlayMode | S2, S3 | inline | done (2 pre-existing EditMode failures) | L8 |
| T5 README, CREDITS, AGENTS/CLAUDE | S4, S5 | inline | done | docs commit |

## Publication blockers

Status of every top-level third-party source that was in `Assets/` at the fork point, with the evidence found in the repo.

| Source | Evidence | Classification | In this branch |
|---|---|---|---|
| Original survivor template (`GorodiskiGames/Survivalio`: framework code, UI, fonts, audio, ResourcesStatic) | its README asks buyers to attach "the purchase invoice number" — commercial Asset Store template | MUST NOT publish | removed entirely |
| MoreMountains Feel (`ExternalPackages/Feel`, incl. NiceVibrations) | `license.txt`, Asset Store package | MUST NOT publish (paid) | removed; haptics, feedbacks and camera shake dropped (no replacement needed by the core) |
| AllIn1VfxToolkit (`Plugins/AllIn1VfxToolkit`) | Asset Store package, no open license | MUST NOT publish (paid) | removed with the impact VFX that used it |
| DOTween (`Plugins/Demigiant`) | Demigiant free version, redistribution not granted | UNKNOWN → not needed | removed (only HUDs used it) |
| Joystick pack (`Survivalio/Plugins/Joystick`) | no license file | UNKNOWN | removed; replaced by own `TouchStick` |
| Google Mobile Ads, LevelPlay/IronSource, EDM4U, Unity Ads/IAP/UGS packages | SDKs for removed services | not needed | removed |
| `ExternalPackages/HDR` (ECS_EnemyShader) | survivor shader, no license | UNKNOWN | removed |
| Fonts (DonJose, NotoSansSC) | OFL notice | OK but unused | removed with the HUDs |
| `ThirdParty/Audio` (Kenney audio CC0, OpenGameArt packs, synth) | per-folder LICENSE files | mixed (OGA packs may need attribution) | removed (car audio not in the core) |
| `ThirdParty/FestivalDerby/ArenaProps` | own LICENSE.txt, used by the removed arena map | OK | removed (arena map removed) |
| Meshy AI "realistic" cars (`Art/Cars/*`, 138 MB) | file names `Meshy_AI_*`; no license file; Meshy terms depend on the account plan | UNKNOWN | removed; the toy set is the default |
| Kenney Car Kit, Racing Kit, Toy Car Kit | `License.txt`: CC0 | OK to publish | kept |
| Own code (Driving, World, Look, Drive), maps, look profiles, road surface art, shaders | written for Festival Derby | OK to publish | kept |
| `com.coplaydev.unity-mcp` (UPM git dependency, not vendored) | no license evidence in the repo | verify upstream before publishing | kept in `manifest.json` only |

**Git history (blocker):** this branch descends from the full private history, which still contains the paid assets, the commercial template, private agent memory files and commit metadata with the owner's personal name. Publish it as a fresh history (for example an orphan branch with one squashed commit, authored with the FreebutGood identity), never by pushing this branch with its ancestors.

Remaining owner decisions: confirm the `com.coplaydev.unity-mcp` license upstream; confirm the bundle id `com.freebutgood.festivalderby`.

## Log

- L1 (owner request, verbatim): "Mientras manda otro sub-agente armar el branch. Reducir los scritps que son externos como shop y todo eso en el branch. Este packete lo subire de template ,si necesitamos cambiar algo hacelo. cambiemos los nombre de gorodiski. Ponele mi nombre y deja todo listo"
- L2: worktree created from 84cbebe7 (feat/catch-core). HEAD did not compile: the Relay asmdef needs packages never committed.
- L3 (owner scope change, verbatim): "NO me interesa,les voy a cambiar el nombre a todo, cambiar la aruqitectura por que saco muchas cosas,solo me quedo con el CORE. en este caso del auto, se va shop,se van menus, se va todo lo demas."
- L4 (owner rename correction, verbatim): "se va a llamar "FreebutGood" el canal no [owner's personal name]." (name redacted: the same request forbids writing it in the branch)
- L5: re-planned for the car core. The new entry point reuses the DriveLab pattern (everything built in code from one MonoBehaviour); DriveLab's own controls were ported as `KeyboardSteer`, `TouchStick`, `HoldButton`, `SteerRouter` so the third-party joystick and TextMeshPro are not needed.
- L6: decoupling inside the car core: damage-zone handling (`ApplyZoneHandling`), match-seat following and race checkpoints were removed from the motor, driver and respawn; the config lost the switches of removed features (track layout, launch smoke, snow ruts, deformation, impact VFX, part detach, reverse button, bots).
- L7: Git LFS rules for `Art/` dropped: only ~2 MB of art remains, and a public course repo should clone with plain git.
- L8 (verification, Unity 6000.0.81f1 batchmode on the worktree): compile 0 `error CS`, 0 `warning CS`. EditMode 929 tests: 927 passed, 2 failed — `TrackLayoutDefinitionEditModeTests.ShippedBigOvalAsset_ImportsAndResolvesToTheDocumentedDimensions` and `LookProfileEditModeTests.ShippedProfile_CarriesTheCameraGroupTheCodeDefaultsDescribe`; both pre-existing (their test file and asset are byte-identical to 84cbebe7), recorded in `tools/known-test-failures.txt`. PlayMode 180 tests: 179 passed, 1 skipped (a manual capture test), 0 failed; the smoke test drove the car 35.6 m in 4 s with the camera travelling 35.9 m.
- L9: the first PlayMode run failed 35 tests because the smoke test left the Drive scene (a whole circuit at the origin) loaded under every later test; its teardown now swaps in an empty scene.
