#include "GameContent.hpp"

namespace mg {

namespace {

bool g_done = false;

void solid(const std::string& id, const std::string& name, const std::string& cat, const std::string& tex) {
    auto* d = new TileDef(id, name, "#", "", "", cat);
    d->texture = "custom/" + tex + ".png";
    TileRegistry::registerTile(d);
}

void platform(const std::string& id, const std::string& name, const std::string& cat, const std::string& tex) {
    auto* d = new TileDef(id, name, "=", "", "", cat);
    d->texture = "custom/" + tex + ".png";
    TileRegistry::registerTile(d);
}

void decor(const std::string& id, const std::string& name, const std::string& cat, const std::string& tex, bool deadly = false) {
    auto* d = new TileDef(id, name, "", "", "", cat);
    d->texture = "custom/" + tex + ".png";
    d->deadly = deadly;
    TileRegistry::registerTile(d);
}

void gem(const std::string& id, const std::string& name, const std::string& cat, const std::string& prefix, int value) {
    auto* d = new TileDef(id, name, "", "", "", cat);
    d->frames = std::vector<std::string>{"custom/" + prefix + "_1.png", "custom/" + prefix + "_2.png"};
    d->fps = 3;
    d->gemValue = value;
    TileRegistry::registerTile(d);
}

void registerTiles() {
    // Shape: "" walk through, "#" solid, "=" one-way platform, "L" ladder
    auto* lava = new TileDef("lava", "Lava", "", "", "", "Hazards");
    lava->frames = std::vector<std::string>{"custom/lava_1.png", "custom/lava_2.png", "custom/lava_3.png"};   // animated tile
    lava->fps = 5;
    lava->deadly = true;                       // kills the player on touch
    TileRegistry::registerTile(lava);

    auto* brick = new TileDef("stone_brick", "Stone brick", "#", "", "", "Custom blocks");
    brick->texture = "custom/stone_brick.png";
    TileRegistry::registerTile(brick);

    auto* ruby = new TileDef("gem_ruby", "Ruby", "", "", "", "Custom blocks");
    ruby->frames = std::vector<std::string>{"custom/ruby_1.png", "custom/ruby_2.png"};
    ruby->fps = 3;
    ruby->gemValue = 25;                       // collectible, worth 25 coins
    TileRegistry::registerTile(ruby);
}

void registerTilesets() {
    const std::string D = "Desert", S = "Snow & ice", C = "Castle";

    solid("desert_sand", "Sand", D, "desert_sand");
    solid("desert_sand_top", "Sand (top)", D, "desert_sand_top");
    solid("desert_brick", "Sandstone brick", D, "desert_brick");
    platform("desert_platform", "Sandstone platform", D, "desert_platform");
    decor("desert_pillar", "Pillar", D, "desert_pillar");
    decor("desert_cactus", "Cactus (hurts)", D, "desert_cactus", true);
    gem("desert_gem", "Amber", D, "desert_gem", 10);

    solid("snow_block", "Snow", S, "snow_block");
    solid("ice_block", "Ice", S, "ice_block");
    solid("snow_brick", "Snow brick", S, "snow_brick");
    platform("snow_platform", "Snow platform", S, "snow_platform");
    decor("snow_pine", "Snowy pine", S, "snow_pine");
    decor("snow_icicle", "Icicles (hurt)", S, "snow_icicle", true);
    gem("snow_gem", "Ice crystal", S, "snow_gem", 10);

    solid("castle_brick", "Castle brick", C, "castle_brick");
    solid("castle_dark", "Dark brick", C, "castle_dark");
    platform("castle_platform", "Castle platform", C, "castle_platform");
    auto* ladder = new TileDef("castle_ladder", "Castle ladder", "L", "", "", C);
    ladder->texture = "custom/castle_ladder.png";
    TileRegistry::registerTile(ladder);
    decor("castle_bars", "Iron bars", C, "castle_bars");
    decor("castle_spikes", "Spikes (hurt)", C, "castle_spikes", true);
    auto* torch = new TileDef("castle_torch", "Torch", "", "", "", C);
    torch->frames = std::vector<std::string>{"custom/castle_torch_1.png", "custom/castle_torch_2.png", "custom/castle_torch_3.png"};
    torch->fps = 6;
    TileRegistry::registerTile(torch);
    gem("castle_gem", "Amethyst", C, "castle_gem", 10);
}

// the overworld's own 2D tileset (art: tools/make_overworld_tiles.py, assets/overworld/)
void ow(const std::string& id, const std::string& name, const std::string& cat, bool walk = true, real speed = 1, int frames = 0) {
    auto* d = new OverworldTileDef(id, name);
    d->category = cat;
    d->walkable = walk;
    d->speed = speed;
    d->texture = "overworld/" + id + ".png";
    if (frames > 0) {
        std::vector<std::string> f;
        for (int i = 1; i <= frames; i++) f.push_back("overworld/" + id + "_" + std::to_string(i) + ".png");
        d->frames = f;
    }
    d->fps = 2;
    OverworldTileRegistry::registerTile(d);
}

void registerOverworldTiles() {
    ow("grass", "Grass", "Ground");
    ow("flowers", "Flowers", "Ground");
    ow("sand", "Sand", "Ground", true, 0.85);
    ow("snow", "Snow", "Ground", true, 0.85);
    ow("hills", "Hills", "Ground", true, 0.7);
    ow("path", "Dirt path", "Paths", true, 1.15);
    ow("road", "Stone road", "Paths", true, 1.25);
    ow("bridge", "Bridge", "Paths");
    ow("forest", "Forest", "Obstacles", false);
    ow("pine_forest", "Snowy forest", "Obstacles", false);
    ow("mountain", "Mountain", "Obstacles", false);
    ow("water", "Water", "Water", false, 1, 2);
    ow("deep_water", "Deep water", "Water", false, 1, 2);
    ow("lava", "Lava", "Water", false, 1, 2);
    ow("town", "Town", "Places");
    ow("castle", "Castle", "Places");
    ow("cave", "Cave", "Places");
}

void registerEntities() {
    auto* slime = new EntityType("slime", "Slime", [](std::shared_ptr<EntityDef> def, bool) -> std::unique_ptr<Entity> { return std::make_unique<Slime>(*def); });
    slime->texture = "custom/slime.png";
    slime->category = "Custom entities";
    slime->prop("direction", "Start direction (left / right)", "right");
    EntityRegistry::registerType(slime);

    auto* zone = new EntityType("zone", "Script zone", [](std::shared_ptr<EntityDef> def, bool editor) -> std::unique_ptr<Entity> { return std::make_unique<Zone>(*def, editor); });
    zone->texture = "custom/zone.png";
    zone->category = "Logic";
    zone->prop("id", "Zone id (used by 'on enter \"id\"')", "zone1", PropKind::Text, true)
        .prop("w", "Width in cells", "1", PropKind::Int)
        .prop("h", "Height in cells", "1", PropKind::Int);
    EntityRegistry::registerType(zone);

    auto* button = new EntityType("button", "Script button", [](std::shared_ptr<EntityDef> def, bool) -> std::unique_ptr<Entity> { return std::make_unique<ScriptButton>(*def); });
    button->texture = "custom/button.png";
    button->category = "Logic";
    button->prop("id", "Button id (used by 'on use \"id\"')", "button1", PropKind::Text, true);
    EntityRegistry::registerType(button);
}

} // namespace

void GameContent::registerAll() {
    if (g_done) return;
    g_done = true;
    registerTiles();
    registerTilesets();
    registerEntities();
    registerOverworldTiles();
}

// ---------------------------------------------------------------- Slime

Slime::Slime(const EntityDef& def) {
    kind = EntityKind::Custom;
    placeInCell(def.x, def.y, 13, 16);
    sizePct = 200;                                       // a 16x16 image drawn at 200% fills one cell
    std::string d = lower(def.get("direction", "right"));
    dir = (!d.empty() && d[0] == 'l') ? -90 : 90;
}

void Slime::update(World& w) {
    if (kind == EntityKind::Flip) { Entity::update(w); return; }   // bumped from below: let the engine tumble it away
    stepWalker(w);                                                 // gravity + walking + ledge hopping
    if (kind == EntityKind::Flip) return;
    if (w.mode == WorldMode::Playing && touchesPlayer(w, 0.8)) w.killPlayer();
}

// ---------------------------------------------------------------- Zone

Zone::Zone(const EntityDef& def, bool editor) {
    kind = EntityKind::Custom;
    id_ = def.get("id");
    int cw = std::max(1, def.getInt("w", 1)), ch = std::max(1, def.getInt("h", 1));
    hw_ = cw * 16; hh_ = ch * 16;
    width = hw_; height = hh_;
    x = def.x * 32 + hw_;
    y = def.y * 32 + hh_;
    sizePct = 200;
    visible = editor;
}

void Zone::update(World& w) {
    if (w.mode != WorldMode::Playing) return;
    bool now = touchesPlayer(w, 1.0);
    if (now && !inside_) w.fireEvent("enter", id_);
    if (!now && inside_) w.fireEvent("exit", id_);
    if (now && w.input.usePressed) w.fireEvent("use", id_);
    inside_ = now;
}

// ---------------------------------------------------------------- ScriptButton

ScriptButton::ScriptButton(const EntityDef& def) {
    kind = EntityKind::Custom;
    id_ = def.get("id");
    placeInCell(def.x, def.y, 12, 12);
    sizePct = 200;
}

void ScriptButton::update(World& w) {
    if (w.mode == WorldMode::Playing && touchesPlayer(w, 1.0) && w.input.usePressed) {
        w.playSound("pop");
        w.fireEvent("use", id_);
    }
}

} // namespace mg
