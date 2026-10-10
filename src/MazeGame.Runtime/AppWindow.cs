using System.Numerics;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>Window + 960x720 virtual screen that is letterboxed into whatever size the window has.</summary>
public sealed class AppWindow : IDisposable
{
    public static int VW { get; private set; } = 960;
    public static int VH { get; private set; } = 720;

    private RenderTexture2D _rt;
    private float _scale = 1, _offX, _offY;

    public AppWindow(string title, bool fullscreen, bool widescreen = false, int windowScale = 3)
    {
        VW = widescreen ? 1280 : 960;
        VH = 720;
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(VW * windowScale / 3, VH * windowScale / 3, title);
        Raylib.SetWindowMinSize(480, 360);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);
        _rt = Raylib.LoadRenderTexture(VW, VH);
        Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Bilinear);
        if (fullscreen) Raylib.ToggleFullscreen();
    }

    /// <summary>Switches between the 4:3 (960x720) and 16:9 (1280x720) virtual screen. Call <see cref="SkiaUi.Resize"/> too.</summary>
    public void SetAspect(bool widescreen)
    {
        int w = widescreen ? 1280 : 960;
        if (w == VW) return;
        VW = w;
        Raylib.UnloadRenderTexture(_rt);
        _rt = Raylib.LoadRenderTexture(VW, VH);
        Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Bilinear);
        MazeGame.Core.GameConstants.SetWidescreen(widescreen);
        if (!Raylib.IsWindowFullscreen()) Raylib.SetWindowSize(VW * 2 / 3, VH * 2 / 3);
    }

    /// <summary>Resizes the window to scale/3 of the virtual size (1 = 1/3, 2 = 2/3, 3 = full).</summary>
    public void SetWindowScale(int scale)
    {
        if (!Raylib.IsWindowFullscreen()) Raylib.SetWindowSize(VW * scale / 3, VH * scale / 3);
    }

    /// <summary>
    /// For tools: makes the virtual screen follow the window size (any aspect ratio). Returns true when the size
    /// changed so the caller can resize its <see cref="SkiaUi"/> (call before drawing each frame).
    /// </summary>
    public bool SyncToWindow()
    {
        int w = Math.Max(320, Raylib.GetScreenWidth()), h = Math.Max(240, Raylib.GetScreenHeight());
        if (w == VW && h == VH) return false;
        VW = w; VH = h;
        Raylib.UnloadRenderTexture(_rt);
        _rt = Raylib.LoadRenderTexture(VW, VH);
        Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Bilinear);
        return true;
    }

    public bool ShouldClose => Raylib.WindowShouldClose();

    public void ToggleFullscreen()
    {
        if (Raylib.IsWindowFullscreen())
        {
            Raylib.ToggleFullscreen();
            Raylib.SetWindowSize(VW, VH);
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
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)255));
        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color((byte)20, (byte)20, (byte)40, (byte)255));
    }

    /// <summary>Finish the virtual screen and blit it letterboxed to the window.</summary>
    public void EndVirtual()
    {
        Raylib.EndTextureMode();
        float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        _scale = Math.Min(sw / VW, sh / VH);
        _offX = (sw - VW * _scale) / 2;
        _offY = (sh - VH * _scale) / 2;
        Raylib.DrawTexturePro(_rt.Texture, new Rectangle(0, 0, VW, -VH),
            new Rectangle(_offX, _offY, VW * _scale, VH * _scale), Vector2.Zero, 0f,
            new Color((byte)255, (byte)255, (byte)255, (byte)255));
        Raylib.EndDrawing();
    }

    /// <summary>Mouse position in virtual (960x720) coordinates.</summary>
    public Vector2 Mouse
    {
        get
        {
            var m = Raylib.GetMousePosition();
            return new Vector2((m.X - _offX) / _scale, (m.Y - _offY) / _scale);
        }
    }

    public void Dispose()
    {
        Raylib.UnloadRenderTexture(_rt);
        Raylib.CloseWindow();
    }
}
