namespace MazeGame.Core;

public enum RotationStyle { LeftRight, AllAround, DontRotate }

/// <summary>
/// Faithful port of the "player" sprite: movement, jumping, crouch/slide, ladders, swimming and
/// tile collision (including 1:1, 1:2 slopes and one-way platforms). Variable names follow the
/// original Scratch variables so the two can be compared side by side.
/// </summary>
public sealed class Player
{
    // Costume numbers (1-based, same order as the original "player" sprite costumes).
    public const int CosWalk1 = 1, CosTurn = 5, CosCrouch = 7, CosAirCrouch = 8, CosLoseLife = 9, CosLoseLife2 = 10,
        CosSlide4 = 14, CosSlide6 = 16, CosLadderUp1 = 18, CosLadderUp2 = 19, CosSwim1 = 20, CosSwim4 = 23;

    private const double T = GameConstants.Tiny;

    private readonly World _w;

    public double X, Y;
    public double SpeedX, SpeedY;
    public double Width = 8, Height = 18;      // half extents of the hit box
    public int Falling = 99, Jumping = 99;
    public string Action = "";                 // '', walk, crouch, aircrouch, slide, turn, ladder*, swim, swim_walk, lose life
    public string ForcedAction = "";           // player_action_forced
    public int AllForced;                      // 'player all forsed'
    public double Dir = 90;
    public double Frame;                       // player frame (animation counter)
    public int KeyWalk;
    public double? BopY;                       // set while falling fast: enables stomping
    public int Timer1;                         // slide timer
    public double PlayerSpeedX, PlayerSpeedY;  // external pushes applied after enemies
    public double LastDir;
    public bool Visible = true;

    // outputs for the renderer
    public int CostumeIndex = 1;
    public RotationStyle Rotation = RotationStyle.LeftRight;
    public double RotationDegrees;             // used with AllAround
    public bool FlipX => Rotation == RotationStyle.LeftRight && Dir < 0;

    // scratch variables used by the collision routines
    private int _solid;
    private double _fixDx, _fixDy, _modX, _modY, _offsetY;
    private char _temp;                        // first letter of the last tile shape that was hit ($temp)
    private int _tile;
    private string _tileShape = "";
    private int _tileIndex = -1;

    public Player(World w) { _w = w; }

    private bool Swim => _w.Level.Underwater;
    private InputState In => _w.Input;

    public void Reset(double x, double y)
    {
        X = x; Y = y;
        Width = 8; Height = 18;
        Action = ""; ForcedAction = ""; AllForced = 0;
        SpeedX = SpeedY = 0; PlayerSpeedX = PlayerSpeedY = 0;
        Jumping = 99; Falling = 99;
        Dir = 90; Frame = 0; Timer1 = 0; BopY = null;
        Visible = true; Rotation = RotationStyle.LeftRight; RotationDegrees = 0;
        Paint();
    }

    // ================================================================ tile access

    /// <summary>"get tile at x y": sets _tile, _tileShape and _tileIndex (0-based into World.Tiles or -1).</summary>
    internal void GetTile(double px, double py)
    {
        _w.TileAt(px, py, out _tile, out _tileIndex);
        _tileShape = TileInfo.Shape(_tile);
    }

    private static double Mod(double a, double b)
    {
        double r = a % b;
        return (r != 0 && (r < 0) != (b < 0)) ? r + b : r;
    }

    // ================================================================ per tick

    public void MovePlayer()
    {
        _w.BumpIndex = 0;
        if (_w.GodMode)
        {
            HandleGodMode();
        }
        else
        {
            if (_w.Level.Width * 32 + 16 < X) X = _w.Level.Width * 32 + 16 - 20;
            BopY = (Falling > 2 && SpeedY < -1) ? Y : null;

            if (Swim || Action != "slide")
            {
                HandleKeysLeftRight();
                HandleKeysJumpCrouch();
            }
            else
            {
                HandleKeysJumpCrouchSlide();
                HandleKeysLeftRightSlide();
            }
            MoveSpriteX();
            MoveSpriteY();
            CheckAroundPlayer();
            HandleLevelTransition();
        }
        Paint();
    }

