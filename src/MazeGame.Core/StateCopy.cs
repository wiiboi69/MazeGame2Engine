using System.Reflection;

namespace MazeGame.Core;

/// <summary>
/// Copies the "plain" instance fields (numbers, bools, enums, strings; public and private) of any object.
/// Used by level state saving so custom entities are captured without writing any code for them.
/// Collections and object references are not copied.
/// </summary>
internal static class StateCopy
{
    private static readonly Dictionary<Type, FieldInfo[]> Cache = new();

    private static FieldInfo[] FieldsOf(Type t)
    {
        if (Cache.TryGetValue(t, out var f)) return f;
        var list = new List<FieldInfo>();
        for (var c = t; c != null && c != typeof(object); c = c.BaseType)
            foreach (var fi in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (fi.IsInitOnly || fi.IsLiteral) continue;
                var ft = fi.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || Nullable.GetUnderlyingType(ft) is { } u && (u.IsPrimitive || u.IsEnum))
                    list.Add(fi);
            }
        return Cache[t] = list.ToArray();
    }

    public static Dictionary<FieldInfo, object?> Capture(object o)
    {
        var d = new Dictionary<FieldInfo, object?>();
        foreach (var f in FieldsOf(o.GetType())) d[f] = f.GetValue(o);
        return d;
    }

    public static void Restore(object o, Dictionary<FieldInfo, object?> d)
    {
        foreach (var kv in d) kv.Key.SetValue(o, kv.Value);
    }
}

/// <summary>A saved copy of one running level (see <see cref="World.CaptureState"/>).</summary>
public sealed class WorldSnapshot
{
    internal string LevelId = "";
    internal int[] Tiles = Array.Empty<int>();
    internal int Coins, BouncePlayer, BumpIndex, ScreenX, ScreenY;
    internal double CamX, CamY, CamXControl, CamYControl;
    internal bool PlayerBehindTiles;
    internal long TickCount;
    internal Dictionary<FieldInfo, object?> Player = new();
    internal Dictionary<string, string> Flags = new();
    internal Dictionary<string, Scripting.Value>? LevelGlobals, PlayerGlobals;

    internal sealed class Ent
    {
        public EntityDef Def = new();
        public Dictionary<FieldInfo, object?> Fields = new();
        public Dictionary<string, Scripting.Value>? Globals;
        public bool Touching;
    }
    internal List<Ent> Entities = new();
}
