using System.Numerics;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame;

/// <summary>
/// Tabbed settings screen (Graphics / Sound / Controls / Game) drawn with Skia. It edits <see cref="Settings"/>
/// directly and applies the changes to the window and audio as soon as they are made.
/// </summary>
public sealed class SettingsScreen
{
    private static readonly string[] Tabs = { "Graphics", "Sound", "Controls", "Game" };

    private static readonly (string Action, string Label)[] BindRows =
    {
        ("left", "Move left"), ("right", "Move right"), ("up", "Jump / up"), ("down", "Crouch / down"),
        ("use", "Interact"), ("z", "Action Z"), ("x", "Action X"), ("pause", "Pause"),
        ("savestate", "Save level state"), ("loadstate", "Load level state"),
    };

    private readonly AppWindow _win;
    private readonly SkiaUi _ui;
    private readonly AudioEngine _audio;
    private readonly Settings _s;
    private readonly SaveManager _saves;

    private int _tab;
    private (string action, int slot)? _capture;
    private string? _confirm;            // "saves" | "settings"
    private int _drag = -1;              // slider being dragged
    private Vector2 _mouse;
    private bool _click, _down;

    public SettingsScreen(AppWindow win, SkiaUi ui, AudioEngine audio, Settings s, SaveManager saves)
    {
        _win = win; _ui = ui; _audio = audio; _s = s; _saves = saves;
    }

    public void Open() { _tab = 0; _capture = null; _confirm = null; _drag = -1; }

    // ================================================================ applying

    public void ApplyAll()
    {
        ApplyAudio();
        ApplyGraphics(initial: true);
    }

    public void ApplyAudio()
    {
        _audio.Master = _s.Mute ? 0f : _s.MasterVolume;
        _audio.MusicVolume = _s.MusicVolume;
        _audio.SfxVolume = _s.SfxVolume;
    }

    private void ApplyGraphics(bool initial = false)
    {
        if (!initial)
        {
            _win.SetAspect(_s.Widescreen);
            _ui.Resize(AppWindow.VW, AppWindow.VH);
            _win.SetWindowScale(_s.WindowScale);
        }
        MazeGame.Core.GameConstants.SetWidescreen(_s.Widescreen);
        if (_s.VSync) Raylib.SetWindowState(ConfigFlags.VSyncHint); else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
        Raylib.SetTargetFPS(_s.FpsLimit);
    }

    // ================================================================ frame

    /// <summary>Draws and updates the screen. Returns true when the player leaves it.</summary>
    public bool Run(Vector2 mouse)
    {
        _mouse = mouse;
        _click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        _down = Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!_down) _drag = -1;

        float W = Math.Min(860, AppWindow.VW - 60), H = 580;
        float X = (AppWindow.VW - W) / 2, Y = (AppWindow.VH - H) / 2;
        bool modal = _confirm != null;

