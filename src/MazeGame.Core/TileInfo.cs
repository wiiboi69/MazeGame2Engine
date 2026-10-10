namespace MazeGame.Core;

/// <summary>Runtime tile numbers of the built-in tiles (the original Scratch costume numbers) and lookups.
/// Level files use string ids instead - see <see cref="TileRegistry"/>.</summary>
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

    // ---- lookups by runtime tile number (data comes from TileRegistry) -----------------------------

    public static string Shape(int t) => TileRegistry.ByNum(t)?.Shape ?? "";
    public static string Group(int t) => TileRegistry.ByNum(t)?.Group ?? "";
    public static string Recipes(int t) => TileRegistry.ByNum(t)?.Recipes ?? "";

    /// <summary>Tiles that are "brushes" for placing entities rather than real tiles.</summary>
    public static bool IsEntityBrush(int t) => TileRegistry.ByNum(t)?.Entity != null;

    /// <summary>Tiles that only appear in the editor (markers / logic), never in game.</summary>
    public static bool IsEditorOnly(int t) => TileRegistry.ByNum(t)?.EditorOnly ?? false;

    public static bool IsGem(int t) => (TileRegistry.ByNum(t)?.GemValue ?? 0) > 0;
    public static int GemValue(int t) => TileRegistry.ByNum(t)?.GemValue ?? 0;
    public static bool IsDeadly(int t) => TileRegistry.ByNum(t)?.Deadly ?? false;
    public static bool CatchesDeath(int t) => TileRegistry.ByNum(t)?.CatchesDeath ?? false;
}
