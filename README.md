# Maze Game 2 – C# port

A C# port of the Scratch project **maze_game_2_engine.sb3**, using

* **raylib** (`Raylib-cs`) – window, input, textures, rendering
* **SkiaSharp 3.119.1** (+ `Svg.Skia` 3.x) – rasterises the SVG/PNG costumes and draws text, HUD, sliders and editor panels
* **OpenAL** (`Silk.NET.OpenAL`, bundled OpenAL Soft) – sound effects and streamed Ogg Vorbis music (`NVorbis` decodes the `.ogg` files)

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
| `tools/tile_ids.py`, `tools/convert_levels.py` | Generate the built-in tile id table / convert old level strings to JSON |
| `tools/extract_assets.py` | Re-extracts costumes/sounds/levels/tile tables from the `.sb3` |

## Game controls

Arrows / WASD move (up = jump, down = crouch, crouch+up = super jump), **E / Enter** use (doors, pipes, NPCs),
Esc / P pause, **F1** fly mode, **F5** restart level, **F11** fullscreen. Every action has two key slots that can be rebound in Settings > Controls.

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

* **Level format** – JSON, tiles and entities by string id (see "Level files" below). The original Scratch
  `mg2ex` strings are converted by `tools/convert_levels.py` (and still imported automatically if you drop a
  `level_NNN.mgl` next to the JSON files). NNN is the original `LEVEL #`; `levels/game.json` holds the start level.
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

## Menus, save slots and settings

The title screen, pink loading screen, pause menu and level-transition wipes use the original project's art.
**Start game** and **Load game** both open the **3 save slots** (`saves/slotN.json`: gems, play time, completed nodes,
script flags, current map/node). An empty slot starts a new game, a used slot continues; slots can be erased.

**Settings** is a tabbed screen: *Graphics* (4:3 / 16:9, window size, fullscreen, VSync, frame limit, FPS counter),
*Sound* (master, music, effects, mute), *Controls* (two keys per action, conflicts are resolved automatically, reset
to defaults) and *Game* (erase saves, reset settings).

**16:9 mode** (1280x720) shows 640 instead of 480 stage units horizontally. The editor stays 4:3.

## Overworlds (`worlds/<id>.json`)

```json
{ "id": "world_1", "name": "Green Hills", "start": "n1",
  "layers": [ { "image": "custom/hills.png", "x": 640, "y": 420, "scale": 5, "parallax": 0.3 } ],
  "nodes": [
    { "id": "n1", "name": "Level 1", "level": "level_002", "x": 120, "y": 520, "links": [] },
    { "id": "n2", "name": "Level 2", "level": "level_003", "x": 250, "y": 360, "requires": ["n1"], "links": ["n1"] },
    { "id": "gate", "name": "To the ruins", "portal": "world_2", "portalNode": "r0", "x": 380, "y": 300, "requires": ["n2"], "links": ["n2"] }
  ] }
```

Nodes are levels or **portals** to other maps. A node unlocks when every node in `requires` is completed (finishing the
level goal completes it and returns to the map). `links` are the paths the cursor can follow. Map layers are images with
a parallax factor. `levels/game.json` has `startLevel` and `startWorld`; with no overworld the game follows each
level's `next` / `left` / `right` ids instead. Levels have a string `id`, a display `name`, plus `layers`
(back / front tile layers and image layers, edited with `L` in the editor).

## Editor additions

* **Any window shape:** the editor window can be resized freely (any aspect ratio); the UI follows the window and dialogs are centred.
* **Map button (overworld editor):** edit `worlds/*.json` visually. Tools `1` Select (drag nodes, Ctrl = free move, Del removes),
  `2` Add (click empty space), `3` Link (click two nodes to link/unlink). The right panel edits the map (id, name, song,
  "start world"), the selected node (id, name, level, portal + arrival node, requirements, position, start node) and the map's
  image layers (image, centre, scale, parallax, order). Guides show the 16:9 and 4:3 screen areas. `Ctrl+S` saves everything.
* **Extra tilesets** in the palette: *Desert*, *Snow & ice*, *Castle* (solid blocks, platforms, ladder, decor, animated torch/gems,
  hazards: cactus, icicles, spikes). Art is generated by `tools/make_tilesets.py`; tiles are registered in `GameContent.cs`.

## Overworld tiles and the two modes

Every map has its own 2D tile grid (`width` x `height` cells of 32 px, `tiles` rows written like level rows, `-` = empty) using a
separate **overworld tileset** (`assets/overworld/`, registered in `GameContent.RegisterOverworldTiles`; 17 tiles: grass,
flowers, sand, snow, hills, dirt path, stone road, bridge, forests, mountain, animated water / deep water / lava, town, castle,
cave). Add your own: `OverworldTileRegistry.Register(new OverworldTileDef("swamp", "Swamp") { Texture = "overworld/swamp.png", Walkable = true, Speed = 0.5 });`.

