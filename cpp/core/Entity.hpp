#pragma once
#include "Common.hpp"
#include "LevelData.hpp"
#include "Particle.hpp"
#include "TileDef.hpp"

namespace mg {

class World;
namespace scripting { class ScriptHost; }

enum class EntityKind {
    Walker,       // tile 29, harmless wanderer ("nomal")
    Danger,       // tile 30, hurts unless stomped / slid into
    Npc,          // tile 46, "useable" star npc that opens a dialog
    EndBox,       // tile 47, level goal (spinning globe)
    EndBoxSpin,   // goal after being touched
    Pole,         // tile 68, piranha plant that pops out of a pipe
    Trigger,      // tiles 77-79, doors and pipes to other levels
    Squish,       // defeated enemy tumbling off screen
    Flip,         // enemy bumped from below (hook, see World::bumpIndex)
    Custom,       // entities defined in GameContent
};

enum class EntityLayer { BehindTiles, Normal, Front };

// One running entity (a "clone" of the original Enemy sprite). Costume numbers refer to the original
// "Enemy" sprite costume list.
//
// Level-state saving (replaces the C# reflection in StateCopy): a custom subclass that has its own
// fields overrides clone() (return std::make_unique<MyType>(*this)) and restoreState() (copy its fields,
// then call Entity::restoreState). Entities with no extra fields need neither.
class Entity {
public:
    static constexpr int CosBig = 1, CosPole = 2, CosPoleHitbox = 3, CosPipeTrigger = 4, CosDoorTrigger = 5, CosDoorWide = 6,
        CosEndGlobeStatic = 7, CosWalk1 = 8, CosDanger1 = 12, CosGlobe1 = 16, CosDangerDie = 43, CosDangerDie2 = 44;

    EntityKind kind = EntityKind::Walker;
    std::string typeId;
    std::optional<std::string> texture;
    real x = 0, y = 0;
    real width = 0, height = 0;          // half extents
    real dir = 90;
    real speedX = 0, speedY = 0;
    int falling = 0, jumping = 0;
    real frame = 0;
    int costume = 1;
    real sizePct = 200;
    std::string extra;
    std::string param;
    real rotationDegrees = 0;
    bool allAround = false;
    real lastDir = 0;
    bool removed = false;
    // The level definition this entity was created from (a private copy; script prop changes land here).
    std::shared_ptr<EntityDef> def;
    std::shared_ptr<scripting::ScriptHost> script;
    bool touching = false;
    bool visible = true;
    EntityLayer layer = EntityLayer::Normal;

    virtual ~Entity() = default;

    // The properties stored with the entity in the level file (same storage as def->props).
    std::map<std::string, std::string>& props() { return def->props; }
    std::string id() const { if (!def) return ""; auto it = def->props.find("id"); return it == def->props.end() ? "" : it->second; }
    bool flipX() const { return !allAround && dir < 0; }

    static std::unique_ptr<Entity> spawn(std::shared_ptr<EntityDef> d, int levelHeight, bool editor);
    static std::unique_ptr<Entity> spawnBuiltin(std::shared_ptr<EntityDef> d, bool editor);

    // Helper for custom entities: put the entity in cell (cx, cy), standing on the bottom of the cell.
    void placeInCell(int cx, int cy, real halfWidth, real halfHeight) {
        width = halfWidth; height = halfHeight; x = cx * 32 + 16; y = cy * 32 + halfHeight;
    }

    // Hit test against the player's box. scale shrinks this entity's box.
    bool touchesPlayer(World& w, real scale = 1.0);

    // "when I receive move enemy" for this clone.
    virtual void update(World& w);

    virtual std::unique_ptr<Entity> clone() const { return std::make_unique<Entity>(*this); }
    virtual void restoreState(const Entity& s);

protected:
    void getTile(World& w, real px, real py);
    void moveSpriteX(World& w);
    void moveSpriteY(World& w);
    void stepWalker(World& w) { tickNpcBase(w); }

private:
    static constexpr real T = GameConstants::Tiny;
    int solid_ = 0;
    real fixDx_ = 0, fixDy_ = 0, modX_ = 0, modY_ = 0, offsetY_ = 0;
    int tile_ = 0;
    std::string tileShape_;
    int tileIndex_ = 0;

    void fixCollisionAtPoint(World& w, real px, real py, bool feet);
    void fixCollisionInDirection(World& w, real dx, real dy);
    void tickNpcBase(World& w);
    void checkFlip(World& w);
    void updateDanger(World& w);
    void die();
    void updateSquish(World& w);
    void updateFlip(World& w);
    void updateNpc(World& w);
    void updatePole(World& w);
    void updateEndBox(World& w);
    void updateEndBoxSpin(World& w);
    void updateTrigger(World& w);
};

// ---------------------------------------------------------------- entity types

enum class PropKind { Text, Int, Number, Level };

struct PropDef {
    std::string name, prompt, defaultValue;
    PropKind kind = PropKind::Text;
    bool required = false;
};

struct EntityType {
    using CreateFn = std::function<std::unique_ptr<Entity>(std::shared_ptr<EntityDef>, bool editor)>;

    std::string id;
    std::string name;
    CreateFn create;
    std::vector<PropDef> props;
    std::optional<std::string> texture;
    std::string category = "Entities";
    std::string brushTileId;

    EntityType(std::string i, std::string n, CreateFn c) : id(std::move(i)), name(std::move(n)), create(std::move(c)) {}

    EntityType& prop(const std::string& pname, const std::string& prompt, const std::string& defaultValue = "",
                     PropKind kind = PropKind::Text, bool required = false) {
        props.push_back({pname, prompt, defaultValue, kind, required});
        return *this;
    }
};

class EntityRegistry {
public:
    static const std::vector<EntityType*>& all() { ensureInit(); return s().all; }
    static EntityType* find(const std::string& id) {
        ensureInit();
        auto it = s().byId.find(id);
        return it == s().byId.end() ? nullptr : it->second;
    }
    // Adds an entity type (and its editor brush tile). Aborts if the id is taken.
    static EntityType* registerType(EntityType* type);
private:
    struct S { std::unordered_map<std::string, EntityType*> byId; std::vector<EntityType*> all; bool init = false; };
    static S& s() { static S st; return st; }
    static void ensureInit();
    static EntityType& builtin(const std::string& id, const std::string& name);
};

} // namespace mg
