#define STB_TRUETYPE_IMPLEMENTATION
#include "../third_party/stb/stb_truetype.h"
#include "Ui.hpp"
#include "core/Fs.hpp"

namespace mg::rt {

struct Ui::Font {
    std::vector<uint8_t> data;
    stbtt_fontinfo info{};
    bool ok = false;
    bool load(const std::string& path) {
        if (!fs::readFileBytes(path, data)) return false;
        ok = stbtt_InitFont(&info, data.data(), stbtt_GetFontOffsetForIndex(data.data(), 0)) != 0;
        return ok;
    }
};

Ui::Ui(const std::string& fontDir) {
    regular_ = std::make_unique<Font>();
    bold_ = std::make_unique<Font>();
    if (!regular_->load(fs::join(fontDir, "LiberationSans-Regular.ttf")))
        std::fprintf(stderr, "[ui] cannot load %s/LiberationSans-Regular.ttf\n", fontDir.c_str());
    if (!bold_->load(fs::join(fontDir, "LiberationSans-Bold.ttf"))) { bold_.reset(); }
    uint8_t px[4] = {255, 255, 255, 255};
    white_ = P().createTexture(px, 1, 1, false);
}

Ui::~Ui() {
    for (auto& kv : glyphs_) P().destroyTexture(kv.second.tex);
    P().destroyTexture(white_);
}

Ui::Font& Ui::font(bool bold) { return (bold && bold_) ? *bold_ : *regular_; }

Ui::Glyph& Ui::glyph(bool bold, float size, uint32_t cp) {
    uint64_t key = ((uint64_t)(bold ? 1 : 0) << 60) | ((uint64_t)(uint32_t)std::lround(size * 4) << 32) | cp;
    auto it = glyphs_.find(key);
    if (it != glyphs_.end()) return it->second;
    Glyph g;
    Font& f = font(bold);
    if (f.ok) {
        float scale = stbtt_ScaleForMappingEmToPixels(&f.info, size);
        int adv, lsb;
        stbtt_GetCodepointHMetrics(&f.info, (int)cp, &adv, &lsb);
        g.advance = adv * scale;
        int w, h, xo, yo;
        unsigned char* bmp = stbtt_GetCodepointBitmap(&f.info, scale, scale, (int)cp, &w, &h, &xo, &yo);
        if (bmp && w > 0 && h > 0) {
            std::vector<uint8_t> rgba((size_t)w * h * 4);
            for (int i = 0; i < w * h; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255; rgba[i * 4 + 3] = bmp[i]; }
            g.tex = P().createTexture(rgba.data(), w, h, true);
            g.w = w; g.h = h; g.xoff = xo; g.yoff = yo;
        }
        if (bmp) stbtt_FreeBitmap(bmp, nullptr);
    }
    return glyphs_.emplace(key, g).first->second;
}

static uint32_t nextCodepoint(const std::string& s, size_t& i) {
    unsigned char c = (unsigned char)s[i++];
    if (c < 0x80) return c;
    int extra = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : 1;
    uint32_t cp = c & (0x3F >> extra);
    while (extra-- > 0 && i < s.size()) cp = (cp << 6) | ((unsigned char)s[i++] & 0x3F);
    return cp;
}

float Ui::measure(const std::string& text, float size, bool bold) {
    float w = 0;
    for (size_t i = 0; i < text.size();) w += glyph(bold, size, nextCodepoint(text, i)).advance;
    return w;
}

void Ui::drawText(const std::string& text, float x, float y, float size, Color color, bool bold) {
    float pen = x;
    for (size_t i = 0; i < text.size();) {
        Glyph& g = glyph(bold, size, nextCodepoint(text, i));
        if (g.tex.valid())
            P().drawTexture(g.tex, 0, 0, (float)g.w, (float)g.h, std::round(pen + g.xoff), std::round(y + g.yoff), (float)g.w, (float)g.h, 0, 0, 0, color);
        pen += g.advance;
    }
}

void Ui::text(const std::string& t, float x, float y, float size, Color color, bool bold, int align, bool shadow) {
    float w = measure(t, size, bold);
    if (align == 1) x -= w / 2; else if (align == 2) x -= w;
    cmds_.push_back([=] {
        if (shadow) drawText(t, x + 2, y + 2, size, Color(0, 0, 0, 160), bold);
        drawText(t, x, y, size, color, bold);
    });
}

// Horizontal span [x0, x1) of a rounded rect at row centre yy, or false when the row is outside.
static bool roundSpan(float x, float y, float w, float h, float r, float yy, float& x0, float& x1) {
    if (yy < y || yy >= y + h) return false;
    r = std::min(r, std::min(w, h) / 2);
    float inset = 0;
    if (r > 0) {
        float dy = 0;
        if (yy < y + r) dy = (y + r) - yy;
        else if (yy > y + h - r) dy = yy - (y + h - r);
        if (dy > 0) inset = r - std::sqrt(std::max(0.f, r * r - dy * dy));
    }
    x0 = x + inset; x1 = x + w - inset;
    return x1 > x0;
}

void Ui::drawRect(float x, float y, float w, float h, Color c, float radius) {
    if (c.a == 0) return;
    if (radius <= 0) { fill(x, y, w, h, c); return; }
    // body in one piece, only the rounded top/bottom bands row by row
    float r = std::min(radius, std::min(w, h) / 2);
    int rows = (int)std::ceil(r);
    fill(x, y + rows, w, h - 2 * rows, c);
    for (int i = 0; i < rows; i++) {
        float x0, x1;
        if (roundSpan(x, y, w, h, radius, y + i + 0.5f, x0, x1)) fill(x0, y + i, x1 - x0, 1, c);
        if (roundSpan(x, y, w, h, radius, y + h - i - 0.5f, x0, x1)) fill(x0, y + h - i - 1, x1 - x0, 1, c);
    }
}

void Ui::drawRing(float x, float y, float w, float h, float radius, float sw, Color c) {
    // Straight stretches of the outline are two tall bars; only the rounded end bands are drawn row by row.
    float o = sw / 2;
    float ox = x - o, oy = y - o, ow = w + sw, oh = h + sw, orad = radius + o;
    float ix = x + o, iy = y + o, iw = w - sw, ih = h - sw, irad = std::max(0.f, radius - o);
    int band = (int)std::ceil(orad);                 // rows that can be affected by the corners
    int totalRows = (int)std::ceil(oh);
    auto row = [&](int r) {
        float yy = oy + r + 0.5f, a0, a1, b0, b1;
        if (!roundSpan(ox, oy, ow, oh, orad, yy, a0, a1)) return;
        if (iw > 0 && ih > 0 && roundSpan(ix, iy, iw, ih, irad, yy, b0, b1)) {
            fill(a0, oy + r, b0 - a0, 1, c);
            fill(b1, oy + r, a1 - b1, 1, c);
        } else {
            fill(a0, oy + r, a1 - a0, 1, c);
        }
    };
    if (totalRows <= 2 * band + 2) { for (int r = 0; r < totalRows; r++) row(r); return; }
    for (int r = 0; r < band; r++) row(r);                                       // top band
    for (int r = totalRows - band; r < totalRows; r++) row(r);                  // bottom band
    float my0 = oy + band, mh = (float)(totalRows - 2 * band);
    fill(ox, my0, sw, mh, c);                                                    // left bar
    fill(ox + ow - sw, my0, sw, mh, c);                                          // right bar
}

void Ui::rect(float x, float y, float w, float h, Color fillC, float radius, std::optional<Color> stroke, float strokeWidth) {
    cmds_.push_back([=] {
        drawRect(x, y, w, h, fillC, radius);
        if (stroke) drawRing(x, y, w, h, radius, strokeWidth, *stroke);
    });
}

void Ui::line(float x1, float y1, float x2, float y2, Color color, float width) {
    cmds_.push_back([=] {
        float dx = x2 - x1, dy = y2 - y1, len = std::sqrt(dx * dx + dy * dy);
        if (len <= 0) return;
        float ang = std::atan2(dy, dx) * 180.f / 3.14159265f;
        // a rotated 1x1 texture: centre the rect on the line's midpoint
        P().drawTexture(white_, 0, 0, 1, 1, (x1 + x2) / 2, (y1 + y2) / 2, len, width, len / 2, width / 2, ang, color);
    });
}

void Ui::circle(float cx, float cy, float r, Color color) {
    cmds_.push_back([=] {
        int rows = (int)std::ceil(r * 2);
        for (int i = 0; i < rows; i++) {
            float yy = cy - r + i + 0.5f, dy = yy - cy;
            float half = std::sqrt(std::max(0.f, r * r - dy * dy));
            if (half > 0) fill(cx - half, cy - r + i, half * 2, 1, color);
        }
    });
}

void Ui::gradientV(float x, float y, float w, float h, Color top, Color bottom) {
    cmds_.push_back([=] {
        int rows = (int)h;
        for (int i = 0; i < rows; i++) {
            float t = rows > 1 ? (float)i / (rows - 1) : 0;
            Color c((int)(top.r + (bottom.r - top.r) * t), (int)(top.g + (bottom.g - top.g) * t),
                    (int)(top.b + (bottom.b - top.b) * t), (int)(top.a + (bottom.a - top.a) * t));
            fill(x, y + i, w, 1, c);
        }
    });
}

bool Ui::button(const std::string& label, float x, float y, float w, float h, float mx, float my, bool selected, float fontSize) {
    bool hot = mx >= x && mx < x + w && my >= y && my < y + h;
    Color fillC = (hot || selected) ? Color(255, 190, 60) : Color(40, 44, 80, 230);
    Color txt = (hot || selected) ? Color(30, 20, 10) : Color(255, 255, 255);
    rect(x, y, w, h, fillC, 10, Color(255, 255, 255, 90));
    text(label, x + w / 2, y + h / 2 + fontSize * 0.35f, fontSize, txt, true, 1);
    return hot;
}

void Ui::endFrame() {
    for (auto& c : cmds_) c();
    cmds_.clear();
}

} // namespace mg::rt
