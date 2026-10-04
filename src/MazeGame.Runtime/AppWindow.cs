using System.Diagnostics;
using System.Numerics;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>
/// Window + virtual screen. The game draws in virtual units (height 720, width 960 in 4:3
/// or fluid 1280..2560 in 16:9). The render target is the window's real pixel size and a
/// camera maps virtual units to pixels, so everything is rasterised at native resolution
/// (no upscaling blur). Unused window area is letterboxed.
/// </summary>
public sealed class AppWindow : IDisposable
{
    public const int VH = 720;
    public const int W43 = 960;
    public const int W169 = 1280;          // minimum width in 16:9 mode
    public const int VW_MAX = 2560;        // maximum width in 16:9 mode

    /// <summary>Current virtual width. Changed with <see cref="SetAspect"/> and, in 16:9 mode, every frame.</summary>
    public static int VW { get; private set; } = W43;

    /// <summary>True when the virtual width follows the window shape (16:9 mode).</summary>
    public static bool Fluid { get; private set; }

    /// <summary>Pixels per virtual unit for the current frame.</summary>
    public static float Scale { get; private set; } = 1;

    /// <summary>Size in pixels of the virtual area on screen for the current frame.</summary>
    public static int PixelW { get; private set; } = W43;
    public static int PixelH { get; private set; } = VH;

    private RenderTexture2D _rt;
    private bool _rtReady;
    private float _offX, _offY;
    private int _fpsLimit = 60;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private double _nextFrame;

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    public AppWindow(string title, bool fullscreen)
    {
        // Windows' default timer is ~15.6 ms, which makes frame pacing coarse and forces spin-waiting.
        // A 1 ms period lets Thread.Sleep hit the frame deadline with almost no CPU use.
        if (OperatingSystem.IsWindows()) timeBeginPeriod(1);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);   // VSync off: our own limiter controls FPS
        Raylib.InitWindow(W43, VH, title);
        Raylib.SetWindowMinSize(480, 360);
        Raylib.SetTargetFPS(0);
        Raylib.SetExitKey(KeyboardKey.Null);
        if (fullscreen) Raylib.ToggleFullscreen();
    }

    /// <summary>Switch the virtual canvas between 4:3 and 16:9. Returns true if the width changed.</summary>
    public static bool SetAspect(string aspect)
    {
        bool fluid = aspect == "16:9";
        int before = VW;
        Fluid = fluid;
        if (!fluid) VW = W43;
        else if (VW < W169) VW = W169;
        return VW != before;
    }

    /// <summary>In 16:9 mode, derive the virtual width from the current window shape.</summary>
    private void UpdateVirtualWidth()
    {
        if (!Fluid) { VW = W43; return; }
        float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        if (sh <= 0) return;
        int w = (int)Math.Round(VH * sw / sh);
        VW = Math.Clamp(w, W169, VW_MAX);
    }

    /// <summary>Compute the virtual-to-pixel mapping for this frame and (re)create the render target if the window changed.</summary>
    private void UpdateLayout()
    {
        int sw = Math.Max(1, Raylib.GetScreenWidth()), sh = Math.Max(1, Raylib.GetScreenHeight());
        Scale = Math.Min(sw / (float)VW, sh / (float)VH);
        PixelW = Math.Max(1, (int)Math.Round(VW * Scale));
        PixelH = Math.Max(1, (int)Math.Round(VH * Scale));
        _offX = (sw - PixelW) / 2f;
        _offY = (sh - PixelH) / 2f;

        if (!_rtReady || _rt.Texture.Width != sw || _rt.Texture.Height != sh)
        {
            if (_rtReady) Raylib.UnloadRenderTexture(_rt);
            _rt = Raylib.LoadRenderTexture(sw, sh);
            Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Point);
            _rtReady = true;
        }
    }

    /// <summary>Frame cap in FPS. 0 = unlimited.</summary>
    public void SetFpsLimit(int fps)
    {
        _fpsLimit = fps;
        _nextFrame = _frameClock.Elapsed.TotalSeconds;
    }

    public bool ShouldClose => Raylib.WindowShouldClose();

    public void ToggleFullscreen()
    {
        if (Raylib.IsWindowFullscreen())
        {
            Raylib.ToggleFullscreen();
            Raylib.SetWindowSize(W43, VH);
        }
        else
        {
            int m = Raylib.GetCurrentMonitor();
            Raylib.SetWindowSize(Raylib.GetMonitorWidth(m), Raylib.GetMonitorHeight(m));
            Raylib.ToggleFullscreen();
        }
    }

    /// <summary>Start drawing into the virtual screen.</summary>
    public void BeginVirtual()
    {
        UpdateVirtualWidth();
        UpdateLayout();
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)255));
        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color((byte)20, (byte)20, (byte)40, (byte)255));
        // Virtual units -> window pixels: translate to the letterbox origin, then scale.
        Raylib.BeginMode2D(new Camera2D(new Vector2(_offX, _offY), Vector2.Zero, 0f, Scale));
    }

    /// <summary>Finish the virtual screen, blit it to the window, then honour the FPS cap.</summary>
    public void EndVirtual()
    {
        Raylib.EndMode2D();
        Raylib.EndTextureMode();
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        // Full-window blit at 1:1; flip Y for raylib (render textures are upside down).
        Raylib.DrawTextureRec(_rt.Texture, new Rectangle(0, 0, sw, -sh), Vector2.Zero,
            new Color((byte)255, (byte)255, (byte)255, (byte)255));
        // Black bars for the letterbox area (the scene may have drawn past the virtual edge).
        var black = new Color((byte)0, (byte)0, (byte)0, (byte)255);
        if (_offX >= 1) { Raylib.DrawRectangle(0, 0, (int)MathF.Ceiling(_offX), sh, black); Raylib.DrawRectangle((int)(_offX + PixelW), 0, sw, sh, black); }
        if (_offY >= 1) { Raylib.DrawRectangle(0, 0, sw, (int)MathF.Ceiling(_offY), black); Raylib.DrawRectangle(0, (int)(_offY + PixelH), sw, sh, black); }
        Raylib.EndDrawing();
        Throttle();
    }

    /// <summary>Sleep until the next frame is due. Sleeps for most of the wait and yields for the last bit, so no core is pinned.</summary>
    private void Throttle()
    {
        if (_fpsLimit <= 0) return;
        double frame = 1.0 / _fpsLimit;
        _nextFrame += frame;
        double now = _frameClock.Elapsed.TotalSeconds;
        // If we fell far behind (e.g. window drag), resync instead of bursting frames.
        if (_nextFrame < now - frame) { _nextFrame = now; return; }
        while (true)
        {
            double wait = _nextFrame - _frameClock.Elapsed.TotalSeconds;
            if (wait <= 0) break;
            if (wait > 0.0015) Thread.Sleep(TimeSpan.FromSeconds(wait - 0.001));
            else Thread.Yield();
        }
    }

    /// <summary>Mouse position in virtual coordinates (VW x 720).</summary>
    public Vector2 Mouse
    {
        get
        {
            var m = Raylib.GetMousePosition();
            return new Vector2((m.X - _offX) / Scale, (m.Y - _offY) / Scale);
        }
    }

    public void Dispose()
    {
        if (_rtReady) Raylib.UnloadRenderTexture(_rt);
        Raylib.CloseWindow();
    }
}
