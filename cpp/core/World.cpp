#include "World.hpp"

namespace mg {

World::World(LevelData* lvl, const std::string& id, std::function<LevelData*(const std::string&)> source)
    : levelSource(std::move(source)) {
    level = lvl;
    load(lvl, id, LoadKind::Respawn, 0);
}

void World::cameraPan(real px, real py, real seconds) {
    int ticks = (int)std::nearbyint(seconds * GameConstants::TicksPerSecond);
    if (ticks <= 0) { cameraLock(px, py); return; }
    panFromX_ = camX; panFromY_ = camY; panToX_ = px; panToY_ = py; panTick_ = 0; panTotal_ = ticks; camLocked = false;
}

void World::applyScriptCamera() {
    if (panTotal_ > 0) {
        panTick_++;
        real t = std::min(1.0, (real)panTick_ / panTotal_);
        t = t * t * (3 - 2 * t);
        camX = panFromX_ + (panToX_ - panFromX_) * t;
        camY = panFromY_ + (panToY_ - panFromY_) * t;
        if (panTick_ >= panTotal_) { panTotal_ = 0; camLocked = true; camLockX = panToX_; camLockY = panToY_; }
    } else if (camLocked) {
        camX = camLockX; camY = camLockY;
    }
    if (shakeTicks_ > 0) {
        shakeTicks_--;
        std::uniform_real_distribution<real> d(0.0, 1.0);
        shakeX = (d(camRng_) * 2 - 1) * shakeAmt_;
        shakeY = (d(camRng_) * 2 - 1) * shakeAmt_;
    } else {
        shakeX = shakeY = 0;
    }
}

Entity* World::findEntity(const std::string& id) {
    for (auto& e : entities) if (!e->removed && e->id() == id) return e.get();
    return nullptr;
}

void World::attachScript(Entity& e, bool fresh) {
    auto chunk = scripting::ScriptLibrary::get(e.def ? e.def->get("script") : "");
    if (!chunk) return;
    e.script = std::make_shared<scripting::ScriptHost>(this, chunk, &e, false, fresh);
    if (fresh) { e.script->fire("start"); e.script->flush(); }
}

Entity* World::spawnEntity(const std::string& type, int cx, int cy, const std::map<std::string, std::string>* props) {
    auto def = std::make_shared<EntityDef>();
    def->x = cx; def->y = cy; def->type = type;
    if (props) def->props = *props;
    auto e = Entity::spawn(def, level->height, false);
    if (!e) return nullptr;
    Entity* raw = e.get();
    entities.push_back(std::move(e));
    attachScript(*raw, true);
    return raw;
}

void World::removeEntitiesAt(int cx, int cy) {
    for (auto& e : entities)
        if ((int)std::floor(e->x / 32) == cx && (int)std::floor(e->y / 32) == cy) e->removed = true;
}

void World::gotoLevel(const std::string& id) {
    if (mode == WorldMode::Playing && hasLevel(id)) {
        mode = WorldMode::Frozen;
        pending = LoadRequest{id, LoadKind::Respawn, true, 0, false};
    }
}

// ================================================================ loading

void World::load(LevelData* data, const std::string& id, LoadKind kind, int entrySide) {
    level = data;
    levelId = id;
    tiles = data->tiles;

    int spawnIndex = -1;
    for (size_t i = 0; i < tiles.size(); i++) if (tiles[i] == TileInfo::PlayerSpawn) { spawnIndex = (int)i; break; }
    if (spawnIndex >= 0 && !godMode) tiles[spawnIndex] = TileInfo::Air;

    entities.clear();
    particles.clear();
    for (auto& def : data->entities) {
        auto e = Entity::spawn(std::make_shared<EntityDef>(def.clone()), data->height, false);   // a copy, so scripts can change props freely
        if (e) entities.push_back(std::move(e));
    }
    camLocked = false; panTotal_ = 0; shakeTicks_ = 0; shakeX = shakeY = 0; camZoom = 1; playerFrozen = false;
    stateCmd_.clear();

    coins = 0;
    bouncePlayer = 0;
    bumpIndex = 0;
    mode = WorldMode::Playing;
    pending.reset();
    deathQueued_ = false;
    deathStarted_ = false;
    playerBehindTiles = false;

    if (kind == LoadKind::Respawn) {
        real x = 1, y = 1;
        if (spawnIndex >= 0) {
            x = spawnIndex / data->height;
            y = spawnIndex % data->height;
        }
        real px = x * 32 + 16;
        real py = y * 32 + 18 - 32;      // original: ((y*32) + height) - 32 with height 18
        player.reset(px, py);
        camX = px;
        camY = py;
    } else {
        player.x = entrySide < 0 ? data->width * 32 + 16 - 40 : 10;
        player.jumping = 99;
        player.falling = 99;
        camX = player.x;
        camY = player.y;
    }
    screenX = (int)std::floor(player.x / GameConstants::stageWidth());
    screenY = (int)std::floor(player.y / GameConstants::StageHeight);
    moveCamera();
    player.paint();

    auto chunk = scripting::ScriptLibrary::get(data->script);
    host = chunk ? std::make_shared<scripting::ScriptHost>(this, chunk) : nullptr;
    if (host) host->fire("start");

    auto pchunk = scripting::ScriptLibrary::get(data->playerScript.empty() ? defaultPlayerScript() : data->playerScript);
    playerHost = pchunk ? std::make_shared<scripting::ScriptHost>(this, pchunk, nullptr, true) : nullptr;
    if (playerHost) playerHost->fire("start");
    std::vector<Entity*> snapshot;
    for (auto& e : entities) snapshot.push_back(e.get());
    for (auto* e : snapshot) attachScript(*e, true);
}

void World::onWin() {
    fireEvent("win");
    pending = LoadRequest{level->next.value_or(""), LoadKind::Respawn, true, 0, true};
}

void World::activateTrigger(Entity& e, const std::string& target) {
    if (e.typeId == "pipe") {
        mode = WorldMode::Pipe;
        pipeTarget_ = target;
        pipePhase_ = 0;
        pipeTimer_ = 0;
        player.pipeBegin(player.x < e.x ? 16 : -16);
    } else {
        mode = WorldMode::Frozen;
        pending = LoadRequest{target, LoadKind::Respawn, true, 0, false};
    }
}

void World::tileAt(real px, real py, int& tile, int& index) const {
    tile = 0;
    index = -1;
    if (py < 0) return;
    real gx = std::floor(px / 32), gy = std::floor(py / 32);
    if (gx < 0 || gx >= level->width || gy >= level->height) return;
    index = (int)gx * level->height + (int)gy;
    tile = tiles[index];
}

// ================================================================ tick

void World::tick(InputState in) {
    if (playerFrozen) in = InputState();
    input = in;
    tickCount++;
    if (host) host->tick();
    if (playerHost) playerHost->tick();
    switch (mode) {
        case WorldMode::Playing:
            player.movePlayer();
            moveEntities();
            player.afterEnemy();
            tickParticles();
            break;
        case WorldMode::Dying:
            tickDying();
            break;
        case WorldMode::Complete:
            player.completeTick();
            moveEntities();
            tickParticles();
            break;
        case WorldMode::Pipe:
            tickPipe();
            break;
        case WorldMode::Frozen:
            break;
    }

    if (deathQueued_ && mode == WorldMode::Playing) {
        deathQueued_ = false;
        mode = WorldMode::Dying;
        deathStarted_ = false;
    }
    if (pending.has_value() && mode != WorldMode::Dying) mode = WorldMode::Frozen;

    if (!stateCmd_.empty()) {
        std::string cmd = stateCmd_;
        stateCmd_.clear();
        if (cmd == "save") saveState(); else loadState();
    }
}

// ================================================================ level state

void World::saveState() {
    if (mode != WorldMode::Playing || deathQueued_) { if (notice) notice("Can't save state right now"); return; }
    (*states)[levelId] = captureState();
    if (notice) notice("State saved");
}

void World::loadState() {
    auto it = states->find(levelId);
    if (it == states->end()) { if (notice) notice("No saved state for this level"); return; }
    restoreState(it->second);
    if (notice) notice("State loaded");
}

WorldSnapshot World::captureState() {
    WorldSnapshot s;
    s.levelId = levelId; s.tiles = tiles; s.coins = coins; s.bouncePlayer = bouncePlayer; s.bumpIndex = bumpIndex;
    s.screenX = screenX; s.screenY = screenY; s.camX = camX; s.camY = camY; s.camXControl = camXControl; s.camYControl = camYControl;
    s.playerBehindTiles = playerBehindTiles; s.tickCount = tickCount;
    s.player = player;
    s.flags = *flags;
    if (host) s.levelGlobals = host->captureGlobals();
    if (playerHost) s.playerGlobals = playerHost->captureGlobals();
    for (auto& e : entities) {
        if (e->removed || !e->def) continue;
        WorldSnapshot::Ent se;
        se.def = e->def->clone();
        se.state = e->clone();
        if (e->script) se.globals = e->script->captureGlobals();
        se.touching = e->touching;
        s.entities.push_back(std::move(se));
    }
    return s;
}

void World::restoreState(const WorldSnapshot& s) {
    tiles = s.tiles;
    coins = s.coins; bouncePlayer = s.bouncePlayer; bumpIndex = s.bumpIndex;
    screenX = s.screenX; screenY = s.screenY; camX = s.camX; camY = s.camY; camXControl = s.camXControl; camYControl = s.camYControl;
    playerBehindTiles = s.playerBehindTiles; tickCount = s.tickCount;
    if (s.player) { player = *s.player; player.setWorld(this); }
    player.paint();
    flags->clear();
    for (auto& kv : s.flags) (*flags)[kv.first] = kv.second;
    if (host) host->restoreGlobals(s.levelGlobals ? &*s.levelGlobals : nullptr);
    if (playerHost) playerHost->restoreGlobals(s.playerGlobals ? &*s.playerGlobals : nullptr);

    entities.clear();
    particles.clear();
    for (auto& se : s.entities) {
        auto e = Entity::spawn(std::make_shared<EntityDef>(se.def.clone()), level->height, false);
        if (!e) continue;
        e->restoreState(*se.state);
        e->touching = se.touching;
        Entity* raw = e.get();
        entities.push_back(std::move(e));
        attachScript(*raw, false);
        if (raw->script) raw->script->restoreGlobals(se.globals ? &*se.globals : nullptr);
    }
    mode = WorldMode::Playing;
    pending.reset();
    deathQueued_ = false;
    deathStarted_ = false;
    playerFrozen = false;
    camLocked = false; panTotal_ = 0; shakeTicks_ = 0; shakeX = shakeY = 0; camZoom = 1;
}

void World::moveEntities() {
    for (size_t i = 0; i < entities.size(); i++) {
        Entity* e = entities[i].get();
        e->update(*this);
        if (e->script && !e->removed) {
            bool touching = e->touchesPlayer(*this);
            if (touching && !e->touching) e->script->fire("touch");
            e->touching = touching;
            e->script->tick();
        }
    }
    for (size_t i = 0; i < entities.size(); i++)
        if (entities[i]->removed && entities[i]->script) { entities[i]->script->fire("remove"); entities[i]->script->flush(); }
    entities.erase(std::remove_if(entities.begin(), entities.end(), [](const std::unique_ptr<Entity>& e) { return e->removed; }), entities.end());
}

void World::tickParticles() {
    for (size_t i = 0; i < particles.size(); i++) particles[i]->tick();
    particles.erase(std::remove_if(particles.begin(), particles.end(), [](const std::shared_ptr<Particle>& p) { return p->dead; }), particles.end());
}

void World::tickDying() {
    if (!deathStarted_) {
        deathStarted_ = true;
        playSound("lose_life");
        player.beginDeath();
        return;
    }
    bool onScreen = player.deathTick();
    tickParticles();
    if (player.deathCaught()) {
        playSound("wood_tap");
        player.recover();
        mode = WorldMode::Playing;
        return;
    }
    if (!onScreen) {
        mode = WorldMode::Frozen;
        pending = LoadRequest{levelId, LoadKind::Respawn, false, 0, false};
    }
}

void World::tickPipe() {
    if (pipePhase_ == 0) {
        player.pipeSettleTick();
        pipeTimer_++;
        if (player.falling < 1 || pipeTimer_ > 90) {
            pipePhase_ = 1;
            pipeTimer_ = 0;
            playerBehindTiles = true;
        }
    } else {
        player.pipeSinkTick();
        pipeTimer_++;
        if (pipeTimer_ >= 20) {
            mode = WorldMode::Frozen;
            pending = LoadRequest{pipeTarget_, LoadKind::Respawn, true, 0, false};
        }
    }
    tickParticles();
}

// ================================================================ camera

void World::moveCamera() {
    auto& p = player;
    int cm = level->cameraMode;
    if (godMode) {
        camX = p.x;
        camY += (p.y - camY) / 4;
    } else {
        switch (cm) {
            case 0:
            case 7:
                camX = p.x;
                camY += (p.y - camY) / 4;
                break;
            case 1:
                cameraScreenMode();
                break;
            case 2:
                camX += camXControl;
                camY += (p.y - camY) / 4;
                break;
            case 3:
                camX = p.x;
                camY += camYControl;
                break;
            case 4:
                camX += camXControl;
                camY += camYControl;
                break;
            case 5:
                camX = p.x;
                camY += (p.y - camY) / 4;
                camY += std::sin(tickCount / (real)GameConstants::TicksPerSecond * 40 * 3.14159265358979323846 / 180.0) * 6;
                break;
            case 6:
                camX += (p.x - camX) / 2;
                camY += (p.y - camY) / 4;
                break;
        }
    }
    if (!godMode && !(cm == 1 || cm == 7))
        limitCamera(272, 180);
    applyScriptCamera();
}

void World::cameraScreenMode() {
    auto& p = player;
    if (p.x < camX - GameConstants::stageWidth() / 2.0) screenX--;
    if (p.x > camX + GameConstants::stageWidth() / 2.0) screenX++;
    if (p.y < camY - GameConstants::StageHeight / 2.0) screenY--;
    if (p.y > camY + GameConstants::StageHeight / 2.0) screenY++;
    camX = screenX * GameConstants::stageWidth() + GameConstants::stageWidth() / 2.0;
    camY = screenY * GameConstants::StageHeight + GameConstants::StageHeight / 2.0;
}

void World::limitCamera(real edgeX, real edgeY) {
    if (camX < edgeX) camX = edgeX;
    if (camY < edgeY) camY = edgeY;
    if (camX > 32.0 * level->width - edgeX) camX = 32.0 * level->width - edgeX;
    if (camY > 32.0 * level->height - edgeY) camY = 32.0 * level->height - edgeY;
}

} // namespace mg
