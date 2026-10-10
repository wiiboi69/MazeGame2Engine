using System.Text;
using System.Text.Json;

namespace MazeGame.Core;

/// <summary>
/// The level store: every level is a JSON file <c>levels/&lt;id&gt;.json</c> (see <see cref="LevelJson"/>). A level's id is the
/// "id" field in the file, or the file name when the field is missing. <c>game.json</c> holds the start level / overworld.
/// Old numbered <c>level_NNN.mgl</c> Scratch strings are still imported when no .json with that id exists.
/// </summary>
public sealed class LevelLibrary
{
    private readonly SortedDictionary<string, string> _raw = new(NaturalComparer.Instance);   // id -> JSON text
    private readonly Dictionary<string, LevelData?> _cache = new(StringComparer.Ordinal);

    public string? Directory { get; private set; }

    /// <summary>The level a new game starts in when there is no overworld.</summary>
    public string? StartLevelId { get; set; }

    /// <summary>The overworld (map) a new game starts in, if any.</summary>
    public string? StartWorldId { get; set; }

    /// <summary>Script every player runs unless the level sets its own PlayerScript.</summary>
    public string? PlayerScriptId { get; set; }

    public static LevelLibrary LoadDirectory(string dir)
    {
        var lib = new LevelLibrary { Directory = dir };
        if (!System.IO.Directory.Exists(dir)) return lib;

        foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Equals("game", StringComparison.OrdinalIgnoreCase)) continue;
            string json = File.ReadAllText(file);
            string id = LevelJson.PeekId(json) ?? name;
            lib._raw[id] = json;
        }

        // legacy Scratch strings (level_NNN.mgl -> id "level_NNN")
        foreach (var file in System.IO.Directory.GetFiles(dir, "level_*.mgl"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(name.AsSpan("level_".Length), out int n)) continue;
            string text = File.ReadAllText(file).Trim();
            if (n == 1)
            {
                int? start = LegacyLevelCodec.DecodeSettings(text);
                if (start.HasValue) lib.StartLevelId ??= $"level_{start.Value:D3}";
                continue;
            }
            if (lib._raw.ContainsKey(name)) continue;
            var level = LegacyLevelCodec.Decode(text);
            if (level == null) continue;
            level.Id = name;
            lib._raw[name] = LevelJson.Write(level);
        }

        string gameFile = Path.Combine(dir, "game.json");
        if (File.Exists(gameFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(gameFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("startLevel", out var sl) && sl.ValueKind == JsonValueKind.String) lib.StartLevelId = sl.GetString();
                if (root.TryGetProperty("startWorld", out var sw) && sw.ValueKind == JsonValueKind.String) lib.StartWorldId = sw.GetString();
                if (root.TryGetProperty("playerScript", out var ps) && ps.ValueKind == JsonValueKind.String) { lib.PlayerScriptId = ps.GetString(); World.DefaultPlayerScript = lib.PlayerScriptId ?? ""; }
            }
            catch (JsonException ex) { Console.Error.WriteLine($"[levels] game.json: {ex.Message}"); }
        }
        return lib;
    }

    /// <summary>Adds / replaces a level (kept in memory until <see cref="Save"/>).</summary>
    public void Set(string id, LevelData level)
    {
        level.Id = id;
        _raw[id] = LevelJson.Write(level);
        _cache[id] = level;
    }

    public string? GetRaw(string id) => _raw.TryGetValue(id, out var s) ? s : null;

    public bool Has(string? id) => id != null && Get(id) != null;

    /// <summary>Parsed level or null. The returned instance is shared; clone it before editing.</summary>
    public LevelData? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_cache.TryGetValue(id, out var cached)) return cached;
        LevelData? level = null;
        if (_raw.TryGetValue(id, out var raw))
        {
            try { level = LevelJson.Parse(raw); level.Id = id; }
            catch (InvalidDataException ex) { Console.Error.WriteLine($"[levels] '{id}': {ex.Message}"); }
        }
        _cache[id] = level;
        return level;
    }

    public void Remove(string id)
    {
        _raw.Remove(id);
        _cache.Remove(id);
        if (Directory != null)
        {
            string f = PathFor(id);
            if (File.Exists(f)) File.Delete(f);
        }
    }

    /// <summary>Ids of all valid levels in natural order (level_2 before level_10).</summary>
    public IEnumerable<string> Ids => _raw.Keys.Where(id => Get(id) != null).ToList();

    /// <summary>The configured start level, or the first level.</summary>
    public string? StartLevel => Has(StartLevelId) ? StartLevelId : Ids.FirstOrDefault();

    public string NextFreeId(string prefix = "level_")
    {
        int n = 1;
        while (_raw.ContainsKey(prefix + n)) n++;
        return prefix + n;
    }

    /// <summary>Makes an id usable as a file name.</summary>
    public static string Sanitize(string id)
    {
        var sb = new StringBuilder();
        foreach (char c in id.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.Length == 0 ? "level" : sb.ToString();
    }

    public string PathFor(string id) => Path.Combine(Directory ?? ".", Sanitize(id) + ".json");

    public void Save(string id)
    {
        if (Directory == null || !_raw.TryGetValue(id, out var raw)) return;
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(id), raw);
    }

    public void SaveAll()
    {
        foreach (var id in _raw.Keys.ToList()) Save(id);
        SaveGameFile();
    }

    public void SaveGameFile()
    {
        if (Directory == null) return;
        System.IO.Directory.CreateDirectory(Directory);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            if (StartLevelId != null) w.WriteString("startLevel", StartLevelId);
            if (StartWorldId != null) w.WriteString("startWorld", StartWorldId);
            if (!string.IsNullOrEmpty(PlayerScriptId)) w.WriteString("playerScript", PlayerScriptId);
            w.WriteEndObject();
        }
        File.WriteAllText(Path.Combine(Directory, "game.json"), Encoding.UTF8.GetString(ms.ToArray()) + "\n");
    }
}

/// <summary>Compares strings so that digit runs are compared as numbers ("level_2" &lt; "level_10").</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.Substring(si, i - si).TrimStart('0');
                var nb = b.Substring(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
