#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#include "../third_party/stb/stb_image.h"
#include "Sprites.hpp"
#include "Lowres.hpp"
#include "core/Fs.hpp"
#include "core/TileDef.hpp"
#include "../third_party/nlohmann/json.hpp"

namespace mg::rt {

int Screen::VW = 960;
int Screen::VH = 720;
plat::Platform* Screen::platform = nullptr;

#ifdef MG_LOWRES
static const char* const SvgBakedSuffix = MG_LOWRES_SUFFIX;   // low-res set (tools/BakeSvgs --psp / --vita)
#else
static const char* const SvgBakedSuffix = ".png";
#endif

static bool endsWithI(const std::string& s, const char* suffix) {
    size_t n = std::char_traits<char>::length(suffix);
    if (s.size() < n) return false;
    for (size_t i = 0; i < n; i++) if (std::tolower((unsigned char)s[s.size() - n + i]) != suffix[i]) return false;
    return true;
}

SpriteLibrary::SpriteLibrary(const std::string& assetsRoot) : root_(assetsRoot) {
#ifdef MG_LOWRES
    {   // true unit sizes of the low-res baked SVGs (written by tools/BakeSvgs --psp)
        std::string st;
        if (fs::readFile(fs::join(assetsRoot, MG_LOWRES_SIZES), st)) {
            auto sj = nlohmann::json::parse(st, nullptr, false);
            if (sj.is_object())
                for (auto it = sj.begin(); it != sj.end(); ++it)
                    if (it.value().is_array() && it.value().size() == 2)
                        pspSizes_[it.key()] = {it.value()[0].get<double>(), it.value()[1].get<double>()};
        }
    }
#endif
    std::string text;
    if (!fs::readFile(fs::join(assetsRoot, "manifest.json"), text)) {
        std::fprintf(stderr, "[sprites] cannot read %s/manifest.json\n", assetsRoot.c_str());
        return;
    }
    auto root = nlohmann::json::parse(text, nullptr, false);
    if (!root.is_object()) return;
    for (auto g = root.begin(); g != root.end(); ++g) {
        auto& map = manifest_[g.key()];
        for (auto it = g.value().begin(); it != g.value().end(); ++it) {
            CostumeInfo ci;
            const auto& j = it.value();
            ci.index = j.value("index", 0);
            ci.name = j.value("name", std::string());
            ci.file = j.value("file", std::string());
            ci.kind = j.value("kind", std::string());
            ci.res = j.value("res", 1);
            ci.cx = j.value("cx", 0.0);
            ci.cy = j.value("cy", 0.0);
            if (j.contains("bbox") && j["bbox"].is_array()) for (auto& v : j["bbox"]) ci.bbox.push_back(v.get<double>());
            map[it.key()] = ci;
            byIndex_[g.key()][ci.index] = it.key();
        }
    }
}

SpriteLibrary::~SpriteLibrary() {
    for (auto& kv : cache_) if (kv.second) P().destroyTexture(kv.second->tex);
}

bool SpriteLibrary::decode(const std::string& path, std::vector<uint8_t>& rgba, int& w, int& h, int& origW, int& origH) {
    std::vector<uint8_t> bytes;
    if (!fs::readFileBytes(path, bytes)) return false;
    int comp;
    unsigned char* px = stbi_load_from_memory(bytes.data(), (int)bytes.size(), &w, &h, &comp, 4);
    if (!px) return false;
    origW = w; origH = h;
    rgba.assign(px, px + (size_t)w * h * 4);
    stbi_image_free(px);
#ifdef MG_LOWRES
    constexpr int MaxSide = MG_MAX_TEX;
    int k = (std::max(w, h) + MaxSide - 1) / MaxSide;
    if (k > 1) {
        int nw = std::max(1, w / k), nh = std::max(1, h / k);
        std::vector<uint8_t> out((size_t)nw * nh * 4);
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++) {
                unsigned sum[4] = {0, 0, 0, 0};
                for (int dy = 0; dy < k; dy++)
                    for (int dx = 0; dx < k; dx++) {
                        const uint8_t* p = &rgba[((size_t)(y * k + dy) * w + (x * k + dx)) * 4];
                        for (int c = 0; c < 4; c++) sum[c] += p[c];
                    }
                for (int c = 0; c < 4; c++) out[((size_t)y * nw + x) * 4 + c] = (uint8_t)(sum[c] / (k * k));
            }
        rgba.swap(out); w = nw; h = nh;
    }
#endif
    return true;
}

void SpriteLibrary::svgUnits(const std::string& rel, int w, int h, float& uw, float& uh) const {
#ifdef MG_LOWRES
    auto it = pspSizes_.find(rel);
    if (it != pspSizes_.end()) { uw = (float)it->second.first; uh = (float)it->second.second; return; }
#endif
    uw = w / SvgTexelsPerUnit; uh = h / SvgTexelsPerUnit;
}

