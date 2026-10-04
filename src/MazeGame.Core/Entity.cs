namespace MazeGame.Core;

public enum EntityKind
{
    Walker,       // tile 29, harmless wanderer ("nomal")
    Danger,       // tile 30, hurts unless stomped / slid into
    Npc,          // tile 46, "useable" star npc that opens a dialog
    EndBox,       // tile 47, level goal (spinning globe)
    EndBoxSpin,   // goal after being touched
    Pole,         // tile 68, piranha plant that pops out of a pipe
    Trigger,      // tiles 77-79, doors and pipes to other levels
    Squish,       // defeated enemy tumbling off screen
    Flip,         // enemy bumped from below (hook, see World.BumpIndex)
}

public enum EntityLayer { BehindTiles, Normal, Front }

/// <summary>
/// One running entity (a "clone" of the original Enemy sprite). Costume numbers refer to the original
/// "Enemy" sprite costume list.
/// </summary>
public sealed class Entity
{
    public const int CosBig = 1, CosPole = 2, CosPoleHitbox = 3, CosPipeTrigger = 4, CosDoorTrigger = 5, CosDoorWide = 6,
        CosEndGlobeStatic = 7, CosWalk1 = 8, CosDanger1 = 12, CosGlobe1 = 16, CosDangerDie = 43, CosDangerDie2 = 44;

    private const double T = GameConstants.Tiny;

    public EntityKind Kind;
    public int TileType;
    public double X, Y;
    public double PrevX = double.NaN, PrevY = double.NaN;   // NaN = not yet ticked (use current)
    public double Width, Height;          // half extents
    public double Dir = 90;
    public double SpeedX, SpeedY;
    public int Falling, Jumping;
    public double Frame;
    public int Costume = 1;
    public double SizePct = 200;
    public string Extra = "";
    public string Param = "";
    public double RotationDegrees;
    public bool AllAround;
    public double LastDir;
    public bool Removed;
    public bool Visible = true;
    public EntityLayer Layer = EntityLayer.Normal;

    private int _solid;
    private double _fixDx, _fixDy, _modX, _modY, _offsetY;
    private int _tile;
    private string _tileShape = "";
    private int _tileIndex;

    public bool FlipX => !AllAround && Dir < 0;

    // ======================================================================== spawning

    public static Entity? Spawn(EntityDef d, int levelHeight, bool editor)
    {
        double cellX = d.X * 32, cellY = d.Y * 32;
        var e = new Entity { TileType = d.Type, Param = d.Param };
        switch (d.Type)
        {
            case TileInfo.Walker:
                e.Kind = EntityKind.Walker; e.Costume = CosWalk1; e.Width = 12; e.Height = 18;
                e.X = cellX + 16; e.Y = cellY + e.Height; break;
            case TileInfo.Danger:
                e.Kind = EntityKind.Danger; e.Costume = CosDanger1; e.Width = 12; e.Height = 18;
                e.X = cellX + 16; e.Y = cellY + e.Height; break;
            case TileInfo.Star:
                e.Kind = EntityKind.Npc; e.Costume = CosDanger1; e.Width = 12; e.Height = 18;
                e.X = cellX + 16; e.Y = cellY + e.Height;
                e.Extra = string.IsNullOrEmpty(d.Param) ? "hello" : LevelData.DecodeText(d.Param);
                if (e.Extra.Length == 0) e.Extra = "hello";
                break;
            case TileInfo.EndBox:
                e.Kind = EntityKind.EndBox; e.Costume = CosEndGlobeStatic; e.Width = 16; e.Height = 16;
                e.X = cellX + 16; e.Y = cellY + 16; e.SizePct = 20; break;
            case TileInfo.Piranha:
                e.Kind = EntityKind.Pole; e.Costume = CosPole; e.Width = 16; e.Height = 32;
                e.X = cellX + 16 + 16; e.Y = cellY + 32;
                if (!editor) e.Y -= 64;
                e.Layer = EntityLayer.BehindTiles; break;
            case TileInfo.DoorTrigger:
                e.Kind = EntityKind.Trigger; e.Costume = CosDoorTrigger; e.Width = 16; e.Height = 8;
                e.X = cellX + 16; e.Y = cellY + 8 + 10; e.Extra = "T-DO-1"; break;
            case TileInfo.PipeTrigger:
                e.Kind = EntityKind.Trigger; e.Costume = CosPipeTrigger; e.Width = 16; e.Height = 32;
                e.X = cellX + 16 + 16; e.Y = cellY + 32 - 16; e.Extra = "T-PI-1"; break;
            case TileInfo.DoorWideTrigger:
                e.Kind = EntityKind.Trigger; e.Costume = CosDoorWide; e.Width = 32; e.Height = 32;
                e.X = cellX + 16; e.Y = cellY + 32 - 16; e.Extra = "T-DW-1"; break;
            default:
                return null;
        }
        if (e.Kind == EntityKind.Trigger) e.Visible = editor;   // invisible in game (ghost 100)
        return e;
    }

