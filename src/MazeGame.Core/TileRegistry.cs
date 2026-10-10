namespace MazeGame.Core;

/// <summary>
/// All known tiles. The original 79 are registered automatically; add your own with <see cref="Register"/>
/// (see <c>Content/GameContent.cs</c>). Call it before loading any level, in both the game and the editor.
/// </summary>
public static class TileRegistry
{
    private static readonly Dictionary<string, TileDef> ByIdMap = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    private static readonly List<TileDef> AllTiles = new();
    private static TileDef?[] _byNum = new TileDef?[256];
    private static int _nextCustom = 80;
    private static bool _init;

    private static void EnsureInit()
    {
        if (_init) return;
        _init = true;
        BuiltinTiles.Register();
    }

    /// <summary>Every tile in registration order (built-ins first).</summary>
    public static IReadOnlyList<TileDef> All { get { EnsureInit(); return AllTiles; } }

    /// <summary>Adds a new tile and returns it. Throws if the id is already taken.</summary>
    public static TileDef Register(TileDef def)
    {
        EnsureInit();
        if (ByIdMap.TryGetValue(def.Id, out var existing))
        {
            if (!existing.Missing) throw new InvalidOperationException($"tile id '{def.Id}' is already registered");
            // a placeholder created while loading a level: the real definition takes over its number
            def.Num = existing.Num;
            AllTiles[AllTiles.IndexOf(existing)] = def;
            _byNum[def.Num] = def;
            ByIdMap[def.Id] = def;
            return def;
        }
        Add(def, _nextCustom++);
        return def;
    }

    /// <summary>Convenience: registers a plain custom tile.</summary>
    public static TileDef Register(string id, string name, string shape = "", string? texture = null,
                                   string category = "Custom", bool deadly = false)
        => Register(new TileDef(id, name, shape, "", "", category) { Texture = texture, Deadly = deadly });

    /// <summary>Lets old level files keep working after you rename a tile id.</summary>
    public static void Alias(string oldId, string newId) => Aliases[oldId] = newId;

    internal static void RegisterBuiltin(TileDef def, int num) => Add(def, num);

    private static void Add(TileDef def, int num)
    {
        def.Num = num;
        if (num >= _byNum.Length) Array.Resize(ref _byNum, Math.Max(num + 1, _byNum.Length * 2));
        _byNum[num] = def;
        ByIdMap[def.Id] = def;
        AllTiles.Add(def);
    }

    public static TileDef? Find(string id)
    {
        EnsureInit();
        if (Aliases.TryGetValue(id, out var a)) id = a;
        return ByIdMap.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>Finds a tile, or creates an invisible placeholder so unknown ids survive a load/save round trip.</summary>
    public static TileDef Resolve(string id)
    {
        var d = Find(id);
        if (d != null) return d;
        d = new TileDef(id, id + " (missing)", "", "", "", "Missing") { Missing = true };
        Add(d, _nextCustom++);
        Console.Error.WriteLine($"[tiles] unknown tile id '{id}' - register it in GameContent.cs");
        return d;
    }

    public static TileDef? ByNum(int num)
    {
        EnsureInit();
        return num >= 0 && num < _byNum.Length ? _byNum[num] : null;
    }

    /// <summary>String id for a runtime tile number ("air" for anything unknown).</summary>
    public static string IdOf(int num) => ByNum(num)?.Id ?? "air";

    public static int NumOf(string id) => Resolve(id).Num;
}
