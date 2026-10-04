namespace MazeGame.Core;

/// <summary>Logical controls for one tick (the original "Controls" sprite variables).</summary>
public struct InputState
{
    public bool Left, Right, Up, Down, Z, X, Use;
    /// <summary>True only on the first tick the use key is down (edge triggered).</summary>
    public bool UsePressed;

    public readonly int XAxis => (Right ? 1 : 0) - (Left ? 1 : 0);
    public readonly int YAxis => (Up ? 1 : 0) - (Down ? 1 : 0);
}
