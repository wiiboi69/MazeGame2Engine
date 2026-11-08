# PS4 target. Mirrors ScratchEverywhere's cmake/platforms/ps4.cmake: SDL2 windowing/audio, controller only, a PKG
# built with orbis_add_pkg from the pacbrew "ps4-homebrew-dev" SDK (see docker/Dockerfile.ps4).
#
# Configure with the SDK's wrapper:  $PS4_HOMEBREW_SDK/bin/orbis-cmake -B build-ps4 -DCMAKE_BUILD_TYPE=Release

set(MG_APP_NAME "Maze Game 2")
set(MG_APP_TITLEID "MAZE00001")          # 9 characters
set(MG_APP_VERSION "1.0")
set(MG_OUTPUT_NAME "maze-game-2-ps4")

# SDL2 from the SDK's package tree (pacbrew installs it into the target sysroot)
find_package(SDL2 CONFIG QUIET)
if(TARGET SDL2::SDL2)
    target_link_libraries(maze_game PRIVATE SDL2::SDL2)
else()
    find_package(PkgConfig REQUIRED)
    pkg_check_modules(SDL2 REQUIRED sdl2)
    target_include_directories(maze_game PRIVATE ${SDL2_INCLUDE_DIRS})
    target_link_libraries(maze_game PRIVATE ${SDL2_LIBRARIES})
endif()
# NOTE: no SceNetCtl here (ScratchEverywhere links it for its network features). This game is offline, and linking it
# makes the loader abort with "Unable to find libSceNetCtl.sprx" (seen in shadPS4's log).
target_compile_definitions(maze_game PRIVATE __PS4__=1)

# ---- romfs: only what the game reads at run time (baked PNGs instead of SVG, ogg instead of mp3)
set(MG_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(MG_ROMFS "${CMAKE_CURRENT_SOURCE_DIR}/romfs")
file(REMOVE_RECURSE "${MG_ROMFS}")
file(COPY "${MG_ROOT}/assets" DESTINATION "${MG_ROMFS}" PATTERN "*.svg" EXCLUDE PATTERN "*.mp3" EXCLUDE)
file(COPY "${MG_ROOT}/levels" DESTINATION "${MG_ROMFS}")
if(EXISTS "${MG_ROOT}/worlds")
    file(COPY "${MG_ROOT}/worlds" DESTINATION "${MG_ROMFS}")
endif()

# ---- sce_sys (icon0.png ...); orbis_add_pkg generates param.sfo from the arguments below
file(COPY "${CMAKE_CURRENT_SOURCE_DIR}/platforms/ps4/sce_sys/" DESTINATION "${CMAKE_CURRENT_SOURCE_DIR}/sce_sys")

string(REPLACE "." ";" MG_VERSION_LIST "${MG_APP_VERSION}")
list(GET MG_VERSION_LIST 0 MG_VERSION_MAJOR)
list(GET MG_VERSION_LIST 1 MG_VERSION_MINOR)
string(LENGTH "${MG_VERSION_MAJOR}" _l)
if(_l LESS 2)
    set(MG_VERSION_MAJOR "0${MG_VERSION_MAJOR}")
endif()
string(LENGTH "${MG_VERSION_MINOR}" _l)
if(_l LESS 2)
    set(MG_VERSION_MINOR "0${MG_VERSION_MINOR}")
endif()
set(MG_PKG_VERSION "${MG_VERSION_MAJOR}.${MG_VERSION_MINOR}")
set(MG_PKG_CONTENT_ID "IV0000-${MG_APP_TITLEID}_00-MAZEGAME2PS40000")

orbis_add_pkg(maze_game
    TITLE ${MG_APP_NAME}
    TITLE_ID ${MG_APP_TITLEID}
    CONTENT_ID ${MG_PKG_CONTENT_ID}
    VERSION ${MG_PKG_VERSION}
    ATTRIBUTE 16384
    ASSETS sce_sys romfs
    # the SDL2 renderer on PS4 goes through Mesa (same module list ScratchEverywhere ships)
    MODULES libEGL.so.1 libGLESv2.so.2 libvulkan.so.1 libgallium-26.2.0.so
)

add_custom_command(TARGET maze_game_pkg POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E rename
        "${CMAKE_CURRENT_BINARY_DIR}/${MG_PKG_CONTENT_ID}.pkg"
        "${CMAKE_CURRENT_BINARY_DIR}/${MG_OUTPUT_NAME}.pkg"
    COMMAND ${CMAKE_COMMAND} -E remove_directory "${CMAKE_CURRENT_SOURCE_DIR}/sce_sys"
    COMMENT "Renaming the PKG..."
)
