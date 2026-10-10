namespace MazeGame.Core;

public enum PropKind
{
    /// <summary>Free text.</summary>
    Text,
    /// <summary>Whole number.</summary>
    Int,
    /// <summary>Decimal number.</summary>
    Number,
    /// <summary>A level number (the editor suggests the next level and checks it is a valid number).</summary>
    Level,
}

/// <summary>A setting an entity stores in the level file. The editor asks for these when the entity is placed.</summary>
public sealed class PropDef
{
    public string Name { get; }
    public string Prompt { get; }
    public string Default { get; }
    public PropKind Kind { get; }
    /// <summary>If the person cancels / enters nothing valid the entity is not placed.</summary>
    public bool Required { get; }

    public PropDef(string name, string prompt, string defaultValue = "", PropKind kind = PropKind.Text, bool required = false)
    {
        Name = name; Prompt = prompt; Default = defaultValue; Kind = kind; Required = required;
    }
}

/// <summary>
/// A kind of entity that can be placed in a level. Register your own in <c>GameContent.cs</c>:
/// <code>
/// EntityRegistry.Register(new EntityType("slime", "Slime", (def, editor) => new Slime(def)) { Texture = "custom/slime.png" });
/// </code>
/// </summary>
public sealed class EntityType
{
    public string Id { get; }
    public string Name { get; set; }
    /// <summary>Builds the running entity for a placed definition. <c>editor</c> is true when drawing in the level editor.</summary>
    public Func<EntityDef, bool, Entity?> Create { get; }
    /// <summary>Settings asked for in the editor and stored in <see cref="EntityDef.Props"/>.</summary>
    public List<PropDef> Props { get; } = new();
    /// <summary>Image used in game and in the editor palette (png/svg relative to the assets folder). Optional for built-ins.</summary>
    public string? Texture { get; set; }
    /// <summary>Editor palette section.</summary>
    public string Category { get; set; } = "Entities";
    /// <summary>Id of the editor brush tile (set by the registry).</summary>
    public string BrushTileId { get; internal set; } = "";

    public EntityType(string id, string name, Func<EntityDef, bool, Entity?> create)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("entity id must not be empty", nameof(id));
        Id = id; Name = name; Create = create;
    }

    public EntityType Prop(string name, string prompt, string defaultValue = "", PropKind kind = PropKind.Text, bool required = false)
    {
        Props.Add(new PropDef(name, prompt, defaultValue, kind, required));
        return this;
    }
}

/// <summary>All known entity types. The built-in ones are registered automatically.</summary>
public static class EntityRegistry
{
    private static readonly Dictionary<string, EntityType> ById = new(StringComparer.Ordinal);
    private static readonly List<EntityType> AllTypes = new();
    private static bool _init;

    private static void EnsureInit()
    {
        if (_init) return;
        _init = true;
        Builtin("walker", "Walker");
        Builtin("danger", "Danger (stompable)");
        Builtin("npc", "NPC (dialog)").Prop("text", "NPC says...", "hello").Prop("id", "Script id (optional, fires \"use\")", "");
        Builtin("end_box", "Level goal");
        Builtin("piranha", "Piranha plant");
        Builtin("door", "Door").Prop("target", "Target level number", "", PropKind.Level, true);
        Builtin("pipe", "Pipe").Prop("target", "Target level number", "", PropKind.Level, true);
        Builtin("door_wide", "Wide door").Prop("target", "Target level number", "", PropKind.Level, true);
    }

    private static EntityType Builtin(string id, string name)
    {
        var t = new EntityType(id, name, (d, editor) => Entity.SpawnBuiltin(d, editor));
        t.BrushTileId = id;                      // the generated built-in tiles already contain the brushes
        ById[id] = t;
        AllTypes.Add(t);
        return t;
    }

    public static IReadOnlyList<EntityType> All { get { EnsureInit(); return AllTypes; } }

    public static EntityType? Find(string id)
    {
        EnsureInit();
        return ById.TryGetValue(id, out var t) ? t : null;
    }

    /// <summary>Adds an entity type (and its editor brush tile). Throws if the id is taken.</summary>
    public static EntityType Register(EntityType type)
    {
        EnsureInit();
        if (ById.ContainsKey(type.Id)) throw new InvalidOperationException($"entity id '{type.Id}' is already registered");
        ById[type.Id] = type;
        AllTypes.Add(type);

        string tileId = TileRegistry.Find(type.Id) == null ? type.Id : "entity_" + type.Id;
        TileRegistry.Register(new TileDef(tileId, type.Name, "", "", "", type.Category) { Texture = type.Texture, Entity = type.Id });
        type.BrushTileId = tileId;
        return type;
    }
}
