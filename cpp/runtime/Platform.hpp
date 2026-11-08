#pragma once
// The only thing a console port has to implement. Everything above this (sprites, scene renderer, UI, audio mixer,
// game) is platform independent and talks to the machine through this interface - like ScratchEverywhere's
// renderers/ + windowing/ + audio/ split, collapsed into one class per backend (PlatformSDL2.cpp is the reference).
#include <cstdint>
#include <functional>

namespace mg::plat {

struct Color {
    uint8_t r = 255, g = 255, b = 255, a = 255;
    constexpr Color() = default;
    constexpr Color(int r_, int g_, int b_, int a_ = 255) : r((uint8_t)r_), g((uint8_t)g_), b((uint8_t)b_), a((uint8_t)a_) {}
};

struct Texture {
    int w = 0, h = 0;
    void* impl = nullptr;
    bool valid() const { return impl != nullptr; }
};

// Key codes are raylib's numbering (so settings.json written by the C# game keeps working).
namespace Key {
enum : int {
    None = 0, Space = 32, Apostrophe = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47,
    Zero = 48, Nine = 57, Semicolon = 59, Equal = 61, A = 65, D = 68, E = 69, N = 78, P = 80, Q = 81, S = 83, W = 87, Y = 89, Z = 90, X = 88,
    LeftBracket = 91, Backslash = 92, RightBracket = 93, Grave = 96,
    Escape = 256, Enter = 257, Tab = 258, Backspace = 259, Insert = 260, Delete = 261,
    Right = 262, Left = 263, Down = 264, Up = 265, PageUp = 266, PageDown = 267, Home = 268, End = 269,
    CapsLock = 280, F1 = 290, F5 = 294, F6 = 295, F7 = 296, F11 = 300, F12 = 301,
    LeftShift = 340, LeftControl = 341, LeftAlt = 342, RightShift = 344, RightControl = 345, RightAlt = 346,
    Max = 512,
};
}

enum class PadButton { Left, Right, Up, Down, A, B, X, Y, RightBumper, Start, Count };

struct PadState {
    bool connected = false;
    bool button[(int)PadButton::Count] = {};
    float axisX = 0, axisY = 0;   // left stick
};

class Platform {
public:
    virtual ~Platform() = default;

    // ---- window
    virtual bool createWindow(const char* title, int windowW, int windowH, int virtualW, int virtualH) = 0;
    virtual void setWindowSize(int w, int h) = 0;
    virtual void setVirtualSize(int w, int h) = 0;       // size of the render target everything is drawn into
    virtual bool toggleFullscreen() = 0;                 // returns the new state
    virtual bool isFullscreen() const = 0;
    virtual int  windowWidth() const = 0;
    virtual int  windowHeight() const = 0;
    virtual void setVSync(bool on) = 0;
    virtual void setFpsLimit(int fps) = 0;               // 0 = unlimited

    // ---- frame
    virtual void pollEvents() = 0;                       // call once at the start of each frame
    virtual bool shouldClose() const = 0;
    virtual float frameTime() const = 0;                 // seconds the last frame took
    virtual int  fps() const = 0;
    virtual void beginFrame() = 0;                       // start drawing into the virtual target
    virtual void endFrame() = 0;                         // letterbox it to the window, present, limit the frame rate

    // ---- input (mouse in virtual-target coordinates)
    virtual bool keyDown(int key) const = 0;
    virtual bool keyPressed(int key) const = 0;
    virtual int  keyPressedAny() = 0;                    // next key pressed this frame (0 = none), like raylib GetKeyPressed
    virtual bool mouseDown(int button) const = 0;        // 0 = left
    virtual bool mousePressed(int button) const = 0;
    virtual bool mouseReleased(int button) const = 0;
    virtual void mousePos(float& x, float& y) const = 0;
    virtual void mouseDelta(float& dx, float& dy) const = 0;
    virtual const PadState& pad() const = 0;

    // ---- graphics
    virtual Texture createTexture(const uint8_t* rgba, int w, int h, bool linear) = 0;   // straight alpha
    virtual void destroyTexture(Texture& t) = 0;
    // Like raylib DrawTexturePro: src rect (negative srcW flips horizontally), dest rect whose origin point
    // (originX, originY, relative to the dest rect) lands on (dstX, dstY) and is the rotation centre.
    virtual void drawTexture(const Texture& t, float srcX, float srcY, float srcW, float srcH,
                             float dstX, float dstY, float dstW, float dstH,
                             float originX, float originY, float rotationDeg, Color tint) = 0;
    virtual void fillRect(float x, float y, float w, float h, Color c) = 0;

    // ---- audio: the callback fills interleaved stereo int16 frames; it runs on the audio thread
    virtual bool openAudio(int sampleRate, std::function<void(int16_t* out, int frames)> callback) = 0;
    virtual void lockAudio() = 0;
    virtual void unlockAudio() = 0;
    virtual int  audioSampleRate() const = 0;
};

} // namespace mg::plat
