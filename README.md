## this was made with claude ai bro
# Maze Game 2 - C++ anywhere

A C++17 port of the Scratch project **maze_game_2_engine.sb3** that runs on PC, PS4, PSP and PS Vita from one code base,
built the same way as [ScratchEverywhere](https://github.com/ScratchEverywhere/ScratchEverywhere): shared game code,
one small platform backend per machine, Docker toolchain images for the consoles.

The original C# version lives on the `master` branch. This branch has no C# game source.

| Target | Output | How it is built |
|---|---|---|
| PC (Windows / Linux) | `maze_game(.exe)` | CMake + SDL2 (fetched), MSVC / GCC |
| PS4 | `maze-game-2-ps4.pkg` | Docker: pacbrew SDK + OpenOrbis headers, `orbis-cmake` |
| PSP | `maze-game-2-psp.zip` (`MazeGame2/EBOOT.PBP`) | Docker: `pspdev/pspdev`, `psp-cmake`, SDL2 |
| PS Vita | `maze-game-2-vita.vpk` | Docker: `vitasdk/vitasdk`, SDL2 (native GXM renderer) |

## Build (Windows)
```
build_pc.bat [run]      desktop game          -> cpp\build\Release\maze_game.exe
build_ps4.bat           PS4 package           -> out\maze-game-2-ps4.pkg
build_psp.bat           PSP game folder zip   -> out\maze-game-2-psp.zip
build_vita.bat          Vita package          -> out\maze-game-2-vita.vpk
build_all.bat           everything
```
Needs CMake + Visual Studio C++ tools (PC), Docker Desktop (consoles) and the .NET 8 SDK once, to bake the SVG art to PNG
(`REBAKE=1` forces a re-bake). On GitHub the same builds run from `.github/workflows/nightly-*.yml`.

## Layout
```
cpp/core/       game logic, no platform code (physics, entities, world, levels, script compiler + VM)
cpp/runtime/    Platform.hpp (what a console must implement), SDL2 backend, sprites, renderer, UI, audio mixer, input
cpp/game/       the game itself: title, slots, map, loading, pause, dialog, settings
cpp/cmake/      per-platform CMake (ps4, psp, vita)     cpp/docker/   toolchain images
cpp/tools/      BakeSvgs (SVG -> PNG at the right size per platform)
cpp/tests/      core_smoke: loads and simulates every level, runs the script VM
assets/ levels/ worlds/   game data (levels are JSON)
tools/*.py      scripts that extracted the original assets / levels from the .sb3
```
More detail, platform notes and known gaps: [cpp/README.md](cpp/README.md).
