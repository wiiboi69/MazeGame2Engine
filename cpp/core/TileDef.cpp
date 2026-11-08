#include "TileDef.hpp"
namespace mg {

void TileRegistry::ensureInit() {
    auto& st = s();
    if (st.init) return;
    st.init = true;
#include "BuiltinTiles.gen.inc"
}

void TileRegistry::add(TileDef* def, int num) {
    auto& st = s();
    def->num = num;
    if ((size_t)num >= st.byNumV.size()) st.byNumV.resize(std::max((size_t)num + 1, st.byNumV.size() * 2), nullptr);
    st.byNumV[num] = def;
    st.byId[def->id] = def;
    st.allTiles.push_back(def);
}

TileDef* TileRegistry::registerTile(TileDef* def) {
    ensureInit();
    auto& st = s();
    auto it = st.byId.find(def->id);
    if (it != st.byId.end()) {
        TileDef* existing = it->second;
        if (!existing->missing) { std::fprintf(stderr, "tile id '%s' is already registered\n", def->id.c_str()); std::abort(); }
        def->num = existing->num;
        *std::find(st.allTiles.begin(), st.allTiles.end(), existing) = def;
        st.byNumV[def->num] = def;
        st.byId[def->id] = def;
        return def;
    }
    add(def, st.nextCustom++);
    return def;
}

TileDef* TileRegistry::registerTile(const std::string& id, const std::string& name, const std::string& shape,
                                    const std::optional<std::string>& texture, const std::string& category, bool deadly) {
    auto* d = new TileDef(id, name, shape, "", "", category);
    d->texture = texture; d->deadly = deadly;
    return registerTile(d);
}

TileDef* TileRegistry::find(std::string id) {
    ensureInit();
    auto& st = s();
    auto a = st.aliases.find(id);
    if (a != st.aliases.end()) id = a->second;
    auto it = st.byId.find(id);
    return it == st.byId.end() ? nullptr : it->second;
}

TileDef* TileRegistry::resolve(const std::string& id) {
    if (auto* d = find(id)) return d;
    auto* d = new TileDef(id, id + " (missing)", "", "", "", "Missing");
    d->missing = true;
    add(d, s().nextCustom++);
    std::fprintf(stderr, "[tiles] unknown tile id '%s' - register it in GameContent\n", id.c_str());
    return d;
}

} // namespace mg
