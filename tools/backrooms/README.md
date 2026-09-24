# Backrooms renderer

Procedural (non-AI) pixel art for the hidden Level 0 theme: a tiny Wolfenstein-style
raycaster, quantised to the 22-colour [Backrooms-22](https://lospec.com/palette-list/backrooms-22)
palette with ordered dithering. Needs Python 3 with Pillow and numpy.

| Script | What it does |
|---|---|
| `export.py` | Writes the frames the app ships: `Assets/Backrooms/level0_{dead,dim,lit}.png`, 320x180, one per state of the dead ceiling panel. **Re-run after changing anything below.** |
| `scene.py` | The Level 0 view itself: a hand-laid room framed like the original Backrooms photo, camera turned 28°. `python scene.py [yaw] [out.png]` for a 4x preview. |
| `flicker.py` | Preview GIF of the panel flicker (the app randomises it instead — see `Views/BackroomsView.axaml.cs`). Also holds the panel/yaw `export.py` uses. |
| `render.py` | Random-corridor generator (`python render.py [seed]`), plus the palette and dithering `scene.py` shares. |

The app shows the frames at native size, scaled nearest-neighbour
(`RenderOptions.BitmapInterpolationMode="None"`), so keep exports at 320x180.
