namespace MazeGame.Core.Scripting;

/// <summary>One running function call chain (a coroutine). Created by <see cref="ScriptHost"/> for each fired event.</summary>
public sealed class ScriptTask
{
    private struct Frame { public int Func, Ip, Base; }

    private readonly Chunk _c;
    private readonly Value[] _stack = new Value[512];
    private int _sp;
    private readonly Frame[] _frames = new Frame[64];
    private int _fp;
    private int _steps;

    public int WaitTicks;
    public bool WaitDialog;
    public bool Done { get; private set; }
    public string Name { get; }

    public ScriptTask(Chunk chunk, int function, Value[]? args, string name)
    {
        _c = chunk; Name = name;
        Push(function, args ?? Array.Empty<Value>());
    }

    private void Push(int func, Value[] args)
    {
        if (_fp >= _frames.Length) throw new ScriptException("call stack overflow");
        var info = _c.Functions[func];
        int b = _sp;
        for (int i = 0; i < info.Locals; i++) _stack[_sp++] = i < args.Length ? args[i] : Value.Nil;
        _frames[_fp++] = new Frame { Func = func, Ip = info.Entry, Base = b };
    }

    /// <summary>Runs until the task finishes, waits, or uses up its step budget for this tick.</summary>
    public void Run(ScriptContext ctx, Dictionary<string, Value> globals)
    {
        if (Done) return;
        if (WaitTicks > 0) { WaitTicks--; if (WaitTicks > 0) return; }
        if (WaitDialog) return;
        ctx.Task = this;
        var code = _c.Code;
        int budget = 20000;

        while (budget-- > 0)
        {
            ref Frame fr = ref _frames[_fp - 1];
            var op = (OpCode)code[fr.Ip++];
            switch (op)
            {
                case OpCode.Nop: break;
                case OpCode.Const: _stack[_sp++] = _c.Constants[U16(code, ref fr.Ip)]; break;
                case OpCode.Nil: _stack[_sp++] = Value.Nil; break;
                case OpCode.True: _stack[_sp++] = Value.True; break;
                case OpCode.False: _stack[_sp++] = Value.False; break;
                case OpCode.Pop: _sp--; break;
                case OpCode.GetLocal: _stack[_sp++] = _stack[fr.Base + code[fr.Ip++]]; break;
                case OpCode.SetLocal: _stack[fr.Base + code[fr.Ip++]] = _stack[--_sp]; break;
                case OpCode.GetGlobal:
                {
                    string n = _c.Constants[U16(code, ref fr.Ip)].AsString();
                    _stack[_sp++] = globals.TryGetValue(n, out var gv) ? gv : Value.Nil;
                    break;
                }
                case OpCode.SetGlobal:
                    globals[_c.Constants[U16(code, ref fr.Ip)].AsString()] = _stack[--_sp];
                    break;

                case OpCode.Add:
                {
                    var b = _stack[--_sp]; var a = _stack[_sp - 1];
                    _stack[_sp - 1] = (a.Kind == ValueKind.Str || b.Kind == ValueKind.Str)
                        ? Value.From(a.AsString() + b.AsString()) : Value.From(a.AsNumber() + b.AsNumber());
                    break;
                }
                case OpCode.Sub: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() - b.AsNumber()); break; }
                case OpCode.Mul: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() * b.AsNumber()); break; }
                case OpCode.Div:
                {
                    var b = _stack[--_sp]; double d = b.AsNumber();
                    _stack[_sp - 1] = Value.From(d == 0 ? 0 : _stack[_sp - 1].AsNumber() / d);
                    break;
                }
                case OpCode.Mod:
                {
                    var b = _stack[--_sp]; double d = b.AsNumber();
                    _stack[_sp - 1] = Value.From(d == 0 ? 0 : _stack[_sp - 1].AsNumber() % d);
                    break;
                }
                case OpCode.Neg: _stack[_sp - 1] = Value.From(-_stack[_sp - 1].AsNumber()); break;
                case OpCode.Not: _stack[_sp - 1] = Value.From(!_stack[_sp - 1].Truthy); break;
                case OpCode.Eq: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].Equals(b)); break; }
                case OpCode.Ne: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(!_stack[_sp - 1].Equals(b)); break; }
                case OpCode.Lt: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() < b.AsNumber()); break; }
                case OpCode.Le: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() <= b.AsNumber()); break; }
                case OpCode.Gt: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() > b.AsNumber()); break; }
                case OpCode.Ge: { var b = _stack[--_sp]; _stack[_sp - 1] = Value.From(_stack[_sp - 1].AsNumber() >= b.AsNumber()); break; }

                case OpCode.Jump: { int o = I16(code, ref fr.Ip); fr.Ip += o; break; }
                case OpCode.JumpIfFalse: { int o = I16(code, ref fr.Ip); if (!_stack[--_sp].Truthy) fr.Ip += o; break; }
                case OpCode.JumpIfFalseKeep: { int o = I16(code, ref fr.Ip); if (!_stack[_sp - 1].Truthy) fr.Ip += o; else _sp--; break; }
                case OpCode.JumpIfTrueKeep: { int o = I16(code, ref fr.Ip); if (_stack[_sp - 1].Truthy) fr.Ip += o; else _sp--; break; }

                case OpCode.Call:
                {
                    int f = U16(code, ref fr.Ip); int argc = code[fr.Ip++];
                    var args = new Value[argc];
                    for (int i = argc - 1; i >= 0; i--) args[i] = _stack[--_sp];
                    Push(f, args);
                    break;
                }
                case OpCode.CallNative:
                {
                    int n = U16(code, ref fr.Ip); int argc = code[fr.Ip++];
                    var args = new Value[argc];
                    for (int i = argc - 1; i >= 0; i--) args[i] = _stack[--_sp];
                    var native = ScriptNatives.Find(_c.Natives[n]);
                    _stack[_sp++] = native == null ? Value.Nil : native.Fn(ctx, args);
                    if (WaitTicks > 0 || WaitDialog) return;
                    break;
                }
                case OpCode.Return:
                {
                    var rv = _stack[--_sp];
                    _sp = fr.Base;
                    _fp--;
                    if (_fp == 0) { Done = true; return; }
                    _stack[_sp++] = rv;
                    break;
                }
                default: throw new ScriptException($"bad opcode {op} in {Name}");
            }
        }
        Console.WriteLine($"[script] '{Name}' ran too long in one tick; yielding");
    }

    private static int U16(byte[] c, ref int ip) { int v = c[ip] | (c[ip + 1] << 8); ip += 2; return v; }
    private static int I16(byte[] c, ref int ip) { int v = (short)(c[ip] | (c[ip + 1] << 8)); ip += 2; return v; }
}
