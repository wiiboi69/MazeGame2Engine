using System.Numerics;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Runtime;

/// <summary>
/// A full-screen overlay drawn with SkiaSharp (text, panels, menus, HUD) at the 960x720 virtual resolution
/// and uploaded to a raylib texture once per frame.
/// </summary>
public sealed unsafe class SkiaUi : IDisposable
{
    public const int Width = 960;
    public const int Height = 720;

    private readonly SKSurface _surface;
    private readonly Texture2D _tex;
    private readonly Dictionary<string, SKFont> _fonts = new();
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;

    public SKCanvas Canvas => _surface.Canvas;

    public SkiaUi()
    {
        _surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var img = Raylib.GenImageColor(Width, Height, new Color((byte)0, (byte)0, (byte)0, (byte)0));
        _tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_tex, TextureFilter.Bilinear);
        _regular = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;
        _bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    public void BeginFrame() => Canvas.Clear(SKColors.Transparent);

    /// <summary>Uploads the canvas and draws it over the current render target.</summary>
    public void EndFrame()
    {
        Canvas.Flush();
        using var pix = _surface.PeekPixels();
        Raylib.UpdateTexture(_tex, (void*)pix.GetPixels());
        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        Raylib.DrawTexture(_tex, 0, 0, new Color((byte)255, (byte)255, (byte)255, (byte)255));
        Raylib.EndBlendMode();
    }

    private SKFont Font(float size, bool bold)
    {
        string key = (bold ? "b" : "r") + size.ToString("0.#");
        if (!_fonts.TryGetValue(key, out var f))
        {
            f = new SKFont(bold ? _bold : _regular, size) { Edging = SKFontEdging.SubpixelAntialias };
            _fonts[key] = f;
        }
        return f;
    }

    public float Measure(string text, float size, bool bold = false) => Font(size, bold).MeasureText(text);

    /// <summary>align: 0 = left, 1 = centre, 2 = right. y is the text baseline.</summary>
    public void Text(string text, float x, float y, float size, SKColor color, bool bold = false, int align = 0, bool shadow = false)
    {
        var font = Font(size, bold);
        float w = font.MeasureText(text);
        if (align == 1) x -= w / 2; else if (align == 2) x -= w;
        var face = bold ? _bold : _regular;
        if (shadow)
        {
            using var sp = new SKPaint { Color = new SKColor(0, 0, 0, 160), IsAntialias = true, Typeface = face, TextSize = size };
            Canvas.DrawText(text, x + 2, y + 2, sp);
        }
        using var paint = new SKPaint { Color = color, IsAntialias = true, Typeface = face, TextSize = size };
        Canvas.DrawText(text, x, y, paint);
    }

    public void Rect(float x, float y, float w, float h, SKColor fill, float radius = 0, SKColor? stroke = null, float strokeWidth = 2)
    {
        using var p = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        if (radius > 0) Canvas.DrawRoundRect(x, y, w, h, radius, radius, p); else Canvas.DrawRect(x, y, w, h, p);
        if (stroke.HasValue)
        {
            using var sp = new SKPaint { Color = stroke.Value, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };
            if (radius > 0) Canvas.DrawRoundRect(x, y, w, h, radius, radius, sp); else Canvas.DrawRect(x, y, w, h, sp);
        }
    }

    public void Line(float x1, float y1, float x2, float y2, SKColor color, float width = 1)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true, StrokeWidth = width, Style = SKPaintStyle.Stroke };
        Canvas.DrawLine(x1, y1, x2, y2, p);
    }

    public void Circle(float cx, float cy, float r, SKColor color)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true };
        Canvas.DrawCircle(cx, cy, r, p);
    }

    /// <summary>A clickable-looking button. Returns true if the mouse is over it.</summary>
    public bool Button(string label, float x, float y, float w, float h, Vector2 mouse, bool selected = false, float fontSize = 26)
    {
        bool hot = mouse.X >= x && mouse.X < x + w && mouse.Y >= y && mouse.Y < y + h;
        var fill = hot || selected ? new SKColor(255, 190, 60) : new SKColor(40, 44, 80, 230);
        var text = hot || selected ? new SKColor(30, 20, 10) : SKColors.White;
        Rect(x, y, w, h, fill, 10, new SKColor(255, 255, 255, 90));
        Text(label, x + w / 2, y + h / 2 + fontSize * 0.35f, fontSize, text, true, 1);
        return hot;
    }

    public void Dispose()
    {
        Raylib.UnloadTexture(_tex);
        _surface.Dispose();
        foreach (var f in _fonts.Values) f.Dispose();
    }
}
