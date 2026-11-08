# PS Vita target (VitaSDK). SDL2 comes from the SDK (vdpm package "sdl2"); output is a .vpk you install with VitaShell.
# Build with the toolchain file (see docker/Dockerfile.vita):
#   cmake -S cpp -B build-vita -DCMAKE_TOOLCHAIN_FILE=$VITASDK/share/vita.toolchain.cmake -DMG_PLATFORM=vita

include("$ENV{VITASDK}/share/vita.cmake" REQUIRED)

set(MG_APP_NAME "Maze Game 2")
set(MG_APP_TITLEID "MZGM00001")     # 9 characters
set(MG_APP_VERSION "01.00")         # must be ##.##
set(MG_OUTPUT_NAME "maze-game-2-vita")

# SDL2: pkg-config knows its (long) list of Vita system stubs
find_package(PkgConfig REQUIRED)
set(ENV{PKG_CONFIG_PATH} "$ENV{VITASDK}/arm-vita-eabi/lib/pkgconfig")
pkg_check_modules(SDL2 REQUIRED sdl2)
target_include_directories(maze_game PRIVATE ${SDL2_INCLUDE_DIRS})
target_link_directories(maze_game PRIVATE ${SDL2_LIBRARY_DIRS})
# SceShaccCgExt + taihen: needed by the vitaGL / vitashark layer SDL2 renders through (not listed in sdl2.pc)
target_link_libraries(maze_game PRIVATE ${SDL2_LIBRARIES} SceShaccCgExt taihen_stub pthread)
target_compile_options(maze_game PRIVATE -Wno-psabi)

set(VITA_MKSFOEX_FLAGS "${VITA_MKSFOEX_FLAGS} -d PARENTAL_LEVEL=1")
vita_create_self(maze_game.self maze_game UNSAFE)

# ---- what goes into the VPK: only the high-res handheld art (*.svg.vita.png + vita_sizes.json), no SVGs / 4x PNGs / MP3s
set(MG_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(MG_STAGE "${CMAKE_CURRENT_BINARY_DIR}/stage")
file(REMOVE_RECURSE "${MG_STAGE}")
file(COPY "${MG_ROOT}/assets" DESTINATION "${MG_STAGE}" PATTERN "*.svg" EXCLUDE PATTERN "*.mp3" EXCLUDE)
file(GLOB_RECURSE MG_UNWANTED "${MG_STAGE}/assets/*.svg.png" "${MG_STAGE}/assets/*.svg.psp.png")
foreach(f IN LISTS MG_UNWANTED)
    if(NOT f MATCHES "\.vita\.png$")
        file(REMOVE "${f}")
    endif()
endforeach()
file(COPY "${MG_ROOT}/levels" DESTINATION "${MG_STAGE}")
if(EXISTS "${MG_ROOT}/worlds")
    file(COPY "${MG_ROOT}/worlds" DESTINATION "${MG_STAGE}")
endif()

# one "<file> <path inside the vpk>" pair per file
set(MG_VPK_FILES
    ${CMAKE_CURRENT_SOURCE_DIR}/platforms/vita/icon0.png sce_sys/icon0.png
    ${CMAKE_CURRENT_SOURCE_DIR}/platforms/vita/bg.png sce_sys/livearea/contents/bg.png
    ${CMAKE_CURRENT_SOURCE_DIR}/platforms/vita/startup.png sce_sys/livearea/contents/startup.png
    ${CMAKE_CURRENT_SOURCE_DIR}/platforms/vita/template.xml sce_sys/livearea/contents/template.xml)
file(GLOB_RECURSE MG_STAGED RELATIVE "${MG_STAGE}" "${MG_STAGE}/*")
foreach(rel IN LISTS MG_STAGED)
    list(APPEND MG_VPK_FILES "${MG_STAGE}/${rel}" "${rel}")
endforeach()

vita_create_vpk(${MG_OUTPUT_NAME}.vpk ${MG_APP_TITLEID} maze_game.self
    VERSION ${MG_APP_VERSION}
    NAME ${MG_APP_NAME}
    FILE ${MG_VPK_FILES}
)
