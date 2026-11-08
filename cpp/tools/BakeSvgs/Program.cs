// Rasterises every assets/**/*.svg:
//   default      -> "<file>.svg.png" at 4 texels per source unit (exactly what SpriteLibrary.RasterSvg does in the C# game)
//   --psp        -> "<file>.svg.psp.png" at 1.5 px per unit (the on-screen size) and <= 512 px on the longest side (PSP texture limit),
//                   plus assets/psp_sizes.json with each image's true size in source units, so the runtime can still
//                   draw it at the right scale.
using System.Text.Json;
using SkiaSharp;
using Svg.Skia;

bool vita = args.Contains("--vita");
bool psp = args.Contains("--psp") || vita;   // "handheld" profile
string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "assets";
const float TexelsPerUnit = 4f;
int PspMaxSide = vita ? 1024 : 512;
double PspPxPerUnit = vita ? 3.0 : 1.5;   // Vita: 2 x 2 x (960/1280) = 3 px per unit;     // most sprites are drawn at size 200% x world scale 2 x (480/1280) = 1.5 px per source unit on the PSP
int ok = 0, fail = 0;
var sizes = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
foreach (var path in Directory.EnumerateFiles(root, "*.svg", SearchOption.AllDirectories))
{
    try
    {
        using var svg = new SKSvg();
        svg.Load(path);
        var pic = svg.Picture ?? throw new InvalidDataException("empty picture");
        var r = pic.CullRect;
        int w, h;
        if (psp)
        {
            double k = Math.Min(PspPxPerUnit, PspMaxSide / Math.Max(r.Width, r.Height));
            w = Math.Max(1, (int)Math.Round(r.Width * k));
            h = Math.Max(1, (int)Math.Round(r.Height * k));
            sizes[Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')] = new double[] { r.Width, r.Height };
        }
        else
        {
            w = Math.Min(8192, Math.Max(1, (int)Math.Ceiling(r.Width * TexelsPerUnit)));
            h = Math.Min(8192, Math.Max(1, (int)Math.Ceiling(r.Height * TexelsPerUnit)));
        }
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(w / r.Width, h / r.Height);
            canvas.Translate(-r.Left, -r.Top);
            canvas.DrawPicture(pic);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path + (vita ? ".vita.png" : psp ? ".psp.png" : ".png"), data.ToArray());
        ok++;
    }
    catch (Exception ex) { Console.Error.WriteLine($"FAILED {path}: {ex.Message}"); fail++; }
}
if (psp) File.WriteAllText(Path.Combine(root, vita ? "vita_sizes.json" : "psp_sizes.json"), JsonSerializer.Serialize(sizes, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"baked {ok} svgs{(psp ? " (handheld set)" : "")}, {fail} failed");
