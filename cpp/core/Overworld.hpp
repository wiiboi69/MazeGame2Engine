#pragma once
#include "Common.hpp"
#include "Layer.hpp"
#include "TileDef.hpp"

namespace mg {

// One stop on an overworld map: a level to play, or a portal to another map.
struct MapNode {
    std::string id, name;
    std::string level;        // level id to play (empty for a pure portal / junction)
    std::string portal;       // if set, entering this node opens another overworld
    std::string portalNode;
    real x = 0, y = 0;
    std::vector<std::string> requires_;   // node ids that must be completed before this one unlocks
    std::vector<std::string> links;       // node ids connected to this one; made two-way on load
};

// A decorative image on the map: the same properties as a level image layer.
struct MapLayer : ImageLayerProps {};

// A multi-layered overworld map (worlds/<id>.json). Coordinates are in map pixels with y pointing DOWN,
// origin at the top-left of the 16:9 view (1280x720).
struct OverworldData {
    static constexpr int CellSize = 32;

    std::string id, name, song, start;
    std::vector<MapLayer> layers;
    std::vector<MapNode> nodes;

    std::string mode = "path";   // "path" = cursor hops along links; "free" = avatar walks the tile map
    bool freeRoam() const { return mode == "free"; }

    int width = 40, height = 23;
    std::vector<std::string> cells;   // row-major (index = y * width + x), y = 0 is the TOP row

    int pixelWidth() const { return width * CellSize; }
    int pixelHeight() const { return height * CellSize; }
    bool hasTiles() const { for (auto& c : cells) if (!c.empty()) return true; return false; }

    std::string cellAt(int x, int y) const {
        return (x < 0 || y < 0 || x >= width || y >= height || (int)cells.size() != width * height) ? "" : cells[(size_t)y * width + x];
    }
    void setCell(int x, int y, const std::string& id) {
        ensureGrid();
        if (x >= 0 && y >= 0 && x < width && y < height) cells[(size_t)y * width + x] = id;
    }
    void ensureGrid() {
        if ((int)cells.size() != width * height) cells.resize((size_t)width * height);
    }
    void resize(int w, int h);

    bool walkableAtPixel(real px, real py) const;
    real speedAtPixel(real px, real py) const;

    std::string toJson() const;
    MapNode* node(const std::string& id);
    MapNode* nodeForLevel(const std::string& level);
    static OverworldData parse(const std::string& json, const std::string& fallbackId);

private:
    std::string rowToString(int y) const;
    void readRow(int y, const std::string& row);
};

// All overworlds found in a worlds folder.
class OverworldLibrary {
public:
    std::string directory;
    std::vector<std::string> ids() const;
    void set(OverworldData w) { std::string id = w.id; worlds_[id] = std::move(w); }
    void save(const OverworldData& w);
    void remove(const std::string& id);
    std::string nextFreeId() const;
    OverworldData* get(const std::optional<std::string>& id) { if (!id) return nullptr; auto it = worlds_.find(*id); return it == worlds_.end() ? nullptr : &it->second; }
    bool any() const { return !worlds_.empty(); }
    static OverworldLibrary load(const std::string& dir);

private:
    std::map<std::string, OverworldData> worlds_;
    std::string pathFor(const std::string& id) const;
};

} // namespace mg
