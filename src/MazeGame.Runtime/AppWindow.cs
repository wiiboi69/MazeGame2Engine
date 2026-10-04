using System.Numerics;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>Window + 960x720 virtual screen that is letterboxed into whatever size the window has.</summary>
public sealed class AppWindow : IDisposable
{
    public const int VW = SkiaUi.Width;
    public const int VH = SkiaUi.Height;

    private RenderTexture2D _rt;
    private float _scale = 1, _offX, _offY;

    public AppWindow(string title, bool fullscreen)
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(VW, VH, title);
        Raylib.SetWindowMinSize(480, 360);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);
        _rt = Raylib.LoadRenderTexture(VW, VH);
        Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Bilinear);
        if (fullscreen) Raylib.ToggleFullscreen();
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
