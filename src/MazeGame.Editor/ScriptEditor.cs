using System.Numerics;
using System.Text.RegularExpressions;
using MazeGame.Core;
using MazeGame.Core.Scripting;
using MazeGame.Runtime;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Editor;

// Full-screen script editor: file list, text area with syntax colours, compile check, save, attach to level.
public sealed partial class EditorApp
{
    private string _assetsDir = "";
    private bool _scriptMode;
    private readonly List<string> _scFiles = new();
    private string _scId = "";
    private List<string> _scLines = new() { "" };
    private int _scRow, _scCol, _scTop;
    private bool _scDirty;
    private string _scStatus = "";
    private bool _scStatusOk = true;
    private int _scErrLine = -1;
    private float _scFileScroll;

    private const int ScListW = 200, ScLineH = 20, ScFont = 16, ScGutter = 48;
    private static readonly HashSet<string> ScKeywords = new() { "var", "func", "on", "if", "else", "while", "break", "continue", "return", "true", "false", "nil" };

    private string ScriptDir => Path.Combine(_assetsDir, "scripts");
    private string ScriptPath(string id) => Path.Combine(ScriptDir, id + ".mgs");

    private void EnterScriptMode(string? id = null)
    {
        CommitToLibrary();
        _scriptMode = true;
        RefreshScriptFiles();
        if (!string.IsNullOrEmpty(id))
        {
            if (!File.Exists(ScriptPath(id))) CreateScript(id);
            OpenScript(id);
        }
        else if (_scId.Length == 0 && _scFiles.Count > 0) OpenScript(_scFiles[0]);
    }

    private void LeaveScriptMode()
    {
        if (_scDirty) SaveScript();
        _scriptMode = false;
        ScriptLibrary.ClearCache();
    }

    private void RefreshScriptFiles()
    {
        _scFiles.Clear();
        if (Directory.Exists(ScriptDir))
            foreach (var f in Directory.GetFiles(ScriptDir, "*.mgs")) _scFiles.Add(Path.GetFileNameWithoutExtension(f));
        _scFiles.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private void CreateScript(string id)
    {
        Directory.CreateDirectory(ScriptDir);
        File.WriteAllText(ScriptPath(id),
            "// " + id + "\n// Events: start, tick, touch, remove, trigger \"id\", enter, exit, use, death, win, gem\n" +
            "// self_x() self_y() set_self_pos(x, y) camera_pan(x, y, seconds) ... see README for every function.\n\n" +
            "on start {\n}\n\non tick {\n}\n");
        RefreshScriptFiles();
    }

    private void OpenScript(string id)
    {
        if (_scDirty && _scId.Length > 0) SaveScript();
        _scId = id;
        string text = File.Exists(ScriptPath(id)) ? File.ReadAllText(ScriptPath(id)) : "";
        _scLines = text.Replace("\r", "").Split('\n').ToList();
        if (_scLines.Count == 0) _scLines.Add("");
        _scRow = _scCol = _scTop = 0;
        _scDirty = false;
        _scErrLine = -1;
        _scStatus = "";
    }

    private string ScText => string.Join("\n", _scLines);

    private void SaveScript()
    {
        if (_scId.Length == 0) return;
        Directory.CreateDirectory(ScriptDir);
        File.WriteAllText(ScriptPath(_scId), ScText);
        _scDirty = false;
        ScriptLibrary.ClearCache();
        CheckScript(true);
    }

    private void CheckScript(bool afterSave = false)
    {
        _scErrLine = -1;
        try
        {
            ScriptCompiler.Compile(ScText, _scId);
            _scStatus = afterSave ? "Saved. No errors." : "No errors.";
            _scStatusOk = true;
        }
        catch (Exception ex)
        {
            _scStatus = (afterSave ? "Saved, but: " : "") + ex.Message;
            _scStatusOk = false;
            var mt = Regex.Match(ex.Message, @"line (\d+)");
            if (mt.Success && int.TryParse(mt.Groups[1].Value, out int ln)) _scErrLine = ln - 1;
        }
    }

    private int ScVisibleLines => (AppWindow.VH - TopH - BotH - 40 - 8) / ScLineH;
    private float ScTextX => ScListW + ScGutter + 10;

    private void Mark() { _scDirty = true; }

    private void ClampCursor()
    {
        _scRow = Math.Clamp(_scRow, 0, _scLines.Count - 1);
        _scCol = Math.Clamp(_scCol, 0, _scLines[_scRow].Length);
        if (_scRow < _scTop) _scTop = _scRow;
        if (_scRow >= _scTop + ScVisibleLines) _scTop = _scRow - ScVisibleLines + 1;
        _scTop = Math.Clamp(_scTop, 0, Math.Max(0, _scLines.Count - 1));
    }

    private void InsertText(string s)
    {
        foreach (char c in s.Replace("\r", ""))
        {
            var line = _scLines[_scRow];
            if (c == '\n')
            {
                string rest = line.Substring(_scCol);
                string indent = new string(' ', line.TakeWhile(ch => ch == ' ').Count());
                if (line.Substring(0, _scCol).TrimEnd().EndsWith("{")) indent += "  ";
                _scLines[_scRow] = line.Substring(0, _scCol);
                _scLines.Insert(_scRow + 1, indent + rest);
                _scRow++; _scCol = indent.Length;
            }
            else if (c == '\t') { _scLines[_scRow] = line.Insert(_scCol, "  "); _scCol += 2; }
            else if (c >= ' ') { _scLines[_scRow] = line.Insert(_scCol, c.ToString()); _scCol++; }
        }
        Mark();
    }

    // Raylib-cs 7: GetClipboardText_ returns the clipboard as a string (adjust here if your binding differs)
    private static string ClipText() { try { return Raylib.GetClipboardText_() ?? ""; } catch { return ""; } }

    private static bool KeyRep(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);

    private void UpdateScript(float dt)
    {
        var m = _win.Mouse;
        if (_modal == Modal.Prompt) { UpdatePrompt(); _lastMouse = m; return; }

        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) { SaveScript(); Say("Script saved"); }
        if (Raylib.IsKeyPressed(KeyboardKey.F5) || (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Enter))) CheckScript();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { LeaveScriptMode(); return; }

