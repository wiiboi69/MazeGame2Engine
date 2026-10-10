namespace MazeGame.Core;

public enum LayerKind { Tiles, Image }

/// <summary>
/// The image properties shared by level layers and overworld map layers (so both are edited and stored the same way).
/// </summary>
public class ImageLayerProps
{
    /// <summary>png / svg path relative to the assets folder.</summary>
    public string Image = "";
    /// <summary>Position of the image centre (level: world units, y up; map: pixels, y down).</summary>
    public double X, Y;
    /// <summary>Units per image pixel.</summary>
    public double Scale = 1;
    /// <summary>How far the layer moves with the camera: 1 = with the world, 0 = fixed on screen, 0.5 = half speed.</summary>
    public double Parallax = 1;
    public bool RepeatX, RepeatY;
    /// <summary>Stretch the image over the whole screen (ignores position, scale, repeat and parallax) - good for skies.</summary>
    public bool FitScreen;
}

/// <summary>
/// An extra layer of a level, drawn behind (<see cref="Front"/> = false) or in front of the main tile layer.
/// Tile layers are decoration only (no collision); image layers use the same properties as overworld map layers.
/// The order of the layers list is the drawing order (later = on top, within back / front).
/// </summary>
public sealed class Layer : ImageLayerProps
{
    public string Name = "layer";
    public LayerKind Kind = LayerKind.Tiles;
    public bool Front;
    public bool Visible = true;

    // ---- tile layers: same size as the level, column-major like the main layer
    public int[] Tiles = Array.Empty<int>();

    public static Layer NewTiles(string name, int width, int height, bool front)
    {
        var l = new Layer { Name = name, Kind = LayerKind.Tiles, Front = front, Tiles = new int[width * height] };
        Array.Fill(l.Tiles, TileInfo.Air);
        return l;
    }

    public Layer Clone()
    {
        var c = (Layer)MemberwiseClone();
        c.Tiles = (int[])Tiles.Clone();
        return c;
    }
}
