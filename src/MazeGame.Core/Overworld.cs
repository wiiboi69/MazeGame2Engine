using System.Text.Json;
using System.Text.Json.Nodes;

namespace MazeGame.Core;

/// <summary>One stop on an overworld map: a level to play, or a portal to another map.</summary>
public sealed class MapNode
{
    public string Id = "";
    public string Name = "";
    /// <summary>Level id to play (empty for a pure portal / junction).</summary>
    public string Level = "";
    /// <summary>If set, entering this node opens another overworld (optionally at node <see cref="PortalNode"/>).</summary>
    public string Portal = "";
    public string PortalNode = "";
    public double X, Y;
    /// <summary>Node ids that must be completed before this one unlocks.</summary>
    public List<string> Requires = new();
    /// <summary>Node ids connected to this one (for cursor movement); links are made two-way on load.</summary>
    public List<string> Links = new();
}

/// <summary>A decorative image on the map: the same properties as a level image layer (see <see cref="ImageLayerProps"/>).</summary>
public sealed class MapLayer : ImageLayerProps { }

/// <summary>
/// A multi-layered overworld map (<c>worlds/&lt;id&gt;.json</c>): image layers, level nodes, and portals to other maps.
/// Coordinates are in map pixels with y pointing DOWN, origin at the top-left of the 16:9 view (1280x720).
/// </summary>
public sealed class OverworldData
{
    public string Id = "";
    public string Name = "";
    public string Song = "";
    public string Start = "";
    public List<MapLayer> Layers = new();
    public List<MapNode> Nodes = new();

    /// <summary>"path" = the cursor hops along the links between nodes; "free" = an avatar walks around the tile map.</summary>
    public string Mode = "path";
    public bool FreeRoam => Mode == "free";
    public const int CellSize = 32;

    /// <summary>Size of the tile grid in cells. Tile ids come from <see cref="OverworldTileRegistry"/>; "" = empty.</summary>
    public int Width = 40, Height = 23;
    /// <summary>Row-major (index = y * Width + x), y = 0 is the TOP row.</summary>
    public string[] Cells = Array.Empty<string>();

    public int PixelWidth => Width * CellSize;
    public int PixelHeight => Height * CellSize;
    public bool HasTiles => Cells.Any(c => c.Length > 0);

