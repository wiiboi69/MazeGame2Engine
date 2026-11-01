using System.Numerics;

// Minimal Raylib_cs-compatible surface, implemented on Silk.NET OpenGL ES so the shared game code builds unchanged for Android.
namespace Raylib_cs;

public struct Color
{
    public byte R, G, B, A;
    public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
}

public struct Rectangle
{
    public float X, Y, Width, Height;
    public Rectangle(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
}

public struct Texture2D
{
    public uint Id;
    public int Width, Height, Mipmaps;
    public PixelFormat Format;
}

public struct RenderTexture2D
{
    public uint Id;
    public Texture2D Texture;
}

public unsafe struct Image
{
    public void* Data;
    public int Width, Height, Mipmaps;
    public PixelFormat Format;
}

public enum PixelFormat { UncompressedR8G8B8A8 = 7 }
public enum TextureFilter { Point = 0, Bilinear = 1 }
public enum BlendMode { Alpha = 0, AlphaPremultiply = 7 }
public enum MouseButton { Left = 0, Right = 1, Middle = 2 }

[Flags]
public enum ConfigFlags { VSyncHint = 0x40, ResizableWindow = 0x04 }

public enum GamepadButton
{
    LeftFaceUp = 1, LeftFaceRight, LeftFaceDown, LeftFaceLeft,
    RightFaceUp, RightFaceRight, RightFaceDown, RightFaceLeft,
    LeftTrigger1, LeftTrigger2, RightTrigger1, RightTrigger2,
    MiddleLeft, Middle, MiddleRight,
}

public enum GamepadAxis { LeftX = 0, LeftY = 1 }

public enum KeyboardKey
{
    Null = 0, Space = 32,
    Zero = 48, One, Two, Three, Four, Five, Six, Seven, Eight, Nine,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Escape = 256, Enter = 257, Tab = 258, Backspace = 259, Delete = 261,
    Right = 262, Left = 263, Down = 264, Up = 265, PageUp = 266, PageDown = 267,
    F1 = 290, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    LeftShift = 340, LeftControl = 341, RightShift = 344, RightControl = 345,
}
