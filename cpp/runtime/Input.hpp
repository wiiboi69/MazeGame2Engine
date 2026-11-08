#pragma once
#include "Platform.hpp"
#include "Sprites.hpp"
#include "core/Common.hpp"
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace mg::rt {

// ---------------------------------------------------------------- settings (settings.json next to the executable)
struct Settings {
    float masterVolume = 1.f;
    bool mute = false;
    float musicVolume = 0.5f;
    float sfxVolume = 0.8f;
    bool fullscreen = false;
    bool showStats = false;
#if defined(__PSP__) || defined(__vita__)
    bool widescreen = true;      // handheld screens are 16:9
#else
    bool widescreen = false;     // false = 4:3 (960x720), true = 16:9 (1280x720)
#endif
    int windowScale = 3;         // window size as a fraction of the full virtual size: 1, 2 or 3 (thirds)
    bool vsync = true;
    int fpsLimit = 60;
    int lastSlot = 0;
    std::map<std::string, std::vector<int>> keys = defaultKeys();   // action -> key codes (raylib numbering)

    static std::map<std::string, std::vector<int>> defaultKeys();
    static Settings load(const std::string& path);
    void save(const std::string& path) const;
};

// ---------------------------------------------------------------- save slots (saves/slotN.json)
struct SaveSlot {
    bool used = false;
    std::string world, node, level;
    int gems = 0;
    double playTime = 0;
    std::vector<std::string> completed;
    std::map<std::string, std::string> flags;
    std::string savedAt;
    int index = 0;

    static std::string formatTime(double s);
};

class SaveManager {
public:
    static constexpr int SlotCount = 3;
    SaveSlot slots[SlotCount];
    explicit SaveManager(const std::string& dir);
    void save(SaveSlot& s);
    void erase(int i);
private:
    std::string dir_;
    std::string pathOf(int i) const;
    SaveSlot read(int i);
};

// ---------------------------------------------------------------- gamepad + keyboard
enum class PadAct { Left, Right, Up, Down, Confirm, Back, Jump, Z, X, Use, Start, Count };

// First connected controller. D-pad / left stick = move, A = jump / confirm, X = action Z, B = action X / back,
// Y or RB = interact, Start = pause. Menus call keyPressed() instead of the raw platform call so the pad drives them.
struct Pad {
    static void update();                 // once per frame
    static bool connected() { return connected_; }
    static bool down(PadAct a) { return cur_[(int)a]; }
    static bool pressed(PadAct a) { return cur_[(int)a] && !prev_[(int)a]; }
    static bool keyPressed(int key);      // keyboard OR the matching controller input
    static bool keyDown(int key) { return P().keyDown(key); }
private:
    static bool cur_[(int)PadAct::Count], prev_[(int)PadAct::Count], connected_;
};

std::string keyName(int key);              // raylib enum name of a key code ("A", "Left", "Enter"...)

// Turns keyboard state into the core InputState using the configurable bindings.
class GameInput {
public:
    explicit GameInput(Settings& s) : s_(s) {}
    InputState poll();                    // call exactly once per logic tick
    void reset() { useWasDown_ = true; }  // forget held keys (used when menus take over)
private:
    Settings& s_;
    bool useWasDown_ = false;
    bool down(const std::string& action) const;
};

} // namespace mg::rt
