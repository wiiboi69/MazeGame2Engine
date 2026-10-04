using System.Numerics;
using MazeGame.Core;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame;

public enum GameState { Title, Settings, Playing, Paused, Dialog, Wipe, End }

/// <summary>The playable game: title screen, settings, pause menu, dialogs, wipes, HUD and level flow.</summary>
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

    private World? _world;
    private GameState _state = GameState.Title;
    private GameState _settingsReturn = GameState.Title;
    private double _acc;
    private double _titleScroll;
    private readonly UiMenu _titleMenu = new(), _pauseMenu = new(), _settingsMenu = new(), _endMenu = new();

    // wipe transition
    private float _wipeT;
    private bool _wipeCovering;
    private LoadRequest _wipeRequest;

    private string _dialogText = "";
    private string? _rebinding;
    private static readonly string[] BindActions = { "left", "right", "up", "down", "use" };

    public Game(string assetsDir, string levelsDir, string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = Settings.Load(settingsPath);
        _win = new AppWindow("Maze Game 2", _settings.Fullscreen);
        _sprites = new SpriteLibrary(assetsDir);
        _scene = new SceneRenderer(_sprites);
        _ui = new SkiaUi();
        _audio = new AudioEngine(Path.Combine(assetsDir, "audio"))
        {
            SfxVolume = _settings.SfxVolume,
            MusicVolume = _settings.MusicVolume,
        };
        _input = new GameInput(_settings);
        _levels = LevelLibrary.LoadDirectory(levelsDir);
        AppWindow.SetAspect(_settings.AspectRatio);
        _win.SetFpsLimit(_settings.FpsLimit);
    }

    // ================================================================ main loop

    public void Run()
    {
        while (!_win.ShouldClose)
        {
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _audio.Update();
            if (Raylib.IsKeyPressed(KeyboardKey.F11)) { _win.ToggleFullscreen(); _settings.Fullscreen = Raylib.IsWindowFullscreen(); }

            Update(dt);
            Draw(dt);
            if (_quit) break;
        }
        _settings.Save(_settingsPath);
    }

    private bool _quit;

    private void Update(float dt)
    {
        switch (_state)
        {
            case GameState.Playing:
                if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.P))
                {
                    _state = GameState.Paused;
                    _audio.PauseMusic(true);
                    break;
                }
                if (Raylib.IsKeyPressed(KeyboardKey.F1) && _world != null) _world.GodMode = !_world.GodMode;
                if (Raylib.IsKeyPressed(KeyboardKey.F5) && _world != null) _world.Load(_world.Level, _world.LevelNumber, LoadKind.Respawn, 0);
                _acc += dt;
                while (_acc >= Step && _state == GameState.Playing && _world != null)
                {
                    _acc -= Step;
                    _world.SavePrevious();
                    _world.Tick(_input.Poll());
                    HandlePending();
                }
                // how far we are between the last logic tick and the next, used to smooth drawing
                if (_world != null) _world.Alpha = Math.Clamp(_acc / Step, 0, 1);
                break;

            case GameState.Wipe:
                UpdateWipe(dt);
                break;
        }
    }

    // ================================================================ level flow

    private void StartGame()
    {
        int start = _levels.StartLevel;
        var level = _levels.Get(start);
        if (level == null) return;
        _world = new World(level, start, _levels.Get);
        _world.SoundRequested += n => _audio.PlaySfx(n);
        _world.DialogRequested += OpenDialog;
        _audio.PlayMusic("my_song_68");
        _world.SavePrevious();
        _acc = 0;
        _input.Reset();
        _state = GameState.Playing;
    }

    private void OpenDialog(string text)
    {
        _dialogText = text;
        _state = GameState.Dialog;
    }

    private void HandlePending()
    {
        if (_world?.Pending is not { } req) return;
        if (req.Wipe)
        {
            _wipeRequest = req;
            _wipeT = 0;
            _wipeCovering = true;
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
        var level = _levels.Get(req.Level);
        if (level == null)
        {
            if (req.Win) { _state = GameState.End; return; }
            level = _world.Level;
            req = new LoadRequest(_world.LevelNumber, LoadKind.Respawn, false);
        }
        _world.Load(level, req.Level, req.Kind, req.EntrySide);
        _world.SavePrevious();   // no smoothing across a level change
        _input.Reset();
    }

    private void UpdateWipe(float dt)
    {
        _wipeT += dt / 0.35f;
        if (_wipeCovering && _wipeT >= 1f)
        {
            _wipeCovering = false;
            _wipeT = 0;
            ApplyLoad(_wipeRequest);
            if (_state == GameState.End) return;
        }
        else if (!_wipeCovering && _wipeT >= 1f)
        {
            _state = GameState.Playing;
            _acc = 0;
        }
    }

    private void ToTitle()
    {
        _audio.StopMusic();
        _world = null;
        _state = GameState.Title;
    }

    // ================================================================ drawing

    private void Draw(float dt)
    {
        _win.BeginVirtual();
        var mouse = _win.Mouse;
        _ui.BeginFrame();

        switch (_state)
        {
            case GameState.Title: DrawTitle(dt, mouse); break;
            case GameState.Settings: DrawSettings(dt, mouse); break;
            case GameState.End: DrawEnd(dt, mouse); break;
            default: DrawPlayfield(mouse); break;
        }

        if (_settings.ShowStats)
            _ui.Text($"{Raylib.GetFPS()} fps", 12, 22, 16, SKColors.White, false, 0, true);
        _ui.EndFrame();
        _win.EndVirtual();
    }

    private LevelData? BackdropLevel() => _levels.Get(_levels.StartLevel);

    private void DrawBackdropOnly(float dt, double speed)
    {
        _titleScroll += dt * speed;
        var lvl = BackdropLevel() ?? new LevelData(10, 10);
        _scene.Ppu = 2;
        _scene.CenterX = _titleScroll; _scene.CenterY = 300;
        _scene.DrawBackground(lvl, _titleScroll, 300);
    }

    private void DrawTitle(float dt, Vector2 mouse)
    {
        DrawBackdropOnly(dt, 60);
        var logo = _sprites.Get("mainmenu", "costume1");
        if (logo != null)
        {
            double scale = Math.Min(1.0, 420.0 / Math.Max(1f, logo.SrcW));
            _scene.CenterX = 0; _scene.CenterY = 0;
            _scene.DrawSprite(logo, 0, 120, scale);
        }
        else
        {
            _ui.Text("MAZE GAME 2", SkiaUi.CX, 190, 72, SKColors.White, true, 1, true);
        }
        _ui.Text("C# port: raylib + SkiaSharp + OpenAL", SkiaUi.CX, 250, 18, new SKColor(255, 255, 255, 200), false, 1, true);

        int hit = _titleMenu.Run(_ui, mouse, new[] { "Play", "Settings", "Quit" }, SkiaUi.CX - 150, 330, 300);
        if (hit >= 0) _audio.PlaySfx("click");
        if (hit == 0) StartGame();
        else if (hit == 1) { _settingsReturn = GameState.Title; _state = GameState.Settings; }
        else if (hit == 2) _quit = true;
        _ui.Text("Arrows / WASD: move   E / Enter: use   F11: fullscreen", SkiaUi.CX, 690, 16, new SKColor(255, 255, 255, 190), false, 1, true);
    }

    private void DrawEnd(float dt, Vector2 mouse)
    {
        DrawBackdropOnly(dt, 40);
        _ui.Rect(SkiaUi.CX - 300, 170, 600, 330, new SKColor(10, 10, 40, 220), 18, new SKColor(255, 255, 255, 120));
        _ui.Text("You finished the last level!", SkiaUi.CX, 270, 40, SKColors.White, true, 1, true);
        _ui.Text("Thanks for playing.", SkiaUi.CX, 320, 24, new SKColor(255, 255, 255, 220), false, 1);
        int hit = _endMenu.Run(_ui, mouse, new[] { "Back to title" }, SkiaUi.CX - 150, 380, 300);
        if (hit == 0) ToTitle();
    }

    private void DrawPlayfield(Vector2 mouse)
    {
        if (_world == null) return;
        _scene.DrawWorld(_world);

        // HUD
        _ui.Rect(12, 12, 230, 44, new SKColor(0, 0, 0, 120), 10);
        _ui.Text($"Gems {_world.Coins}", 26, 43, 26, new SKColor(255, 230, 90), true, 0, true);
        _ui.Text($"Level {_world.LevelNumber - 1}", 228, 43, 20, SKColors.White, false, 2, true);
        if (_world.GodMode) _ui.Text("FLY MODE (F1)", SkiaUi.CX, 700, 18, new SKColor(255, 120, 120), true, 1, true);

        if (_state == GameState.Paused)
        {
            _ui.Rect(0, 0, SkiaUi.W, 720, new SKColor(0, 0, 0, 150));
            _ui.Text("Paused", SkiaUi.CX, 200, 56, SKColors.White, true, 1, true);
            int hit = _pauseMenu.Run(_ui, mouse, new[] { "Resume", "Settings", "Quit to title" }, SkiaUi.CX - 150, 260, 300);
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.P)) hit = 0;
            if (hit >= 0) _audio.PlaySfx("click");
            if (hit == 0) { _state = GameState.Playing; _audio.PauseMusic(false); _acc = 0; _input.Reset(); }
            else if (hit == 1) { _settingsReturn = GameState.Paused; _state = GameState.Settings; }
            else if (hit == 2) ToTitle();
        }
        else if (_state == GameState.Dialog)
        {
            _ui.Rect(SkiaUi.CX - 360, 470, 720, 190, new SKColor(10, 10, 40, 235), 16, new SKColor(255, 255, 255, 160), 3);
            _ui.Text(_dialogText, 160, 540, 32, SKColors.White, false, 0, true);
            _ui.Text("press E / Enter", SkiaUi.CX + 320, 640, 16, new SKColor(255, 255, 255, 170), false, 2);
            if (Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Enter) ||
                Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _state = GameState.Playing;
                _input.Reset();
                _acc = 0;
            }
        }
        else if (_state == GameState.Wipe)
        {
            float t = Math.Clamp(_wipeT, 0f, 1f);
            float cover = _wipeCovering ? t : 1f - t;      // 0 = clear, 1 = fully covered
            _ui.Rect(0, 0, SkiaUi.W, 720 * cover, new SKColor(0, 0, 0, 255));
        }
    }

    // ================================================================ settings

    private void DrawSettings(float dt, Vector2 mouse)
    {
        if (_world != null && _settingsReturn == GameState.Paused) _scene.DrawWorld(_world);
        else DrawBackdropOnly(dt, 30);
        _ui.Rect(0, 0, SkiaUi.W, 720, new SKColor(0, 0, 0, 170));
        _ui.Text("Settings", SkiaUi.CX, 80, 52, SKColors.White, true, 1, true);

        var labels = new List<string>
        {
            $"Music volume: {(int)Math.Round(_settings.MusicVolume * 100)}%",
            $"Sound volume: {(int)Math.Round(_settings.SfxVolume * 100)}%",
            $"Fullscreen: {(_settings.Fullscreen ? "on" : "off")}",
            $"Show fps: {(_settings.ShowStats ? "on" : "off")}",
            $"Aspect ratio: {_settings.AspectRatio}  (experimental)",
            $"Max FPS: {(_settings.FpsLimit <= 0 ? "Unlimited" : _settings.FpsLimit.ToString())}",
        };
        foreach (var a in BindActions)
        {
            string keyName = _rebinding == a ? "press a key..." :
                string.Join(" / ", _settings.Keys[a].Select(k => ((KeyboardKey)k).ToString()));
            labels.Add($"{char.ToUpper(a[0])}{a.Substring(1)}: {keyName}");
        }
        labels.Add("Back");

        if (_rebinding != null)
        {
            int key = Raylib.GetKeyPressed();
            if (key != 0)
            {
                if ((KeyboardKey)key != KeyboardKey.Escape) _settings.Keys[_rebinding] = new[] { key };
                _rebinding = null;
            }
        }

        int sel = RunScrollableSettings(labels.ToArray(), mouse);
        int cur = _settingsMenu.Selected;

        // left / right adjusts sliders
        int dir = (Raylib.IsKeyPressed(KeyboardKey.Right) ? 1 : 0) - (Raylib.IsKeyPressed(KeyboardKey.Left) ? 1 : 0);
        if (dir != 0 && _rebinding == null)
        {
            if (cur == 0) { _settings.MusicVolume = Math.Clamp(_settings.MusicVolume + dir * 0.1f, 0, 1); _audio.MusicVolume = _settings.MusicVolume; }
            if (cur == 1) { _settings.SfxVolume = Math.Clamp(_settings.SfxVolume + dir * 0.1f, 0, 1); _audio.SfxVolume = _settings.SfxVolume; _audio.PlaySfx("coin"); }
        }
        if (sel >= 0 && _rebinding == null)
        {
            _audio.PlaySfx("click");
            if (sel == 0) { _settings.MusicVolume = (_settings.MusicVolume + 0.1f) > 1.01f ? 0 : _settings.MusicVolume + 0.1f; _audio.MusicVolume = _settings.MusicVolume; }
            else if (sel == 1) { _settings.SfxVolume = (_settings.SfxVolume + 0.1f) > 1.01f ? 0 : _settings.SfxVolume + 0.1f; _audio.SfxVolume = _settings.SfxVolume; }
            else if (sel == 2) { _win.ToggleFullscreen(); _settings.Fullscreen = Raylib.IsWindowFullscreen(); }
            else if (sel == 3) _settings.ShowStats = !_settings.ShowStats;
            else if (sel == 4) CycleAspect();
            else if (sel == 5) CycleFps();
            else if (sel >= 6 && sel < 6 + BindActions.Length) _rebinding = BindActions[sel - 6];
            else
            {
                _settings.Save(_settingsPath);
                _state = _settingsReturn;
                _settingsMenu.Selected = 0;
            }
        }
        _ui.Text("Left/Right changes volume. Click a key row, then press the new key.", SkiaUi.CX, 690, 16, new SKColor(255, 255, 255, 190), false, 1, true);
    }

    // Scroll state for the settings list (visible rows, top offset in pixels).
    private float _settingsScroll;
    private const float RowH = 46, RowGap = 10, ListTop = 120, ListBottom = 660;

    /// <summary>Settings list in a clipped viewport that scrolls with the mouse wheel, scrollbar and arrow keys.</summary>
    private int RunScrollableSettings(string[] items, Vector2 mouse)
    {
        float x = SkiaUi.CX - 200, w = 400;
        float viewH = ListBottom - ListTop;
        float contentH = items.Length * (RowH + RowGap) - RowGap;
        float maxScroll = Math.Max(0, contentH - viewH);

        // mouse wheel scrolls when the pointer is over the list
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && mouse.X >= x - 20 && mouse.X <= x + w + 40 && mouse.Y >= ListTop && mouse.Y <= ListBottom)
            _settingsScroll -= wheel * 40;

        // keep the keyboard-selected row visible
        int sel0 = _settingsMenu.Selected;
        float rowTop = sel0 * (RowH + RowGap);
        if (rowTop < _settingsScroll) _settingsScroll = rowTop;
        if (rowTop + RowH > _settingsScroll + viewH) _settingsScroll = rowTop + RowH - viewH;
        _settingsScroll = Math.Clamp(_settingsScroll, 0, maxScroll);

        // clip to the viewport and draw rows shifted by the scroll offset
        _ui.PushClip(x - 20, ListTop, x + w + 40, ListBottom);
        // rows are drawn shifted by the scroll, so the screen mouse hit-tests them directly
        int hit = _settingsMenu.RunAt(_ui, mouse, items, x, ListTop - _settingsScroll, w, RowH, RowGap);
        _ui.PopClip();

        // scrollbar
        if (maxScroll > 0)
        {
            float barX = x + w + 14, barH = viewH * viewH / contentH;
            float barY = ListTop + (viewH - barH) * (_settingsScroll / maxScroll);
            _ui.Rect(barX, ListTop, 6, viewH, new SKColor(255, 255, 255, 40), 3);
            _ui.Rect(barX, barY, 6, barH, new SKColor(255, 190, 60, 220), 3);
        }
        return hit;
    }

    private void CycleAspect()
    {
        _settings.AspectRatio = _settings.AspectRatio == "4:3" ? "16:9" : "4:3";
        AppWindow.SetAspect(_settings.AspectRatio);
    }

    private void CycleFps()
    {
        var opts = Settings.FpsOptions;
        int i = Array.IndexOf(opts, _settings.FpsLimit);
        // advance to the next option; an unknown saved value jumps to the first option
        _settings.FpsLimit = i < 0 ? opts[0] : opts[(i + 1) % opts.Length];
        _win.SetFpsLimit(_settings.FpsLimit);
    }

    public void Dispose()
    {
        _audio.Dispose();
        _ui.Dispose();
        _sprites.Dispose();
        _win.Dispose();
    }
}
