using System.Numerics;
using MazeGame.Core;
using MazeGame.Core.Scripting;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame;

public enum GameState { Title, Settings, Slots, Map, Loading, Playing, Paused, Dialog, Wipe, End }

/// <summary>
/// The playable game. Menus, loading screen and transitions follow the Scratch project: all menu art is drawn in
/// Scratch stage coordinates (480x360, origin in the centre, y up) and animated at the project's 30 ticks per second.
/// Flow: title -> save slot -> (overworld map ->) level -> back to the map.
/// </summary>
public sealed class Game : IDisposable
{
    private const double Step = 1.0 / GameConstants.TicksPerSecond;

    private readonly AppWindow _win;
    private readonly SpriteLibrary _sprites;
    private readonly SceneRenderer _scene;
    private readonly SkiaUi _ui;
    private readonly AudioEngine _audio;
    private readonly Settings _settings;
    private readonly string _settingsPath;
    private readonly GameInput _input;
    private readonly LevelLibrary _levels;
    /// <summary>Per-level saved states (F6 / F7). Cleared whenever the overworld is shown.</summary>
    private readonly Dictionary<string, WorldSnapshot> _states = new();
    private string _toast = "";
    private float _toastT;
    private readonly OverworldLibrary _worlds;
    private readonly SaveManager _saves;
    private readonly SettingsScreen _settingsScreen;

    private World? _world;
    private GameState _state = GameState.Title;
    private double _acc;      // gameplay accumulator
    private double _uiAcc;    // menu animation accumulator
    private bool _musicOn;

    // mouse in screen and stage coordinates
    private Vector2 _mouse;
    private double _mx, _my;
    private bool _click;

    // ---- save slots and overworld
    private SaveSlot? _slot;
    private OverworldData? _map;
    private bool _inMap;                 // the running level was started from the map
    private bool _slotLoadMode;
    private int _slotSel;
    private int _eraseConfirm = -1;
    private double _mapScroll, _mapScrollY, _mapTime;
    private double _frX, _frY, _frAnim;     // free-roam avatar (map pixels)
    private int _frDir = 1;
    private double _mapPulse;

    // ---- title screen (sprite "main menu dilog" slides, the buttons follow it)
    private double _panelX = -571;
    private double _panelMenuX = 5;      // target factor: the panel settles at 4.7 * this
    private int _panelMode;              // 0 = slide in, 1 = slide out to the left
    private readonly double[] _titleSize = { 100, 100, 100 };
    private static readonly (string Costume, double Y)[] TitleButtons = { ("resome", 55), ("setting", -90), ("quit", -130) };
    private int _kbSel = -1;

    // ---- loading screen
    private int _ldT;
    private string _ldLevel = "";
    private bool _ldStarted;
    private int _introT = 99;            // ticks since the level appeared (drives the pink fade-out)

    // ---- pause menu ("menu dilog" green shard)
    private double _pauseX = -500;
    private bool _pauseClosing;

    private GameState _settingsReturn = GameState.Title;
    private readonly UiMenu _endMenu = new();

    // ---- transitions
    private LoadRequest _req;
    private double _wipeX;
    private int _wipePhase;
    private bool _wipeLoaded, _wipeWin;

    private string _dialogText = "";
    private bool _quit;

