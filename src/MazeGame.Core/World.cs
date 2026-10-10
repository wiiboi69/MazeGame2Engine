namespace MazeGame.Core;

public enum WorldMode { Playing, Dying, Complete, Pipe, Frozen }

public enum LoadKind
{
    /// <summary>Put the player on the level's spawn marker ("LEVEL - Start Game Loop - Respawn").</summary>
    Respawn,
    /// <summary>Keep the player and walk in from the side ("level connect" when leaving the level edge).</summary>
    Connect,
}

/// <summary>Something the host (game / editor) must do between ticks: swap the level, usually with a wipe.</summary>
public readonly record struct LoadRequest(string Level, LoadKind Kind, bool Wipe, int EntrySide = 0, bool Win = false);

/// <summary>
/// One running level: tiles, player, entities, particles and camera. Call <see cref="Tick"/> 30 times a second
/// with the current <see cref="InputState"/>. Mirrors the original "game loop":
/// controls, move player, move enemy, move player after enemy, position tiles.
/// </summary>
public sealed class World
{
    public LevelData Level { get; private set; }
    public string LevelId { get; private set; } = "";
    /// <summary>Live tile grid (column-major, copy of the level so picked-up gems can be removed).</summary>
    public int[] Tiles { get; private set; } = Array.Empty<int>();

    public Player Player { get; }
    public List<Entity> Entities { get; } = new();
    public List<Particle> Particles { get; } = new();

    public double CamX, CamY;
    public double CamXControl, CamYControl;
    public int ScreenX, ScreenY;

    public int Coins;
    public int BouncePlayer;      // BOUNCE PLAYER: remaining ticks of stomp bounce
    public int BumpIndex;         // BUMP INDEX (1-based tile index, 0 = none)
    public bool GodMode;          // fly / no-collision debug movement (the original's EDITOR mode)
    public WorldMode Mode { get; private set; } = WorldMode.Playing;
    public LoadRequest? Pending { get; private set; }
    public bool PlayerBehindTiles { get; private set; }
    public long TickCount { get; private set; }
    public InputState Input;

    public Func<string, LevelData?>? LevelSource;

    public event Action<string>? SoundRequested;
    public event Action<string>? DialogRequested;

    /// <summary>Script flags (saved with the save slot). Shared with the game, which binds this to the slot.</summary>
    public Dictionary<string, string> Flags = new();
    /// <summary>The running script of the current level, if any.</summary>
    public Scripting.ScriptHost? Host { get; private set; }
    /// <summary>The player's own script runtime (level property PlayerScript, else the game-wide default).</summary>
    public Scripting.ScriptHost? PlayerHost { get; private set; }
    /// <summary>Game-wide fallback player script id (game.json "playerScript").</summary>
    public static string DefaultPlayerScript = "";

    // ---- level state saving (see CaptureState)
    /// <summary>Saved states by level id. The game shares one dictionary and clears it when the overworld opens.</summary>
    public Dictionary<string, WorldSnapshot> States = new();
    private string _stateCmd = "";
    public event Action<string>? Notice;
    public void RequestSaveState() => _stateCmd = "save";
    public void RequestLoadState() => _stateCmd = "load";
    public bool PlayerFrozen;

    // ---- script-controlled camera
    public bool CamLocked;
    public double CamLockX, CamLockY;
    public double CamZoom = 1;
    public double ShakeX, ShakeY;
    private double _panFromX, _panFromY, _panToX, _panToY, _shakeAmt;
    private int _panTick, _panTotal, _shakeTicks;
    private static readonly Random CamRng = new();

    public void CameraLock(double px, double py) { _panTotal = 0; CamLocked = true; CamLockX = px; CamLockY = py; CamX = px; CamY = py; }
    public void CameraFollow() { _panTotal = 0; CamLocked = false; }
    public void CameraPan(double px, double py, double seconds)
    {
        int ticks = (int)Math.Round(seconds * GameConstants.TicksPerSecond);
        if (ticks <= 0) { CameraLock(px, py); return; }
        _panFromX = CamX; _panFromY = CamY; _panToX = px; _panToY = py; _panTick = 0; _panTotal = ticks; CamLocked = false;
    }
    public void CameraShake(double amount, double seconds) { _shakeAmt = amount; _shakeTicks = (int)Math.Round(seconds * GameConstants.TicksPerSecond); }

