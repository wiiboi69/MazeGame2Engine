#if !defined(__PSP__)
#define SDL_MAIN_HANDLED   // the PSP needs SDL2main for its exit callback / module setup
#endif
#include "Game.hpp"
#include "core/Fs.hpp"
#include "runtime/PlatformSDL2.hpp"
#include <SDL.h>

#ifdef __vita__
// The Vita's newlib heap is tiny by default; sprites, music and the level cache need more.
extern "C" { unsigned int _newlib_heap_size_user = 192u * 1024u * 1024u; }
#endif

// Optional arguments:  maze_game [levels-directory]
// Script tools:        maze_game --compile script.mgs script.mgb     compile source to bytecode
//                      maze_game --disasm script.mgb|script.mgs      print the bytecode
int main(int argc, char** argv) {
    using namespace mg;
    if (argc >= 3 && std::string(argv[1]) == "--compile") {
        std::string text; fs::readFile(argv[2], text);
        try {
            auto chunk = scripting::compileScript(text, fs::stem(argv[2]));
            std::string out = argc > 3 ? argv[3] : fs::stem(argv[2]) + ".mgb";
            fs::writeFileBytes(out, chunk->write());
            std::printf("compiled %s\n", argv[2]);
        } catch (const std::exception& ex) { std::fprintf(stderr, "%s\n", ex.what()); return 1; }
        return 0;
    }
    if (argc >= 3 && std::string(argv[1]) == "--disasm") {
        std::string path = argv[2];
        try {
            std::shared_ptr<scripting::Chunk> chunk;
            if (path.size() > 4 && path.substr(path.size() - 4) == ".mgs") { std::string t; fs::readFile(path, t); chunk = scripting::compileScript(t, fs::stem(path)); }
            else { std::vector<uint8_t> b; fs::readFileBytes(path, b); chunk = scripting::Chunk::read(b); }
            std::printf("%s\n", chunk->disassemble().c_str());
        } catch (const std::exception& ex) { std::fprintf(stderr, "%s\n", ex.what()); return 1; }
        return 0;
    }

    GameContent::registerAll();   // your tiles and entities (core/GameContent.cpp)

#if defined(__PSP__)
    // PSP: everything lives in the game folder next to EBOOT.PBP (argv[0] is e.g. ms0:/PSP/GAME/MazeGame2/EBOOT.PBP).
    std::string self = argc > 0 ? argv[0] : "";
    size_t cut = self.find_last_of("/\\");
    std::string baseDir = cut == std::string::npos ? "ms0:/PSP/GAME/MazeGame2/" : self.substr(0, cut + 1);
    std::string assets = fs::join(baseDir, "assets");
    std::string levels = fs::join(baseDir, "levels");
    std::string settings = fs::join(baseDir, "settings.json");
    std::string dataDir = baseDir;
#elif defined(__vita__)
    // Vita: the VPK's files are under app0:, saves and settings go to ux0:data
    std::string assets = "app0:assets";
    std::string levels = "app0:levels";
    std::string dataDir = "ux0:data/MazeGame2";
    fs::createDirectories(dataDir);
    std::string settings = fs::join(dataDir, "settings.json");
#elif defined(__PS4__)
    // PS4: the PKG's romfs is mounted read-only under /app0; saves and settings go to the writable /data partition.
    // (Paths follow the usual PS4 homebrew convention - confirm them against your SDK / loader.)
    std::string assets = "/app0/romfs/assets";
    std::string levels = "/app0/romfs/levels";
    std::string dataDir = "/data/mazegame";
    fs::createDirectories(dataDir);
    std::string settings = fs::join(dataDir, "settings.json");
#else
    char* base = SDL_GetBasePath();
    std::string baseDir = base ? base : "./";
    SDL_free(base);
    std::string assets = fs::dirExists(fs::join(baseDir, "assets")) ? fs::join(baseDir, "assets") : "assets";
    std::string levels = argc > 1 ? argv[1] : (fs::dirExists(fs::join(baseDir, "levels")) ? fs::join(baseDir, "levels") : "levels");
    std::string settings = fs::join(baseDir, "settings.json");
    std::string dataDir;
#endif

    auto platform = plat::createSDL2Platform();
    Game game(*platform, assets, levels, settings, dataDir);
    game.run();
    return 0;
}
