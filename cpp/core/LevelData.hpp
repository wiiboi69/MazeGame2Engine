#pragma once
#include "Common.hpp"
#include "Layer.hpp"
namespace mg {

// An entity placed in a level. x/y is the tile cell (y=0 is the bottom row).
struct EntityDef {
    int x = 0, y = 0;
    std::string type;
    std::map<std::string, std::string> props;   // ordered like a stable file dump

    std::string get(const std::string& name, const std::string& fallback = "") const {
        auto it = props.find(name); return it == props.end() ? fallback : it->second;
    }
    int getInt(const std::string& name, int fallback = 0) const {
        auto it = props.find(name);
        if (it == props.end()) return fallback;
        char* end = nullptr;
        long v = std::strtol(it->second.c_str(), &end, 10);
        return (end != it->second.c_str() && *end == 0) ? (int)v : fallback;
    }
    real getDouble(const std::string& name, real fallback = 0) const {
        auto it = props.find(name);
        if (it == props.end()) return fallback;
        char* end = nullptr;
        double v = std::strtod(it->second.c_str(), &end);
        return (end != it->second.c_str() && *end == 0) ? v : fallback;
    }
    bool getBool(const std::string& name, bool fallback = false) const {
        auto it = props.find(name);
        return it == props.end() ? fallback : (lower(it->second) == "true" || it->second == "1");
    }
    EntityDef clone() const { return *this; }
};

// A decoded level. Tiles are column-major: index = x * height + y, y=0 is the bottom.
struct LevelData {
    int width = 70;
    int height = 40;
    std::vector<int> tiles;
    std::vector<EntityDef> entities;

    std::string id;
    std::optional<std::string> next, left, right;
    std::string script;
    std::string playerScript;
    std::vector<Layer> layers;
    int editLayer = 0;   // editor only: 0 = main layer, n = layers[n-1]

    std::vector<int>& grid() {
        if (editLayer > 0 && (size_t)editLayer <= layers.size() && layers[editLayer - 1].kind == LayerKind::Tiles)
            return layers[editLayer - 1].tiles;
        return tiles;
    }

    std::string backgroundColor = "#f0f000";
    int backdrop = 1;
    int flags = 5;            // bit0 = parallax background, bit1 = underwater
    int cameraMode = 0;
    std::string name = "test";
    int difficulty = 0;
    std::string song = "0";
    int custom = 69;
    int extraFlags = 420;

    bool showBackground() const { return (flags & GameConstants::FlagShowBackground) != 0; }
    bool underwater() const { return (flags & GameConstants::FlagUnderwater) != 0; }

    LevelData() = default;
    LevelData(int w, int h) : width(w), height(h), tiles((size_t)w * h, TileInfo::Air) {}

    int index(int x, int y) const { return x * height + y; }
    bool inBounds(int x, int y) const { return x >= 0 && y >= 0 && x < width && y < height; }
    int get(int x, int y) { return inBounds(x, y) ? grid()[index(x, y)] : 0; }
    void set(int x, int y, int tile) { if (inBounds(x, y)) grid()[index(x, y)] = tile; }

    static LevelData createBlank(int width, int height) {
        LevelData l(width, height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++) {
                bool wall = x == 0 || x == width - 1;
                if (wall) l.set(x, y, TileInfo::DefaultSolid);
                else if (y == 0) l.set(x, y, TileInfo::DefaultSolid);
                else l.set(x, y, TileInfo::Air);
            }
        return l;
    }

    // First spawn marker (tile 28) in original index order.
    std::optional<std::pair<int, int>> findSpawn() const {
        for (size_t i = 0; i < tiles.size(); i++)
            if (tiles[i] == TileInfo::PlayerSpawn) return std::make_pair((int)i / height, (int)i % height);
        return std::nullopt;
    }

    void resize(int newWidth, int newHeight) {
        auto move = [&](const std::vector<int>& old) {
            std::vector<int> n((size_t)newWidth * newHeight, TileInfo::Air);
            for (int x = 0; x < std::min(width, newWidth); x++)
                for (int y = 0; y < std::min(height, newHeight); y++)
                    n[x * newHeight + y] = old[x * height + y];
            return n;
        };
        auto mainT = move(tiles);
        for (auto& l : layers) if (l.kind == LayerKind::Tiles) l.tiles = move(l.tiles);
        entities.erase(std::remove_if(entities.begin(), entities.end(),
                       [&](const EntityDef& e) { return e.x >= newWidth || e.y >= newHeight; }), entities.end());
        width = newWidth; height = newHeight;
        tiles = std::move(mainT);
    }

    LevelData clone() const { return *this; }
};

} // namespace mg
