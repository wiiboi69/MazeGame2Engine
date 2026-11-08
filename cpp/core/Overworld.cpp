#include "Overworld.hpp"
#include "Fs.hpp"
#include "../third_party/nlohmann/json.hpp"

namespace mg {

using json = nlohmann::ordered_json;

void OverworldData::resize(int w, int h) {
    w = std::clamp(w, 10, 300); h = std::clamp(h, 6, 200);
    ensureGrid();
    std::vector<std::string> n((size_t)w * h);
    for (int y = 0; y < std::min(h, height); y++)
        for (int x = 0; x < std::min(w, width); x++) n[(size_t)y * w + x] = cells[(size_t)y * width + x];
    width = w; height = h; cells = std::move(n);
}

bool OverworldData::walkableAtPixel(real px, real py) const {
    if (px < 0 || py < 0 || px >= pixelWidth() || py >= pixelHeight()) return false;
    std::string cid = cellAt((int)(px / CellSize), (int)(py / CellSize));
    if (cid.empty()) return true;
    auto* d = OverworldTileRegistry::find(cid);
    return d ? d->walkable : true;
}

real OverworldData::speedAtPixel(real px, real py) const {
    auto* d = OverworldTileRegistry::find(cellAt((int)(px / CellSize), (int)(py / CellSize)));
    return d ? d->speed : 1;
}

std::string OverworldData::rowToString(int y) const {
    std::string out;
    int x = 0;
    while (x < width) {
        const std::string& cid = cells[(size_t)y * width + x];
        int run = 1;
        while (x + run < width && cells[(size_t)y * width + x + run] == cid) run++;
        std::string token = cid.empty() ? "-" : cid;
        if (!out.empty()) out += ' ';
        out += run > 1 ? token + "*" + std::to_string(run) : token;
        x += run;
    }
    return out;
}

void OverworldData::readRow(int y, const std::string& row) {
    int x = 0;
    size_t i = 0;
    while (i < row.size()) {
        while (i < row.size() && row[i] == ' ') i++;
        size_t b = i;
        while (i < row.size() && row[i] != ' ') i++;
        if (i == b) break;
        std::string tok = row.substr(b, i - b);
        std::string cid = tok;
        int count = 1;
        size_t star = tok.rfind('*');
        if (star != std::string::npos && star > 0) {
            char* end = nullptr;
            long c = std::strtol(tok.c_str() + star + 1, &end, 10);
            if (end != tok.c_str() + star + 1 && *end == 0) { cid = tok.substr(0, star); count = (int)c; }
        }
        if (cid == "-") cid = "";
        for (int k = 0; k < count && x < width; k++) cells[(size_t)y * width + x++] = cid;
    }
}

MapNode* OverworldData::node(const std::string& id) {
    for (auto& n : nodes) if (n.id == id) return &n;
    return nullptr;
}
MapNode* OverworldData::nodeForLevel(const std::string& level) {
    for (auto& n : nodes) if (n.level == level) return &n;
    return nullptr;
}

std::string OverworldData::toJson() const {
    json o = json::object();
    o["id"] = id; o["name"] = name;
    if (!song.empty()) o["song"] = song;
    o["start"] = start;
    o["mode"] = mode;
    o["width"] = width;
    o["height"] = height;
    if (hasTiles()) {
        OverworldData copy = *this;
        copy.ensureGrid();
        json rows = json::array();
        for (int y = 0; y < height; y++) rows.push_back(copy.rowToString(y));
        o["tiles"] = rows;
    }
    json ls = json::array();
    for (auto& l : layers) {
        json j = json::object();
        j["image"] = l.image; j["x"] = l.x; j["y"] = l.y; j["scale"] = l.scale; j["parallax"] = l.parallax;
        std::string rep = std::string(l.repeatX ? "x" : "") + (l.repeatY ? "y" : "");
        if (!rep.empty()) j["repeat"] = rep;
        if (l.fitScreen) j["fit"] = true;
        ls.push_back(j);
    }
    o["layers"] = ls;
    json ns = json::array();
    for (auto& n : nodes) {
        json j = json::object();
        j["id"] = n.id; j["name"] = n.name;
        if (!n.level.empty()) j["level"] = n.level;
        if (!n.portal.empty()) j["portal"] = n.portal;
        if (!n.portalNode.empty()) j["portalNode"] = n.portalNode;
        j["x"] = n.x; j["y"] = n.y;
        if (!n.requires_.empty()) j["requires"] = n.requires_;
        if (!n.links.empty()) j["links"] = n.links;
        ns.push_back(j);
    }
    o["nodes"] = ns;
    return o.dump(2);
}

namespace {
std::string str(const json& j, const char* k, const std::string& fb) {
    auto it = j.find(k);
    return (it != j.end() && it->is_string()) ? it->get<std::string>() : fb;
}
double num(const json& j, const char* k, double fb) {
    auto it = j.find(k);
    return (it != j.end() && it->is_number()) ? it->get<double>() : fb;
}
}

OverworldData OverworldData::parse(const std::string& text, const std::string& fallbackId) {
    json root = json::parse(text, nullptr, true, true);
    OverworldData w;
    w.id = str(root, "id", fallbackId);
    w.name = str(root, "name", fallbackId);
    w.song = str(root, "song", "");
    w.start = str(root, "start", "");
    w.mode = str(root, "mode", "") == "free" ? "free" : "path";
    w.width = std::clamp((int)num(root, "width", 40), 10, 300);
    w.height = std::clamp((int)num(root, "height", 23), 6, 200);
    w.ensureGrid();
    auto trows = root.find("tiles");
    if (trows != root.end() && trows->is_array())
        for (int y = 0; y < std::min((int)trows->size(), w.height); y++)
            w.readRow(y, (*trows)[y].is_string() ? (*trows)[y].get<std::string>() : "");
    auto ls = root.find("layers");
    if (ls != root.end() && ls->is_array()) {
        for (auto& l : *ls) {
            MapLayer m;
            m.image = str(l, "image", "");
            m.x = num(l, "x", 0); m.y = num(l, "y", 0);
            m.scale = num(l, "scale", 1); m.parallax = num(l, "parallax", 1);
            std::string rep = str(l, "repeat", "");
            m.repeatX = rep.find('x') != std::string::npos;
            m.repeatY = rep.find('y') != std::string::npos;
            auto fit = l.find("fit");
            m.fitScreen = fit != l.end() && fit->is_boolean() && fit->get<bool>();
            w.layers.push_back(m);
        }
    }
    auto ns = root.find("nodes");
    if (ns != root.end() && ns->is_array()) {
        for (auto& n : *ns) {
            if (!n.is_object()) continue;
            MapNode m;
            m.id = str(n, "id", ""); m.name = str(n, "name", ""); m.level = str(n, "level", "");
            m.portal = str(n, "portal", ""); m.portalNode = str(n, "portalNode", "");
            m.x = num(n, "x", 0); m.y = num(n, "y", 0);
            auto rq = n.find("requires");
            if (rq != n.end() && rq->is_array()) for (auto& r : *rq) m.requires_.push_back(r.is_string() ? r.get<std::string>() : "");
            auto lk = n.find("links");
            if (lk != n.end() && lk->is_array()) for (auto& r : *lk) m.links.push_back(r.is_string() ? r.get<std::string>() : "");
            if (!m.id.empty()) w.nodes.push_back(std::move(m));
        }
    }
    // links are two-way
    for (size_t ai = 0; ai < w.nodes.size(); ai++) {
        std::string aid = w.nodes[ai].id;
        std::vector<std::string> links = w.nodes[ai].links;
        for (auto& lid : links) {
            MapNode* b = w.node(lid);
            if (b && std::find(b->links.begin(), b->links.end(), aid) == b->links.end()) b->links.push_back(aid);
        }
    }
    if (w.start.empty() && !w.nodes.empty()) w.start = w.nodes[0].id;
    return w;
}

// ---------------------------------------------------------------- library

std::vector<std::string> OverworldLibrary::ids() const {
    std::vector<std::string> out;
    for (auto& kv : worlds_) out.push_back(kv.first);
    std::sort(out.begin(), out.end(), [](const std::string& a, const std::string& b) { return lower(a) < lower(b); });
    return out;
}

std::string OverworldLibrary::pathFor(const std::string& id) const { return fs::join(directory, id + ".json"); }

void OverworldLibrary::save(const OverworldData& w) {
    fs::createDirectories(directory);
    fs::writeFile(pathFor(w.id), w.toJson());
}

void OverworldLibrary::remove(const std::string& id) {
    worlds_.erase(id);
    if (fs::fileExists(pathFor(id))) fs::removeFile(pathFor(id));
}

std::string OverworldLibrary::nextFreeId() const {
    for (int i = 1;; i++) if (!worlds_.count("world_" + std::to_string(i))) return "world_" + std::to_string(i);
}

OverworldLibrary OverworldLibrary::load(const std::string& dir) {
    OverworldLibrary lib;
    lib.directory = dir;
    if (!fs::dirExists(dir)) return lib;
    auto files = fs::listFiles(dir, ".json");
    std::sort(files.begin(), files.end(), [](const std::string& a, const std::string& b) { return lower(a) < lower(b); });
    for (auto& f : files) {
        try {
            std::string text;
            fs::readFile(fs::join(dir, f), text);
            auto w = OverworldData::parse(text, fs::stem(f));
            std::string id = w.id;
            lib.worlds_[id] = std::move(w);
        } catch (const std::exception& ex) {
            std::printf("[world] failed to load %s: %s\n", f.c_str(), ex.what());
        }
    }
    return lib;
}

} // namespace mg
