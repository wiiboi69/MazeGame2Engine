namespace MazeGame.Core.Scripting;

/// <summary>Owns a level's loaded script: globals, running tasks and event dispatch.</summary>
public sealed class ScriptHost
{
    private readonly Chunk _chunk;
    private readonly World _world;
    private readonly Dictionary<string, Value> _globals = new();
    private readonly List<ScriptTask> _tasks = new();
    private readonly ScriptContext _ctx;

    public ScriptHost(World world, Chunk chunk, Entity? self = null, bool isPlayer = false, bool runInit = true)
    {
        _world = world; _chunk = chunk;
        _ctx = new ScriptContext { World = world, Host = this, Self = self, IsPlayer = isPlayer };
        if (runInit)
        {
            if (chunk.InitFunction >= 0) Spawn(chunk.InitFunction, "init");
            RunTasks();
        }
    }

    public bool HasHandler(string ev) => _chunk.Handlers.Any(h => h.Event == ev);

    /// <summary>Runs queued tasks now (used right after firing an event outside the normal tick).</summary>
    public void Flush() => RunTasks();

    public Dictionary<string, Value> CaptureGlobals() => new(_globals);

    /// <summary>Restores saved globals and drops every running task (they cannot be resumed).</summary>
    public void RestoreGlobals(Dictionary<string, Value>? g)
    {
        _tasks.Clear();
        _globals.Clear();
        if (g != null) foreach (var kv in g) _globals[kv.Key] = kv.Value;
    }

    private void Spawn(int func, string name) => _tasks.Add(new ScriptTask(_chunk, func, null, name));

    /// <summary>Starts every handler for the event whose target matches (a handler with no target matches all).</summary>
    public void Fire(string ev, string? target = null)
    {
        foreach (var h in _chunk.Handlers)
        {
            if (h.Event != ev) continue;
            if (!string.IsNullOrEmpty(h.Target) && h.Target != target) continue;
            if (_tasks.Count > 64) return;
            Spawn(h.Function, $"on {ev} {target}");
        }
    }

    public void DialogClosed()
    {
        foreach (var t in _tasks) t.WaitDialog = false;
    }

    public void Tick()
    {
        Fire("tick");
        RunTasks();
    }

    private void RunTasks()
    {
        for (int i = 0; i < _tasks.Count; i++)
        {
            try { _tasks[i].Run(_ctx, _globals); }
            catch (Exception ex)
            {
                Console.WriteLine($"[script] error in {_chunk.Name}: {ex.Message}");
                _tasks.RemoveAt(i--);
                continue;
            }
            if (_tasks[i].Done) _tasks.RemoveAt(i--);
        }
    }
}

/// <summary>Loads scripts by id from <see cref="BaseDir"/>: <c>scripts/&lt;id&gt;.mgb</c> (bytecode) wins over <c>.mgs</c> (source).</summary>
public static class ScriptLibrary
{
    public static string BaseDir = "";
    private static readonly Dictionary<string, Chunk?> Cache = new();

    public static Chunk? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Cache.TryGetValue(id, out var c)) return c;
        c = null;
        try
        {
            string dir = Path.Combine(BaseDir, "scripts");
            string mgb = Path.Combine(dir, id + ".mgb"), mgs = Path.Combine(dir, id + ".mgs");
            if (File.Exists(mgb)) c = Chunk.Load(mgb);
            else if (File.Exists(mgs)) c = ScriptCompiler.Compile(File.ReadAllText(mgs), id);
        }
        catch (Exception ex) { Console.WriteLine($"[script] failed to load '{id}': {ex.Message}"); }
        Cache[id] = c;
        return c;
    }

    public static void ClearCache() => Cache.Clear();
}
