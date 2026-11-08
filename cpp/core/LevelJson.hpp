#pragma once
#include "Common.hpp"
#include "LevelData.hpp"
#include <stdexcept>

namespace mg {

struct InvalidDataException : std::runtime_error {
    using std::runtime_error::runtime_error;
};

// The level file format (JSON). Tiles and entities are identified by string ids.
// See src/MazeGame.Core/LevelJson.cs for the format description; the layout is identical.
struct LevelJson {
    static constexpr int FormatVersion = 3;

    static std::string write(const LevelData& l);
    // Parses a level file. Throws InvalidDataException if it is not a valid level.
    static LevelData parse(const std::string& json);
    // Reads just the "id" of a level file without building the level.
    static std::optional<std::string> peekId(const std::string& json);
};

// Reads (import only) the original "mg2ex" level string.
struct LegacyLevelCodec {
    static std::optional<LevelData> decode(const std::string& text);
    static std::optional<int> decodeSettings(const std::string& text);
};

} // namespace mg
