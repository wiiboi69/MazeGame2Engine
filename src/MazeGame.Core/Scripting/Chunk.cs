using System.Text;

namespace MazeGame.Core.Scripting;

public sealed class FunctionInfo
{
    public string Name = "";
    public int Arity;
    public int Locals;        // total slots including parameters
    public int Entry = -1;    // offset into Chunk.Code
}

public sealed class HandlerInfo
{
    public string Event = "";     // start, tick, trigger, enter, exit, use, death, win, gem
    public string? Target;        // optional entity / trigger id
    public int Function;
}

/// <summary>A compiled script: bytecode, constants, functions, event handlers and the host functions it calls.</summary>
public sealed class Chunk
{
    public const string Magic = "MGBC";
    public const int Version = 1;

    public string Name = "script";
    public byte[] Code = Array.Empty<byte>();
    public Value[] Constants = Array.Empty<Value>();
    public List<FunctionInfo> Functions = new();
    public List<HandlerInfo> Handlers = new();
    /// <summary>Names of the host functions used by CallNative (resolved when the script is loaded).</summary>
    public List<string> Natives = new();
    /// <summary>Index of the function that sets up the global variables (runs once when the level starts).</summary>
    public int InitFunction = -1;

    // ================================================================ binary form (.mgb)

    public void Save(string path)
    {
        using var f = File.Create(path);
        Write(f);
    }

    public void Write(Stream s)
    {
        using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes(Magic));
        w.Write(Version);
        w.Write(Name);
        w.Write(Constants.Length);
        foreach (var c in Constants)
        {
            w.Write((byte)c.Kind);
            switch (c.Kind)
            {
                case ValueKind.Num: case ValueKind.Bool: w.Write(c.Num); break;
                case ValueKind.Str: w.Write(c.Str ?? ""); break;
            }
        }
        w.Write(Natives.Count);
        foreach (var n in Natives) w.Write(n);
        w.Write(Functions.Count);
        foreach (var f in Functions) { w.Write(f.Name); w.Write(f.Arity); w.Write(f.Locals); w.Write(f.Entry); }
        w.Write(Handlers.Count);
        foreach (var h in Handlers) { w.Write(h.Event); w.Write(h.Target ?? ""); w.Write(h.Function); }
        w.Write(InitFunction);
        w.Write(Code.Length);
        w.Write(Code);
    }

    public static Chunk Load(string path)
    {
        using var f = File.OpenRead(path);
        return Read(f);
    }

    public static Chunk Read(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("not a script bytecode file");
        if (r.ReadInt32() != Version) throw new InvalidDataException("unsupported bytecode version");
        var c = new Chunk { Name = r.ReadString() };
        int nc = r.ReadInt32();
        c.Constants = new Value[nc];
        for (int i = 0; i < nc; i++)
        {
            var kind = (ValueKind)r.ReadByte();
            c.Constants[i] = kind switch
            {
                ValueKind.Num => Value.From(r.ReadDouble()),
                ValueKind.Bool => Value.From(r.ReadDouble() != 0),
                ValueKind.Str => Value.From(r.ReadString()),
                _ => Value.Nil,
            };
        }
        int nn = r.ReadInt32();
        for (int i = 0; i < nn; i++) c.Natives.Add(r.ReadString());
        int nf = r.ReadInt32();
        for (int i = 0; i < nf; i++)
            c.Functions.Add(new FunctionInfo { Name = r.ReadString(), Arity = r.ReadInt32(), Locals = r.ReadInt32(), Entry = r.ReadInt32() });
        int nh = r.ReadInt32();
        for (int i = 0; i < nh; i++)
        {
            string ev = r.ReadString(), target = r.ReadString();
            c.Handlers.Add(new HandlerInfo { Event = ev, Target = target.Length == 0 ? null : target, Function = r.ReadInt32() });
        }
        c.InitFunction = r.ReadInt32();
        int len = r.ReadInt32();
        c.Code = r.ReadBytes(len);
        return c;
    }

    // ================================================================ disassembler

    public string Disassemble()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; {Name}  {Code.Length} bytes, {Constants.Length} constants, {Functions.Count} functions");
        for (int fi = 0; fi < Functions.Count; fi++)
        {
            var f = Functions[fi];
            sb.AppendLine($"\nfunc #{fi} {f.Name}  (arity {f.Arity}, locals {f.Locals}){(fi == InitFunction ? "  ; init" : "")}");
            int end = Code.Length;
            foreach (var other in Functions) if (other.Entry > f.Entry && other.Entry < end) end = other.Entry;
            int ip = f.Entry;
            while (ip < end)
            {
                int at = ip;
                var op = (OpCode)Code[ip++];
                string operand = "";
                switch (op)
                {
                    case OpCode.Const: { int k = U16(ref ip); operand = $"{k}  ; {Quote(Constants[k])}"; break; }
                    case OpCode.GetGlobal: case OpCode.SetGlobal: { int k = U16(ref ip); operand = $"{k}  ; {Constants[k]}"; break; }
                    case OpCode.GetLocal: case OpCode.SetLocal: operand = Code[ip++].ToString(); break;
                    case OpCode.Jump: case OpCode.JumpIfFalse: case OpCode.JumpIfFalseKeep: case OpCode.JumpIfTrueKeep:
                    { int off = (short)U16(ref ip); operand = $"{off:+#;-#;0}  ; -> {ip + off:D4}"; break; }
                    case OpCode.Call: { int k = U16(ref ip); int argc = Code[ip++]; operand = $"{Functions[k].Name}, {argc} args"; break; }
                    case OpCode.CallNative: { int k = U16(ref ip); int argc = Code[ip++]; operand = $"{Natives[k]}, {argc} args"; break; }
                }
                sb.AppendLine($"  {at:D4}  {op,-16} {operand}");
            }
        }
        sb.AppendLine("\nhandlers:");
        foreach (var h in Handlers) sb.AppendLine("  on " + h.Event + (string.IsNullOrEmpty(h.Target) ? "" : " \"" + h.Target + "\"") + " -> " + Functions[h.Function].Name);
        return sb.ToString();
    }

    private int U16(ref int ip) { int v = Code[ip] | (Code[ip + 1] << 8); ip += 2; return v; }
    private static string Quote(Value v) => v.Kind == ValueKind.Str ? "\"" + v.Str + "\"" : v.ToString();
}