    // ======================================================================== helpers

    private static double Mod(double a, double b)
    {
        double r = a % b;
        return (r != 0 && (r < 0) != (b < 0)) ? r + b : r;
    }

    private void GetTile(World w, double px, double py)
    {
        w.TileAt(px, py, out _tile, out _tileIndex);
        _tileShape = TileInfo.Shape(_tile);
    }

    /// <summary>Hit test against the player's box. <paramref name="scale"/> shrinks this entity's box.</summary>
    public bool TouchesPlayer(World w, double scale = 1.0)
    {
        var p = w.Player;
        return Math.Abs(p.X - X) < p.Width + Width * scale && Math.Abs(p.Y - Y) < p.Height + Height * scale;
    }

    private void FixCollisionAtPoint(World w, double px, double py, bool feet)
    {
        GetTile(w, px, py);
        if (_tileShape.Length == 0) return;
        _modX = Mod(px, 32);
        _modY = Mod(py, 32);
        char t = _tileShape[0];
        if (t == '\\' || t == '/')
        {
            double m = 1.0 / (_tileShape[1] - '0');
            double c = (_tileShape[2] - '0') * (32 * m);
            if (t == '\\') { m = -m; c = 32 - c; }
            _offsetY = _modY - (_modX * m + c);
            if (!(_offsetY < 0)) return;
            if (_fixDy < 0) { Y += -_offsetY; _solid = 10; return; }
            if (_fixDy == 0 && ((_fixDx > 0) == (m > 0))) { X += _offsetY / m; _solid = 10; return; }
        }
        if (_tileShape == "=")
        {
            if (!feet || (_modY - _fixDy) < 32) return;
        }
        _solid = 10;
        if (_fixDy < 0) Y += 32 - _modY;
        if (_fixDx < 0) X += 32 - _modX;
        if (_fixDy > 0) Y += T - _modY;
        if (_fixDx > 0) X += T - _modX;
    }

