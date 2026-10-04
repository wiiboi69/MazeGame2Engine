using System.Numerics;
using Raylib_cs;
using SkiaSharp;

namespace MazeGame.Runtime;

/// <summary>Immediate-mode vertical button menu with keyboard + mouse support.</summary>
public sealed class UiMenu
{
    public int Selected;
    private Vector2 _lastMouse;

    /// <summary>Draws the items and returns the index that was activated this frame (Enter / click) or -1.</summary>
    public int Run(SkiaUi ui, Vector2 mouse, string[] items, float x, float y, float w, float h = 52, float gap = 14)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) Selected = (Selected + 1) % items.Length;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) Selected = (Selected + items.Length - 1) % items.Length;
        if (Selected >= items.Length) Selected = 0;

        bool mouseMoved = (mouse - _lastMouse).LengthSquared() > 0.5f;
        _lastMouse = mouse;
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        int activated = -1;
        for (int i = 0; i < items.Length; i++)
        {
            float by = y + i * (h + gap);
            bool hot = ui.Button(items[i], x, by, w, h, mouse, i == Selected);
            if (hot && mouseMoved) Selected = i;
            if (hot && click) activated = i;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space)) activated = Selected;
        return activated;
    }
}
