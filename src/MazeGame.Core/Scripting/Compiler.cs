namespace MazeGame.Core.Scripting;

/// <summary>
/// Compiles the script language to <see cref="Chunk"/> bytecode.
/// <code>
/// var needed = 3;                          // global variable
/// func open_gate() { set_tile(10, 4, "air"); play_sound("pop"); }
/// on start { if (!flag("intro")) { say("Welcome!"); set_flag("intro", true); } }
/// on enter "gate_zone" { if (gems() >= needed) { open_gate(); } else { say("Need " + needed + " gems"); } }
/// </code>
/// Events: start, tick, trigger "id", enter "id", exit "id", use "id", death, win, gem, touch (player overlaps this entity), remove.
/// </summary>
public sealed class ScriptCompiler
{
    private static readonly HashSet<string> Events = new() { "start", "tick", "trigger", "enter", "exit", "use", "death", "win", "gem", "touch", "remove" };
    private static readonly HashSet<string> Keywords = new()
        { "var", "func", "on", "if", "else", "while", "return", "break", "continue", "true", "false", "nil" };

    private sealed class Local { public string Name = ""; public int Depth; public bool Active = true; }
    private sealed class Loop { public readonly List<int> Breaks = new(); public readonly List<int> Continues = new(); public int Start; public int ScopeDepth; }

    private sealed class FuncBuilder
    {
        public string Name = "";
        public int Arity;
        public int FuncIndex;
        public readonly List<byte> Code = new();
        public readonly List<Local> Locals = new();
        public readonly List<Loop> Loops = new();
        public int Depth;
        public bool IsInit;
    }

    private readonly List<Token> _t;
    private int _p;
    private readonly string _name;
    private readonly Chunk _chunk = new();
    private readonly List<byte> _code = new();
    private readonly List<Value> _consts = new();
    private readonly Dictionary<string, int> _constIndex = new();
    private readonly Dictionary<string, int> _funcIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _globals = new(StringComparer.Ordinal);
    private readonly List<(string name, int argc, int line)> _pendingCalls = new();
    private FuncBuilder _f = null!;

    private ScriptCompiler(string source, string name)
    {
        _name = name;
        _t = Lexer.Tokenize(source);
    }

    /// <summary>Compiles script source. Throws <see cref="ScriptException"/> with a line number on errors.</summary>
    public static Chunk Compile(string source, string name = "script")
    {
        try { return new ScriptCompiler(source, name).Run(); }
        catch (ScriptException ex) { throw new ScriptException($"{name}: {ex.Message}"); }
    }

    // ================================================================ driver

    private Chunk Run()
    {
        _chunk.Name = _name;
        // pre-scan: top-level "var name" declarations are globals, so functions may use them before the declaration
        int depth = 0;
        for (int i = 0; i < _t.Count; i++)
        {
            var tk = _t[i];
            if (tk.Kind == Tok.LBrace) depth++;
            else if (tk.Kind == Tok.RBrace) depth--;
            else if (depth == 0 && tk.Kind == Tok.Ident && tk.Text == "var" && i + 1 < _t.Count && _t[i + 1].Kind == Tok.Ident)
                _globals.Add(_t[i + 1].Text);
        }

        var init = new FuncBuilder { Name = "__init", IsInit = true, FuncIndex = FuncSlot("__init") };
        _chunk.InitFunction = init.FuncIndex;

        while (Peek.Kind != Tok.Eof)
        {
            if (IsWord("var"))
            {
                _f = init;
                Next();
                string name = ExpectIdent("variable name");
                if (Match(Tok.Assign)) Expression(); else Emit(OpCode.Nil);
                Expect(Tok.Semi, "';'");
                Emit(OpCode.SetGlobal); EmitU16(Const(name));
            }
            else if (IsWord("func")) FunctionDecl();
            else if (IsWord("on")) HandlerDecl();
            else throw Err($"expected 'var', 'func' or 'on' but found '{Peek.Text}'");
        }

        _f = init;
        Emit(OpCode.Nil); Emit(OpCode.Return);
        Finish(init, locals: init.Locals.Count);

        foreach (var (name, argc, line) in _pendingCalls)
        {
            var info = _chunk.Functions[_funcIndex[name]];
            if (info.Entry < 0) throw new ScriptException($"call to undefined function '{name}'", line);
            if (info.Arity != argc) throw new ScriptException($"function '{name}' takes {info.Arity} arguments, not {argc}", line);
        }
        _chunk.Code = _code.ToArray();
        _chunk.Constants = _consts.ToArray();
        return _chunk;
    }

    private int FuncSlot(string name)
    {
        if (_funcIndex.TryGetValue(name, out int i)) return i;
        i = _chunk.Functions.Count;
        _chunk.Functions.Add(new FunctionInfo { Name = name });
        _funcIndex[name] = i;
        return i;
    }