Sprite* SpriteLibrary::get(const std::string& group, const std::string& key) {
    std::string ck = group + "/" + key;
    auto c = cache_.find(ck);
    if (c != cache_.end()) return c->second.get();
    std::unique_ptr<Sprite> loaded;
    auto g = manifest_.find(group);
    if (g != manifest_.end()) {
        auto k = g->second.find(key);
        if (k != g->second.end()) {
            const CostumeInfo& info = k->second;
            bool svg = info.kind == "svg";
            std::vector<uint8_t> px; int w, h, ow, oh;
            if (!decode(fs::join(root_, svg ? info.file + SvgBakedSuffix : info.file), px, w, h, ow, oh)) {
                std::fprintf(stderr, "[sprites] failed to load %s\n", ck.c_str());
            } else {
                loaded = std::make_unique<Sprite>();
                float texels = svg ? SvgTexelsPerUnit : (float)std::max(1, info.res);
                if (svg) svgUnits(info.file, w, h, loaded->srcW, loaded->srcH);
                else { loaded->srcW = ow / texels; loaded->srcH = oh / texels; }
                loaded->cx = svg ? (float)info.cx : (float)(info.cx / std::max(1, info.res));
                loaded->cy = svg ? (float)info.cy : (float)(info.cy / std::max(1, info.res));
                if (info.bbox.size() == 4) {
                    auto& bb = info.bbox;
                    loaded->visDx = (float)((bb[0] + bb[2]) / 2 - loaded->cx);
                    loaded->visDy = (float)((bb[1] + bb[3]) / 2 - loaded->cy);
                    loaded->visHw = (float)((bb[2] - bb[0]) / 2);
                    loaded->visHh = (float)((bb[3] - bb[1]) / 2);
                } else {
                    loaded->visDx = loaded->srcW / 2 - loaded->cx;
                    loaded->visDy = loaded->srcH / 2 - loaded->cy;
                    loaded->visHw = loaded->srcW / 2;
                    loaded->visHh = loaded->srcH / 2;
                }
#ifdef MG_LOWRES
                bool pixelArt = false;   // drawn at 0.75x: nearest-neighbour would shimmer
#else
                bool pixelArt = info.kind == "png" && info.res <= 2 && w <= 128;
#endif
                loaded->tex = P().createTexture(px.data(), w, h, !pixelArt);
            }
        }
    }
    Sprite* raw = loaded.get();
    cache_[ck] = std::move(loaded);
    return raw;
}

Sprite* SpriteLibrary::byIndex(const std::string& group, int index) {
    auto g = byIndex_.find(group);
    if (g == byIndex_.end()) return nullptr;
    auto k = g->second.find(index);
    return k == g->second.end() ? nullptr : get(group, k->second);
}

Sprite* SpriteLibrary::tile(int id, double time) {
    TileDef* def = TileRegistry::byNum(id);
    if (def) {
        if (def->frames && !def->frames->empty()) {
            auto& frames = *def->frames;
            int i = (int)std::floor(time * std::max(0.01, def->fps)) % (int)frames.size();
            const std::string& f = frames[i];
            if (f.compare(0, 5, "tile:") == 0) {
                TileDef* other = TileRegistry::find(f.substr(5));
                return (!other || other->num == id) ? nullptr : tile(other->num, 0);
            }
            return getFile(f);
        }
        if (def->texture) return getFile(*def->texture);
    }
    return get("tiles", std::to_string(id));
}

Sprite* SpriteLibrary::getFile(const std::string& relativePath) {
    std::string ck = "file/" + relativePath;
    auto c = cache_.find(ck);
    if (c != cache_.end()) return c->second.get();
    std::unique_ptr<Sprite> sprite;
    bool svg = endsWithI(relativePath, ".svg");
    std::vector<uint8_t> px; int w, h, ow, oh;
    if (!decode(fs::join(root_, svg ? relativePath + SvgBakedSuffix : relativePath), px, w, h, ow, oh)) {
        std::fprintf(stderr, "[sprites] failed to load custom image '%s'\n", relativePath.c_str());
    } else {
        sprite = std::make_unique<Sprite>();
        if (svg) svgUnits(relativePath, w, h, sprite->srcW, sprite->srcH);
        else { sprite->srcW = (float)ow; sprite->srcH = (float)oh; }
        sprite->cx = sprite->srcW / 2; sprite->cy = sprite->srcH / 2;
        sprite->visHw = sprite->srcW / 2; sprite->visHh = sprite->srcH / 2;
#ifdef MG_LOWRES
        sprite->tex = P().createTexture(px.data(), w, h, true);
#else
        sprite->tex = P().createTexture(px.data(), w, h, svg || w > 128);
#endif
    }
    Sprite* raw = sprite.get();
    cache_[ck] = std::move(sprite);
    return raw;
}

} // namespace mg::rt
