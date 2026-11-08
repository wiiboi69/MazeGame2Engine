#include "Entity.hpp"
#include "World.hpp"
#include "Player.hpp"
#include "Scripting.hpp"

namespace mg {

// ======================================================================== registry

EntityType& EntityRegistry::builtin(const std::string& id, const std::string& name) {
    auto* t = new EntityType(id, name, [](std::shared_ptr<EntityDef> d, bool editor) { return Entity::spawnBuiltin(std::move(d), editor); });
    t->brushTileId = id;   // the generated built-in tiles already contain the brushes
    s().byId[id] = t;
    s().all.push_back(t);
    return *t;
}

void EntityRegistry::ensureInit() {
    if (s().init) return;
    s().init = true;
    builtin("walker", "Walker");
    builtin("danger", "Danger (stompable)");
    builtin("npc", "NPC (dialog)").prop("text", "NPC says...", "hello").prop("id", "Script id (optional, fires \"use\")", "");
    builtin("end_box", "Level goal");
    builtin("piranha", "Piranha plant");
    builtin("door", "Door").prop("target", "Target level number", "", PropKind::Level, true);
    builtin("pipe", "Pipe").prop("target", "Target level number", "", PropKind::Level, true);
    builtin("door_wide", "Wide door").prop("target", "Target level number", "", PropKind::Level, true);
}

EntityType* EntityRegistry::registerType(EntityType* type) {
    ensureInit();
    if (s().byId.count(type->id)) { std::fprintf(stderr, "entity id '%s' is already registered\n", type->id.c_str()); std::abort(); }
    s().byId[type->id] = type;
    s().all.push_back(type);

    std::string tileId = TileRegistry::find(type->id) == nullptr ? type->id : "entity_" + type->id;
    auto* d = new TileDef(tileId, type->name, "", "", "", type->category);
    d->texture = type->texture;
    d->entity = type->id;
    TileRegistry::registerTile(d);
    type->brushTileId = tileId;
    return type;
}

// ======================================================================== spawning

std::unique_ptr<Entity> Entity::spawn(std::shared_ptr<EntityDef> d, int /*levelHeight*/, bool editor) {
    auto* type = EntityRegistry::find(d->type);
    if (!type) return nullptr;
    auto e = type->create(d, editor);
    if (!e) return nullptr;
    e->typeId = type->id;
    e->def = d;
    if (!e->texture) e->texture = type->texture;
    return e;
}

std::unique_ptr<Entity> Entity::spawnBuiltin(std::shared_ptr<EntityDef> d, bool editor) {
    real cellX = d->x * 32, cellY = d->y * 32;
    auto e = std::make_unique<Entity>();
    e->typeId = d->type;
    e->def = d;
    e->param = d->get("target");
    const std::string& t = d->type;
    if (t == "walker") {
        e->kind = EntityKind::Walker; e->costume = CosWalk1; e->width = 12; e->height = 18;
        e->x = cellX + 16; e->y = cellY + e->height;
    } else if (t == "danger") {
        e->kind = EntityKind::Danger; e->costume = CosDanger1; e->width = 12; e->height = 18;
        e->x = cellX + 16; e->y = cellY + e->height;
    } else if (t == "npc") {
        e->kind = EntityKind::Npc; e->costume = CosDanger1; e->width = 12; e->height = 18;
        e->x = cellX + 16; e->y = cellY + e->height;
        e->extra = d->get("text", "hello");
        if (e->extra.empty()) e->extra = "hello";
    } else if (t == "end_box") {
        e->kind = EntityKind::EndBox; e->costume = CosEndGlobeStatic; e->width = 16; e->height = 16;
        e->x = cellX + 16; e->y = cellY + 16; e->sizePct = 20;
    } else if (t == "piranha") {
        e->kind = EntityKind::Pole; e->costume = CosPole; e->width = 16; e->height = 32;
        e->x = cellX + 16 + 16; e->y = cellY + 32;
        if (!editor) e->y -= 64;
        e->layer = EntityLayer::BehindTiles;
    } else if (t == "door") {
        e->kind = EntityKind::Trigger; e->costume = CosDoorTrigger; e->width = 16; e->height = 8;
        e->x = cellX + 16; e->y = cellY + 8 + 10; e->extra = "T-DO-1";
    } else if (t == "pipe") {
        e->kind = EntityKind::Trigger; e->costume = CosPipeTrigger; e->width = 16; e->height = 32;
        e->x = cellX + 16 + 16; e->y = cellY + 32 - 16; e->extra = "T-PI-1";
    } else if (t == "door_wide") {
        e->kind = EntityKind::Trigger; e->costume = CosDoorWide; e->width = 32; e->height = 32;
        e->x = cellX + 16; e->y = cellY + 32 - 16; e->extra = "T-DW-1";
    } else {
        return nullptr;
    }
    if (e->kind == EntityKind::Trigger) e->visible = editor;   // invisible in game (ghost 100)
    return e;
}

void Entity::restoreState(const Entity& s) {
    kind = s.kind; typeId = s.typeId; texture = s.texture;
    x = s.x; y = s.y; width = s.width; height = s.height; dir = s.dir; speedX = s.speedX; speedY = s.speedY;
    falling = s.falling; jumping = s.jumping; frame = s.frame; costume = s.costume; sizePct = s.sizePct;
    extra = s.extra; param = s.param; rotationDegrees = s.rotationDegrees; allAround = s.allAround;
    lastDir = s.lastDir; removed = s.removed; visible = s.visible; layer = s.layer;
    solid_ = s.solid_; fixDx_ = s.fixDx_; fixDy_ = s.fixDy_; modX_ = s.modX_; modY_ = s.modY_; offsetY_ = s.offsetY_;
    tile_ = s.tile_; tileShape_ = s.tileShape_; tileIndex_ = s.tileIndex_;
}

// ======================================================================== helpers

void Entity::getTile(World& w, real px, real py) {
    w.tileAt(px, py, tile_, tileIndex_);
    tileShape_ = TileInfo::shape(tile_);
}

bool Entity::touchesPlayer(World& w, real scale) {
    auto& p = w.player;
    return std::abs(p.x - x) < p.width + width * scale && std::abs(p.y - y) < p.height + height * scale;
}

void Entity::fixCollisionAtPoint(World& w, real px, real py, bool feet) {
    getTile(w, px, py);
    if (tileShape_.empty()) return;
    modX_ = fmodPos(px, 32);
    modY_ = fmodPos(py, 32);
    char t = tileShape_[0];
    if (t == '\\' || t == '/') {
        real m = 1.0 / (tileShape_[1] - '0');
        real c = (tileShape_[2] - '0') * (32 * m);
        if (t == '\\') { m = -m; c = 32 - c; }
        offsetY_ = modY_ - (modX_ * m + c);
        if (!(offsetY_ < 0)) return;
        if (fixDy_ < 0) { y += -offsetY_; solid_ = 10; return; }
        if (fixDy_ == 0 && ((fixDx_ > 0) == (m > 0))) { x += offsetY_ / m; solid_ = 10; return; }
    }
    if (tileShape_ == "=") {
        if (!feet || (modY_ - fixDy_) < 32) return;
    }
    solid_ = 10;
    if (fixDy_ < 0) y += 32 - modY_;
    if (fixDx_ < 0) x += 32 - modX_;
    if (fixDy_ > 0) y += T - modY_;
    if (fixDx_ > 0) x += T - modX_;
}

void Entity::fixCollisionInDirection(World& w, real dx, real dy) {
    fixDx_ = dx; fixDy_ = dy; solid_ = 0;
    for (int pass = 0; pass < 2; pass++) {
        fixCollisionAtPoint(w, x - width, y - height, true);
        fixCollisionAtPoint(w, x - width, y, false);
        fixCollisionAtPoint(w, x - width, y + height, false);
        fixCollisionAtPoint(w, x + (width + T), y - height, true);
        fixCollisionAtPoint(w, x + (width + T), y, false);
        fixCollisionAtPoint(w, x + (width + T), y + height, false);
        if (solid_ < 1) return;
    }
}

void Entity::moveSpriteX(World& w) {
    real origY = y;
    x += speedX;
    fixCollisionInDirection(w, 0, -1);
    if (y > origY + (std::abs(speedX) + 4)) {
        y = origY;
        fixCollisionInDirection(w, speedX, 0);
        if (solid_ > 0) {
            speedX = 0;
            dir = dir > 0 ? -90 : 90;      // turn around at walls
        }
    } else if (y > origY) {
        speedX *= 0.85;
    }
}

void Entity::moveSpriteY(World& w) {
    y += speedY;
    falling++;
    fixCollisionInDirection(w, 0, speedY);
    if (solid_ > 0) {
        if (speedY < 0) falling = 0; else jumping = 99;
        speedY = 0;
    }
}

// ======================================================================== behaviour

void Entity::update(World& w) {
    switch (kind) {
        case EntityKind::Trigger: updateTrigger(w); break;
        case EntityKind::Pole: updatePole(w); break;
        case EntityKind::Flip: updateFlip(w); break;
        case EntityKind::Squish: updateSquish(w); break;
        case EntityKind::EndBoxSpin: updateEndBoxSpin(w); break;
        case EntityKind::EndBox: updateEndBox(w); break;
        case EntityKind::Npc: updateNpc(w); break;
        case EntityKind::Custom: break;
        case EntityKind::Walker:
            tickNpcBase(w); frame += 0.1; costume = CosWalk1 + (int)std::floor(fmodPos(frame, 4)); break;
        case EntityKind::Danger: tickNpcBase(w); updateDanger(w); break;
    }
}

void Entity::tickNpcBase(World& w) {
    speedY += -1;
    moveSpriteY(w);
    if (dir > 0) { if (speedX < 2) speedX += 2.5; }
    else { if (speedX > -2) speedX += -2.5; }
    moveSpriteX(w);
    checkFlip(w);
    getTile(w, x + (dir / 90) * 32, y);
    if (tileShape_ == "#") {
        getTile(w, x + (dir / 90) * 32, y + 32);
        if (falling < 5 && tileShape_.empty())
            speedY = 9;       // hop up one-tile ledges
    }
}

void Entity::checkFlip(World& w) {
    if (w.bumpIndex < 1) return;
    getTile(w, x - width, y - height - 8);
    if (tileIndex_ + 1 != w.bumpIndex) {
        getTile(w, x + width, y - height - 8);
        if (tileIndex_ + 1 != w.bumpIndex) return;
    }
    speedY = 14;
    speedX = w.player.dir / 45;
    kind = EntityKind::Flip;
    allAround = true;
    dir = -90;
    rotationDegrees = 180;
}

void Entity::updateDanger(World& w) {
    frame += 0.1;
    costume = CosDanger1 + (int)std::floor(fmodPos(frame, 4));
    if (!touchesPlayer(w, 0.8)) return;

    auto& p = w.player;
    bool slidingInto = (p.action == "slide" || p.action == "crouch") && std::abs(p.speedX) > 1;
    if (p.bopY.has_value() || slidingInto) {
        bool slide = p.action == "slide" || p.action == "crouch";
        if (!slide) {
            speedX = 0;
            w.playSound("stomped");
            w.bouncePlayer = 5;
        } else {
            speedX = (p.dir / 90) * 3;
            w.playSound("tennis_hit");
        }
        w.addParticle(ParticleKind::Score100, x, y);
        die();
    } else {
        w.killPlayer();
    }
}

void Entity::die() {
    lastDir = dir;
    kind = EntityKind::Squish;
    speedY = 15;
    allAround = true;
    dir = 90;
    rotationDegrees = 0;
    costume = lastDir > 0 ? CosDangerDie2 : CosDangerDie;
    layer = EntityLayer::Normal;
}

void Entity::updateSquish(World&) {
    if (!(speedY > 0)) layer = EntityLayer::Front;
    if (lastDir > 0) rotationDegrees -= 8; else rotationDegrees += 8;
    speedY += -1;
    y += speedY;
    x += speedX;
    if (y < -200) removed = true;
}

void Entity::updateFlip(World& w) {
    x += speedX;
    speedY += -1;
    y += speedY;
    if (y < w.camY - 200) removed = true;
}

void Entity::updateNpc(World& w) {
    frame += 0.1;
    costume = CosDanger1 + (int)std::floor(fmodPos(frame, 4));
    if (w.mode != WorldMode::Playing) return;
    auto& p = w.player;
    if (touchesPlayer(w, 0.9) && !(p.action == "slide" || p.action == "crouch") && w.input.usePressed) {
        w.playSound("stomped");
        w.showDialog(extra);
        std::string nid = id();
        if (!nid.empty()) w.fireEvent("use", nid);
    }
}

void Entity::updatePole(World& w) {
    frame += 1;
    costume = CosPole;
    real temp = fmodPos(frame / 16, 8);
    if (std::floor(temp) == 4) y += 4;
    if (std::floor(temp) == 7) y += -4;
    if (temp > 4.2 && w.mode == WorldMode::Playing) {
        // pole_hitbox costume is narrow (about 7 wide) and tall
        if (std::abs(w.player.x - x) < w.player.width + 4 && std::abs(w.player.y - y) < w.player.height + 29)
            w.killPlayer();
    }
}

void Entity::updateEndBox(World& w) {
    sizePct = 20;
    frame += 0.15 * 7.8;
    costume = CosGlobe1 + (int)std::fmod(std::floor(frame), 24);
    if (w.mode != WorldMode::Playing) return;
    auto& p = w.player;
    real d = std::sqrt((p.x - x) * (p.x - x) + (p.y - y) * (p.y - y));
    if (touchesPlayer(w) && d < 28) {
        w.beginLevelComplete();
        kind = EntityKind::EndBoxSpin;
        speedY += 12;
    }
}

void Entity::updateEndBoxSpin(World& w) {
    frame += 0.15 * 8.8;
    costume = CosGlobe1 + (int)std::fmod(std::floor(frame), 24);
    if (!(speedY > 0)) {
        layer = EntityLayer::Front;
        speedY += -1.2;
        if (sizePct < 250) sizePct += 0.2;
    } else {
        speedY += -.4;
    }
    y += speedY;
    x += speedX;
    if (y < -200) {
        removed = true;
        w.onWin();
    }
}

void Entity::updateTrigger(World& w) {
    if (w.mode != WorldMode::Playing) return;
    if (!touchesPlayer(w) || !w.input.usePressed) return;
    if (param.empty() || !w.hasLevel(param)) return;
    w.activateTrigger(*this, param);
}

} // namespace mg
