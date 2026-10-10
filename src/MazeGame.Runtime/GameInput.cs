using MazeGame.Core;
using Raylib_cs;

namespace MazeGame.Runtime;

/// <summary>Turns keyboard state into the Core <see cref="InputState"/> using the configurable bindings.</summary>
public sealed class GameInput
{
    private readonly Settings _settings;
    private bool _useWasDown;

    public GameInput(Settings s) { _settings = s; }

    private bool Down(string action)
    {
        if (!_settings.Keys.TryGetValue(action, out var keys)) return false;
        foreach (int k in keys)
            if (Raylib.IsKeyDown((KeyboardKey)k)) return true;
        return false;
    }

    /// <summary>Call exactly once per logic tick.</summary>
    public InputState Poll()
    {
        var s = new InputState
        {
            Left = Down("left") || Pad.Down(PadAct.Left), Right = Down("right") || Pad.Down(PadAct.Right),
            Up = Down("up") || Pad.Down(PadAct.Jump), Down = Down("down") || Pad.Down(PadAct.Down),
            Z = Down("z") || Pad.Down(PadAct.Z), X = Down("x") || Pad.Down(PadAct.X), Use = Down("use") || Pad.Down(PadAct.Use),
        };
        s.UsePressed = s.Use && !_useWasDown;
        _useWasDown = s.Use;
        return s;
    }

    /// <summary>Forget held keys (used when menus take over so a held key does not leak into the game).</summary>
    public void Reset() => _useWasDown = true;
}
