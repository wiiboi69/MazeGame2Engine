#include "Input.hpp"
#include "core/Fs.hpp"
#include "../third_party/nlohmann/json.hpp"
#include <ctime>

namespace mg::rt {

namespace Key = plat::Key;
using plat::PadButton;
using json = nlohmann::ordered_json;

// ================================================================ Settings

std::map<std::string, std::vector<int>> Settings::defaultKeys() {
    return {
        {"left", {Key::Left, Key::A}},
        {"right", {Key::Right, Key::D}},
        {"up", {Key::Up, Key::W}},
        {"down", {Key::Down, Key::S}},
        {"use", {Key::E, Key::Enter}},
        {"pause", {Key::Escape, Key::P}},
        {"z", {Key::Z}},
        {"x", {Key::X}},
        {"savestate", {Key::F6}},
        {"loadstate", {Key::F7}},
    };
}

Settings Settings::load(const std::string& path) {
    Settings s;
    try {
        std::string text;
        if (!fs::fileExists(path) || !fs::readFile(path, text)) return s;
        json j = json::parse(text);
        auto f = [&](const char* k, auto& dst) {
            auto it = j.find(k);
            if (it != j.end()) { try { dst = it->get<std::decay_t<decltype(dst)>>(); } catch (...) {} }
        };
        f("MasterVolume", s.masterVolume); f("Mute", s.mute); f("MusicVolume", s.musicVolume); f("SfxVolume", s.sfxVolume);
        f("Fullscreen", s.fullscreen); f("ShowStats", s.showStats); f("Widescreen", s.widescreen);
        f("WindowScale", s.windowScale); f("VSync", s.vsync); f("FpsLimit", s.fpsLimit); f("LastSlot", s.lastSlot);
        auto k = j.find("Keys");
        if (k != j.end() && k->is_object()) {
            s.keys.clear();
            for (auto it = k->begin(); it != k->end(); ++it) s.keys[it.key()] = it.value().get<std::vector<int>>();
            for (auto& kv : defaultKeys()) if (!s.keys.count(kv.first)) s.keys[kv.first] = kv.second;
        }
    } catch (...) { /* fall back to defaults */ }
    return s;
}

void Settings::save(const std::string& path) const {
    json j = json::object();
    j["MasterVolume"] = masterVolume; j["Mute"] = mute; j["MusicVolume"] = musicVolume; j["SfxVolume"] = sfxVolume;
    j["Fullscreen"] = fullscreen; j["ShowStats"] = showStats; j["Widescreen"] = widescreen;
    j["WindowScale"] = windowScale; j["VSync"] = vsync; j["FpsLimit"] = fpsLimit; j["LastSlot"] = lastSlot;
    json k = json::object();
    for (auto& kv : keys) k[kv.first] = kv.second;
    j["Keys"] = k;
    fs::writeFile(path, j.dump(2));
}

// ================================================================ Save slots

std::string SaveSlot::formatTime(double s) {
    int t = (int)s;
    char buf[64];
    if (t >= 3600) std::snprintf(buf, sizeof buf, "%dh %02dm", t / 3600, t % 3600 / 60);
    else std::snprintf(buf, sizeof buf, "%dm %02ds", t / 60, t % 60);
    return buf;
}

SaveManager::SaveManager(const std::string& dir) : dir_(dir) {
    for (int i = 0; i < SlotCount; i++) slots[i] = read(i);
}

std::string SaveManager::pathOf(int i) const { return fs::join(dir_, "slot" + std::to_string(i + 1) + ".json"); }

SaveSlot SaveManager::read(int i) {
    SaveSlot s;
    s.index = i;
    try {
        std::string text;
        if (fs::fileExists(pathOf(i)) && fs::readFile(pathOf(i), text)) {
            json j = json::parse(text);
            s.used = j.value("Used", false);
            s.world = j.value("World", std::string());
            s.node = j.value("Node", std::string());
            s.level = j.value("Level", std::string());
            s.gems = j.value("Gems", 0);
            s.playTime = j.value("PlayTime", 0.0);
            s.savedAt = j.value("SavedAt", std::string());
            if (j.contains("Completed")) s.completed = j["Completed"].get<std::vector<std::string>>();
            if (j.contains("Flags")) s.flags = j["Flags"].get<std::map<std::string, std::string>>();
        }
    } catch (...) { /* corrupt slot: treat as empty */ s = SaveSlot(); s.index = i; }
    return s;
}

void SaveManager::save(SaveSlot& s) {
    s.used = true;
    char buf[32];
    std::time_t t = std::time(nullptr);
    std::strftime(buf, sizeof buf, "%Y-%m-%d %H:%M", std::localtime(&t));
    s.savedAt = buf;
    json j = json::object();
    j["Used"] = s.used; j["World"] = s.world; j["Node"] = s.node; j["Level"] = s.level;
    j["Gems"] = s.gems; j["PlayTime"] = s.playTime; j["Completed"] = s.completed;
    j["Flags"] = s.flags; j["SavedAt"] = s.savedAt;
    fs::createDirectories(dir_);
    fs::writeFile(pathOf(s.index), j.dump(2));
}

void SaveManager::erase(int i) {
    if (fs::fileExists(pathOf(i))) fs::removeFile(pathOf(i));
    slots[i] = SaveSlot();
    slots[i].index = i;
}

// ================================================================ Pad

bool Pad::cur_[(int)PadAct::Count] = {};
bool Pad::prev_[(int)PadAct::Count] = {};
bool Pad::connected_ = false;

void Pad::update() {
    std::copy(std::begin(cur_), std::end(cur_), std::begin(prev_));
    std::fill(std::begin(cur_), std::end(cur_), false);
    const auto& p = P().pad();
    connected_ = p.connected;
    if (!p.connected) return;

    constexpr float Dead = 0.45f;
    auto b = [&](PadButton x) { return p.button[(int)x]; };
    bool left = b(PadButton::Left) || p.axisX < -Dead, right = b(PadButton::Right) || p.axisX > Dead;
    bool up = b(PadButton::Up) || p.axisY < -Dead, down = b(PadButton::Down) || p.axisY > Dead;
    bool a = b(PadButton::A);
    cur_[(int)PadAct::Left] = left; cur_[(int)PadAct::Right] = right; cur_[(int)PadAct::Up] = up; cur_[(int)PadAct::Down] = down;
    cur_[(int)PadAct::Confirm] = a;
    cur_[(int)PadAct::Back] = b(PadButton::B);
    cur_[(int)PadAct::Jump] = a || up;
    cur_[(int)PadAct::Z] = b(PadButton::X);
    cur_[(int)PadAct::X] = b(PadButton::B);
    cur_[(int)PadAct::Use] = b(PadButton::Y) || b(PadButton::RightBumper);
    cur_[(int)PadAct::Start] = b(PadButton::Start);
}

bool Pad::keyPressed(int k) {
    if (P().keyPressed(k)) return true;
    switch (k) {
        case Key::Up: case Key::W: return pressed(PadAct::Up);
        case Key::Down: case Key::S: return pressed(PadAct::Down);
        case Key::Left: case Key::A: return pressed(PadAct::Left);
        case Key::Right: case Key::D: return pressed(PadAct::Right);
        case Key::Enter: case Key::Space: case Key::E: return pressed(PadAct::Confirm);
        case Key::Escape: return pressed(PadAct::Back) || pressed(PadAct::Start);
    }
    return false;
}

std::string keyName(int k) {
    if (k >= Key::A && k <= Key::A + 25) return std::string(1, (char)('A' + (k - Key::A)));
    if (k >= Key::Zero && k <= Key::Nine) {
        static const char* const digits[] = {"Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"};
        return digits[k - Key::Zero];
    }
    if (k >= Key::F1 && k <= Key::F1 + 11) return "F" + std::to_string(k - Key::F1 + 1);
    switch (k) {
        case Key::Space: return "Space"; case Key::Apostrophe: return "Apostrophe"; case Key::Comma: return "Comma";
        case Key::Minus: return "Minus"; case Key::Period: return "Period"; case Key::Slash: return "Slash";
        case Key::Semicolon: return "Semicolon"; case Key::Equal: return "Equal";
        case Key::LeftBracket: return "LeftBracket"; case Key::Backslash: return "Backslash";
        case Key::RightBracket: return "RightBracket"; case Key::Grave: return "Grave";
        case Key::Escape: return "Escape"; case Key::Enter: return "Enter"; case Key::Tab: return "Tab";
        case Key::Backspace: return "Backspace"; case Key::Insert: return "Insert"; case Key::Delete: return "Delete";
        case Key::Right: return "Right"; case Key::Left: return "Left"; case Key::Down: return "Down"; case Key::Up: return "Up";
        case Key::PageUp: return "PageUp"; case Key::PageDown: return "PageDown"; case Key::Home: return "Home"; case Key::End: return "End";
        case Key::CapsLock: return "CapsLock";
        case Key::LeftShift: return "LeftShift"; case Key::LeftControl: return "LeftControl"; case Key::LeftAlt: return "LeftAlt";
        case Key::RightShift: return "RightShift"; case Key::RightControl: return "RightControl"; case Key::RightAlt: return "RightAlt";
    }
    return "Key" + std::to_string(k);
}

// ================================================================ GameInput

bool GameInput::down(const std::string& action) const {
    auto it = s_.keys.find(action);
    if (it == s_.keys.end()) return false;
    for (int k : it->second) if (P().keyDown(k)) return true;
    return false;
}

InputState GameInput::poll() {
    InputState s;
    s.left = down("left") || Pad::down(PadAct::Left);
    s.right = down("right") || Pad::down(PadAct::Right);
    s.up = down("up") || Pad::down(PadAct::Jump);
    s.down = down("down") || Pad::down(PadAct::Down);
    s.z = down("z") || Pad::down(PadAct::Z);
    s.x = down("x") || Pad::down(PadAct::X);
    s.use = down("use") || Pad::down(PadAct::Use);
    s.usePressed = s.use && !useWasDown_;
    useWasDown_ = s.use;
    return s;
}

} // namespace mg::rt
