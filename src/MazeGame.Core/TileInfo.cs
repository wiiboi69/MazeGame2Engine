namespace MazeGame.Core;

/// <summary>Tile ids and lookups. Ids are the original Scratch costume numbers (1-based).</summary>
public static class TileInfo
{
    public const int Blank = 1;          // costume "BIG" - invisible helper, treated as air
    public const int Air = 2;
    public const int LadderTop = 22;
    public const int Ladder = 23;
    public const int Gem1 = 24, Gem2 = 25, Gem3 = 26, Gem4 = 27;
    public const int PlayerSpawn = 28;
    public const int Walker = 29;        // entity brush, "nomal" npc
    public const int Danger = 30;        // entity brush, stompable enemy
    public const int Cloud = 32;
    public const int Star = 46;          // entity brush, "useable" npc (dialog)
    public const int EndBox = 47;        // entity brush, level goal
    public const int Piranha = 68;       // entity brush, pipe plant ("pole")
    public const int CloudSolid = 75;    // 'R' shape
    public const int LogicAuto = 74;
    public const int LogicDeath = 76;
    public const int DoorTrigger = 77;
    public const int PipeTrigger = 78;
    public const int DoorWideTrigger = 79;
    public const int DefaultSolid = 10;  // Block-Wood, the default "wall" tile

    public const int TileCount = 79;

    public static string Shape(int t) => t >= 0 && t < TileTables.Shape.Length ? TileTables.Shape[t] : "";
    public static string Group(int t) => t >= 0 && t < TileTables.Group.Length ? TileTables.Group[t] : "";
    public static string Recipes(int t) => t >= 0 && t < TileTables.Recipes.Length ? TileTables.Recipes[t] : "";
    public static string Keymap(int t) => t >= 0 && t < TileTables.Keymap.Length ? TileTables.Keymap[t] : "";

    /// <summary>Tiles that are "brushes" for placing entities rather than real tiles.</summary>
    public static bool IsEntityBrush(int t) =>
        t == Walker || t == Danger || t == Star || t == EndBox || t == Piranha ||
        t == DoorTrigger || t == PipeTrigger || t == DoorWideTrigger;

    public static bool IsTrigger(int t) => t == DoorTrigger || t == PipeTrigger || t == DoorWideTrigger;

    /// <summary>Tiles that only appear in the editor (markers / logic), never in game.</summary>
    public static bool IsEditorOnly(int t) => t == PlayerSpawn || t == LogicAuto || t == LogicDeath || t == Blank;

    public static bool IsGem(int t) => t > Ladder && t < PlayerSpawn;

    public static int GemValue(int t) => t switch { Gem1 => 1, Gem2 => 2, Gem3 => 5, Gem4 => 10, _ => 0 };
}
