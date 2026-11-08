#pragma once
#include "Sprites.hpp"
#include "core/Overworld.hpp"
#include "core/World.hpp"

namespace mg::rt {

// Draws levels and worlds into the current virtual screen. World units: 32 per tile; ppu = render pixels per
// world unit (2 in game, variable zoom in the editor). Sprites are placed by their rotation centre exactly like
// in Scratch, then scaled by size%/100.
class SceneRenderer {
public:
    explicit SceneRenderer(SpriteLibrary& sprites) : sprites_(sprites) {}

    real centerX = 0, centerY = 0;   // world position shown at the middle of the screen
    real ppu = 2;                    // pixels per world unit
    real animTime = 0;               // seconds, advanced by the host every frame; drives animated tiles

    // render interpolation: logic ticks at 30 Hz, drawing runs at any frame rate
    real interpAlpha = 1;            // 0..1 = how far we are between the previous and the current logic tick
    void captureInterp(World& w);    // call right BEFORE each World::tick

    SpriteLibrary& sprites() { return sprites_; }

    float sx(real wx) const { return (float)((wx - centerX) * ppu + Screen::VW / 2.0); }
    float sy(real wy) const { return (float)(Screen::VH / 2.0 - (wy - centerY) * ppu); }
    real worldX(float px) const { return (px - Screen::VW / 2.0) / ppu + centerX; }
    real worldY(float py) const { return (Screen::VH / 2.0 - py) / ppu + centerY; }

    void drawSprite(Sprite* s, real wx, real wy, real scale, bool flipX = false, real rotationDeg = 0, int alpha = 255, bool snap = false);
    void drawSpriteScreen(Sprite* s, float sx, float sy, float ppu, int alpha = 255);
    void beginStage() { ppu = 2; centerX = 0; centerY = 0; }
    static bool hit(const Sprite& s, real x, real y, real scale, real mx, real my);

    void drawBackground(const LevelData& level, real camX, real camY);
    void drawTiles(const std::vector<int>& tiles, int width, int height, bool editorView, int alpha = 255);
    void drawLayers(LevelData& level, real camX, real camY, bool front, bool editorView = false, int editLayer = 0);
    void drawMapLayer(const ImageLayerProps& layer, real scrollX, real scrollY, float areaX, float areaY, float areaW, float areaH);
    Sprite* overworldTileSprite(const OverworldTileDef& def, real time);
    void drawOverworldTiles(const OverworldData& w, real scrollX, real scrollY, float areaX, float areaY, float areaW, float areaH, real time);
    void drawEntities(World& w, EntityLayer layer);
    void drawEntityDefs(LevelData& level, const EntityDef* highlight = nullptr);
    void drawPlayer(World& w);
    void drawParticles(World& w);
    // everything in the right order for gameplay: backdrop, back layers, entities, tiles, player, front layers
    void drawWorld(World& w);

private:
    SpriteLibrary& sprites_;
    struct XY { real x, y; };
    std::unordered_map<const Entity*, XY> prevEnt_;
    std::unordered_map<const Particle*, XY> prevPart_;
    XY prevPlayer_{0, 0}, prevCam_{0, 0};
    const World* prevWorld_ = nullptr;
    int64_t prevTick_ = -10;

    void drawImageLayer(const Layer& layer, int alpha);
    Sprite* entitySprite(const Entity& e);
    void drawWorldCore(World& w);
};

} // namespace mg::rt
