using System.Text.Json;
using System.Text.Json.Serialization;

namespace MazeGame.Runtime;

/// <summary>One save slot. Stored as <c>saves/slotN.json</c> next to the executable.</summary>
public sealed class SaveSlot
{
    public bool Used { get; set; }
    /// <summary>Overworld currently shown (empty when the game has no overworlds).</summary>
    public string World { get; set; } = "";
    /// <summary>Node the cursor stands on.</summary>
    public string Node { get; set; } = "";
    /// <summary>Level id of the last level started; used when there is no overworld.</summary>
    public string Level { get; set; } = "";
    public int Gems { get; set; }
    public double PlayTime { get; set; }
    public List<string> Completed { get; set; } = new();
    public Dictionary<string, string> Flags { get; set; } = new();
    public string SavedAt { get; set; } = "";

    [JsonIgnore] public int Index;

    public string Summary(string worldName, string levelName)
        => !Used ? "Empty" : $"{(worldName.Length > 0 ? worldName : levelName)}  -  {Gems} gems  -  {FormatTime(PlayTime)}";

    public static string FormatTime(double s)
    {
        int t = (int)s;
        return t >= 3600 ? $"{t / 3600}h {t % 3600 / 60:D2}m" : $"{t / 60}m {t % 60:D2}s";
    }
}

/// <summary>Reads and writes the three save slots.</summary>
public sealed class SaveManager
{
    public const int SlotCount = 3;
    private readonly string _dir;
    public SaveSlot[] Slots { get; } = new SaveSlot[SlotCount];

    public SaveManager(string dir)
    {
        _dir = dir;
        for (int i = 0; i < SlotCount; i++) Slots[i] = Read(i);
    }

    private string PathOf(int i) => Path.Combine(_dir, $"slot{i + 1}.json");

    private SaveSlot Read(int i)
    {
        try
        {
            if (File.Exists(PathOf(i)))
            {
                var s = JsonSerializer.Deserialize<SaveSlot>(File.ReadAllText(PathOf(i)));
                if (s != null) { s.Index = i; return s; }
            }
        }
        catch (Exception) { /* corrupt slot: treat as empty */ }
        return new SaveSlot { Index = i };
    }

    public void Save(SaveSlot s)
    {
        s.Used = true;
        s.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathOf(s.Index), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* read-only install dir: ignore */ }
    }

    public void Erase(int i)
    {
        try { if (File.Exists(PathOf(i))) File.Delete(PathOf(i)); } catch (Exception) { }
        Slots[i] = new SaveSlot { Index = i };
    }
}