    public string CellAt(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height || Cells.Length != Width * Height ? "" : Cells[y * Width + x];

    public void SetCell(int x, int y, string id)
    {
        EnsureGrid();
        if (x >= 0 && y >= 0 && x < Width && y < Height) Cells[y * Width + x] = id;
    }

    public void EnsureGrid()
    {
        if (Cells.Length != Width * Height)
        {
            var n = new string[Width * Height];
            Array.Fill(n, "");
            Array.Copy(Cells, n, Math.Min(Cells.Length, n.Length));
            Cells = n;
        }
    }

    public void Resize(int w, int h)
    {
        w = Math.Clamp(w, 10, 300); h = Math.Clamp(h, 6, 200);
        EnsureGrid();
        var n = new string[w * h];
        Array.Fill(n, "");
        for (int y = 0; y < Math.Min(h, Height); y++)
            for (int x = 0; x < Math.Min(w, Width); x++) n[y * w + x] = Cells[y * Width + x];
        Width = w; Height = h; Cells = n;
    }

    /// <summary>Can the free-roam avatar stand at this pixel? Empty cells are walkable.</summary>
    public bool WalkableAtPixel(double px, double py)
    {
        if (px < 0 || py < 0 || px >= PixelWidth || py >= PixelHeight) return false;
        var id = CellAt((int)(px / CellSize), (int)(py / CellSize));
        return id.Length == 0 || (OverworldTileRegistry.Find(id)?.Walkable ?? true);
    }

    public double SpeedAtPixel(double px, double py)
        => OverworldTileRegistry.Find(CellAt((int)(px / CellSize), (int)(py / CellSize)))?.Speed ?? 1;

    public string ToJson()
    {
        var o = new JsonObject { ["id"] = Id, ["name"] = Name };
        if (Song.Length > 0) o["song"] = Song;
        o["start"] = Start;
        o["mode"] = Mode;
        o["width"] = Width;
        o["height"] = Height;
        if (HasTiles)
        {
            EnsureGrid();
            var rows = new JsonArray();
            for (int y = 0; y < Height; y++) rows.Add(RowToString(y));
            o["tiles"] = rows;
        }
        var layers = new JsonArray();
        foreach (var l in Layers)
        {
            var j = new JsonObject { ["image"] = l.Image, ["x"] = l.X, ["y"] = l.Y, ["scale"] = l.Scale, ["parallax"] = l.Parallax };
            string rep = (l.RepeatX ? "x" : "") + (l.RepeatY ? "y" : "");
            if (rep.Length > 0) j["repeat"] = rep;
            if (l.FitScreen) j["fit"] = true;
            layers.Add(j);
        }
        o["layers"] = layers;
        var nodes = new JsonArray();
        foreach (var n in Nodes)
        {
            var j = new JsonObject { ["id"] = n.Id, ["name"] = n.Name };
            if (n.Level.Length > 0) j["level"] = n.Level;
            if (n.Portal.Length > 0) j["portal"] = n.Portal;
            if (n.PortalNode.Length > 0) j["portalNode"] = n.PortalNode;
            j["x"] = n.X; j["y"] = n.Y;
            if (n.Requires.Count > 0) j["requires"] = new JsonArray(n.Requires.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
            if (n.Links.Count > 0) j["links"] = new JsonArray(n.Links.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
            nodes.Add(j);
        }
        o["nodes"] = nodes;
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private string RowToString(int y)
    {
        var parts = new List<string>();
        int x = 0;
        while (x < Width)
        {
            string id = Cells[y * Width + x];
            int run = 1;
            while (x + run < Width && Cells[y * Width + x + run] == id) run++;
            string token = id.Length == 0 ? "-" : id;
            parts.Add(run > 1 ? $"{token}*{run}" : token);
            x += run;
        }
        return string.Join(' ', parts);
    }

    private void ReadRow(int y, string row)
    {
        int x = 0;
        foreach (var tok in row.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string id = tok; int count = 1;
            int star = tok.LastIndexOf('*');
            if (star > 0 && int.TryParse(tok.AsSpan(star + 1), out int c)) { id = tok.Substring(0, star); count = c; }
            if (id == "-") id = "";
            for (int i = 0; i < count && x < Width; i++) Cells[y * Width + x++] = id;
        }
    }

    public MapNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);
    public MapNode? NodeForLevel(string level) => Nodes.FirstOrDefault(n => n.Level == level);

    public static OverworldData Parse(string json, string fallbackId)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        var w = new OverworldData
        {
            Id = (string?)root["id"] ?? fallbackId,
            Name = (string?)root["name"] ?? fallbackId,
            Song = (string?)root["song"] ?? "",
            Start = (string?)root["start"] ?? "",
        };
        w.Mode = (string?)root["mode"] == "free" ? "free" : "path";
        w.Width = Math.Clamp((int?)root["width"] ?? 40, 10, 300);
        w.Height = Math.Clamp((int?)root["height"] ?? 23, 6, 200);
        w.EnsureGrid();
        if (root["tiles"] is JsonArray trows)
            for (int y = 0; y < Math.Min(trows.Count, w.Height); y++) w.ReadRow(y, (string?)trows[y] ?? "");
        foreach (var l in root["layers"]?.AsArray() ?? new JsonArray())
            w.Layers.Add(new MapLayer
            {
                Image = (string?)l?["image"] ?? "",
                X = (double?)l?["x"] ?? 0, Y = (double?)l?["y"] ?? 0,
                Scale = (double?)l?["scale"] ?? 1, Parallax = (double?)l?["parallax"] ?? 1,
                RepeatX = ((string?)l?["repeat"] ?? "").Contains('x'),
                RepeatY = ((string?)l?["repeat"] ?? "").Contains('y'),
                FitScreen = (bool?)l?["fit"] ?? false,
            });
        foreach (var n in root["nodes"]?.AsArray() ?? new JsonArray())
        {
            if (n == null) continue;
            var m = new MapNode
            {
                Id = (string?)n["id"] ?? "",
                Name = (string?)n["name"] ?? "",
                Level = (string?)n["level"] ?? "",
                Portal = (string?)n["portal"] ?? "",
                PortalNode = (string?)n["portalNode"] ?? "",
                X = (double?)n["x"] ?? 0, Y = (double?)n["y"] ?? 0,
            };
            foreach (var r in n["requires"]?.AsArray() ?? new JsonArray()) m.Requires.Add((string?)r ?? "");
            foreach (var r in n["links"]?.AsArray() ?? new JsonArray()) m.Links.Add((string?)r ?? "");
            if (m.Id.Length > 0) w.Nodes.Add(m);
        }
        // links are two-way
        foreach (var a in w.Nodes.ToList())
            foreach (var id in a.Links.ToList())
            {
                var b = w.Node(id);
                if (b != null && !b.Links.Contains(a.Id)) b.Links.Add(a.Id);
            }
        if (w.Start.Length == 0 && w.Nodes.Count > 0) w.Start = w.Nodes[0].Id;
        return w;
    }
}

/// <summary>All overworlds found in a <c>worlds</c> folder.</summary>
public sealed class OverworldLibrary
{
    private readonly Dictionary<string, OverworldData> _worlds = new();

    public string Directory { get; private set; } = "";
    public IEnumerable<string> Ids => _worlds.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    private string PathFor(string id) => Path.Combine(Directory, id + ".json");
    public void Set(OverworldData w) => _worlds[w.Id] = w;

    public void Save(OverworldData w)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(w.Id), w.ToJson());
    }

    public void Remove(string id)
    {
        _worlds.Remove(id);
        try { if (File.Exists(PathFor(id))) File.Delete(PathFor(id)); } catch (Exception) { }
    }

    public string NextFreeId()
    {
        for (int i = 1; ; i++) if (!_worlds.ContainsKey("world_" + i)) return "world_" + i;
    }
    public OverworldData? Get(string? id) => id != null && _worlds.TryGetValue(id, out var w) ? w : null;
    public bool Any => _worlds.Count > 0;

    public static OverworldLibrary Load(string dir)
    {
        var lib = new OverworldLibrary { Directory = dir };
        if (!System.IO.Directory.Exists(dir)) return lib;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var w = OverworldData.Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f));
                lib._worlds[w.Id] = w;
            }
            catch (Exception ex) { Console.WriteLine($"[world] failed to load {f}: {ex.Message}"); }
        }
        return lib;
    }
}
