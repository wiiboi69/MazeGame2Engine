// Reference backend: SDL2 (window, renderer, input, game controller, audio). Works on PC; PSP/PS4 use SDL2/SDL3 builds
// of their own (see ScratchEverywhere's cmake/platforms/psp.cmake and ps4.cmake) - this file is what you adapt.
#include "PlatformSDL2.hpp"
#include "Lowres.hpp"
#include <SDL.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>
#include <cstdlib>
#include <string>
#define STB_IMAGE_WRITE_IMPLEMENTATION
#include "../third_party/stb/stb_image_write.h"

#if defined(__PSP__)
#define MG_PAD_INIT SDL_INIT_JOYSTICK
#else
#define MG_PAD_INIT SDL_INIT_GAMECONTROLLER
#endif

namespace mg::plat {

namespace {

int toRaylibKey(SDL_Keycode k) {
    if (k >= SDLK_a && k <= SDLK_z) return Key::A + (k - SDLK_a);
    if (k >= SDLK_0 && k <= SDLK_9) return Key::Zero + (k - SDLK_0);
    if (k >= SDLK_F1 && k <= SDLK_F12) return Key::F1 + (k - SDLK_F1);
    switch (k) {
        case SDLK_SPACE: return Key::Space;
        case SDLK_QUOTE: return Key::Apostrophe;
        case SDLK_COMMA: return Key::Comma;
        case SDLK_MINUS: return Key::Minus;
        case SDLK_PERIOD: return Key::Period;
        case SDLK_SLASH: return Key::Slash;
        case SDLK_SEMICOLON: return Key::Semicolon;
        case SDLK_EQUALS: return Key::Equal;
        case SDLK_LEFTBRACKET: return Key::LeftBracket;
        case SDLK_BACKSLASH: return Key::Backslash;
        case SDLK_RIGHTBRACKET: return Key::RightBracket;
        case SDLK_BACKQUOTE: return Key::Grave;
        case SDLK_ESCAPE: return Key::Escape;
        case SDLK_RETURN: case SDLK_KP_ENTER: return Key::Enter;
        case SDLK_TAB: return Key::Tab;
        case SDLK_BACKSPACE: return Key::Backspace;
        case SDLK_INSERT: return Key::Insert;
        case SDLK_DELETE: return Key::Delete;
        case SDLK_RIGHT: return Key::Right;
        case SDLK_LEFT: return Key::Left;
        case SDLK_DOWN: return Key::Down;
        case SDLK_UP: return Key::Up;
        case SDLK_PAGEUP: return Key::PageUp;
        case SDLK_PAGEDOWN: return Key::PageDown;
        case SDLK_HOME: return Key::Home;
        case SDLK_END: return Key::End;
        case SDLK_CAPSLOCK: return Key::CapsLock;
        case SDLK_LSHIFT: return Key::LeftShift;
        case SDLK_LCTRL: return Key::LeftControl;
        case SDLK_LALT: return Key::LeftAlt;
        case SDLK_RSHIFT: return Key::RightShift;
        case SDLK_RCTRL: return Key::RightControl;
        case SDLK_RALT: return Key::RightAlt;
        default: return 0;
    }
}

class SDL2Platform final : public Platform {
public:
    ~SDL2Platform() override {
#ifdef __PSP__
        if (pad_) SDL_JoystickClose(pad_);
#else
        if (pad_) SDL_GameControllerClose(pad_);
#endif
        if (audioDev_) SDL_CloseAudioDevice(audioDev_);
        if (target_) SDL_DestroyTexture(target_);
        if (ren_) SDL_DestroyRenderer(ren_);
        if (win_) SDL_DestroyWindow(win_);
        SDL_Quit();
    }

    bool createWindow(const char* title, int windowW, int windowH, int vw, int vh) override {
        if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS | MG_PAD_INIT | SDL_INIT_AUDIO) != 0) {
            std::fprintf(stderr, "SDL_Init: %s\n", SDL_GetError());
            return false;
        }
#if defined(__PSP__)
        win_ = SDL_CreateWindow(title, 0, 0, 480, 272, 0);   // the PSP screen
#elif defined(__vita__)
        win_ = SDL_CreateWindow(title, 0, 0, 960, 544, SDL_WINDOW_FULLSCREEN);   // the Vita screen
#elif defined(__PS4__)
        // console: one fixed fullscreen surface, no resizing / fullscreen toggling / mouse
        win_ = SDL_CreateWindow(title, 0, 0, 1920, 1080, SDL_WINDOW_FULLSCREEN);
#else
        win_ = SDL_CreateWindow(title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, windowW, windowH, SDL_WINDOW_RESIZABLE);
#endif
        if (!win_) { std::fprintf(stderr, "SDL_CreateWindow: %s\n", SDL_GetError()); return false; }
#if !defined(__PS4__) && !defined(__PSP__) && !defined(__vita__)
        SDL_SetWindowMinimumSize(win_, 480, 360);
#endif
        createRenderer(true);
        setVirtualSize(vw, vh);
        lastTicks_ = SDL_GetPerformanceCounter();
        return ren_ != nullptr;
    }

