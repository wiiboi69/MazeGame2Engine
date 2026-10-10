using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MazeGame.Core;

/// <summary>
/// The level file format (JSON). Tiles and entities are identified by string ids.
/// <code>
/// {
///   "format": 3, "id": "cave_1", "name": "The Cave", "width": 70, "height": 40,
///   "next": "cave_2", "left": null, "right": null, "script": "scripts/cave_1.mgs",
///   "background": "#f0f000", "backdrop": 1, "parallax": true, "underwater": false,
///   "camera": 0, "song": "0", "difficulty": 0,
///   "tiles": [                      // top row first, one string per row: "id" or "id*count", space separated
///     "block_wood*70",
///     "block_wood air*68 block_wood",
///     ...
///   ],
///   "layers": [   // optional extra layers, drawn behind / in front of the main tile layer
///     { "name": "sky", "kind": "image", "depth": "back", "image": "custom/sky.png", "parallax": [0.2, 0.1], "repeat": "x" },
///     { "name": "plants", "kind": "tiles", "depth": "front", "tiles": [ ...rows like the main layer... ] }
///   ],
///   "entities": [ { "type": "npc", "x": 12, "y": 3, "props": { "text": "hello" } } ]   // x,y: cell, y=0 is the bottom row
/// }
/// </code>
/// </summary>
public static class LevelJson
{
    public const int FormatVersion = 3;

    // ================================================================ write

    public static string Write(LevelData l)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", FormatVersion);
            w.WriteString("id", l.Id);
            w.WriteString("name", l.Name);
            if (l.Next != null) w.WriteString("next", l.Next);
            if (l.Left != null) w.WriteString("left", l.Left);
            if (l.Right != null) w.WriteString("right", l.Right);
            if (l.Script.Length > 0) w.WriteString("script", l.Script);
            if (l.PlayerScript.Length > 0) w.WriteString("playerScript", l.PlayerScript);
            w.WriteNumber("width", l.Width);
            w.WriteNumber("height", l.Height);
            w.WriteString("background", l.BackgroundColor);
            w.WriteNumber("backdrop", l.Backdrop);
            w.WriteBoolean("parallax", l.ShowBackground);
            w.WriteBoolean("underwater", l.Underwater);
            int extraBits = l.Flags & ~(GameConstants.FlagShowBackground | GameConstants.FlagUnderwater);
            if (extraBits != 0) w.WriteNumber("flagBits", extraBits);
            w.WriteNumber("camera", l.CameraMode);
            w.WriteString("song", l.Song);
            w.WriteNumber("difficulty", l.Difficulty);
            w.WriteNumber("custom", l.Custom);
            w.WriteNumber("extraFlags", l.ExtraFlags);

            w.WriteStartArray("tiles");
            WriteRows(w, l.Tiles, l.Width, l.Height);
            w.WriteEndArray();

            if (l.Layers.Count > 0)
            {
                w.WriteStartArray("layers");
                foreach (var layer in l.Layers) WriteLayer(w, l, layer);
                w.WriteEndArray();
            }

