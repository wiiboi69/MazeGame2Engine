using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;
using Silk.NET.Core.Contexts;

namespace Raylib_cs;

/// <summary>Supplied by the platform head: the EGL surface the shim draws into.</summary>
public interface IGlHost
{
    int Width { get; }
    int Height { get; }
    bool Closing { get; }
    nint GetProcAddress(string name);
    void SwapBuffers();
}

public static unsafe class Raylib
{
    public static IGlHost Host = null!;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float X, Y, U, V; public byte R, G, B, A; }

    private static GL _gl = null!;
    private static uint _prog, _vao, _vbo, _whiteTex, _circleTex;
    private static int _uRes;
    private static readonly List<Vertex> Batch = new(4096);
    private static uint _batchTex;
    private static int _resW = 1, _resH = 1;
    private static uint _curFbo;
    private static BlendMode _blend = BlendMode.Alpha;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _lastFrame;
    private static float _frameTime = 1 / 60f;
    private static int _fpsFrames; private static double _fpsStamp; private static int _fps = 60;

    private static readonly HashSet<KeyboardKey> KeysNow = new(), KeysPrev = new(), KeysScratch = new();
    private static bool _ptrNow, _ptrPrev;
    private static Vector2 _ptrPos, _ptrPrevPos;

    // ---------------------------------------------------------------- window

    public static void SetConfigFlags(ConfigFlags f) { }
    public static void SetWindowMinSize(int w, int h) { }
    public static void SetTargetFPS(int fps) { }
    public static void SetExitKey(KeyboardKey k) { }
    public static void SetWindowSize(int w, int h) { }
    public static void SetWindowState(ConfigFlags f) { }
    public static void ClearWindowState(ConfigFlags f) { }
    public static void ToggleFullscreen() { }
    public static bool IsWindowFullscreen() => true;
    public static int GetCurrentMonitor() => 0;
    public static int GetMonitorWidth(int m) => GetScreenWidth();
    public static int GetMonitorHeight(int m) => GetScreenHeight();
    public static int GetScreenWidth() => Host.Width;
    public static int GetScreenHeight() => Host.Height;
    public static bool WindowShouldClose() => Host.Closing;
    public static float GetFrameTime() => _frameTime;
    public static int GetFPS() => _fps;

    public static void InitWindow(int w, int h, string title)
    {
        _gl = GL.GetApi(new LamdaNativeContext(Host.GetProcAddress));
        TouchInput.ScreenW = Host.Width;
        TouchInput.ScreenH = Host.Height;
        InitGl();
    }

    public static void CloseWindow() { }

    private static void InitGl()
    {
        const string vs = @"#version 300 es
layout(location=0) in vec2 aPos; layout(location=1) in vec2 aUv; layout(location=2) in vec4 aCol;
uniform vec2 uRes; out vec2 vUv; out vec4 vCol;
void main(){ vUv=aUv; vCol=aCol; gl_Position=vec4(aPos.x/uRes.x*2.0-1.0, 1.0-aPos.y/uRes.y*2.0, 0.0, 1.0); }";
        const string fs = @"#version 300 es
precision mediump float; in vec2 vUv; in vec4 vCol; uniform sampler2D uTex; out vec4 o;
void main(){ o = texture(uTex, vUv) * vCol; }";
        uint v = Compile(ShaderType.VertexShader, vs), f = Compile(ShaderType.FragmentShader, fs);
        _prog = _gl.CreateProgram();
        _gl.AttachShader(_prog, v); _gl.AttachShader(_prog, f);
        _gl.LinkProgram(_prog);
        _uRes = _gl.GetUniformLocation(_prog, "uRes");

        _vao = _gl.GenVertexArray(); _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer(); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        uint stride = (uint)sizeof(Vertex);
        _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2); _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)16);

        byte[] white = { 255, 255, 255, 255 };
        fixed (byte* p = white) _whiteTex = MakeTexture(1, 1, p, false);
        const int cs = 64;
        var circle = new byte[cs * cs * 4];
        for (int y = 0; y < cs; y++)
            for (int x = 0; x < cs; x++)
            {
                float dx = x + 0.5f - cs / 2f, dy = y + 0.5f - cs / 2f;
                float a = Math.Clamp(cs / 2f - MathF.Sqrt(dx * dx + dy * dy), 0, 1);
                int i = (y * cs + x) * 4;
                circle[i] = circle[i + 1] = circle[i + 2] = 255; circle[i + 3] = (byte)(a * 255);
            }
        fixed (byte* p = circle) _circleTex = MakeTexture(cs, cs, p, true);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        ApplyBlend();
        PollInput();
    }

    private static uint Compile(ShaderType t, string src)
    {
        uint s = _gl.CreateShader(t);
        _gl.ShaderSource(s, src); _gl.CompileShader(s);
        _gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException("shader: " + _gl.GetShaderInfoLog(s));
        return s;
    }

    private static uint MakeTexture(int w, int h, void* data, bool linear)
    {
        uint id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, id);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, Silk.NET.OpenGLES.PixelFormat.Rgba, PixelType.UnsignedByte, data);
        SetFilterRaw(linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return id;
    }

    private static void SetFilterRaw(bool linear)
    {
        int f = (int)(linear ? GLEnum.Linear : GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, f);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, f);
    }

    // ---------------------------------------------------------------- textures

    public static Image GenImageColor(int w, int h, Color c)
    {
        var mem = (byte*)NativeMemory.Alloc((nuint)(w * h * 4));
        for (int i = 0; i < w * h; i++) { mem[i * 4] = c.R; mem[i * 4 + 1] = c.G; mem[i * 4 + 2] = c.B; mem[i * 4 + 3] = c.A; }
        return new Image { Data = mem, Width = w, Height = h, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
    }

    public static void UnloadImage(Image img) { if (img.Data != null) NativeMemory.Free(img.Data); }

    public static Texture2D LoadTextureFromImage(Image img)
    {
        FlushBatch();
        uint id = MakeTexture(img.Width, img.Height, img.Data, true);
        return new Texture2D { Id = id, Width = img.Width, Height = img.Height, Mipmaps = 1, Format = img.Format };
    }

    public static void UpdateTexture(Texture2D t, void* pixels)
    {
        FlushBatch();
        _gl.BindTexture(TextureTarget.Texture2D, t.Id);
        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)t.Width, (uint)t.Height, Silk.NET.OpenGLES.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
    }

    public static void SetTextureFilter(Texture2D t, TextureFilter f)
    {
        FlushBatch();
        _gl.BindTexture(TextureTarget.Texture2D, t.Id);
        SetFilterRaw(f != TextureFilter.Point);
    }

    public static void UnloadTexture(Texture2D t) { if (t.Id != 0) _gl.DeleteTexture(t.Id); }

    public static RenderTexture2D LoadRenderTexture(int w, int h)
    {
        FlushBatch();
        uint tex = MakeTexture(w, h, null, true);
        uint fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _curFbo);
        return new RenderTexture2D { Id = fbo, Texture = new Texture2D { Id = tex, Width = w, Height = h, Mipmaps = 1 } };
    }

    public static void UnloadRenderTexture(RenderTexture2D rt)
    {
        _gl.DeleteFramebuffer(rt.Id);
        _gl.DeleteTexture(rt.Texture.Id);
    }

    // ---------------------------------------------------------------- drawing

    private static void ApplyBlend()
    {
        if (_blend == BlendMode.AlphaPremultiply) _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        else _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
    }

    public static void BeginBlendMode(BlendMode m) { FlushBatch(); _blend = m; ApplyBlend(); }
    public static void EndBlendMode() { FlushBatch(); _blend = BlendMode.Alpha; ApplyBlend(); }

    private static void SetTarget(uint fbo, int w, int h)
    {
        FlushBatch();
        _curFbo = fbo; _resW = w; _resH = h;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.Viewport(0, 0, (uint)w, (uint)h);
    }

    public static void BeginDrawing() => SetTarget(0, GetScreenWidth(), GetScreenHeight());

    public static void ClearBackground(Color c)
    {
        FlushBatch();
        _gl.ClearColor(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public static void BeginTextureMode(RenderTexture2D rt) => SetTarget(rt.Id, rt.Texture.Width, rt.Texture.Height);
    public static void EndTextureMode() => SetTarget(0, GetScreenWidth(), GetScreenHeight());

    private static void FlushBatch()
    {
        if (Batch.Count == 0) return;
        _gl.UseProgram(_prog);
        _gl.Uniform2(_uRes, (float)_resW, (float)_resH);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _batchTex);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var arr = CollectionsMarshal.AsSpan(Batch);
        fixed (Vertex* p = arr)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(Vertex)), p, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)Batch.Count);
        Batch.Clear();
    }

    private static void Quad(uint tex, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float u0, float v0, float u1, float v1, Color ca, Color cb, Color cc, Color cd)
    {
        if (tex != _batchTex) { FlushBatch(); _batchTex = tex; }
        Vertex V(Vector2 p, float u, float v, Color k) => new() { X = p.X, Y = p.Y, U = u, V = v, R = k.R, G = k.G, B = k.B, A = k.A };
        var va = V(a, u0, v0, ca); var vb = V(b, u1, v0, cb); var vc = V(c, u1, v1, cc); var vd = V(d, u0, v1, cd);
        Batch.Add(va); Batch.Add(vb); Batch.Add(vc);
        Batch.Add(va); Batch.Add(vc); Batch.Add(vd);
    }

    public static void DrawTexturePro(Texture2D t, Rectangle src, Rectangle dst, Vector2 origin, float rotation, Color tint)
    {
        bool flipX = false;
        if (src.Width < 0) { flipX = true; src.Width = -src.Width; }
        if (src.Height < 0) src.Y -= src.Height;
        float u0 = src.X / t.Width, u1 = (src.X + src.Width) / t.Width;
        float v0 = src.Y / t.Height, v1 = (src.Y + src.Height) / t.Height;
        if (flipX) (u0, u1) = (u1, u0);

        Vector2 pos = new(dst.X, dst.Y), a, b, c, d;
        if (rotation == 0)
        {
            float x = dst.X - origin.X, y = dst.Y - origin.Y;
            a = new(x, y); b = new(x + dst.Width, y); c = new(x + dst.Width, y + dst.Height); d = new(x, y + dst.Height);
        }
        else
        {
            float r = rotation * MathF.PI / 180f, cs = MathF.Cos(r), sn = MathF.Sin(r);
            Vector2 R(float px, float py) { float x = px - origin.X, y = py - origin.Y; return pos + new Vector2(x * cs - y * sn, x * sn + y * cs); }
            a = R(0, 0); b = R(dst.Width, 0); c = R(dst.Width, dst.Height); d = R(0, dst.Height);
        }
        Quad(t.Id, a, b, c, d, u0, v0, u1, v1, tint, tint, tint, tint);
    }

    public static void DrawTexture(Texture2D t, int x, int y, Color tint) =>
        DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(x, y, t.Width, t.Height), Vector2.Zero, 0, tint);

    public static void DrawRectangleGradientV(int x, int y, int w, int h, Color top, Color bottom) =>
        Quad(_whiteTex, new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), 0, 0, 1, 1, top, top, bottom, bottom);

    public static void EndDrawing()
    {
        DrawOverlay();
        FlushBatch();
        Host.SwapBuffers();
        double now = Clock.Elapsed.TotalSeconds;
        _frameTime = (float)Math.Clamp(now - _lastFrame, 0.0001, 0.25);
        _lastFrame = now;
        _fpsFrames++;
        if (now - _fpsStamp >= 1) { _fps = _fpsFrames; _fpsFrames = 0; _fpsStamp = now; }
        TouchInput.ScreenW = GetScreenWidth(); TouchInput.ScreenH = GetScreenHeight();
        PollInput();
    }

    private static readonly bool[] Pressed = new bool[5];

    private static void DrawOverlay()
    {
        TouchInput.OverlayState(out bool stick, out var sb, out var knob, Pressed);
        void Disc(Vector2 c, float r, byte cr, byte cg, byte cb, byte a)
        {
            var col = new Color(cr, cg, cb, a);
            Quad(_circleTex, new(c.X - r, c.Y - r), new(c.X + r, c.Y - r), new(c.X + r, c.Y + r), new(c.X - r, c.Y + r), 0, 0, 1, 1, col, col, col, col);
        }
        float u = TouchInput.Unit;
        if (stick) { Disc(sb, u * 1.4f, 255, 255, 255, 40); Disc(knob, u * 0.6f, 255, 255, 255, 110); }
        else Disc(new Vector2(u * 2f, TouchInput.ScreenH - u * 2f), u * 1.4f, 255, 255, 255, 25);
        for (int i = 0; i < 5; i++)
        {
            var (r, g, b) = TouchInput.ButtonColors[i];
            Disc(TouchInput.ButtonCenter(i), TouchInput.ButtonRadius(i), r, g, b, (byte)(Pressed[i] ? 150 : 60));
        }
    }

    // ---------------------------------------------------------------- input

    private static void PollInput()
    {
        KeysPrev.Clear(); foreach (var k in KeysNow) KeysPrev.Add(k);
        _ptrPrev = _ptrNow; _ptrPrevPos = _ptrPos;
        TouchInput.Snapshot(KeysScratch, out _ptrNow, out var p);
        KeysNow.Clear(); foreach (var k in KeysScratch) KeysNow.Add(k);
        if (_ptrNow) _ptrPos = p;
    }

    public static bool IsKeyDown(KeyboardKey k) => KeysNow.Contains(k);
    public static bool IsKeyPressed(KeyboardKey k) => KeysNow.Contains(k) && !KeysPrev.Contains(k);
    public static int GetKeyPressed() { foreach (var k in KeysNow) if (!KeysPrev.Contains(k)) return (int)k; return 0; }

    public static Vector2 GetMousePosition() => _ptrPos;
    public static Vector2 GetMouseDelta() => _ptrNow && _ptrPrev ? _ptrPos - _ptrPrevPos : Vector2.Zero;
    public static bool IsMouseButtonDown(MouseButton b) => b == MouseButton.Left && _ptrNow;
    public static bool IsMouseButtonPressed(MouseButton b) => b == MouseButton.Left && _ptrNow && !_ptrPrev;
    public static bool IsMouseButtonReleased(MouseButton b) => b == MouseButton.Left && !_ptrNow && _ptrPrev;

    public static bool IsGamepadAvailable(int i) => false;
    public static bool IsGamepadButtonDown(int i, GamepadButton b) => false;
    public static float GetGamepadAxisMovement(int i, GamepadAxis a) => 0;
}
