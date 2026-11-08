#pragma once
#include "Platform.hpp"
#include <memory>

namespace mg::plat {
std::unique_ptr<Platform> createSDL2Platform();
}
