#pragma once
// === YOUR CONTENT GOES HERE ===
// Add tiles and entities in GameContent.cpp. Both the game and the level editor call registerAll() at
// start-up, so whatever you register shows up in the editor palette and in levels.
#include "World.hpp"

namespace mg {

struct GameContent {
    static void registerAll();
};

// Example custom entity: a slime that walks like the original walkers (it hops up one-tile ledges and
// turns at walls) and kills the player on touch.
class Slime : public Entity {
public:
    explicit Slime(const EntityDef& def);
    void update(World& w) override;
    std::unique_ptr<Entity> clone() const override { return std::make_unique<Slime>(*this); }
};

// Invisible rectangle (visible in the editor) that fires the script events enter / exit / use.
class Zone : public Entity {
public:
    Zone(const EntityDef& def, bool editor);
    void update(World& w) override;
    std::unique_ptr<Entity> clone() const override { return std::make_unique<Zone>(*this); }
    void restoreState(const Entity& s) override {
        Entity::restoreState(s);
        if (auto* z = dynamic_cast<const Zone*>(&s)) inside_ = z->inside_;
    }
private:
    std::string id_;
    real hw_, hh_;
    bool inside_ = false;
};

// A visible object the player activates with the use key; fires the script event "use".
class ScriptButton : public Entity {
public:
    explicit ScriptButton(const EntityDef& def);
    void update(World& w) override;
    std::unique_ptr<Entity> clone() const override { return std::make_unique<ScriptButton>(*this); }
private:
    std::string id_;
};

} // namespace mg
