// Host functions callable from scripts (ScriptNatives.cs + EntityNatives.cs).
// Positions are in tiles (fractions allowed): an entity standing in cell 5,3 is at x=5.5.
// Functions without an id act on "self": the entity that owns the script, or the player in a player script.
#include "Scripting.hpp"
#include "World.hpp"
#include <random>

namespace mg::scripting {

namespace {

constexpr double U = 32.0;

std::unordered_map<std::string, NativeInfo>& table() { static std::unordered_map<std::string, NativeInfo> t; return t; }
bool g_init = false;

using Args = std::vector<Value>;
using Ctx = ScriptContext;

double selfX(Ctx& c) { return c.self ? c.self->x : (c.isPlayer ? c.world->player.x : 0); }
double selfY(Ctx& c) { return c.self ? c.self->y : (c.isPlayer ? c.world->player.y : 0); }

template <typename F>
void with(Ctx& c, const Value& id, F act) {
    if (Entity* e = c.world->findEntity(id.asString())) act(*e);
}

void setPos(Ctx& c, Entity* e, double x, double y) {
    if (e) { e->x = x * U; e->y = y * U; }
    else if (c.isPlayer) { c.world->player.x = x * U; c.world->player.y = y * U; }
}

void setSpeed(Ctx& c, Entity* e, double sx, double sy) {
    if (e) { e->speedX = sx; e->speedY = sy; }
    else if (c.isPlayer) { c.world->player.speedX = sx; c.world->player.speedY = sy; }
}

std::mt19937& rng() { static std::mt19937 r{mgSeed()}; return r; }

void reg(const char* name, int arity, NativeFn fn) { ScriptNatives::registerNative(name, arity, std::move(fn)); }

void registerEntityNatives() {
    // ---------------------------------------------------------------- self
    reg("self_x", 0, [](Ctx& c, Args&) { return Value::from(selfX(c) / U); });
    reg("self_y", 0, [](Ctx& c, Args&) { return Value::from(selfY(c) / U); });
    reg("set_self_pos", 2, [](Ctx& c, Args& a) { setPos(c, c.self, a[0].asNumber(), a[1].asNumber()); return Value::nil(); });
    reg("set_self_speed", 2, [](Ctx& c, Args& a) { setSpeed(c, c.self, a[0].asNumber(), a[1].asNumber()); return Value::nil(); });
    reg("self_dir", 0, [](Ctx& c, Args&) { return Value::from(c.self ? c.self->dir : c.isPlayer ? c.world->player.dir : 0.0); });
    reg("set_self_dir", 1, [](Ctx& c, Args& a) {
        if (c.self) c.self->dir = a[0].asNumber(); else if (c.isPlayer) c.world->player.dir = a[0].asNumber();
        return Value::nil(); });
    reg("self_type", 0, [](Ctx& c, Args&) { return Value::from(c.self ? c.self->typeId : c.isPlayer ? std::string("player") : std::string()); });
    reg("self_id", 0, [](Ctx& c, Args&) { return Value::from(c.self ? c.self->id() : std::string()); });
    reg("self_prop", 1, [](Ctx& c, Args& a) {
        if (c.self && c.self->def) {
            auto it = c.self->props().find(a[0].asString());
            if (it != c.self->props().end()) return Value::parse(it->second);
        }
        return Value::nil(); });
    reg("set_self_prop", 2, [](Ctx& c, Args& a) { if (c.self && c.self->def) c.self->props()[a[0].asString()] = a[1].asString(); return Value::nil(); });
    reg("remove_self", 0, [](Ctx& c, Args&) { if (c.self) c.self->removed = true; return Value::nil(); });
    reg("hide_self", 0, [](Ctx& c, Args&) { if (c.self) c.self->visible = false; return Value::nil(); });
    reg("show_self", 0, [](Ctx& c, Args&) { if (c.self) c.self->visible = true; return Value::nil(); });
    reg("self_touching", 0, [](Ctx& c, Args&) { return Value::from(c.self != nullptr && c.self->touchesPlayer(*c.world)); });

    // ---------------------------------------------------------------- entities by id
    reg("entity_exists", 1, [](Ctx& c, Args& a) { return Value::from(c.world->findEntity(a[0].asString()) != nullptr); });
    reg("entity_x", 1, [](Ctx& c, Args& a) { auto* e = c.world->findEntity(a[0].asString()); return Value::from((e ? e->x : 0.0) / U); });
    reg("entity_y", 1, [](Ctx& c, Args& a) { auto* e = c.world->findEntity(a[0].asString()); return Value::from((e ? e->y : 0.0) / U); });
    reg("set_entity_pos", 3, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { setPos(c, &e, a[1].asNumber(), a[2].asNumber()); }); return Value::nil(); });
    reg("move_entity", 3, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { e.x += a[1].asNumber() * U; e.y += a[2].asNumber() * U; }); return Value::nil(); });
    reg("set_entity_speed", 3, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { setSpeed(c, &e, a[1].asNumber(), a[2].asNumber()); }); return Value::nil(); });
    reg("remove_entity", 1, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { e.removed = true; }); return Value::nil(); });
    reg("hide_entity", 1, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { e.visible = false; }); return Value::nil(); });
    reg("show_entity", 1, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { e.visible = true; }); return Value::nil(); });
    reg("entity_prop", 2, [](Ctx& c, Args& a) {
        Entity* e = c.world->findEntity(a[0].asString());
        if (e && e->def) {
            auto it = e->props().find(a[1].asString());
            if (it != e->props().end()) return Value::parse(it->second);
        }
        return Value::nil(); });
    reg("set_entity_prop", 3, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { if (e.def) e.props()[a[1].asString()] = a[2].asString(); }); return Value::nil(); });
    reg("send_entity", 2, [](Ctx& c, Args& a) { with(c, a[0], [&](Entity& e) { if (e.script) e.script->fire("trigger", a[1].asString()); }); return Value::nil(); });
    reg("count_entities", 1, [](Ctx& c, Args& a) {
        std::string t = a[0].asString();
        int n = 0;
        for (auto& e : c.world->entities) if (!e->removed && (t.empty() || e->typeId == t)) n++;
        return Value::from(n); });
    reg("spawn_named", 4, [](Ctx& c, Args& a) {
        std::map<std::string, std::string> props{{"id", a[3].asString()}};
        c.world->spawnEntity(a[0].asString(), a[1].asInt(), a[2].asInt(), &props);
        return Value::nil(); });

    // ---------------------------------------------------------------- player
    reg("player_px", 0, [](Ctx& c, Args&) { return Value::from(c.world->player.x / U); });
    reg("player_py", 0, [](Ctx& c, Args&) { return Value::from(c.world->player.y / U); });
    reg("set_player_pos", 2, [](Ctx& c, Args& a) { c.world->player.x = a[0].asNumber() * U; c.world->player.y = a[1].asNumber() * U; return Value::nil(); });
    reg("set_player_speed", 2, [](Ctx& c, Args& a) { c.world->player.speedX = a[0].asNumber(); c.world->player.speedY = a[1].asNumber(); return Value::nil(); });
    reg("player_dir", 0, [](Ctx& c, Args&) { return Value::from(c.world->player.dir); });
    reg("freeze_player", 1, [](Ctx& c, Args& a) { c.world->playerFrozen = a[0].truthy(); return Value::nil(); });
    reg("key_down", 1, [](Ctx& c, Args& a) {
        const auto& i = c.world->input;
        std::string k = a[0].asString();
        bool r = k == "left" ? i.left : k == "right" ? i.right : k == "up" ? i.up : k == "down" ? i.down
               : k == "z" ? i.z : k == "x" ? i.x : k == "use" ? i.use : false;
        return Value::from(r); });

    // ---------------------------------------------------------------- camera
    reg("camera_x", 0, [](Ctx& c, Args&) { return Value::from(c.world->camX / U); });
    reg("camera_y", 0, [](Ctx& c, Args&) { return Value::from(c.world->camY / U); });
    reg("set_camera", 2, [](Ctx& c, Args& a) { c.world->cameraLock(a[0].asNumber() * U, a[1].asNumber() * U); return Value::nil(); });
    reg("camera_pan", 3, [](Ctx& c, Args& a) { c.world->cameraPan(a[0].asNumber() * U, a[1].asNumber() * U, a[2].asNumber()); return Value::nil(); });
    reg("camera_follow", 0, [](Ctx& c, Args&) { c.world->cameraFollow(); return Value::nil(); });
    reg("camera_shake", 2, [](Ctx& c, Args& a) { c.world->cameraShake(a[0].asNumber(), a[1].asNumber()); return Value::nil(); });
    reg("camera_zoom", 1, [](Ctx& c, Args& a) { c.world->camZoom = std::clamp(a[0].asNumber(), 0.25, 4.0); return Value::nil(); });

    // ---------------------------------------------------------------- level state
    reg("save_state", 0, [](Ctx& c, Args&) { c.world->requestSaveState(); return Value::nil(); });
    reg("load_state", 0, [](Ctx& c, Args&) { c.world->requestLoadState(); return Value::nil(); });
}

} // namespace

