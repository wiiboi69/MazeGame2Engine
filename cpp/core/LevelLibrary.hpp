#pragma once
#include "Common.hpp"
#include "LevelData.hpp"
#include "LevelJson.hpp"

namespace mg {

// Compares strings so that digit runs are compared as numbers ("level_2" < "level_10").
struct NaturalLess {
    static int compare(const std::string& a, const std::string& b);
    bool operator()(const std::string& a, const std::string& b) const { return compare(a, b) < 0; }
};

// The level store: every level is a JSON file levels/<id>.json (see LevelJson). A level's id is the
// "id" field in the file, or the file name when the field is missing. game.json holds the start level / overworld.
// Old numbered level_NNN.mgl Scratch strings are still imported when no .json with that id exists.
class LevelLibrary {
public:
    std::string directory;
    std::optional<std::string> startLevelId;   // the level a new game starts in when there is no overworld
    std::optional<std::string> startWorldId;   // the overworld (map) a new game starts in, if any
    std::optional<std::string> playerScriptId; // script every player runs unless the level sets its own

    static std::unique_ptr<LevelLibrary> loadDirectory(const std::string& dir);

    void set(const std::string& id, LevelData level);
    const std::string* getRaw(const std::string& id) const;
    bool has(const std::optional<std::string>& id);
    bool has(const std::string& id) { return get(id) != nullptr; }
    // Parsed level or nullptr. The returned instance is shared; clone it before editing.
    LevelData* get(const std::string& id);
    LevelData* get(const std::optional<std::string>& id) { return id ? get(*id) : nullptr; }
    void remove(const std::string& id);
    // Ids of all valid levels in natural order (level_2 before level_10).
    std::vector<std::string> ids();
    std::optional<std::string> startLevel();
    std::string nextFreeId(const std::string& prefix = "level_");
    static std::string sanitize(const std::string& id);
    std::string pathFor(const std::string& id) const;
    void save(const std::string& id);
    void saveAll();
    void saveGameFile();

private:
    std::map<std::string, std::string, NaturalLess> raw_;   // id -> JSON text
    std::map<std::string, std::optional<LevelData>> cache_;
};

} // namespace mg
