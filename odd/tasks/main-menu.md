# Main menu (pre-Gameplay)
Objective: editable, responsive uGUI main menu scene (Play, mode, settings, car skins, bots, laps + maps). Branch: main (uncommitted). Runner: tools/unity.ps1 EditMode.

## Specs
- S1 "Primero devolveme un brief de layout escrito (zonas, prioridades, estados) y esperá mi OK antes de tocar nada." (done, OK given)
- S2 "Vamos a configurarlo manualmente en unity, crealo como un UI para poder editarlo."
- S3 "La UI debe escalarse en todas las distintas pantallas es decir, se readapte automaticamente."
- S4 "Hace editable, los botones, la ui , las distintas secciones para poder configurar a mi gusto si no me gusta algo."
- S5 "Necesitamos : Boton de Play, seleccion de modo,configuracion,seleccion de skins para el player(distintos autos), distintos huds de configuraciones de bots, y laps con mapas."
- S6 "builder idempotente, textos localizados, animaciones que restauren su estado en OnDisable"
- S7 Localization: lightweight own system, ES + EN (user decision).

## Tasks
- T1 (S6,S7) data + localization + tests - inline - pending verification
- T2 (S2-S5) views + controller - inline - pending verification
- T3 (S2,S4,S6) editor builder, build MainMenu.unity - inline - pending
- T4 (S3) captures 16:9 and 20:9 - pending
- T5 gameplay hookup: MenuSession.MarkLaunched on Play; DriveBootstrap.Build -> MenuLaunch.Apply (car, map, bots, pace); TargetLaps exposed, no lap logic yet - compiled, untested at runtime

## Log
- L1 (user): original request, see Specs.
- Decision: legacy UI Text (no TextMeshPro Essentials in project). Scene not added to Build Settings.
