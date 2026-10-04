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
public readonly record struct LoadRequest(int Level, LoadKind Kind, bool Wipe, int EntrySide = 0, bool Win = false);

/// <summary>
/// One running level: tiles, player, entities, particles and camera. Call <see cref="Tick"/> 30 times a second
/// with the current <see cref="InputState"/>. Mirrors the original "game loop":
/// controls, move player, move enemy, move player after enemy, position tiles.
/// </summary>
public sealed class World
{
    public LevelData Level { get; private set; }
    public int LevelNumber { get; private set; }
    /// <summary>Live tile grid (column-major, copy of the level so picked-up gems can be removed).</summary>
    public int[] Tiles { get; private set; } = Array.Empty<int>();

    public Player Player { get; }
    public List<Entity> Entities { get; } = new();
    public List<Particle> Particles { get; } = new();

    public double CamX, CamY;
    public double PrevCamX, PrevCamY;

    /// <summary>0..1 position between the previous and current logic tick, set by the game loop each frame.</summary>
    public double Alpha = 1;

    /// <summary>Call immediately before every logic tick (and after loads/teleports) so rendering can interpolate.</summary>
    public void SavePrevious()
    {
        PrevCamX = CamX; PrevCamY = CamY;
        Player.PrevX = Player.X; Player.PrevY = Player.Y;
        foreach (var e in Entities) { e.PrevX = e.X; e.PrevY = e.Y; }
        foreach (var p in Particles) { p.PrevX = p.X; p.PrevY = p.Y; }
    }

    /// <summary>Interpolated value for rendering. Big jumps (teleports, respawns) are not smoothed.</summary>
    public double Lerp(double prev, double cur)
    {
        if (double.IsNaN(prev) || Math.Abs(cur - prev) > 64) return cur;
        return prev + (cur - prev) * Alpha;
    }
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

    public Func<int, LevelData?>? LevelSource;

    public event Action<string>? SoundRequested;
    public event Action<string>? DialogRequested;

    private bool _deathQueued;
    private bool _deathStarted;
    private int _pipeTarget, _pipePhase, _pipeTimer;

    public World(LevelData level, int levelNumber, Func<int, LevelData?>? source = null)
    {
        LevelSource = source;
        Player = new Player(this);
        Level = level;
        Load(level, levelNumber, LoadKind.Respawn, 0);
    }

    public int Width => Level.Width;
    public int Height => Level.Height;

    // ================================================================ loading

    public void Load(LevelData data, int number, LoadKind kind, int entrySide)
    {
        Level = data;
        LevelNumber = number;
        Tiles = (int[])data.Tiles.Clone();

        int spawnIndex = Array.IndexOf(Tiles, TileInfo.PlayerSpawn);
        if (spawnIndex >= 0 && !GodMode) Tiles[spawnIndex] = TileInfo.Air;

        Entities.Clear();
        Particles.Clear();
        foreach (var def in data.Entities)
        {
            var e = Entity.Spawn(def, data.Height, false);
            if (e != null) Entities.Add(e);
        }

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
    }

    public bool HasLevel(int n) => n >= GameConstants.FirstLevel && LevelSource != null && LevelSource(n) != null;

    internal void RequestConnect(int level, int side)
    {
        Pending = new LoadRequest(level, LoadKind.Connect, false, side);
    }

    internal void OnWin()
    {
        Pending = new LoadRequest(LevelNumber + 1, LoadKind.Respawn, true, 0, true);
    }

    internal void ActivateTrigger(Entity e, int target)
    {
        if (e.TileType == TileInfo.PipeTrigger)
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

    internal void AddParticle(ParticleKind kind, double x, double y) => Particles.Add(new Particle(kind, x, y));

    internal void KillPlayer()
    {
        if (Mode == WorldMode.Playing && !GodMode) _deathQueued = true;
    }

    internal void BeginLevelComplete()
    {
        if (Mode == WorldMode.Playing) Mode = WorldMode.Complete;
    }

    // ================================================================ tick

    public void Tick(InputState input)
    {
        Input = input;
        TickCount++;
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
    }

    private void MoveEntities()
    {
        for (int i = 0; i < Entities.Count; i++)
            Entities[i].Update(this);
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
            Pending = new LoadRequest(LevelNumber, LoadKind.Respawn, false);
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
