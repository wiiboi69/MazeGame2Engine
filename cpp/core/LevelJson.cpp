#include "LevelJson.hpp"
#include "../third_party/nlohmann/json.hpp"

namespace mg {

using json = nlohmann::ordered_json;

namespace {

bool parseInt(const std::string& s, int& out) {
    if (s.empty()) return false;
    char* end = nullptr;
    long v = std::strtol(s.c_str(), &end, 10);
    if (end == s.c_str() || *end != 0) return false;
    out = (int)v;
    return true;
}

json rowsToJson(const std::vector<int>& grid, int width, int height) {
    json rows = json::array();
    std::string sb;
    for (int y = height - 1; y >= 0; y--) {
        sb.clear();
        int run = 0, cur = -1;
        auto flush = [&]() {
            if (run == 0) return;
            if (!sb.empty()) sb += ' ';
            sb += TileRegistry::idOf(cur);
            if (run > 1) { sb += '*'; sb += std::to_string(run); }
        };
        for (int x = 0; x < width; x++) {
            int t = grid[(size_t)x * height + y];
            if (run > 0 && t == cur) { run++; continue; }
            flush();
            cur = t;
            run = 1;
        }
        flush();
        rows.push_back(sb);
    }
    return rows;
}

std::vector<std::string> splitSpaces(const std::string& s) {
    std::vector<std::string> out;
    size_t i = 0;
    while (i < s.size()) {
        while (i < s.size() && s[i] == ' ') i++;
        size_t b = i;
        while (i < s.size() && s[i] != ' ') i++;
        if (i > b) out.push_back(s.substr(b, i - b));
    }
    return out;
}

std::vector<int> readRows(const json* rows, int width, int height) {
    std::vector<int> grid((size_t)width * height, TileInfo::Air);
    if (!rows || !rows->is_array()) return grid;
    int r = 0;
    std::unordered_map<std::string, int> cache;
    for (auto& row : *rows) {
        int y = height - 1 - r++;
        if (y < 0) break;
        int x = 0;
        std::string text = row.is_string() ? row.get<std::string>() : "";
        for (auto& token : splitSpaces(text)) {
            std::string id = token;
            int count = 1;
            size_t star = token.rfind('*');
            int c;
            if (star != std::string::npos && star > 0 && parseInt(token.substr(star + 1), c)) {
                id = token.substr(0, star);
                count = std::max(1, c);
            }
            auto it = cache.find(id);
            int num;
            if (it == cache.end()) { num = TileRegistry::numOf(id); cache[id] = num; } else num = it->second;
            for (int k = 0; k < count && x < width; k++) grid[(size_t)(x++) * height + y] = num;
        }
    }
    return grid;
}

int jInt(const json& e, const char* name, int fallback) {
    auto it = e.find(name);
    if (it == e.end()) return fallback;
    if (it->is_number_integer()) return it->get<int>();
    if (it->is_number_float()) { double d = it->get<double>(); if (d == std::floor(d)) return (int)d; }
    return fallback;
}
std::string jStr(const json& e, const char* name, const std::string& fallback) {
    auto it = e.find(name);
    if (it == e.end()) return fallback;
    return it->is_string() ? it->get<std::string>() : it->dump();
}
std::optional<std::string> jNullableStr(const json& e, const char* name) {
    auto it = e.find(name);
    if (it != e.end() && it->is_string() && !it->get<std::string>().empty()) return it->get<std::string>();
    return std::nullopt;
}
bool jBool(const json& e, const char* name, bool fallback) {
    auto it = e.find(name);
    return (it != e.end() && it->is_boolean()) ? it->get<bool>() : fallback;
}

json layerToJson(const LevelData& l, const Layer& layer) {
    json w = json::object();
    w["name"] = layer.name;
    w["kind"] = layer.kind == LayerKind::Image ? "image" : "tiles";
    w["depth"] = layer.front ? "front" : "back";
    if (!layer.visible) w["visible"] = false;
    if (layer.parallax != 1) w["parallax"] = layer.parallax;
    if (layer.kind == LayerKind::Image) {
        w["image"] = layer.image;
        w["x"] = layer.x;
        w["y"] = layer.y;
        if (layer.scale != 1) w["scale"] = layer.scale;
        std::string rep = std::string(layer.repeatX ? "x" : "") + (layer.repeatY ? "y" : "");
        if (!rep.empty()) w["repeat"] = rep;
        if (layer.fitScreen) w["fit"] = true;
    } else {
        w["tiles"] = rowsToJson(layer.tiles, l.width, l.height);
    }
    return w;
}

Layer layerFromJson(const json& e, const LevelData& l) {
    Layer layer;
    layer.name = jStr(e, "name", "layer");
    layer.kind = jStr(e, "kind", "tiles") == "image" ? LayerKind::Image : LayerKind::Tiles;
    layer.front = jStr(e, "depth", "back") == "front";
    layer.visible = jBool(e, "visible", true);
    auto par = e.find("parallax");
    if (par != e.end()) {
        if (par->is_number()) layer.parallax = par->get<double>();
        else if (par->is_array() && !par->empty() && (*par)[0].is_number()) layer.parallax = (*par)[0].get<double>();   // older files stored [x, y]
    }
    if (layer.kind == LayerKind::Image) {
        layer.image = jStr(e, "image", "");
        auto lx = e.find("x"); if (lx != e.end() && lx->is_number()) layer.x = lx->get<double>();
        auto ly = e.find("y"); if (ly != e.end() && ly->is_number()) layer.y = ly->get<double>();
        auto sc = e.find("scale"); if (sc != e.end() && sc->is_number()) layer.scale = sc->get<double>();
        std::string rep = jStr(e, "repeat", "");
        layer.repeatX = rep.find('x') != std::string::npos;
        layer.repeatY = rep.find('y') != std::string::npos;
        layer.fitScreen = jBool(e, "fit", false);
    } else {
        auto rows = e.find("tiles");
        layer.tiles = readRows(rows != e.end() ? &*rows : nullptr, l.width, l.height);
    }
    return layer;
}

json scalar(const std::string& value) {
    // numbers that round-trip as integers are written as numbers, true/false as booleans
    if (!value.empty()) {
        char* end = nullptr;
        long long n = std::strtoll(value.c_str(), &end, 10);
        if (end != value.c_str() && *end == 0 && std::to_string(n) == value) return n;
    }
    if (value == "true") return true;
    if (value == "false") return false;
    return value;
}

} // namespace

std::string LevelJson::write(const LevelData& l) {
    json w = json::object();
    w["format"] = FormatVersion;
    w["id"] = l.id;
    w["name"] = l.name;
    if (l.next) w["next"] = *l.next;
    if (l.left) w["left"] = *l.left;
    if (l.right) w["right"] = *l.right;
    if (!l.script.empty()) w["script"] = l.script;
    if (!l.playerScript.empty()) w["playerScript"] = l.playerScript;
    w["width"] = l.width;
    w["height"] = l.height;
    w["background"] = l.backgroundColor;
    w["backdrop"] = l.backdrop;
    w["parallax"] = l.showBackground();
    w["underwater"] = l.underwater();
    int extraBits = l.flags & ~(GameConstants::FlagShowBackground | GameConstants::FlagUnderwater);
    if (extraBits != 0) w["flagBits"] = extraBits;
    w["camera"] = l.cameraMode;
    w["song"] = l.song;
    w["difficulty"] = l.difficulty;
    w["custom"] = l.custom;
    w["extraFlags"] = l.extraFlags;
    w["tiles"] = rowsToJson(l.tiles, l.width, l.height);
    if (!l.layers.empty()) {
        json layers = json::array();
        for (auto& layer : l.layers) layers.push_back(layerToJson(l, layer));
        w["layers"] = layers;
    }
    json ents = json::array();
    for (auto& e : l.entities) {
        json o = json::object();
        o["type"] = e.type;
        o["x"] = e.x;
        o["y"] = e.y;
        if (!e.props.empty()) {
            json p = json::object();
            for (auto& kv : e.props) p[kv.first] = scalar(kv.second);
            o["props"] = p;
        }
        ents.push_back(o);
    }
    w["entities"] = ents;
    return w.dump(2) + "\n";
}

LevelData LevelJson::parse(const std::string& text) {
    json root;
    try { root = json::parse(text, nullptr, true, /*ignore_comments*/ true); }
    catch (const json::exception& ex) { throw InvalidDataException(std::string("not valid JSON: ") + ex.what()); }
    if (!root.is_object()) throw InvalidDataException("level must be a JSON object");

    LevelData l;
    l.width = jInt(root, "width", 0);
    l.height = jInt(root, "height", 0);
    l.id = jStr(root, "id", "");
    l.name = jStr(root, "name", "level");
    l.next = jNullableStr(root, "next");
    l.left = jNullableStr(root, "left");
    l.right = jNullableStr(root, "right");
    l.script = jStr(root, "script", "");
    l.playerScript = jStr(root, "playerScript", "");
    l.backgroundColor = jStr(root, "background", "#f0f000");
    l.backdrop = jInt(root, "backdrop", 1);
    l.cameraMode = jInt(root, "camera", 0);
    l.song = jStr(root, "song", "0");
    l.difficulty = jInt(root, "difficulty", 0);
    l.custom = jInt(root, "custom", 69);
    l.extraFlags = jInt(root, "extraFlags", 420);
    if (l.width <= 0 || l.height <= 0 || (long long)l.width * l.height > 4000000)
        throw InvalidDataException("bad width/height");

    int flags = jInt(root, "flagBits", 0);
    if (jBool(root, "parallax", true)) flags |= GameConstants::FlagShowBackground;
    if (jBool(root, "underwater", false)) flags |= GameConstants::FlagUnderwater;
    l.flags = flags;

    auto rows = root.find("tiles");
    l.tiles = readRows(rows != root.end() ? &*rows : nullptr, l.width, l.height);

    auto layers = root.find("layers");
    if (layers != root.end() && layers->is_array())
        for (auto& le : *layers) if (le.is_object()) l.layers.push_back(layerFromJson(le, l));

    auto ents = root.find("entities");
    if (ents != root.end() && ents->is_array()) {
        for (auto& e : *ents) {
            if (!e.is_object()) continue;
            std::string type = jStr(e, "type", "");
            if (type.empty()) continue;
            EntityDef def;
            def.type = type; def.x = jInt(e, "x", 0); def.y = jInt(e, "y", 0);
            auto props = e.find("props");
            if (props != e.end() && props->is_object()) {
                for (auto it = props->begin(); it != props->end(); ++it) {
                    const json& v = it.value();
                    std::string s;
                    if (v.is_string()) s = v.get<std::string>();
                    else if (v.is_boolean()) s = v.get<bool>() ? "true" : "false";
                    else if (v.is_null()) s = "";
                    else s = v.dump();
                    def.props[it.key()] = s;
                }
            }
            l.entities.push_back(std::move(def));
        }
    }
    return l;
}

std::optional<std::string> LevelJson::peekId(const std::string& text) {
    try {
        json root = json::parse(text, nullptr, true, true);
        if (!root.is_object()) return std::nullopt;
        return jNullableStr(root, "id");
    } catch (const json::exception&) { return std::nullopt; }
}

// ================================================================ legacy "mg2ex" strings

namespace {
struct Reader {
    const std::string& s;
    size_t i = 0;
    explicit Reader(const std::string& str) : s(str) {}
    bool end() const { return i >= s.size(); }
    char next() { return i < s.size() ? s[i++] : '\0'; }
    std::string token() {
        size_t start = i;
        while (i < s.size() && s[i] != '_') i++;
        std::string t = s.substr(start, i - start);
        if (i < s.size()) i++;
        return t;
    }
    std::string digits() {
        size_t start = i;
        while (i < s.size() && s[i] >= '0' && s[i] <= '9') i++;
        return s.substr(start, i - start);
    }
};

int parseIntOr(const std::string& s, int fallback) { int v; return parseInt(s, v) ? v : fallback; }

std::string decodeHexText(const std::string& hex) {
    if (hex.empty() || hex.size() % 2 != 0) return "";
    std::string out;
    for (size_t i = 0; i < hex.size(); i += 2) {
        auto nib = [](char c) -> int { if (c >= '0' && c <= '9') return c - '0'; if (c >= 'a' && c <= 'f') return c - 'a' + 10; if (c >= 'A' && c <= 'F') return c - 'A' + 10; return -1; };
        int a = nib(hex[i]), b = nib(hex[i + 1]);
        if (a < 0 || b < 0) return "";
        out += (char)(a * 16 + b);
    }
    return out;
}
} // namespace

std::optional<LevelData> LegacyLevelCodec::decode(const std::string& text) {
    if (text.empty()) return std::nullopt;
    Reader r(text);
    if (r.token() != "1") return std::nullopt;   // validity marker

    LevelData level;
    level.width = parseIntOr(r.token(), 0);
    level.height = parseIntOr(r.token(), 0);
    if (level.width <= 0 || level.height <= 0 || (long long)level.width * level.height > 4000000) return std::nullopt;
    level.backgroundColor = r.token();

    r.token(); r.token();                       // two reserved "1"s
    level.backdrop = parseIntOr(r.token(), 1);
    r.token(); r.token();                       // two reserved "1"s
    level.flags = parseIntOr(r.token(), 0);
    level.cameraMode = parseIntOr(r.token(), 0) & 0xFF;
    for (int i = 0; i < 9; i++) r.token();      // "nul" x9
    r.token(); r.token();                       // fork / format version
    r.token(); r.token(); r.token();
    level.name = r.token();
    level.difficulty = parseIntOr(r.token(), 0);
    level.song = r.token();
    for (int i = 0; i < 4; i++) r.token();
    level.custom = parseIntOr(r.token(), 0);
    level.extraFlags = parseIntOr(r.token(), 0);

    // ---- tiles
    int w = level.width, h = level.height;
    level.tiles.assign((size_t)w * h, 0);
    int filled = 0;
    int cx = 0, cy = 0;                         // walk: y outer, x inner
    while (filled < w * h && !r.end()) {
        std::string digits = r.digits();
        int tile = digits.empty() ? TileInfo::Air : parseIntOr(digits, TileInfo::Air);
        char letter = r.next();
        int run = (letter >= 'a' && letter <= 'z') ? letter - 'a' + 1 : 1;
        for (int k = 0; k < run && filled < w * h; k++) {
            level.tiles[(size_t)cx * h + cy] = tile;
            filled++;
            cx++;
            if (cx >= w) { cx = 0; cy++; }
        }
    }
    for (auto& t : level.tiles) if (t == 0) t = TileInfo::Air;

    // ---- objects
    int count = parseIntOr(r.token(), 0);
    for (int i = 0; i < count && !r.end(); i++) {
        int idx = parseIntOr(r.token(), 0);
        int type = parseIntOr(r.token(), 0);
        std::string param = r.token();
        if (idx < 1 || type <= 0) continue;
        auto* brush = TileRegistry::byNum(type);
        if (!brush || !brush->entity) continue;
        EntityDef def;
        def.x = (idx - 1) / h; def.y = (idx - 1) % h; def.type = *brush->entity;
        if (*brush->entity == "npc") { std::string t = decodeHexText(param); if (!t.empty()) def.props["text"] = t; }
        else if (*brush->entity == "door" || *brush->entity == "pipe" || *brush->entity == "door_wide") {
            int tn;
            if (parseInt(param, tn)) { char buf[32]; std::snprintf(buf, sizeof buf, "level_%03d", tn); def.props["target"] = buf; }
        }
        level.entities.push_back(std::move(def));
    }
    return level;
}

std::optional<int> LegacyLevelCodec::decodeSettings(const std::string& text) {
    if (text.size() < 3 || text.compare(0, 3, "gs_") != 0) return std::nullopt;
    size_t a = text.find('_');
    size_t b = text.find('_', a + 1);
    std::string part = text.substr(a + 1, b == std::string::npos ? std::string::npos : b - a - 1);
    int n;
    if (parseInt(part, n)) return n;
    return std::nullopt;
}

} // namespace mg
