# Kenney Car Kit (CC0)

Low-poly car models imported from the Kenney **Car Kit** (version 3.1).

- Source: https://kenney.nl/assets/car-kit
- License: **CC0 1.0** (Creative Commons Zero) — public domain, no attribution required.
  See `License.txt` for the full text.
- Author: Kenney (https://kenney.nl)

## What is inside

| Folder | Contents |
|---|---|
| `Models/` | The car bodies we use (`sedan`, `sedan-sports`, `hatchback-sports`, `suv`, `suv-luxury`, `van`, `delivery`, `taxi`, `police`, `truck`, `race`, `ambulance`), the four wheels we use (`wheel-default`, `wheel-dark`, `wheel-racing`, `wheel-truck`), the crash `debris-*` pieces and the `cone`/`box` props. |
| `Textures/` | `colormap.png`, the single palette all kit models UV into. |
| `Materials/` | `CarKit.mat`, the one shared URP material every Toy prefab renderer uses. |
| `License.txt` | The upstream CC0 license text. |

Flat-shaded, untextured low-poly: every model reads the same palette texture through UVs, which
keeps the material count at one and makes the kit cheap on mobile. The car prefabs built on top of
these models live under
`Assets/FreebutGood/FestivalDerby/Prefabs/Cars/Toy/`.