        _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, new SKColor(0, 0, 0, 150));
        _ui.Rect(X, Y, W, H, new SKColor(18, 20, 44, 245), 18, new SKColor(255, 255, 255, 120), 3);
        _ui.Text("Settings", X + 30, Y + 52, 38, SKColors.White, true, 0, true);

        // tabs
        float tw = 130, tx = X + W - 30 - tw * Tabs.Length - 6 * (Tabs.Length - 1);
        for (int i = 0; i < Tabs.Length; i++)
        {
            float bx = tx + i * (tw + 6);
            bool hot = !modal && Inside(bx, Y + 22, tw, 42);
            _ui.Rect(bx, Y + 22, tw, 42, i == _tab ? new SKColor(255, 190, 60) : hot ? new SKColor(70, 76, 130) : new SKColor(40, 44, 80),
                10, new SKColor(255, 255, 255, 80));
            _ui.Text(Tabs[i], bx + tw / 2, Y + 50, 20, i == _tab ? new SKColor(30, 20, 10) : SKColors.White, true, 1);
            if (hot && _click && _capture == null) { _tab = i; Tick(); }
        }
        if (!modal && _capture == null)
        {
            if (Pad.KeyPressed(KeyboardKey.Q) || Pad.KeyPressed(KeyboardKey.PageUp)) { _tab = (_tab + Tabs.Length - 1) % Tabs.Length; Tick(); }
            if (Pad.KeyPressed(KeyboardKey.PageDown)) { _tab = (_tab + 1) % Tabs.Length; Tick(); }
        }
        _ui.Line(X + 24, Y + 78, X + W - 24, Y + 78, new SKColor(255, 255, 255, 70), 2);

        float cx = X + 36, cy = Y + 100, cw = W - 72;
        switch (_tab)
        {
            case 0: Graphics(cx, cy, cw, modal); break;
            case 1: Sound(cx, cy, cw, modal); break;
            case 2: Controls(cx, cy, cw, modal); break;
            default: GameTab(cx, cy, cw, modal); break;
        }

        // footer
        bool back = Button("Back", X + 30, Y + H - 66, 150, 44, modal) ||
                    (!modal && _capture == null && Pad.KeyPressed(KeyboardKey.Escape));
        _ui.Text("Q / PgUp, PgDn: switch tab", X + W - 30, Y + H - 36, 16, new SKColor(255, 255, 255, 170), false, 2);

        if (_confirm != null) DrawConfirm();
        if (back) { _capture = null; _confirm = null; return true; }
        return false;
    }

    // ================================================================ tabs

    private void Graphics(float x, float y, float w, bool modal)
    {
        float row = 58;
        int i = 0;
        Choice("Aspect ratio", x, y + row * i++, w, modal, new[] { "4:3", "16:9" }, _s.Widescreen ? 1 : 0, v =>
        {
            _s.Widescreen = v == 1;
            ApplyGraphics();
        });
        Choice("Window size", x, y + row * i++, w, modal, new[] { "Small", "Medium", "Large" }, _s.WindowScale - 1, v =>
        {
            _s.WindowScale = v + 1;
            _win.SetWindowScale(_s.WindowScale);
        });
        Toggle("Fullscreen (F11)", x, y + row * i++, w, modal, _s.Fullscreen, v =>
        {
            if (Raylib.IsWindowFullscreen() != v) _win.ToggleFullscreen();
            _s.Fullscreen = Raylib.IsWindowFullscreen();
        });
        Toggle("VSync", x, y + row * i++, w, modal, _s.VSync, v => { _s.VSync = v; ApplyGraphics(true); });
        int[] limits = { 30, 60, 120, 0 };
        int cur = Math.Max(0, Array.IndexOf(limits, _s.FpsLimit));
        Choice("Frame limit", x, y + row * i++, w, modal, new[] { "30", "60", "120", "None" }, cur, v =>
        {
            _s.FpsLimit = limits[v];
            ApplyGraphics(true);
        });
        Toggle("Show FPS", x, y + row * i++, w, modal, _s.ShowStats, v => _s.ShowStats = v);
        _ui.Text("16:9 shows more of the level to the left and right.", x, y + row * i + 14, 17, new SKColor(255, 255, 255, 170));
    }

    private void Sound(float x, float y, float w, bool modal)
    {
        float row = 70;
        Slider(0, "Master volume", x, y, w, modal, _s.MasterVolume, v => { _s.MasterVolume = v; ApplyAudio(); });
        Slider(1, "Music", x, y + row, w, modal, _s.MusicVolume, v => { _s.MusicVolume = v; ApplyAudio(); });
        Slider(2, "Sound effects", x, y + row * 2, w, modal, _s.SfxVolume, v => { _s.SfxVolume = v; ApplyAudio(); });
        Toggle("Mute everything", x, y + row * 3, w, modal, _s.Mute, v => { _s.Mute = v; ApplyAudio(); });
        if (_drag == -1 && Raylib.IsMouseButtonReleased(MouseButton.Left) && _lastSfxDrag) { _audio.PlaySfx("coin"); }
        _lastSfxDrag = _drag == 2 || (_lastSfxDrag && _down);
    }

    private bool _lastSfxDrag;

    private void Controls(float x, float y, float w, bool modal)
    {
        if (_capture != null) CaptureKey();
        float row = 40;
        _ui.Text("Click a key box, then press the new key. Backspace clears it.", x, y + 8, 17, new SKColor(255, 255, 255, 180));
        float ky = y + 28;
        for (int i = 0; i < BindRows.Length; i++)
        {
            var (action, label) = BindRows[i];
            float ry = ky + i * row;
            _ui.Text(label, x, ry + 30, 22, SKColors.White, false, 0, true);
            for (int slot = 0; slot < 2; slot++)
            {
                float bx = x + w - 2 * 190 - 12 + slot * 202;
                bool capt = _capture is { } c && c.action == action && c.slot == slot;
                string txt = capt ? "press a key..." : KeyName(action, slot);
                bool hot = !modal && _capture == null && Inside(bx, ry + 4, 190, 36);
                _ui.Rect(bx, ry + 4, 190, 36, capt ? new SKColor(255, 190, 60) : hot ? new SKColor(70, 76, 130) : new SKColor(40, 44, 80),
                    8, new SKColor(255, 255, 255, 90));
                _ui.Text(txt, bx + 95, ry + 29, 18, capt ? new SKColor(30, 20, 10) : SKColors.White, true, 1);
                if (hot && _click) { _capture = (action, slot); Tick(); }
            }
        }
        if (Button("Reset to defaults", x + w - 220, y + 28 + BindRows.Length * row + 16, 220, 40, modal || _capture != null))
        {
            _s.Keys = Settings.DefaultKeys();
            Tick();
        }
    }

    private string KeyName(string action, int slot)
    {
        if (!_s.Keys.TryGetValue(action, out var keys) || slot >= keys.Length || keys[slot] == 0) return "-";
        return Pretty(((KeyboardKey)keys[slot]).ToString());
    }

    private static string Pretty(string k) => k switch
    {
        "Left" => "Left arrow", "Right" => "Right arrow", "Up" => "Up arrow", "Down" => "Down arrow",
        "LeftShift" => "L Shift", "RightShift" => "R Shift", "LeftControl" => "L Ctrl", "RightControl" => "R Ctrl",
        _ => k,
    };

    private void CaptureKey()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && _captureFrames > 1) { _capture = null; _captureFrames = 0; return; }
        _captureFrames++;
        int key = Raylib.GetKeyPressed();
        if (key == 0) return;
        var (action, slot) = _capture!.Value;
        _capture = null;
        _captureFrames = 0;
        if ((KeyboardKey)key == KeyboardKey.Escape) return;

        var cur = _s.Keys.TryGetValue(action, out var k0) ? k0 : Array.Empty<int>();
        var arr = new[] { cur.Length > 0 ? cur[0] : 0, cur.Length > 1 ? cur[1] : 0 };
        if ((KeyboardKey)key == KeyboardKey.Backspace || (KeyboardKey)key == KeyboardKey.Delete) arr[slot] = 0;
        else
        {
            // a key can only do one thing: take it away from whatever used it before
            foreach (var kv in _s.Keys.ToList())
            {
                if (kv.Key == action) continue;
                if (kv.Value.Contains(key)) _s.Keys[kv.Key] = kv.Value.Select(v => v == key ? 0 : v).ToArray();
            }
            arr[slot] = key;
            if (arr[1 - slot] == key) arr[1 - slot] = 0;
        }
        _s.Keys[action] = arr;
        Tick();
    }

    private int _captureFrames;

    private void GameTab(float x, float y, float w, bool modal)
    {
        _ui.Text("Save data", x, y + 24, 24, new SKColor(255, 220, 120), true);
        _ui.Text("Your three save slots are kept next to the game in the 'saves' folder.", x, y + 56, 18, new SKColor(255, 255, 255, 190));
        if (Button("Erase all save slots", x, y + 80, 300, 46, modal)) { _confirm = "saves"; Tick(); }

        _ui.Text("Settings", x, y + 190, 24, new SKColor(255, 220, 120), true);
        _ui.Text("Put every option on this screen back to its default.", x, y + 222, 18, new SKColor(255, 255, 255, 190));
        if (Button("Reset all settings", x, y + 246, 300, 46, modal)) { _confirm = "settings"; Tick(); }
    }

    // ================================================================ confirm box

    private void DrawConfirm()
    {
        float cx = AppWindow.VW / 2f, cy = AppWindow.VH / 2f;
        _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, new SKColor(0, 0, 0, 150));
        _ui.Rect(cx - 220, cy - 100, 440, 200, new SKColor(20, 22, 50, 250), 16, new SKColor(255, 255, 255, 140), 3);
        _ui.Text("Are you sure?", cx, cy - 40, 34, SKColors.White, true, 1, true);
        _ui.Text(_confirm == "saves" ? "This deletes all three save slots." : "Keys, sound and graphics go back to default.",
            cx, cy - 8, 18, new SKColor(255, 255, 255, 210), false, 1);
        bool yes = Button("Yes", cx - 170, cy + 30, 150, 46, false);
        bool no = Button("No", cx + 20, cy + 30, 150, 46, false);
        if (Pad.KeyPressed(KeyboardKey.Escape) || Pad.KeyPressed(KeyboardKey.N)) no = true;
        if (yes)
        {
            if (_confirm == "saves") for (int i = 0; i < SaveManager.SlotCount; i++) _saves.Erase(i);
            else
            {
                var d = new Settings();
                _s.MasterVolume = d.MasterVolume; _s.Mute = d.Mute; _s.MusicVolume = d.MusicVolume; _s.SfxVolume = d.SfxVolume;
                _s.Widescreen = d.Widescreen; _s.WindowScale = d.WindowScale; _s.VSync = d.VSync; _s.FpsLimit = d.FpsLimit;
                _s.ShowStats = d.ShowStats; _s.Keys = Settings.DefaultKeys();
                ApplyAudio();
                ApplyGraphics();
            }
            _audio.PlaySfx("pop");
            _confirm = null;
        }
        else if (no) { _confirm = null; }
    }

    // ================================================================ widgets

    private bool Inside(float x, float y, float w, float h)
        => _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;

    private void Tick() => _audio.PlaySfx("click");

    private bool Button(string label, float x, float y, float w, float h, bool disabled)
    {
        bool hot = _ui.Button(label, x, y, w, h, disabled ? new Vector2(-1000, -1000) : _mouse);
        if (hot && _click && !disabled) { Tick(); return true; }
        return false;
    }

    private void Label(string text, float x, float y) => _ui.Text(text, x, y + 32, 22, SKColors.White, false, 0, true);

    private void Toggle(string label, float x, float y, float w, bool modal, bool value, Action<bool> set)
    {
        Label(label, x, y);
        float bw = 150, bx = x + w - bw;
        bool hot = !modal && Inside(bx, y + 6, bw, 38);
        _ui.Rect(bx, y + 6, bw, 38, value ? new SKColor(90, 190, 90) : hot ? new SKColor(70, 76, 130) : new SKColor(40, 44, 80),
            19, new SKColor(255, 255, 255, 90));
        _ui.Text(value ? "On" : "Off", bx + bw / 2, y + 32, 20, SKColors.White, true, 1);
        if (hot && _click) { Tick(); set(!value); }
    }

    private void Choice(string label, float x, float y, float w, bool modal, string[] options, int selected, Action<int> set)
    {
        Label(label, x, y);
        float bw = 112, gap = 6;
        float total = options.Length * bw + (options.Length - 1) * gap;
        float bx0 = x + w - total;
        for (int i = 0; i < options.Length; i++)
        {
            float bx = bx0 + i * (bw + gap);
            bool hot = !modal && Inside(bx, y + 6, bw, 38);
            _ui.Rect(bx, y + 6, bw, 38, i == selected ? new SKColor(255, 190, 60) : hot ? new SKColor(70, 76, 130) : new SKColor(40, 44, 80),
                10, new SKColor(255, 255, 255, 90));
            _ui.Text(options[i], bx + bw / 2, y + 32, 20, i == selected ? new SKColor(30, 20, 10) : SKColors.White, true, 1);
            if (hot && _click && i != selected) { Tick(); set(i); }
        }
    }

    private void Slider(int id, string label, float x, float y, float w, bool modal, float value, Action<float> set)
    {
        Label(label, x, y);
        float sx0 = x + w - 340, sx1 = x + w - 60, sy = y + 24;
        _ui.Rect(sx0, sy - 5, sx1 - sx0, 10, new SKColor(0, 0, 0, 150), 5, new SKColor(255, 255, 255, 160), 2);
        _ui.Rect(sx0, sy - 5, (sx1 - sx0) * value, 10, new SKColor(255, 190, 60), 5);
        _ui.Circle(sx0 + (sx1 - sx0) * value, sy, 13, SKColors.White);
        _ui.Text($"{(int)Math.Round(value * 100)}", x + w, sy + 8, 22, SKColors.White, true, 2, true);

        if (!modal)
        {
            if (_click && _mouse.X > sx0 - 16 && _mouse.X < sx1 + 16 && Math.Abs(_mouse.Y - sy) < 20) _drag = id;
            if (_drag == id) set(Math.Clamp((_mouse.X - sx0) / (sx1 - sx0), 0f, 1f));
            else if (Inside(sx0 - 16, sy - 20, sx1 - sx0 + 32, 40))
            {
                int dir = (Pad.KeyPressed(KeyboardKey.Right) ? 1 : 0) - (Pad.KeyPressed(KeyboardKey.Left) ? 1 : 0);
                if (dir != 0) set(Math.Clamp(value + dir * 0.05f, 0f, 1f));
            }
        }
    }
}
