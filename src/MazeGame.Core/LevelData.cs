namespace MazeGame.Core;

/// <summary>An entity placed in a level. X/Y is the tile cell (y=0 is the bottom row).</summary>
public sealed class EntityDef
{
    public int X;
    public int Y;
    /// <summary>String id of the <see cref="EntityType"/>, e.g. "walker" or "slime".</summary>
    public string Type = "";
    /// <summary>Free-form settings stored in the level file ("text", "target", or anything your entity reads).</summary>
    public Dictionary<string, string> Props = new();

    public string Get(string name, string fallback = "") => Props.TryGetValue(name, out var v) ? v : fallback;

    public int GetInt(string name, int fallback = 0) =>
        Props.TryGetValue(name, out var v) && int.TryParse(v, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : fallback;

    public double GetDouble(string name, double fallback = 0) =>
        Props.TryGetValue(name, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : fallback;

    public bool GetBool(string name, bool fallback = false) =>
        Props.TryGetValue(name, out var v) ? v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" : fallback;

    public EntityDef Clone() => new() { X = X, Y = Y, Type = Type, Props = new Dictionary<string, string>(Props) };
}

/// <summary>A decoded level. Tiles are stored column-major: index = x * Height + y, y=0 is the bottom.</summary>
public sealed class LevelData
{
    public int Width = 70;
    public int Height = 40;
    public int[] Tiles = Array.Empty<int>();
    public List<EntityDef> Entities = new();

    /// <summary>Unique string id (also the file name). Doors, overworld nodes and saves refer to levels by it.</summary>
    public string Id = "";
    /// <summary>Id of the level that follows when this one is completed (optional).</summary>
    public string? Next;
    /// <summary>Levels entered by walking off the left / right edge of this one (optional).</summary>
    public string? Left, Right;
    /// <summary>Script file (under the assets folder, e.g. "scripts/intro.mgs") run while this level is played.</summary>
    public string Script = "";
    /// <summary>Script id run by the player while this level is played (empty = game.json "playerScript").</summary>
    public string PlayerScript = "";
    /// <summary>Background / foreground tile and image layers around the main (collision) layer.</summary>
    public List<Layer> Layers = new();
    /// <summary>Editor only: which grid <see cref="Get"/> / <see cref="Set"/> use. 0 = main layer, n = Layers[n-1].</summary>
    public int EditLayer;
    public int[] Grid => EditLayer > 0 && EditLayer <= Layers.Count && Layers[EditLayer - 1].Kind == LayerKind.Tiles
        ? Layers[EditLayer - 1].Tiles : Tiles;

    // Header fields (names follow the original variables)
    public string BackgroundColor = "#f0f000";
    public int Backdrop = 1;
    public int Flags = 5;            // level_flags_01 : bit0 = parallax background, bit1 = underwater
    public int CameraMode;           // level_flag_camera (low byte)
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
    public int Get(int x, int y) => InBounds(x, y) ? Grid[Index(x, y)] : 0;

    public void Set(int x, int y, int tile)
    {
        if (InBounds(x, y)) Grid[Index(x, y)] = tile;
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
        int[] Move(int[] old)
        {
            var n = new int[newWidth * newHeight];
            Array.Fill(n, TileInfo.Air);
            for (int x = 0; x < Math.Min(Width, newWidth); x++)
                for (int y = 0; y < Math.Min(Height, newHeight); y++)
                    n[x * newHeight + y] = old[x * Height + y];
            return n;
        }
        var main = Move(Tiles);
        foreach (var l in Layers)
            if (l.Kind == LayerKind.Tiles) l.Tiles = Move(l.Tiles);
        Entities.RemoveAll(e => e.X >= newWidth || e.Y >= newHeight);
        Width = newWidth;
        Height = newHeight;
        Tiles = main;
    }

    public LevelData Clone()
    {
        var c = (LevelData)MemberwiseClone();
        c.Tiles = (int[])Tiles.Clone();
        c.Entities = Entities.Select(e => e.Clone()).ToList();
        c.Layers = Layers.Select(l => l.Clone()).ToList();
        return c;
    }
}
