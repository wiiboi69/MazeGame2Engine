cmake_minimum_required(VERSION 3.16)
project(MazeGame CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Platform selection: the PS4 toolchain (orbis-cmake) defines PS4, like in ScratchEverywhere's top-level CMakeLists.
# Everything else is a desktop build.
if(PS4 OR MG_PLATFORM STREQUAL "ps4")
    set(MG_IS_PS4 TRUE)
endif()
if(VITA OR MG_PLATFORM STREQUAL "vita")   # the VitaSDK toolchain file defines VITA
    set(MG_IS_VITA TRUE)
endif()
if(PSP OR MG_PLATFORM STREQUAL "psp")   # psp-cmake's toolchain file defines PSP
    set(MG_IS_PSP TRUE)
endif()

# core/ has no platform dependencies at all (see PORTING notes in README.md).
file(GLOB CORE_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/core/*.cpp)
add_library(mg_core STATIC ${CORE_SOURCES})
target_include_directories(mg_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
if(MSVC)
    target_compile_options(mg_core PRIVATE /W3 /utf-8)
else()
    target_compile_options(mg_core PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()
if(MG_IS_PS4)
    target_compile_definitions(mg_core PUBLIC __PS4__=1 MG_NO_STD_FILESYSTEM=1)
elseif(MG_IS_PSP OR MG_IS_VITA)
    target_compile_definitions(mg_core PUBLIC MG_NO_STD_FILESYSTEM=1)   # __PSP__ / __vita__ come from the compiler itself
else()
    # headless smoke test: loads every level, runs the simulation, runs the script VM
    add_executable(core_smoke tests/core_smoke.cpp)
    target_link_libraries(core_smoke PRIVATE mg_core)
endif()

# ---------------------------------------------------------------- the game (SDL2 backend)
file(GLOB RUNTIME_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/runtime/*.cpp)
file(GLOB GAME_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/game/*.cpp)
add_executable(maze_game ${RUNTIME_SOURCES} ${GAME_SOURCES})
target_include_directories(maze_game PRIVATE ${CMAKE_CURRENT_SOURCE_DIR} ${CMAKE_CURRENT_SOURCE_DIR}/runtime)
target_link_libraries(maze_game PRIVATE mg_core)

if(MG_IS_PS4)
    include(${CMAKE_CURRENT_SOURCE_DIR}/cmake/ps4.cmake)
elseif(MG_IS_PSP)
    include(${CMAKE_CURRENT_SOURCE_DIR}/cmake/psp.cmake)
elseif(MG_IS_VITA)
    include(${CMAKE_CURRENT_SOURCE_DIR}/cmake/vita.cmake)
else()
    include(FetchContent)
    set(SDL_SHARED OFF CACHE BOOL "" FORCE)
    set(SDL_STATIC ON CACHE BOOL "" FORCE)
    set(SDL_TEST OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(SDL2 URL https://github.com/libsdl-org/SDL/releases/download/release-2.30.9/SDL2-2.30.9.zip)
    FetchContent_MakeAvailable(SDL2)
    target_link_libraries(maze_game PRIVATE SDL2::SDL2-static)
    if(MSVC)
        target_compile_options(maze_game PRIVATE /W3 /utf-8 /wd4244 /wd4267 /wd4996)
        set_source_files_properties(runtime/StbVorbis.cpp PROPERTIES COMPILE_FLAGS "/w")
    endif()
endif()
