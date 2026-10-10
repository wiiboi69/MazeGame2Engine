namespace MazeGame.Core;

/// <summary>A tile of the overworld's own 2D tileset (separate from the level tiles). Cells are 32x32 pixels.</summary>
public sealed class OverworldTileDef
{
    public string Id { get; }
    public string Name { get; set; }
    public string Category { get; set; } = "Terrain";
    /// <summary>png / svg under assets/ (drawn at 2x when 16x16). Ignored when <see cref="Frames"/> is set.</summary>
    public string Texture { get; set; } = "";
    public string[]? Frames { get; set; }
    public double Fps { get; set; } = 3;
    /// <summary>Free-roam mode: can the avatar walk on it?</summary>
    public bool Walkable { get; set; } = true;
    /// <summary>Free-roam walking speed multiplier (e.g. 0.6 for hills).</summary>
    public double Speed { get; set; } = 1;

    public OverworldTileDef(string id, string name) { Id = id; Name = name; }
}

/// <summary>
/// Registry of overworld tiles. Add your own in <c>GameContent.RegisterOverworldTiles</c>:
/// <c>OverworldTileRegistry.Register(new OverworldTileDef("swamp", "Swamp") { Texture = "overworld/swamp.png", Speed = 0.5 });</c>
/// </summary>
public static class OverworldTileRegistry
{
    private static readonly Dictionary<string, OverworldTileDef> ById = new(StringComparer.Ordinal);
    private static readonly List<OverworldTileDef> Ordered = new();

    public static IReadOnlyList<OverworldTileDef> All => Ordered;

    public static OverworldTileDef? Find(string? id) => id != null && ById.TryGetValue(id, out var d) ? d : null;

    public static void Register(OverworldTileDef def)
    {
        if (ById.TryGetValue(def.Id, out var old)) Ordered.Remove(old);
        ById[def.Id] = def;
        Ordered.Add(def);
    }
}
