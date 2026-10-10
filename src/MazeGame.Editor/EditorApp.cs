using System.Numerics;
using MazeGame.Core;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Editor;

public enum Tool { Pencil, Rect, Fill, Pick, Move }
public enum Modal { None, Open, Props, Layers, Prompt, Entity }

/// <summary>
/// Level editor: tile / entity palette, pencil + rectangle + fill + picker tools, auto-tiling, undo / redo,
/// level properties, level browser and a play-test that runs the real game simulation.
/// </summary>
public sealed partial class EditorApp : IDisposable
{
    // layout (virtual 960x720 screen)
    private const int PalW = 216, TopH = 44, BotH = 28;
    private const int CellPx = 38;

    private readonly AppWindow _win;
    private readonly SpriteLibrary _sprites;
    private readonly SceneRenderer _scene;
    private readonly SkiaUi _ui;
    private readonly AudioEngine _audio;
    private readonly Settings _settings = new();
    private readonly GameInput _input;
    private readonly LevelLibrary _lib;

    private string _levelId = "";
    private int _propsPage;
    private int _selLayer = -1;          // layer selected in the layers panel (-1 = main)
    private LevelData _level = new(70, 40);
    private bool _unsaved;
    private readonly Stack<LevelData> _undo = new(), _redo = new();

    private int _brush = TileInfo.DefaultSolid;
    private Tool _tool = Tool.Pencil;
    private bool _auto = true;
    private bool _grid = true;
    private double _zoom = 1.0;
    private Vector2 _lastMouse;
    private bool _stroke;
    private bool _strokeErase;
    private (int x, int y) _rectStart;
    private int _lastPaintX = -1, _lastPaintY = -1;
    private float _paletteScroll;
    private string _message = "";
    private float _messageTime;

    private Modal _modal = Modal.None;
    private float _openScroll;
    private string _promptTitle = "", _promptValue = "";
    private bool _promptNumeric;
    private Action<string>? _promptOk;
    private Action? _promptCancel;

    // play test
    private World? _test;
    private double _acc;
    private string? _testDialog;
    private bool _quit;

    private readonly List<(string title, int[] ids)> _sections = new();

    public EditorApp(string assetsDir, string levelsDir)
    {
        _assetsDir = assetsDir;
        _win = new AppWindow("Maze Game 2 - Level Editor", false);
        Raylib.SetWindowMinSize(960, 600);
        _sprites = new SpriteLibrary(assetsDir);
        _scene = new SceneRenderer(_sprites);
        _ui = new SkiaUi();
        _audio = new AudioEngine(Path.Combine(assetsDir, "audio")) { MusicVolume = 0.4f, SfxVolume = 0.8f };
        _input = new GameInput(_settings);
        _lib = LevelLibrary.LoadDirectory(levelsDir);
        string root = Path.GetDirectoryName(Path.GetFullPath(levelsDir.TrimEnd('/', '\\'))) ?? ".";
        _owLib = OverworldLibrary.Load(Path.Combine(root, "worlds"));
        BuildPalette();

        string? first = _lib.Ids.FirstOrDefault();
        if (first == null)
        {
            _levelId = _lib.NextFreeId();
            _level = LevelData.CreateBlank(70, 40);
            _level.Name = "New level";
            _lib.Set(_levelId, _level);
        }
        else
        {
            OpenLevel(first);
        }
        CenterOnLevel();
    }

    // ================================================================ palette

    private void BuildPalette()
    {
        // built-in sections keep the original order, sections created by GameContent.cs follow
        var order = new List<string>
        {
            "Wood & pipes", "Blocks, ladders, clouds", "Orange blocks", "Grass & slopes", "Edge tiles",
            "Gems & player spawn", "Entities", "Doors, pipes & logic", "Other",
        };
        foreach (var t in TileRegistry.All)
            if (t.Num >= 3 && !order.Contains(t.Category)) order.Add(t.Category);

        foreach (var title in order)
        {
            var ids = TileRegistry.All.Where(t => t.Num >= 3 && t.Category == title && !t.Missing)
                .OrderBy(t => t.Num == TileInfo.PlayerSpawn ? 1 : 0)          // spawn marker last in its section
                .Select(t => t.Num).ToList();
            if (ids.Count > 0) _sections.Add((title, ids.ToArray()));
        }
    }

    private static string TileName(int id) => TileRegistry.ByNum(id)?.Name ?? "tile " + id;

    private static double TileScale(int id, Sprite s)
    {
        var d = TileRegistry.ByNum(id);
        return d?.Texture != null || d?.Frames != null ? 32.0 / s.SrcW : 2.0;
    }

    // ================================================================ level management

    private void CommitToLibrary()
    {
        if (_levelId.Length > 0) _lib.Set(_levelId, _level);
    }

    private void OpenLevel(string id)
    {
        if (_levelId.Length > 0 && _level.Tiles.Length > 0) CommitToLibrary();
        var l = _lib.Get(id);
        if (l == null) return;
        _levelId = id;
        _level = l.Clone();
        _level.EditLayer = 0;
        _selLayer = -1;
        _undo.Clear();
        _redo.Clear();
        CenterOnLevel();
    }

    /// <summary>Gives the open level a new id (and file name). Other levels that point at the old id are not changed.</summary>
    private void RenameLevel(string newId)
    {
        newId = LevelLibrary.Sanitize(newId);
        if (newId == _levelId || newId.Length == 0) return;
        if (_lib.Has(newId)) { Say($"A level called '{newId}' already exists"); return; }
        string old = _levelId;
        _lib.Remove(old);
        _levelId = newId;
        CommitToLibrary();
        _unsaved = true;
        Say($"Renamed {old} -> {newId} (update doors that pointed at it)");
    }

    private void CenterOnLevel()
    {
        var sp = _level.FindSpawn();
        _scene.CenterX = sp.HasValue ? sp.Value.x * 32 + 16 : 480;
        _scene.CenterY = sp.HasValue ? sp.Value.y * 32 + 16 : 200;
    }

    private void SaveAll()
    {
        CommitToLibrary();
        _lib.SaveAll();
        SaveWorldsIfDirty();
        _unsaved = false;
        Say($"Saved to {_lib.Directory}");
    }