    void setWindowSize(int w, int h) override {
#if !defined(__PS4__) && !defined(__PSP__) && !defined(__vita__)
        if (!isFullscreen()) SDL_SetWindowSize(win_, w, h);
#else
        (void)w; (void)h;
#endif
    }

    void setVirtualSize(int w, int h) override {
#ifdef MG_NO_RT
        vw_ = w; vh_ = h;
        SDL_RenderSetLogicalSize(ren_, w, h);   // SDL letterboxes the 16:9 / 4:3 virtual screen into 480x272
        return;
#endif
        if (target_ && vw_ == w && vh_ == h) return;
        if (target_) SDL_DestroyTexture(target_);
        vw_ = w; vh_ = h;
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "1");
        target_ = SDL_CreateTexture(ren_, SDL_PIXELFORMAT_RGBA32, SDL_TEXTUREACCESS_TARGET, w, h);
    }

    bool toggleFullscreen() override {
#if defined(__PS4__) || defined(__PSP__) || defined(__vita__)
        return true;   // always fullscreen
#endif
        bool fs = isFullscreen();
        SDL_SetWindowFullscreen(win_, fs ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
        return !fs;
    }
    bool isFullscreen() const override { return (SDL_GetWindowFlags(win_) & SDL_WINDOW_FULLSCREEN_DESKTOP) != 0; }
    int windowWidth() const override { int w, h; SDL_GetWindowSize(win_, &w, &h); return w; }
    int windowHeight() const override { int w, h; SDL_GetWindowSize(win_, &w, &h); return h; }

    void setVSync(bool on) override { vsync_ = on; SDL_RenderSetVSync(ren_, on ? 1 : 0); }
    void setFpsLimit(int fps) override { fpsLimit_ = fps; }

    void pollEvents() override {
        std::fill(std::begin(pressed_), std::end(pressed_), false);
        std::fill(std::begin(mousePressed_), std::end(mousePressed_), false);
        std::fill(std::begin(mouseReleased_), std::end(mouseReleased_), false);
        pressedQueue_.clear();
        SDL_Event e;
        while (SDL_PollEvent(&e)) {
            switch (e.type) {
                case SDL_QUIT: quit_ = true; break;
                case SDL_KEYDOWN: {
                    int k = toRaylibKey(e.key.keysym.sym);
                    if (k > 0 && k < Key::Max) {
                        if (!e.key.repeat) { pressed_[k] = true; pressedQueue_.push_back(k); }
                        down_[k] = true;
                    }
                    break;
                }
                case SDL_KEYUP: {
                    int k = toRaylibKey(e.key.keysym.sym);
                    if (k > 0 && k < Key::Max) down_[k] = false;
                    break;
                }
                case SDL_MOUSEBUTTONDOWN: if (e.button.button <= 3) { int b = btn(e.button.button); mouseDown_[b] = mousePressed_[b] = true; } break;
                case SDL_MOUSEBUTTONUP: if (e.button.button <= 3) { int b = btn(e.button.button); mouseDown_[b] = false; mouseReleased_[b] = true; } break;
#ifndef __PSP__
                case SDL_CONTROLLERDEVICEADDED: if (!pad_) openPad(e.cdevice.which); break;
                case SDL_CONTROLLERDEVICEREMOVED:
                    if (pad_ && e.cdevice.which == SDL_JoystickInstanceID(SDL_GameControllerGetJoystick(pad_))) { SDL_GameControllerClose(pad_); pad_ = nullptr; }
                    break;
#endif
                default: break;
            }
        }
        // mouse position + delta in virtual coordinates
        int mx, my;
        SDL_GetMouseState(&mx, &my);
        updateLayout();
        float vx = (mx - offX_) / scale_, vy = (my - offY_) / scale_;
        mdx_ = vx - mouseX_; mdy_ = vy - mouseY_;
        mouseX_ = vx; mouseY_ = vy;
        updatePad();
    }

    bool shouldClose() const override { return quit_; }
    float frameTime() const override { return dt_; }
    int fps() const override { return fps_; }

    void beginFrame() override {
#ifndef MG_NO_RT
        SDL_SetRenderTarget(ren_, target_);
#endif
        SDL_SetRenderDrawColor(ren_, 20, 20, 40, 255);
        SDL_RenderClear(ren_);
    }

    void endFrame() override {
#ifndef MG_NO_RT
        SDL_SetRenderTarget(ren_, nullptr);
        SDL_SetRenderDrawColor(ren_, 0, 0, 0, 255);
        SDL_RenderClear(ren_);
        updateLayout();
        SDL_FRect dst{offX_, offY_, vw_ * scale_, vh_ * scale_};
        SDL_RenderCopyF(ren_, target_, nullptr, &dst);
#endif
        debugHook();
        SDL_RenderPresent(ren_);

        // frame limiter (when vsync is off, or the limit is below the display rate)
        Uint64 now = SDL_GetPerformanceCounter();
        double freq = (double)SDL_GetPerformanceFrequency();
        if (fpsLimit_ > 0) {
            double target = 1.0 / fpsLimit_;
            double elapsed = (now - lastTicks_) / freq;
            if (elapsed < target) {
                SDL_Delay((Uint32)((target - elapsed) * 1000.0));
                now = SDL_GetPerformanceCounter();
            }
        }
        dt_ = (float)((now - lastTicks_) / freq);
        lastTicks_ = now;
        fpsAcc_ += dt_; fpsFrames_++;
        if (fpsAcc_ >= 0.5) { fps_ = (int)std::lround(fpsFrames_ / fpsAcc_); fpsAcc_ = 0; fpsFrames_ = 0; }
    }

    bool keyDown(int k) const override { return k > 0 && k < Key::Max && (down_[k] || held_[k]); }
    bool keyPressed(int k) const override { return k > 0 && k < Key::Max && pressed_[k]; }
    int keyPressedAny() override {
        if (pressedQueue_.empty()) return 0;
        int k = pressedQueue_.front();
        pressedQueue_.erase(pressedQueue_.begin());
        return k;
    }
    bool mouseDown(int b) const override { return b >= 0 && b < 3 && mouseDown_[b]; }
    bool mousePressed(int b) const override { return b >= 0 && b < 3 && mousePressed_[b]; }
    bool mouseReleased(int b) const override { return b >= 0 && b < 3 && mouseReleased_[b]; }
    void mousePos(float& x, float& y) const override { x = mouseX_; y = mouseY_; }
    void mouseDelta(float& dx, float& dy) const override { dx = mdx_; dy = mdy_; }
    const PadState& pad() const override { return padState_; }

    Texture createTexture(const uint8_t* rgba, int w, int h, bool linear) override {
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, linear ? "1" : "0");
        SDL_Texture* t = SDL_CreateTexture(ren_, SDL_PIXELFORMAT_RGBA32, SDL_TEXTUREACCESS_STATIC, w, h);
        Texture out;
        if (!t) return out;
        SDL_UpdateTexture(t, nullptr, rgba, w * 4);
        SDL_SetTextureBlendMode(t, SDL_BLENDMODE_BLEND);
        out.w = w; out.h = h; out.impl = t;
        return out;
    }

    void destroyTexture(Texture& t) override {
        if (t.impl) SDL_DestroyTexture((SDL_Texture*)t.impl);
        t = Texture();
    }

    void drawTexture(const Texture& t, float srcX, float srcY, float srcW, float srcH,
                     float dstX, float dstY, float dstW, float dstH,
                     float originX, float originY, float rot, Color tint) override {
        if (!t.impl) return;
        auto* tex = (SDL_Texture*)t.impl;
        SDL_SetTextureColorMod(tex, tint.r, tint.g, tint.b);
        SDL_SetTextureAlphaMod(tex, tint.a);
        SDL_Rect src{(int)srcX, (int)srcY, (int)std::abs(srcW), (int)srcH};
        SDL_FRect dst{dstX - originX, dstY - originY, dstW, dstH};
        SDL_FPoint centre{originX, originY};
        SDL_RenderCopyExF(ren_, tex, &src, &dst, rot, &centre, srcW < 0 ? SDL_FLIP_HORIZONTAL : SDL_FLIP_NONE);
    }

    void fillRect(float x, float y, float w, float h, Color c) override {
        SDL_SetRenderDrawBlendMode(ren_, c.a == 255 ? SDL_BLENDMODE_NONE : SDL_BLENDMODE_BLEND);
        SDL_SetRenderDrawColor(ren_, c.r, c.g, c.b, c.a);
        SDL_FRect r{x, y, w, h};
        SDL_RenderFillRectF(ren_, &r);
    }

    bool openAudio(int rate, std::function<void(int16_t*, int)> cb) override {
        callback_ = std::move(cb);
        SDL_AudioSpec want{}, have{};
        want.freq = rate; want.format = AUDIO_S16SYS; want.channels = 2; want.samples = 1024;
        want.callback = [](void* user, Uint8* stream, int len) {
            auto* self = (SDL2Platform*)user;
            int frames = len / 4;
            if (self->callback_) self->callback_((int16_t*)stream, frames);
            else SDL_memset(stream, 0, len);
        };
        want.userdata = this;
        audioDev_ = SDL_OpenAudioDevice(nullptr, 0, &want, &have, 0);
        if (!audioDev_) { std::fprintf(stderr, "[audio] %s\n", SDL_GetError()); return false; }
        rate_ = have.freq;
        SDL_PauseAudioDevice(audioDev_, 0);
        return true;
    }
    void lockAudio() override { if (audioDev_) SDL_LockAudioDevice(audioDev_); }
    void unlockAudio() override { if (audioDev_) SDL_UnlockAudioDevice(audioDev_); }
    int audioSampleRate() const override { return rate_; }