    private void Finish(FuncBuilder f, int locals)
    {
        var info = _chunk.Functions[f.FuncIndex];
        info.Arity = f.Arity;
        info.Locals = Math.Max(locals, f.Arity);
        info.Entry = _code.Count;
        _code.AddRange(f.Code);
    }

    // ================================================================ declarations

    private void FunctionDecl()
    {
        Next();   // func
        int line = Peek.Line;
        string name = ExpectIdent("function name");
        if (ScriptNatives.Find(name) != null) throw Err($"'{name}' is a built-in function");
        int slot = FuncSlot(name);
        if (_chunk.Functions[slot].Entry >= 0) throw Err($"function '{name}' is defined twice");

        var f = new FuncBuilder { Name = name, FuncIndex = slot };
        _f = f;
        Expect(Tok.LParen, "'('");
        if (Peek.Kind != Tok.RParen)
        {
            do { AddLocal(ExpectIdent("parameter name")); f.Arity++; } while (Match(Tok.Comma));
        }
        Expect(Tok.RParen, "')'");
        Block();
        Emit(OpCode.Nil); Emit(OpCode.Return);
        Finish(f, f.Locals.Count);
        _ = line;
    }

    private void HandlerDecl()
    {
        Next();   // on
        string ev = ExpectIdent("event name");
        if (!Events.Contains(ev)) throw Err($"unknown event '{ev}' (use {string.Join(", ", Events)})");
        string? target = null;
        if (Peek.Kind == Tok.String) target = Next().Text;

        string tgt = target != null ? " \"" + target + "\"" : "";
        string fname = "on " + ev + tgt + " #" + _chunk.Handlers.Count;
        int slot = FuncSlot(fname);
        var f = new FuncBuilder { Name = fname, FuncIndex = slot };
        _f = f;
        Block();
        Emit(OpCode.Nil); Emit(OpCode.Return);
        Finish(f, f.Locals.Count);
        _chunk.Handlers.Add(new HandlerInfo { Event = ev, Target = target, Function = slot });
    }

    // ================================================================ statements

    private void Block()
    {
        Expect(Tok.LBrace, "'{'");
        BeginScope();
        while (Peek.Kind != Tok.RBrace)
        {
            if (Peek.Kind == Tok.Eof) throw Err("missing '}'");
            Statement();
        }
        Next();
        EndScope();
    }

    private void BeginScope() => _f.Depth++;

    private void EndScope()
    {
        foreach (var l in _f.Locals)
            if (l.Active && l.Depth >= _f.Depth) l.Active = false;
        _f.Depth--;
    }

    private void Statement()
    {
        var t = Peek;
        if (t.Kind == Tok.LBrace) { Block(); return; }
        if (t.Kind == Tok.Semi) { Next(); return; }
        if (t.Kind == Tok.Ident)
        {
            switch (t.Text)
            {
                case "var": VarStatement(); return;
                case "if": IfStatement(); return;
                case "while": WhileStatement(); return;
                case "return":
                    Next();
                    if (Peek.Kind == Tok.Semi) Emit(OpCode.Nil); else Expression();
                    Expect(Tok.Semi, "';'");
                    Emit(OpCode.Return);
                    return;
                case "break":
                {
                    Next(); Expect(Tok.Semi, "';'");
                    if (_f.Loops.Count == 0) throw Err("'break' outside a loop");
                    _f.Loops[^1].Breaks.Add(EmitJump(OpCode.Jump));
                    return;
                }
                case "continue":
                {
                    Next(); Expect(Tok.Semi, "';'");
                    if (_f.Loops.Count == 0) throw Err("'continue' outside a loop");
                    _f.Loops[^1].Continues.Add(EmitJump(OpCode.Jump));
                    return;
                }
            }
            // assignment?
            var n = _t[_p + 1].Kind;
            if (!Keywords.Contains(t.Text) && (n == Tok.Assign || n == Tok.PlusEq || n == Tok.MinusEq))
            {
                Next(); Next();
                Assignment(t.Text, n, t.Line);
                return;
            }
        }
        Expression();
        Expect(Tok.Semi, "';'");
        Emit(OpCode.Pop);
    }

    private void VarStatement()
    {
        Next();
        string name = ExpectIdent("variable name");
        if (_f.IsInit && _f.Depth == 0) throw Err("internal: top-level var");
        // the initialiser is compiled before the name exists, so "var x = x" refers to an outer x
        if (Match(Tok.Assign)) Expression(); else Emit(OpCode.Nil);
        Expect(Tok.Semi, "';'");
        int slot = AddLocal(name);
        Emit(OpCode.SetLocal); Emit(slot);
    }

