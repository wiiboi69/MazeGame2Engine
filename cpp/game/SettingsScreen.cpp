#include "SettingsScreen.hpp"

namespace mg {

using plat::Color;
namespace Key = plat::Key;
using rt::Pad;
using rt::Screen;

static const std::vector<std::string> Tabs = {"Graphics", "Sound", "Controls", "Game"};

static const std::vector<std::pair<std::string, std::string>> BindRows = {
    {"left", "Move left"}, {"right", "Move right"}, {"up", "Jump / up"}, {"down", "Crouch / down"},
    {"use", "Interact"}, {"z", "Action Z"}, {"x", "Action X"}, {"pause", "Pause"},
    {"savestate", "Save level state"}, {"loadstate", "Load level state"},
};

static const Color White(255, 255, 255);

// ================================================================ applying

void SettingsScreen::applyAll() {
    applyAudio();
    applyGraphics(true);
}

void SettingsScreen::applyAudio() {
    audio_.setMaster(s_.mute ? 0.f : s_.masterVolume);
    audio_.setMusicVolume(s_.musicVolume);
    audio_.sfxVolume = s_.sfxVolume;
}

void SettingsScreen::applyGraphics(bool initial) {
    if (!initial) {
        win_.setAspect(s_.widescreen);
        win_.setWindowScale(s_.windowScale);
    }
    GameConstants::setWidescreen(s_.widescreen);
    rt::P().setVSync(s_.vsync);
    rt::P().setFpsLimit(s_.fpsLimit);
}

// ================================================================ frame

bool SettingsScreen::run(float mouseX, float mouseY) {
    mx_ = mouseX; my_ = mouseY;
    click_ = rt::P().mousePressed(0);
    down_ = rt::P().mouseDown(0);
    if (!down_) drag_ = -1;

    float W = std::min(860, Screen::VW - 60), H = 580;
    float X = (Screen::VW - W) / 2, Y = (Screen::VH - H) / 2;
    bool modal = !confirm_.empty();

    ui_.rect(0, 0, (float)Screen::VW, (float)Screen::VH, Color(0, 0, 0, 150));
    ui_.rect(X, Y, W, H, Color(18, 20, 44, 245), 18, Color(255, 255, 255, 120), 3);
    ui_.text("Settings", X + 30, Y + 52, 38, White, true, 0, true);

    // tabs
    float tw = 130, tx = X + W - 30 - tw * Tabs.size() - 6 * (Tabs.size() - 1);
    for (int i = 0; i < (int)Tabs.size(); i++) {
        float bx = tx + i * (tw + 6);
        bool hot = !modal && inside(bx, Y + 22, tw, 42);
        ui_.rect(bx, Y + 22, tw, 42, i == tab_ ? Color(255, 190, 60) : hot ? Color(70, 76, 130) : Color(40, 44, 80),
                 10, Color(255, 255, 255, 80));
        ui_.text(Tabs[i], bx + tw / 2, Y + 50, 20, i == tab_ ? Color(30, 20, 10) : White, true, 1);
        if (hot && click_ && !capture_) { tab_ = i; tick(); }
    }
    if (!modal && !capture_) {
        if (Pad::keyPressed(Key::Q) || Pad::keyPressed(Key::PageUp)) { tab_ = (tab_ + (int)Tabs.size() - 1) % (int)Tabs.size(); tick(); }
        if (Pad::keyPressed(Key::PageDown)) { tab_ = (tab_ + 1) % (int)Tabs.size(); tick(); }
    }
    ui_.line(X + 24, Y + 78, X + W - 24, Y + 78, Color(255, 255, 255, 70), 2);

    float cx = X + 36, cy = Y + 100, cw = W - 72;
    switch (tab_) {
        case 0: graphics(cx, cy, cw, modal); break;
        case 1: sound(cx, cy, cw, modal); break;
        case 2: controls(cx, cy, cw, modal); break;
        default: gameTab(cx, cy, cw, modal); break;
    }

    // footer
    bool back = button("Back", X + 30, Y + H - 66, 150, 44, modal) ||
                (!modal && !capture_ && Pad::keyPressed(Key::Escape));
    ui_.text("Q / PgUp, PgDn: switch tab", X + W - 30, Y + H - 36, 16, Color(255, 255, 255, 170), false, 2);

    if (!confirm_.empty()) drawConfirm();
    if (back) { capture_.reset(); confirm_.clear(); return true; }
    return false;
}

// ================================================================ tabs

void SettingsScreen::graphics(float x, float y, float w, bool modal) {
    float row = 58;
    int i = 0;
    choice("Aspect ratio", x, y + row * i++, w, modal, {"4:3", "16:9"}, s_.widescreen ? 1 : 0, [&](int v) {
        s_.widescreen = v == 1;
        applyGraphics();
    });
    choice("Window size", x, y + row * i++, w, modal, {"Small", "Medium", "Large"}, s_.windowScale - 1, [&](int v) {
        s_.windowScale = v + 1;
        win_.setWindowScale(s_.windowScale);
    });
    toggle("Fullscreen (F11)", x, y + row * i++, w, modal, s_.fullscreen, [&](bool v) {
        if (rt::P().isFullscreen() != v) win_.toggleFullscreen();
        s_.fullscreen = rt::P().isFullscreen();
    });
    toggle("VSync", x, y + row * i++, w, modal, s_.vsync, [&](bool v) { s_.vsync = v; applyGraphics(true); });
    const int limits[] = {30, 60, 120, 0};
    int cur = 0;
    for (int k = 0; k < 4; k++) if (limits[k] == s_.fpsLimit) cur = k;
    choice("Frame limit", x, y + row * i++, w, modal, {"30", "60", "120", "None"}, cur, [&](int v) {
        s_.fpsLimit = limits[v];
        applyGraphics(true);
    });
    toggle("Show FPS", x, y + row * i++, w, modal, s_.showStats, [&](bool v) { s_.showStats = v; });
    ui_.text("16:9 shows more of the level to the left and right.", x, y + row * i + 14, 17, Color(255, 255, 255, 170));
}

void SettingsScreen::sound(float x, float y, float w, bool modal) {
    float row = 70;
    slider(0, "Master volume", x, y, w, modal, s_.masterVolume, [&](float v) { s_.masterVolume = v; applyAudio(); });
    slider(1, "Music", x, y + row, w, modal, s_.musicVolume, [&](float v) { s_.musicVolume = v; applyAudio(); });
    slider(2, "Sound effects", x, y + row * 2, w, modal, s_.sfxVolume, [&](float v) { s_.sfxVolume = v; applyAudio(); });
    toggle("Mute everything", x, y + row * 3, w, modal, s_.mute, [&](bool v) { s_.mute = v; applyAudio(); });
    if (drag_ == -1 && rt::P().mouseReleased(0) && lastSfxDrag_) audio_.playSfx("coin");
    lastSfxDrag_ = drag_ == 2 || (lastSfxDrag_ && down_);
}

void SettingsScreen::controls(float x, float y, float w, bool modal) {
    if (capture_) captureKey();
    float row = 40;
    ui_.text("Click a key box, then press the new key. Backspace clears it.", x, y + 8, 17, Color(255, 255, 255, 180));
    float ky = y + 28;
    for (int i = 0; i < (int)BindRows.size(); i++) {
        const std::string& action = BindRows[i].first;
        float ry = ky + i * row;
        ui_.text(BindRows[i].second, x, ry + 30, 22, White, false, 0, true);
        for (int slot = 0; slot < 2; slot++) {
            float bx = x + w - 2 * 190 - 12 + slot * 202;
            bool capt = capture_ && capture_->action == action && capture_->slot == slot;
            std::string txt = capt ? "press a key..." : keyLabel(action, slot);
            bool hot = !modal && !capture_ && inside(bx, ry + 4, 190, 36);
            ui_.rect(bx, ry + 4, 190, 36, capt ? Color(255, 190, 60) : hot ? Color(70, 76, 130) : Color(40, 44, 80),
                     8, Color(255, 255, 255, 90));
            ui_.text(txt, bx + 95, ry + 29, 18, capt ? Color(30, 20, 10) : White, true, 1);
            if (hot && click_) { capture_ = Capture{action, slot}; tick(); }
        }
    }
    if (button("Reset to defaults", x + w - 220, y + 28 + BindRows.size() * row + 16, 220, 40, modal || capture_.has_value())) {
        s_.keys = rt::Settings::defaultKeys();
        tick();
    }
}

std::string SettingsScreen::keyLabel(const std::string& action, int slot) const {
    auto it = s_.keys.find(action);
    if (it == s_.keys.end() || slot >= (int)it->second.size() || it->second[slot] == 0) return "-";
    std::string k = rt::keyName(it->second[slot]);
    if (k == "Left") return "Left arrow";
    if (k == "Right") return "Right arrow";
    if (k == "Up") return "Up arrow";
    if (k == "Down") return "Down arrow";
    if (k == "LeftShift") return "L Shift";
    if (k == "RightShift") return "R Shift";
    if (k == "LeftControl") return "L Ctrl";
    if (k == "RightControl") return "R Ctrl";
    return k;
}

void SettingsScreen::captureKey() {
    if (rt::P().mousePressed(0) && captureFrames_ > 1) { capture_.reset(); captureFrames_ = 0; return; }
    captureFrames_++;
    int key = rt::P().keyPressedAny();
    if (key == 0) return;
    Capture c = *capture_;
    capture_.reset();
    captureFrames_ = 0;
    if (key == Key::Escape) return;

    auto cur = s_.keys.count(c.action) ? s_.keys[c.action] : std::vector<int>();
    int arr[2] = {cur.size() > 0 ? cur[0] : 0, cur.size() > 1 ? cur[1] : 0};
    if (key == Key::Backspace || key == Key::Delete) arr[c.slot] = 0;
    else {
        // a key can only do one thing: take it away from whatever used it before
        for (auto& kv : s_.keys) {
            if (kv.first == c.action) continue;
            for (int& v : kv.second) if (v == key) v = 0;
        }
        arr[c.slot] = key;
        if (arr[1 - c.slot] == key) arr[1 - c.slot] = 0;
    }
    s_.keys[c.action] = {arr[0], arr[1]};
    tick();
}

void SettingsScreen::gameTab(float x, float y, float w, bool modal) {
    ui_.text("Save data", x, y + 24, 24, Color(255, 220, 120), true);
    ui_.text("Your three save slots are kept next to the game in the 'saves' folder.", x, y + 56, 18, Color(255, 255, 255, 190));
    if (button("Erase all save slots", x, y + 80, 300, 46, modal)) { confirm_ = "saves"; tick(); }

    ui_.text("Settings", x, y + 190, 24, Color(255, 220, 120), true);
    ui_.text("Put every option on this screen back to its default.", x, y + 222, 18, Color(255, 255, 255, 190));
    if (button("Reset all settings", x, y + 246, 300, 46, modal)) { confirm_ = "settings"; tick(); }
}

// ================================================================ confirm box

void SettingsScreen::drawConfirm() {
    float cx = Screen::VW / 2.f, cy = Screen::VH / 2.f;
    ui_.rect(0, 0, (float)Screen::VW, (float)Screen::VH, Color(0, 0, 0, 150));
    ui_.rect(cx - 220, cy - 100, 440, 200, Color(20, 22, 50, 250), 16, Color(255, 255, 255, 140), 3);
    ui_.text("Are you sure?", cx, cy - 40, 34, White, true, 1, true);
    ui_.text(confirm_ == "saves" ? "This deletes all three save slots." : "Keys, sound and graphics go back to default.",
             cx, cy - 8, 18, Color(255, 255, 255, 210), false, 1);
    bool yes = button("Yes", cx - 170, cy + 30, 150, 46, false);
    bool no = button("No", cx + 20, cy + 30, 150, 46, false);
    if (Pad::keyPressed(Key::Escape) || Pad::keyPressed(Key::N)) no = true;
    if (yes) {
        if (confirm_ == "saves") { for (int i = 0; i < rt::SaveManager::SlotCount; i++) saves_.erase(i); }
        else {
            rt::Settings d;
            s_.masterVolume = d.masterVolume; s_.mute = d.mute; s_.musicVolume = d.musicVolume; s_.sfxVolume = d.sfxVolume;
            s_.widescreen = d.widescreen; s_.windowScale = d.windowScale; s_.vsync = d.vsync; s_.fpsLimit = d.fpsLimit;
            s_.showStats = d.showStats; s_.keys = rt::Settings::defaultKeys();
            applyAudio();
            applyGraphics();
        }
        audio_.playSfx("pop");
        confirm_.clear();
    } else if (no) {
        confirm_.clear();
    }
}

// ================================================================ widgets

bool SettingsScreen::button(const std::string& lbl, float x, float y, float w, float h, bool disabled) {
    bool hot = ui_.button(lbl, x, y, w, h, disabled ? -1000.f : mx_, disabled ? -1000.f : my_);
    if (hot && click_ && !disabled) { tick(); return true; }
    return false;
}

void SettingsScreen::label(const std::string& text, float x, float y) { ui_.text(text, x, y + 32, 22, White, false, 0, true); }

void SettingsScreen::toggle(const std::string& lbl, float x, float y, float w, bool modal, bool value, const std::function<void(bool)>& set) {
    label(lbl, x, y);
    float bw = 150, bx = x + w - bw;
    bool hot = !modal && inside(bx, y + 6, bw, 38);
    ui_.rect(bx, y + 6, bw, 38, value ? Color(90, 190, 90) : hot ? Color(70, 76, 130) : Color(40, 44, 80),
             19, Color(255, 255, 255, 90));
    ui_.text(value ? "On" : "Off", bx + bw / 2, y + 32, 20, White, true, 1);
    if (hot && click_) { tick(); set(!value); }
}

void SettingsScreen::choice(const std::string& lbl, float x, float y, float w, bool modal, const std::vector<std::string>& options, int selected, const std::function<void(int)>& set) {
    label(lbl, x, y);
    float bw = 112, gap = 6;
    float total = options.size() * bw + (options.size() - 1) * gap;
    float bx0 = x + w - total;
    for (int i = 0; i < (int)options.size(); i++) {
        float bx = bx0 + i * (bw + gap);
        bool hot = !modal && inside(bx, y + 6, bw, 38);
        ui_.rect(bx, y + 6, bw, 38, i == selected ? Color(255, 190, 60) : hot ? Color(70, 76, 130) : Color(40, 44, 80),
                 10, Color(255, 255, 255, 90));
        ui_.text(options[i], bx + bw / 2, y + 32, 20, i == selected ? Color(30, 20, 10) : White, true, 1);
        if (hot && click_ && i != selected) { tick(); set(i); }
    }
}

void SettingsScreen::slider(int id, const std::string& lbl, float x, float y, float w, bool modal, float value, const std::function<void(float)>& set) {
    label(lbl, x, y);
    float sx0 = x + w - 340, sx1 = x + w - 60, sy = y + 24;
    ui_.rect(sx0, sy - 5, sx1 - sx0, 10, Color(0, 0, 0, 150), 5, Color(255, 255, 255, 160), 2);
    ui_.rect(sx0, sy - 5, (sx1 - sx0) * value, 10, Color(255, 190, 60), 5);
    ui_.circle(sx0 + (sx1 - sx0) * value, sy, 13, White);
    ui_.text(std::to_string((int)std::nearbyint(value * 100)), x + w, sy + 8, 22, White, true, 2, true);

    if (!modal) {
        if (click_ && mx_ > sx0 - 16 && mx_ < sx1 + 16 && std::abs(my_ - sy) < 20) drag_ = id;
        if (drag_ == id) set(std::clamp((mx_ - sx0) / (sx1 - sx0), 0.f, 1.f));
        else if (inside(sx0 - 16, sy - 20, sx1 - sx0 + 32, 40)) {
            int dir = (Pad::keyPressed(Key::Right) ? 1 : 0) - (Pad::keyPressed(Key::Left) ? 1 : 0);
            if (dir != 0) set(std::clamp(value + dir * 0.05f, 0.f, 1.f));
        }
    }
}

} // namespace mg