private:
    SDL_Window* win_ = nullptr;
    SDL_Renderer* ren_ = nullptr;
    SDL_Texture* target_ = nullptr;
#ifdef __PSP__
    SDL_Joystick* pad_ = nullptr;
#else
    SDL_GameController* pad_ = nullptr;
#endif
    SDL_AudioDeviceID audioDev_ = 0;
    std::function<void(int16_t*, int)> callback_;
    int rate_ = 44100;

    int vw_ = 0, vh_ = 0;
    float scale_ = 1, offX_ = 0, offY_ = 0;
    bool quit_ = false, vsync_ = true;
    int fpsLimit_ = 60;
    Uint64 lastTicks_ = 0;
    float dt_ = 1.f / 60;
    int fps_ = 0, fpsFrames_ = 0;
    float fpsAcc_ = 0;

    bool down_[Key::Max] = {}, pressed_[Key::Max] = {};
    std::vector<int> pressedQueue_;
    bool mouseDown_[3] = {}, mousePressed_[3] = {}, mouseReleased_[3] = {};
    float mouseX_ = 0, mouseY_ = 0, mdx_ = 0, mdy_ = 0;
    PadState padState_;

    // ---- debug hooks for headless checks: MG_SHOT=file.png MG_SHOT_FRAME=n MG_HOLD=263,65 MG_PRESS=frame:key,...
    bool held_[Key::Max] = {};
    int frame_ = 0;
    void debugHook() {
        static const char* shot = std::getenv("MG_SHOT");
        static int shotFrame = std::getenv("MG_SHOT_FRAME") ? std::atoi(std::getenv("MG_SHOT_FRAME")) : 60;
        if (frame_ == 0) {
            if (const char* hold = std::getenv("MG_HOLD")) {
                std::string h = hold;
                size_t i = 0;
                while (i < h.size()) { size_t j = h.find(',', i); if (j == std::string::npos) j = h.size(); int k = std::atoi(h.substr(i, j - i).c_str()); if (k > 0 && k < Key::Max) held_[k] = true; i = j + 1; }
            }
        }
        frame_++;
        if (shot && frame_ == shotFrame) {
            int w, h;
            SDL_GetRendererOutputSize(ren_, &w, &h);
            std::vector<uint8_t> px((size_t)w * h * 4);
            SDL_RenderReadPixels(ren_, nullptr, SDL_PIXELFORMAT_RGBA32, px.data(), w * 4);
            stbi_write_png(shot, w, h, 4, px.data(), w * 4);
            quit_ = true;
        }
    }

    static int btn(int sdlButton) { return sdlButton == SDL_BUTTON_LEFT ? 0 : sdlButton == SDL_BUTTON_RIGHT ? 1 : 2; }

    void createRenderer(bool vsync) {
#ifdef __vita__
        // SDL's native GXM renderer ships precompiled shaders. The default GLES2 renderer goes through vitaGL, which
        // compiles shaders at run time with Sony's libshacccg.suprx (missing in Vita3K, and a separate install on real
        // consoles) and crashed there. Fall back to the default only if GXM is unavailable.
        SDL_SetHint(SDL_HINT_RENDER_DRIVER, "VITA gxm");
        ren_ = SDL_CreateRenderer(win_, -1, SDL_RENDERER_ACCELERATED | (vsync ? SDL_RENDERER_PRESENTVSYNC : 0));
        if (ren_) return;
        std::fprintf(stderr, "[video] VITA gxm renderer unavailable, using SDL's default\n");
        SDL_SetHint(SDL_HINT_RENDER_DRIVER, "");
#endif
        ren_ = SDL_CreateRenderer(win_, -1, SDL_RENDERER_ACCELERATED | (vsync ? SDL_RENDERER_PRESENTVSYNC : 0));
        if (!ren_) ren_ = SDL_CreateRenderer(win_, -1, SDL_RENDERER_SOFTWARE);
        if (!ren_) std::fprintf(stderr, "SDL_CreateRenderer: %s\n", SDL_GetError());
    }

    void updateLayout() {
        int sw, sh;
        SDL_GetWindowSize(win_, &sw, &sh);
        if (vw_ <= 0 || vh_ <= 0) return;
        scale_ = std::min((float)sw / vw_, (float)sh / vh_);
        offX_ = (sw - vw_ * scale_) / 2;
        offY_ = (sh - vh_ * scale_) / 2;
    }

