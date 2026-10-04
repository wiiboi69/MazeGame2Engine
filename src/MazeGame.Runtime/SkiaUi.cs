using System.Numerics;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Runtime;

/// <summary>
/// A full-screen overlay drawn with SkiaSharp (text, panels, menus, HUD) in virtual units.
/// Draw calls are recorded into a display list. At the end of the frame the list is replayed
/// and uploaded to the GPU only if it differs from the previous frame, so static menus cost
/// almost nothing.
/// </summary>
public sealed unsafe class SkiaUi : IDisposable
{
    public const int Width = AppWindow.VW_MAX;
    public const int Height = AppWindow.VH;

    private SKSurface? _surface;
    private Texture2D _tex;
    private bool _texReady;
    private int _pw, _ph;

    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly Dictionary<string, SKFont> _fonts = new();

    private readonly List<Action<SKCanvas>> _ops = new();
    private HashCode _hc;
    private int _lastHash;
    private bool _haveLast;

    /// <summary>Direct canvas access. Drawing through this is NOT recorded and will be lost on cached frames; prefer the methods below.</summary>
    public SKCanvas Canvas => _surface!.Canvas;

    public SkiaUi()
    {
        _regular = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;
        _bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    /// <summary>Keep the overlay surface at the window's pixel size for the virtual area.</summary>
    private void EnsureSize()
    {
        int pw = Math.Max(1, AppWindow.PixelW), ph = Math.Max(1, AppWindow.PixelH);
        if (_surface != null && pw == _pw && ph == _ph) return;
        _surface?.Dispose();
        if (_texReady) Raylib.UnloadTexture(_tex);
        _pw = pw; _ph = ph;
        _surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul));
        var img = Raylib.GenImageColor(pw, ph, new Color((byte)0, (byte)0, (byte)0, (byte)0));
        _tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_tex, TextureFilter.Point);
        _texReady = true;
        _haveLast = false;   // surface contents are gone; force a redraw
    }

    public void BeginFrame()
    {
        EnsureSize();
        _ops.Clear();
        _hc = new HashCode();
        _hc.Add(AppWindow.VW);
        _hc.Add(AppWindow.Scale);
    }

    public static float W => AppWindow.VW;
    public static float CX => AppWindow.VW / 2f;

    /// <summary>Replays the display list if it changed, then draws the overlay over the current render target.</summary>
    public void EndFrame()
    {
        int h = _hc.ToHashCode();
        if (!_haveLast || h != _lastHash)
        {
            var c = _surface!.Canvas;
            c.ResetMatrix();
            c.Clear(SKColors.Transparent);
            c.Scale(AppWindow.Scale);   // ops are in virtual units, rasterised at pixel size
            foreach (var op in _ops) op(c);
            c.Flush();
            using var pix = _surface.PeekPixels();
            Raylib.UpdateTexture(_tex, (void*)pix.GetPixels());
            _lastHash = h;
            _haveLast = true;
        }

        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        Raylib.DrawTexturePro(_tex, new Rectangle(0, 0, _pw, _ph), new Rectangle(0, 0, AppWindow.VW, AppWindow.VH),
            Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, (byte)255));
        Raylib.EndBlendMode();
    }

    private void Record(Action<SKCanvas> op, int key)
    {
        _ops.Add(op);
        _hc.Add(key);
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
        float px = x, py = y;
        Record(c =>
        {
            if (shadow)
            {
                using var sp = new SKPaint { Color = new SKColor(0, 0, 0, 160), IsAntialias = true, Typeface = face, TextSize = size };
                c.DrawText(text, px + 2, py + 2, sp);
            }
            using var paint = new SKPaint { Color = color, IsAntialias = true, Typeface = face, TextSize = size };
            c.DrawText(text, px, py, paint);
        }, HashCode.Combine(text, px, py, size, color, bold, shadow));
    }

    public void Rect(float x, float y, float w, float h, SKColor fill, float radius = 0, SKColor? stroke = null, float strokeWidth = 2)
    {
        Record(c =>
        {
            using var p = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
            if (radius > 0) c.DrawRoundRect(x, y, w, h, radius, radius, p); else c.DrawRect(x, y, w, h, p);
            if (stroke.HasValue)
            {
                using var sp = new SKPaint { Color = stroke.Value, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };
                if (radius > 0) c.DrawRoundRect(x, y, w, h, radius, radius, sp); else c.DrawRect(x, y, w, h, sp);
            }
        }, HashCode.Combine(x, y, w, h, fill, radius, stroke, strokeWidth));
    }

    public void Line(float x1, float y1, float x2, float y2, SKColor color, float width = 1)
    {
        Record(c =>
        {
            using var p = new SKPaint { Color = color, IsAntialias = true, StrokeWidth = width, Style = SKPaintStyle.Stroke };
            c.DrawLine(x1, y1, x2, y2, p);
        }, HashCode.Combine(x1, y1, x2, y2, color, width));
    }

    public void Circle(float cx, float cy, float r, SKColor color)
    {
        Record(c =>
        {
            using var p = new SKPaint { Color = color, IsAntialias = true };
            c.DrawCircle(cx, cy, r, p);
        }, HashCode.Combine(cx, cy, r, color));
    }

    /// <summary>Clip subsequent drawing to a rectangle until <see cref="PopClip"/>.</summary>
    public void PushClip(float left, float top, float right, float bottom)
    {
        var rect = new SKRect(left, top, right, bottom);
        Record(c => { c.Save(); c.ClipRect(rect); }, HashCode.Combine(1, left, top, right, bottom));
    }

    public void PopClip() => Record(c => c.Restore(), 2);

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
        if (_texReady) Raylib.UnloadTexture(_tex);
        _surface?.Dispose();
        foreach (var f in _fonts.Values) f.Dispose();
    }
}
