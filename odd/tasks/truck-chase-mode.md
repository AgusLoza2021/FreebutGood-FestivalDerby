# Truck Chase mode (win/lose) + Laps mode
Objective: add the match layer. Mode 1 "Truck Chase": cars chase a real truck, first to ride it 3 s wins, last ones are eliminated by a resetting countdown. Mode 2: classic Laps race.
Branch: feat/truck-chase-mode. Delivery: ask-on-risk. Runner: tools/unity.ps1 (compile, EditMode, PlayMode); Editor must be closed.

## Specs
S1. "ganar y/o perder, es un camion en el que los bots-players intenten subirse para ganar la partida, el primero que suba gana."
S2. "Los ultimos son eliminados por un contador que se reseatea cada que uno muera." (countdown eliminates the last-placed alive car; resets on every elimination; player eliminated = lose)
S3. "si el player o bot adelanta al camion deba dar otra vuelta para volver a intentar subirse, sino no cuenta la victoria." (overtaking the truck cancels boarding until the car is behind it again)
S4. "3 segundos minimo arriba del camion para ganar." (continuous time on the bed)
S5. "Este es el priemr metodo de juego, el segundo es por LAPS tipico de carrera." (second mode: laps race, lap count from MenuSelection.Laps)
S6. User answer: "Fisico real." (truck is a physical vehicle with bed + ramp, cars drive up it)

Assumptions (not asked, all tunable in a config, not in code): elimination interval 20 s; truck speed ~65% of car top speed; bed counts only while the car's chassis touches the bed and speed relative to the truck is small; "behind" is measured by wrapped racing-line progress, the lap wrap re-arms boarding.

## Tasks
- [x] T1 (S2,S3,S4) Engine-free match rules + EditMode tests (BoardingTimer, OvertakeRule, EliminationClock, TruckChaseMatch). Route: inline.
- [x] T2a (S1,S6) Pure truck pieces ported from the old CATCH: TruckSettings, TruckPose/WheelProbe, TruckBed (settled rule), TruckMotion (BakedPath). Route: inline.
- [x] T2b (S1,S6) (compiles against Unity DLLs; runtime behaviour NOT yet exercised in Unity) Unity truck view: kinematic body, box cab/deck/chassis/ramp, IGroundVelocitySource, wheel probe per car (adapt CatchCarrierView). Route: inline.
- [~] T3 (S1) (engine-free brain done + 61 tests; bot hook + Unity glue pending) Bot boarding: bots catch up with the truck tail, climb the ramp, hold the bed. Route: inline.
- [ ] T4 (S1-S4) Match controller wired in DriveBootstrap + HUD (countdown, board timer, win/lose). Route: inline.
- [ ] T5 (S5) Laps mode on the same match seam + menu mode ids. Route: inline.

## Log
L1 (2026-11-21) -Usando thoughtline vamos a crear la ultima mecanica del juego y mas importante.
Ganar y/o perder, es un camion en el que los bots-players intenten subirse para ganar la partida, el primero que suba gana. Los ultimos son eliminados por un contador que se reseatea cada que uno muera.
-Necesitamos que si el player o bot adelanta al camion deba dar otra vuelta para volver a intentar subirse, sino no cuenta la victoria.
-3 segundos minimo arriba del camion para ganar.
-Este es el priemr metodo de juego, el segundo es por LAPS tipico de carrera.
L2 (2026-11-21) Truck question answered: "Fisico real."
L4 (2026-11-21) User: "analiza teniamos los modos de juego ya creados en "C:\Users\Lozita\Projects\FestivalDerby" por si te sirve."
L5 Old project analysis (Assets/GorodiskiGames/Survivalio/Scripts/Derby): MatchModeId {Lap, Timing, Derby, LastSurvivor, Catch}. CATCH already exists and was playtested: CatchMatchMode (dwell ticks, winner, ranking by best dwell then distance to ramp foot), CarrierBed (all 4 wheels grounded + inside bed rect/height band + relative speed <= MaxRelativeSpeed), CarrierMotion (pure function of time along a closed route), CarrierPose/CarBedProbe/ICarrierRoute/PolylineCarrierRoute, CatchSettings.Default (speed 11 m/s, bed 6x2.4 m, height 0.75, ramp 5 m, dwell 3 s, maxRelSpeed 3), CatchCarrierView (kinematic body, box cab/deck/chassis/ramp, IGroundVelocitySource, respawn keep-out 12 m, carrier waits ~45 m ahead of the grid). It does NOT have: bot boarding AI, "overtake = another lap" rule, resetting elimination clock (LastSurvivor eliminates by destruction, not by clock). Decision: port the proven CATCH pieces (engine-free ones into Match.Core, using PathVector3 instead of NetVec3; the view into Match runtime) and layer the new rules on top. T1 stays; its onBed input comes from CarrierBed.IsSettled.
L3 Mapping: no existing match layer. Reuse: BotField/RacingLineBot/RacingLineBrain (Bots), BakedPath.NearestDistance (progress), LapRules (laps), IGroundVelocitySource (moving platform, motor already supports it), PlayerRespawn, MenuSelection.ModeId/Laps. Menu only offers mode "Race" today.
