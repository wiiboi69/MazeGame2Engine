using System.Numerics;
using MazeGame.Core;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Editor;

public enum Tool { Pencil, Rect, Fill, Pick }
public enum Modal { None, Open, Props, Prompt }

/// <summary>
/// Level editor: tile / entity palette, pencil + rectangle + fill + picker tools, auto-tiling, undo / redo,
/// level properties, level browser and a play-test that runs the real game simulation.
/// </summary>
public sealed class EditorApp : IDisposable
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

    private int _levelNumber;
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

    // play test
    private World? _test;
    private double _acc;
    private string? _testDialog;
    private bool _quit;

    private readonly List<(string title, int[] ids)> _sections = new();

    public EditorApp(string assetsDir, string levelsDir)
    {
        _win = new AppWindow("Maze Game 2 - Level Editor", false);
        _sprites = new SpriteLibrary(assetsDir);
        _scene = new SceneRenderer(_sprites);
        _ui = new SkiaUi();
        _audio = new AudioEngine(Path.Combine(assetsDir, "audio")) { MusicVolume = 0.4f, SfxVolume = 0.8f };
        _input = new GameInput(_settings);
        _lib = LevelLibrary.LoadDirectory(levelsDir);
        BuildPalette();

        int first = _lib.LevelNumbers.FirstOrDefault();
        if (first == 0)
        {
            _levelNumber = _lib.NextFreeNumber;
            _level = LevelData.CreateBlank(70, 40);
            _lib.Set(_levelNumber, _level);
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
        var titles = new (string key, string title)[]
        {
            ("1", "Wood & pipes"), ("2", "Blocks, ladders, clouds"), ("3", "Orange blocks"), ("5", "Grass & slopes"),
            ("6", "Edge tiles"), ("4", "Gems & player spawn"), ("9", "Entities"), ("8", "Doors, pipes & logic"), ("", "Other"),
        };
        foreach (var (key, title) in titles)
        {
            var ids = new List<int>();
            for (int id = 3; id <= TileInfo.TileCount; id++)
                if (TileInfo.Keymap(id) == key) ids.Add(id);
            if (key == "4") { ids.Remove(TileInfo.PlayerSpawn); ids.Add(TileInfo.PlayerSpawn); }
            if (ids.Count > 0) _sections.Add((title, ids.ToArray()));
        }
    }

    private string TileName(int id)
    {
        var g = _sprites.Group("tiles");
        return g != null && g.TryGetValue(id.ToString(), out var c) ? c.Name : "tile " + id;
    }

    // ================================================================ level management

    private void CommitToLibrary() => _lib.Set(_levelNumber, _level);

    private void OpenLevel(int n)
    {
        if (_levelNumber >= GameConstants.FirstLevel && _level.Tiles.Length > 0) CommitToLibrary();
        var l = _lib.Get(n);
        if (l == null) return;
        _levelNumber = n;
        _level = l.Clone();
        _undo.Clear();
        _redo.Clear();
        CenterOnLevel();
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
        _level = _undo.Pop();
        _unsaved = true;
    }

    private void DoRedo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_level.Clone());
        _level = _redo.Pop();
        _unsaved = true;
    }

    // ================================================================ main loop

    public void Run()
    {
        while (!_win.ShouldClose && !_quit)
        {
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _audio.Update();
            if (_messageTime > 0) _messageTime -= dt;

            if (_test != null) UpdateTest(dt); else UpdateEdit(dt);
            Draw();
        }
        CommitToLibrary();
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
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _grid = !_grid;
            if (Raylib.IsKeyPressed(KeyboardKey.Q)) { _auto = !_auto; Say("Auto-tiling " + (_auto ? "on" : "off")); }
            if (Raylib.IsKeyPressed(KeyboardKey.P)) _modal = Modal.Props;
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

        if (_tool == Tool.Pick)
        {
            if (lmbPressed && _level.InBounds(cx, cy))
            {
                var e = EntityAt(cx, cy);
                _brush = e != null ? e.Type : _level.Get(cx, cy);
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
        bool entityBrush = TileInfo.IsEntityBrush(_brush);
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
                    if (!RemoveEntityAt(cx, cy)) { _level.Set(cx, cy, TileInfo.Air); if (_auto) AutoTile.FixAround(_level, cx, cy); }
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
        if (tile == TileInfo.PlayerSpawn)
            for (int i = 0; i < _level.Tiles.Length; i++)
                if (_level.Tiles[i] == TileInfo.PlayerSpawn) _level.Tiles[i] = TileInfo.Air;
        _level.Set(x, y, tile);
        if (_auto && TileInfo.Group(tile).Length > 0) AutoTile.FixAround(_level, x, y);
    }

    private bool RemoveEntityAt(int x, int y) => _level.Entities.RemoveAll(e => e.X == x && e.Y == y) > 0;

    private void PlaceEntity(int x, int y)
    {
        var existing = EntityAt(x, y);
        if (existing != null) { _level.Entities.Remove(existing); return; }
        var def = new EntityDef { X = x, Y = y, Type = _brush };
        if (TileInfo.IsTrigger(_brush))
        {
            _level.Entities.Add(def);
            int[] nums = _lib.LevelNumbers.ToArray();
            string hint = nums.Length > 0 ? $"Target level number (existing: {nums.First()}-{nums.Last()})" : "Target level number";
            OpenPrompt(hint, (_levelNumber + 1).ToString(), true, v =>
            {
                if (int.TryParse(v, out int n) && n >= GameConstants.FirstLevel) def.Param = n.ToString();
                else _level.Entities.Remove(def);
            });
        }
        else if (_brush == TileInfo.Star)
        {
            _level.Entities.Add(def);
            OpenPrompt("NPC says...", "hello", false, v => def.Param = string.IsNullOrWhiteSpace(v) ? "" : LevelData.EncodeText(v));
        }
        else
        {
            _level.Entities.Add(def);
        }
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

    private void OpenPrompt(string title, string initial, bool numeric, Action<string> ok)
    {
        _promptTitle = title; _promptValue = initial; _promptNumeric = numeric; _promptOk = ok;
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
            _promptOk?.Invoke(_promptNumeric ? "" : "hello");
        }
    }

    // ================================================================ play test

    private void BeginTest()
    {
        CommitToLibrary();
        var level = _level.Clone();
        _test = new World(level, _levelNumber, n => n == _levelNumber ? level : _lib.Get(n));
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
                if (req.Level == _levelNumber || lvl == null) _test.Load(_test.Level, _test.LevelNumber, LoadKind.Respawn, 0);
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

        _scene.DrawTiles(_level.Tiles, _level.Width, _level.Height, true);
        _scene.DrawEntityDefs(_level);

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
                else if (_tool != Tool.Pick)
                {
                    var spr = _sprites.Tile(_brush);
                    _scene.DrawSprite(spr, cx * 32 + 16, cy * 32 + 16, 2.0, false, 0, 150, true);
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
            if (_modal == Modal.Open) _ui.Rect(180, 90, 600, 540, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Props) _ui.Rect(250, 90, 460, 540, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            if (_modal == Modal.Prompt) _ui.Rect(200, 270, 560, 170, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
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
            if (Btn(label, bx, 6, w, 32, mm, sel, 16)) act();
            bx += w + 6;
        }
        Top("Open", 60, () => _modal = Modal.Open);
        Top("Save", 60, SaveAll);
        Top("Props", 62, () => _modal = Modal.Props);
        Top("Test", 58, BeginTest);
        Top("Undo", 58, DoUndo);
        Top("Redo", 58, DoRedo);
        bx += 10;
        Top("Pencil", 70, () => _tool = Tool.Pencil, _tool == Tool.Pencil);
        Top("Rect", 56, () => _tool = Tool.Rect, _tool == Tool.Rect);
        Top("Fill", 52, () => _tool = Tool.Fill, _tool == Tool.Fill);
        Top("Pick", 54, () => _tool = Tool.Pick, _tool == Tool.Pick);
        bx += 10;
        Top("Grid", 54, () => _grid = !_grid, _grid);
        Top("Auto", 56, () => _auto = !_auto, _auto);

        string title = $"Level {_levelNumber}  \"{_level.Name}\"  {_level.Width}x{_level.Height}{(_unsaved ? " *" : "")}";
        _ui.Text(title, AppWindow.VW - 10, 28, 15, SKColors.White, false, 2);

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
        string status = $"Brush: {TileName(_brush)} ({_brush})   Zoom {(int)(_zoom * 100)}%";
        if (free && InCanvas(m))
        {
            var (cx, cy) = CellAt(m);
            if (_level.InBounds(cx, cy))
            {
                var e = EntityAt(cx, cy);
                status += $"   Cell {cx},{cy}: {TileName(_level.Get(cx, cy))}";
                if (e != null) status += $"  [entity {TileName(e.Type)}{(e.Param.Length > 0 && TileInfo.IsTrigger(e.Type) ? " -> level " + e.Param : "")}]";
            }
        }
        _ui.Text(status, 10, AppWindow.VH - 9, 14, SKColors.White);
        if (_messageTime > 0) _ui.Text(_message, AppWindow.VW - 10, AppWindow.VH - 9, 14, C(255, 230, 120), true, 2);
        else _ui.Text("LMB paint  RMB erase  MMB/Space+LMB pan  wheel zoom  Ctrl+S save  F5 test", AppWindow.VW - 10, AppWindow.VH - 9, 13, C(255, 255, 255, 150), false, 2);
    }

    // ================================================================ modals

    private void DrawModals(Vector2 m)
    {
        switch (_modal)
        {
            case Modal.Open: DrawOpenModal(m); break;
            case Modal.Props: DrawPropsModal(m); break;
            case Modal.Prompt: DrawPromptModal(); break;
        }
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
        var nums = _lib.LevelNumbers.ToList();
        float wheel = Raylib.GetMouseWheelMove();
        _openScroll = Math.Max(0, _openScroll - wheel * 40);
        int rowH = 34;
        float top = 146;
        int visible = 11;
        _openScroll = Math.Min(_openScroll, Math.Max(0, (nums.Count - visible) * rowH));
        for (int i = 0; i < nums.Count; i++)
        {
            float y = top + i * rowH - _openScroll;
            if (y < top - 1 || y + rowH > top + visible * rowH) continue;
            var l = _lib.Get(nums[i]);
            string label = $"{nums[i]}   {l?.Name}   ({l?.Width}x{l?.Height})" + (nums[i] == _levelNumber ? "   <- editing" : "") +
                           (nums[i] == _lib.StartLevel ? "   [start]" : "");
            if (Btn(label, 200, y, 560, rowH - 4, m, nums[i] == _levelNumber, 16))
            {
                OpenLevel(nums[i]);
                _modal = Modal.None;
            }
        }
        float by = 540;
        if (Btn("New level", 200, by, 130, 40, m))
        {
            CommitToLibrary();
            _levelNumber = _lib.NextFreeNumber;
            _level = LevelData.CreateBlank(70, 40);
            _lib.Set(_levelNumber, _level);
            _undo.Clear(); _redo.Clear();
            CenterOnLevel();
            _unsaved = true;
            _modal = Modal.None;
        }
        if (Btn("Delete", 340, by, 110, 40, m) && nums.Count > 1)
        {
            int del = _levelNumber;
            _lib.Remove(del);
            _levelNumber = 0;
            _level = new LevelData(1, 1);
            OpenLevel(_lib.LevelNumbers.First());
            _unsaved = true;
            Say($"Deleted level {del}");
        }
        if (Btn("Set as start", 460, by, 160, 40, m))
        {
            _lib.SetStartLevel(_levelNumber);
            _lib.Save(GameConstants.SettingsSlot);
            Say($"Game now starts at level {_levelNumber}");
        }
        if (Btn("Close", 630, by, 130, 40, m)) _modal = Modal.None;
    }

    private void DrawPropsModal(Vector2 m)
    {
        float x = 280, y = 130;
        _ui.Text("Level properties", x, y + 10, 28, SKColors.White, true);
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

        Row("Name", _level.Name, null, null, () =>
            OpenPrompt("Level name", _level.Name, false, v => { PushUndo(); _level.Name = v.Replace("_", " ").Trim(); }));
        int step = Shift ? 10 : 1;
        Row("Width", _level.Width.ToString(), () => Resize(-step, 0), () => Resize(step, 0));
        Row("Height", _level.Height.ToString(), () => Resize(0, -step), () => Resize(0, step));
        Row("Parallax city", _level.ShowBackground ? "on" : "off", () => ToggleFlag(GameConstants.FlagShowBackground), () => ToggleFlag(GameConstants.FlagShowBackground));
        Row("Underwater", _level.Underwater ? "on" : "off", () => ToggleFlag(GameConstants.FlagUnderwater), () => ToggleFlag(GameConstants.FlagUnderwater));
        Row("Camera mode", _level.CameraMode.ToString(), () => { PushUndo(); _level.CameraMode = Math.Max(0, _level.CameraMode - 1); }, () => { PushUndo(); _level.CameraMode = Math.Min(7, _level.CameraMode + 1); });
        Row("Backdrop", _level.Backdrop.ToString(), () => { PushUndo(); _level.Backdrop = Math.Max(1, _level.Backdrop - 1); }, () => { PushUndo(); _level.Backdrop = Math.Min(3, _level.Backdrop + 1); });

        _ui.Text("Camera: 0 follow, 1 screen-by-screen, 5 shake, 6 smooth, 7 free fall.", x, y + 12, 13, C(255, 255, 255, 170));
        _ui.Text("Hold Shift for steps of 10 when resizing.", x, y + 30, 13, C(255, 255, 255, 170));
        if (Btn("Close", x + 130, 560, 140, 44, m)) _modal = Modal.None;
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