    private void ApplyScriptCamera()
    {
        if (_panTotal > 0)
        {
            _panTick++;
            double t = Math.Min(1.0, (double)_panTick / _panTotal);
            t = t * t * (3 - 2 * t);
            CamX = _panFromX + (_panToX - _panFromX) * t;
            CamY = _panFromY + (_panToY - _panFromY) * t;
            if (_panTick >= _panTotal) { _panTotal = 0; CamLocked = true; CamLockX = _panToX; CamLockY = _panToY; }
        }
        else if (CamLocked) { CamX = CamLockX; CamY = CamLockY; }
        if (_shakeTicks > 0)
        {
            _shakeTicks--;
            ShakeX = (CamRng.NextDouble() * 2 - 1) * _shakeAmt;
            ShakeY = (CamRng.NextDouble() * 2 - 1) * _shakeAmt;
        }
        else ShakeX = ShakeY = 0;
    }

    /// <summary>Finds an entity by the "id" given to it in the editor.</summary>
    public Entity? FindEntity(string id)
    {
        foreach (var e in Entities) if (!e.Removed && e.Id == id) return e;
        return null;
    }

    internal void AttachScript(Entity e, bool fresh)
    {
        var chunk = Scripting.ScriptLibrary.Get(e.Def?.Get("script"));
        if (chunk == null) return;
        e.Script = new Scripting.ScriptHost(this, chunk, e, false, fresh);
        if (fresh) { e.Script.Fire("start"); e.Script.Flush(); }
    }
    /// <summary>Directory-less hook: lets the game provide scripts instead of <see cref="Scripting.ScriptLibrary"/>.</summary>
    public void DialogClosed() => Host?.DialogClosed();
    public void FireEvent(string ev, string? target = null) => Host?.Fire(ev, target);

    public Entity? SpawnEntity(string type, int cx, int cy, Dictionary<string, string>? props = null)
    {
        var def = new EntityDef { X = cx, Y = cy, Type = type };
        if (props != null) def.Props = props;
        var e = Entity.Spawn(def, Level.Height, false);
        if (e != null) { Entities.Add(e); AttachScript(e, true); }
        return e;
    }

    public void RemoveEntitiesAt(int cx, int cy)
    {
        foreach (var e in Entities)
            if ((int)Math.Floor(e.X / 32) == cx && (int)Math.Floor(e.Y / 32) == cy) e.Removed = true;
    }

    public void GotoLevel(string id)
    {
        if (Mode == WorldMode.Playing && HasLevel(id))
        { Mode = WorldMode.Frozen; Pending = new LoadRequest(id, LoadKind.Respawn, true); }
    }

    private bool _deathQueued;
    private bool _deathStarted;
    private string _pipeTarget = "";
    private int _pipePhase, _pipeTimer;

    public World(LevelData level, string levelId, Func<string, LevelData?>? source = null)
    {
        LevelSource = source;
        Player = new Player(this);
        Level = level;
        Load(level, levelId, LoadKind.Respawn, 0);
    }

    public int Width => Level.Width;
    public int Height => Level.Height;

    // ================================================================ loading

