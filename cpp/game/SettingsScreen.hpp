#pragma once
#include "runtime/AppWindow.hpp"
#include "runtime/Audio.hpp"
#include "runtime/Input.hpp"
#include "runtime/Ui.hpp"
#include <optional>

namespace mg {

// Tabbed settings screen (Graphics / Sound / Controls / Game). It edits Settings directly and applies the
// changes to the window and audio as soon as they are made.
class SettingsScreen {
public:
    SettingsScreen(rt::AppWindow& win, rt::Ui& ui, rt::AudioEngine& audio, rt::Settings& s, rt::SaveManager& saves)
        : win_(win), ui_(ui), audio_(audio), s_(s), saves_(saves) {}

    void open() { tab_ = 0; capture_.reset(); confirm_.clear(); drag_ = -1; }
    void applyAll();
    void applyAudio();
    // Draws and updates the screen. Returns true when the player leaves it.
    bool run(float mouseX, float mouseY);

private:
    rt::AppWindow& win_;
    rt::Ui& ui_;
    rt::AudioEngine& audio_;
    rt::Settings& s_;
    rt::SaveManager& saves_;

    int tab_ = 0;
    struct Capture { std::string action; int slot; };
    std::optional<Capture> capture_;
    std::string confirm_;     // "saves" | "settings" | ""
    int drag_ = -1;
    float mx_ = 0, my_ = 0;
    bool click_ = false, down_ = false;
    bool lastSfxDrag_ = false;
    int captureFrames_ = 0;

    void applyGraphics(bool initial = false);
    void graphics(float x, float y, float w, bool modal);
    void sound(float x, float y, float w, bool modal);
    void controls(float x, float y, float w, bool modal);
    void gameTab(float x, float y, float w, bool modal);
    void drawConfirm();
    void captureKey();
    std::string keyLabel(const std::string& action, int slot) const;

    bool inside(float x, float y, float w, float h) const { return mx_ >= x && mx_ < x + w && my_ >= y && my_ < y + h; }
    void tick() { audio_.playSfx("click"); }
    bool button(const std::string& label, float x, float y, float w, float h, bool disabled);
    void label(const std::string& text, float x, float y);
    void toggle(const std::string& label, float x, float y, float w, bool modal, bool value, const std::function<void(bool)>& set);
    void choice(const std::string& label, float x, float y, float w, bool modal, const std::vector<std::string>& options, int selected, const std::function<void(int)>& set);
    void slider(int id, const std::string& label, float x, float y, float w, bool modal, float value, const std::function<void(float)>& set);
};

} // namespace mg
