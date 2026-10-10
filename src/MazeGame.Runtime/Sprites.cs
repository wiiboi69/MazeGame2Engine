using MazeGame.Core;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using SkiaSharp;
using Svg.Skia;

namespace MazeGame.Runtime;

/// <summary>A GPU texture plus the Scratch-style rotation centre. Sizes are in "source units" (stage px at 100%).</summary>
public sealed class Sprite
{
    public Texture2D Tex;
    public float SrcW, SrcH;   // size in source units
    public float Cx, Cy;       // rotation centre in source units from the top-left
    /// <summary>Visible-pixel box relative to the rotation centre: centre offset (right, down) and half size.</summary>
    public float VisDx, VisDy, VisHw, VisHh;
}

public sealed class CostumeInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Res { get; set; } = 1;
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double[]? Bbox { get; set; }
}

/// <summary>
/// Loads costumes listed in assets/manifest.json. SVG costumes are rasterised with Svg.Skia/SkiaSharp
/// (4 texels per source unit, i.e. crisp at the 2x render scale), PNG costumes are decoded with SkiaSharp.
/// </summary>
public sealed class SpriteLibrary : IDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, Dictionary<string, CostumeInfo>> _manifest;
    private readonly Dictionary<string, Dictionary<int, string>> _byIndex = new();
    private readonly Dictionary<string, Sprite?> _cache = new();

    public const float SvgTexelsPerUnit = 4f;

    public SpriteLibrary(string assetsRoot)
    {
        _root = assetsRoot;
        string json = File.ReadAllText(Path.Combine(assetsRoot, "manifest.json"));
        _manifest = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, CostumeInfo>>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        foreach (var (group, items) in _manifest)
        {
            var map = new Dictionary<int, string>();
            foreach (var (key, info) in items) map[info.Index] = key;
            _byIndex[group] = map;
        }
    }

    public IReadOnlyDictionary<string, CostumeInfo>? Group(string group) =>
        _manifest.TryGetValue(group, out var g) ? g : null;

    public Sprite? Get(string group, string key)
    {
        string ck = group + "/" + key;
        if (_cache.TryGetValue(ck, out var s)) return s;
        Sprite? loaded = null;
        if (_manifest.TryGetValue(group, out var g) && g.TryGetValue(key, out var info))
        {
            try { loaded = Load(info); }
            catch (Exception ex) { Console.Error.WriteLine($"[sprites] failed to load {ck}: {ex.Message}"); }
        }
        _cache[ck] = loaded;
        return loaded;
    }

    /// <summary>Lookup by the original 1-based costume number.</summary>
    public Sprite? ByIndex(string group, int index) =>
        _byIndex.TryGetValue(group, out var map) && map.TryGetValue(index, out var key) ? Get(group, key) : null;

    /// <summary>Sprite for a runtime tile number: the original costume, or the tile's custom texture / animation frame.</summary>
    public Sprite? Tile(int id, double time = 0)
    {
        var def = TileRegistry.ByNum(id);
        if (def != null)
        {
            if (def.Frames is { Length: > 0 } frames)
            {
                int i = (int)Math.Floor(time * Math.Max(0.01, def.Fps)) % frames.Length;
                string f = frames[i];
                if (f.StartsWith("tile:", StringComparison.Ordinal))
                {
                    var other = TileRegistry.Find(f.Substring(5));
                    return other == null || other.Num == id ? null : Tile(other.Num, 0);
                }
                return GetFile(f);
            }
            if (def.Texture != null) return GetFile(def.Texture);
        }
        return Get("tiles", id.ToString());
    }

    /// <summary>Loads a custom .png / .svg from the assets folder (cached). The rotation centre is the image centre.</summary>
    public Sprite? GetFile(string relativePath)
    {
        string ck = "file/" + relativePath;
        if (_cache.TryGetValue(ck, out var cached)) return cached;
        Sprite? sprite = null;
        try
        {
            string path = Path.Combine(_root, relativePath);
            bool svg = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            using SKBitmap? bmp = svg ? RasterSvg(path, out _, out _) : DecodePng(path);
            if (bmp == null) throw new InvalidDataException("could not decode image");
            float texels = svg ? SvgTexelsPerUnit : 1f;
            sprite = new Sprite { SrcW = bmp.Width / texels, SrcH = bmp.Height / texels, Tex = Upload(bmp) };
            sprite.Cx = sprite.SrcW / 2; sprite.Cy = sprite.SrcH / 2;
            sprite.VisHw = sprite.SrcW / 2; sprite.VisHh = sprite.SrcH / 2;
            Raylib.SetTextureFilter(sprite.Tex, !svg && bmp.Width <= 128 ? TextureFilter.Point : TextureFilter.Bilinear);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sprites] failed to load custom image '{relativePath}': {ex.Message}");
        }
        _cache[ck] = sprite;
        return sprite;
    }

    private Sprite Load(CostumeInfo info)
    {
        string path = Path.Combine(_root, info.File);
        using SKBitmap? bmp = info.Kind == "svg" ? RasterSvg(path, out _, out _) : DecodePng(path);
        if (bmp == null) throw new InvalidDataException("could not decode image");

        float texels = info.Kind == "svg" ? SvgTexelsPerUnit : Math.Max(1, info.Res);
        var sprite = new Sprite
        {
            SrcW = bmp.Width / texels,
            SrcH = bmp.Height / texels,
            Cx = info.Kind == "svg" ? (float)info.Cx : (float)(info.Cx / Math.Max(1, info.Res)),
            Cy = info.Kind == "svg" ? (float)info.Cy : (float)(info.Cy / Math.Max(1, info.Res)),
            Tex = Upload(bmp),
        };
        if (info.Bbox is { Length: 4 } bb)
        {
            sprite.VisDx = (float)((bb[0] + bb[2]) / 2 - sprite.Cx);
            sprite.VisDy = (float)((bb[1] + bb[3]) / 2 - sprite.Cy);
            sprite.VisHw = (float)((bb[2] - bb[0]) / 2);
            sprite.VisHh = (float)((bb[3] - bb[1]) / 2);
        }
        else
        {
            sprite.VisDx = sprite.SrcW / 2 - sprite.Cx;
            sprite.VisDy = sprite.SrcH / 2 - sprite.Cy;
            sprite.VisHw = sprite.SrcW / 2;
            sprite.VisHh = sprite.SrcH / 2;
        }
        bool pixelArt = info.Kind == "png" && info.Res <= 2 && bmp.Width <= 128;
        Raylib.SetTextureFilter(sprite.Tex, pixelArt ? TextureFilter.Point : TextureFilter.Bilinear);
        return sprite;
    }

    private static SKBitmap? DecodePng(string path)
    {
        using var decoded = SKBitmap.Decode(path);
        if (decoded == null) return null;
        var bmp = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(decoded, 0, 0);
        return bmp;
    }

    private static SKBitmap? RasterSvg(string path, out float left, out float top)
    {
        left = top = 0;
        using var svg = new SKSvg();
        svg.Load(path);
        var pic = svg.Picture;
        if (pic == null) return null;
        var r = pic.CullRect;
        left = r.Left; top = r.Top;
        int w = Math.Max(1, (int)Math.Ceiling(r.Width * SvgTexelsPerUnit));
        int h = Math.Max(1, (int)Math.Ceiling(r.Height * SvgTexelsPerUnit));
        w = Math.Min(w, 8192); h = Math.Min(h, 8192);
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(w / r.Width, h / r.Height);
        canvas.Translate(-r.Left, -r.Top);
        canvas.DrawPicture(pic);
        return bmp;
    }

    /// <summary>Uploads a premultiplied RGBA bitmap as a straight-alpha raylib texture.</summary>
    public static unsafe Texture2D Upload(SKBitmap premul)
    {
        var info = new SKImageInfo(premul.Width, premul.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var data = new byte[info.BytesSize];
        fixed (byte* p = data)
        {
            using var img = SKImage.FromBitmap(premul);
            if (!img.ReadPixels(info, (IntPtr)p, info.RowBytes, 0, 0))
                throw new InvalidOperationException("ReadPixels failed");
            var image = new Image
            {
                Data = p,
                Width = info.Width,
                Height = info.Height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            return Raylib.LoadTextureFromImage(image);
        }
    }

    public void Dispose()
    {
        foreach (var s in _cache.Values)
            if (s != null) Raylib.UnloadTexture(s.Tex);
        _cache.Clear();
    }
}
