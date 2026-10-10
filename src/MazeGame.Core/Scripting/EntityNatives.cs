namespace MazeGame.Core.Scripting;

/// <summary>
/// Script functions for entities, the player, the camera and level state.
/// Positions are in tiles (fractions allowed): an entity standing in cell 5,3 is at x=5.5.
/// Functions without an id act on "self": the entity that owns the script, or the player in a player script.
/// </summary>
internal static class EntityNatives
{
    private const double U = 32.0;

    public static void RegisterAll(Action<string, int, NativeFn> R)
    {
        // ---------------------------------------------------------------- self
        R("self_x", 0, (c, a) => Value.From(SelfX(c) / U));
        R("self_y", 0, (c, a) => Value.From(SelfY(c) / U));
        R("set_self_pos", 2, (c, a) => { SetPos(c, c.Self, a[0].AsNumber(), a[1].AsNumber()); return Value.Nil; });
        R("set_self_speed", 2, (c, a) => { SetSpeed(c, c.Self, a[0].AsNumber(), a[1].AsNumber()); return Value.Nil; });
        R("self_dir", 0, (c, a) => Value.From(c.Self != null ? c.Self.Dir : c.IsPlayer ? c.World.Player.Dir : 0));
        R("set_self_dir", 1, (c, a) => { if (c.Self != null) c.Self.Dir = a[0].AsNumber(); else if (c.IsPlayer) c.World.Player.Dir = a[0].AsNumber(); return Value.Nil; });
        R("self_type", 0, (c, a) => Value.From(c.Self != null ? c.Self.TypeId : c.IsPlayer ? "player" : ""));
        R("self_id", 0, (c, a) => Value.From(c.Self?.Id ?? ""));
        R("self_prop", 1, (c, a) => c.Self != null && c.Self.Props.TryGetValue(a[0].AsString(), out var v) ? Value.Parse(v) : Value.Nil);
        R("set_self_prop", 2, (c, a) => { if (c.Self != null) c.Self.Props[a[0].AsString()] = a[1].AsString(); return Value.Nil; });
        R("remove_self", 0, (c, a) => { if (c.Self != null) c.Self.Removed = true; return Value.Nil; });
        R("hide_self", 0, (c, a) => { if (c.Self != null) c.Self.Visible = false; return Value.Nil; });
        R("show_self", 0, (c, a) => { if (c.Self != null) c.Self.Visible = true; return Value.Nil; });
        R("self_touching", 0, (c, a) => Value.From(c.Self != null && c.Self.TouchesPlayer(c.World)));

        // ---------------------------------------------------------------- entities by id
        R("entity_exists", 1, (c, a) => Value.From(c.World.FindEntity(a[0].AsString()) != null));
        R("entity_x", 1, (c, a) => Value.From((c.World.FindEntity(a[0].AsString())?.X ?? 0) / U));
        R("entity_y", 1, (c, a) => Value.From((c.World.FindEntity(a[0].AsString())?.Y ?? 0) / U));
        R("set_entity_pos", 3, (c, a) => { With(c, a[0], e => SetPos(c, e, a[1].AsNumber(), a[2].AsNumber())); return Value.Nil; });
        R("move_entity", 3, (c, a) => { With(c, a[0], e => { e.X += a[1].AsNumber() * U; e.Y += a[2].AsNumber() * U; }); return Value.Nil; });
        R("set_entity_speed", 3, (c, a) => { With(c, a[0], e => SetSpeed(c, e, a[1].AsNumber(), a[2].AsNumber())); return Value.Nil; });
        R("remove_entity", 1, (c, a) => { With(c, a[0], e => e.Removed = true); return Value.Nil; });
        R("hide_entity", 1, (c, a) => { With(c, a[0], e => e.Visible = false); return Value.Nil; });
        R("show_entity", 1, (c, a) => { With(c, a[0], e => e.Visible = true); return Value.Nil; });
        R("entity_prop", 2, (c, a) => c.World.FindEntity(a[0].AsString()) is { } e && e.Props.TryGetValue(a[1].AsString(), out var v) ? Value.Parse(v) : Value.Nil);
        R("set_entity_prop", 3, (c, a) => { With(c, a[0], e => e.Props[a[1].AsString()] = a[2].AsString()); return Value.Nil; });
        R("send_entity", 2, (c, a) => { With(c, a[0], e => e.Script?.Fire("trigger", a[1].AsString())); return Value.Nil; });
        R("count_entities", 1, (c, a) =>
        {
            string t = a[0].AsString();
            return Value.From(c.World.Entities.Count(e => !e.Removed && (t.Length == 0 || e.TypeId == t)));
        });
        R("spawn_named", 4, (c, a) =>
        {
            var props = new Dictionary<string, string> { ["id"] = a[3].AsString() };
            c.World.SpawnEntity(a[0].AsString(), a[1].AsInt(), a[2].AsInt(), props);
            return Value.Nil;
        });

        // ---------------------------------------------------------------- player
        R("player_px", 0, (c, a) => Value.From(c.World.Player.X / U));
        R("player_py", 0, (c, a) => Value.From(c.World.Player.Y / U));
        R("set_player_pos", 2, (c, a) => { c.World.Player.X = a[0].AsNumber() * U; c.World.Player.Y = a[1].AsNumber() * U; return Value.Nil; });
        R("set_player_speed", 2, (c, a) => { c.World.Player.SpeedX = a[0].AsNumber(); c.World.Player.SpeedY = a[1].AsNumber(); return Value.Nil; });
        R("player_dir", 0, (c, a) => Value.From(c.World.Player.Dir));
        R("freeze_player", 1, (c, a) => { c.World.PlayerFrozen = a[0].Truthy; return Value.Nil; });
        R("key_down", 1, (c, a) =>
        {
            var i = c.World.Input;
            return Value.From(a[0].AsString() switch
            {
                "left" => i.Left, "right" => i.Right, "up" => i.Up, "down" => i.Down,
                "z" => i.Z, "x" => i.X, "use" => i.Use, _ => false,
            });
        });

        // ---------------------------------------------------------------- camera
        R("camera_x", 0, (c, a) => Value.From(c.World.CamX / U));
        R("camera_y", 0, (c, a) => Value.From(c.World.CamY / U));
        R("set_camera", 2, (c, a) => { c.World.CameraLock(a[0].AsNumber() * U, a[1].AsNumber() * U); return Value.Nil; });
        R("camera_pan", 3, (c, a) => { c.World.CameraPan(a[0].AsNumber() * U, a[1].AsNumber() * U, a[2].AsNumber()); return Value.Nil; });
        R("camera_follow", 0, (c, a) => { c.World.CameraFollow(); return Value.Nil; });
        R("camera_shake", 2, (c, a) => { c.World.CameraShake(a[0].AsNumber(), a[1].AsNumber()); return Value.Nil; });
        R("camera_zoom", 1, (c, a) => { c.World.CamZoom = Math.Clamp(a[0].AsNumber(), 0.25, 4); return Value.Nil; });

        // ---------------------------------------------------------------- level state
        R("save_state", 0, (c, a) => { c.World.RequestSaveState(); return Value.Nil; });
        R("load_state", 0, (c, a) => { c.World.RequestLoadState(); return Value.Nil; });
    }

    private static double SelfX(ScriptContext c) => c.Self?.X ?? (c.IsPlayer ? c.World.Player.X : 0);
    private static double SelfY(ScriptContext c) => c.Self?.Y ?? (c.IsPlayer ? c.World.Player.Y : 0);

    private static void With(ScriptContext c, Value id, Action<Entity> act)
    {
        var e = c.World.FindEntity(id.AsString());
        if (e != null) act(e);
    }

    private static void SetPos(ScriptContext c, Entity? e, double x, double y)
    {
        if (e != null) { e.X = x * U; e.Y = y * U; }
        else if (c.IsPlayer) { c.World.Player.X = x * U; c.World.Player.Y = y * U; }
    }

    private static void SetSpeed(ScriptContext c, Entity? e, double sx, double sy)
    {
        if (e != null) { e.SpeedX = sx; e.SpeedY = sy; }
        else if (c.IsPlayer) { c.World.Player.SpeedX = sx; c.World.Player.SpeedY = sy; }
    }
}