#ifdef __PSP__
    // PSP: SDL exposes the console buttons as a plain joystick (0 triangle, 1 circle, 2 cross, 3 square, 4 L, 5 R,
    // 6 down, 7 left, 8 up, 9 right, 10 select, 11 start) plus the analog nub on axes 0 / 1.
    void openPad(int index) { pad_ = SDL_JoystickOpen(index); }
    void updatePad() {
        if (!pad_) openPad(0);
        padState_ = PadState();
        if (!pad_) return;
        padState_.connected = true;
        auto b = [&](int i) { return SDL_JoystickGetButton(pad_, i) != 0; };
        auto& s = padState_.button;
        s[(int)PadButton::Left] = b(7);  s[(int)PadButton::Right] = b(9);
        s[(int)PadButton::Up] = b(8);    s[(int)PadButton::Down] = b(6);
        s[(int)PadButton::A] = b(2);     // cross = confirm / jump
        s[(int)PadButton::B] = b(1);     // circle = back / action X
        s[(int)PadButton::X] = b(3);     // square = action Z
        s[(int)PadButton::Y] = b(0);     // triangle = interact
        s[(int)PadButton::RightBumper] = b(5);
        s[(int)PadButton::Start] = b(11);
        padState_.axisX = SDL_JoystickGetAxis(pad_, 0) / 32767.f;
        padState_.axisY = SDL_JoystickGetAxis(pad_, 1) / 32767.f;
    }
