using System.Numerics;
using MazeGame.Core;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Editor;

/// <summary>
/// Overworld map editor (the "Map" button): drag level nodes around, link them, set unlock requirements,
/// portals to other maps, and the image layers of the map. Maps are stored in <c>worlds/&lt;id&gt;.json</c>.
/// </summary>
public sealed partial class EditorApp
{
    private enum MapTool { Select, Add, Link, Paint, Fill, Pick }

    private const int MapPanelW = 330;
    private const int MapPalW = 150;          // tile palette on the left

    private string _owBrush = "grass";
    private float _owPalScroll;
    private readonly Stack<(int w, int h, string[] cells)> _owUndo = new();

    private readonly OverworldLibrary _owLib;
    private bool _worldMode;
    private OverworldData? _ow;
    private bool _owDirty;
    private MapTool _mapTool = MapTool.Select;
    private string _owSel = "";
    private string _owLinkFrom = "";
    private MapNode? _owDrag;
    private Vector2 _owDragOff;
    private bool _owPan;
    private float _owScrollX, _owScrollY;
    private float _owPanelScroll, _owPanelContent;
    private int _owLayerSel = -1;
    private bool _owShowGuides = true;

    // ================================================================ enter / leave

    private void EnterWorldMode()
    {
        CommitToLibrary();
        _worldMode = true;
        if (_ow == null || _owLib.Get(_ow.Id) == null)
        {
            string? first = _owLib.Ids.FirstOrDefault();
            if (first != null) _ow = _owLib.Get(first);
            else NewWorld();
        }
        _owSel = "";
        _owLinkFrom = "";
        _modal = Modal.None;
    }

    private void LeaveWorldMode()
    {
        SaveWorldsIfDirty();
        _worldMode = false;
        _modal = Modal.None;
    }

    private void SaveWorldsIfDirty()
    {
        if (!_owDirty) return;
        foreach (var id in _owLib.Ids) { var w = _owLib.Get(id); if (w != null) _owLib.Save(w); }
        _owDirty = false;
    }

    private void NewWorld()
    {
        var w = new OverworldData { Id = _owLib.NextFreeId() };
        w.Name = w.Id;
        _owLib.Set(w);
        _ow = w;
        _owSel = "";
        _owLayerSel = -1;
        _owDirty = true;
    }

    private void MarkDirty() { _owDirty = true; }

    private void CycleWorld(int dir)
    {
        var ids = _owLib.Ids.ToList();
        if (ids.Count == 0) return;
        int i = Math.Max(0, ids.IndexOf(_ow?.Id ?? ""));
        _ow = _owLib.Get(ids[(i + dir + ids.Count) % ids.Count]);
        _owSel = ""; _owLinkFrom = ""; _owLayerSel = -1;
        _owScrollX = _owScrollY = 0;
    }

    // ================================================================ helpers

    private Vector2 NodeScreen(MapNode n) => new((float)n.X - _owScrollX + MapPalW, (float)n.Y - _owScrollY + TopH);

    private MapNode? NodeAtScreen(Vector2 m)
    {
        if (_ow == null) return null;
        for (int i = _ow.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _ow.Nodes[i];
            var p = NodeScreen(n);
            if ((m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y) <= 26 * 26) return n;
        }
        return null;
    }

    private bool InMapCanvas(Vector2 m) => m.X >= MapPalW && m.X < AppWindow.VW - MapPanelW && m.Y >= TopH && m.Y < AppWindow.VH - BotH;

    private (int x, int y) OwCell(Vector2 m)
        => ((int)Math.Floor((m.X - MapPalW + _owScrollX) / OverworldData.CellSize), (int)Math.Floor((m.Y - TopH + _owScrollY) / OverworldData.CellSize));

    private void OwPushUndo()
    {
        if (_ow == null) return;
        _ow.EnsureGrid();
        _owUndo.Push((_ow.Width, _ow.Height, (string[])_ow.Cells.Clone()));
        while (_owUndo.Count > 60) { var keep = _owUndo.Reverse().Skip(1).ToArray(); _owUndo.Clear(); foreach (var k in keep) _owUndo.Push(k); }
    }