    /// <summary>"move player after enemy": bounce off stomped enemies, apply pushes, camera.</summary>
    public void AfterEnemy()
    {
        if (_w.BouncePlayer > 0)
        {
            _w.BouncePlayer--;
            SpeedY = 13;
            Falling = 2;
            Jumping = 1;
        }
        if (Math.Abs(PlayerSpeedY) > 0) { SpeedY += PlayerSpeedY; PlayerSpeedY = 0; }
        if (Math.Abs(PlayerSpeedX) > 0) { SpeedX += PlayerSpeedX; PlayerSpeedX = 0; }
        _w.MoveCamera();
        Paint();
    }

    private void HandleGodMode()
    {
        Action = "";
        SpeedX += 7 * In.XAxis;
        SpeedY += 7 * In.YAxis;
        SpeedX *= 0.7;
        SpeedY *= 0.7;
        X += SpeedX;
        Y += SpeedY;
    }

    // ================================================================ collision

    private void FixCollisionAtPoint(double px, double py, bool feet)
    {
        GetTile(px, py);
        if (_tileShape.Contains('L') ||
            (_tileShape.Contains('R') && (In.Down || Action.Contains("ladder"))))
            return;
        if (_tileShape.Length == 0) return;

        _modX = Mod(px, 32);
        _modY = Mod(py, 32);
        char t = _tileShape[0];
        _temp = t;

        if (t == '\\' || t == '/')
        {
            double m = 1.0 / (_tileShape[1] - '0');
            double c = (_tileShape[2] - '0') * (32 * m);
            if (t == '\\') { m = -m; c = 32 - c; }
            _offsetY = _modY - (_modX * m + c);
            if (Action == "crouch")
            {
                Action = "slide";
                SpeedX = (Dir / 90) * 12;
                SpeedY = -2;
            }
            if (!(_offsetY < 0)) return;
            if (_fixDy < 0)
            {
                Y += -_offsetY;
                _solid = 10;
                return;
            }
            if (_fixDy == 0 && ((_fixDx > 0) == (m > 0)))
            {
                X += _offsetY / m;
                _solid = 10;
                return;
            }
        }

        if (_tileShape == "=")
        {
            if (!feet || (_modY - _fixDy) < 32) return;
            if (In.Down) return;
        }

        _solid = 10;
        if (_fixDy < 0) { AllForced = 0; Y += 32 - _modY; }
        if (_fixDx < 0) X += 32 - _modX;
        if (_fixDy > 0) { AllForced = 0; Y += T - _modY; }
        if (_fixDx > 0) X += T - _modX;
    }

