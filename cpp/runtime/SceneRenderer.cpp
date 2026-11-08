#include "SceneRenderer.hpp"

namespace mg::rt {

namespace {
constexpr real TeleportDist = 48;   // a move bigger than this in one tick (door, respawn, camera snap) is not blended
real lerpTp(real a, real b, real t) { return std::abs(b - a) > TeleportDist ? b : a + (b - a) * t; }
real modPos(real a, real b) { real r = std::fmod(a, b); return r < 0 ? r + b : r; }
}

void SceneRenderer::captureInterp(World& w) {
    prevEnt_.clear(); prevPart_.clear();
    for (auto& e : w.entities) prevEnt_[e.get()] = {e->x, e->y};
    for (auto& p : w.particles) prevPart_[p.get()] = {p->x, p->y};
    prevPlayer_ = {w.player.x, w.player.y};
    prevCam_ = {w.camX, w.camY};
    prevWorld_ = &w;
    prevTick_ = w.tickCount;
}

void SceneRenderer::drawSprite(Sprite* s, real wx, real wy, real scale, bool flipX, real rotationDeg, int alpha, bool snap) {
    if (!s) return;
    float pp = (float)(scale * ppu);
    float px = sx(wx), py = sy(wy);
    if (snap) { px = std::round(px); py = std::round(py); }
    float dw = s->srcW * pp, dh = s->srcH * pp;
    if (px + dw < 0 || py + dh < 0 || px - dw > Screen::VW || py - dh > Screen::VH) return;
    float ox = (flipX ? s->srcW - s->cx : s->cx) * pp;
    float oy = s->cy * pp;
    P().drawTexture(s->tex, 0, 0, flipX ? (float)-s->tex.w : (float)s->tex.w, (float)s->tex.h,
                    px, py, dw, dh, ox, oy, (float)rotationDeg, plat::Color(255, 255, 255, std::clamp(alpha, 0, 255)));
}

void SceneRenderer::drawSpriteScreen(Sprite* s, float x, float y, float k, int alpha) {
    if (!s) return;
    P().drawTexture(s->tex, 0, 0, (float)s->tex.w, (float)s->tex.h, x, y, s->srcW * k, s->srcH * k,
                    s->cx * k, s->cy * k, 0.f, plat::Color(255, 255, 255, std::clamp(alpha, 0, 255)));
}

bool SceneRenderer::hit(const Sprite& s, real x, real y, real scale, real mx, real my) {
    real cx = x + s.visDx * scale, cy = y - s.visDy * scale;
    return std::abs(mx - cx) <= s.visHw * scale && std::abs(my - cy) <= s.visHh * scale;
}

// ================================================================ background

void SceneRenderer::drawBackground(const LevelData& level, real camX, real camY) {
    int idx = std::clamp(level.backdrop, 1, 3);
    const char* stageKey = idx == 2 ? "backdrop1" : idx == 3 ? "backdrop2" : "Blue";
    ppu = 2;
    centerX = camX; centerY = camY;
    // fixed stage backdrop: centre of the stage is world (camX, camY)
    Sprite* backdrop = sprites_.get("stage", stageKey);
    real fill = !backdrop ? 1.0 : std::max(1.0, std::max(Screen::VW / ppu / backdrop->srcW, Screen::VH / ppu / backdrop->srcH));
    drawSprite(backdrop, camX, camY, fill);

    Sprite* back = sprites_.get("background", "Background-1-back");
    real backY = camY < 1400 ? 220 - camY / 3.4 : -183.82352941176458;
    drawSprite(back, camX, camY + backY, 2.0);

    if (!level.showBackground()) return;

    real pieceW = 512;
    const char* pieces[] = {"Background-1-1", "Background-1-2", "Background-1-3", "Background-1-4"};
    for (int i = 0; i < 4; i++) {
        real screenX = i * pieceW - camX / 4.0;
        real x = modPos(screenX, pieceW * 4) - pieceW;
        real y = 220 - camY / 4.0;
        // wrap-around: also draw one period to the right so there is never a gap
        drawSprite(sprites_.get("background", pieces[i]), camX + x, camY + y, 2.0);
        drawSprite(sprites_.get("background", pieces[i]), camX + x + pieceW * 4, camY + y, 2.0);
    }

    real l2 = modPos(((4 * pieceW - camX / 4.0) * 1.3), pieceW * 4) - pieceW - 512 * 1.041;
    drawSprite(sprites_.get("background", "Background-1-1-L2"), camX + l2, camY + 220 - camY / 4.0, 2.0);
    drawSprite(sprites_.get("background", "Background-1-1-L2"), camX + l2 + pieceW * 4, camY + 220 - camY / 4.0, 2.0);

    drawSprite(sprites_.get("background", "Background-1-frount"), camX, camY + 220 - camY / 4.0, 2.0);
}

// ================================================================ tiles

void SceneRenderer::drawTiles(const std::vector<int>& tiles, int width, int height, bool editorView, int alpha) {
    int x0 = std::max(0, (int)std::floor(worldX(0) / 32) - 1);
    int x1 = std::min(width - 1, (int)std::floor(worldX((float)Screen::VW) / 32) + 1);
    int y0 = std::max(0, (int)std::floor(worldY((float)Screen::VH) / 32) - 1);
    int y1 = std::min(height - 1, (int)std::floor(worldY(0) / 32) + 1);
    for (int x = x0; x <= x1; x++) {
        for (int y = y0; y <= y1; y++) {
            int t = tiles[(size_t)x * height + y];
            if (t <= TileInfo::Air) continue;
            if (!editorView && TileInfo::isEditorOnly(t)) continue;
            Sprite* spr = sprites_.tile(t, animTime);
            if (!spr) continue;
            // built-in costumes are 16 units drawn at 200%; custom textures are stretched to one 32x32 cell
            TileDef* d = TileRegistry::byNum(t);
            real sc = (d && (d->texture || d->frames)) ? 32.0 / spr->srcW : 2.0;
            drawSprite(spr, x * 32 + 16, y * 32 + 16, sc, false, 0, alpha, true);
        }
    }
}

// ================================================================ layers

void SceneRenderer::drawLayers(LevelData& level, real camX, real camY, bool front, bool editorView, int editLayer) {
    for (size_t i = 0; i < level.layers.size(); i++) {
        Layer& layer = level.layers[i];
        if (layer.front != front) continue;
        if (!layer.visible && !editorView) continue;
        // in the editor the layers that are not being edited are drawn dimmed
        int alpha = (editorView && editLayer != 0 && editLayer != (int)i + 1) ? 110 : 255;
        if (!layer.visible) alpha = 70;

        real saveX = centerX, saveY = centerY;
        if (layer.kind == LayerKind::Image && layer.fitScreen) {
            Sprite* img = sprites_.getFile(layer.image);
            if (img) {
                real scale = std::max(Screen::VW / ppu / img->srcW, Screen::VH / ppu / img->srcH);
                drawSprite(img, camX, camY, scale, false, 0, alpha);
            }
            continue;
        }

        centerX = camX * layer.parallax;
        centerY = camY * layer.parallax;
        if (layer.kind == LayerKind::Tiles) {
            if ((int)layer.tiles.size() == level.width * level.height)
                drawTiles(layer.tiles, level.width, level.height, editorView, alpha);
        } else {
            drawImageLayer(layer, alpha);
        }
        centerX = saveX; centerY = saveY;
    }
}

void SceneRenderer::drawImageLayer(const Layer& layer, int alpha) {
    Sprite* img = sprites_.getFile(layer.image);
    if (!img) return;
    real w = img->srcW * layer.scale, h = img->srcH * layer.scale;
    if (w <= 0 || h <= 0) return;
    // sprites are drawn by their centre; the layer position is the centre of the first image
    real viewL = worldX(0), viewR = worldX((float)Screen::VW), viewB = worldY((float)Screen::VH), viewT = worldY(0);

    int ix0 = 0, ix1 = 0, iy0 = 0, iy1 = 0;
    if (layer.repeatX) { ix0 = (int)std::floor((viewL - layer.x) / w + 0.5); ix1 = (int)std::floor((viewR - layer.x) / w + 0.5); }
    if (layer.repeatY) { iy0 = (int)std::floor((viewB - layer.y) / h + 0.5); iy1 = (int)std::floor((viewT - layer.y) / h + 0.5); }
    for (int ix = ix0; ix <= ix1; ix++)
        for (int iy = iy0; iy <= iy1; iy++)
            drawSprite(img, layer.x + ix * w, layer.y + iy * h, layer.scale, false, 0, alpha);
}

void SceneRenderer::drawMapLayer(const ImageLayerProps& layer, real scrollX, real scrollY, float areaX, float areaY, float areaW, float areaH) {
    Sprite* img = sprites_.getFile(layer.image);
    if (!img) return;
    if (layer.fitScreen) {
        float k = (float)std::max(areaW / img->srcW, areaH / img->srcH);
        drawSpriteScreen(img, areaX + areaW / 2, areaY + areaH / 2, k);
        return;
    }
    float pp = (float)layer.scale;
    float w = img->srcW * pp, h = img->srcH * pp;
    if (w <= 0 || h <= 0) return;
    real cx = layer.x - scrollX * layer.parallax + areaX, cy = layer.y - scrollY * layer.parallax + areaY;
    int ix0 = 0, ix1 = 0, iy0 = 0, iy1 = 0;
    if (layer.repeatX) { ix0 = (int)std::floor((areaX - cx) / w + 0.5); ix1 = (int)std::floor((areaX + areaW - cx) / w + 0.5); }
    if (layer.repeatY) { iy0 = (int)std::floor((areaY - cy) / h + 0.5); iy1 = (int)std::floor((areaY + areaH - cy) / h + 0.5); }
    for (int ix = ix0; ix <= ix1; ix++)
        for (int iy = iy0; iy <= iy1; iy++)
            drawSpriteScreen(img, (float)(cx + ix * w), (float)(cy + iy * h), pp);
}

Sprite* SceneRenderer::overworldTileSprite(const OverworldTileDef& def, real time) {
    if (def.frames && !def.frames->empty())
        return sprites_.getFile((*def.frames)[(int)std::floor(time * def.fps) % (int)def.frames->size()]);
    return !def.texture.empty() ? sprites_.getFile(def.texture) : nullptr;
}

void SceneRenderer::drawOverworldTiles(const OverworldData& w, real scrollX, real scrollY, float areaX, float areaY, float areaW, float areaH, real time) {
    if ((int)w.cells.size() != w.width * w.height) return;
    const int C = OverworldData::CellSize;
    int x0 = std::max(0, (int)std::floor(scrollX / C)), x1 = std::min(w.width - 1, (int)std::floor((scrollX + areaW) / C));
    int y0 = std::max(0, (int)std::floor(scrollY / C)), y1 = std::min(w.height - 1, (int)std::floor((scrollY + areaH) / C));
    for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++) {
            const std::string& id = w.cells[(size_t)y * w.width + x];
            if (id.empty()) continue;
            OverworldTileDef* def = OverworldTileRegistry::find(id);
            Sprite* spr = def ? overworldTileSprite(*def, time) : nullptr;
            if (!spr) continue;
            float k = C / spr->srcW;
            drawSpriteScreen(spr, (float)(areaX + x * C + C / 2.0 - scrollX), (float)(areaY + y * C + C / 2.0 - scrollY), k);
        }
}

