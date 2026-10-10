using System.Text.Json;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>User settings saved next to the executable (settings.json).</summary>
public sealed class Settings
{
    public float MasterVolume { get; set; } = 1f;
    public bool Mute { get; set; }
    public float MusicVolume { get; set; } = 0.5f;
    public float SfxVolume { get; set; } = 0.8f;
    public bool Fullscreen { get; set; }
    public bool ShowStats { get; set; }
    /// <summary>false = 4:3 (960x720), true = 16:9 (1280x720).</summary>
    public bool Widescreen { get; set; }
    /// <summary>Window size as a fraction of the full virtual size: 1, 2 or 3 (thirds).</summary>
    public int WindowScale { get; set; } = 3;
    public bool VSync { get; set; } = true;
    public int FpsLimit { get; set; } = 60;
    public int LastSlot { get; set; }

    /// <summary>action name -> keyboard keys (as ints of Raylib_cs.KeyboardKey).</summary>
    public Dictionary<string, int[]> Keys { get; set; } = DefaultKeys();

    public static Dictionary<string, int[]> DefaultKeys() => new()
    {
        ["left"] = new[] { (int)KeyboardKey.Left, (int)KeyboardKey.A },
        ["right"] = new[] { (int)KeyboardKey.Right, (int)KeyboardKey.D },
        ["up"] = new[] { (int)KeyboardKey.Up, (int)KeyboardKey.W },
        ["down"] = new[] { (int)KeyboardKey.Down, (int)KeyboardKey.S },
        ["use"] = new[] { (int)KeyboardKey.E, (int)KeyboardKey.Enter },
        ["pause"] = new[] { (int)KeyboardKey.Escape, (int)KeyboardKey.P },
        ["z"] = new[] { (int)KeyboardKey.Z },
        ["x"] = new[] { (int)KeyboardKey.X },
        ["savestate"] = new[] { (int)KeyboardKey.F6 },
        ["loadstate"] = new[] { (int)KeyboardKey.F7 },
    };

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                if (s != null)
                {
                    foreach (var kv in DefaultKeys())
                        if (!s.Keys.ContainsKey(kv.Key)) s.Keys[kv.Key] = kv.Value;
                    return s;
                }
            }
        }
        catch (Exception) { /* fall back to defaults */ }
        return new Settings();
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception) { /* read-only install dir: ignore */ }
    }
}