`"mode"` selects how the map is played:
* **`path`** - the cursor hops between nodes along their `links` (arrow keys / click), `Enter` plays the node.
* **`free`** - a free-roam avatar walks over the tile map (arrows / WASD); non-walkable tiles (water, forest, mountain, lava) block it,
  hills / sand / snow slow it down, roads speed it up. Stand next to a node and press `Enter` to enter it. The camera follows in both axes.

In the editor's **Map** mode: palette on the left, tools `4` Paint (right mouse erases), `5` Fill, `6` Pick, `Ctrl+Z` undo; the right
panel sets the mode, map size, name and so on. "Guides" shows the map border and, in free-roam maps, the blocked tiles in red.
The editor now also copies `worlds/` next to itself, so existing maps show up and can be edited (this was missing before).

## Layers (levels and overworlds share one system)

Level image layers and overworld map layers use the same properties (`ImageLayerProps`): `image`, `x` / `y` (image centre),
`scale`, `parallax` (one number: 1 = moves with the world, 0 = fixed, 0.5 = half speed), `repeat` (`"x"`, `"y"`, `"xy"`) and
`fit` (stretch over the screen). Level layers additionally have a `name`, `kind` (`image` / `tiles`), `depth` (`back` / `front`)
and `visible`. Layer order in the list is the drawing order (later = on top). Both editors edit them with the same rows and
have Move up / Move down. Level image layers are placed in world units (y up), map layers in map pixels (y down).
(Older level files that stored `offset` / `[x, y]` parallax need their image layers re-positioned.)

## Scripting

A level's `script` property is the id of `assets/scripts/<id>.mgs` (source) or `<id>.mgb` (compiled bytecode, preferred).

```
var needed = 3;                                   // global
func welcome() { if (!flag("seen")) { say("Collect " + needed + " gems"); set_flag("seen", true); } }
on start { welcome(); }
on enter "gate_zone" { if (gems() >= needed) { set_tile(10, 4, "air"); play_sound("pop"); } }
on use "lever" { win(); }
```

Events: `start`, `tick`, `death`, `win`, `gem`, and `enter` / `exit` / `use` / `trigger` with an optional id.
Statements: `var`, assignment (`=`, `+=`, `-=`), `if / else`, `while`, `break`, `continue`, `return`, calls.
Operators: `+ - * / %`, comparisons, `&& || !` (strings concatenate with `+`). `wait(seconds)` and `say(text)` pause
only that script. Natives: `say wait wait_ticks print flag set_flag gems give_gems tile set_tile spawn remove_at
teleport player_x player_y kill_player play_sound goto_level win level_id emit random floor abs str len`.
Add your own in code: `ScriptNatives.Register("shake", 1, (ctx, a) => { ...; return Value.Nil; });`.

Entities for scripts: **zone** (`id`, `w`, `h` in cells; invisible in game; fires enter/exit/use), **button** (`id`; use key
fires `use`) and NPCs with an optional `id`. Tools: `MazeGame --compile in.mgs out.mgb` and `MazeGame --disasm file`.
Compile errors are printed to the console with a line number and the script is skipped.

## Animated tiles

Register a tile with `Frames = new[] { "custom/lava_1.png", ... }` and `Fps` (see `GameContent.cs`: `lava`, `gem_ruby`).

## Level files (JSON)

```json
{
  "format": 2, "name": "Level 1", "width": 70, "height": 40,
  "background": "#f0f000", "backdrop": 1, "parallax": true, "underwater": false,
  "camera": 0, "song": "0", "difficulty": 0,
  "tiles": [
    "block_wood*70",
    "block_wood air*68 block_wood",
    "block_wood player_spawn lava*3 air*64 block_wood"
  ],
  "entities": [
    { "type": "npc",    "x": 12, "y": 3, "props": { "text": "hello" } },
    { "type": "door",   "x": 20, "y": 1, "props": { "target": 3 } },
    { "type": "slime",  "x": 30, "y": 1, "props": { "direction": "left" } }
  ]
}
```

* `tiles` lists the rows **top first**; each row is a string of space separated `id` or `id*count` tokens.
  Missing cells are `air`. Unknown ids are kept (and reported on the console) so nothing is lost on save.
* Entities use cell coordinates (`y = 0` is the bottom row) and a free-form `props` object.
* Built-in tile ids are in `src/MazeGame.Core/Generated/BuiltinTiles.g.cs` (e.g. `air`, `block_wood`, `ladder`,
  `gem_1`..`gem_4`, `player_spawn`, `wood_0`..`wood_5`, `grass_1`...). Built-in entities: `walker`, `danger`, `npc`
  (`text`), `end_box`, `piranha`, `door` / `pipe` / `door_wide` (`target` = level number).

