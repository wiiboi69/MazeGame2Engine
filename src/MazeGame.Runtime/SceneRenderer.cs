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
    /// <summary>Seconds, advanced by the host every frame; drives animated tiles.</summary>
    public double AnimTime;

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

    /// <summary>Switch to Scratch stage coordinates (480x360, origin centre, y up) for menus and overlays.</summary>
    public void BeginStage() { Ppu = 2; CenterX = 0; CenterY = 0; }

    /// <summary>Does the stage point (mx, my) touch the visible part of a sprite placed at (x, y)?</summary>
    public static bool Hit(Sprite s, double x, double y, double scale, double mx, double my)
    {
        double cx = x + s.VisDx * scale, cy = y - s.VisDy * scale;
        return Math.Abs(mx - cx) <= s.VisHw * scale && Math.Abs(my - cy) <= s.VisHh * scale;
    }

    // ================================================================ background

    public void DrawBackground(LevelData level, double camX, double camY)
    {
        int idx = Math.Clamp(level.Backdrop, 1, 3);
        var stageKey = idx switch { 2 => "backdrop1", 3 => "backdrop2", _ => "Blue" };
        Ppu = 2;
        CenterX = camX; CenterY = camY;
        // fixed stage backdrop: centre of the stage is world (camX, camY)
        var backdrop = _sprites.Get("stage", stageKey);
        double fill = backdrop == null ? 1.0 : Math.Max(1.0, Math.Max(AppWindow.VW / Ppu / backdrop.SrcW, AppWindow.VH / Ppu / backdrop.SrcH));
        DrawSprite(backdrop, camX, camY, fill);

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
    public void DrawTiles(int[] tiles, int width, int height, bool editorView, int alpha = 255)
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
                var spr = _sprites.Tile(t, AnimTime);
                if (spr == null) continue;
                // built-in costumes are 16 units drawn at 200%; custom textures are stretched to one 32x32 cell
                double sc = TileRegistry.ByNum(t)?.Texture != null || TileRegistry.ByNum(t)?.Frames != null ? 32.0 / spr.SrcW : 2.0;
                DrawSprite(spr, x * 32 + 16, y * 32 + 16, sc, false, 0, alpha, true);
            }
        }
    }

    // ================================================================ layers

    /// <summary>Draws the extra layers of a level that sit behind (front = false) or in front of the main layer.</summary>
    public void DrawLayers(LevelData level, double camX, double camY, bool front, bool editorView = false, int editLayer = 0)
    {
        for (int i = 0; i < level.Layers.Count; i++)
        {
            var layer = level.Layers[i];
            if (layer.Front != front) continue;
            if (!layer.Visible && !editorView) continue;
            // in the editor the layers that are not being edited are drawn dimmed
            int alpha = editorView && editLayer != 0 && editLayer != i + 1 ? 110 : 255;
            if (!layer.Visible) alpha = 70;

            double saveX = CenterX, saveY = CenterY;
            if (layer.Kind == LayerKind.Image && layer.FitScreen)
            {
                var img = _sprites.GetFile(layer.Image);
                if (img != null)
                {
                    double scale = Math.Max(AppWindow.VW / Ppu / img.SrcW, AppWindow.VH / Ppu / img.SrcH);
                    DrawSprite(img, camX, camY, scale, false, 0, alpha);
                }
                continue;
            }

            CenterX = camX * layer.Parallax;
            CenterY = camY * layer.Parallax;
            if (layer.Kind == LayerKind.Tiles)
            {
                if (layer.Tiles.Length == level.Width * level.Height)
                    DrawTiles(layer.Tiles, level.Width, level.Height, editorView, alpha);
            }
            else
            {
                DrawImageLayer(layer, alpha);
            }
            CenterX = saveX; CenterY = saveY;
        }
    }

    private void DrawImageLayer(Layer layer, int alpha)
    {
        var img = _sprites.GetFile(layer.Image);
        if (img == null) return;
        double w = img.SrcW * layer.Scale, h = img.SrcH * layer.Scale;
        if (w <= 0 || h <= 0) return;
        // sprites are drawn by their centre; the layer position is the centre of the first image
        double viewL = WorldX(0), viewR = WorldX(AppWindow.VW), viewB = WorldY(AppWindow.VH), viewT = WorldY(0);

        int ix0 = 0, ix1 = 0, iy0 = 0, iy1 = 0;
        if (layer.RepeatX) { ix0 = (int)Math.Floor((viewL - layer.X) / w + 0.5); ix1 = (int)Math.Floor((viewR - layer.X) / w + 0.5); }
        if (layer.RepeatY) { iy0 = (int)Math.Floor((viewB - layer.Y) / h + 0.5); iy1 = (int)Math.Floor((viewT - layer.Y) / h + 0.5); }
        for (int ix = ix0; ix <= ix1; ix++)
            for (int iy = iy0; iy <= iy1; iy++)
                DrawSprite(img, layer.X + ix * w, layer.Y + iy * h, layer.Scale, false, 0, alpha);
    }

    /// <summary>
    /// Draws an image layer in screen pixels (y down). Used by the overworld map and its editor: same properties as
    /// level image layers. (scrollX, scrollY) is the map position at the top-left of the area (areaX, areaY, areaW, areaH).
    /// </summary>
    public void DrawMapLayer(ImageLayerProps layer, double scrollX, double scrollY, float areaX, float areaY, float areaW, float areaH)
    {
        var img = _sprites.GetFile(layer.Image);
        if (img == null) return;
        if (layer.FitScreen)
        {
            float k = (float)Math.Max(areaW / img.SrcW, areaH / img.SrcH);
            DrawSpriteScreen(img, areaX + areaW / 2, areaY + areaH / 2, k);
            return;
        }
        float ppu = (float)layer.Scale;
        float w = img.SrcW * ppu, h = img.SrcH * ppu;
        if (w <= 0 || h <= 0) return;
        double cx = layer.X - scrollX * layer.Parallax + areaX, cy = layer.Y - scrollY * layer.Parallax + areaY;
        int ix0 = 0, ix1 = 0, iy0 = 0, iy1 = 0;
        if (layer.RepeatX) { ix0 = (int)Math.Floor((areaX - cx) / w + 0.5); ix1 = (int)Math.Floor((areaX + areaW - cx) / w + 0.5); }
        if (layer.RepeatY) { iy0 = (int)Math.Floor((areaY - cy) / h + 0.5); iy1 = (int)Math.Floor((areaY + areaH - cy) / h + 0.5); }
        for (int ix = ix0; ix <= ix1; ix++)
            for (int iy = iy0; iy <= iy1; iy++)
                DrawSpriteScreen(img, (float)(cx + ix * w), (float)(cy + iy * h), ppu);
    }

    /// <summary>The sprite of an overworld tile at the given time (animated tiles cycle their frames).</summary>
    public Sprite? OverworldTileSprite(OverworldTileDef def, double time)
    {
        if (def.Frames is { Length: > 0 })
            return _sprites.GetFile(def.Frames[(int)Math.Floor(time * def.Fps) % def.Frames.Length]);
        return def.Texture.Length > 0 ? _sprites.GetFile(def.Texture) : null;
    }

    /// <summary>
    /// Draws the overworld's tile grid in screen pixels into the area (areaX, areaY, areaW, areaH);
    /// (scrollX, scrollY) is the map pixel at the top-left of the area.
    /// </summary>
    public void DrawOverworldTiles(OverworldData w, double scrollX, double scrollY, float areaX, float areaY, float areaW, float areaH, double time)
    {
        if (w.Cells.Length != w.Width * w.Height) return;
        const int C = OverworldData.CellSize;
        int x0 = Math.Max(0, (int)Math.Floor(scrollX / C)), x1 = Math.Min(w.Width - 1, (int)Math.Floor((scrollX + areaW) / C));
        int y0 = Math.Max(0, (int)Math.Floor(scrollY / C)), y1 = Math.Min(w.Height - 1, (int)Math.Floor((scrollY + areaH) / C));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                string id = w.Cells[y * w.Width + x];
                if (id.Length == 0) continue;
                var def = OverworldTileRegistry.Find(id);
                var spr = def == null ? null : OverworldTileSprite(def, time);
                if (spr == null) continue;
                float k = C / spr.SrcW;
                DrawSpriteScreen(spr, (float)(areaX + x * C + C / 2.0 - scrollX), (float)(areaY + y * C + C / 2.0 - scrollY), k);
            }
    }

    // ================================================================ entities

    private Sprite? EntitySprite(Entity e) =>
        e.Texture != null ? _sprites.GetFile(e.Texture) : _sprites.ByIndex("enemy", e.Costume);

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

    /// <summary>Everything in the right order for gameplay: backdrop, back layers, entities, tiles, player, front layers.</summary>
    public void DrawWorld(World w)
    {
        double cx = w.CamX + w.ShakeX, cy = w.CamY + w.ShakeY, ppu = 2 * w.CamZoom;
        Ppu = 2;
        CenterX = cx; CenterY = cy;
        DrawBackground(w.Level, cx, cy);
        DrawLayers(w.Level, cx, cy, front: false, editorView: w.GodMode);
        Ppu = ppu; CenterX = cx; CenterY = cy;
        DrawEntities(w, EntityLayer.BehindTiles);
        if (w.PlayerBehindTiles) DrawPlayer(w);
        DrawTiles(w.Tiles, w.Level.Width, w.Level.Height, w.GodMode);
        DrawEntities(w, EntityLayer.Normal);
        if (!w.PlayerBehindTiles) DrawPlayer(w);
        DrawEntities(w, EntityLayer.Front);
        DrawParticles(w);
        Ppu = 2;
        DrawLayers(w.Level, cx, cy, front: true, editorView: w.GodMode);
        Ppu = ppu; CenterX = cx; CenterY = cy;
    }
}
