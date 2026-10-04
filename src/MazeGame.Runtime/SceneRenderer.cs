using System.Numerics;
using MazeGame.Core;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>
/// Draws levels and worlds into the current 960x720 render target. World units: 32 per tile;
/// <see cref="Ppu"/> = render pixels per world unit (2 in game, variable zoom in the editor).
/// Sprites are placed by their rotation centre exactly like in Scratch, then scaled by size%/100.
/// </summary>
public sealed class SceneRenderer
{
    private readonly SpriteLibrary _sprites;

    public double CenterX, CenterY;        // world position shown at the middle of the screen
    public double Ppu = 2;                 // pixels per world unit

    public SceneRenderer(SpriteLibrary sprites) { _sprites = sprites; }

    public SpriteLibrary Sprites => _sprites;

    private static Color White => new Color((byte)255, (byte)255, (byte)255, (byte)255);
    private static Color Alpha(int a) => new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(a, 0, 255));

    public float SX(double wx) => (float)((wx - CenterX) * Ppu + AppWindow.VW / 2.0);
    public float SY(double wy) => (float)(AppWindow.VH / 2.0 - (wy - CenterY) * Ppu);

    public double WorldX(float sx) => (sx - AppWindow.VW / 2.0) / Ppu + CenterX;
    public double WorldY(float sy) => (AppWindow.VH / 2.0 - sy) / Ppu + CenterY;

    /// <summary>Draw a sprite with its rotation centre at world (wx, wy). scale = Scratch size / 100.</summary>
    public void DrawSprite(Sprite? s, double wx, double wy, double scale, bool flipX = false,
                           double rotationDeg = 0, int alpha = 255, bool snap = false)
    {
        if (s == null) return;
        float ppu = (float)(scale * Ppu);
        float px = SX(wx), py = SY(wy);
        if (snap) { px = MathF.Round(px); py = MathF.Round(py); }
        float dw = s.SrcW * ppu, dh = s.SrcH * ppu;
        if (px + dw < 0 || py + dh < 0 || px - dw > AppWindow.VW || py - dh > AppWindow.VH) return;
        float ox = (flipX ? s.SrcW - s.Cx : s.Cx) * ppu;
        float oy = s.Cy * ppu;
        var src = new Rectangle(0, 0, flipX ? -s.Tex.Width : s.Tex.Width, s.Tex.Height);
        Raylib.DrawTexturePro(s.Tex, src, new Rectangle(px, py, dw, dh), new Vector2(ox, oy),
            (float)rotationDeg, alpha >= 255 ? White : Alpha(alpha));
    }

    /// <summary>Draw a sprite in screen pixels (UI icons): rotation centre at (sx, sy), ppu = pixels per source unit.</summary>
    public void DrawSpriteScreen(Sprite? s, float sx, float sy, float ppu, int alpha = 255)
    {
        if (s == null) return;
        var src = new Rectangle(0, 0, s.Tex.Width, s.Tex.Height);
        Raylib.DrawTexturePro(s.Tex, src, new Rectangle(sx, sy, s.SrcW * ppu, s.SrcH * ppu),
            new Vector2(s.Cx * ppu, s.Cy * ppu), 0f, alpha >= 255 ? White : Alpha(alpha));
    }

    // ================================================================ background

    public void DrawBackground(LevelData level, double camX, double camY)
    {
        int idx = Math.Clamp(level.Backdrop, 1, 3);
        var stageKey = idx switch { 2 => "backdrop1", 3 => "backdrop2", _ => "Blue" };
        Ppu = 2;
        CenterX = camX; CenterY = camY;
        // fixed stage backdrop: centre of the stage is world (camX, camY)
        DrawSprite(_sprites.Get("stage", stageKey), camX, camY, 1.0);

        var back = _sprites.Get("background", "Background-1-back");
        double backY = camY < 1400 ? 220 - camY / 3.4 : -183.82352941176458;
        DrawSprite(back, camX, camY + backY, 2.0);

        if (!level.ShowBackground) return;

        double pieceW = 512;
        string[] pieces = { "Background-1-1", "Background-1-2", "Background-1-3", "Background-1-4" };
        for (int i = 0; i < pieces.Length; i++)
        {
            double screenX = i * pieceW - camX / 4.0;
            double x = Mod(screenX, pieceW * 4) - pieceW;
            double y = 220 - camY / 4.0;
            // wrap-around: also draw one period to the right so there is never a gap
            DrawSprite(_sprites.Get("background", pieces[i]), camX + x, camY + y, 2.0);
            DrawSprite(_sprites.Get("background", pieces[i]), camX + x + pieceW * 4, camY + y, 2.0);
        }

        double l2 = Mod(((4 * pieceW - camX / 4.0) * 1.3), pieceW * 4) - pieceW - 512 * 1.041;
        DrawSprite(_sprites.Get("background", "Background-1-1-L2"), camX + l2, camY + 220 - camY / 4.0, 2.0);
        DrawSprite(_sprites.Get("background", "Background-1-1-L2"), camX + l2 + pieceW * 4, camY + 220 - camY / 4.0, 2.0);

        DrawSprite(_sprites.Get("background", "Background-1-frount"), camX, camY + 220 - camY / 4.0, 2.0);
    }

    private static double Mod(double a, double b)
    {
        double r = a % b;
        return r < 0 ? r + b : r;
    }

    // ================================================================ tiles

    /// <summary>Draw a tile grid (column-major). <paramref name="editorView"/> shows markers and logic tiles.</summary>
    public void DrawTiles(int[] tiles, int width, int height, bool editorView)
    {
        int x0 = Math.Max(0, (int)Math.Floor(WorldX(0) / 32) - 1);
        int x1 = Math.Min(width - 1, (int)Math.Floor(WorldX(AppWindow.VW) / 32) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(WorldY(AppWindow.VH) / 32) - 1);
        int y1 = Math.Min(height - 1, (int)Math.Floor(WorldY(0) / 32) + 1);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                int t = tiles[x * height + y];
                if (t <= TileInfo.Air) continue;
                if (!editorView && TileInfo.IsEditorOnly(t)) continue;
                DrawSprite(_sprites.Tile(t), x * 32 + 16, y * 32 + 16, 2.0, snap: true);
            }
        }
    }

    // ================================================================ entities

    private Sprite? EntitySprite(Entity e) => _sprites.ByIndex("enemy", e.Costume);

    public void DrawEntities(World w, EntityLayer layer)
    {
        foreach (var e in w.Entities)
        {
            if (e.Layer != layer || !e.Visible) continue;
            DrawSprite(EntitySprite(e), e.X, e.Y, e.SizePct / 100.0, e.FlipX,
                e.AllAround ? e.RotationDegrees : 0, 255, false);
        }
    }

    /// <summary>Editor: draw placed entity definitions (including invisible triggers) as their costumes.</summary>
    public void DrawEntityDefs(LevelData level, EntityDef? highlight = null)
    {
        foreach (var d in level.Entities)
        {
            var e = Entity.Spawn(d, level.Height, true);
            if (e == null) continue;
            DrawSprite(EntitySprite(e), e.X, e.Y, e.SizePct / 100.0, false, 0, d == highlight ? 255 : 230);
        }
    }

    public void DrawPlayer(World w)
    {
        var p = w.Player;
        if (!p.Visible) return;
        var s = _sprites.ByIndex("player", p.CostumeIndex);
        DrawSprite(s, p.X, p.Y, 2.0, p.FlipX, p.Rotation == RotationStyle.AllAround ? p.RotationDegrees : 0);
    }

    public void DrawParticles(World w)
    {
        foreach (var p in w.Particles)
        {
            var s = _sprites.ByIndex("particles", p.Costume);
            DrawSprite(s, p.X, p.Y, 2.0, false, 0, (int)(255 * (1 - Math.Clamp(p.Ghost, 0, 100) / 100.0)));
        }
    }

    /// <summary>Everything in the right order for gameplay: background, back entities, tiles, entities, player.</summary>
    public void DrawWorld(World w)
    {
        Ppu = 2;
        CenterX = w.CamX; CenterY = w.CamY;
        DrawBackground(w.Level, w.CamX, w.CamY);
        DrawEntities(w, EntityLayer.BehindTiles);
        if (w.PlayerBehindTiles) DrawPlayer(w);
        DrawTiles(w.Tiles, w.Level.Width, w.Level.Height, w.GodMode);
        DrawEntities(w, EntityLayer.Normal);
        if (!w.PlayerBehindTiles) DrawPlayer(w);
        DrawEntities(w, EntityLayer.Front);
        DrawParticles(w);
    }
}