    public void Load(LevelData data, string id, LoadKind kind, int entrySide)
    {
        Level = data;
        LevelId = id;
        Tiles = (int[])data.Tiles.Clone();

        int spawnIndex = Array.IndexOf(Tiles, TileInfo.PlayerSpawn);
        if (spawnIndex >= 0 && !GodMode) Tiles[spawnIndex] = TileInfo.Air;

        Entities.Clear();
        Particles.Clear();
        foreach (var def in data.Entities)
        {
            var e = Entity.Spawn(def.Clone(), data.Height, false);   // a copy, so scripts can change props freely
            if (e != null) Entities.Add(e);
        }
        CamLocked = false; _panTotal = 0; _shakeTicks = 0; ShakeX = ShakeY = 0; CamZoom = 1; PlayerFrozen = false;
        _stateCmd = "";

        Coins = 0;
        BouncePlayer = 0;
        BumpIndex = 0;
        Mode = WorldMode.Playing;
        Pending = null;
        _deathQueued = false;
        _deathStarted = false;
        PlayerBehindTiles = false;

        if (kind == LoadKind.Respawn)
        {
            double x = 1, y = 1;
            if (spawnIndex >= 0)
            {
                x = spawnIndex / data.Height;
                y = spawnIndex % data.Height;
            }
            double px = x * 32 + 16;
            double py = y * 32 + 18 - 32;      // original: ((y*32) + height) - 32 with height 18
            Player.Reset(px, py);
            CamX = px;
            CamY = py;
        }
        else
        {
            Player.X = entrySide < 0 ? data.Width * 32 + 16 - 40 : 10;
            Player.Jumping = 99;
            Player.Falling = 99;
            CamX = Player.X;
            CamY = Player.Y;
        }
        ScreenX = (int)Math.Floor(Player.X / GameConstants.StageWidth);
        ScreenY = (int)Math.Floor(Player.Y / GameConstants.StageHeight);
        MoveCamera();
        Player.Paint();

        var chunk = Scripting.ScriptLibrary.Get(data.Script);
        Host = chunk != null ? new Scripting.ScriptHost(this, chunk) : null;
        Host?.Fire("start");

        var pchunk = Scripting.ScriptLibrary.Get(string.IsNullOrEmpty(data.PlayerScript) ? DefaultPlayerScript : data.PlayerScript);
        PlayerHost = pchunk != null ? new Scripting.ScriptHost(this, pchunk, null, true) : null;
        PlayerHost?.Fire("start");
        foreach (var e in Entities.ToArray()) AttachScript(e, true);
    }

    public bool HasLevel(string? id) => !string.IsNullOrEmpty(id) && LevelSource != null && LevelSource(id) != null;

    internal void RequestConnect(string level, int side)
    {
        Pending = new LoadRequest(level, LoadKind.Connect, false, side);
    }

    internal void OnWin()
    {
        FireEvent("win");
        Pending = new LoadRequest(Level.Next ?? "", LoadKind.Respawn, true, 0, true);
    }

    internal void ActivateTrigger(Entity e, string target)
    {
        if (e.TypeId == "pipe")
        {
            Mode = WorldMode.Pipe;
            _pipeTarget = target;
            _pipePhase = 0;
            _pipeTimer = 0;
            Player.PipeBegin(Player.X < e.X ? 16 : -16);
        }
        else
        {
            Mode = WorldMode.Frozen;
            Pending = new LoadRequest(target, LoadKind.Respawn, true);
        }
    }

    // ================================================================ helpers used by Player / Entity

    internal void TileAt(double px, double py, out int tile, out int index)
    {
        tile = 0;
        index = -1;
        if (py < 0) return;
        double gx = Math.Floor(px / 32), gy = Math.Floor(py / 32);
        if (gx < 0 || gx >= Level.Width || gy >= Level.Height) return;
        index = (int)gx * Level.Height + (int)gy;
        tile = Tiles[index];
    }

    public void PlaySound(string name) => SoundRequested?.Invoke(name);

    internal void ShowDialog(string text) => DialogRequested?.Invoke(text);

    public void AddParticle(ParticleKind kind, double x, double y) => Particles.Add(new Particle(kind, x, y));

    public void KillPlayer()
    {
        if (Mode == WorldMode.Playing && !GodMode) { _deathQueued = true; FireEvent("death"); }
    }

    internal void BeginLevelComplete()
    {
        if (Mode == WorldMode.Playing) Mode = WorldMode.Complete;
    }

    // ================================================================ tick

