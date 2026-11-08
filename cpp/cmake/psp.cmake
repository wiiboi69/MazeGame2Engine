# PSP target. Same recipe as ScratchEverywhere's cmake/platforms/psp.cmake: SDL, controller only, EBOOT.PBP via
# create_pbp_file, assets bundled next to it. Build with the pspdev toolchain wrapper (see docker/Dockerfile.psp):
#   psp-cmake -S cpp -B build-psp -DCMAKE_BUILD_TYPE=Release

set(MG_APP_NAME "Maze Game 2")
set(MG_APP_VERSION "1.0")
set(MG_OUTPUT_NAME "maze-game-2-psp")

# SDL2 (pspdev ships SDL2 and SDL3; the backend in runtime/PlatformSDL2.cpp uses the SDL2 API)
find_package(SDL2 REQUIRED CONFIG)
target_link_libraries(maze_game PRIVATE
    SDL2::SDL2main SDL2::SDL2-static
    GL pspvram pspaudio pspvfpu pspdisplay pspgu pspge pspctrl psphprm psppower)
target_compile_options(maze_game PRIVATE -Wno-psabi)

# 144x82 icon, 480x272 background shown in the XMB
create_pbp_file(
    TARGET maze_game
    TITLE ${MG_APP_NAME}
    VERSION ${MG_APP_VERSION}
    ICON_PATH ${CMAKE_CURRENT_SOURCE_DIR}/platforms/psp/ICON0.PNG
    BACKGROUND_PATH ${CMAKE_CURRENT_SOURCE_DIR}/platforms/psp/PIC1.PNG
    PREVIEW_PATH NULL
)

# ---- bundle: <build>/bundle/MazeGame2/{EBOOT.PBP, assets, levels, worlds}, zipped
# Only the low-res art is shipped: *.svg.psp.png + psp_sizes.json (tools/BakeSvgs --psp), no SVGs, no 4x PNGs, no MP3s.
set(MG_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(MG_BUNDLE "${CMAKE_CURRENT_BINARY_DIR}/bundle/MazeGame2")
file(REMOVE_RECURSE "${CMAKE_CURRENT_BINARY_DIR}/bundle")
file(COPY "${MG_ROOT}/assets" DESTINATION "${MG_BUNDLE}"
     PATTERN "*.svg" EXCLUDE PATTERN "*.mp3" EXCLUDE PATTERN "*.wav.bak" EXCLUDE)
file(GLOB_RECURSE MG_HIRES "${MG_BUNDLE}/assets/*.svg.png")
foreach(f IN LISTS MG_HIRES)
    if(NOT f MATCHES "\.psp\.png$")
        file(REMOVE "${f}")
    endif()
endforeach()
file(COPY "${MG_ROOT}/levels" DESTINATION "${MG_BUNDLE}")
if(EXISTS "${MG_ROOT}/worlds")
    file(COPY "${MG_ROOT}/worlds" DESTINATION "${MG_BUNDLE}")
endif()

add_custom_target(maze_game_bundle ALL DEPENDS maze_game
    COMMAND ${CMAKE_COMMAND} -E copy "$<TARGET_FILE_DIR:maze_game>/EBOOT.PBP" "${MG_BUNDLE}/EBOOT.PBP"
    COMMAND ${CMAKE_COMMAND} -E chdir "${CMAKE_CURRENT_BINARY_DIR}/bundle"
            ${CMAKE_COMMAND} -E tar cf "${CMAKE_CURRENT_BINARY_DIR}/${MG_OUTPUT_NAME}.zip" --format=zip MazeGame2
    COMMENT "Bundling ${MG_OUTPUT_NAME}.zip")
