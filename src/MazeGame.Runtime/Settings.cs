using System.Text.Json;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>User settings saved next to the executable (settings.json).</summary>
public sealed class Settings
{
    public float MusicVolume { get; set; } = 0.5f;
    public float SfxVolume { get; set; } = 0.8f;
    public bool Fullscreen { get; set; }
    public bool ShowStats { get; set; }

    /// <summary>"4:3" (960x720) or "16:9" (1280x720, experimental).</summary>
    public string AspectRatio { get; set; } = "4:3";

    /// <summary>Render frame cap. 0 = unlimited. Game logic is fixed at 30 ticks/s regardless.</summary>
    public int FpsLimit { get; set; } = 60;

    public static readonly int[] FpsOptions = { 30, 60, 90, 120, 165, 180, 240, 0 };

    /// <summary>action name -> keyboard keys (as ints of Raylib_cs.KeyboardKey).</summary>
    public Dictionary<string, int[]> Keys { get; set; } = DefaultKeys();

    public static Dictionary<string, int[]> DefaultKeys() => new()
    {
        ["left"] = new[] { (int)KeyboardKey.Left, (int)KeyboardKey.A },
        ["right"] = new[] { (int)KeyboardKey.Right, (int)KeyboardKey.D },
        ["up"] = new[] { (int)KeyboardKey.Up, (int)KeyboardKey.W },
        ["down"] = new[] { (int)KeyboardKey.Down, (int)KeyboardKey.S },
        ["use"] = new[] { (int)KeyboardKey.E, (int)KeyboardKey.Enter },
        ["z"] = new[] { (int)KeyboardKey.Z },
        ["x"] = new[] { (int)KeyboardKey.X },
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
