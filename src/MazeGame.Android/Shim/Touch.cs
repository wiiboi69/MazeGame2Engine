using System.Numerics;

namespace Raylib_cs;

/// <summary>Multi-touch state fed from the Android activity, turned into virtual keys / a mouse pointer plus an on-screen overlay.</summary>
public static class TouchInput
{
    private sealed class Pointer { public float X, Y, StartX, StartY; public int Role = -1; }   // Role: -1 free, 0 stick, 1+ = button index

    private static readonly object Lock = new();
    private static readonly Dictionary<int, Pointer> Active = new();
    private static readonly HashSet<KeyboardKey> Hardware = new();

    public static int ScreenW = 1, ScreenH = 1;

    public static readonly KeyboardKey[] ButtonKeys = { KeyboardKey.Up, KeyboardKey.Z, KeyboardKey.X, KeyboardKey.E, KeyboardKey.Escape };
    public static readonly (byte r, byte g, byte b)[] ButtonColors = { (90, 200, 110), (90, 150, 240), (235, 90, 90), (240, 200, 70), (200, 200, 200) };

    public static float Unit => Math.Min(ScreenW, ScreenH) * 0.105f;

    public static Vector2 ButtonCenter(int i)
    {
        float u = Unit, w = ScreenW, h = ScreenH;
        return i switch
        {
            0 => new Vector2(w - 2.0f * u, h - 1.5f * u),
            1 => new Vector2(w - 3.9f * u, h - 1.5f * u),
            2 => new Vector2(w - 2.0f * u, h - 3.4f * u),
            3 => new Vector2(w - 3.9f * u, h - 3.4f * u),
            _ => new Vector2(w - 0.9f * u, 0.9f * u),
        };
    }

    public static float ButtonRadius(int i) => i == 4 ? Unit * 0.6f : Unit * 0.85f;

    public static void OnTouch(int action, int id, float x, float y, bool isPointerEvent)
    {
        lock (Lock)
        {
            switch (action)
            {
                case 0:   // down
                    var p = new Pointer { X = x, Y = y, StartX = x, StartY = y };
                    for (int i = 0; i < ButtonKeys.Length; i++)
                        if (Vector2.Distance(new Vector2(x, y), ButtonCenter(i)) <= ButtonRadius(i) * 1.25f) { p.Role = i + 1; break; }
                    if (p.Role < 0 && x < ScreenW * 0.45f && y > ScreenH * 0.25f) p.Role = 0;
                    Active[id] = p;
                    break;
                case 2:   // move
                    if (Active.TryGetValue(id, out var m)) { m.X = x; m.Y = y; }
                    break;
                case 1:   // up
                case 3:   // cancel
                    Active.Remove(id);
                    break;
            }
        }
    }

    public static void OnHardwareKey(KeyboardKey k, bool down)
    {
        lock (Lock) { if (down) Hardware.Add(k); else Hardware.Remove(k); }
    }

    /// <summary>Snapshot: which virtual keys are held, and the primary pointer.</summary>
    public static void Snapshot(HashSet<KeyboardKey> keys, out bool pointerDown, out Vector2 pointer)
    {
        keys.Clear();
        pointerDown = false; pointer = default;
        lock (Lock)
        {
            foreach (var k in Hardware) keys.Add(k);
            int best = int.MaxValue;
            foreach (var (id, t) in Active)
            {
                if (id < best) { best = id; pointer = new Vector2(t.X, t.Y); pointerDown = true; }
                if (t.Role == 0)
                {
                    float dx = t.X - t.StartX, dy = t.Y - t.StartY, dead = Unit * 0.35f;
                    if (dx < -dead) keys.Add(KeyboardKey.Left);
                    if (dx > dead) keys.Add(KeyboardKey.Right);
                    if (dy < -dead * 1.5f) keys.Add(KeyboardKey.Up);
                    if (dy > dead) keys.Add(KeyboardKey.Down);
                }
                else if (t.Role > 0) keys.Add(ButtonKeys[t.Role - 1]);
            }
        }
    }

    /// <summary>Overlay geometry for drawing: stick base/knob (if held) and which buttons are pressed.</summary>
    public static void OverlayState(out bool stick, out Vector2 stickBase, out Vector2 knob, bool[] pressed)
    {
        stick = false; stickBase = default; knob = default;
        Array.Clear(pressed);
        lock (Lock)
        {
            foreach (var t in Active.Values)
            {
                if (t.Role == 0)
                {
                    stick = true; stickBase = new Vector2(t.StartX, t.StartY);
                    var d = new Vector2(t.X - t.StartX, t.Y - t.StartY);
                    float max = Unit * 1.1f;
                    if (d.Length() > max) d = Vector2.Normalize(d) * max;
                    knob = stickBase + d;
                }
                else if (t.Role > 0) pressed[t.Role - 1] = true;
            }
        }
    }
}
