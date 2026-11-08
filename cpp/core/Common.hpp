// MazeGame.Core -> C++17. No platform includes in core/.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>
#include <ctime>

namespace mg {

// The C# code uses double everywhere. PSP has a single-precision FPU only, so a console
// build can flip this to float (physics will then differ slightly from the C# build).
using real = double;

// std::random_device can throw on some embedded libcs (PSP newlib), so seed from the clock instead.
inline uint32_t mgSeed() { static uint32_t n = 0; return (uint32_t)std::time(nullptr) * 2654435761u + (uint32_t)std::clock() + (n += 0x9E3779B9u); }

inline std::string lower(std::string s) { for (auto& c : s) c = (char)std::tolower((unsigned char)c); return s; }

struct GameConstants {
    static constexpr int TileSize = 32;
    static constexpr int StageHeight = 360;
    static constexpr int TicksPerSecond = 30;
    static constexpr real Tiny = -0.000001;
    static constexpr int FlagShowBackground = 1 << 0;
    static constexpr int FlagUnderwater = 1 << 1;

    static int stageWidth() { return stageWidthRef(); }
    static bool widescreen() { return widescreenRef(); }
    static void setWidescreen(bool on) { widescreenRef() = on; stageWidthRef() = on ? 640 : 480; }
private:
    static int& stageWidthRef() { static int v = 480; return v; }
    static bool& widescreenRef() { static bool v = false; return v; }
};

struct InputState {
    bool left = false, right = false, up = false, down = false, z = false, x = false, use = false;
    bool usePressed = false;   // first tick the use key is down
    int xAxis() const { return (right ? 1 : 0) - (left ? 1 : 0); }
    int yAxis() const { return (up ? 1 : 0) - (down ? 1 : 0); }
};

} // namespace mg