    private void Assignment(string name, Tok op, int line)
    {
        if (op != Tok.Assign) GetVariable(name, line);
        Expression();
        if (op == Tok.PlusEq) Emit(OpCode.Add);
        else if (op == Tok.MinusEq) Emit(OpCode.Sub);
        Expect(Tok.Semi, "';'");
        SetVariable(name, line);
    }

    private void IfStatement()
    {
        Next();
        Expect(Tok.LParen, "'('");
        Expression();
        Expect(Tok.RParen, "')'");
        int elseJump = EmitJump(OpCode.JumpIfFalse);
        Block();
        if (IsWord("else"))
        {
            int endJump = EmitJump(OpCode.Jump);
            Patch(elseJump);
            Next();
            if (IsWord("if")) IfStatement(); else Block();
            Patch(endJump);
        }
        else Patch(elseJump);
    }

    private void WhileStatement()
    {
        Next();
        var loop = new Loop { Start = _f.Code.Count, ScopeDepth = _f.Depth };
        _f.Loops.Add(loop);
        Expect(Tok.LParen, "'('");
        Expression();
        Expect(Tok.RParen, "')'");
        int exit = EmitJump(OpCode.JumpIfFalse);
        Block();
        foreach (int c in loop.Continues) PatchTo(c, loop.Start);
        EmitLoop(loop.Start);
        Patch(exit);
        foreach (int b in loop.Breaks) Patch(b);
        _f.Loops.RemoveAt(_f.Loops.Count - 1);
    }

    // ================================================================ expressions

    private void Expression() => OrExpr();

    private void OrExpr()
    {
        AndExpr();
        while (Match(Tok.OrOr))
        {
            int j = EmitJump(OpCode.JumpIfTrueKeep);
            AndExpr();
            Patch(j);
        }
    }

    private void AndExpr()
    {
        Equality();
        while (Match(Tok.AndAnd))
        {
            int j = EmitJump(OpCode.JumpIfFalseKeep);
            Equality();
            Patch(j);
        }
    }

    private void Equality()
    {
        Comparison();
        while (true)
        {
            if (Match(Tok.Eq)) { Comparison(); Emit(OpCode.Eq); }
            else if (Match(Tok.Ne)) { Comparison(); Emit(OpCode.Ne); }
            else break;
        }
    }

    private void Comparison()
    {
        Term();
        while (true)
        {
            if (Match(Tok.Lt)) { Term(); Emit(OpCode.Lt); }
            else if (Match(Tok.Le)) { Term(); Emit(OpCode.Le); }
            else if (Match(Tok.Gt)) { Term(); Emit(OpCode.Gt); }
            else if (Match(Tok.Ge)) { Term(); Emit(OpCode.Ge); }
            else break;
        }
    }

    private void Term()
    {
        Factor();
        while (true)
        {
            if (Match(Tok.Plus)) { Factor(); Emit(OpCode.Add); }
            else if (Match(Tok.Minus)) { Factor(); Emit(OpCode.Sub); }
            else break;
        }
    }

    private void Factor()
    {
        Unary();
        while (true)
        {
            if (Match(Tok.Star)) { Unary(); Emit(OpCode.Mul); }
            else if (Match(Tok.Slash)) { Unary(); Emit(OpCode.Div); }
            else if (Match(Tok.Percent)) { Unary(); Emit(OpCode.Mod); }
            else break;
        }
    }

    private void Unary()
    {
        if (Match(Tok.Bang)) { Unary(); Emit(OpCode.Not); }
        else if (Match(Tok.Minus)) { Unary(); Emit(OpCode.Neg); }
        else Primary();
    }

    private void Primary()
    {
        var t = Next();
        switch (t.Kind)
        {
            case Tok.Number: Emit(OpCode.Const); EmitU16(Const(Value.From(t.Num))); return;
            case Tok.String: Emit(OpCode.Const); EmitU16(Const(Value.From(t.Text))); return;
            case Tok.LParen: Expression(); Expect(Tok.RParen, "')'"); return;
            case Tok.Ident:
                switch (t.Text)
                {
                    case "true": Emit(OpCode.True); return;
                    case "false": Emit(OpCode.False); return;
                    case "nil": Emit(OpCode.Nil); return;
                }
                if (Keywords.Contains(t.Text)) throw new ScriptException($"unexpected '{t.Text}'", t.Line);
                if (Peek.Kind == Tok.LParen) CallExpr(t); else GetVariable(t.Text, t.Line);
                return;
        }
        throw new ScriptException($"unexpected '{t.Text}'", t.Line);
    }