    private void FixCollisionInDirection(World w, double dx, double dy)
    {
        _fixDx = dx; _fixDy = dy; _solid = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            FixCollisionAtPoint(w, X - Width, Y - Height, true);
            FixCollisionAtPoint(w, X - Width, Y, false);
            FixCollisionAtPoint(w, X - Width, Y + Height, false);
            FixCollisionAtPoint(w, X + (Width + T), Y - Height, true);
            FixCollisionAtPoint(w, X + (Width + T), Y, false);
            FixCollisionAtPoint(w, X + (Width + T), Y + Height, false);
            if (_solid < 1) return;
        }
    }

    private void MoveSpriteX(World w)
    {
        double origY = Y;
        X += SpeedX;
        FixCollisionInDirection(w, 0, -1);
        if (Y > origY + (Math.Abs(SpeedX) + 4))
        {
            Y = origY;
            FixCollisionInDirection(w, SpeedX, 0);
            if (_solid > 0)
            {
                SpeedX = 0;
                Dir = Dir > 0 ? -90 : 90;      // turn around at walls
            }
        }
        else if (Y > origY)
        {
            SpeedX *= 0.85;
        }
    }

    private void MoveSpriteY(World w)
    {
        Y += SpeedY;
        Falling++;
        FixCollisionInDirection(w, 0, SpeedY);
        if (_solid > 0)
        {
            if (SpeedY < 0) Falling = 0; else Jumping = 99;
            SpeedY = 0;
        }
    }

    // ======================================================================== behaviour

    /// <summary>"when I receive move enemy" for this clone.</summary>
    public void Update(World w)
    {
        switch (Kind)
        {
            case EntityKind.Trigger: UpdateTrigger(w); break;
            case EntityKind.Pole: UpdatePole(w); break;
            case EntityKind.Flip: UpdateFlip(w); break;
            case EntityKind.Squish: UpdateSquish(w); break;
            case EntityKind.EndBoxSpin: UpdateEndBoxSpin(w); break;
            case EntityKind.EndBox: UpdateEndBox(w); break;
            case EntityKind.Npc: UpdateNpc(w); break;
            case EntityKind.Walker: TickNpcBase(w); Frame += 0.1; Costume = CosWalk1 + (int)Math.Floor(Mod(Frame, 4)); break;
            case EntityKind.Danger: TickNpcBase(w); UpdateDanger(w); break;
        }
    }

    private void TickNpcBase(World w)
    {
        SpeedY += -1;
        MoveSpriteY(w);
        if (Dir > 0) { if (SpeedX < 2) SpeedX += 2.5; }
        else { if (SpeedX > -2) SpeedX += -2.5; }
        MoveSpriteX(w);
        CheckFlip(w);
        GetTile(w, X + (Dir / 90) * 32, Y);
        if (_tileShape == "#")
        {
            GetTile(w, X + (Dir / 90) * 32, Y + 32);
            if (Falling < 5 && _tileShape.Length == 0)
                SpeedY = 9;       // hop up one-tile ledges
        }
    }

    private void CheckFlip(World w)
    {
        if (w.BumpIndex < 1) return;
        GetTile(w, X - Width, Y - Height - 8);
        if (_tileIndex + 1 != w.BumpIndex)
        {
            GetTile(w, X + Width, Y - Height - 8);
            if (_tileIndex + 1 != w.BumpIndex) return;
        }
        SpeedY = 14;
        SpeedX = w.Player.Dir / 45;
        Kind = EntityKind.Flip;
        AllAround = true;
        Dir = -90;
        RotationDegrees = 180;
    }

    private void UpdateDanger(World w)
    {
        Frame += 0.1;
        Costume = CosDanger1 + (int)Math.Floor(Mod(Frame, 4));
        if (!TouchesPlayer(w, 0.8)) return;

        var p = w.Player;
        bool slidingInto = (p.Action == "slide" || p.Action == "crouch") && Math.Abs(p.SpeedX) > 1;
        if (p.BopY.HasValue || slidingInto)
        {
            bool slide = p.Action == "slide" || p.Action == "crouch";
            if (!slide)
            {
                SpeedX = 0;
                w.PlaySound("stomped");
                w.BouncePlayer = 5;
            }
            else
            {
                SpeedX = (p.Dir / 90) * 3;
                w.PlaySound("tennis_hit");
            }
            w.AddParticle(ParticleKind.Score100, X, Y);
            Die();
        }
        else
        {
            w.KillPlayer();
        }
    }

    private void Die()
    {
        LastDir = Dir;
        Kind = EntityKind.Squish;
        SpeedY = 15;
        AllAround = true;
        Dir = 90;
        RotationDegrees = 0;
        Costume = LastDir > 0 ? CosDangerDie2 : CosDangerDie;
        Layer = EntityLayer.Normal;
    }

    private void UpdateSquish(World w)
    {
        if (!(SpeedY > 0)) Layer = EntityLayer.Front;
        if (LastDir > 0) RotationDegrees -= 8; else RotationDegrees += 8;
        SpeedY += -1;
        Y += SpeedY;
        X += SpeedX;
        if (Y < -200) Removed = true;
    }

    private void UpdateFlip(World w)
    {
        X += SpeedX;
        SpeedY += -1;
        Y += SpeedY;
        if (Y < w.CamY - 200) Removed = true;
    }

    private void UpdateNpc(World w)
    {
        Frame += 0.1;
        Costume = CosDanger1 + (int)Math.Floor(Mod(Frame, 4));
        if (w.Mode != WorldMode.Playing) return;
        var p = w.Player;
        if (TouchesPlayer(w, 0.9) && !(p.Action == "slide" || p.Action == "crouch") && w.Input.UsePressed)
        {
            w.PlaySound("stomped");
            w.ShowDialog(Extra);
        }
    }

    private void UpdatePole(World w)
    {
        Frame += 1;
        Costume = CosPole;
        double temp = Mod(Frame / 16, 8);
        if (Math.Floor(temp) == 4) Y += 4;
        if (Math.Floor(temp) == 7) Y += -4;
        if (temp > 4.2 && w.Mode == WorldMode.Playing)
        {
            // pole_hitbox costume is narrow (about 7 wide) and tall
            if (Math.Abs(w.Player.X - X) < w.Player.Width + 4 && Math.Abs(w.Player.Y - Y) < w.Player.Height + 29)
                w.KillPlayer();
        }
    }

    private void UpdateEndBox(World w)
    {
        SizePct = 20;
        Frame += 0.15 * 7.8;
        Costume = CosGlobe1 + (int)(Math.Floor(Frame) % 24);
        if (w.Mode != WorldMode.Playing) return;
        var p = w.Player;
        double d = Math.Sqrt((p.X - X) * (p.X - X) + (p.Y - Y) * (p.Y - Y));
        if (TouchesPlayer(w) && d < 28)
        {
            w.BeginLevelComplete();
            Kind = EntityKind.EndBoxSpin;
            SpeedY += 12;
        }
    }

    private void UpdateEndBoxSpin(World w)
    {
        Frame += 0.15 * 8.8;
        Costume = CosGlobe1 + (int)(Math.Floor(Frame) % 24);
        if (!(SpeedY > 0))
        {
            Layer = EntityLayer.Front;
            SpeedY += -1.2;
            if (SizePct < 250) SizePct += 0.2;
        }
        else
        {
            SpeedY += -.4;
        }
        Y += SpeedY;
        X += SpeedX;
        if (Y < -200)
        {
            Removed = true;
            w.OnWin();
        }
    }

    private void UpdateTrigger(World w)
    {
        if (w.Mode != WorldMode.Playing) return;
        if (!TouchesPlayer(w) || !w.Input.UsePressed) return;
        if (!int.TryParse(Param, out int target) || !w.HasLevel(target)) return;
        w.ActivateTrigger(this, target);
    }
}