    public void Tick(InputState input)
    {
        if (PlayerFrozen) input = default;
        Input = input;
        TickCount++;
        Host?.Tick();
        PlayerHost?.Tick();
        switch (Mode)
        {
            case WorldMode.Playing:
                Player.MovePlayer();
                MoveEntities();
                Player.AfterEnemy();
                TickParticles();
                break;
            case WorldMode.Dying:
                TickDying();
                break;
            case WorldMode.Complete:
                Player.CompleteTick();
                MoveEntities();
                TickParticles();
                break;
            case WorldMode.Pipe:
                TickPipe();
                break;
            case WorldMode.Frozen:
                break;
        }

        if (_deathQueued && Mode == WorldMode.Playing)
        {
            _deathQueued = false;
            Mode = WorldMode.Dying;
            _deathStarted = false;
        }
        if (Pending.HasValue && Mode != WorldMode.Dying) Mode = WorldMode.Frozen;

        if (_stateCmd.Length > 0)
        {
            string cmd = _stateCmd;
            _stateCmd = "";
            if (cmd == "save") SaveState(); else LoadState();
        }
    }

    // ================================================================ level state

    private void SaveState()
    {
        if (Mode != WorldMode.Playing || _deathQueued) { Notice?.Invoke("Can't save state right now"); return; }
        States[LevelId] = CaptureState();
        Notice?.Invoke("State saved");
    }

    private void LoadState()
    {
        if (!States.TryGetValue(LevelId, out var s)) { Notice?.Invoke("No saved state for this level"); return; }
        RestoreState(s);
        Notice?.Invoke("State loaded");
    }

    public WorldSnapshot CaptureState()
    {
        var s = new WorldSnapshot
        {
            LevelId = LevelId, Tiles = (int[])Tiles.Clone(), Coins = Coins, BouncePlayer = BouncePlayer, BumpIndex = BumpIndex,
            ScreenX = ScreenX, ScreenY = ScreenY, CamX = CamX, CamY = CamY, CamXControl = CamXControl, CamYControl = CamYControl,
            PlayerBehindTiles = PlayerBehindTiles, TickCount = TickCount,
            Player = StateCopy.Capture(Player), Flags = new Dictionary<string, string>(Flags),
            LevelGlobals = Host?.CaptureGlobals(), PlayerGlobals = PlayerHost?.CaptureGlobals(),
        };
        foreach (var e in Entities)
        {
            if (e.Removed || e.Def == null) continue;
            s.Entities.Add(new WorldSnapshot.Ent
            {
                Def = e.Def.Clone(), Fields = StateCopy.Capture(e), Globals = e.Script?.CaptureGlobals(), Touching = e.Touching,
            });
        }
        return s;
    }

    public void RestoreState(WorldSnapshot s)
    {
        Tiles = (int[])s.Tiles.Clone();
        Coins = s.Coins; BouncePlayer = s.BouncePlayer; BumpIndex = s.BumpIndex;
        ScreenX = s.ScreenX; ScreenY = s.ScreenY; CamX = s.CamX; CamY = s.CamY; CamXControl = s.CamXControl; CamYControl = s.CamYControl;
        PlayerBehindTiles = s.PlayerBehindTiles; TickCount = s.TickCount;
        StateCopy.Restore(Player, s.Player);
        Player.Paint();
        Flags.Clear();
        foreach (var kv in s.Flags) Flags[kv.Key] = kv.Value;
        Host?.RestoreGlobals(s.LevelGlobals);
        PlayerHost?.RestoreGlobals(s.PlayerGlobals);

        Entities.Clear();
        Particles.Clear();
        foreach (var se in s.Entities)
        {
            var def = se.Def.Clone();
            var e = Entity.Spawn(def, Level.Height, false);
            if (e == null) continue;
            StateCopy.Restore(e, se.Fields);
            e.Touching = se.Touching;
            Entities.Add(e);
            AttachScript(e, false);
            e.Script?.RestoreGlobals(se.Globals);
        }
        Mode = WorldMode.Playing;
        Pending = null;
        _deathQueued = false;
        _deathStarted = false;
        PlayerFrozen = false;
        CamLocked = false; _panTotal = 0; _shakeTicks = 0; ShakeX = ShakeY = 0; CamZoom = 1;
    }

