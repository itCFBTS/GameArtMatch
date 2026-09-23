# App icon

Pixel-art icon: an N64 cartridge that turns, across a diagonal cut, into a vaporwave
sunset (striped sun, pink horizon, neon grid). Drawn entirely in code — no image
editor, no AI. Needs Python 3 with Pillow.

```bash
python tools/icon/build_icon.py
```

writes `Assets/Icon/gameartmatch.ico` (16, 24, 32, 48, 64, 128, 256) and the same
sizes as separate PNGs in `Assets/Icon/png/`, and checks every stored size is
pixel-identical to its design. Windows reads the `.ico`; on Linux the app hands the
PNGs to the window manager itself (`Services/X11WindowHints.cs`), because Avalonia
only passes along one resampled 128px image.

| Size | Design | Script |
|---|---|---|
| 16 | Hand-placed mini cartridge — groove, label corner, sun; no wing notches | `small.py` (`MINI_CART`) |
| 24 | Same family, rule-built so the diagonal is exact | `small24.py` |
| 32 | Full design — wing notches, plastic shading, grid, road | `n64.py` (cartridge) + `vapor.py` (picture) |
| 48 | The 32 enlarged 1.5x (slightly uneven) | — |
| 64/128/256 | The 32 enlarged exactly 2x/4x/8x | — |

Pixel icons don't shrink well, so the small sizes are separate designs, not
downscales — change a detail in one and check the others still match.
`python small.py` / `small24.py` / `vapor.py` also write preview sheets (including a
mock taskbar) to the current directory.
