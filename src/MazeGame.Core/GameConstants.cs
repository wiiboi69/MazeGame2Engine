namespace MazeGame.Core;

/// <summary>Numbers lifted straight from the original Scratch engine.</summary>
public static class GameConstants
{
    /// <summary>One tile is 32x32 "stage pixels" (16px costume at 200% size).</summary>
    public const int TileSize = 32;

    /// <summary>The Scratch stage is 480x360 and the camera is centred on the player.</summary>
    public const int StageWidth = 640;
    public const int StageHeight = 360;

    /// <summary>
    /// The original runs one logic step every 2 frames of a 60 fps runtime. All speeds
    /// (jump 13, gravity -2, walk 0.8...) are per-tick, so this must stay at 30.
    /// </summary>
    public const int TicksPerSecond = 30;

    /// <summary>The "-TINY" epsilon the collision code uses to stay just inside a tile.</summary>
    public const double Tiny = -0.000001;

    /// <summary>Level number 1 in the original level store is the "game settings" slot (gs_2_).</summary>
    public const int SettingsSlot = 1;
    public const int FirstLevel = 2;

    // Level flag bits (level_flags_01)
    public const int FlagShowBackground = 1 << 0;
    public const int FlagUnderwater = 1 << 1;
}