    private void OwDoUndo()
    {
        if (_ow == null || _owUndo.Count == 0) return;
        var (w, h, cells) = _owUndo.Pop();
        _ow.Width = w; _ow.Height = h; _ow.Cells = cells;
        MarkDirty();
    }

    private void OwFlood(int sx, int sy, string id)
    {
        var ow = _ow!;
        if (sx < 0 || sy < 0 || sx >= ow.Width || sy >= ow.Height) return;
        string old = ow.CellAt(sx, sy);
        if (old == id) return;
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= ow.Width || y >= ow.Height || ow.CellAt(x, y) != old) continue;
            ow.SetCell(x, y, id);
            stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }
    }

    private void HandleTilePaint(Vector2 m, bool press, bool down)
    {
        var ow = _ow!;
        bool rpress = Raylib.IsMouseButtonPressed(MouseButton.Right), rdown = Raylib.IsMouseButtonDown(MouseButton.Right);
        var (cx, cy) = OwCell(m);
        if (_mapTool == MapTool.Pick)
        {
            if (press)
            {
                string id = ow.CellAt(cx, cy);
                if (id.Length > 0) { _owBrush = id; _mapTool = MapTool.Paint; }
            }
            return;
        }
        if (press || rpress) OwPushUndo();
        if (_mapTool == MapTool.Fill)
        {
            if (press) { OwFlood(cx, cy, _owBrush); MarkDirty(); }
            else if (rpress) { OwFlood(cx, cy, ""); MarkDirty(); }
            return;
        }
        if (down) { ow.SetCell(cx, cy, _owBrush); MarkDirty(); }
        else if (rdown) { ow.SetCell(cx, cy, ""); MarkDirty(); }
    }

    private MapNode? SelectedNode => _ow?.Node(_owSel);

    private string NextNodeId()
    {
        for (int i = 1; ; i++) if (_ow!.Node("n" + i) == null) return "n" + i;
    }

    private void DeleteNode(MapNode n)
    {
        if (_ow == null) return;
        _ow.Nodes.Remove(n);
        foreach (var o in _ow.Nodes) { o.Links.Remove(n.Id); o.Requires.Remove(n.Id); }
        if (_ow.Start == n.Id) _ow.Start = _ow.Nodes.FirstOrDefault()?.Id ?? "";
        _owSel = "";
        MarkDirty();
    }

    private void RenameNode(MapNode n, string newId)
    {
        newId = LevelLibrary.Sanitize(newId);
        if (newId.Length == 0 || newId == n.Id) return;
        if (_ow!.Node(newId) != null) { Say($"Node '{newId}' already exists"); return; }
        string old = n.Id;
        foreach (var o in _ow.Nodes)
        {
            for (int i = 0; i < o.Links.Count; i++) if (o.Links[i] == old) o.Links[i] = newId;
            for (int i = 0; i < o.Requires.Count; i++) if (o.Requires[i] == old) o.Requires[i] = newId;
        }
        if (_ow.Start == old) _ow.Start = newId;
        n.Id = newId;
        _owSel = newId;
        MarkDirty();
    }

    private void ToggleLink(MapNode a, MapNode b)
    {
        if (a == b) return;
        if (a.Links.Contains(b.Id)) { a.Links.Remove(b.Id); b.Links.Remove(a.Id); Say("Link removed"); }
        else { a.Links.Add(b.Id); b.Links.Add(a.Id); Say("Linked"); }
        MarkDirty();
    }

    private void TextPrompt(string title, string initial, Action<string> ok)
        => OpenPrompt(title, initial, false, v => { ok((v ?? "").Trim()); MarkDirty(); }, () => { });

    private static bool TryNum(string s, out double d)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);

    // ================================================================ update

    private void UpdateWorld(float dt)
    {
        var m = _win.Mouse;
        if (_modal == Modal.Prompt) { UpdatePrompt(); _lastMouse = m; return; }
        if (_ow == null) { LeaveWorldMode(); return; }

        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) SaveAll();
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) OwDoUndo();
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) _mapTool = MapTool.Paint;
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) _mapTool = MapTool.Fill;
        if (Raylib.IsKeyPressed(KeyboardKey.Six)) _mapTool = MapTool.Pick;
        if (Raylib.IsKeyPressed(KeyboardKey.One)) _mapTool = MapTool.Select;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) _mapTool = MapTool.Add;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) _mapTool = MapTool.Link;
        if (Raylib.IsKeyPressed(KeyboardKey.G)) _owShowGuides = !_owShowGuides;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_owLinkFrom.Length > 0) _owLinkFrom = "";
            else LeaveWorldMode();
            return;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace)) && SelectedNode is { } sel) DeleteNode(sel);

        float pan = 600 * dt;
        if (Raylib.IsKeyDown(KeyboardKey.Left)) _owScrollX -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) _owScrollX += pan;
        if (Raylib.IsKeyDown(KeyboardKey.Up)) _owScrollY -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.Down)) _owScrollY += pan;

        float wheel = Raylib.GetMouseWheelMove();
        if (m.X < MapPalW) _owPalScroll = Math.Max(0, _owPalScroll - wheel * 40);
        else if (m.X >= AppWindow.VW - MapPanelW) _owPanelScroll = Math.Clamp(_owPanelScroll - wheel * 40, 0, Math.Max(0, _owPanelContent - (AppWindow.VH - TopH - BotH)));
        else if (wheel != 0) _owScrollY -= wheel * 40;

        bool panning = Raylib.IsMouseButtonDown(MouseButton.Middle) || (Raylib.IsKeyDown(KeyboardKey.Space) && Raylib.IsMouseButtonDown(MouseButton.Left));
        if (panning)
        {
            var d = m - _lastMouse;
            _owScrollX -= d.X; _owScrollY -= d.Y;
            _lastMouse = m;
            return;
        }

        bool press = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool down = Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!down) _owDrag = null;

        if ((_mapTool == MapTool.Paint || _mapTool == MapTool.Fill || _mapTool == MapTool.Pick) && InMapCanvas(m) && _owDrag == null)
        {
            HandleTilePaint(m, press, down);
            _lastMouse = m;
            return;
        }
        if (press && m.X < MapPalW && m.Y > TopH && m.Y < AppWindow.VH - BotH)
        {
            string? picked = OwPaletteHit(m);
            if (picked != null) { _owBrush = picked; if (_mapTool == MapTool.Select || _mapTool == MapTool.Add || _mapTool == MapTool.Link || _mapTool == MapTool.Pick) _mapTool = MapTool.Paint; }
            _lastMouse = m;
            return;
        }

        if (_owDrag != null)
        {
            // snap to 10 px, hold Ctrl for free movement
            double nx = m.X - MapPalW + _owScrollX + _owDragOff.X, ny = m.Y - TopH + _owScrollY + _owDragOff.Y;
            if (!Ctrl) { nx = Math.Round(nx / 10) * 10; ny = Math.Round(ny / 10) * 10; }
            _owDrag.X = Math.Max(0, nx); _owDrag.Y = Math.Max(0, ny);
            MarkDirty();
        }
        else if (press && InMapCanvas(m))
        {
            var hit = NodeAtScreen(m);
            switch (_mapTool)
            {
                case MapTool.Select:
                    if (hit != null)
                    {
                        _owSel = hit.Id;
                        _owDrag = hit;
                        var p = NodeScreen(hit);
                        _owDragOff = new Vector2(p.X - m.X, p.Y - m.Y);
                    }
                    else _owSel = "";
                    break;
                case MapTool.Add:
                    if (hit == null)
                    {
                        double x = Math.Round((m.X - MapPalW + _owScrollX) / 10) * 10, y = Math.Round((m.Y - TopH + _owScrollY) / 10) * 10;
                        var n = new MapNode { Id = NextNodeId(), Name = "Level", X = x, Y = y };
                        _ow.Nodes.Add(n);
                        if (_ow.Start.Length == 0) _ow.Start = n.Id;
                        _owSel = n.Id;
                        MarkDirty();
                        _mapTool = MapTool.Select;
                    }
                    break;
                case MapTool.Link:
                    if (hit != null)
                    {
                        if (_owLinkFrom.Length == 0) _owLinkFrom = hit.Id;
                        else
                        {
                            var a = _ow.Node(_owLinkFrom);
                            if (a != null) ToggleLink(a, hit);
                            _owLinkFrom = "";
                        }
                    }
                    else _owLinkFrom = "";
                    break;
            }
        }
        _lastMouse = m;
    }

    // ================================================================ tile palette (left)

    /// <summary>Palette layout: tiles in rows of three under category headers.</summary>
    private IEnumerable<(OverworldTileDef? tile, string header, float x, float y)> OwPaletteLayout()
    {
        float y = TopH + 8 - _owPalScroll;
        foreach (var grp in OverworldTileRegistry.All.GroupBy(t => t.Category))
        {
            yield return (null, grp.Key, 8, y);
            y += 22;
            int i = 0;
            foreach (var t in grp)
            {
                yield return (t, "", 10 + (i % 3) * 45, y + (i / 3) * 45);
                i++;
            }
            y += ((i + 2) / 3) * 45 + 8;
        }
    }

    private string? OwPaletteHit(Vector2 m)
    {
        foreach (var (t, _, x, y) in OwPaletteLayout())
            if (t != null && m.X >= x && m.X < x + 40 && m.Y >= y && m.Y < y + 40) return t.Id;
        return null;
    }

    private void DrawOwPaletteIcons()
    {
        Raylib.BeginScissorMode(0, TopH, MapPalW, AppWindow.VH - TopH - BotH);
        foreach (var (t, _, x, y) in OwPaletteLayout())
        {
            if (t == null || y + 40 < TopH || y > AppWindow.VH - BotH) continue;
            var spr = _scene.OverworldTileSprite(t, _scene.AnimTime);
            if (spr != null) _scene.DrawSpriteScreen(spr, x + 20, y + 20, 32f / spr.SrcW);
        }
        Raylib.EndScissorMode();
    }

    // ================================================================ draw

    private void DrawWorldScreen(Vector2 m)
    {
        int VW = AppWindow.VW, VH = AppWindow.VH;
        if (_ow == null) return;
        bool modal = _modal != Modal.None;
        var mm = modal ? new Vector2(-999, -999) : m;

        // --- raylib pass: sky + image layers, clipped to the canvas
        int cw = VW - MapPanelW - MapPalW, ch = VH - TopH - BotH;
        Raylib.BeginScissorMode(MapPalW, TopH, cw, ch);
        Raylib.DrawRectangleGradientV(MapPalW, TopH, cw, ch, new Color((byte)90, (byte)170, (byte)235, (byte)255), new Color((byte)200, (byte)235, (byte)255, (byte)255));
        foreach (var layer in _ow.Layers) _scene.DrawMapLayer(layer, _owScrollX, _owScrollY, MapPalW, TopH, cw, ch);
        _scene.DrawOverworldTiles(_ow, _owScrollX, _owScrollY, MapPalW, TopH, cw, ch, _scene.AnimTime);
        Raylib.EndScissorMode();

        // --- skia pass
        _ui.BeginFrame();
        _ui.Canvas.Save();
        _ui.Canvas.ClipRect(new SKRect(MapPalW, TopH, VW - MapPanelW, VH - BotH));
        if (_owShowGuides)
        {
            // map border and (free roam) blocked tiles
            float mx0 = MapPalW - _owScrollX, my0 = TopH - _owScrollY;
            _ui.Rect(mx0, my0, _ow.PixelWidth, _ow.PixelHeight, C(0, 0, 0, 0), 0, C(255, 80, 80, 200), 2);
            if (_ow.FreeRoam)
                for (int cy = 0; cy < _ow.Height; cy++)
                    for (int cx = 0; cx < _ow.Width; cx++)
                    {
                        string id = _ow.CellAt(cx, cy);
                        if (id.Length == 0 || (OverworldTileRegistry.Find(id)?.Walkable ?? true)) continue;
                        float sx = mx0 + cx * 32, sy = my0 + cy * 32;
                        if (sx + 32 < MapPalW || sx > VW - MapPanelW || sy + 32 < TopH || sy > VH - BotH) continue;
                        _ui.Rect(sx, sy, 32, 32, C(255, 0, 0, 60));
                    }
            float gx0 = MapPalW - _owScrollX, gy0 = TopH - _owScrollY;
            var gc = C(255, 255, 255, 140);
            _ui.Rect(gx0, gy0, 1280, 720, C(0, 0, 0, 0), 0, gc, 2);                    // 16:9 view
            _ui.Rect(gx0, gy0, 960, 720, C(0, 0, 0, 0), 0, C(255, 220, 120, 150), 1);  // 4:3 view
            _ui.Text("16:9 view", gx0 + 1274, gy0 + 16, 13, gc, false, 2);
            _ui.Text("4:3 view", gx0 + 954, gy0 + 32, 13, C(255, 220, 120, 200), false, 2);
        }

        foreach (var a in _ow.Nodes)
            foreach (var id in a.Links)
            {
                var b = _ow.Node(id);
                if (b == null || string.CompareOrdinal(a.Id, b.Id) > 0) continue;
                var pa = NodeScreen(a); var pb = NodeScreen(b);
                _ui.Line(pa.X, pa.Y, pb.X, pb.Y, C(60, 40, 20, 230), 10);
                _ui.Line(pa.X, pa.Y, pb.X, pb.Y, C(250, 225, 150, 255), 6);
            }
        foreach (var n in _ow.Nodes)
        {
            var p = NodeScreen(n);
            bool portal = n.Portal.Length > 0;
            var fill = portal ? C(170, 110, 255, 255) : n.Level.Length == 0 ? C(150, 150, 160, 255) : C(255, 190, 60, 255);
            bool sel = n.Id == _owSel;
            _ui.Circle(p.X, p.Y, sel ? 30 : 26, sel ? C(255, 255, 255, 255) : C(40, 30, 20, 255));
            _ui.Circle(p.X, p.Y, 21, fill);
            if (n.Id == _owLinkFrom) _ui.Circle(p.X, p.Y, 10, C(255, 80, 80, 255));
            if (n.Id == _ow.Start) _ui.Text("S", p.X, p.Y + 8, 22, SKColors.White, true, 1);
            _ui.Text(n.Name, p.X, p.Y + 50, 16, SKColors.White, true, 1, true);
            _ui.Text(n.Id, p.X, p.Y - 34, 12, C(255, 255, 255, 200), false, 1, true);
        }
        if ((_mapTool == MapTool.Paint || _mapTool == MapTool.Fill) && InMapCanvas(m) && !modal)
        {
            var (hx, hy) = OwCell(m);
            _ui.Rect(MapPalW - _owScrollX + hx * 32, TopH - _owScrollY + hy * 32, 32, 32, C(0, 0, 0, 0), 0, C(255, 255, 255, 230), 2);
        }
        _ui.Canvas.Restore();

        // --- bars + panel
        _ui.Rect(0, 0, VW, TopH, C(34, 36, 62, 255));
        _ui.Rect(0, VH - BotH, VW, BotH, C(34, 36, 62, 255));
        _ui.Rect(VW - MapPanelW, TopH, MapPanelW, VH - TopH - BotH, C(24, 26, 44, 255));
        _ui.Line(VW - MapPanelW, TopH, VW - MapPanelW, VH - BotH, C(255, 255, 255, 60));
        // tile palette background (icons are drawn by raylib in between the two Skia passes)
        _ui.Rect(0, TopH, MapPalW, VH - TopH - BotH, C(24, 26, 44, 255));
        _ui.Line(MapPalW, TopH, MapPalW, VH - BotH, C(255, 255, 255, 60));
        foreach (var (t, _, px, py) in OwPaletteLayout())
        {
            if (t == null || py + 40 < TopH || py > VH - BotH) continue;
            bool selT = t.Id == _owBrush;
            _ui.Rect(px - 2, py - 2, 44, 44, selT ? C(255, 190, 60, 255) : C(48, 52, 90, 255), 5);
        }
        _ui.EndFrame();
        DrawOwPaletteIcons();
        _ui.BeginFrame();
        _ui.Canvas.Save();
        _ui.Canvas.ClipRect(new SKRect(0, TopH, MapPalW, VH - BotH));
        foreach (var (t, hdr, px, py) in OwPaletteLayout())
            if (t == null && py + 18 > TopH && py < VH - BotH) _ui.Text(hdr, px, py + 15, 13, C(255, 210, 120, 255), true);
        _ui.Canvas.Restore();

        float bx = 8;
        void Top(string label, float w, Action act, bool sel = false)
        {
            if (Btn(label, bx, 6, w, 32, mm, sel, 16)) act();
            bx += w + 6;
        }
        Top("< Levels", 84, LeaveWorldMode);
        Top("<", 32, () => CycleWorld(-1));
        _ui.Text(_ow.Id, bx + 4, 28, 17, SKColors.White, true);
        bx += Math.Max(70, _ui.Measure(_ow.Id, 17, true) + 14);
        Top(">", 32, () => CycleWorld(1));
        Top("New", 48, () => { NewWorld(); });
        Top("Delete", 62, () =>
        {
            if (_owLib.Ids.Count() <= 1) { Say("Keep at least one map"); return; }
            string del = _ow.Id;
            _owLib.Remove(del);
            _ow = null;
            CycleWorld(1);
            Say($"Deleted map {del}");
        });
        Top("Save", 52, SaveAll);
        bx += 8;
        Top("Select", 62, () => _mapTool = MapTool.Select, _mapTool == MapTool.Select);
        Top("Add", 48, () => _mapTool = MapTool.Add, _mapTool == MapTool.Add);
        Top("Link", 52, () => _mapTool = MapTool.Link, _mapTool == MapTool.Link);
        bx += 4;
        Top("Paint", 54, () => _mapTool = MapTool.Paint, _mapTool == MapTool.Paint);
        Top("Fill", 44, () => _mapTool = MapTool.Fill, _mapTool == MapTool.Fill);
        Top("Pick", 46, () => _mapTool = MapTool.Pick, _mapTool == MapTool.Pick);
        Top("Guides", 64, () => _owShowGuides = !_owShowGuides, _owShowGuides);

        string hint = _mapTool switch
        {
            MapTool.Paint => $"Paint '{_owBrush}': left drag paints, right drag erases, 4/5/6 = paint/fill/pick, Ctrl+Z undo",
            MapTool.Fill => $"Fill '{_owBrush}': click to flood fill, right click clears an area",
            MapTool.Pick => "Pick: click a map tile to use it as the brush",
            MapTool.Add => "Add: click empty space to place a node",
            MapTool.Link => _owLinkFrom.Length > 0 ? $"Link: click the node to connect with '{_owLinkFrom}' (click again to unlink)" : "Link: click a node, then another",
            _ => "Select: click / drag nodes (Ctrl = free move)   Del removes   Space+drag or middle drag pans   1/2/3 tools   Esc back",
        };
        _ui.Text(_messageTime > 0 ? _message : hint, 10, VH - 9, 14, C(255, 255, 255, 220));

        DrawMapPanel(mm);
        if (_modal == Modal.Prompt)
        {
            _ui.Rect(0, 0, VW, VH, C(0, 0, 0, 150));
            var o = ModalOrigin;
            _ui.Rect(o.X + 200, o.Y + 270, 560, 170, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
        }
        DrawModals(m);
        _ui.EndFrame();
    }

    private void DrawMapPanel(Vector2 m)
    {
        if (_ow == null) return;
        float x = AppWindow.VW - MapPanelW + 12, w = MapPanelW - 24;
        float top = TopH, bottom = AppWindow.VH - BotH;
        float y = top + 8 - _owPanelScroll;
        _ui.Canvas.Save();
        _ui.Canvas.ClipRect(new SKRect(AppWindow.VW - MapPanelW, top, AppWindow.VW, bottom));

        void Head(string t)
        {
            if (y + 20 > top && y < bottom) _ui.Text(t, x, y + 16, 15, C(255, 210, 120, 255), true);
            y += 26;
        }
        void Row(string label, string value, Action act)
        {
            if (y + 30 > top && y < bottom && Btn(Shorten($"{label}: {value}", 38), x, y, w, 30, m, false, 14) && m.Y > top && m.Y < bottom) act();
            y += 34;
        }
        void Gap() { y += 8; }

        Head("Map");
        Row("Id", _ow.Id, () => TextPrompt("Map id (letters, digits, _)", _ow.Id, v =>
        {
            string id = LevelLibrary.Sanitize(v);
            if (id.Length == 0 || id == _ow.Id) return;
            if (_owLib.Get(id) != null) { Say($"Map '{id}' already exists"); return; }
            string old = _ow.Id;
            _owLib.Remove(old);
            _ow.Id = id;
            _owLib.Set(_ow);
            foreach (var oid in _owLib.Ids.ToList())            // portals that pointed at the old id
                foreach (var n in _owLib.Get(oid)!.Nodes.Where(n => n.Portal == old)) n.Portal = id;
            if (_lib.StartWorldId == old) { _lib.StartWorldId = id; _lib.SaveGameFile(); }
        }));
        Row("Name", _ow.Name, () => TextPrompt("Map name", _ow.Name, v => _ow.Name = v));
        Row("Mode", _ow.FreeRoam ? "free roam (walk around)" : "path (hop between nodes)", () => { _ow.Mode = _ow.FreeRoam ? "path" : "free"; MarkDirty(); });
        Row("Size (cells)", $"{_ow.Width} x {_ow.Height}", () => TextPrompt("Map size in cells: width,height (32 px each)", $"{_ow.Width},{_ow.Height}", v =>
        {
            var p = v.Split(',');
            if (p.Length == 2 && int.TryParse(p[0].Trim(), out int w) && int.TryParse(p[1].Trim(), out int h)) { OwPushUndo(); _ow.Resize(w, h); }
        }));
        Row("Clear all tiles", "", () => { OwPushUndo(); _ow.Cells = Array.Empty<string>(); _ow.EnsureGrid(); MarkDirty(); });
        Row("Song", _ow.Song.Length == 0 ? "(default)" : _ow.Song, () => TextPrompt("Song name (empty = default)", _ow.Song, v => _ow.Song = v));
        Row("Start world", _lib.StartWorldId == _ow.Id ? "yes" : "no — click to set", () =>
        {
            _lib.StartWorldId = _ow.Id;
            _lib.SaveGameFile();
            Say($"The game now starts on map {_ow.Id}");
        });
        Gap();

        var n = SelectedNode;
        Head(n == null ? "Node (select one)" : $"Node '{n.Id}'");
        if (n != null)
        {
            Row("Id", n.Id, () => TextPrompt("Node id", n.Id, v => RenameNode(n, v)));
            Row("Name", n.Name, () => TextPrompt("Name shown on the map", n.Name, v => n.Name = v));
            Row("Level", n.Level.Length == 0 ? "(none)" : n.Level, () =>
            {
                var ids = _lib.Ids.ToList();
                string next = ids.Count > 0 ? (ids.SkipWhile(i => i != n.Level).Skip(1).FirstOrDefault() ?? ids[0]) : "";
                TextPrompt($"Level id to play, empty for none (e.g. {string.Join(", ", ids.Take(4))})", n.Level.Length == 0 ? next : n.Level, v =>
                {
                    n.Level = v;
                    if (v.Length > 0 && !_lib.Has(v)) Say($"Warning: no level '{v}'");
                    if (v.Length > 0 && n.Name is "Level" or "") { var lv = _lib.Get(v); if (lv != null && lv.Name.Length > 0) n.Name = lv.Name; }
                });
            });
            Row("Portal to map", n.Portal.Length == 0 ? "(none)" : n.Portal, () =>
                TextPrompt($"Map id to travel to, empty for none (maps: {string.Join(", ", _owLib.Ids)})", n.Portal, v => n.Portal = v));
            if (n.Portal.Length > 0)
                Row("Arrive at node", n.PortalNode.Length == 0 ? "(map start)" : n.PortalNode, () =>
                    TextPrompt("Node id to arrive at in the other map, empty = its start", n.PortalNode, v => n.PortalNode = v));
            Row("Requires", n.Requires.Count == 0 ? "(always open)" : string.Join(",", n.Requires), () =>
                TextPrompt("Nodes that must be completed first (comma separated)", string.Join(",", n.Requires), v =>
                {
                    n.Requires = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    foreach (var r in n.Requires.Where(r => _ow.Node(r) == null)) Say($"Warning: no node '{r}' on this map");
                }));
            Row("Position", $"{n.X:0}, {n.Y:0}", () => TextPrompt("x,y in map pixels", $"{n.X:0},{n.Y:0}", v =>
            {
                var p = v.Split(',');
                if (p.Length == 2 && TryNum(p[0], out var px) && TryNum(p[1], out var py)) { n.X = px; n.Y = py; }
            }));
            Row("Start node", _ow.Start == n.Id ? "yes" : "no — click to set", () => { _ow.Start = n.Id; MarkDirty(); });
            Row("Delete node", "", () => DeleteNode(n));
        }
        Gap();

        Head("Image layers");
        for (int i = 0; i < _ow.Layers.Count; i++)
        {
            int li = i;
            var l = _ow.Layers[i];
            if (y + 30 > top && y < bottom && Btn(Shorten($"{i + 1}. {l.Image}", 38), x, y, w, 28, m, li == _owLayerSel, 14) && m.Y > top && m.Y < bottom)
                _owLayerSel = li == _owLayerSel ? -1 : li;
            y += 32;
        }
        if (y + 30 > top && y < bottom && Btn("+ Add image layer", x, y, w, 30, m, false, 14) && m.Y > top && m.Y < bottom)
            TextPrompt("Image path in assets, e.g. custom/hills.png", "custom/hills.png", v =>
            {
                if (v.Length == 0) return;
                _ow.Layers.Add(new MapLayer { Image = v, X = 640, Y = 360, Scale = 1, Parallax = 1 });
                _owLayerSel = _ow.Layers.Count - 1;
            });
        y += 38;
        if (_owLayerSel >= 0 && _owLayerSel < _ow.Layers.Count)
        {
            var l = _ow.Layers[_owLayerSel];
            Head($"Layer {_owLayerSel + 1}");
            ImageLayerRows(l, true, Row, MarkDirty);
            Row("Move up (draw later)", "", () =>
            {
                int i = _owLayerSel;
                if (i < _ow.Layers.Count - 1) { (_ow.Layers[i], _ow.Layers[i + 1]) = (_ow.Layers[i + 1], _ow.Layers[i]); _owLayerSel++; MarkDirty(); }
            });
            Row("Move down (draw earlier)", "", () =>
            {
                int i = _owLayerSel;
                if (i > 0) { (_ow.Layers[i], _ow.Layers[i - 1]) = (_ow.Layers[i - 1], _ow.Layers[i]); _owLayerSel--; MarkDirty(); }
            });
            Row("Delete layer", "", () => { _ow.Layers.RemoveAt(_owLayerSel); _owLayerSel = -1; MarkDirty(); });
        }
        _owPanelContent = y + _owPanelScroll - top + 20;
        _ui.Canvas.Restore();
    }

    /// <summary>
    /// The property rows of an image layer. Shared by the overworld map editor and the level layers window, because
    /// both kinds of layer use the same <see cref="ImageLayerProps"/>. <paramref name="before"/> runs before each edit
    /// is applied (undo snapshot / dirty flag).
    /// </summary>
    private void ImageLayerRows(ImageLayerProps l, bool image, Action<string, string, Action> row, Action before)
    {
        void Edit(string title, string initial, Action<string> set)
            => OpenPrompt(title, initial, false, v => { before(); set((v ?? "").Trim()); }, () => { });
        string N(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        if (image)
        {
            row("Image", l.Image, () => Edit("Image path under assets/ (png or svg)", l.Image, v => { if (v.Length > 0) l.Image = v; }));
            row("Centre X", N(l.X), () => Edit("Centre x of the image", N(l.X), v => { if (TryNum(v, out var d)) l.X = d; }));
            row("Centre Y", N(l.Y), () => Edit("Centre y of the image", N(l.Y), v => { if (TryNum(v, out var d)) l.Y = d; }));
            row("Scale", N(l.Scale), () => Edit("Scale (units per image pixel)", N(l.Scale), v => { if (TryNum(v, out var d) && d > 0) l.Scale = d; }));
        }
        row("Parallax", N(l.Parallax), () => Edit("Parallax (1 = moves with the world, 0 = fixed, 0.5 = half speed)", N(l.Parallax), v => { if (TryNum(v, out var d)) l.Parallax = d; }));
        if (image)
        {
            string rep = (l.RepeatX ? "x" : "") + (l.RepeatY ? "y" : "");
            row("Repeat", rep.Length == 0 ? "none" : rep, () => Edit("Repeat: x, y, xy or none", rep, v => { l.RepeatX = v.Contains('x'); l.RepeatY = v.Contains('y'); }));
            row("Fill screen", l.FitScreen ? "yes" : "no", () => Edit("Stretch over the whole screen? yes / no", l.FitScreen ? "yes" : "no", v => l.FitScreen = v.StartsWith("y", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}
