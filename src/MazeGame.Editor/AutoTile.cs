using MazeGame.Core;

namespace MazeGame.Editor;

/// <summary>
/// Port of the editor's "Fix costume at" / "Build recipe": a tile that belongs to a tile group (wood, pipes,
/// slopes, orange blocks...) is swapped for the member whose edge recipe matches its four neighbours.
/// Recipe digits are: up, right, down, left neighbour belongs to the same group (1) or not (0).
/// </summary>
public static class AutoTile
{
    public static void Fix(LevelData level, int x, int y)
    {
        if (!level.InBounds(x, y)) return;
        int tile = level.Get(x, y);
        if (TileInfo.Recipes(tile).Length == 0) return;
        string group = TileInfo.Group(tile);
        if (group.Length == 0) return;

        string recipe = Neighbour(level, x, y + 1, group) + Neighbour(level, x + 1, y, group)
                      + Neighbour(level, x, y - 1, group) + Neighbour(level, x - 1, y, group);

        foreach (var t in TileRegistry.All)
        {
            if (t.Group == group && t.Recipes.Contains(recipe))
            {
                level.Set(x, y, t.Num);
                return;
            }
        }
    }

    private static string Neighbour(LevelData level, int x, int y, string group) =>
        level.InBounds(x, y) && TileInfo.Group(level.Get(x, y)) == group ? "1" : "0";

    /// <summary>Re-evaluates a cell and its four neighbours.</summary>
    public static void FixAround(LevelData level, int x, int y)
    {
        Fix(level, x, y);
        Fix(level, x, y + 1);
        Fix(level, x + 1, y);
        Fix(level, x, y - 1);
        Fix(level, x - 1, y);
    }
}
