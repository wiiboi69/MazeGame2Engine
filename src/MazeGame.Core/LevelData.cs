namespace MazeGame.Core;

/// <summary>An entity placed in a level. X/Y is the tile cell (y=0 is the bottom row).</summary>
public sealed class EntityDef
{
    public int X;
    public int Y;
    /// <summary>The tile id used as the brush (29,30,46,47,68,77,78,79).</summary>
    public int Type;
    /// <summary>
    /// Original "spedi" value. Triggers: target level number. Useable NPC: hex-encoded UTF-8 text
    /// (hex keeps the file readable by the original Scratch reader). Otherwise empty.
    /// </summary>
    public string Param = "";

    public EntityDef Clone() => new() { X = X, Y = Y, Type = Type, Param = Param };
}

/// <summary>A decoded level. Tiles are stored column-major: index = x * Height + y, y=0 is the bottom.</summary>
public sealed class LevelData
{
    public int Width = 70;
    public int Height = 40;
    public int[] Tiles = Array.Empty<int>();
    public List<EntityDef> Entities = new();

    // Header fields (names follow the original variables)
    public string BackgroundColor = "#f0f000";
    public int Backdrop = 1;
    public int Flags = 5;            // level_flags_01 : bit0 = parallax background, bit1 = underwater
    public int CameraMode;           // level_flag_camera (low byte)
    public string ForkVersion = "mg2ex";
    public string Version = "1.0v";
    public string Name = "test";
    public int Difficulty;
    public string Song = "0";
    public int Custom = 69;          // level_custom
    public int ExtraFlags = 420;     // level_flags

    public bool ShowBackground => (Flags & GameConstants.FlagShowBackground) != 0;
    public bool Underwater => (Flags & GameConstants.FlagUnderwater) != 0;

    public LevelData() { }

    public LevelData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new int[width * height];
        Array.Fill(Tiles, TileInfo.Air);
    }

    public int Index(int x, int y) => x * Height + y;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Tile id at a cell; anything outside the grid is empty (id 0, shape "").</summary>
    public int Get(int x, int y) => InBounds(x, y) ? Tiles[Index(x, y)] : 0;

    public void Set(int x, int y, int tile)
    {
        if (InBounds(x, y)) Tiles[Index(x, y)] = tile;
    }

    /// <summary>The original "generate level": wood wall columns at both ends, floor row at the bottom.</summary>
    public static LevelData CreateBlank(int width, int height)
    {
        var l = new LevelData(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool wall = x == 0 || x == width - 1;
                if (wall) l.Set(x, y, TileInfo.DefaultSolid);
                else if (y == 0) l.Set(x, y, TileInfo.DefaultSolid);
                else l.Set(x, y, TileInfo.Air);
            }
        }
        return l;
    }

    /// <summary>First spawn marker (tile 28) in original index order, or null.</summary>
    public (int x, int y)? FindSpawn()
    {
        for (int i = 0; i < Tiles.Length; i++)
            if (Tiles[i] == TileInfo.PlayerSpawn)
                return (i / Height, i % Height);
        return null;
    }

    public void Resize(int newWidth, int newHeight)
    {
        var n = new int[newWidth * newHeight];
        Array.Fill(n, TileInfo.Air);
        for (int x = 0; x < Math.Min(Width, newWidth); x++)
            for (int y = 0; y < Math.Min(Height, newHeight); y++)
                n[x * newHeight + y] = Tiles[Index(x, y)];
        Entities.RemoveAll(e => e.X >= newWidth || e.Y >= newHeight);
        Width = newWidth;
        Height = newHeight;
        Tiles = n;
    }

    public LevelData Clone()
    {
        var c = (LevelData)MemberwiseClone();
        c.Tiles = (int[])Tiles.Clone();
        c.Entities = Entities.Select(e => e.Clone()).ToList();
        return c;
    }

    // ---- entity param helpers -------------------------------------------------------------

    public static string EncodeText(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string DecodeText(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return "";
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(hex)); }
        catch (FormatException) { return ""; }
    }
}