void ScriptNatives::registerNative(const std::string& name, int arity, NativeFn fn) {
    table()[name] = NativeInfo{name, arity, std::move(fn)};
}

NativeInfo* ScriptNatives::find(const std::string& name) {
    init();
    auto it = table().find(name);
    return it == table().end() ? nullptr : &it->second;
}

void ScriptNatives::init() {
    if (g_init) return;
    g_init = true;

    // ---- dialog / timing
    reg("say", 1, [](Ctx& c, Args& a) { c.world->showDialog(a[0].asString()); c.task->waitDialog = true; return Value::nil(); });
    reg("wait", 1, [](Ctx& c, Args& a) { c.task->waitTicks = (int)std::nearbyint(a[0].asNumber() * GameConstants::TicksPerSecond); return Value::nil(); });
    reg("wait_ticks", 1, [](Ctx& c, Args& a) { c.task->waitTicks = a[0].asInt(); return Value::nil(); });
    reg("print", -1, [](Ctx&, Args& a) {
        std::string s = "[script] ";
        for (size_t i = 0; i < a.size(); i++) { if (i) s += " "; s += a[i].asString(); }
        std::printf("%s\n", s.c_str());
        return Value::nil(); });

    // ---- flags & gems
    reg("flag", 1, [](Ctx& c, Args& a) {
        auto it = c.world->flags->find(a[0].asString());
        return it != c.world->flags->end() ? Value::parse(it->second) : Value::False(); });
    reg("set_flag", 2, [](Ctx& c, Args& a) { (*c.world->flags)[a[0].asString()] = a[1].asString(); return Value::nil(); });
    reg("gems", 0, [](Ctx& c, Args&) { return Value::from(c.world->coins); });
    reg("give_gems", 1, [](Ctx& c, Args& a) { c.world->coins += a[0].asInt(); return Value::nil(); });

    // ---- tiles & entities
    reg("tile", 2, [](Ctx& c, Args& a) {
        int x = a[0].asInt(), y = a[1].asInt(), h = c.world->height();
        if (x < 0 || y < 0 || x >= c.world->width() || y >= h) return Value::nil();
        return Value::from(TileRegistry::idOf(c.world->tiles[x * h + y])); });
    reg("set_tile", 3, [](Ctx& c, Args& a) {
        int x = a[0].asInt(), y = a[1].asInt(), h = c.world->height();
        if (x < 0 || y < 0 || x >= c.world->width() || y >= h) return Value::nil();
        auto* def = TileRegistry::find(a[2].asString());
        if (!def) { std::printf("[script] unknown tile '%s'\n", a[2].asString().c_str()); return Value::nil(); }
        c.world->tiles[x * h + y] = def->num;
        return Value::nil(); });
    reg("spawn", 3, [](Ctx& c, Args& a) { c.world->spawnEntity(a[0].asString(), a[1].asInt(), a[2].asInt()); return Value::nil(); });
    reg("remove_at", 2, [](Ctx& c, Args& a) { c.world->removeEntitiesAt(a[0].asInt(), a[1].asInt()); return Value::nil(); });

    // ---- player
    reg("teleport", 2, [](Ctx& c, Args& a) {
        c.world->player.x = a[0].asNumber() * 32 + 16; c.world->player.y = a[1].asNumber() * 32;
        c.world->player.speedX = c.world->player.speedY = 0; return Value::nil(); });
    reg("player_x", 0, [](Ctx& c, Args&) { return Value::from(std::floor(c.world->player.x / 32)); });
    reg("player_y", 0, [](Ctx& c, Args&) { return Value::from(std::floor(c.world->player.y / 32)); });
    reg("kill_player", 0, [](Ctx& c, Args&) { c.world->killPlayer(); return Value::nil(); });

    // ---- flow
    reg("play_sound", 1, [](Ctx& c, Args& a) { c.world->playSound(a[0].asString()); return Value::nil(); });
    reg("goto_level", 1, [](Ctx& c, Args& a) { c.world->gotoLevel(a[0].asString()); return Value::nil(); });
    reg("win", 0, [](Ctx& c, Args&) { c.world->beginLevelComplete(); return Value::nil(); });
    reg("level_id", 0, [](Ctx& c, Args&) { return Value::from(c.world->levelId); });
    reg("emit", 1, [](Ctx& c, Args& a) { c.host->fire("trigger", a[0].asString()); return Value::nil(); });

    // ---- math / strings
    reg("random", 2, [](Ctx&, Args& a) {
        int lo = a[0].asInt(), hi = a[1].asInt();
        if (hi < lo) return Value::from(lo);
        return Value::from(std::uniform_int_distribution<int>(lo, hi)(rng())); });
    reg("floor", 1, [](Ctx&, Args& a) { return Value::from(std::floor(a[0].asNumber())); });
    reg("abs", 1, [](Ctx&, Args& a) { return Value::from(std::abs(a[0].asNumber())); });
    reg("str", 1, [](Ctx&, Args& a) { return Value::from(a[0].asString()); });
    reg("len", 1, [](Ctx&, Args& a) { return Value::from((int)a[0].asString().size()); });

    registerEntityNatives();
}

} // namespace mg::scripting