    private void MoveEntities()
    {
        for (int i = 0; i < Entities.Count; i++)
        {
            var e = Entities[i];
            e.Update(this);
            if (e.Script != null && !e.Removed)
            {
                bool touching = e.TouchesPlayer(this);
                if (touching && !e.Touching) e.Script.Fire("touch");
                e.Touching = touching;
                e.Script.Tick();
            }
        }
        for (int i = 0; i < Entities.Count; i++)
            if (Entities[i].Removed && Entities[i].Script is { } sh) { sh.Fire("remove"); sh.Flush(); }
        Entities.RemoveAll(e => e.Removed);
    }

    private void TickParticles()
    {
        for (int i = 0; i < Particles.Count; i++) Particles[i].Tick();
        Particles.RemoveAll(p => p.Dead);
    }

    private void TickDying()
    {
        if (!_deathStarted)
        {
            _deathStarted = true;
            PlaySound("lose_life");
            Player.BeginDeath();
            return;
        }
        bool onScreen = Player.DeathTick();
        TickParticles();
        if (Player.DeathCaught())
        {
            PlaySound("wood_tap");
            Player.Recover();
            Mode = WorldMode.Playing;
            return;
        }
        if (!onScreen)
        {
            Mode = WorldMode.Frozen;
            Pending = new LoadRequest(LevelId, LoadKind.Respawn, false);
        }
    }

    private void TickPipe()
    {
        if (_pipePhase == 0)
        {
            Player.PipeSettleTick();
            _pipeTimer++;
            if (Player.Falling < 1 || _pipeTimer > 90)
            {
                _pipePhase = 1;
                _pipeTimer = 0;
                PlayerBehindTiles = true;
            }
        }
        else
        {
            Player.PipeSinkTick();
            _pipeTimer++;
            if (_pipeTimer >= 20)
            {
                Mode = WorldMode.Frozen;
                Pending = new LoadRequest(_pipeTarget, LoadKind.Respawn, true);
            }
        }
        TickParticles();
    }

    // ================================================================ camera

    internal void MoveCamera()
    {
        var p = Player;
        int mode = Level.CameraMode;
        if (GodMode)
        {
            CamX = p.X;
            CamY += (p.Y - CamY) / 4;
        }
        else
        {
            switch (mode)
            {
                case 0:
                case 7:
                    CamX = p.X;
                    CamY += (p.Y - CamY) / 4;
                    break;
                case 1:
                    CameraScreenMode();
                    break;
                case 2:
                    CamX += CamXControl;
                    CamY += (p.Y - CamY) / 4;
                    break;
                case 3:
                    CamX = p.X;
                    CamY += CamYControl;
                    break;
                case 4:
                    CamX += CamXControl;
                    CamY += CamYControl;
                    break;
                case 5:
                    CamX = p.X;
                    CamY += (p.Y - CamY) / 4;
                    CamY += Math.Sin(TickCount / (double)GameConstants.TicksPerSecond * 40 * Math.PI / 180.0) * 6;
                    break;
                case 6:
                    CamX += (p.X - CamX) / 2;
                    CamY += (p.Y - CamY) / 4;
                    break;
            }
        }
        if (!GodMode && !(mode == 1 || mode == 7))
            LimitCamera(272, 180);
        ApplyScriptCamera();
    }

    private void CameraScreenMode()
    {
        var p = Player;
        if (p.X < CamX - GameConstants.StageWidth / 2.0) ScreenX--;
        if (p.X > CamX + GameConstants.StageWidth / 2.0) ScreenX++;
        if (p.Y < CamY - GameConstants.StageHeight / 2.0) ScreenY--;
        if (p.Y > CamY + GameConstants.StageHeight / 2.0) ScreenY++;
        CamX = ScreenX * GameConstants.StageWidth + GameConstants.StageWidth / 2.0;
        CamY = ScreenY * GameConstants.StageHeight + GameConstants.StageHeight / 2.0;
    }

    private void LimitCamera(double edgeX, double edgeY)
    {
        if (CamX < edgeX) CamX = edgeX;
        if (CamY < edgeY) CamY = edgeY;
        if (CamX > 32.0 * Level.Width - edgeX) CamX = 32.0 * Level.Width - edgeX;
        if (CamY > 32.0 * Level.Height - edgeY) CamY = 32.0 * Level.Height - edgeY;
    }
}
