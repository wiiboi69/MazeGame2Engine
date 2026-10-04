namespace MazeGame.Core;

/// <summary>
/// The "level store": numbered slots (1 = game settings, 2.. = levels) kept as files
/// <c>level_NNN.mgl</c> in a directory, each holding the original encoded level string.
/// </summary>
public sealed class LevelLibrary
{
    private readonly SortedDictionary<int, string> _raw = new();
    private readonly Dictionary<int, LevelData?> _cache = new();

    public string? Directory { get; private set; }

    public static LevelLibrary LoadDirectory(string dir)
    {
        var lib = new LevelLibrary { Directory = dir };
        if (System.IO.Directory.Exists(dir))
        {
            foreach (var file in System.IO.Directory.GetFiles(dir, "level_*.mgl"))
            {
                string name = Path.GetFileNameWithoutExtension(file);   // level_007
                if (int.TryParse(name.AsSpan("level_".Length), out int n))
                    lib._raw[n] = File.ReadAllText(file).Trim();
            }
        }
        return lib;
    }

    /// <summary>Adds / replaces a level (kept in memory until <see cref="Save"/>).</summary>
    public void Set(int number, LevelData level)
    {
        _raw[number] = LevelCodec.Encode(level);
        _cache[number] = level;
    }

    public void SetRaw(int number, string encoded)
    {
        _raw[number] = encoded;
        _cache.Remove(number);
    }

    public string? GetRaw(int number) => _raw.TryGetValue(number, out var s) ? s : null;

    /// <summary>Parsed level or null. The returned instance is shared; clone it before editing.</summary>
    public LevelData? Get(int number)
    {
        if (number < GameConstants.FirstLevel) return null;
        if (_cache.TryGetValue(number, out var cached)) return cached;
        LevelData? level = _raw.TryGetValue(number, out var raw) ? LevelCodec.Decode(raw) : null;
        _cache[number] = level;
        return level;
    }

    public void Remove(int number)
    {
        _raw.Remove(number);
        _cache.Remove(number);
        if (Directory != null)
        {
            string f = PathFor(number);
            if (File.Exists(f)) File.Delete(f);
        }
    }

    /// <summary>Numbers of all slots that contain a playable level, ascending.</summary>
    public IEnumerable<int> LevelNumbers => _raw.Keys.Where(n => n >= GameConstants.FirstLevel && Get(n) != null);

    public int StartLevel
    {
        get
        {
            if (_raw.TryGetValue(GameConstants.SettingsSlot, out var gs))
            {
                int? n = LevelCodec.DecodeSettings(gs);
                if (n.HasValue && Get(n.Value) != null) return n.Value;
            }
            return LevelNumbers.FirstOrDefault(GameConstants.FirstLevel);
        }
    }

    public int NextFreeNumber
    {
        get
        {
            int n = GameConstants.FirstLevel;
            while (_raw.ContainsKey(n)) n++;
            return n;
        }
    }

    public string PathFor(int number) => Path.Combine(Directory ?? ".", $"level_{number:D3}.mgl");

    public void Save(int number)
    {
        if (Directory == null || !_raw.TryGetValue(number, out var raw)) return;
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(number), raw);
    }

    public void SaveAll()
    {
        foreach (var n in _raw.Keys.ToList()) Save(n);
    }

    public void SetStartLevel(int number)
    {
        _raw[GameConstants.SettingsSlot] = $"gs_{number}_";
    }
}