    private void Say(string msg) { _message = msg; _messageTime = 3.5f; }

    private void PushUndo()
    {
        _undo.Push(_level.Clone());
        if (_undo.Count > 100) { var arr = _undo.Reverse().Skip(1).ToArray(); _undo.Clear(); foreach (var a in arr) _undo.Push(a); }
        _redo.Clear();
        _unsaved = true;
    }

    private void DoUndo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_level.Clone());
        int el = _level.EditLayer;
        _level = _undo.Pop();
        _level.EditLayer = el;
        _unsaved = true;
    }

    private void DoRedo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_level.Clone());
        int el = _level.EditLayer;
        _level = _redo.Pop();
        _level.EditLayer = el;
        _unsaved = true;
    }

    // ================================================================ main loop

    public void Run()
    {
        while (!_win.ShouldClose && !_quit)
        {
            Pad.Update();
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _audio.Update();
            if (_messageTime > 0) _messageTime -= dt;
            _scene.AnimTime += dt;

            if (_win.SyncToWindow()) _ui.Resize(AppWindow.VW, AppWindow.VH);
            if (_test != null) UpdateTest(dt);
            else if (_scriptMode) UpdateScript(dt);
            else if (_worldMode) UpdateWorld(dt);
            else UpdateEdit(dt);
            Draw();
        }
        CommitToLibrary();
        SaveWorldsIfDirty();
    }

    private static bool Ctrl => Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
    private static bool Shift => Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

    // ================================================================ editing input

    private (int x, int y) CellAt(Vector2 m) =>
        ((int)Math.Floor(_scene.WorldX(m.X) / 32), (int)Math.Floor(_scene.WorldY(m.Y) / 32));

    private bool InCanvas(Vector2 m) => m.X >= PalW && m.Y >= TopH && m.Y < AppWindow.VH - BotH;

    private EntityDef? EntityAt(int x, int y) => _level.Entities.FirstOrDefault(e => e.X == x && e.Y == y);

    private void UpdateEdit(float dt)
    {
        var m = _win.Mouse;
        if (_modal == Modal.Prompt) { UpdatePrompt(); _lastMouse = m; return; }
        if (_modal != Modal.None)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _modal = Modal.None;
            _lastMouse = m;
            return;
        }

        // hotkeys
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) SaveAll();
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) { if (Shift) DoRedo(); else DoUndo(); }
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) DoRedo();
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.O)) _modal = Modal.Open;
        if (!Ctrl)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.One)) _tool = Tool.Pencil;
            if (Raylib.IsKeyPressed(KeyboardKey.Two)) _tool = Tool.Rect;
            if (Raylib.IsKeyPressed(KeyboardKey.Three)) _tool = Tool.Fill;
            if (Raylib.IsKeyPressed(KeyboardKey.Four) || Raylib.IsKeyPressed(KeyboardKey.E)) _tool = Tool.Pick;
            if (Raylib.IsKeyPressed(KeyboardKey.Five) || Raylib.IsKeyPressed(KeyboardKey.M)) _tool = Tool.Move;
            if (Raylib.IsKeyPressed(KeyboardKey.F4)) EnterScriptMode();
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _grid = !_grid;
            if (Raylib.IsKeyPressed(KeyboardKey.Q)) { _auto = !_auto; Say("Auto-tiling " + (_auto ? "on" : "off")); }
            if (Raylib.IsKeyPressed(KeyboardKey.P)) _modal = Modal.Props;
            if (Raylib.IsKeyPressed(KeyboardKey.L)) _modal = Modal.Layers;
            if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) CycleEditLayer(-1);
            if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) CycleEditLayer(1);
            if (Raylib.IsKeyPressed(KeyboardKey.F5) || Raylib.IsKeyPressed(KeyboardKey.Tab)) BeginTest();
        }

        // keyboard pan
        double pan = 700 * dt / _zoom / 2;
        if (Raylib.IsKeyDown(KeyboardKey.Left)) _scene.CenterX -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) _scene.CenterX += pan;
        if (Raylib.IsKeyDown(KeyboardKey.Up)) _scene.CenterY += pan;
        if (Raylib.IsKeyDown(KeyboardKey.Down)) _scene.CenterY -= pan;

        float wheel = Raylib.GetMouseWheelMove();
        if (m.X < PalW && m.Y > TopH)
        {
            _paletteScroll = Math.Max(0, _paletteScroll - wheel * 60);
        }
        else if (InCanvas(m))
        {
            if (wheel != 0) ZoomAt(m, wheel > 0 ? 1.15 : 1 / 1.15);

            bool panning = Raylib.IsMouseButtonDown(MouseButton.Middle) ||
                           (Raylib.IsKeyDown(KeyboardKey.Space) && Raylib.IsMouseButtonDown(MouseButton.Left));
            if (panning)
            {
                var d = m - _lastMouse;
                _scene.CenterX -= d.X / _scene.Ppu;
                _scene.CenterY += d.Y / _scene.Ppu;
            }
            else
            {
                HandleTools(m);
            }
        }
        else if (_stroke && !Raylib.IsMouseButtonDown(MouseButton.Left) && !Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            _stroke = false;
        }
        if (!Raylib.IsMouseButtonDown(MouseButton.Left)) _dragEnt = null;
        _lastMouse = m;
    }

    private void ZoomAt(Vector2 m, double factor)
    {
        double wx = _scene.WorldX(m.X), wy = _scene.WorldY(m.Y);
        _zoom = Math.Clamp(_zoom * factor, 0.2, 4.0);
        _scene.Ppu = _zoom;
        _scene.CenterX = wx - (m.X - AppWindow.VW / 2.0) / _zoom;
        _scene.CenterY = wy + (m.Y - AppWindow.VH / 2.0) / _zoom;
    }

    private void HandleTools(Vector2 m)
    {
        var (cx, cy) = CellAt(m);
        bool lmbPressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rmbPressed = Raylib.IsMouseButtonPressed(MouseButton.Right);
        bool lmbDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        bool rmbDown = Raylib.IsMouseButtonDown(MouseButton.Right);
        bool lmbReleased = Raylib.IsMouseButtonReleased(MouseButton.Left);
        bool rmbReleased = Raylib.IsMouseButtonReleased(MouseButton.Right);

        if (_tool == Tool.Move)
        {
            HandleMove(m, cx, cy, lmbPressed, lmbDown, lmbReleased);
            return;
        }

        if (_tool == Tool.Pick)
        {
            if (lmbPressed && _level.InBounds(cx, cy))
            {
                var e = EntityAt(cx, cy);
                _brush = e != null ? BrushOf(e.Type) : _level.Get(cx, cy);
                if (_brush <= TileInfo.Air) _brush = TileInfo.DefaultSolid;
                _tool = Tool.Pencil;
            }
            return;
        }

        if (_tool == Tool.Fill)
        {
            if (lmbPressed && _level.InBounds(cx, cy) && !TileInfo.IsEntityBrush(_brush))
            {
                PushUndo();
                FloodFill(cx, cy, _brush);
            }
            else if (rmbPressed && _level.InBounds(cx, cy))
            {
                PushUndo();
                FloodFill(cx, cy, TileInfo.Air);
            }
            return;
        }

        if (_tool == Tool.Rect)
        {
            if ((lmbPressed || rmbPressed) && !_stroke)
            {
                _stroke = true;
                _strokeErase = rmbPressed;
                _rectStart = (cx, cy);
            }
            if (_stroke && ((lmbReleased && !_strokeErase) || (rmbReleased && _strokeErase)))
            {
                _stroke = false;
                PushUndo();
                int x0 = Math.Min(_rectStart.x, cx), x1 = Math.Max(_rectStart.x, cx);
                int y0 = Math.Min(_rectStart.y, cy), y1 = Math.Max(_rectStart.y, cy);
                int tile = _strokeErase || TileInfo.IsEntityBrush(_brush) ? TileInfo.Air : _brush;
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        if (_level.InBounds(x, y)) { _level.Set(x, y, tile); if (_strokeErase) RemoveEntityAt(x, y); }
                if (_auto) for (int x = x0 - 1; x <= x1 + 1; x++) for (int y = y0 - 1; y <= y1 + 1; y++) AutoTile.Fix(_level, x, y);
            }
            return;
        }

        // pencil
        bool entityBrush = TileInfo.IsEntityBrush(_brush) && _level.EditLayer == 0;
        if (lmbPressed || rmbPressed)
        {
            PushUndo();
            _stroke = true;
            _strokeErase = rmbPressed;
            _lastPaintX = _lastPaintY = -1;
        }
        if (_stroke && (lmbDown || rmbDown))
        {
            if ((cx, cy) != (_lastPaintX, _lastPaintY) && _level.InBounds(cx, cy))
            {
                bool first = _lastPaintX == -1;
                _lastPaintX = cx; _lastPaintY = cy;
                if (_strokeErase)
                {
                    if (_level.EditLayer != 0 || !RemoveEntityAt(cx, cy)) { _level.Set(cx, cy, TileInfo.Air); if (_auto) AutoTile.FixAround(_level, cx, cy); }
                }
                else if (entityBrush)
                {
                    if (first) PlaceEntity(cx, cy);
                }
                else
                {
                    PaintTile(cx, cy, _brush);
                }
            }
        }
        else
        {
            _stroke = false;
        }
    }

    private void PaintTile(int x, int y, int tile)
    {
        if (TileInfo.IsEntityBrush(tile)) return;
        if (_level.EditLayer != 0 && TileInfo.IsEditorOnly(tile)) { Say("Markers can only go on the main layer"); return; }
        if (tile == TileInfo.PlayerSpawn)
            for (int i = 0; i < _level.Tiles.Length; i++)
                if (_level.Tiles[i] == TileInfo.PlayerSpawn) _level.Tiles[i] = TileInfo.Air;
        _level.Set(x, y, tile);
        if (_auto && TileInfo.Group(tile).Length > 0) AutoTile.FixAround(_level, x, y);
    }

    private bool RemoveEntityAt(int x, int y) => _level.Entities.RemoveAll(e => e.X == x && e.Y == y) > 0;

    private static int BrushOf(string entityType)
    {
        var t = EntityRegistry.Find(entityType);
        return t == null ? TileInfo.DefaultSolid : TileRegistry.NumOf(t.BrushTileId);
    }

    private void PlaceEntity(int x, int y)
    {
        var existing = EntityAt(x, y);
        if (existing != null) { _level.Entities.Remove(existing); return; }
        string? typeId = TileRegistry.ByNum(_brush)?.Entity;
        var type = typeId == null ? null : EntityRegistry.Find(typeId);
        if (type == null) return;
        var def = new EntityDef { X = x, Y = y, Type = type.Id };
        _level.Entities.Add(def);
        PromptProp(def, type, 0);
    }

    /// <summary>Asks for the entity's settings one after the other (see <see cref="EntityType.Props"/>).</summary>
    private void PromptProp(EntityDef def, EntityType type, int index)
    {
        if (index >= type.Props.Count) return;
        var p = type.Props[index];
        string title = p.Prompt, initial = p.Default;
        if (p.Kind == PropKind.Level)
        {
            var ids = _lib.Ids.ToList();
            initial = ids.Count > 0 ? (ids.SkipWhile(i => i != _levelId).Skip(1).FirstOrDefault() ?? ids[0]) : "";
            title = $"{p.Prompt} (e.g. {string.Join(", ", ids.Take(4))})";
        }
        bool digitsOnly = p.Kind == PropKind.Int;
        OpenPrompt(title, initial, digitsOnly, v =>
        {
            v = (v ?? "").Trim();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool valid = p.Kind switch
            {
                PropKind.Level => v.Length > 0,
                PropKind.Int => int.TryParse(v, out _),
                PropKind.Number => double.TryParse(v, System.Globalization.NumberStyles.Float, inv, out _),
                _ => true,
            };
            if (v.Length == 0 || !valid)
            {
                if (p.Required) { _level.Entities.Remove(def); return; }
                v = valid ? "" : p.Default;
            }
            if (v.Length > 0) def.Props[p.Name] = v;
            PromptProp(def, type, index + 1);
        }, () =>
        {
            if (p.Required) _level.Entities.Remove(def);
            else PromptProp(def, type, index + 1);
        });
    }

    private void FloodFill(int sx, int sy, int newTile)
    {
        int old = _level.Get(sx, sy);
        if (old == newTile) return;
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (!_level.InBounds(x, y) || _level.Get(x, y) != old) continue;
            _level.Set(x, y, newTile);
            stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }
        if (newTile == TileInfo.PlayerSpawn) Say("Fill with spawn marker is not allowed");
    }

    // ================================================================ prompt

    private void OpenPrompt(string title, string initial, bool numeric, Action<string> ok, Action? cancel = null)
    {
        _promptTitle = title; _promptValue = initial; _promptNumeric = numeric; _promptOk = ok; _promptCancel = cancel;
        _modal = Modal.Prompt;
        while (Raylib.GetCharPressed() > 0) { }     // swallow keys typed before the prompt opened
    }

    private void UpdatePrompt()
    {
        int ch;
        while ((ch = Raylib.GetCharPressed()) > 0)
        {
            if (_promptNumeric && (ch < '0' || ch > '9')) continue;
            if (_promptValue.Length < 60) _promptValue += char.ConvertFromUtf32(ch);
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && _promptValue.Length > 0)
            _promptValue = _promptValue.Substring(0, _promptValue.Length - 1);
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            _modal = Modal.None;
            _promptOk?.Invoke(_promptValue);
            _unsaved = true;
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _modal = Modal.None;
            if (_promptCancel != null) _promptCancel();
            else _promptOk?.Invoke(_promptNumeric ? "" : "hello");
        }
    }

    // ================================================================ play test

    private void BeginTest()
    {
        CommitToLibrary();
        var level = _level.Clone();
        string testId = _levelId;
        _test = new World(level, testId, n => n == testId ? level : _lib.Get(n));
        _test.SoundRequested += n => _audio.PlaySfx(n);
        _test.DialogRequested += t => _testDialog = t;
        _testDialog = null;
        _acc = 0;
        _input.Reset();
        _audio.PlayMusic("my_song_68");
    }

    private void EndTest()
    {
        _test = null;
        _audio.StopMusic();
    }

    private void UpdateTest(float dt)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.F5) || Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            if (_testDialog != null) _testDialog = null; else { EndTest(); return; }
        }
        if (_test == null) return;
        if (Raylib.IsKeyPressed(KeyboardKey.F1)) _test.GodMode = !_test.GodMode;
        if (_testDialog != null)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Enter)) { _testDialog = null; _input.Reset(); }
            return;
        }
        _acc += dt;
        while (_acc >= 1.0 / GameConstants.TicksPerSecond && _test != null)
        {
            _acc -= 1.0 / GameConstants.TicksPerSecond;
            _test.Tick(_input.Poll());
            if (_test.Pending is { } req)
            {
                var lvl = _lib.Get(req.Level);
                if (req.Win) { Say("Level complete!"); EndTest(); return; }
                if (req.Level == _levelId || lvl == null) _test.Load(_test.Level, _test.LevelId, LoadKind.Respawn, 0);
                else _test.Load(lvl, req.Level, req.Kind, req.EntrySide);
                _input.Reset();
            }
        }
    }

    // ================================================================ drawing

    private static SKColor C(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);

    private void Draw()
    {
        _win.BeginVirtual();
        var m = _win.Mouse;
        if (_test != null)
        {
            DrawTest();
        }
        else if (_scriptMode)
        {
            DrawScriptScreen(m);
        }
        else if (_worldMode)
        {
            DrawWorldScreen(m);
        }
        else
        {
            DrawCanvas(m);
            _ui.BeginFrame();
            DrawPanelsBackground();
            _ui.EndFrame();
            DrawPaletteIcons();
            _ui.BeginFrame();
            DrawPanelsText(m);
            DrawModals(m);
            _ui.EndFrame();
        }
        _win.EndVirtual();
    }

    private void DrawTest()
    {
        if (_test == null) return;
        double cx = _scene.CenterX, cy = _scene.CenterY;
        _scene.DrawWorld(_test);
        _scene.CenterX = cx; _scene.CenterY = cy;     // DrawWorld follows the player camera; restore the editor view
        _scene.Ppu = _zoom;
        _ui.BeginFrame();
        _ui.Rect(12, 12, 360, 44, C(0, 0, 0, 130), 10);
        _ui.Text($"TEST PLAY   gems {_test.Coins}", 26, 42, 24, SKColors.White, true, 0, true);
        _ui.Text("Esc / F5: back to editor    F1: fly mode", 480, 700, 16, C(255, 255, 255, 200), false, 1, true);
        if (_testDialog != null)
        {
            _ui.Rect(120, 470, 720, 190, C(10, 10, 40, 235), 16, C(255, 255, 255, 160), 3);
            _ui.Text(_testDialog, 160, 540, 32, SKColors.White, false, 0, true);
        }
        _ui.EndFrame();
    }

    private void DrawCanvas(Vector2 m)
    {
        _scene.Ppu = _zoom;
        Raylib.DrawRectangle(0, 0, AppWindow.VW, AppWindow.VH, new Color((byte)28, (byte)30, (byte)46, (byte)255));

        float x0 = _scene.SX(0), x1 = _scene.SX(_level.Width * 32.0);
        float yTop = _scene.SY(_level.Height * 32.0), yBot = _scene.SY(0);
        Raylib.DrawRectangle((int)x0, (int)yTop, (int)(x1 - x0), (int)(yBot - yTop), new Color((byte)88, (byte)128, (byte)200, (byte)255));

        _scene.DrawLayers(_level, _scene.CenterX, _scene.CenterY, front: false, editorView: true, editLayer: _level.EditLayer);
        _scene.DrawTiles(_level.Tiles, _level.Width, _level.Height, true, _level.EditLayer == 0 ? 255 : 120);
        _scene.DrawEntityDefs(_level);
        foreach (var ed in _level.Entities)
            if (ed.Get("script").Length > 0)
                Raylib.DrawRectangle((int)_scene.SX(ed.X * 32.0 + 22), (int)_scene.SY(ed.Y * 32.0 + 32), (int)Math.Max(3, 8 * _zoom), (int)Math.Max(3, 8 * _zoom), new Color((byte)255, (byte)220, (byte)60, (byte)255));
        _scene.DrawLayers(_level, _scene.CenterX, _scene.CenterY, front: true, editorView: true, editLayer: _level.EditLayer);

        if (_grid && _zoom >= 0.5)
        {
            var gc = new Color((byte)255, (byte)255, (byte)255, (byte)28);
            for (int gx = 0; gx <= _level.Width; gx++)
            {
                float sx = _scene.SX(gx * 32.0);
                if (sx < PalW || sx > AppWindow.VW) continue;
                Raylib.DrawLineV(new Vector2(sx, Math.Max(TopH, yTop)), new Vector2(sx, Math.Min(AppWindow.VH - BotH, yBot)), gc);
            }
            for (int gy = 0; gy <= _level.Height; gy++)
            {
                float sy = _scene.SY(gy * 32.0);
                if (sy < TopH || sy > AppWindow.VH - BotH) continue;
                Raylib.DrawLineV(new Vector2(Math.Max(PalW, x0), sy), new Vector2(Math.Min(AppWindow.VW, x1), sy), gc);
            }
        }
        Raylib.DrawRectangleLines((int)x0, (int)yTop, (int)(x1 - x0), (int)(yBot - yTop), new Color((byte)255, (byte)255, (byte)255, (byte)160));

        // cursor / brush preview / rectangle preview
        if (_modal == Modal.None && InCanvas(m))
        {
            var (cx, cy) = CellAt(m);
            if (_level.InBounds(cx, cy))
            {
                if (_tool == Tool.Rect && _stroke)
                {
                    int rx0 = Math.Min(_rectStart.x, cx), rx1 = Math.Max(_rectStart.x, cx);
                    int ry0 = Math.Min(_rectStart.y, cy), ry1 = Math.Max(_rectStart.y, cy);
                    float sx0 = _scene.SX(rx0 * 32.0), sy0 = _scene.SY((ry1 + 1) * 32.0);
                    float sw = (float)((rx1 - rx0 + 1) * 32 * _zoom), sh = (float)((ry1 - ry0 + 1) * 32 * _zoom);
                    Raylib.DrawRectangleLines((int)sx0, (int)sy0, (int)sw, (int)sh, new Color((byte)255, (byte)230, (byte)80, (byte)255));
                }
                else if (_tool != Tool.Pick && _tool != Tool.Move)
                {
                    var spr = _sprites.Tile(_brush, _scene.AnimTime);
                    if (spr != null) _scene.DrawSprite(spr, cx * 32 + 16, cy * 32 + 16, TileScale(_brush, spr), false, 0, 150, true);
                }
                float hx = _scene.SX(cx * 32.0), hy = _scene.SY((cy + 1) * 32.0);
                Raylib.DrawRectangleLines((int)hx, (int)hy, (int)(32 * _zoom), (int)(32 * _zoom), new Color((byte)255, (byte)255, (byte)255, (byte)230));
            }
        }
    }

    // ---- palette geometry (shared by drawing + hit testing)

    private IEnumerable<(int id, float x, float y)> PaletteCells()
    {
        float y = TopH + 8 - _paletteScroll;
        foreach (var (_, ids) in _sections)
        {
            y += 22;
            for (int i = 0; i < ids.Length; i++)
            {
                float cx = 8 + (i % 5) * CellPx;
                float cy = y + (i / 5) * CellPx;
                yield return (ids[i], cx, cy);
            }
            y += ((ids.Length + 4) / 5) * CellPx + 8;
        }
    }

    private float PaletteContentHeight()
    {
        float h = 8;
        foreach (var (_, ids) in _sections) h += 22 + ((ids.Length + 4) / 5) * CellPx + 8;
        return h;
    }

    private void DrawPanelsBackground()
    {
        _ui.Rect(0, TopH, PalW, AppWindow.VH - TopH - BotH, C(24, 26, 44, 255));
        _ui.Rect(0, 0, AppWindow.VW, TopH, C(34, 36, 62, 255));
        _ui.Rect(0, AppWindow.VH - BotH, AppWindow.VW, BotH, C(34, 36, 62, 255));
        _ui.Line(PalW, TopH, PalW, AppWindow.VH - BotH, C(255, 255, 255, 60));

        foreach (var (id, x, y) in PaletteCells())
        {
            if (y < TopH || y + CellPx > AppWindow.VH - BotH) continue;
            bool sel = id == _brush;
            _ui.Rect(x, y, CellPx - 3, CellPx - 3, sel ? C(255, 190, 60, 255) : C(48, 52, 90, 255), 5);
            _ui.Rect(x + 2, y + 2, CellPx - 7, CellPx - 7, C(70, 110, 190, 255), 3);
        }

        if (_modal != Modal.None)
        {
            _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, C(0, 0, 0, 150));
            _ui.Canvas.Save();
            _ui.Canvas.Translate(ModalOrigin.X, ModalOrigin.Y);
            if (_modal == Modal.Open) _ui.Rect(180, 90, 600, 540, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Layers) _ui.Rect(150, 70, 660, 590, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Props) _ui.Rect(200, 90, 560, 540, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Entity) _ui.Rect(200, 90, 560, 540, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Prompt) _ui.Rect(200, 270, 560, 170, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            _ui.Canvas.Restore();
        }
    }

    private void DrawPaletteIcons()
    {
        foreach (var (id, x, y) in PaletteCells())
        {
            if (y < TopH || y + CellPx > AppWindow.VH - BotH) continue;
            var s = _sprites.Tile(id);
            if (s == null) continue;
            float ppu = Math.Min(2f, 28f / Math.Max(s.SrcW, s.SrcH));
            // centre the sprite's visual centre (not the rotation centre) in the cell
            float cxp = x + (CellPx - 3) / 2f - (s.SrcW / 2f - s.Cx) * ppu;
            float cyp = y + (CellPx - 3) / 2f - (s.SrcH / 2f - s.Cy) * ppu;
            _scene.DrawSpriteScreen(s, cxp, cyp, ppu);
        }
    }

    private bool Btn(string label, float x, float y, float w, float h, Vector2 m, bool selected = false, float fs = 18)
    {
        bool hot = _ui.Button(label, x, y, w, h, m, selected, fs);
        return hot && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    private void DrawPanelsText(Vector2 m)
    {
        bool free = _modal == Modal.None;
        var mm = free ? m : new Vector2(-999, -999);

        // top bar
        float bx = 8;
        void Top(string label, float w, Action act, bool sel = false)
        {
            if (Btn(label, bx, 6, w, 32, mm, sel, 14)) act();
            bx += w + 4;
        }
        Top("Open", 48, () => _modal = Modal.Open);
        Top("Save", 48, SaveAll);
        Top("Props", 52, () => _modal = Modal.Props);
        Top("Layers", 58, () => _modal = Modal.Layers);
        Top("Map", 44, EnterWorldMode);
        Top("Test", 48, BeginTest);
        Top("Undo", 48, DoUndo);
        Top("Redo", 48, DoRedo);
        bx += 10;
        Top("Pencil", 56, () => _tool = Tool.Pencil, _tool == Tool.Pencil);
        Top("Rect", 44, () => _tool = Tool.Rect, _tool == Tool.Rect);
        Top("Fill", 40, () => _tool = Tool.Fill, _tool == Tool.Fill);
        Top("Pick", 44, () => _tool = Tool.Pick, _tool == Tool.Pick);
        Top("Move", 48, () => _tool = Tool.Move, _tool == Tool.Move);
        Top("Script", 52, () => EnterScriptMode());
        bx += 10;
        Top("Grid", 46, () => _grid = !_grid, _grid);
        Top("Auto", 46, () => _auto = !_auto, _auto);

        string layerName = _level.EditLayer == 0 ? "main" : _level.Layers[_level.EditLayer - 1].Name;
        string title = $"{_levelId}{(_unsaved ? " *" : "")}  {_level.Width}x{_level.Height}  [{layerName}]";
        _ui.Text(title, AppWindow.VW - 10, 28, 13, SKColors.White, false, 2);

        // palette headers
        float y = TopH + 8 - _paletteScroll;
        foreach (var (titleS, ids) in _sections)
        {
            if (y + 22 > TopH && y < AppWindow.VH - BotH)
            {
                _ui.Rect(0, Math.Max(y, TopH), PalW, 18, C(24, 26, 44, 255));
                _ui.Text(titleS, 8, y + 15, 14, C(255, 210, 120), true);
            }
            y += 22 + ((ids.Length + 4) / 5) * CellPx + 8;
        }

        // palette clicks
        if (free && m.X < PalW && m.Y > TopH && m.Y < AppWindow.VH - BotH && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            foreach (var (id, x, cy) in PaletteCells())
            {
                if (m.X >= x && m.X < x + CellPx - 3 && m.Y >= cy && m.Y < cy + CellPx - 3)
                {
                    _brush = id;
                    if (_tool == Tool.Pick) _tool = Tool.Pencil;
                    break;
                }
            }
        }
        _paletteScroll = Math.Clamp(_paletteScroll, 0, Math.Max(0, PaletteContentHeight() - (AppWindow.VH - TopH - BotH)));

        // status bar
        string status = $"Brush: {TileName(_brush)} [{TileRegistry.IdOf(_brush)}]   Zoom {(int)(_zoom * 100)}%";
        if (free && InCanvas(m))
        {
            var (cx, cy) = CellAt(m);
            if (_level.InBounds(cx, cy))
            {
                var e = EntityAt(cx, cy);
                status += $"   Cell {cx},{cy}: {TileName(_level.Get(cx, cy))}";
                if (e != null) status += $"  [entity {EntityRegistry.Find(e.Type)?.Name ?? e.Type}{(e.Props.Count > 0 ? ": " + string.Join(", ", e.Props.Select(kv => kv.Key + "=" + kv.Value)) : "")}]";
            }
        }
        _ui.Text(status, 10, AppWindow.VH - 9, 14, SKColors.White);
        if (_messageTime > 0) _ui.Text(_message, AppWindow.VW - 10, AppWindow.VH - 9, 14, C(255, 230, 120), true, 2);
        else _ui.Text("LMB paint  RMB erase  MMB/Space+LMB pan  wheel zoom  Ctrl+S save  F5 test", AppWindow.VW - 10, AppWindow.VH - 9, 13, C(255, 255, 255, 150), false, 2);
    }

    // ================================================================ modals

    /// <summary>Modals are laid out for 960x720; in larger windows they are centred.</summary>
    private static Vector2 ModalOrigin => new(Math.Max(0, (AppWindow.VW - 960) / 2f), Math.Max(0, (AppWindow.VH - 720) / 2f));

    private void DrawModals(Vector2 m)
    {
        if (_modal == Modal.None) return;
        var o = ModalOrigin;
        _ui.Canvas.Save();
        _ui.Canvas.Translate(o.X, o.Y);
        m -= o;
        switch (_modal)
        {
            case Modal.Open: DrawOpenModal(m); break;
            case Modal.Props: DrawPropsModal(m); break;
            case Modal.Layers: DrawLayersModal(m); break;
            case Modal.Entity: DrawEntityModal(m); break;
            case Modal.Prompt: DrawPromptModal(); break;
        }
        _ui.Canvas.Restore();
    }

    private void DrawPromptModal()
    {
        _ui.Text(_promptTitle, 220, 310, 20, SKColors.White, true);
        _ui.Rect(220, 330, 520, 44, C(10, 10, 24, 255), 8, C(255, 255, 255, 140));
        string shown = _promptValue + ((int)(Raylib.GetTime() * 2) % 2 == 0 ? "|" : "");
        _ui.Text(shown, 230, 360, 24, SKColors.White);
        _ui.Text("Enter = OK   Esc = cancel", 220, 410, 14, C(255, 255, 255, 170));
    }

    private void DrawOpenModal(Vector2 m)
    {
        _ui.Text("Levels", 200, 126, 28, SKColors.White, true);
        var ids = _lib.Ids.ToList();
        float wheel = Raylib.GetMouseWheelMove();
        _openScroll = Math.Max(0, _openScroll - wheel * 40);
        int rowH = 34;
        float top = 146;
        int visible = 11;
        _openScroll = Math.Min(_openScroll, Math.Max(0, (ids.Count - visible) * rowH));
        for (int i = 0; i < ids.Count; i++)
        {
            float y = top + i * rowH - _openScroll;
            if (y < top - 1 || y + rowH > top + visible * rowH) continue;
            var l = _lib.Get(ids[i]);
            string label = $"{ids[i]}   {l?.Name}   ({l?.Width}x{l?.Height})" + (ids[i] == _levelId ? "   <- editing" : "") +
                           (ids[i] == _lib.StartLevel ? "   [start]" : "");
            if (Btn(label, 200, y, 560, rowH - 4, m, ids[i] == _levelId, 16))
            {
                OpenLevel(ids[i]);
                _modal = Modal.None;
            }
        }
        float by = 540;
        if (Btn("New level", 200, by, 130, 40, m))
        {
            string suggestion = _lib.NextFreeId();
            OpenPrompt("Id of the new level (letters, digits, _)", suggestion, false, v =>
            {
                string id = LevelLibrary.Sanitize(v);
                if (_lib.Has(id)) { Say($"'{id}' already exists"); return; }
                CommitToLibrary();
                _levelId = id;
                _level = LevelData.CreateBlank(70, 40);
                _level.Name = id;
                _lib.Set(_levelId, _level);
                _undo.Clear(); _redo.Clear();
                _selLayer = -1;
                CenterOnLevel();
                _unsaved = true;
            });
        }
        if (Btn("Delete", 340, by, 110, 40, m) && ids.Count > 1)
        {
            string del = _levelId;
            _lib.Remove(del);
            _levelId = "";
            _level = new LevelData(1, 1);
            OpenLevel(_lib.Ids.First());
            _unsaved = true;
            Say($"Deleted level {del}");
        }
        if (Btn("Set as start", 460, by, 160, 40, m))
        {
            _lib.StartLevelId = _levelId;
            _lib.SaveGameFile();
            Say($"Game now starts at level {_levelId}");
        }
        if (Btn("Close", 630, by, 130, 40, m)) _modal = Modal.None;
    }

    private void DrawPropsModal(Vector2 m)
    {
        float x = 230, y = 130;
        _ui.Text("Level properties", x, y + 10, 28, SKColors.White, true);
        if (Btn(_propsPage == 0 ? "Page 2 >" : "< Page 1", x + 380, y - 6, 120, 34, m, false, 15)) _propsPage ^= 1;
        y += 40;

        void Row(string label, string value, Action? minus, Action? plus, Action? edit = null)
        {
            _ui.Text(label, x, y + 28, 18, SKColors.White);
            if (minus != null && Btn("-", x + 190, y, 38, 38, m)) minus();
            if (edit != null) { if (Btn(value, x + 232, y, 120, 38, m, false, 16)) edit(); }
            else _ui.Text(value, x + 292, y + 27, 18, C(255, 230, 120), true, 1);
            if (plus != null && Btn("+", x + 356, y, 38, 38, m)) plus();
            y += 50;
        }

        void Link(string label, string value, string prompt, Action<string> set)
        {
            _ui.Text(label, x, y + 28, 18, SKColors.White);
            if (Btn(value.Length == 0 ? "(none)" : value, x + 190, y, 300, 38, m, false, 16))
                OpenPrompt(prompt, value, false, v => { PushUndo(); set(v.Trim()); });
            y += 50;
        }

        if (_propsPage == 0)
        {
            Row("Name", _level.Name, null, null, () =>
                OpenPrompt("Level name", _level.Name, false, v => { PushUndo(); _level.Name = v.Trim(); }));
            int step = Shift ? 10 : 1;
            Row("Width", _level.Width.ToString(), () => Resize(-step, 0), () => Resize(step, 0));
            Row("Height", _level.Height.ToString(), () => Resize(0, -step), () => Resize(0, step));
            Row("Parallax city", _level.ShowBackground ? "on" : "off", () => ToggleFlag(GameConstants.FlagShowBackground), () => ToggleFlag(GameConstants.FlagShowBackground));
            Row("Underwater", _level.Underwater ? "on" : "off", () => ToggleFlag(GameConstants.FlagUnderwater), () => ToggleFlag(GameConstants.FlagUnderwater));
            Row("Camera mode", _level.CameraMode.ToString(), () => { PushUndo(); _level.CameraMode = Math.Max(0, _level.CameraMode - 1); }, () => { PushUndo(); _level.CameraMode = Math.Min(7, _level.CameraMode + 1); });
            Row("Backdrop", _level.Backdrop.ToString(), () => { PushUndo(); _level.Backdrop = Math.Max(1, _level.Backdrop - 1); }, () => { PushUndo(); _level.Backdrop = Math.Min(3, _level.Backdrop + 1); });

            _ui.Text("Camera: 0 follow, 1 screen-by-screen, 5 shake, 6 smooth, 7 free fall.", x, y + 12, 13, C(255, 255, 255, 170));
            _ui.Text("Hold Shift for steps of 10 when resizing.", x, y + 30, 13, C(255, 255, 255, 170));
        }
        else
        {
            Row("Id", _levelId, null, null, () => OpenPrompt("Level id (file name)", _levelId, false, RenameLevel));
            Link("Next level", _level.Next ?? "", "Level that follows when this one is completed (empty = none)", v => _level.Next = v.Length == 0 ? null : v);
            Link("Left exit", _level.Left ?? "", "Level entered by walking off the left edge (empty = none)", v => _level.Left = v.Length == 0 ? null : v);
            Link("Right exit", _level.Right ?? "", "Level entered by walking off the right edge (empty = none)", v => _level.Right = v.Length == 0 ? null : v);
            Link("Script", _level.Script, "Script id: file assets/scripts/<id>.mgs (empty = none)", v => _level.Script = v);
            Link("Player script", _level.PlayerScript, "Script id run by the player in this level (empty = game default)", v => _level.PlayerScript = v);
            Link("Song", _level.Song, "Song name (0 = default)", v => _level.Song = v.Length == 0 ? "0" : v);
            _ui.Text("Levels are saved as <id>.json. Doors, exits and the overworld refer to levels by id.", x, y + 12, 13, C(255, 255, 255, 170));
        }
        if (Btn("Close", x + 130, 560, 140, 44, m)) _modal = Modal.None;
    }

    // ================================================================ layers

    private void CycleEditLayer(int dir)
    {
        // only tile layers can be painted: main (0) and the tile layers
        var stops = new List<int> { 0 };
        for (int i = 0; i < _level.Layers.Count; i++)
            if (_level.Layers[i].Kind == LayerKind.Tiles) stops.Add(i + 1);
        int at = Math.Max(0, stops.IndexOf(_level.EditLayer));
        _level.EditLayer = stops[(at + dir + stops.Count) % stops.Count];
        _selLayer = _level.EditLayer - 1;
        Say("Editing layer: " + (_level.EditLayer == 0 ? "main" : _level.Layers[_level.EditLayer - 1].Name));
    }

    private void DrawLayersModal(Vector2 m)
    {
        float x = 175, y = 100;
        _ui.Text("Layers", x, y + 20, 28, SKColors.White, true);
        _ui.Text("Back layers are drawn behind the main (solid) layer, front layers on top. [ and ] switch the layer you paint on.", x, y + 44, 13, C(255, 255, 255, 170));
        y += 56;

        // list: front layers first (top of the stack), main in the middle, back layers last
        var order = new List<int>();
        for (int i = _level.Layers.Count - 1; i >= 0; i--) if (_level.Layers[i].Front) order.Add(i);
        order.Add(-1);
        for (int i = _level.Layers.Count - 1; i >= 0; i--) if (!_level.Layers[i].Front) order.Add(i);

        foreach (int i in order)
        {
            string label;
            bool editing;
            if (i < 0) { label = "[main]  solid tile layer"; editing = _level.EditLayer == 0; }
            else
            {
                var l = _level.Layers[i];
                label = $"{(l.Front ? "front" : "back ")}  {l.Name}   ({(l.Kind == LayerKind.Image ? "image" : "tiles")}{(l.Visible ? "" : ", hidden")})";
                editing = _level.EditLayer == i + 1;
            }
            if (Btn((editing ? "> " : "  ") + label, x, y, 610, 30, m, _selLayer == i, 15))
            {
                _selLayer = i;
                if (i < 0) _level.EditLayer = 0;
                else if (_level.Layers[i].Kind == LayerKind.Tiles) _level.EditLayer = i + 1;
            }
            y += 33;
            if (y > 330) break;
        }

        y = 346;
        float bx = x;
        void B(string label, float w, Action act)
        {
            if (Btn(label, bx, y, w, 34, m, false, 15)) act();
            bx += w + 6;
        }
        B("+ Back tiles", 112, () => AddLayer(Layer.NewTiles("back tiles", _level.Width, _level.Height, false)));
        B("+ Front tiles", 116, () => AddLayer(Layer.NewTiles("front tiles", _level.Width, _level.Height, true)));
        B("+ Image", 80, () => OpenPrompt("Image path under assets/ (png or svg)", "custom/", false, v =>
            AddLayer(new Layer { Name = Path.GetFileNameWithoutExtension(v), Kind = LayerKind.Image, Image = v.Trim(), RepeatX = true, Parallax = 0.5, X = _level.Width * 16, Y = _level.Height * 16 })));
        if (_selLayer >= 0 && _selLayer < _level.Layers.Count)
        {
            var l = _level.Layers[_selLayer];
            bx = x; y = 384;
            B("Show/Hide", 100, () => { PushUndo(); l.Visible = !l.Visible; });
            B("Front/Back", 100, () => { PushUndo(); l.Front = !l.Front; });
            B("Up", 44, () => MoveLayer(_selLayer, 1));
            B("Down", 56, () => MoveLayer(_selLayer, -1));
            B("Delete", 72, () =>
            {
                PushUndo();
                _level.Layers.RemoveAt(_selLayer);
                _level.EditLayer = 0;
                _selLayer = -1;
            });
        }

        // properties of the selected layer (the same rows the overworld map editor uses)
        if (_selLayer >= 0 && _selLayer < _level.Layers.Count)
        {
            var l = _level.Layers[_selLayer];
            _ui.Text($"{l.Name}", x, 440, 18, C(255, 210, 120), true);
            int n = 0;
            void Row(string label, string value, Action act)
            {
                float cx = x + (n < 4 ? 0 : 320), cy = 452 + (n % 4) * 34;
                n++;
                if (Btn(Shorten($"{label}: {value}", 34), cx, cy, 310, 30, m, false, 14)) act();
            }
            Row("Name", l.Name, () => OpenPrompt("Layer name", l.Name, false, v => { PushUndo(); l.Name = v.Trim(); }, () => { }));
            ImageLayerRows(l, l.Kind == LayerKind.Image, Row, PushUndo);
        }
        if (Btn("Close", x + 235, 610, 140, 40, m)) _modal = Modal.None;
    }

    /// <summary>Changes the drawing order: +1 = drawn later (on top), -1 = earlier.</summary>
    private void MoveLayer(int i, int dir)
    {
        int j = i + dir;
        if (j < 0 || j >= _level.Layers.Count) return;
        PushUndo();
        // keep the layer being painted selected
        int edit = _level.EditLayer;
        (_level.Layers[i], _level.Layers[j]) = (_level.Layers[j], _level.Layers[i]);
        if (edit == i + 1) _level.EditLayer = j + 1; else if (edit == j + 1) _level.EditLayer = i + 1;
        _selLayer = j;
    }

    private void AddLayer(Layer layer)
    {
        PushUndo();
        _level.Layers.Add(layer);
        _selLayer = _level.Layers.Count - 1;
        if (layer.Kind == LayerKind.Tiles) _level.EditLayer = _level.Layers.Count;
        _unsaved = true;
    }

    private void Resize(int dw, int dh)
    {
        int w = Math.Clamp(_level.Width + dw, 10, 2000);
        int h = Math.Clamp(_level.Height + dh, 10, 200);
        if (w == _level.Width && h == _level.Height) return;
        PushUndo();
        _level.Resize(w, h);
    }

    private void ToggleFlag(int bit)
    {
        PushUndo();
        _level.Flags ^= bit;
    }

    public void Dispose()
    {
        _audio.Dispose();
        _ui.Dispose();
        _sprites.Dispose();
        _win.Dispose();
    }
}
