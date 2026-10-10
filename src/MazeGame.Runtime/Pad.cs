using Raylib_cs;

namespace MazeGame.Runtime;

public enum PadAct { Left, Right, Up, Down, Confirm, Back, Jump, Z, X, Use, Start, Count }

/// <summary>
/// Gamepad support. Uses the first connected controller. Layout (Xbox naming):
/// D-pad / left stick = move, A = jump / confirm, X = action Z, B = action X / back, Y or RB = interact, Start = pause.
/// Menus call <see cref="KeyPressed"/> instead of Raylib.IsKeyPressed so the pad also drives them.
/// Call <see cref="Update"/> once per frame.
/// </summary>
public static class Pad
{
    private static readonly bool[] Cur = new bool[(int)PadAct.Count], Prev = new bool[(int)PadAct.Count];
    private const float Dead = 0.45f;
    public static bool Connected { get; private set; }
    public static string Name { get; private set; } = "";

    public static void Update()
    {
        Array.Copy(Cur, Prev, Cur.Length);
        Array.Clear(Cur);
        int id = -1;
        for (int i = 0; i < 4; i++) if (Raylib.IsGamepadAvailable(i)) { id = i; break; }
        Connected = id >= 0;
        if (id < 0) { Name = ""; return; }
        Name = "controller";

        bool B(GamepadButton b) => Raylib.IsGamepadButtonDown(id, b);
        float ax = Raylib.GetGamepadAxisMovement(id, GamepadAxis.LeftX);
        float ay = Raylib.GetGamepadAxisMovement(id, GamepadAxis.LeftY);
        bool left = B(GamepadButton.LeftFaceLeft) || ax < -Dead, right = B(GamepadButton.LeftFaceRight) || ax > Dead;
        bool up = B(GamepadButton.LeftFaceUp) || ay < -Dead, down = B(GamepadButton.LeftFaceDown) || ay > Dead;
        bool a = B(GamepadButton.RightFaceDown);
        Cur[(int)PadAct.Left] = left; Cur[(int)PadAct.Right] = right; Cur[(int)PadAct.Up] = up; Cur[(int)PadAct.Down] = down;
        Cur[(int)PadAct.Confirm] = a;
        Cur[(int)PadAct.Back] = B(GamepadButton.RightFaceRight);
        Cur[(int)PadAct.Jump] = a || up;
        Cur[(int)PadAct.Z] = B(GamepadButton.RightFaceLeft);
        Cur[(int)PadAct.X] = B(GamepadButton.RightFaceRight);
        Cur[(int)PadAct.Use] = B(GamepadButton.RightFaceUp) || B(GamepadButton.RightTrigger1);
        Cur[(int)PadAct.Start] = B(GamepadButton.MiddleRight);
    }

    public static bool Down(PadAct a) => Cur[(int)a];
    public static bool Pressed(PadAct a) => Cur[(int)a] && !Prev[(int)a];

    /// <summary>Replacement for Raylib.IsKeyPressed that also reacts to the matching controller input.</summary>
    public static bool KeyPressed(KeyboardKey k)
    {
        if (Raylib.IsKeyPressed(k)) return true;
        switch (k)
        {
            case KeyboardKey.Up: case KeyboardKey.W: return Pressed(PadAct.Up);
            case KeyboardKey.Down: case KeyboardKey.S: return Pressed(PadAct.Down);
            case KeyboardKey.Left: case KeyboardKey.A: return Pressed(PadAct.Left);
            case KeyboardKey.Right: case KeyboardKey.D: return Pressed(PadAct.Right);
            case KeyboardKey.Enter: case KeyboardKey.Space: case KeyboardKey.E: return Pressed(PadAct.Confirm);
            case KeyboardKey.Escape: return Pressed(PadAct.Back) || Pressed(PadAct.Start);
        }
        return false;
    }
}