            w.WriteStartArray("entities");
            foreach (var e in l.Entities)
            {
                w.WriteStartObject();
                w.WriteString("type", e.Type);
                w.WriteNumber("x", e.X);
                w.WriteNumber("y", e.Y);
                if (e.Props.Count > 0)
                {
                    w.WriteStartObject("props");
                    foreach (var (k, v) in e.Props) WriteScalar(w, k, v);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    private static void WriteRows(Utf8JsonWriter w, int[] grid, int width, int height)
    {
        var sb = new StringBuilder();
        for (int y = height - 1; y >= 0; y--)
        {
            sb.Clear();
            int run = 0, cur = -1;
            void Flush()
            {
                if (run == 0) return;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(TileRegistry.IdOf(cur));
                if (run > 1) sb.Append('*').Append(run.ToString(CultureInfo.InvariantCulture));
            }
            for (int x = 0; x < width; x++)
            {
                int t = grid[x * height + y];
                if (run > 0 && t == cur) { run++; continue; }
                Flush();
                cur = t;
                run = 1;
            }
            Flush();
            w.WriteStringValue(sb.ToString());
        }
    }

    private static void WriteLayer(Utf8JsonWriter w, LevelData l, Layer layer)
    {
        w.WriteStartObject();
        w.WriteString("name", layer.Name);
        w.WriteString("kind", layer.Kind == LayerKind.Image ? "image" : "tiles");
        w.WriteString("depth", layer.Front ? "front" : "back");
        if (!layer.Visible) w.WriteBoolean("visible", false);
        if (layer.Parallax != 1) w.WriteNumber("parallax", layer.Parallax);
        if (layer.Kind == LayerKind.Image)
        {
            w.WriteString("image", layer.Image);
            w.WriteNumber("x", layer.X);
            w.WriteNumber("y", layer.Y);
            if (layer.Scale != 1) w.WriteNumber("scale", layer.Scale);
            string rep = (layer.RepeatX ? "x" : "") + (layer.RepeatY ? "y" : "");
            if (rep.Length > 0) w.WriteString("repeat", rep);
            if (layer.FitScreen) w.WriteBoolean("fit", true);
        }
        else
        {
            w.WriteStartArray("tiles");
            WriteRows(w, layer.Tiles, l.Width, l.Height);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static int[] ReadRows(JsonElement rows, int width, int height)
    {
        var grid = new int[width * height];
        Array.Fill(grid, TileInfo.Air);
        if (rows.ValueKind != JsonValueKind.Array) return grid;
        int r = 0;
        var cache = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            int y = height - 1 - r++;
            if (y < 0) break;
            int x = 0;
            foreach (string token in (row.ValueKind == JsonValueKind.String ? row.GetString() ?? "" : "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string id = token;
                int count = 1;
                int star = token.LastIndexOf('*');
                if (star > 0 && int.TryParse(token.AsSpan(star + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                {
                    id = token.Substring(0, star);
                    count = Math.Max(1, c);
                }
                if (!cache.TryGetValue(id, out int num)) cache[id] = num = TileRegistry.NumOf(id);
                for (int k = 0; k < count && x < width; k++) grid[x++ * height + y] = num;
            }
        }
        return grid;
    }

    private static Layer ReadLayer(JsonElement e, LevelData l)
    {
        var layer = new Layer
        {
            Name = Str(e, "name", "layer"),
            Kind = Str(e, "kind", "tiles") == "image" ? LayerKind.Image : LayerKind.Tiles,
            Front = Str(e, "depth", "back") == "front",
            Visible = Bool(e, "visible", true),
        };
        if (e.TryGetProperty("parallax", out var par))
        {
            if (par.ValueKind == JsonValueKind.Number) layer.Parallax = par.GetDouble();
            else if (par.ValueKind == JsonValueKind.Array && par.GetArrayLength() > 0 && par[0].ValueKind == JsonValueKind.Number)
                layer.Parallax = par[0].GetDouble();     // older files stored [x, y]
        }
        if (layer.Kind == LayerKind.Image)
        {
            layer.Image = Str(e, "image", "");
            if (e.TryGetProperty("x", out var lx) && lx.ValueKind == JsonValueKind.Number) layer.X = lx.GetDouble();
            if (e.TryGetProperty("y", out var ly) && ly.ValueKind == JsonValueKind.Number) layer.Y = ly.GetDouble();
            if (e.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number) layer.Scale = sc.GetDouble();
            string rep = Str(e, "repeat", "");
            layer.RepeatX = rep.Contains('x');
            layer.RepeatY = rep.Contains('y');
            layer.FitScreen = Bool(e, "fit", false);
        }
        else
        {
            layer.Tiles = e.TryGetProperty("tiles", out var rows) ? ReadRows(rows, l.Width, l.Height) : ReadRows(default, l.Width, l.Height);
        }
        return layer;
    }

    private static void WriteScalar(Utf8JsonWriter w, string key, string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n.ToString(CultureInfo.InvariantCulture) == value)
            w.WriteNumber(key, n);
        else if (value == "true" || value == "false") w.WriteBoolean(key, value == "true");
        else w.WriteString(key, value);
    }

    // ================================================================ read

    /// <summary>Parses a level file. Throws <see cref="InvalidDataException"/> if it is not a valid level.</summary>
    public static LevelData Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { throw new InvalidDataException("not valid JSON: " + ex.Message); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("level must be a JSON object");

            var l = new LevelData
            {
                Width = Int(root, "width", 0),
                Height = Int(root, "height", 0),
                Id = Str(root, "id", ""),
                Name = Str(root, "name", "level"),
                Next = NullableStr(root, "next"),
                Left = NullableStr(root, "left"),
                Right = NullableStr(root, "right"),
                Script = Str(root, "script", ""),
                PlayerScript = Str(root, "playerScript", ""),
                BackgroundColor = Str(root, "background", "#f0f000"),
                Backdrop = Int(root, "backdrop", 1),
                CameraMode = Int(root, "camera", 0),
                Song = Str(root, "song", "0"),
                Difficulty = Int(root, "difficulty", 0),
                Custom = Int(root, "custom", 69),
                ExtraFlags = Int(root, "extraFlags", 420),
            };
            if (l.Width <= 0 || l.Height <= 0 || (long)l.Width * l.Height > 4_000_000)
                throw new InvalidDataException("bad width/height");

            int flags = Int(root, "flagBits", 0);
            if (Bool(root, "parallax", true)) flags |= GameConstants.FlagShowBackground;
            if (Bool(root, "underwater", false)) flags |= GameConstants.FlagUnderwater;
            l.Flags = flags;

            // tiles
            l.Tiles = root.TryGetProperty("tiles", out var rows) ? ReadRows(rows, l.Width, l.Height) : ReadRows(default, l.Width, l.Height);

            // extra layers
            if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
                foreach (var le in layers.EnumerateArray())
                    if (le.ValueKind == JsonValueKind.Object) l.Layers.Add(ReadLayer(le, l));

            // entities
            if (root.TryGetProperty("entities", out var ents) && ents.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in ents.EnumerateArray())
                {
                    string type = Str(e, "type", "");
                    if (type.Length == 0) continue;
                    var def = new EntityDef { Type = type, X = Int(e, "x", 0), Y = Int(e, "y", 0) };
                    if (e.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
                        foreach (var p in props.EnumerateObject())
                            def.Props[p.Name] = p.Value.ValueKind switch
                            {
                                JsonValueKind.String => p.Value.GetString() ?? "",
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                _ => p.Value.GetRawText(),
                            };
                    l.Entities.Add(def);
                }
            }
            return l;
        }
    }

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback;

    private static string Str(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.GetRawText()) : fallback;

    private static string? NullableStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()) ? v.GetString() : null;

    /// <summary>Reads just the "id" of a level file without building the level (null if missing).</summary>
    public static string? PeekId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return NullableStr(doc.RootElement, "id");
        }
        catch (JsonException) { return null; }
    }

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
}