    private void FixCollisionInDirection(double dx, double dy)
    {
        _fixDx = dx;
        _fixDy = dy;
        _solid = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            FixCollisionAtPoint(X - Width, Y - Height, true);
            FixCollisionAtPoint(X + (Width + T), Y - Height, true);
            FixCollisionAtPoint(X - Width, Y, false);
            FixCollisionAtPoint(X + (Width + T), Y, false);
            if (!(Action == "crouch" || Action == "aircrouch"))
            {
                FixCollisionAtPoint(X - Width, Y + Height, false);
                FixCollisionAtPoint(X + (Width + T), Y + Height, false);
            }
            if (_solid < 1) return;
        }
    }

    private void MoveSpriteX()
    {
        double origY = Y;
        X += SpeedX;
        FixCollisionInDirection(0, -1);
        if (Y > origY + (Math.Abs(SpeedX) + 4))
        {
            Y = origY;
            FixCollisionInDirection(SpeedX, 0);
            if (_solid > 0) SpeedX = 0;
        }
        else if (Y > origY)
        {
            SpeedX *= 0.85;
        }
    }

    private void MoveSpriteY()
    {
        Y += SpeedY;
        Falling++;
        FixCollisionInDirection(0, SpeedY);
        if (_solid > 0)
        {
            if (AllForced == 0) ForcedAction = "";
            if (SpeedY < 0)
            {
                Falling = 0;
            }
            else
            {
                Jumping = 99;
                BumpHead();
            }
            SpeedY = 0;
        }
    }

    /// <summary>
    /// The original calls an undefined "bump head" procedure here (so it did nothing). Kept as a hook:
    /// set <see cref="World.BumpIndex"/> to a tile index and enemies standing on it get flipped.
    /// </summary>
    private void BumpHead() { }

    // ================================================================ keys

    private void MakeSkidSmoke()
    {
        Frame += 1;
        if (Mod(Frame, 3) < 1)
        {
            _w.AddParticle(ParticleKind.Smoke, X, Y - 20);
            _w.PlaySound("shaker");
        }
    }

    private void HandleGetUp()
    {
        Action = "";
        double origY = Y;
        FixCollisionInDirection(0, 1);
        if (_solid > 0)
        {
            Action = "crouch";
            Y = origY;
        }
    }

    private void MoveWalk()
    {
        GetTile(X, Y);   // ladder_check
        if (KeyWalk == 0)
        {
            if (Falling < 2)
            {
                if (SpeedX > .8) SpeedX += -1;
                else if (SpeedX < -.8) SpeedX += 1;
                else { SpeedX = 0; Frame = 0; }
            }
        }
        else
        {
            Dir = 90 * KeyWalk;
            if (KeyWalk * SpeedX < 10)
            {
                if (KeyWalk * SpeedX < 0)
                {
                    SpeedX += KeyWalk * .8;
                    if (Falling < 2 && !Swim)
                    {
                        Action = "turn";
                        MakeSkidSmoke();
                    }
                }
                else
                {
                    SpeedX += Swim ? KeyWalk * 1.2 : KeyWalk * .8;
                }
            }
        }
        double temp = Math.Abs(SpeedX) / (Swim ? 30 : 19);
        _temp = '\0';
        if (!Action.Contains("ladder"))
        {
            if (temp < 0.2) temp = 0.2;
            Frame += temp;
        }
    }

    private void MoveCrouch()
    {
        if (KeyWalk == 0)
        {
            if (Falling < 2)
            {
                if (SpeedX > .8) SpeedX += -1;
                else if (SpeedX < -.8) SpeedX += 1;
                else { SpeedX = 0; Frame = 0; }
            }
        }
        else
        {
            Dir = 90 * KeyWalk;
            if (KeyWalk * SpeedX < 10)
            {
                if (KeyWalk * SpeedX < 0)
                {
                    SpeedX += KeyWalk * .8;
                    if (Falling < 2)
                    {
                        Action = "turn";
                        MakeSkidSmoke();
                    }
                }
                else
                {
                    SpeedX += KeyWalk * 1.2;
                }
            }
        }
        double temp = Math.Abs(SpeedX) / 19;
        _temp = '\0';
        if (temp < 0.2) temp = 0.2;
        Frame += temp;
    }

    private void HandleKeysLeftRight()
    {
        if (Action == "crouch")
        {
            if (Math.Abs(SpeedX) > 0.4)
            {
                SpeedX += -.8 * (Math.Abs(SpeedX) / SpeedX);
                if (!Swim) MakeSkidSmoke();
            }
            else
            {
                SpeedX = 0;
                Frame = 0;
                KeyWalk = In.XAxis;
                MoveCrouch();
            }
        }
        else
        {
            if (Swim)
            {
                if (Falling < 1) Action = "swim_walk";
            }
            else
            {
                Action = "walk";
            }
            KeyWalk = In.XAxis;
            MoveWalk();
        }
    }

    private void HandleKeysLeftRightSlide()
    {
        if (Math.Abs(SpeedX) > 0.4)
        {
            MakeSkidSmoke();
        }
        else
        {
            Frame = 0;
            KeyWalk = In.XAxis;
            if (SpeedX > 11) SpeedX = 11;
            else if (SpeedX < -11) SpeedX = -11;
            else Frame = 0;
        }
        GetTile(X, Y - 8);
        char first = _tileShape.Length > 0 ? _tileShape[0] : '\0';
        if (first == '\\') SpeedX += 3.5;
        if (first == '/') SpeedX += -3.5;
        if (SpeedX > 11) SpeedX = 11;
        if (SpeedX < -11) SpeedX = -11; else Frame = 0;
        if (SpeedX > 6) Dir = 90;
        if (SpeedX < -6) Dir = -90;
    }

    private void HandleKeysJumpCrouch()
    {
        GetTile(X, Y - 32);
        if (_tileShape == "#" && Falling == 1)
        {
            Rotation = RotationStyle.LeftRight;
            if (In.Down && AllForced == 0)
            {
                ForcedAction = "";
            }
            else if (!Swim)
            {
                Action = "walk";
                if (AllForced == 0) ForcedAction = "walk";
            }
        }
        else if (AllForced == 0)
        {
            ForcedAction = "";
        }

        if (Swim)
        {
            WaterMovement();
            return;
        }

        GetTile(X, Y - 16);
        if ((Action == "ladder" || _tileShape == "L" || _tileShape == "R") &&
            (_tileShape.Length > 0 || Action != "crouch"))
            LadderMovement();
        else
            JumpCrouch();
    }

    private void HandleKeysJumpCrouchSlide()
    {
        GetTile(X, Y - 8);
        if (_temp == '#')
        {
            if ((Timer1 > 0 && Timer1 < 12) && In.Up)
            {
                if (Dir > SpeedX) { SpeedY = 18; SpeedX = 18; }
                else if (Dir < SpeedX) { SpeedY = 18; SpeedX = -18; }
                Timer1 = 0;
                ForcedAction = "aircrouch";
                Action = "aircrouch";
                return;
            }
            Timer1++;
            if (Timer1 > 11)
            {
                Timer1 = 0;
                Action = "crouch";
                HandleKeysJumpCrouch();
                return;
            }
        }
        else
        {
            Timer1 = 0;
        }
        SpeedY += -2;
        if (SpeedY < -22) SpeedY = -22;
        if (In.Down)
        {
            Jumping = 0;
        }
        else if (Action == "crouch")
        {
            HandleGetUp();
        }
        else if (In.Up)
        {
            Action = "walk";
            if (Falling < 2 || Jumping > 0)
            {
                Jumping += 2;
                if (Jumping < 11)
                {
                    SpeedY = 13;
                    if (Jumping == 1) _w.PlaySound("jump");
                }
            }
        }
        else
        {
            Jumping = 0;
        }
    }

    private void JumpCrouch()
    {
        SpeedY += -2;
        if (SpeedY < -22) SpeedY = -22;
        if (!(Jumping > 0) && In.Down)
        {
            if (Falling > 0) Action = "aircrouch";
            if (Falling < 2) Action = "crouch";
            if (In.Up)
            {
                ForcedAction = "aircrouch";
                AllForced = 1;
                if (Falling < 2 || Jumping > 0)
                {
                    Jumping += 2;
                    if (Jumping < 11)
                    {
                        SpeedY = 30;       // crouch + jump = super jump
                        if (Jumping == 1) _w.PlaySound("jump");
                    }
                }
            }
            else
            {
                ForcedAction = "aircrouch";
                Jumping = 0;
            }
        }
        else
        {
            if (Action == "crouch")
            {
                HandleGetUp();
            }
            else if (In.Up)
            {
                if (Falling < 2 || Jumping > 0)
                {
                    Jumping += 2;
                    if (Jumping < 11)
                    {
                        SpeedY = 13;
                        if (Jumping == 1) _w.PlaySound("jump");
                    }
                }
            }
            else
            {
                Jumping = 0;
            }
        }
    }

    private void SnapTo(bool useX, bool useY)
    {
        if (useX) X = Math.Floor(X / 32) * 32 + 16;
        if (useY) Y = Math.Floor(Y / 32) * 32 + 16;
    }

    private void LadderMovement()
    {
        if (!(Action == "aircrouch" || ForcedAction == "aircrouch"))
        {
            if (In.YAxis < 0)
            {
                if (In.XAxis == 0 && Action != "crouch")
                {
                    SnapTo(true, false);
                    SpeedX = 0;
                }
                Frame += 0.5;
                if (!(Action == "lose life" || Action == "wark" || (Action == "crouch" && _tileShape != "R")))
                {
                    ForcedAction = "";
                    Action = "ladder-down";
                }
                Jumping = 0;
                SpeedY = -6;
                return;
            }
            if (In.YAxis > 0)
            {
                if (In.XAxis == 0 && Action != "crouch")
                {
                    SnapTo(true, false);
                    SpeedX = 0;
                }
                Frame += 0.5;
                if (!(Action == "lose life" || Action == "wark" || Action == "aircrouch" || ForcedAction == "aircrouch"))
                {
                    ForcedAction = "";
                    Action = "ladder-up";
                }
                Jumping = 0;
                SpeedY = 6;
                return;
            }
        }
        if (!(Action == "lose life" || Action == "wark" || (Action == "crouch" && _tileShape != "R")))
            Action = "ladder";
        if (!(Action == "aircrouch" || ForcedAction == "aircrouch"))
        {
            SpeedY = 0;
            Jumping = 0;
            Falling = 0;
        }
    }

    private void WaterMovement()
    {
        SpeedY += -1.5;
        if (SpeedY < -5) SpeedY = -5;
        if (In.Up)
        {
            Action = "swim";
            if (Jumping == 0)
            {
                Jumping += 2;
                if (Jumping < 11) SpeedY = 15;
            }
        }
        else
        {
            Jumping = 0;
        }
    }

    // ================================================================ environment

    private void CheckAroundPlayer()
    {
        if (Y < 0 && _w.Level.CameraMode != 7)
            _w.KillPlayer();
        CollectAt(X, Y - 8);
        CollectAt(X, Y + 8);
    }

    private void CollectAt(double px, double py)
    {
        GetTile(px, py);
        if (TileInfo.IsGem(_tile) && _tileIndex >= 0)
        {
            _w.Tiles[_tileIndex] = TileInfo.Air;
            _w.Coins += TileInfo.GemValue(_tile);
            _w.PlaySound("coin");
        }
    }

    private void HandleLevelTransition()
    {
        if (X > -100 && X < 10 && _w.HasLevel(_w.LevelNumber - 1))
        {
            _w.RequestConnect(_w.LevelNumber - 1, -1);
        }
        else if (X > 32 * _w.Level.Width && X < 32 * _w.Level.Width + 100 && _w.HasLevel(_w.LevelNumber + 1))
        {
            _w.RequestConnect(_w.LevelNumber + 1, +1);
        }
    }

    // ================================================================ pipe + death + completion helpers

    /// <summary>Called by the world while the pipe sequence waits for the player to settle.</summary>
    internal void PipeSettleTick()
    {
        HandleKeysJumpCrouch();
        MoveSpriteX();
        MoveSpriteY();
        Paint();
    }

    internal void PipeSinkTick()
    {
        Y += -3;
        Frame += 0.6;
        Paint();
    }

    internal void PipeBegin(double offsetX)
    {
        SnapTo(true, false);
        X += offsetX;
    }

    internal void CompleteTick()
    {
        SpeedY += -1;
        SpeedX = 0;
        MoveSpriteX();
        MoveSpriteY();
        Paint();
    }

    internal void BeginDeath()
    {
        LastDir = Dir;
        Action = "lose life";
        SpeedY = 18.23;
        Rotation = RotationStyle.AllAround;
        Dir = 90;
        CostumeIndex = LastDir > 0 ? CosLoseLife : CosLoseLife2;
        RotationDegrees = 0;
    }

    /// <summary>One step of the lose-life animation. Returns false once the player has fallen off screen.</summary>
    internal bool DeathTick()
    {
        Rotation = RotationStyle.AllAround;
        if (LastDir > 0) { X += -2; Dir -= 8; }
        else { X += 2; Dir += 8; }
        RotationDegrees = Dir - 90;
        SpeedY += -1;
        Y += SpeedY;
        CostumeIndex = LastDir > 0 ? CosLoseLife : CosLoseLife2;
        return !(Y - _w.CamY < -220);
    }

    /// <summary>Tile 76 ("logic_player_death") catches a dying player and puts them back into play.</summary>
    internal bool DeathCaught()
    {
        GetTile(X, Y);
        return _tile == TileInfo.LogicDeath;
    }

    internal void Recover()
    {
        Rotation = RotationStyle.LeftRight;
        RotationDegrees = 0;
        Dir = 90;
        Action = "";
        SpeedX = SpeedY = 0;
        Falling = 99;
        Jumping = 99;
    }

    // ================================================================ costume selection

    public void Paint()
    {
        if (ForcedAction.Length > 0) { ForcedPlayerAct(); return; }
        PlayerAct();
    }

    private void ForcedPlayerAct()
    {
        if (ForcedAction == "slide") { CostumeIndex = CosSlide6; return; }
        if (ForcedAction == "aircrouch") { CostumeIndex = CosSlide4; return; }
        if (ForcedAction == "walk")
        {
            CostumeIndex = CosWalk1;
            if (Math.Abs(In.XAxis) > 0 || Math.Abs(In.YAxis) > 0)
                CostumeIndex = 1 + (int)(Math.Floor(Frame * .25) % 4);
            return;
        }
        CostumeIndex = 1 + (int)(Math.Floor(Frame) % 8);
    }

    private void PlayerAct()
    {
        if (!Swim)
        {
            if (!(Math.Abs(SpeedX) > 4) && Action.Contains("ladder"))
            {
                Rotation = RotationStyle.DontRotate;
                if (Dir == 90) CostumeIndex = CosLadderUp2;
                else if (Dir == -90) CostumeIndex = CosLadderUp1;
                return;
            }
            if (Action != "lose life") Rotation = RotationStyle.LeftRight;
            switch (Action)
            {
                case "slide": CostumeIndex = CosSlide6; return;
                case "": CostumeIndex = CosWalk1; return;
                case "crouch": CostumeIndex = Math.Abs(SpeedX) > 4 ? CosSlide6 : CosCrouch; return;
                case "aircrouch": CostumeIndex = CosAirCrouch; return;
                case "lose life": CostumeIndex = LastDir > 0 ? CosLoseLife : CosLoseLife2; return;
                case "turn": CostumeIndex = CosTurn; return;
            }
            if (Action == "walk" || Action.Contains("ladder"))
            {
                CostumeIndex = CosWalk1;
                if (Math.Abs(In.XAxis) > 0 || Math.Abs(In.YAxis) > 0)
                    CostumeIndex = 1 + (int)(Math.Floor(Frame * .25) % 4);
            }
            else
            {
                CostumeIndex = 1 + (int)(Math.Floor(Frame) % 4);
            }
        }
        else
        {
            if (Action != "lose life") Rotation = RotationStyle.LeftRight;
            if (Action == "" || Action == "swim") { CostumeIndex = CosSwim4; return; }
            if (Action == "lose life") { CostumeIndex = LastDir > 0 ? CosLoseLife : CosLoseLife2; return; }
            if (Action == "swim_walk" || Action.Contains("ladder"))
            {
                CostumeIndex = CosSwim1 + (int)(Math.Floor(Frame) % 5);
            }
            else
            {
                if (Math.Abs(In.XAxis) > 0 || Math.Abs(In.YAxis) > 0)
                    CostumeIndex = CosSwim1 + (int)(Math.Floor(Frame * .25) % 5);
            }
        }
    }
}