        if (_scId.Length > 0)
        {
            int ch;
            while ((ch = Raylib.GetCharPressed()) > 0) if (!Ctrl) InsertText(char.ConvertFromUtf32(ch));
            if (KeyRep(KeyboardKey.Enter)) { if (!Ctrl) InsertText("\n"); }
            if (KeyRep(KeyboardKey.Tab)) InsertText("\t");
            if (KeyRep(KeyboardKey.Backspace))
            {
                if (_scCol > 0) { _scLines[_scRow] = _scLines[_scRow].Remove(_scCol - 1, 1); _scCol--; Mark(); }
                else if (_scRow > 0)
                {
                    int len = _scLines[_scRow - 1].Length;
                    _scLines[_scRow - 1] += _scLines[_scRow];
                    _scLines.RemoveAt(_scRow); _scRow--; _scCol = len; Mark();
                }
            }
            if (KeyRep(KeyboardKey.Delete))
            {
                if (_scCol < _scLines[_scRow].Length) { _scLines[_scRow] = _scLines[_scRow].Remove(_scCol, 1); Mark(); }
                else if (_scRow < _scLines.Count - 1) { _scLines[_scRow] += _scLines[_scRow + 1]; _scLines.RemoveAt(_scRow + 1); Mark(); }
            }
            if (KeyRep(KeyboardKey.Left)) { if (_scCol > 0) _scCol--; else if (_scRow > 0) { _scRow--; _scCol = _scLines[_scRow].Length; } }
            if (KeyRep(KeyboardKey.Right)) { if (_scCol < _scLines[_scRow].Length) _scCol++; else if (_scRow < _scLines.Count - 1) { _scRow++; _scCol = 0; } }
            if (KeyRep(KeyboardKey.Up)) _scRow--;
            if (KeyRep(KeyboardKey.Down)) _scRow++;
            if (KeyRep(KeyboardKey.PageUp)) _scRow -= ScVisibleLines;
            if (KeyRep(KeyboardKey.PageDown)) _scRow += ScVisibleLines;
            if (Raylib.IsKeyPressed(KeyboardKey.Home)) _scCol = 0;
            if (Raylib.IsKeyPressed(KeyboardKey.End)) _scCol = _scLines[Math.Clamp(_scRow, 0, _scLines.Count - 1)].Length;
            if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.V)) InsertText(ClipText());
            if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.C)) Raylib.SetClipboardText(_scLines[_scRow] + "\n");
            if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.X))
            {
                Raylib.SetClipboardText(_scLines[_scRow] + "\n");
                _scLines.RemoveAt(_scRow);
                if (_scLines.Count == 0) _scLines.Add("");
                _scRow = Math.Min(_scRow, _scLines.Count - 1); Mark();
            }
            if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.D)) { _scLines.Insert(_scRow + 1, _scLines[_scRow]); _scRow++; Mark(); }
            ClampCursor();

            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && m.X > ScListW) _scTop = Math.Clamp(_scTop - (int)Math.Round(wheel * 3), 0, Math.Max(0, _scLines.Count - 1));
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && m.X > ScListW + ScGutter && m.Y > TopH + 40 && m.Y < AppWindow.VH - BotH)
            {
                _scRow = Math.Clamp(_scTop + (int)((m.Y - TopH - 40) / ScLineH), 0, _scLines.Count - 1);
                string line = _scLines[_scRow];
                int col = 0;
                while (col < line.Length && ScTextX + _ui.Measure(line.Substring(0, col + 1), ScFont) - 4 < m.X) col++;
                _scCol = col;
            }
        }
        _lastMouse = m;
    }

    private void DrawScriptScreen(Vector2 m)
    {
        var mm = _modal == Modal.None ? m : new Vector2(-999, -999);
        _ui.BeginFrame();
        _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, C(20, 22, 38));
        _ui.Rect(0, 0, AppWindow.VW, TopH, C(34, 36, 62));
        _ui.Rect(0, AppWindow.VH - BotH, AppWindow.VW, BotH, C(34, 36, 62));
        _ui.Rect(0, TopH, ScListW, AppWindow.VH - TopH - BotH, C(24, 26, 44));

        float bx = 8;
        void Top(string label, float w, Action act, bool sel = false)
        {
            if (Btn(label, bx, 6, w, 32, mm, sel, 16)) act();
            bx += w + 6;
        }
        Top("< Back", 70, LeaveScriptMode);
        Top("New", 54, () => OpenPrompt("New script id", "my_script", false, v =>
        {
            v = SafeId(v);
            if (!File.Exists(ScriptPath(v))) CreateScript(v);
            OpenScript(v);
        }, () => { }));
        Top("Save", 56, () => { SaveScript(); Say("Script saved"); });
        Top("Check", 62, () => CheckScript());
        Top("Delete", 68, () =>
        {
            if (_scId.Length == 0) return;
            OpenPrompt("Type the script id to delete it: " + _scId, "", false, v =>
            {
                if (v.Trim() == _scId) { try { File.Delete(ScriptPath(_scId)); } catch { } _scId = ""; _scLines = new() { "" }; _scDirty = false; RefreshScriptFiles(); ScriptLibrary.ClearCache(); }
            }, () => { });
        });
        bx += 10;
        Top("Level script", 110, () => { if (_scId.Length > 0) { PushUndo(); _level.Script = _scId; Say("Level script = " + _scId); } }, _scId.Length > 0 && _level.Script == _scId);
        Top("Player script", 120, () => { if (_scId.Length > 0) { PushUndo(); _level.PlayerScript = _scId; Say("Player script of this level = " + _scId); } }, _scId.Length > 0 && _level.PlayerScript == _scId);
        Top("Detach", 70, () => { PushUndo(); if (_level.Script == _scId) _level.Script = ""; if (_level.PlayerScript == _scId) _level.PlayerScript = ""; });

        // file list
        float y = TopH + 8;
        _ui.Text("Scripts", 12, y + 14, 15, C(255, 210, 120), true);
        y += 24;
        foreach (var f in _scFiles)
        {
            bool sel = f == _scId;
            string tag = (f == _level.Script ? " [level]" : "") + (f == _level.PlayerScript ? " [player]" : "");
            if (Btn(Shorten(f + tag, 22), 8, y, ScListW - 16, 28, mm, sel, 14)) OpenScript(f);
            y += 32;
        }

        // editor area
        float top = TopH + 4;
        _ui.Text(_scId.Length == 0 ? "no script open" : _scId + ".mgs" + (_scDirty ? " *" : ""), ScListW + 12, top + 22, 16, SKColors.White, true);
        float areaY = TopH + 40;
        _ui.Rect(ScListW, areaY, AppWindow.VW - ScListW, AppWindow.VH - BotH - areaY, C(14, 15, 28));
        _ui.Rect(ScListW, areaY, ScGutter, AppWindow.VH - BotH - areaY, C(22, 24, 42));

        int vis = ScVisibleLines;
        for (int i = 0; i < vis && _scTop + i < _scLines.Count; i++)
        {
            int ln = _scTop + i;
            float ly = areaY + i * ScLineH;
            if (ln == _scErrLine) _ui.Rect(ScListW + ScGutter, ly, AppWindow.VW - ScListW - ScGutter, ScLineH, C(120, 30, 30, 160));
            else if (ln == _scRow) _ui.Rect(ScListW + ScGutter, ly, AppWindow.VW - ScListW - ScGutter, ScLineH, C(255, 255, 255, 14));
            _ui.Text((ln + 1).ToString(), ScListW + ScGutter - 8, ly + 15, 13, C(255, 255, 255, 110), false, 2);
            DrawHighlighted(_scLines[ln], ScTextX, ly + 15);
        }
        if (_scId.Length > 0 && _scRow >= _scTop && _scRow < _scTop + vis && (int)(Raylib.GetTime() * 2) % 2 == 0)
        {
            float cx = ScTextX + _ui.Measure(_scLines[_scRow].Substring(0, Math.Min(_scCol, _scLines[_scRow].Length)), ScFont);
            float cy = areaY + (_scRow - _scTop) * ScLineH;
            _ui.Rect(cx, cy + 2, 2, ScLineH - 4, SKColors.White);
        }

        _ui.Text(_scStatus, 12, AppWindow.VH - 9, 14, _scStatusOk ? C(140, 255, 160) : C(255, 140, 140), true);
        if (_messageTime > 0) _ui.Text(_message, AppWindow.VW - 10, AppWindow.VH - 9, 14, C(255, 230, 120), true, 2);
        else _ui.Text("Ctrl+S save   F5 check   Esc back   Ctrl+C/X copy/cut line   Ctrl+V paste", AppWindow.VW - 10, AppWindow.VH - 9, 13, C(255, 255, 255, 150), false, 2);

        if (_modal == Modal.Prompt)
        {
            _ui.Rect(0, 0, AppWindow.VW, AppWindow.VH, C(0, 0, 0, 150));
            var o = ModalOrigin;
            _ui.Canvas.Save();
            _ui.Canvas.Translate(o.X, o.Y);
            _ui.Rect(200, 270, 560, 170, C(24, 26, 50, 250), 14, C(255, 255, 255, 120), 2);
            DrawPromptModal();
            _ui.Canvas.Restore();
        }
        _ui.EndFrame();
    }

    private void DrawHighlighted(string line, float x, float y)
    {
        int i = 0;
        while (i < line.Length)
        {
            int s = i;
            SKColor col;
            char c = line[i];
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') { i = line.Length; col = C(110, 190, 120); }
            else if (c == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"') { if (line[i] == '\\') i++; i++; }
                i = Math.Min(line.Length, i + 1);
                col = C(240, 190, 110);
            }
            else if (char.IsDigit(c)) { while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) i++; col = C(150, 200, 255); }
            else if (char.IsLetter(c) || c == '_')
            {
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                string w = line.Substring(s, i - s);
                col = ScKeywords.Contains(w) ? C(255, 130, 170)
                    : ScriptNatives.Find(w) != null ? C(120, 210, 255)
                    : C(235, 235, 245);
            }
            else { i++; col = C(200, 200, 215); }
            string seg = line.Substring(s, i - s);
            float px = x + (s == 0 ? 0 : _ui.Measure(line.Substring(0, s), ScFont));
            _ui.Text(seg, px, y, ScFont, col);
        }
    }
}