#else
    void openPad(int index) {
        if (SDL_IsGameController(index)) pad_ = SDL_GameControllerOpen(index);
    }

    void updatePad() {
        if (!pad_) { for (int i = 0; i < SDL_NumJoysticks() && !pad_; i++) openPad(i); }
        padState_ = PadState();
        if (!pad_) return;
        padState_.connected = true;
        auto b = [&](SDL_GameControllerButton x) { return SDL_GameControllerGetButton(pad_, x) != 0; };
        auto& s = padState_.button;
        s[(int)PadButton::Left] = b(SDL_CONTROLLER_BUTTON_DPAD_LEFT);
        s[(int)PadButton::Right] = b(SDL_CONTROLLER_BUTTON_DPAD_RIGHT);
        s[(int)PadButton::Up] = b(SDL_CONTROLLER_BUTTON_DPAD_UP);
        s[(int)PadButton::Down] = b(SDL_CONTROLLER_BUTTON_DPAD_DOWN);
        s[(int)PadButton::A] = b(SDL_CONTROLLER_BUTTON_A);
        s[(int)PadButton::B] = b(SDL_CONTROLLER_BUTTON_B);
        s[(int)PadButton::X] = b(SDL_CONTROLLER_BUTTON_X);
        s[(int)PadButton::Y] = b(SDL_CONTROLLER_BUTTON_Y);
        s[(int)PadButton::RightBumper] = b(SDL_CONTROLLER_BUTTON_RIGHTSHOULDER);
        s[(int)PadButton::Start] = b(SDL_CONTROLLER_BUTTON_START);
        padState_.axisX = SDL_GameControllerGetAxis(pad_, SDL_CONTROLLER_AXIS_LEFTX) / 32767.f;
        padState_.axisY = SDL_GameControllerGetAxis(pad_, SDL_CONTROLLER_AXIS_LEFTY) / 32767.f;
    }
#endif
};

} // namespace

std::unique_ptr<Platform> createSDL2Platform() { return std::make_unique<SDL2Platform>(); }

} // namespace mg::plat