// ================================================================ entities

Sprite* SceneRenderer::entitySprite(const Entity& e) {
    return e.texture ? sprites_.getFile(*e.texture) : sprites_.byIndex("enemy", e.costume);
}

void SceneRenderer::drawEntities(World& w, EntityLayer layer) {
    for (auto& e : w.entities) {
        if (e->layer != layer || !e->visible) continue;
        drawSprite(entitySprite(*e), e->x, e->y, e->sizePct / 100.0, e->flipX(), e->allAround ? e->rotationDegrees : 0, 255, false);
    }
}

void SceneRenderer::drawEntityDefs(LevelData& level, const EntityDef* highlight) {
    for (auto& d : level.entities) {
        auto e = Entity::spawn(std::make_shared<EntityDef>(d), level.height, true);
        if (!e) continue;
        drawSprite(entitySprite(*e), e->x, e->y, e->sizePct / 100.0, false, 0, &d == highlight ? 255 : 230);
    }
}

void SceneRenderer::drawPlayer(World& w) {
    Player& p = w.player;
    if (!p.visible) return;
    Sprite* s = sprites_.byIndex("player", p.costumeIndex);
    drawSprite(s, p.x, p.y, 2.0, p.flipX(), p.rotation == RotationStyle::AllAround ? p.rotationDegrees : 0);
}