## Adding tiles and entities (in code)

Everything custom lives in **`src/MazeGame.Core/Content/GameContent.cs`**. Both the game and the editor call it at
start-up, so new content shows up in the editor palette automatically (in its own section).

```csharp
// a tile: image is a .png/.svg under assets/ (stretched to one cell)
TileRegistry.Register(new TileDef("lava", "Lava", shape: "", category: "Hazards") { Texture = "custom/lava.png", Deadly = true });
TileRegistry.Register(new TileDef("stone_brick", "Stone brick", shape: "#") { Texture = "custom/stone_brick.png" });
TileRegistry.Register(new TileDef("gem_ruby", "Ruby") { Texture = "custom/ruby.png", GemValue = 25 });

// an entity: subclass Entity, override Update, register with a factory and (optional) editor prompts
EntityRegistry.Register(new EntityType("slime", "Slime", (def, editor) => new Slime(def)) { Texture = "custom/slime.png" }
    .Prop("direction", "Start direction (left / right)", "right"));
```

`TileDef` options: `Shape` (`""` walk through, `"#"` solid, `"="` one-way, `"L"` ladder, slopes...), `Deadly`,
`GemValue`, `Group`/`Recipes` (auto-tiling), `Category` (editor section). Entities get `Props` from the level file
(`def.Get("name")`, `def.GetInt(...)`), helpers `PlaceInCell`, `StepWalker` (the walker AI), `TouchesPlayer`, and
`w.KillPlayer()`, `w.PlaySound(..)`. Renaming a tile later? `TileRegistry.Alias("old_id", "new_id")` keeps old levels working.
Custom ids are saved by name, so adding/removing tiles never renumbers existing levels.


## Level states, entity scripts, camera, script editor

**Level state (quick save).** F6 saves and F7 loads the state of the current level (tiles, player, entities, gems, flags,
script variables). One state is kept per level and all states are cleared whenever the overworld map is shown (and on the title screen).
Keys are rebindable in Settings > Controls. Scripts can call `save_state()` / `load_state()`.
Only plain fields (numbers, bools, strings) of custom entities are captured.

**Script runtimes.** Every entity with a `script` prop gets its own script (own variables and tasks); set it in the editor
(Move tool, click an entity). The player runs the level's `PlayerScript` (Level props, page 2) or `playerScript` in `levels/game.json`.
New events: `touch` (player starts overlapping the entity) and `remove`. `self_*` functions act on the entity that owns the script
(or the player in a player script). Positions are in tiles; an entity in cell 5,3 is at 5.5, 3.x.

| Area | Functions |
|---|---|
| self | self_x self_y set_self_pos set_self_speed self_dir set_self_dir self_type self_id self_prop set_self_prop remove_self hide_self show_self self_touching |
| by id | entity_exists entity_x entity_y set_entity_pos move_entity set_entity_speed remove_entity hide_entity show_entity entity_prop set_entity_prop send_entity count_entities(type) spawn_named(type,cx,cy,id) |
| player | player_px player_py set_player_pos set_player_speed player_dir freeze_player(bool) key_down("left"/"right"/"up"/"down"/"z"/"x"/"use") |
| camera | camera_x camera_y set_camera(x,y) camera_pan(x,y,seconds) camera_follow() camera_shake(amount,seconds) camera_zoom(z) |
| state | save_state load_state |

**Editor.** Move tool (5 / M): drag entities to reposition, click one to edit its props, `id` and `script`. Yellow corner marks entities with scripts.
Script button (F4): file list, syntax colours, Ctrl+S save, F5 check (errors show the line), Level script / Player script buttons attach the open script.
See `assets/scripts/patrol.mgs` and `player_camera_demo.mgs`.

## Controllers and audio formats

**Controller** (first connected pad, hot-plug works): D-pad / left stick move, A jump + confirm, X action Z, B action X + back,
Y or right bumper interact, Start pause. Menus, the title screen, slots, overworld and settings are all navigable with it.
Keyboard bindings stay rebindable; controller buttons are fixed (see `src/MazeGame.Runtime/Pad.cs` to change them).

**Audio.** Music (`assets/audio/music/<name>`) and sound effects (`assets/audio/sfx/<name>`) can be `.ogg`, `.mp3` or `.wav`
(WAV: 8/16/24/32-bit PCM or float). The first match is used in the order ogg, mp3, wav. Mono and stereo only.
MP3 decoding uses the NLayer package (1.16+).
