#pragma once
#include "Platform.hpp"
#include "core/Common.hpp"
#include <string>
#include <unordered_map>

namespace mg::rt {

// The virtual screen everything is drawn into (960x720 in 4:3, 1280x720 in 16:9) and the platform handle.
struct Screen {
    static int VW, VH;
    static plat::Platform* platform;
};
inline plat::Platform& P() { return *Screen::platform; }

// A GPU texture plus the Scratch-style rotation centre. Sizes are in "source units" (stage px at 100%).
struct Sprite {
    plat::Texture tex;
    float srcW = 0, srcH = 0;   // size in source units
    float cx = 0, cy = 0;       // rotation centre in source units from the top-left
    // visible-pixel box relative to the rotation centre: centre offset (right, down) and half size
    float visDx = 0, visDy = 0, visHw = 0, visHh = 0;
};

struct CostumeInfo {
    int index = 0;
    std::string name, file, kind;
    int res = 1;
    double cx = 0, cy = 0;
    std::vector<double> bbox;
};

// Loads costumes listed in assets/manifest.json. SVG costumes are pre-rasterised by cpp/tools/BakeSvgs to
// "<file>.svg.png" (4 texels per source unit); PNG costumes are decoded with stb_image.
class SpriteLibrary {
public:
    static constexpr float SvgTexelsPerUnit = 4.f;

    explicit SpriteLibrary(const std::string& assetsRoot);
    ~SpriteLibrary();

    const std::map<std::string, CostumeInfo>* group(const std::string& g) const {
        auto it = manifest_.find(g); return it == manifest_.end() ? nullptr : &it->second;
    }
    Sprite* get(const std::string& group, const std::string& key);
    Sprite* byIndex(const std::string& group, int index);
    // Sprite for a runtime tile number: the original costume, or the tile's custom texture / animation frame.
    Sprite* tile(int id, double time = 0);
    // Loads a custom .png / .svg from the assets folder (cached). The rotation centre is the image centre.
    Sprite* getFile(const std::string& relativePath);

private:
    std::string root_;
    std::map<std::string, std::map<std::string, CostumeInfo>> manifest_;
    std::map<std::string, std::map<int, std::string>> byIndex_;
    std::unordered_map<std::string, std::unique_ptr<Sprite>> cache_;

    // Decodes a PNG. On the PSP anything larger than 512 px is box-filtered down (hardware texture limit);
    // origW / origH always report the file's real size so sprite dimensions stay correct.
    static bool decode(const std::string& path, std::vector<uint8_t>& rgba, int& w, int& h, int& origW, int& origH);
    // Size in source units of a baked SVG: pixels / 4 normally, the recorded value from psp_sizes.json on the PSP.
    void svgUnits(const std::string& relSvg, int w, int h, float& uw, float& uh) const;
    std::map<std::string, std::pair<double, double>> pspSizes_;
};

} // namespace mg::rt
