#pragma once
#include "Common.hpp"
namespace mg {

struct TileDef {
    std::string id;
    int num = 0;
    std::string name;
    std::string shape;     // "" none, "#" solid, "=" one-way, "L" ladder, "R" cloud, "/10" slopes, "T-.." triggers
    std::string group;
    std::string recipes;
    std::string category;
    std::optional<std::string> texture;
    std::optional<std::vector<std::string>> frames;
    real fps = 6;
    bool editorOnly = false;
    int gemValue = 0;
    bool deadly = false;
    bool catchesDeath = false;
    std::optional<std::string> entity;
    bool missing = false;

    bool isGem() const { return gemValue > 0; }

    TileDef(std::string id_, std::string name_, std::string shape_ = "", std::string group_ = "",
            std::string recipes_ = "", std::string category_ = "Custom")
        : id(std::move(id_)), name(std::move(name_)), shape(std::move(shape_)), group(std::move(group_)),
          recipes(std::move(recipes_)), category(std::move(category_)) {}
};

class TileRegistry {
public:
    static const std::vector<TileDef*>& all() { ensureInit(); return s().allTiles; }
    static TileDef* registerTile(TileDef* def);
    static TileDef* registerTile(const std::string& id, const std::string& name, const std::string& shape = "",
                                 const std::optional<std::string>& texture = std::nullopt,
                                 const std::string& category = "Custom", bool deadly = false);
    static void alias(const std::string& oldId, const std::string& newId) { s().aliases[oldId] = newId; }
    static void registerBuiltin(TileDef* def, int num) { add(def, num); }
    static TileDef* find(std::string id);
    static TileDef* resolve(const std::string& id);
    static TileDef* byNum(int num) { ensureInit(); return num >= 0 && (size_t)num < s().byNumV.size() ? s().byNumV[num] : nullptr; }
    static std::string idOf(int num) { auto* d = byNum(num); return d ? d->id : "air"; }
    static int numOf(const std::string& id) { return resolve(id)->num; }
private:
    struct State {
        std::unordered_map<std::string, TileDef*> byId;
        std::unordered_map<std::string, std::string> aliases;
        std::vector<TileDef*> allTiles;
        std::vector<TileDef*> byNumV = std::vector<TileDef*>(256, nullptr);
        int nextCustom = 80;
        bool init = false;
    };
    static State& s() { static State st; return st; }
    static void ensureInit();
    static void add(TileDef* def, int num);
};

// Runtime tile numbers of the built-in tiles (original Scratch costume numbers).
struct TileInfo {
    static constexpr int Blank = 1, Air = 2, LadderTop = 22, Ladder = 23;
    static constexpr int Gem1 = 24, Gem2 = 25, Gem3 = 26, Gem4 = 27;
    static constexpr int PlayerSpawn = 28, Walker = 29, Danger = 30, Cloud = 32, Star = 46, EndBox = 47, Piranha = 68;
    static constexpr int CloudSolid = 75, LogicAuto = 74, LogicDeath = 76, DoorTrigger = 77, PipeTrigger = 78, DoorWideTrigger = 79;
    static constexpr int DefaultSolid = 10;

    static std::string shape(int t) { auto* d = TileRegistry::byNum(t); return d ? d->shape : ""; }
    static std::string group(int t) { auto* d = TileRegistry::byNum(t); return d ? d->group : ""; }
    static std::string recipes(int t) { auto* d = TileRegistry::byNum(t); return d ? d->recipes : ""; }
    static bool isEntityBrush(int t) { auto* d = TileRegistry::byNum(t); return d && d->entity.has_value(); }
    static bool isEditorOnly(int t) { auto* d = TileRegistry::byNum(t); return d && d->editorOnly; }
    static bool isGem(int t) { auto* d = TileRegistry::byNum(t); return d && d->gemValue > 0; }
    static int gemValue(int t) { auto* d = TileRegistry::byNum(t); return d ? d->gemValue : 0; }
    static bool isDeadly(int t) { auto* d = TileRegistry::byNum(t); return d && d->deadly; }
    static bool catchesDeath(int t) { auto* d = TileRegistry::byNum(t); return d && d->catchesDeath; }
};

struct OverworldTileDef {
    std::string id, name;
    std::string category = "Terrain";
    std::string texture;
    std::optional<std::vector<std::string>> frames;
    real fps = 3;
    bool walkable = true;
    real speed = 1;
    OverworldTileDef(std::string i, std::string n) : id(std::move(i)), name(std::move(n)) {}
};

class OverworldTileRegistry {
public:
    static const std::vector<OverworldTileDef*>& all() { return st().ordered; }
    static OverworldTileDef* find(const std::string& id) { auto it = st().byId.find(id); return it == st().byId.end() ? nullptr : it->second; }
    static void registerTile(OverworldTileDef* def) {
        auto& s = st();
        auto it = s.byId.find(def->id);
        if (it != s.byId.end()) s.ordered.erase(std::find(s.ordered.begin(), s.ordered.end(), it->second));
        s.byId[def->id] = def;
        s.ordered.push_back(def);
    }
private:
    struct S { std::unordered_map<std::string, OverworldTileDef*> byId; std::vector<OverworldTileDef*> ordered; };
    static S& st() { static S s; return s; }
};

} // namespace mg
