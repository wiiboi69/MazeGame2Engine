// Headless smoke test for mg_core: loads all levels, simulates them, compiles + runs a script.
#include "core/GameContent.hpp"
#include "core/LevelLibrary.hpp"
#include "core/World.hpp"
#include <cstdio>

using namespace mg;

int main(int argc, char** argv) {
    std::string root = argc > 1 ? argv[1] : "..";
    bool trace = argc > 2 && std::string(argv[2]) == "--trace";
    GameContent::registerAll();
    scripting::ScriptLibrary::baseDir() = root + "/assets";

    auto lib = LevelLibrary::loadDirectory(root + "/levels");
    auto ids = lib->ids();
    if (!trace) std::printf("levels: %d, start: %s\n", (int)ids.size(), lib->startLevel().value_or("(none)").c_str());

    int fails = 0;
    for (auto& id : ids) {
        LevelData* lvl = lib->get(id);
        World w(lvl, id, [&](const std::string& s) { return lib->get(s); });
        int deaths = 0, sounds = 0;
        w.soundRequested = [&](const std::string&) { sounds++; };
        real maxX = w.player.x;
        for (int t = 0; t < 600; t++) {
            InputState in;
            in.right = true;
            in.up = (t % 40) < 12;
            in.usePressed = (t % 90) == 0;
            w.tick(in);
            maxX = std::max(maxX, w.player.x);
            if (trace && t % 20 == 0)
                std::printf("%s t=%d mode=%d x=%.6f y=%.6f sx=%.6f sy=%.6f coins=%d ents=%d cam=%.6f,%.6f\n", id.c_str(), t, (int)w.mode,
                            w.player.x, w.player.y, w.player.speedX, w.player.speedY, w.coins, (int)w.entities.size(), w.camX, w.camY);
            if (w.mode == WorldMode::Frozen && w.pending) {
                if (w.pending->level == id) { deaths++; w.load(lvl, id, LoadKind::Respawn, 0); }
                else break;
            }
        }
        if (!trace) std::printf("  %-14s %3dx%-3d ents=%-3d player=(%.1f, %.1f) maxX=%.0f coins=%d respawns=%d sounds=%d\n",
                    id.c_str(), lvl->width, lvl->height, (int)w.entities.size(), w.player.x, w.player.y, maxX, w.coins, deaths, sounds);
        if (std::isnan(w.player.x) || std::isnan(w.player.y)) fails++;

        // JSON round trip
        std::string a = LevelJson::write(*lvl);
        std::string b = LevelJson::write(LevelJson::parse(a));
        if (a != b) { std::printf("  !! JSON round trip differs for %s\n", id.c_str()); fails++; }

        // state save / restore
        auto snap = w.captureState();
        w.tick(InputState());
        w.restoreState(snap);
    }

    // script compile + run
    try {
        auto chunk = scripting::compileScript(
            "var n = 3;\nfunc add(a, b) { return a + b; }\non start { n = add(n, 4); print(\"n=\" + n); set_flag(\"x\", n * 2); }\n", "test");
        auto bytes = chunk->write();
        auto again = scripting::Chunk::read(bytes);
        std::printf("script ok, %d bytes bytecode\n%s", (int)bytes.size(), again->disassemble().c_str());
        LevelData lv = LevelData::createBlank(20, 10);
        lv.tiles[1 * 10 + 1] = TileInfo::PlayerSpawn;
        World w(&lv, "test");
        auto host = std::make_shared<scripting::ScriptHost>(&w, again);
        host->fire("start");
        host->flush();
        auto it = w.flags->find("x");
        std::printf("flag x = %s (expect 14)\n", it == w.flags->end() ? "(unset)" : it->second.c_str());
        if (it == w.flags->end() || it->second != "14") fails++;
    } catch (const std::exception& ex) {
        std::printf("script test failed: %s\n", ex.what());
        fails++;
    }

    std::printf(fails ? "FAILED (%d)\n" : "OK\n", fails);
    return fails ? 1 : 0;
}
