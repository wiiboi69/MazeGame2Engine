#pragma once
#include "Sprites.hpp"
#include "core/Common.hpp"
#include "core/GameContent.hpp"

namespace mg::rt {

// Window + virtual screen (960x720, or 1280x720 in 16:9) that is letterboxed into whatever size the window has.
struct AppWindow {
    AppWindow(plat::Platform& p, const char* title, bool fullscreen, bool widescreen, int windowScale) : plat_(p) {
        Screen::platform = &p;
        Screen::VW = widescreen ? 1280 : 960;
        Screen::VH = 720;
        p.createWindow(title, Screen::VW * windowScale / 3, Screen::VH * windowScale / 3, Screen::VW, Screen::VH);
        p.setFpsLimit(60);
        if (fullscreen) p.toggleFullscreen();
    }

    // Switches between the 4:3 (960x720) and 16:9 (1280x720) virtual screen.
    void setAspect(bool widescreen) {
        int w = widescreen ? 1280 : 960;
        if (w == Screen::VW) return;
        Screen::VW = w;
        plat_.setVirtualSize(Screen::VW, Screen::VH);
        GameConstants::setWidescreen(widescreen);
        if (!plat_.isFullscreen()) plat_.setWindowSize(Screen::VW * 2 / 3, Screen::VH * 2 / 3);
    }

    // Resizes the window to scale/3 of the virtual size (1 = 1/3, 2 = 2/3, 3 = full).
    void setWindowScale(int scale) {
        if (!plat_.isFullscreen()) plat_.setWindowSize(Screen::VW * scale / 3, Screen::VH * scale / 3);
    }

    bool shouldClose() const { return plat_.shouldClose(); }
    void toggleFullscreen() { plat_.toggleFullscreen(); }
    void beginVirtual() { plat_.beginFrame(); }
    void endVirtual() { plat_.endFrame(); }
    void mouse(float& x, float& y) const { plat_.mousePos(x, y); }

private:
    plat::Platform& plat_;
};

} // namespace mg::rt
