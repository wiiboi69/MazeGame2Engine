#pragma once
#include "Common.hpp"
#include "Entity.hpp"
#include "LevelData.hpp"
#include "Particle.hpp"
#include "Player.hpp"
#include "Scripting.hpp"
#include <random>

namespace mg {

enum class WorldMode { Playing, Dying, Complete, Pipe, Frozen };

enum class LoadKind {
    Respawn,   // put the player on the level's spawn marker
    Connect,   // keep the player and walk in from the side
};

// Something the host (game / editor) must do between ticks: swap the level, usually with a wipe.
struct LoadRequest {
    std::string level;
    LoadKind kind = LoadKind::Respawn;
    bool wipe = false;
    int entrySide = 0;
    bool win = false;
};

// A saved copy of one running level (see World::captureState).
struct WorldSnapshot {
    std::string levelId;
    std::vector<int> tiles;
    int coins = 0, bouncePlayer = 0, bumpIndex = 0, screenX = 0, screenY = 0;
    real camX = 0, camY = 0, camXControl = 0, camYControl = 0;
    bool playerBehindTiles = false;
    int64_t tickCount = 0;
    std::optional<Player> player;
    std::map<std::string, std::string> flags;
    std::optional<std::map<std::string, scripting::Value>> levelGlobals, playerGlobals;

    struct Ent {
        EntityDef def;
        std::shared_ptr<Entity> state;   // a clone() of the running entity
        std::optional<std::map<std::string, scripting::Value>> globals;
        bool touching = false;
    };
    std::vector<Ent> entities;
};

// One running level: tiles, player, entities, particles and camera. Call tick() 30 times a second
// with the current InputState. Mirrors the original "game loop":
// controls, move player, move enemy, move player after enemy, position tiles.
class World {
public:
    LevelData* level = nullptr;          // owned by the LevelLibrary
    std::string levelId;
    std::vector<int> tiles;              // live tile grid (column-major, copy of the level so gems can be removed)

    Player player{this};
    std::vector<std::unique_ptr<Entity>> entities;
    std::vector<std::shared_ptr<Particle>> particles;

    real camX = 0, camY = 0;
    real camXControl = 0, camYControl = 0;
    int screenX = 0, screenY = 0;

    int coins = 0;
    int bouncePlayer = 0;     // remaining ticks of stomp bounce
    int bumpIndex = 0;        // 1-based tile index, 0 = none
    bool godMode = false;     // fly / no-collision debug movement
    WorldMode mode = WorldMode::Playing;
    std::optional<LoadRequest> pending;
    bool playerBehindTiles = false;
    int64_t tickCount = 0;
    InputState input;

    std::function<LevelData*(const std::string&)> levelSource;
    std::function<void(const std::string&)> soundRequested;
    std::function<void(const std::string&)> dialogRequested;
    std::function<void(const std::string&)> notice;

    // Script flags (saved with the save slot). Shared with the game, which binds this to the slot.
    std::shared_ptr<std::map<std::string, std::string>> flags = std::make_shared<std::map<std::string, std::string>>();
    std::shared_ptr<scripting::ScriptHost> host;          // the running script of the current level
    std::shared_ptr<scripting::ScriptHost> playerHost;    // the player's own script runtime
    static std::string& defaultPlayerScript() { static std::string s; return s; }

    // level state saving
    std::shared_ptr<std::map<std::string, WorldSnapshot>> states = std::make_shared<std::map<std::string, WorldSnapshot>>();
    void requestSaveState() { stateCmd_ = "save"; }
    void requestLoadState() { stateCmd_ = "load"; }
    bool playerFrozen = false;

    // script-controlled camera
    bool camLocked = false;
    real camLockX = 0, camLockY = 0;
    real camZoom = 1;
    real shakeX = 0, shakeY = 0;

    World(LevelData* lvl, const std::string& id, std::function<LevelData*(const std::string&)> source = nullptr);
    World(const World&) = delete;
    World& operator=(const World&) = delete;

    int width() const { return level->width; }
    int height() const { return level->height; }

    void cameraLock(real px, real py) { panTotal_ = 0; camLocked = true; camLockX = px; camLockY = py; camX = px; camY = py; }
    void cameraFollow() { panTotal_ = 0; camLocked = false; }
    void cameraPan(real px, real py, real seconds);
    void cameraShake(real amount, real seconds) { shakeAmt_ = amount; shakeTicks_ = (int)std::nearbyint(seconds * GameConstants::TicksPerSecond); }

    Entity* findEntity(const std::string& id);
    void attachScript(Entity& e, bool fresh);
    void dialogClosed() { if (host) host->dialogClosed(); }
    void fireEvent(const std::string& ev, const std::optional<std::string>& target = std::nullopt) { if (host) host->fire(ev, target); }

    Entity* spawnEntity(const std::string& type, int cx, int cy, const std::map<std::string, std::string>* props = nullptr);
    void removeEntitiesAt(int cx, int cy);
    void gotoLevel(const std::string& id);

    void load(LevelData* data, const std::string& id, LoadKind kind, int entrySide);
    bool hasLevel(const std::string& id) const { return !id.empty() && levelSource && levelSource(id) != nullptr; }
    bool hasLevel(const std::optional<std::string>& id) const { return id.has_value() && hasLevel(*id); }

    void requestConnect(const std::string& lvl, int side) { pending = LoadRequest{lvl, LoadKind::Connect, false, side, false}; }
    void onWin();
    void activateTrigger(Entity& e, const std::string& target);

    void tileAt(real px, real py, int& tile, int& index) const;
    void playSound(const std::string& name) { if (soundRequested) soundRequested(name); }
    void showDialog(const std::string& text) { if (dialogRequested) dialogRequested(text); }
    void addParticle(ParticleKind kind, real x, real y) { particles.push_back(std::make_shared<Particle>(kind, x, y)); }
    void killPlayer() { if (mode == WorldMode::Playing && !godMode) { deathQueued_ = true; fireEvent("death"); } }
    void beginLevelComplete() { if (mode == WorldMode::Playing) mode = WorldMode::Complete; }

    void tick(InputState in);

    WorldSnapshot captureState();
    void restoreState(const WorldSnapshot& s);

    void moveCamera();

private:
    std::string stateCmd_;
    real panFromX_ = 0, panFromY_ = 0, panToX_ = 0, panToY_ = 0, shakeAmt_ = 0;
    int panTick_ = 0, panTotal_ = 0, shakeTicks_ = 0;
    bool deathQueued_ = false, deathStarted_ = false;
    std::string pipeTarget_;
    int pipePhase_ = 0, pipeTimer_ = 0;
    std::mt19937 camRng_{mgSeed()};

    void applyScriptCamera();
    void saveState();
    void loadState();
    void moveEntities();
    void tickParticles();
    void tickDying();
    void tickPipe();
    void cameraScreenMode();
    void limitCamera(real edgeX, real edgeY);
};

} // namespace mg
