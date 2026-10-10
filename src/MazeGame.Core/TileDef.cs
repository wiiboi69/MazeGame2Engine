namespace MazeGame.Core;

/// <summary>
/// One kind of tile. Tiles are identified in level files by their string <see cref="Id"/>; <see cref="Num"/> is the
/// small integer the engine uses at run time (assigned when the tile is registered, never saved to disk).
/// </summary>
public sealed class TileDef
{
    /// <summary>Stable string id used in level files, e.g. "block_wood" or "lava".</summary>
    public string Id { get; }

    /// <summary>Runtime number (assigned by <see cref="TileRegistry"/>).</summary>
    public int Num { get; internal set; }

    /// <summary>Display name in the editor.</summary>
    public string Name { get; set; }

    /// <summary>
    /// Collision shape: "" = none (walk through), "#" = solid, "=" = one-way platform, "L" = ladder, "R" = cloud,
    /// "/10" "\\10" ... = slopes (see the original shape list), "T-.." = trigger markers.
    /// </summary>
    public string Shape { get; set; }

    /// <summary>Auto-tiling: tiles with the same non-empty group join up; <see cref="Recipes"/> lists the edge patterns.</summary>
    public string Group { get; set; }
    public string Recipes { get; set; }

    /// <summary>Editor palette section.</summary>
    public string Category { get; set; }

    /// <summary>
    /// Image for a custom tile: a .png or .svg path relative to the assets folder (e.g. "custom/lava.png").
    /// It is stretched to one 32x32 cell. Null for the built-in tiles, which use the original costumes.
    /// </summary>
    public string? Texture { get; set; }

    /// <summary>
    /// Animation: image paths (like <see cref="Texture"/>) played in a loop at <see cref="Fps"/> frames per second.
    /// An entry written as "tile:some_id" shows another tile's picture. Overrides <see cref="Texture"/>.
    /// </summary>
    public string[]? Frames { get; set; }
    public double Fps { get; set; } = 6;

    /// <summary>Only shown in the editor (markers, logic tiles).</summary>
    public bool EditorOnly { get; set; }

    /// <summary>&gt; 0 makes the tile a collectible gem worth this many coins.</summary>
    public int GemValue { get; set; }

    /// <summary>Touching this tile kills the player.</summary>
    public bool Deadly { get; set; }

    /// <summary>A dying player standing in this tile is caught and put back into play (the original "logic" tile).</summary>
    public bool CatchesDeath { get; set; }

    /// <summary>When set, this tile is an editor brush that places the entity type with this id instead of a tile.</summary>
    public string? Entity { get; set; }

    /// <summary>True for ids that were found in a level file but never registered.</summary>
    public bool Missing { get; internal set; }

    public bool IsGem => GemValue > 0;

    public TileDef(string id, string name, string shape = "", string group = "", string recipes = "", string category = "Custom")
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("tile id must not be empty", nameof(id));
        Id = id;
        Name = name;
        Shape = shape;
        Group = group;
        Recipes = recipes;
        Category = category;
    }

    public override string ToString() => $"{Id} ({Num})";
}