    private void CallExpr(Token name)
    {
        Next();   // (
        int argc = 0;
        if (Peek.Kind != Tok.RParen)
        {
            do { Expression(); argc++; } while (Match(Tok.Comma));
        }
        Expect(Tok.RParen, "')'");
        if (argc > 255) throw new ScriptException("too many arguments", name.Line);

        var native = ScriptNatives.Find(name.Text);
        if (native != null)
        {
            if (native.Arity >= 0 && native.Arity != argc)
                throw new ScriptException($"'{name.Text}' takes {native.Arity} arguments, not {argc}", name.Line);
            int idx = _chunk.Natives.IndexOf(name.Text);
            if (idx < 0) { idx = _chunk.Natives.Count; _chunk.Natives.Add(name.Text); }
            Emit(OpCode.CallNative); EmitU16(idx); Emit(argc);
        }
        else
        {
            int slot = FuncSlot(name.Text);
            _pendingCalls.Add((name.Text, argc, name.Line));
            Emit(OpCode.Call); EmitU16(slot); Emit(argc);
        }
    }

    // ================================================================ variables

    private int AddLocal(string name)
    {
        foreach (var l in _f.Locals)
            if (l.Active && l.Depth == _f.Depth && l.Name == name && _f.Depth > 0)
                throw Err($"variable '{name}' is already declared in this block");
        if (_f.Locals.Count >= 250) throw Err("too many local variables");
        _f.Locals.Add(new Local { Name = name, Depth = _f.Depth });
        return _f.Locals.Count - 1;
    }

    private int FindLocal(string name)
    {
        for (int i = _f.Locals.Count - 1; i >= 0; i--)
            if (_f.Locals[i].Active && _f.Locals[i].Name == name) return i;
        return -1;
    }

    private void GetVariable(string name, int line)
    {
        int slot = FindLocal(name);
        if (slot >= 0) { Emit(OpCode.GetLocal); Emit(slot); }
        else if (_globals.Contains(name)) { Emit(OpCode.GetGlobal); EmitU16(Const(name)); }
        else throw new ScriptException($"unknown variable '{name}'", line);
    }

    private void SetVariable(string name, int line)
    {
        int slot = FindLocal(name);
        if (slot >= 0) { Emit(OpCode.SetLocal); Emit(slot); }
        else if (_globals.Contains(name)) { Emit(OpCode.SetGlobal); EmitU16(Const(name)); }
        else throw new ScriptException($"unknown variable '{name}' (declare it with var)", line);
    }

    // ================================================================ emit helpers

    private void Emit(OpCode op) => _f.Code.Add((byte)op);
    private void Emit(int b) => _f.Code.Add((byte)b);

    private void EmitU16(int v)
    {
        if (v < 0 || v > 0xFFFF) throw Err("too many constants / functions");
        _f.Code.Add((byte)(v & 0xFF));
        _f.Code.Add((byte)(v >> 8));
    }

    private int Const(string s) => Const(Value.From(s));

    private int Const(Value v)
    {
        string key = v.Kind + ":" + v;
        if (_constIndex.TryGetValue(key, out int i)) return i;
        i = _consts.Count;
        _consts.Add(v);
        _constIndex[key] = i;
        return i;
    }

    /// <summary>Emits a forward jump and returns the position of its operand for <see cref="Patch"/>.</summary>
    private int EmitJump(OpCode op)
    {
        Emit(op);
        Emit(0); Emit(0);
        return _f.Code.Count - 2;
    }

    private void Patch(int operandPos) => PatchTo(operandPos, _f.Code.Count);

    private void PatchTo(int operandPos, int target)
    {
        int off = target - (operandPos + 2);
        if (off < short.MinValue || off > short.MaxValue) throw Err("jump too far");
        _f.Code[operandPos] = (byte)(off & 0xFF);
        _f.Code[operandPos + 1] = (byte)((off >> 8) & 0xFF);
    }

    private void EmitLoop(int target)
    {
        Emit(OpCode.Jump);
        Emit(0); Emit(0);
        PatchTo(_f.Code.Count - 2, target);
    }

    // ================================================================ token helpers

    private Token Peek => _t[_p];
    private Token Next() => _t[_p < _t.Count - 1 ? _p++ : _p];
    private bool Match(Tok k) { if (Peek.Kind == k) { Next(); return true; } return false; }
    private bool IsWord(string w) => Peek.Kind == Tok.Ident && Peek.Text == w;
    private ScriptException Err(string msg) => new(msg, Peek.Line);

    private void Expect(Tok k, string what)
    {
        if (Peek.Kind != k) throw Err($"expected {what} but found '{(Peek.Kind == Tok.Eof ? "end of file" : Peek.Text)}'");
        Next();
    }

    private string ExpectIdent(string what)
    {
        if (Peek.Kind != Tok.Ident || Keywords.Contains(Peek.Text)) throw Err($"expected {what}");
        return Next().Text;
    }
}