    public Game(string assetsDir, string levelsDir, string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = Settings.Load(settingsPath);
        _win = new AppWindow("Maze Game 2", _settings.Fullscreen, _settings.Widescreen, _settings.WindowScale);
        GameConstants.SetWidescreen(_settings.Widescreen);
        _sprites = new SpriteLibrary(assetsDir);
        _scene = new SceneRenderer(_sprites);
        _ui = new SkiaUi(AppWindow.VW, AppWindow.VH);
        _audio = new AudioEngine(Path.Combine(assetsDir, "audio"));
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(levelsDir.TrimEnd('/', '\\'))) ?? ".";
        _input = new GameInput(_settings);
        _levels = LevelLibrary.LoadDirectory(levelsDir);
        _worlds = OverworldLibrary.Load(Path.Combine(baseDir, "worlds"));
        _saves = new SaveManager(Path.Combine(baseDir, "saves"));
        ScriptLibrary.BaseDir = assetsDir;
        _settingsScreen = new SettingsScreen(_win, _ui, _audio, _settings, _saves);
        _settingsScreen.ApplyAll();
    }

    // ================================================================ main loop

    public void Run()
    {
        while (!_win.ShouldClose)
        {
            Pad.Update();
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _audio.Update();
            if (Pad.KeyPressed(KeyboardKey.F11)) { _win.ToggleFullscreen(); _settings.Fullscreen = Raylib.IsWindowFullscreen(); }

            Update(dt);
            Draw();
            if (_quit) break;
        }
        SaveSlotNow();
        _settings.Save(_settingsPath);
    }

    private void Update(float dt)
    {
        _mouse = _win.Mouse;
        _mx = (_mouse.X - AppWindow.VW / 2.0) / 2.0;
        _my = (AppWindow.VH / 2.0 - _mouse.Y) / 2.0;
        _click = Raylib.IsMouseButtonPressed(MouseButton.Left);

        _uiAcc += dt;
        while (_uiAcc >= Step)
        {
            _uiAcc -= Step;
            UiTick();
        }

        switch (_state)
        {
            case GameState.Title: UpdateTitleInput(); break;
            case GameState.Paused: UpdatePauseInput(); break;
            case GameState.Playing: UpdatePlaying(dt); break;
            case GameState.Map: UpdateMap(dt); break;
        }
    }

    /// <summary>Everything that animates at the original's fixed 30 ticks per second.</summary>
    private void UiTick()
    {
        switch (_state)
        {
            case GameState.Title: PanelTick(); TitleTick(); break;
            case GameState.Settings: PanelTick(); break;
            case GameState.Slots: PanelTick(); break;
            case GameState.Loading: LoadingTick(); break;
            case GameState.Paused: PauseTick(); break;
            case GameState.Wipe: WipeTick(); break;
            case GameState.Map: _mapPulse += 0.2; break;
            case GameState.Playing: if (_introT < 99) _introT++; break;
        }
    }

    // ================================================================ helpers

    private void Stage(string group, string key, double x, double y, double size = 100, int alpha = 255)
        => _scene.DrawSprite(_sprites.Get(group, key), x, y, size / 100.0, false, 0, alpha);

    /// <summary>Full-screen Scratch backdrop art; stretched in 16:9 so the sides are covered.</summary>
    private double BgSize => GameConstants.Widescreen ? 134 : 100;
    private void Bg(string group, string key, int alpha = 255) => Stage(group, key, 0, 0, BgSize, alpha);

    private bool HitSprite(string group, string key, double x, double y, double size = 100)
    {
        var s = _sprites.Get(group, key);
        return s != null && SceneRenderer.Hit(s, x, y, size / 100.0, _mx, _my);
    }

    private static double Ease(double size, bool hot) => size + 0.2 * ((hot ? 125.0 : 100.0) - size);

    private void Click() => _audio.PlaySfx("click");

    /// <summary>True when one of the keys bound to the action went down this frame.</summary>
    private bool Pressed(string action)
    {
        if (!_settings.Keys.TryGetValue(action, out var keys)) return false;
        foreach (int k in keys)
            if (k != 0 && Raylib.IsKeyPressed((KeyboardKey)k)) return true;
        if (action == "pause" && Pad.Pressed(PadAct.Start)) return true;
        return false;
    }

    private void EnsureMusic()
    {
        if (_musicOn) return;
        _audio.PlayMusic("my_song_68");
        _musicOn = true;
    }

    private void SaveSlotNow()
    {
        if (_slot != null) _saves.Save(_slot);
    }

    // ================================================================ level flow

    private void StartGame(string id)
    {
        var level = _levels.Get(id);
        if (level == null) return;
        _world = new World(level, id, _levels.Get);
        _world.Flags = _slot?.Flags ?? new Dictionary<string, string>();
        _world.SoundRequested += n => _audio.PlaySfx(n);
        _world.DialogRequested += OpenDialog;
        _world.States = _states;
        _world.Notice += msg => { _toast = msg; _toastT = 2.0f; };
        if (_slot != null && _map == null) _slot.Level = id;
        EnsureMusic();
        _acc = 0;
        _input.Reset();
    }

    private void OpenDialog(string text)
    {
        _dialogText = text;
        _state = GameState.Dialog;
    }

    private void HandlePending()
    {
        if (_world?.Pending is not { } req) return;
        _req = req;
        if (req.Wipe)
        {
            _wipeWin = req.Win;
            _wipeX = req.Win ? -811 : -826;   // Scratch fences the sprite just off the left edge
            _wipePhase = 1;
            _wipeLoaded = false;
            _state = GameState.Wipe;
        }
        else
        {
            ApplyLoad(req);
        }
    }

    private void ApplyLoad(LoadRequest req)
    {
        if (_world == null) return;
        if (req.Win)
        {
            CompleteLevel();
            if (_map != null && _inMap) { ShowMap(); return; }
        }
        var level = _levels.Get(req.Level);
        if (level == null)
        {
            if (req.Win) { _audio.StopMusic(); _musicOn = false; _state = GameState.End; return; }
            level = _world.Level;
            req = new LoadRequest(_world.LevelId, LoadKind.Respawn, false);
        }
        if (_slot != null && _map == null) _slot.Level = req.Level;
        _world.Load(level, req.Level, req.Kind, req.EntrySide);
        _input.Reset();
    }

    /// <summary>Records the finished level in the save slot.</summary>
    private void CompleteLevel()
    {
        if (_slot == null || _world == null) return;
        _slot.Gems += _world.Coins;
        if (_map != null && _inMap && _map.Node(_slot.Node) is { } node)
        {
            string key = _map.Id + "/" + node.Id;
            if (!_slot.Completed.Contains(key)) _slot.Completed.Add(key);
        }
        _saves.Save(_slot);
    }

    private void ToTitle()
    {
        _states.Clear();
        SaveSlotNow();
        _audio.StopMusic();
        _musicOn = false;
        _world = null;
        _slot = null;
        _map = null;
        _inMap = false;
        _state = GameState.Title;
        ResetTitle();
    }

    private void LeaveToMapOrTitle()
    {
        _audio.PauseMusic(false);
        if (_map != null && _inMap) { _world = null; ShowMap(); } else ToTitle();
    }

    private void ResetTitle()
    {
        _panelX = -571;
        _panelMenuX = 5;
        _panelMode = 0;
        _kbSel = -1;
        for (int i = 0; i < _titleSize.Length; i++) _titleSize[i] = 100;
    }

    // ================================================================ title screen

    private void PanelTick()
    {
        if (_panelMode == 0) _panelX += _panelMenuX - _panelX / 4.7;
        else if (Math.Abs(_panelX) <= 400) _panelX += _panelX / 32 - 60;
    }

    private bool TitleHot(int i) => _panelMode == 0 && (_kbSel == i || HitSprite("mainmenu", TitleButtons[i].Costume, _panelX - 180, TitleButtons[i].Y, _titleSize[i]));

    private void TitleTick()
    {
        for (int i = 0; i < _titleSize.Length; i++) _titleSize[i] = Ease(_titleSize[i], TitleHot(i));
    }

    private void UpdateTitleInput()
    {
        if (Pad.KeyPressed(KeyboardKey.Down) || Pad.KeyPressed(KeyboardKey.S)) _kbSel = (_kbSel + 1) % 3;
        if (Pad.KeyPressed(KeyboardKey.Up) || Pad.KeyPressed(KeyboardKey.W)) _kbSel = (_kbSel + 2) % 3;

        int act = -1;
        if (_click)
            for (int i = 0; i < 3; i++)
                if (HitSprite("mainmenu", TitleButtons[i].Costume, _panelX - 180, TitleButtons[i].Y, _titleSize[i])) act = i;
        if (Pad.KeyPressed(KeyboardKey.Enter) || Pad.KeyPressed(KeyboardKey.Space)) act = _kbSel < 0 ? 0 : _kbSel;

        if (act == 0) OpenSlots(false);
        else if (act == 1) OpenSettings(GameState.Title);
        else if (act == 2) OpenSlots(true);
    }

    private void DrawTitleBackdrop(int sceneAlpha = 255)
    {
        _scene.BeginStage();
        Bg("dialog", "costume5");
        Bg("dialog", "costume2", sceneAlpha);
    }

    private void DrawTitle(bool buttons)
    {
        DrawTitleBackdrop();
        if (_panelMode == 1 && Math.Abs(_panelX) > 400) return;
        Stage("mainmenu", "costume1", (_panelX - 150) * -0.9, 120);
        if (!buttons) return;
        for (int i = 0; i < 3; i++)
            Stage("mainmenu", TitleButtons[i].Costume, _panelX - 180, TitleButtons[i].Y, _titleSize[i]);
        _ui.Text("Start = pick a slot   Load = continue a slot   F11: fullscreen", AppWindow.VW / 2f, AppWindow.VH - 20, 16,
            new SKColor(255, 255, 255, 200), false, 1, true);
    }
    // ================================================================ loading screen

    private void BeginLoading(string level)
    {
        if (_levels.Get(level) == null) return;
        Click();
        _ldLevel = level;
        _ldT = 0;
        _ldStarted = false;
        _state = GameState.Loading;
    }

    private void LoadingTick()
    {
        _ldT++;
        if (_ldT == 50 && !_ldStarted)
        {
            _ldStarted = true;
            StartGame(_ldLevel);
            _introT = 0;
            _state = _world != null ? GameState.Playing : GameState.Title;
            if (_world == null) ResetTitle();
        }
    }

    private void DrawLoading()
    {
        _scene.BeginStage();
        if (_ldT < 20)
        {
            Bg("dialog", "costume5");
            Bg("dialog", "costume2", 255 - _ldT * 255 / 20);
        }
        else
        {
            Bg("dialog", "costume10");
        }
    }
    // ================================================================ pause menu

    private static readonly (string Costume, double Y)[] PauseButtons = { ("resome", 80), ("setting", -20), ("quit", -120) };

    private void EnterPause()
    {
        _state = GameState.Paused;
        _pauseX = -500;
        _pauseClosing = false;
        _audio.PauseMusic(true);
        _audio.PlaySfx("swoosh");
    }

    private void PauseTick()
    {
        if (!_pauseClosing) _pauseX += 0 - _pauseX / 4.7;
        else
        {
            _pauseX += _pauseX / 8 - 20;
            if (_pauseX < -500)
            {
                _state = GameState.Playing;
                _audio.PauseMusic(false);
                _acc = 0;
                _input.Reset();
            }
        }
    }

    private void UpdatePauseInput()
    {
        if (_pauseClosing) return;
        if (Pressed("pause")) { _pauseClosing = true; return; }
        if (!_click) return;
        for (int i = 0; i < PauseButtons.Length; i++)
        {
            if (!HitSprite("pause", PauseButtons[i].Costume, _pauseX - 180, PauseButtons[i].Y)) continue;
            Click();
            if (i == 0) _pauseClosing = true;
            else if (i == 1) OpenSettings(GameState.Paused);
            else LeaveToMapOrTitle();
            return;
        }
    }

    private void DrawPause()
    {
        _scene.BeginStage();
        Stage("menudialog", "costume2", _pauseX, 0);
        foreach (var (costume, y) in PauseButtons)
            Stage("pause", costume, _pauseX - 180, y);
        // the blank button in the pause art gets its label here
        _ui.Text("settings", AppWindow.VW / 2f + (float)(_pauseX - 180) * 2, AppWindow.VH / 2f - (float)PauseButtons[1].Y * 2 + 8, 22,
            new SKColor(30, 30, 30), false, 1);
    }
    // ================================================================ save slots

    private void OpenSlots(bool loadMode)
    {
        Click();
        _slotLoadMode = loadMode;
        _slotSel = Math.Clamp(_settings.LastSlot, 0, SaveManager.SlotCount - 1);
        _eraseConfirm = -1;
        _state = GameState.Slots;
        _panelMode = 1;
        _panelX = -25;
    }

    private string SlotLevelName(SaveSlot s)
    {
        string w = _worlds.Get(s.World)?.Name ?? "";
        string l = _levels.Get(s.Level) is { } lv && lv.Name.Length > 0 ? lv.Name : s.Level;
        return w.Length > 0 ? w : l;
    }

    private void EnterSlot(SaveSlot s)
    {
        Click();
        _slot = s;
        _settings.LastSlot = s.Index;
        if (!s.Used)
        {
            s.Completed.Clear(); s.Flags.Clear(); s.Gems = 0; s.PlayTime = 0;
            string? sw = _levels.StartWorldId;
            s.World = sw != null && _worlds.Get(sw) != null ? sw : (_worlds.Ids.FirstOrDefault() ?? "");
            s.Node = _worlds.Get(s.World)?.Start ?? "";
            s.Level = _levels.StartLevel ?? "";
            _saves.Save(s);
        }
        _map = _worlds.Get(s.World);
        if (_map != null)
        {
            if (_map.Node(s.Node) == null) s.Node = _map.Start;
            _inMap = true;
            ShowMap();
        }
        else
        {
            _inMap = false;
            string id = _levels.Has(s.Level) ? s.Level : (_levels.StartLevel ?? "");
            BeginLoading(id);
        }
    }

    private void UpdateSlots()
    {
        if (_eraseConfirm >= 0) return;
        int n = SaveManager.SlotCount;
        if (Pad.KeyPressed(KeyboardKey.Down) || Pad.KeyPressed(KeyboardKey.S)) _slotSel = (_slotSel + 1) % n;
        if (Pad.KeyPressed(KeyboardKey.Up) || Pad.KeyPressed(KeyboardKey.W)) _slotSel = (_slotSel + n - 1) % n;
        if (Pad.KeyPressed(KeyboardKey.Delete) && _saves.Slots[_slotSel].Used) { _eraseConfirm = _slotSel; return; }
        if (Pad.KeyPressed(KeyboardKey.Escape)) { Click(); LeaveSlots(); return; }
        if (Pad.KeyPressed(KeyboardKey.Enter) || Pad.KeyPressed(KeyboardKey.Space)) TryPlaySlot(_slotSel);
    }

    private void TryPlaySlot(int i)
    {
        var s = _saves.Slots[i];
        if (_slotLoadMode && !s.Used) return;
        EnterSlot(s);
    }

    private void LeaveSlots()
    {
        _state = GameState.Title;
        ResetTitle();
        _panelMenuX = 0;
    }

    private void DrawSlots()
    {
        DrawTitle(false);
        float W = 700, X = (AppWindow.VW - W) / 2f;
        _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, new SKColor(0, 0, 0, 120));
        _ui.Text(_slotLoadMode ? "Load game" : "Start game", AppWindow.VW / 2f, 100, 48, SKColors.White, true, 1, true);
        _ui.Text("Pick a save slot", AppWindow.VW / 2f, 134, 20, new SKColor(255, 255, 255, 200), false, 1);

        for (int i = 0; i < SaveManager.SlotCount; i++)
        {
            var s = _saves.Slots[i];
            float y = 170 + i * 150;
            bool disabled = _slotLoadMode && !s.Used;
            bool hot = _eraseConfirm < 0 && _mouse.X >= X && _mouse.X < X + W && _mouse.Y >= y && _mouse.Y < y + 130;
            if (hot && _click && !(_mouse.X > X + W - 140)) { _slotSel = i; TryPlaySlot(i); return; }
            if (hot && (Raylib.GetMouseDelta().LengthSquared() > 0.5f)) _slotSel = i;
            bool sel = _slotSel == i;
            _ui.Rect(X, y, W, 130, sel ? new SKColor(50, 54, 100, 240) : new SKColor(24, 26, 56, 235), 16,
                sel ? new SKColor(255, 190, 60) : new SKColor(255, 255, 255, 100), sel ? 4 : 2);
            var txt = disabled ? new SKColor(255, 255, 255, 110) : SKColors.White;
            _ui.Text($"Slot {i + 1}", X + 28, y + 44, 30, txt, true, 0, true);
            if (s.Used)
            {
                _ui.Text(SlotLevelName(s), X + 28, y + 78, 22, new SKColor(255, 230, 130), true);
                _ui.Text($"{s.Gems} gems   -   {SaveSlot.FormatTime(s.PlayTime)}   -   {s.Completed.Count} levels done", X + 28, y + 108, 18,
                    new SKColor(255, 255, 255, 200));
                bool eraseHot = _eraseConfirm < 0 && _mouse.X > X + W - 130 && _mouse.X < X + W - 20 && _mouse.Y > y + 20 && _mouse.Y < y + 62;
                _ui.Rect(X + W - 130, y + 20, 110, 42, eraseHot ? new SKColor(220, 70, 70) : new SKColor(120, 40, 50), 10, new SKColor(255, 255, 255, 90));
                _ui.Text("Erase", X + W - 75, y + 48, 20, SKColors.White, true, 1);
                if (eraseHot && _click) { Click(); _eraseConfirm = i; }
            }
            else
            {
                _ui.Text(disabled ? "empty" : "New game", X + 28, y + 86, 24, disabled ? new SKColor(255, 255, 255, 110) : new SKColor(160, 255, 160), true);
            }
        }
        _ui.Text("Enter: play   Delete: erase   Esc: back", AppWindow.VW / 2f, 650, 18, new SKColor(255, 255, 255, 190), false, 1);

        if (_eraseConfirm >= 0)
        {
            float cx = AppWindow.VW / 2f, cy = AppWindow.VH / 2f;
            _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, new SKColor(0, 0, 0, 150));
            _ui.Rect(cx - 220, cy - 100, 440, 200, new SKColor(20, 22, 50, 250), 16, new SKColor(255, 255, 255, 140), 3);
            _ui.Text($"Erase slot {_eraseConfirm + 1}?", cx, cy - 40, 34, SKColors.White, true, 1, true);
            _ui.Text("This cannot be undone.", cx, cy - 8, 18, new SKColor(255, 255, 255, 210), false, 1);
            bool yes = _ui.Button("Yes", cx - 170, cy + 30, 150, 46, _mouse) && _click;
            bool no = (_ui.Button("No", cx + 20, cy + 30, 150, 46, _mouse) && _click)
                      || Pad.KeyPressed(KeyboardKey.Escape) || Pad.KeyPressed(KeyboardKey.N);
            if (yes || Pad.KeyPressed(KeyboardKey.Y)) { _saves.Erase(_eraseConfirm); _audio.PlaySfx("pop"); _eraseConfirm = -1; }
            else if (no) { Click(); _eraseConfirm = -1; }
        }
    }

    // ================================================================ overworld map

    private bool NodeDone(string id) => _slot != null && _map != null && _slot.Completed.Contains(_map.Id + "/" + id);
    private bool NodeOpen(MapNode n) => n.Requires.All(NodeDone);

    private double MapWidth => Math.Max(_map?.PixelWidth ?? 1280, (_map?.Nodes.Max(n => n.X) ?? 0) + 160);
    private double MapHeight => Math.Max(_map?.PixelHeight ?? 720, (_map?.Nodes.Max(n => n.Y) ?? 0) + 160);

    /// <summary>Where the map should be scrolled to: centred on the cursor node (path mode) or the avatar (free roam).</summary>
    private (double x, double y) MapTargetScroll()
    {
        double fx = _map != null && _map.FreeRoam ? _frX : (_map?.Node(_slot?.Node ?? "")?.X ?? 0);
        double fy = _map != null && _map.FreeRoam ? _frY : (_map?.Node(_slot?.Node ?? "")?.Y ?? 0);
        return (Math.Clamp(fx - AppWindow.VW / 2.0, 0, Math.Max(0, MapWidth - AppWindow.VW)),
                Math.Clamp(fy - AppWindow.VH / 2.0, 0, Math.Max(0, MapHeight - AppWindow.VH)));
    }

    private void SnapMapScroll()
    {
        PlaceAvatarAtNode();
        (_mapScroll, _mapScrollY) = MapTargetScroll();
    }

    private void PlaceAvatarAtNode()
    {
        var n = _map?.Node(_slot?.Node ?? "") ?? _map?.Nodes.FirstOrDefault();
        if (n == null) return;
        _frX = n.X; _frY = n.Y + 14;
        if (_map != null && !AvatarFits(_frX, _frY)) { _frX = n.X; _frY = n.Y; }
    }

    private void ShowMap()
    {
        _states.Clear();
        SaveSlotNow();
        EnsureMusic();
        _state = GameState.Map;
        _world = null;
        SnapMapScroll();
        _input.Reset();
    }

    private (float x, float y) NodePos(MapNode n) => ((float)(n.X - _mapScroll), (float)(n.Y - _mapScrollY));

    private static bool Held(Settings st, string action)
    {
        if (!st.Keys.TryGetValue(action, out var keys)) return false;
        foreach (int k in keys)
            if (k != 0 && Raylib.IsKeyDown((KeyboardKey)k)) return true;
        return false;
    }

    private void UpdateMap(float dt)
    {
        if (_map == null || _slot == null || _map.Nodes.Count == 0) { ToTitle(); return; }
        _slot.PlayTime += dt;
        _mapTime += dt;
        var (tx, ty) = MapTargetScroll();
        double k = Math.Min(1.0, dt * 6);
        _mapScroll += (tx - _mapScroll) * k;
        _mapScrollY += (ty - _mapScrollY) * k;

        if (Pad.KeyPressed(KeyboardKey.Escape) || Pressed("pause")) { Click(); ToTitle(); return; }
        if (_map.FreeRoam) { UpdateFreeRoam(dt); return; }

        var cur = _map.Node(_slot.Node) ?? _map.Nodes[0];
        int dx = (Pressed("right") || Pad.KeyPressed(KeyboardKey.Right) ? 1 : 0) - (Pressed("left") || Pad.KeyPressed(KeyboardKey.Left) ? 1 : 0);
        int dy = (Pressed("down") || Pad.KeyPressed(KeyboardKey.Down) ? 1 : 0) - (Pressed("up") || Pad.KeyPressed(KeyboardKey.Up) ? 1 : 0);
        if (dx != 0 || dy != 0) MoveCursor(cur, dx, dy);

        if (_click)
        {
            foreach (var n in _map.Nodes)
            {
                var (x, y) = NodePos(n);
                if ((_mouse.X - x) * (_mouse.X - x) + (_mouse.Y - y) * (_mouse.Y - y) > 28 * 28) continue;
                if (n == cur) { EnterNode(n); return; }
                if (NodeOpen(n)) { _slot.Node = n.Id; _audio.PlaySfx("click"); }
                return;
            }
        }
        if (Pressed("use") || Pad.KeyPressed(KeyboardKey.Enter) || Pad.KeyPressed(KeyboardKey.Space)) EnterNode(cur);
    }

    /// <summary>The node the free-roam avatar is standing close enough to enter, if any.</summary>
    private MapNode? NearNode()
    {
        MapNode? best = null;
        double bd = 34 * 34;
        foreach (var n in _map!.Nodes)
        {
            double d = (n.X - _frX) * (n.X - _frX) + (n.Y - (_frY - 14)) * (n.Y - (_frY - 14));
            if (d < bd) { bd = d; best = n; }
        }
        return best;
    }

    private bool AvatarFits(double x, double y)
    {
        // the avatar's feet box: 18 wide, 12 tall
        return _map!.WalkableAtPixel(x - 9, y - 4) && _map.WalkableAtPixel(x + 9, y - 4) &&
               _map.WalkableAtPixel(x - 9, y + 8) && _map.WalkableAtPixel(x + 9, y + 8);
    }

    private void UpdateFreeRoam(float dt)
    {
        double dx = (Held(_settings, "right") || Raylib.IsKeyDown(KeyboardKey.Right) ? 1 : 0) - (Held(_settings, "left") || Raylib.IsKeyDown(KeyboardKey.Left) ? 1 : 0);
        double dy = (Held(_settings, "down") || Raylib.IsKeyDown(KeyboardKey.Down) ? 1 : 0) - (Held(_settings, "up") || Raylib.IsKeyDown(KeyboardKey.Up) ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            double len = Math.Sqrt(dx * dx + dy * dy);
            double sp = 150 * _map!.SpeedAtPixel(_frX, _frY) * dt / len;
            double nx = _frX + dx * sp, ny = _frY + dy * sp;
            bool stuck = !AvatarFits(_frX, _frY);      // standing on a blocked tile (placed by the editor): let them walk out
            if (stuck || AvatarFits(nx, _frY)) _frX = nx;
            if (stuck || AvatarFits(_frX, ny)) _frY = ny;
            if (dx != 0) _frDir = dx > 0 ? 1 : -1;
            _frAnim += dt * 10;
        }
        else _frAnim = 0;

        var near = NearNode();
        if (near != null && (Pressed("use") || Pad.KeyPressed(KeyboardKey.Enter) || Pad.KeyPressed(KeyboardKey.Space))) EnterNode(near);
    }

    private void MoveCursor(MapNode cur, int dx, int dy)
    {
        MapNode? best = null;
        double bestScore = 0.3;
        double len = Math.Sqrt(dx * dx + dy * dy);
        foreach (var id in cur.Links)
        {
            var n = _map!.Node(id);
            if (n == null || !NodeOpen(n)) continue;
            double vx = n.X - cur.X, vy = n.Y - cur.Y, vl = Math.Sqrt(vx * vx + vy * vy);
            if (vl < 1) continue;
            double score = (vx * dx + vy * dy) / (vl * len);
            if (score > bestScore) { bestScore = score; best = n; }
        }
        if (best != null) { _slot!.Node = best.Id; _audio.PlaySfx("click"); }
    }

    private void EnterNode(MapNode n)
    {
        if (_slot == null) return;
        if (!NodeOpen(n)) { _audio.PlaySfx("wood_tap"); return; }
        if (n.Portal.Length > 0)
        {
            var target = _worlds.Get(n.Portal);
            if (target == null) return;
            _map = target;
            _slot.World = target.Id;
            _slot.Node = n.PortalNode.Length > 0 && target.Node(n.PortalNode) != null ? n.PortalNode : target.Start;
            SnapMapScroll();
            _audio.PlaySfx("swoosh");
            SaveSlotNow();
            return;
        }
        if (n.Level.Length > 0 && _levels.Has(n.Level))
        {
            _slot.Node = n.Id;
            _inMap = true;
            BeginLoading(n.Level);
        }
    }

    private void DrawMap()
    {
        if (_map == null || _slot == null) return;
        bool free = _map.FreeRoam;
        int VW = AppWindow.VW, VH = AppWindow.VH;
        Raylib.DrawRectangleGradientV(0, 0, VW, VH, new Color((byte)90, (byte)170, (byte)235, (byte)255), new Color((byte)200, (byte)235, (byte)255, (byte)255));
        foreach (var layer in _map.Layers) _scene.DrawMapLayer(layer, _mapScroll, _mapScrollY, 0, 0, VW, VH);
        _scene.DrawOverworldTiles(_map, _mapScroll, _mapScrollY, 0, 0, VW, VH, _scene.AnimTime);

        // paths
        foreach (var a in _map.Nodes)
            foreach (var id in a.Links)
            {
                var b = _map.Node(id);
                if (b == null || string.CompareOrdinal(a.Id, b.Id) > 0) continue;
                var (ax, ay) = NodePos(a); var (bx, by) = NodePos(b);
                bool open = NodeOpen(a) && NodeOpen(b);
                _ui.Line(ax, ay, bx, by, new SKColor(60, 40, 20, 230), 12);
                _ui.Line(ax, ay, bx, by, open ? new SKColor(250, 225, 150) : new SKColor(150, 130, 110), 7);
            }

        var cur = _map.Node(_slot.Node);
        foreach (var n in _map.Nodes)
        {
            var (x, y) = NodePos(n);
            bool open = NodeOpen(n), done = NodeDone(n.Id), portal = n.Portal.Length > 0;
            var fill = !open ? new SKColor(120, 120, 130) : portal ? new SKColor(170, 110, 255) : done ? new SKColor(90, 200, 100) : new SKColor(255, 190, 60);
            _ui.Circle(x, y, 26, new SKColor(40, 30, 20));
            _ui.Circle(x, y, 21, fill);
            _ui.Text(portal ? ">" : done ? "v" : open ? "" : "x", x, y + 8, 22, SKColors.White, true, 1);
            _ui.Text(n.Name, x, y + 52, 18, SKColors.White, true, 1, true);
        }
        if (free)
        {
            float ax = (float)(_frX - _mapScroll), ay = (float)(_frY - _mapScrollY);
            float bob = (float)Math.Abs(Math.Sin(_frAnim)) * 3;
            _ui.Circle(ax, ay + 6, 11, new SKColor(0, 0, 0, 90));
            _ui.Circle(ax, ay - 12 - bob, 13, new SKColor(40, 30, 20));
            _ui.Circle(ax, ay - 12 - bob, 11, new SKColor(255, 150, 60));
            _ui.Circle(ax + 4 * _frDir, ay - 15 - bob, 3, SKColors.White);
            _ui.Circle(ax + 5 * _frDir, ay - 15 - bob, 1.5f, SKColors.Black);
            var nn = NearNode();
            if (nn != null)
                _ui.Text(NodeOpen(nn) ? $"{nn.Name}  -  Enter" : $"{nn.Name}  (locked)", ax, ay - 42, 18, SKColors.White, true, 1, true);
        }
        else if (cur != null)
        {
            var (x, y) = NodePos(cur);
            float r = 32 + (float)Math.Sin(_mapPulse) * 3;
            _ui.Circle(x, y - 54 + (float)Math.Sin(_mapPulse * 1.3) * 4, 10, new SKColor(255, 80, 80));
            _ui.Line(x - 0, y - 44, x, y - 30, new SKColor(255, 80, 80), 5);
            _ui.Rect(x - r, y - r, r * 2, r * 2, new SKColor(0, 0, 0, 0), r, SKColors.White, 3);
        }

        // header + footer
        _ui.Rect(12, 12, 360, 52, new SKColor(0, 0, 0, 130), 12);
        _ui.Text(_map.Name, 28, 48, 30, SKColors.White, true, 0, true);
        _ui.Rect(VW - 292, 12, 280, 52, new SKColor(0, 0, 0, 130), 12);
        _ui.Text($"{_slot.Gems} gems   {SaveSlot.FormatTime(_slot.PlayTime)}", VW - 28, 46, 22, new SKColor(255, 230, 90), true, 2, true);
        var focus = free ? NearNode() : cur;
        string status = focus == null ? "explore" : !NodeOpen(focus) ? "locked" : focus.Portal.Length > 0 ? "Enter: travel" : "Enter: play";
        _ui.Rect(VW / 2f - 260, VH - 74, 520, 58, new SKColor(0, 0, 0, 150), 14);
        _ui.Text(focus?.Name ?? _map.Name, VW / 2f, VH - 46, 28, SKColors.White, true, 1, true);
        _ui.Text($"{status}    arrows: {(free ? "walk" : "move")}    Esc: save & exit", VW / 2f, VH - 24, 16, new SKColor(255, 255, 255, 200), false, 1);
    }

    // ================================================================ settings

    private void OpenSettings(GameState from)
    {
        Click();
        _settingsReturn = from;
        _settingsScreen.Open();
        _state = GameState.Settings;
        if (from == GameState.Title) { _panelMode = 1; _panelX = -25; }
    }

    private void LeaveSettings()
    {
        _settings.Save(_settingsPath);
        if (_settingsReturn == GameState.Title)
        {
            _state = GameState.Title;
            ResetTitle();
            _panelMenuX = 0;
        }
        else
        {
            _state = GameState.Paused;
            _pauseClosing = false;
        }
    }

    private void DrawSettings()
    {
        if (_settingsReturn == GameState.Paused && _world != null)
        {
            _scene.DrawWorld(_world);
            _scene.BeginStage();
            Stage("dialog", "costume5", 0, 0, BgSize, 215);
        }
        else
        {
            DrawTitle(false);
        }
        if (_settingsScreen.Run(_mouse)) LeaveSettings();
    }

    // ================================================================ transitions

    private void WipeTick()
    {
        if (_wipePhase == 1)
        {
            int steps = _wipeWin ? 2 : 3;
            for (int i = 0; i < steps; i++)
                _wipeX += _wipeWin ? (-2 - _wipeX / 16) / 0.8 : (-2 - _wipeX / 15) / 1.3;

            if (!_wipeLoaded && _wipeX > -280)
            {
                _wipeLoaded = true;
                ApplyLoad(_req);
                if (_state != GameState.Wipe) return;
            }
            double tol = _wipeWin ? 40 : 30;
            if (_wipeX > -2 - tol) { _wipePhase = 2; _wipeX = -25; }
        }
        else
        {
            _wipeX += _wipeX / 8 + 20;
            if (Math.Abs(_wipeX) > 780)
            {
                _state = GameState.Playing;
                _acc = 0;
                _input.Reset();
            }
        }
    }
    // ================================================================ playing

    private void UpdatePlaying(float dt)
    {
        if (Pressed("pause"))
        {
            EnterPause();
            return;
        }
        if (_world == null) return;
        if (_slot != null) _slot.PlayTime += dt;
        if (Pad.KeyPressed(KeyboardKey.F1)) _world.GodMode = !_world.GodMode;
        if (Pressed("savestate")) _world.RequestSaveState();
        if (Pressed("loadstate")) _world.RequestLoadState();
        if (_toastT > 0) _toastT -= dt;
        if (Pad.KeyPressed(KeyboardKey.F5)) _world.Load(_world.Level, _world.LevelId, LoadKind.Respawn, 0);
        if (_introT < 6) return;   // the pink loading fade is still fully opaque

        _acc += dt;
        while (_acc >= Step && _state == GameState.Playing && _world != null)
        {
            _acc -= Step;
            _scene.AnimTime += Step;
            _world.Tick(_input.Poll());
            HandlePending();
        }
    }

    private void UpdateDialogInput()
    {
        if (Pad.KeyPressed(KeyboardKey.E) || Pad.KeyPressed(KeyboardKey.Enter) || Pressed("use") ||
            Pad.KeyPressed(KeyboardKey.Escape) || _click)
        {
            _state = GameState.Playing;
            _world?.DialogClosed();
            _input.Reset();
            _acc = 0;
        }
    }

    // ================================================================ drawing

    private void Draw()
    {
        _win.BeginVirtual();
        _ui.BeginFrame();

        switch (_state)
        {
            case GameState.Title: DrawTitle(true); break;
            case GameState.Settings: DrawSettings(); break;
            case GameState.Slots: UpdateSlots(); DrawSlots(); break;
            case GameState.Map: DrawMap(); break;
            case GameState.Loading: DrawLoading(); break;
            case GameState.End: DrawEnd(); break;
            default: DrawPlayfield(); break;
        }

        if (_settings.ShowStats)
            _ui.Text($"{Raylib.GetFPS()} fps", 12, AppWindow.VH - 12, 16, SKColors.White, false, 0, true);
        _ui.EndFrame();
        _win.EndVirtual();
    }

    private void DrawEnd()
    {
        DrawTitleBackdrop();
        float cx = AppWindow.VW / 2f;
        _ui.Rect(cx - 300, 170, 600, 330, new SKColor(10, 10, 40, 220), 18, new SKColor(255, 255, 255, 120));
        _ui.Text("You finished the last level!", cx, 270, 40, SKColors.White, true, 1, true);
        _ui.Text("Thanks for playing.", cx, 320, 24, new SKColor(255, 255, 255, 220), false, 1);
        int hit = _endMenu.Run(_ui, _mouse, new[] { "Back to title" }, cx - 150, 380, 300);
        if (hit == 0) { Click(); ToTitle(); }
    }

    private void DrawPlayfield()
    {
        if (_world == null) return;
        _scene.DrawWorld(_world);
        _scene.BeginStage();

        // pink loading fade (only just after the level appeared)
        if (_state == GameState.Playing && _introT < 26)
        {
            int a = _introT < 6 ? 255 : Math.Max(0, 255 - (_introT - 6) * 255 / 20);
            if (a > 0) Bg("dialog", "costume10", a);
        }

        // HUD
        string name = _world.Level.Name.Length > 0 && _world.Level.Name != "test" ? _world.Level.Name : _world.LevelId;
        _ui.Rect(12, 12, 360, 44, new SKColor(0, 0, 0, 120), 10);
        _ui.Text($"Gems {_world.Coins}", 26, 43, 26, new SKColor(255, 230, 90), true, 0, true);
        _ui.Text(name, 358, 43, 20, SKColors.White, false, 2, true);
        if (_toastT > 0) _ui.Text(_toast, AppWindow.VW / 2f, 90, 24, SKColors.White, true, 1, true);
        if (_world.GodMode) _ui.Text("FLY MODE (F1)", AppWindow.VW / 2f, AppWindow.VH - 20, 18, new SKColor(255, 120, 120), true, 1, true);

        switch (_state)
        {
            case GameState.Paused:
                DrawPause();
                break;

            case GameState.Dialog:
            {
                float bw = 720, bx = (AppWindow.VW - bw) / 2f;
                _ui.Rect(bx, 470, bw, 190, new SKColor(10, 10, 40, 235), 16, new SKColor(255, 255, 255, 160), 3);
                _ui.Text(_dialogText, bx + 40, 540, 32, SKColors.White, false, 0, true);
                _ui.Text("press E / Enter", bx + bw - 20, 640, 16, new SKColor(255, 255, 255, 170), false, 2);
                UpdateDialogInput();
                break;
            }

            case GameState.Wipe:
                Stage("transition", _wipeWin ? "costume2" : "costume1", _wipeX, 0, BgSize);
                if (_wipeWin) Stage("transition", "costume4", _wipeX, 0, BgSize);
                break;
        }
    }

    public void Dispose()
    {
        _audio.Dispose();
        _ui.Dispose();
        _sprites.Dispose();
        _win.Dispose();
    }
}