void SceneRenderer::drawParticles(World& w) {
    for (auto& p : w.particles) {
        Sprite* s = sprites_.byIndex("particles", p->costume());
        drawSprite(s, p->x, p->y, 2.0, false, 0, (int)(255 * (1 - std::clamp(p->ghost, 0.0, 100.0) / 100.0)));
    }
}

void SceneRenderer::drawWorld(World& w) {
    real t = interpAlpha;
    // only blend when the stored snapshot is exactly one tick behind the world; otherwise (level load, state load) draw as is
    if (t >= 1 || prevWorld_ != &w || w.tickCount != prevTick_ + 1) { drawWorldCore(w); return; }

    struct SavedE { Entity* e; real x, y; };
    struct SavedP { Particle* p; real x, y; };
    std::vector<SavedE> saved; saved.reserve(w.entities.size());
    std::vector<SavedP> savedP; savedP.reserve(w.particles.size());
    real px = w.player.x, py = w.player.y, cx0 = w.camX, cy0 = w.camY;
    for (auto& e : w.entities) {
        auto it = prevEnt_.find(e.get());
        if (it != prevEnt_.end()) { saved.push_back({e.get(), e->x, e->y}); e->x = lerpTp(it->second.x, e->x, t); e->y = lerpTp(it->second.y, e->y, t); }
    }
    for (auto& p : w.particles) {
        auto it = prevPart_.find(p.get());
        if (it != prevPart_.end()) { savedP.push_back({p.get(), p->x, p->y}); p->x = lerpTp(it->second.x, p->x, t); p->y = lerpTp(it->second.y, p->y, t); }
    }
    w.player.x = lerpTp(prevPlayer_.x, px, t); w.player.y = lerpTp(prevPlayer_.y, py, t);
    w.camX = lerpTp(prevCam_.x, cx0, t); w.camY = lerpTp(prevCam_.y, cy0, t);
    drawWorldCore(w);
    for (auto& s : saved) { s.e->x = s.x; s.e->y = s.y; }
    for (auto& s : savedP) { s.p->x = s.x; s.p->y = s.y; }
    w.player.x = px; w.player.y = py; w.camX = cx0; w.camY = cy0;
}

void SceneRenderer::drawWorldCore(World& w) {
    real cx = w.camX + w.shakeX, cy = w.camY + w.shakeY, zoom = 2 * w.camZoom;
    ppu = 2;
    centerX = cx; centerY = cy;
    drawBackground(*w.level, cx, cy);
    drawLayers(*w.level, cx, cy, false, w.godMode);
    ppu = zoom; centerX = cx; centerY = cy;
    drawEntities(w, EntityLayer::BehindTiles);
    if (w.playerBehindTiles) drawPlayer(w);
    drawTiles(w.tiles, w.level->width, w.level->height, w.godMode);
    drawEntities(w, EntityLayer::Normal);
    if (!w.playerBehindTiles) drawPlayer(w);
    drawEntities(w, EntityLayer::Front);
    drawParticles(w);
    ppu = 2;
    drawLayers(*w.level, cx, cy, true, w.godMode);
    ppu = zoom; centerX = cx; centerY = cy;
}

} // namespace mg::rt
