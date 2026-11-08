# Maze Game 2 - C++17 port

`core/` is a 1:1 port of `src/MazeGame.Core` (physics, entities, world, level codec, script compiler + VM).
It has **no platform includes**; everything platform-specific goes behind a small interface (see below).

## Status
| Part | State |
|---|---|
| `core/` (Player, Entity, World, LevelJson, LevelLibrary, Overworld, Scripting, GameContent) | ported, builds with MSVC |
| Verification | while porting, `core_smoke --trace` was compared against the original C# core (still on the `master` branch): **189 trace lines over all 10 levels, byte-identical**. The C# harness was removed from this branch; `core_smoke` now checks that every level loads, simulates and round-trips |
| `runtime/` (platform interface, SDL2 backend, sprites, scene renderer, UI + text, audio mixer, input, settings, saves) | ported; title, map and gameplay frames checked by screenshot |
| `game/` (Game, SettingsScreen) | ported; audio and gamepad not tested by ear / hand |
| level editor | not ported yet (the C# editor is on `master`) |
| PS4 / PSP / Vita | build; PSP and Vita run in PPSSPP / Vita3K (Vita: native GXM renderer); PS4 reaches the loader but needs real hardware (shadPS4 cannot run the SDK's system libraries) |

## Platform layer
`runtime/Platform.hpp` is the whole porting surface: window, frame, input, textures (`createTexture` / `drawTexture` / `fillRect`) and an audio callback.
`PlatformSDL2.cpp` is the reference implementation. A console port = one more `Platform` subclass + a `cmake/platforms/<name>.cmake`.
SVG art is baked to `*.svg.png` by `tools/BakeSvgs` (dotnet, same Skia rasteriser as the C# game); the C++ runtime only ever loads PNG.

## Build + verify
```
cmake -S cpp -B cpp/build && cmake --build cpp/build --config Release
cpp/build/Release/core_smoke.exe .                     # run from repo root: simulates every level
dotnet run -c Release --project cpp/tools/BakeSvgs -- assets   # once: rasterise SVGs to PNG
cpp/build/Release/maze_game.exe                         # the game (run from the repo root)
cpp/build/Release/core_smoke.exe . --trace > cpp.txt
```

## Porting notes
* `real` (Common.hpp) is `double` like the C#. PSP has a single-precision FPU only: flip to `float` there (physics will drift slightly).
* Level-state saving replaced C# reflection: custom entities with their own mutable fields override `clone()` + `restoreState()` (see `Zone`).
* Files go through `core/Fs.*` (std::filesystem). A console without `<filesystem>` only needs to replace `Fs.cpp`.
* JSON uses nlohmann/json (`third_party/`), the same library ScratchEverywhere uses. Known gap: trailing commas in level JSON are not tolerated (needs nlohmann >= 3.12).
* Script errors use C++ exceptions (compiler/VM). Console toolchains support them; they can be turned into error codes later.
* Hash/dictionary iteration order: entity `props` are a sorted `std::map`, so saved JSON key order within `props` can differ from the C# output.

## PS4 build (written, NOT yet built or run - needs a real test)
Mirrors ScratchEverywhere's PS4 port: pacbrew SDK v0.8.1 + OpenOrbis headers, `orbis-cmake`, SDL2, `orbis_add_pkg`.
```
dotnet run -c Release --project cpp/tools/BakeSvgs -- assets          # bake SVGs first
docker build -f cpp/docker/Dockerfile.ps4 --output=out .              # -> out/maze-game-2-ps4.pkg
```
* `cmake/ps4.cmake` - SDL2 lookup, romfs (assets without SVG/MP3, levels, worlds), `sce_sys/icon0.png`, PKG metadata (title id `MAZE00001`, change it freely).
* `core/Fs.cpp` uses POSIX `dirent`/`stat` when `__PS4__` is set (no `<filesystem>` needed).
* `game/main.cpp` reads from `/app0/romfs/...` and writes saves/settings to `/data/mazegame`. **These paths are the usual homebrew convention - verify on your loader.**
* `PlatformSDL2.cpp` creates one fixed fullscreen window on PS4. The pause menu now also works with up/down/confirm (controller).
* Not done for PS4: the settings screen is still mouse-driven (controller can't change settings yet).
* Things most likely to need a tweak on first build: the SDL2 package name/config in `cmake/ps4.cmake`, the `MODULES` list, and which key the loader maps to the pad.

## Console toolchains (from ScratchEverywhere's `docker/` + `cmake/platforms/`)
* **PSP**: `pspdev/pspdev` Docker image, `psp-cmake`, SDL3 renderer/window/audio, `EBOOT.PBP`.
* **PS4**: Debian + OpenOrbis-style PS4 homebrew SDK 0.8.1 + `orbis-cmake`, SDL2 (or OpenGL core) renderer, `.pkg` via `orbis_add_pkg`.
