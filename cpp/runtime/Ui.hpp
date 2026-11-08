#pragma once
#include "Sprites.hpp"
#include <optional>

namespace mg::rt {

using plat::Color;

// Replacement for SkiaUi: text, panels and menus drawn at the virtual resolution. Like the C# overlay, nothing is
// drawn immediately - calls are recorded and replayed at endFrame(), so UI always ends up on top of the sprites.
// Text uses stb_truetype (glyphs rasterised once into small textures); shapes are built from filled rects
// (no anti-aliasing), so any backend that can fill a rect and blit a texture can run the UI.
class Ui {
public:
    explicit Ui(const std::string& fontDir);
    ~Ui();

    void beginFrame() { cmds_.clear(); }
    void endFrame();

    float measure(const std::string& text, float size, bool bold = false);
    // align: 0 = left, 1 = centre, 2 = right. y is the text baseline.
    void text(const std::string& text, float x, float y, float size, Color color, bool bold = false, int align = 0, bool shadow = false);
    void rect(float x, float y, float w, float h, Color fill, float radius = 0, std::optional<Color> stroke = std::nullopt, float strokeWidth = 2);
    void line(float x1, float y1, float x2, float y2, Color color, float width = 1);
    void circle(float cx, float cy, float r, Color color);
    void gradientV(float x, float y, float w, float h, Color top, Color bottom);
    // A clickable-looking button. Returns true if the mouse is over it.
    bool button(const std::string& label, float x, float y, float w, float h, float mouseX, float mouseY, bool selected = false, float fontSize = 26);

private:
    struct Glyph { plat::Texture tex; int w = 0, h = 0, xoff = 0, yoff = 0; float advance = 0; };
    struct Font;
    std::unique_ptr<Font> regular_, bold_;
    std::unordered_map<uint64_t, Glyph> glyphs_;
    plat::Texture white_;
    std::vector<std::function<void()>> cmds_;

    Glyph& glyph(bool bold, float size, uint32_t cp);
    Font& font(bool bold);
    void drawText(const std::string& text, float x, float y, float size, Color color, bool bold);
    void drawRect(float x, float y, float w, float h, Color fill, float radius);
    void drawRing(float x, float y, float w, float h, float radius, float sw, Color c);
    void fill(float x, float y, float w, float h, Color c) { P().fillRect(x, y, w, h, c); }
};

} // namespace mg::rt
