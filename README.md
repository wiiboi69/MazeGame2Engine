# Maze Game 2 – C# port

A C# port of the Scratch project **maze_game_2_engine.sb3**, using

* **raylib** (`Raylib-cs`) – window, input, textures, rendering
* **SkiaSharp** (+ `Svg.Skia`) – rasterises the SVG/PNG costumes and draws all UI/HUD/menus/editor panels
* **OpenAL** (`Silk.NET.OpenAL`, bundled OpenAL Soft) – sound effects and streamed MP3 music (`NLayer` decodes the MP3)

and a separate **level editor** that shares the same game simulation for play-testing.

> **Not compiled yet.** The environment this was written in had no .NET SDK and blocked NuGet, so the code has
> never been built or run. It was written carefully against the library APIs and the level codec was verified
> against all original levels (see below), but expect to fix a few compile errors on the first `dotnet build`.

## Build & run

Requires the .NET 8 SDK.

```
dotnet build MazeGame.sln -c Release
dotnet run --project src/MazeGame          # the game
dotnet run --project src/MazeGame.Editor   # the level editor
```

Both executables copy `assets/` and `levels/` next to themselves. You can point either at another level folder:
`dotnet run --project src/MazeGame -- path/to/levels`. The editor saves into the folder it was started with
(by default the *output* `levels/` folder; pass `levels` from the repo root to edit the source files).

## Solution layout

| Project | What it is |
|---|---|
| `MazeGame.Core` | Pure logic, no dependencies: level codec, tile tables, player physics, enemies, camera, `World` simulation |
| `MazeGame.Runtime` | raylib + SkiaSharp + OpenAL glue: sprite loading, scene renderer, Skia UI overlay, audio engine, input, settings |
| `MazeGame` | The game: title, settings (with key rebinding), pause, dialogs, wipe transitions, HUD |
| `MazeGame.Editor` | The level editor (`MazeEditor`) |
| `tools/extract_assets.py` | Re-extracts costumes/sounds/levels/tile tables from the `.sb3` |

## Game controls

Arrows / WASD move (up = jump, down = crouch, crouch+up = super jump), **E / Enter** use (doors, pipes, NPCs),
Esc / P pause, **F1** fly mode, **F5** restart level, **F11** fullscreen. Keys can be rebound in Settings.

## Editor controls

* Left click paint, right click erase, middle drag / Space+drag pan, wheel zoom, arrow keys pan
* `1` pencil · `2` rectangle · `3` flood fill · `4`/`E` eyedropper · `G` grid · `Q` auto-tiling · `P` properties
* `Ctrl+S` save · `Ctrl+Z` / `Ctrl+Y` undo/redo · `Ctrl+O` level browser · `F5`/`Tab` play-test (Esc to return)
* Entities (enemies, NPC star, goal globe, piranha plant, doors/pipes) are placed with the same palette; click again to
  remove. Doors/pipes ask for the target level number, the NPC for its text.
* Auto-tiling is a port of the original editor's tile "recipes": wood, pipes, orange blocks, grass and the edge tiles
  pick the right neighbour-aware variant automatically.
* The level browser can create/delete levels and choose which level the game starts on.

## What was ported

* **Level format** – the original `mg2ex` string (`1_70_40_#f0f000_…`, run-length tiles, object list) is read and
  written byte-for-byte; all 10 levels in the project round-trip identically. Files are `levels/level_NNN.mgl`,
  where NNN is the original `LEVEL #` (slot 1 = `gs_2_` start settings).
* **Player** – walking acceleration/skidding, variable jump, crouch, super jump, sliding on slopes with wall kick,
  ladders, swimming (level flag), 1:1 / 1:2 slopes, one-way platforms, step-up logic, camera modes 0-7, gems,
  death animation, edge-of-level transitions into the neighbouring level numbers.
* **Entities** – walker, stompable "danger" enemy (stomp bounce, slide kill), piranha plant, NPC star dialog,
  goal globe (complete → next level), doors / wide doors / pipes to other levels.
* **Rendering** – 480×360 stage rendered at 2× (960×720 virtual screen, letterboxed), parallax backgrounds, per-sprite
  rotation centres, SVG costumes rasterised at 4 texels per unit.
* **Audio** – all original sound effects (the ADPCM coin sound is converted to PCM) and both songs.

## Known differences from the Scratch original

* Logic runs at a fixed 30 ticks/s (the original's 2-frame step at 60 fps); there is no render interpolation.
* `touching` is an axis-aligned box test (Scratch used pixel-perfect costume overlap).
* The original's undefined blocks are kept as no-ops: the `Jump` sound does not exist, and the "bump head" hook
  (`World.BumpIndex`) is never set, so blocks can't flip enemies – the hook is there if you want to add it.
* Not ported: the in-game console, the second "top" level strip above the grid, the logic tiles (74/76 are editor
  markers; 76 just catches the death animation), the original in-game keyboard-driven editor (replaced by the editor project).
* Player spawn keeps the original formula (`y = row*32 + 18 - 32`), so put the spawn marker two tiles above the floor.
