using System.Numerics;
using MazeGame.Core;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Editor;

// Move / edit entities: drag to move, click to open the entity modal (props, id, script).
public sealed partial class EditorApp
{
    private EntityDef? _dragEnt;
    private bool _dragMoved;
    private int _entIdx = -1;

    private void HandleMove(Vector2 m, int cx, int cy, bool pressed, bool down, bool released)
    {
        if (pressed) { _dragEnt = EntityAt(cx, cy); _dragMoved = false; }
        if (_dragEnt != null && down && _level.InBounds(cx, cy) && (cx != _dragEnt.X || cy != _dragEnt.Y))
        {
            var other = EntityAt(cx, cy);
            if (other == null)
            {
                if (!_dragMoved) { PushUndo(); _dragMoved = true; }
                _dragEnt.X = cx; _dragEnt.Y = cy;
                _unsaved = true;
            }
        }
        if (released && _dragEnt != null)
        {
            if (!_dragMoved) OpenEntityModal(_dragEnt);
            _dragEnt = null;
        }
    }

    private void OpenEntityModal(EntityDef d)
    {
        _entIdx = _level.Entities.IndexOf(d);
        if (_entIdx >= 0) _modal = Modal.Entity;
    }

    private void EntityPrompt(EntityDef d, string prop, string title)
    {
        OpenPrompt(title, d.Get(prop), false,
            v => { PushUndo(); if (v.Trim().Length == 0) d.Props.Remove(prop); else d.Props[prop] = v.Trim(); _modal = Modal.Entity; },
            () => _modal = Modal.Entity);
    }

    private void DrawEntityModal(Vector2 m)
    {
        if (_entIdx < 0 || _entIdx >= _level.Entities.Count) { _modal = Modal.None; return; }
        var d = _level.Entities[_entIdx];
        var type = EntityRegistry.Find(d.Type);
        float x = 230, y = 120;
        _ui.Text(type?.Name ?? d.Type, x, y + 18, 28, SKColors.White, true);
        _ui.Text($"cell {d.X}, {d.Y}   (drag with the Move tool to reposition)", x, y + 40, 14, C(255, 255, 255, 170));
        y += 56;

        void Row(string label, string prop, string prompt)
        {
            string v = d.Get(prop);
            _ui.Text(label, x, y + 27, 18, SKColors.White);
            if (Btn(v.Length == 0 ? "(none)" : Shorten(v, 28), x + 150, y, 330, 36, m, false, 16)) EntityPrompt(d, prop, prompt);
            y += 44;
        }

        if (type != null)
            foreach (var p in type.Props)
                if (p.Name != "id" && p.Name != "script") Row(p.Name, p.Name, p.Prompt);
        Row("id", "id", "Name for this entity (scripts address it with entity_x(\"name\") etc.)");
        Row("script", "script", "Script id: file assets/scripts/<id>.mgs (empty = none)");

        _ui.Text("Each entity with a script runs its own copy: events start, tick, touch, remove, trigger.", x, y + 14, 13, C(255, 255, 255, 170));
        if (Btn("Edit script", x, 560, 150, 44, m))
        {
            string id = d.Get("script");
            if (id.Length == 0)
                OpenPrompt("New script id", (d.Get("id").Length > 0 ? d.Get("id") : d.Type) + "_script", false,
                    v => { v = SafeId(v); PushUndo(); d.Props["script"] = v; _modal = Modal.None; EnterScriptMode(v); }, () => _modal = Modal.Entity);
            else { _modal = Modal.None; EnterScriptMode(id); }
        }
        if (Btn("Delete", x + 170, 560, 120, 44, m)) { PushUndo(); _level.Entities.Remove(d); _modal = Modal.None; }
        if (Btn("Close", x + 310, 560, 120, 44, m)) _modal = Modal.None;
    }

    private static string SafeId(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.Trim()) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.Length == 0 ? "script" : sb.ToString();
    }
}
