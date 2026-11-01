using System.Runtime.InteropServices;
using Android.App;
using Android.Content.PM;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Raylib_cs;

[assembly: Application(Label = "Maze Game 2", Icon = "@mipmap/appicon")]

namespace MazeGame.Android;

[Activity(Label = "Maze Game 2", MainLauncher = true, Exported = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen")]
public class MainActivity : Activity, ISurfaceHolderCallback, IGlHost
{
    [DllImport("libEGL.so", EntryPoint = "eglGetProcAddress")]
    private static extern nint EglGetProcAddress(string name);

    private SurfaceView? _surface;
    private Thread? _thread;
    private volatile bool _closing;
    private int _w, _h;
    private EGLDisplay? _display;
    private EGLSurface? _eglSurface;

    public int Width => _w;
    public int Height => _h;
    public bool Closing => _closing;
    public nint GetProcAddress(string name) => EglGetProcAddress(name);
    public void SwapBuffers() => EGL14.EglSwapBuffers(_display, _eglSurface);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        _surface = new SurfaceView(this);
        _surface.Holder!.AddCallback(this);
        SetContentView(_surface);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus && Window?.DecorView != null)
            Window.DecorView.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen
                | SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
    }

    public void SurfaceCreated(ISurfaceHolder holder) { }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
        _w = width; _h = height;
        if (_thread != null) return;
        _thread = new Thread(() => GameThread(holder)) { Name = "MazeGame", IsBackground = true };
        _thread.Start();
    }

    // The EGL surface goes away with the window (home button, screen off): stop the game, which saves, and close.
    public void SurfaceDestroyed(ISurfaceHolder holder) { _closing = true; _thread?.Join(2000); }

    protected override void OnDestroy() { _closing = true; base.OnDestroy(); }

    private void GameThread(ISurfaceHolder holder)
    {
        try
        {
            SetupEgl(holder);
            Raylib.Host = this;
            string root = FilesDir!.AbsolutePath;
            CopyAssets(root);
            MazeGame.Core.GameContent.RegisterAll();
            using var game = new Game(Path.Combine(root, "assets"), Path.Combine(root, "levels"), Path.Combine(root, "settings.json"));
            game.Run();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("MazeGame", ex.ToString());
        }
        RunOnUiThread(Finish);
    }

    private void SetupEgl(ISurfaceHolder holder)
    {
        _display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        var ver = new int[2];
        EGL14.EglInitialize(_display, ver, 0, ver, 1);
        int[] cfgAttr =
        {
            0x3033, 0x0004,          // surface type: window
            0x3040, 0x0040,          // renderable type: ES3
            0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8,
            0x3038,
        };
        var configs = new EGLConfig?[1];
        var num = new int[1];
        EGL14.EglChooseConfig(_display, cfgAttr, 0, configs, 0, 1, num, 0);
        var ctx = EGL14.EglCreateContext(_display, configs[0], EGL14.EglNoContext, new[] { 0x3098, 3, 0x3038 }, 0);
        _eglSurface = EGL14.EglCreateWindowSurface(_display, configs[0], holder.Surface, new[] { 0x3038 }, 0);
        EGL14.EglMakeCurrent(_display, _eglSurface, _eglSurface, ctx);
        EGL14.EglSwapInterval(_display, 1);
    }

    /// <summary>The game reads plain files, so the packaged assets/levels/worlds are unpacked once per installed version.</summary>
    private void CopyAssets(string root)
    {
        long version = PackageManager!.GetPackageInfo(PackageName!, 0)!.LongVersionCode;
        string stamp = Path.Combine(root, ".assets-version");
        if (File.Exists(stamp) && File.ReadAllText(stamp) == version.ToString()) return;
        foreach (var dir in new[] { "assets", "levels", "worlds" }) CopyDir(dir, Path.Combine(root, dir));
        File.WriteAllText(stamp, version.ToString());
    }

    private void CopyDir(string assetPath, string target)
    {
        string[] entries = Assets!.List(assetPath) ?? Array.Empty<string>();
        if (entries.Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = Assets.Open(assetPath);
            using var dst = File.Create(target);
            src.CopyTo(dst);
            return;
        }
        Directory.CreateDirectory(target);
        foreach (var e in entries) CopyDir(assetPath + "/" + e, Path.Combine(target, e));
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e == null) return base.DispatchTouchEvent(e);
        var a = e.ActionMasked;
        if (a == MotionEventActions.Down || a == MotionEventActions.PointerDown)
        {
            int i = e.ActionIndex;
            TouchInput.OnTouch(0, e.GetPointerId(i), e.GetX(i), e.GetY(i), true);
        }
        else if (a == MotionEventActions.Up || a == MotionEventActions.PointerUp)
        {
            TouchInput.OnTouch(1, e.GetPointerId(e.ActionIndex), 0, 0, true);
        }
        else if (a == MotionEventActions.Cancel)
        {
            for (int i = 0; i < e.PointerCount; i++) TouchInput.OnTouch(3, e.GetPointerId(i), 0, 0, true);
        }
        else if (a == MotionEventActions.Move)
        {
            for (int i = 0; i < e.PointerCount; i++) TouchInput.OnTouch(2, e.GetPointerId(i), e.GetX(i), e.GetY(i), true);
        }
        return true;
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e != null && e.Action != KeyEventActions.Multiple)
        {
            var k = Map(e.KeyCode);
            if (k != KeyboardKey.Null)
            {
                TouchInput.OnHardwareKey(k, e.Action == KeyEventActions.Down);
                return true;
            }
        }
        return base.DispatchKeyEvent(e);
    }

    private static KeyboardKey Map(Keycode c)
    {
        if (c >= Keycode.A && c <= Keycode.Z) return KeyboardKey.A + (c - Keycode.A);
        return c switch
        {
            Keycode.DpadLeft => KeyboardKey.Left, Keycode.DpadRight => KeyboardKey.Right,
            Keycode.DpadUp => KeyboardKey.Up, Keycode.DpadDown => KeyboardKey.Down,
            Keycode.Enter or Keycode.DpadCenter or Keycode.ButtonA => KeyboardKey.Enter,
            Keycode.Space => KeyboardKey.Space,
            Keycode.Escape or Keycode.Back or Keycode.ButtonStart => KeyboardKey.Escape,
            Keycode.ButtonX => KeyboardKey.Z, Keycode.ButtonB => KeyboardKey.X, Keycode.ButtonY => KeyboardKey.E,
            _ => KeyboardKey.Null,
        };
    }
}
