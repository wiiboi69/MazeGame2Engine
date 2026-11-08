#include "LevelLibrary.hpp"
#include "Fs.hpp"
#include "World.hpp"
#include "../third_party/nlohmann/json.hpp"

namespace mg {

int NaturalLess::compare(const std::string& a, const std::string& b) {
    size_t i = 0, j = 0;
    auto dig = [](char c) { return c >= '0' && c <= '9'; };
    while (i < a.size() && j < b.size()) {
        if (dig(a[i]) && dig(b[j])) {
            size_t si = i, sj = j;
            while (i < a.size() && dig(a[i])) i++;
            while (j < b.size() && dig(b[j])) j++;
            std::string na = a.substr(si, i - si), nb = b.substr(sj, j - sj);
            na.erase(0, na.find_first_not_of('0') == std::string::npos ? na.size() : na.find_first_not_of('0'));
            nb.erase(0, nb.find_first_not_of('0') == std::string::npos ? nb.size() : nb.find_first_not_of('0'));
            if (na.size() != nb.size()) return na.size() < nb.size() ? -1 : 1;
            int c = na.compare(nb);
            if (c != 0) return c < 0 ? -1 : 1;
        } else {
            int ca = std::tolower((unsigned char)a[i]), cb = std::tolower((unsigned char)b[j]);
            if (ca != cb) return ca < cb ? -1 : 1;
            i++; j++;
        }
    }
    long ra = (long)(a.size() - i), rb = (long)(b.size() - j);
    return ra < rb ? -1 : ra > rb ? 1 : 0;
}

std::unique_ptr<LevelLibrary> LevelLibrary::loadDirectory(const std::string& dir) {
    auto lib = std::make_unique<LevelLibrary>();
    lib->directory = dir;
    if (!fs::dirExists(dir)) return lib;

    for (auto& file : fs::listFiles(dir, ".json")) {
        std::string name = fs::stem(file);
        if (lower(name) == "game") continue;
        std::string json;
        if (!fs::readFile(fs::join(dir, file), json)) continue;
        std::string id = LevelJson::peekId(json).value_or(name);
        lib->raw_[id] = json;
    }

    // legacy Scratch strings (level_NNN.mgl -> id "level_NNN")
    for (auto& file : fs::listFiles(dir, ".mgl")) {
        std::string name = fs::stem(file);
        if (name.compare(0, 6, "level_") != 0) continue;
        char* end = nullptr;
        long n = std::strtol(name.c_str() + 6, &end, 10);
        if (end == name.c_str() + 6 || *end != 0) continue;
        std::string text;
        fs::readFile(fs::join(dir, file), text);
        while (!text.empty() && std::isspace((unsigned char)text.back())) text.pop_back();
        while (!text.empty() && std::isspace((unsigned char)text.front())) text.erase(0, 1);
        if (n == 1) {
            auto start = LegacyLevelCodec::decodeSettings(text);
            if (start && !lib->startLevelId) { char buf[32]; std::snprintf(buf, sizeof buf, "level_%03d", *start); lib->startLevelId = buf; }
            continue;
        }
        if (lib->raw_.count(name)) continue;
        auto level = LegacyLevelCodec::decode(text);
        if (!level) continue;
        level->id = name;
        lib->raw_[name] = LevelJson::write(*level);
    }

    std::string gameFile = fs::join(dir, "game.json");
    if (fs::fileExists(gameFile)) {
        try {
            std::string text;
            fs::readFile(gameFile, text);
            auto root = nlohmann::json::parse(text);
            if (root.contains("startLevel") && root["startLevel"].is_string()) lib->startLevelId = root["startLevel"].get<std::string>();
            if (root.contains("startWorld") && root["startWorld"].is_string()) lib->startWorldId = root["startWorld"].get<std::string>();
            if (root.contains("playerScript") && root["playerScript"].is_string()) {
                lib->playerScriptId = root["playerScript"].get<std::string>();
                World::defaultPlayerScript() = *lib->playerScriptId;
            }
        } catch (const nlohmann::json::exception& ex) {
            std::fprintf(stderr, "[levels] game.json: %s\n", ex.what());
        }
    }
    return lib;
}

void LevelLibrary::set(const std::string& id, LevelData level) {
    level.id = id;
    raw_[id] = LevelJson::write(level);
    cache_[id] = std::move(level);
}

const std::string* LevelLibrary::getRaw(const std::string& id) const {
    auto it = raw_.find(id);
    return it == raw_.end() ? nullptr : &it->second;
}

bool LevelLibrary::has(const std::optional<std::string>& id) { return id.has_value() && get(*id) != nullptr; }

LevelData* LevelLibrary::get(const std::string& id) {
    if (id.empty()) return nullptr;
    auto c = cache_.find(id);
    if (c != cache_.end()) return c->second ? &*c->second : nullptr;
    std::optional<LevelData> level;
    auto r = raw_.find(id);
    if (r != raw_.end()) {
        try { level = LevelJson::parse(r->second); level->id = id; }
        catch (const InvalidDataException& ex) { std::fprintf(stderr, "[levels] '%s': %s\n", id.c_str(), ex.what()); level.reset(); }
    }
    auto& slot = cache_[id];
    slot = std::move(level);
    return slot ? &*slot : nullptr;
}

void LevelLibrary::remove(const std::string& id) {
    raw_.erase(id);
    cache_.erase(id);
    if (!directory.empty()) {
        std::string f = pathFor(id);
        if (fs::fileExists(f)) fs::removeFile(f);
    }
}

std::vector<std::string> LevelLibrary::ids() {
    std::vector<std::string> keys;
    for (auto& kv : raw_) keys.push_back(kv.first);
    std::vector<std::string> out;
    for (auto& k : keys) if (get(k) != nullptr) out.push_back(k);
    return out;
}

std::optional<std::string> LevelLibrary::startLevel() {
    if (has(startLevelId)) return startLevelId;
    auto all = ids();
    if (all.empty()) return std::nullopt;
    return all.front();
}

std::string LevelLibrary::nextFreeId(const std::string& prefix) {
    int n = 1;
    while (raw_.count(prefix + std::to_string(n))) n++;
    return prefix + std::to_string(n);
}

std::string LevelLibrary::sanitize(const std::string& id) {
    size_t b = 0, e = id.size();
    while (b < e && std::isspace((unsigned char)id[b])) b++;
    while (e > b && std::isspace((unsigned char)id[e - 1])) e--;
    std::string sb;
    for (size_t i = b; i < e; i++) {
        char c = id[i];
        sb += (std::isalnum((unsigned char)c) || c == '_' || c == '-') ? c : '_';
    }
    return sb.empty() ? "level" : sb;
}

std::string LevelLibrary::pathFor(const std::string& id) const {
    return fs::join(directory.empty() ? "." : directory, sanitize(id) + ".json");
}

void LevelLibrary::save(const std::string& id) {
    auto it = raw_.find(id);
    if (directory.empty() || it == raw_.end()) return;
    fs::createDirectories(directory);
    fs::writeFile(pathFor(id), it->second);
}

void LevelLibrary::saveAll() {
    std::vector<std::string> keys;
    for (auto& kv : raw_) keys.push_back(kv.first);
    for (auto& id : keys) save(id);
    saveGameFile();
}

void LevelLibrary::saveGameFile() {
    if (directory.empty()) return;
    fs::createDirectories(directory);
    nlohmann::ordered_json w = nlohmann::ordered_json::object();
    if (startLevelId) w["startLevel"] = *startLevelId;
    if (startWorldId) w["startWorld"] = *startWorldId;
    if (playerScriptId && !playerScriptId->empty()) w["playerScript"] = *playerScriptId;
    fs::writeFile(fs::join(directory, "game.json"), w.dump(2) + "\n");
}

} // namespace mg
