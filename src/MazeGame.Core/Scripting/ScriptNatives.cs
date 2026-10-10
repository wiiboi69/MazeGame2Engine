namespace MazeGame.Core.Scripting;

/// <summary>What a native function can see and do while a script task is running.</summary>
public sealed class ScriptContext
{
    public World World = null!;
    public ScriptHost Host = null!;
    public ScriptTask Task = null!;
    /// <summary>The entity this script belongs to (null for level and player scripts).</summary>
    public Entity? Self;
    /// <summary>True when this is the player's script.</summary>
    public bool IsPlayer;
}

public delegate Value NativeFn(ScriptContext ctx, Value[] args);

public sealed class NativeInfo
{
    public string Name = "";
    public int Arity;           // -1 = any
    public NativeFn Fn = null!;
}

/// <summary>
/// Table of host functions callable from scripts. Add your own at startup:
/// <c>ScriptNatives.Register("shake", 1, (ctx, a) => { ...; return Value.Nil; });</c>
/// </summary>
public static class ScriptNatives
{
    private static readonly Dictionary<string, NativeInfo> Table = new(StringComparer.Ordinal);
    private static readonly Random Rng = new();

    public static NativeInfo? Find(string name) { Init(); return Table.TryGetValue(name, out var n) ? n : null; }
    public static IEnumerable<string> Names { get { Init(); return Table.Keys; } }

    public static void Register(string name, int arity, NativeFn fn)
        => Table[name] = new NativeInfo { Name = name, Arity = arity, Fn = fn };

    private static bool _init;
    private static void Init()
    {
        if (_init) return;
        _init = true;
        var R = Register;

        // ---- dialog / timing
        R("say", 1, (c, a) => { c.World.ShowDialog(a[0].AsString()); c.Task.WaitDialog = true; return Value.Nil; });
        R("wait", 1, (c, a) => { c.Task.WaitTicks = (int)Math.Round(a[0].AsNumber() * GameConstants.TicksPerSecond); return Value.Nil; });
        R("wait_ticks", 1, (c, a) => { c.Task.WaitTicks = a[0].AsInt(); return Value.Nil; });
        R("print", -1, (c, a) => { Console.WriteLine("[script] " + string.Join(" ", a.Select(v => v.AsString()))); return Value.Nil; });

        // ---- flags & gems
        R("flag", 1, (c, a) => c.World.Flags.TryGetValue(a[0].AsString(), out var v) ? Value.Parse(v) : Value.False);
        R("set_flag", 2, (c, a) => { c.World.Flags[a[0].AsString()] = a[1].AsString(); return Value.Nil; });
        R("gems", 0, (c, a) => Value.From(c.World.Coins));
        R("give_gems", 1, (c, a) => { c.World.Coins += a[0].AsInt(); return Value.Nil; });

        // ---- tiles & entities
        R("tile", 2, (c, a) =>
        {
            int x = a[0].AsInt(), y = a[1].AsInt(), h = c.World.Height;
            if (x < 0 || y < 0 || x >= c.World.Width || y >= h) return Value.Nil;
            return Value.From(TileRegistry.IdOf(c.World.Tiles[x * h + y]));
        });
        R("set_tile", 3, (c, a) =>
        {
            int x = a[0].AsInt(), y = a[1].AsInt(), h = c.World.Height;
            if (x < 0 || y < 0 || x >= c.World.Width || y >= h) return Value.Nil;
            var def = TileRegistry.Find(a[2].AsString());
            if (def == null) { Console.WriteLine($"[script] unknown tile '{a[2].AsString()}'"); return Value.Nil; }
            c.World.Tiles[x * h + y] = def.Num;
            return Value.Nil;
        });
        R("spawn", 3, (c, a) => { c.World.SpawnEntity(a[0].AsString(), a[1].AsInt(), a[2].AsInt()); return Value.Nil; });
        R("remove_at", 2, (c, a) => { c.World.RemoveEntitiesAt(a[0].AsInt(), a[1].AsInt()); return Value.Nil; });

        // ---- player
        R("teleport", 2, (c, a) => { c.World.Player.X = a[0].AsNumber() * 32 + 16; c.World.Player.Y = a[1].AsNumber() * 32; c.World.Player.SpeedX = c.World.Player.SpeedY = 0; return Value.Nil; });
        R("player_x", 0, (c, a) => Value.From(Math.Floor(c.World.Player.X / 32)));
        R("player_y", 0, (c, a) => Value.From(Math.Floor(c.World.Player.Y / 32)));
        R("kill_player", 0, (c, a) => { c.World.KillPlayer(); return Value.Nil; });

        // ---- flow
        R("play_sound", 1, (c, a) => { c.World.PlaySound(a[0].AsString()); return Value.Nil; });
        R("goto_level", 1, (c, a) => { c.World.GotoLevel(a[0].AsString()); return Value.Nil; });
        R("win", 0, (c, a) => { c.World.BeginLevelComplete(); return Value.Nil; });
        R("level_id", 0, (c, a) => Value.From(c.World.LevelId));
        R("emit", 1, (c, a) => { c.Host.Fire("trigger", a[0].AsString()); return Value.Nil; });

        // ---- math / strings
        R("random", 2, (c, a) => { int lo = a[0].AsInt(), hi = a[1].AsInt(); return Value.From(hi < lo ? lo : Rng.Next(lo, hi + 1)); });
        R("floor", 1, (c, a) => Value.From(Math.Floor(a[0].AsNumber())));
        R("abs", 1, (c, a) => Value.From(Math.Abs(a[0].AsNumber())));
        R("str", 1, (c, a) => Value.From(a[0].AsString()));
        R("len", 1, (c, a) => Value.From(a[0].AsString().Length));

        EntityNatives.RegisterAll(R);
    }
}
