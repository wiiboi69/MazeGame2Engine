#pragma once
// Tiny file-system layer so core/ never depends on a platform API. Uses stdio + std::filesystem by default;
// a console port that lacks <filesystem> only has to replace Fs.cpp.
#include "Common.hpp"

namespace mg::fs {

bool readFile(const std::string& path, std::string& out);
bool readFileBytes(const std::string& path, std::vector<uint8_t>& out);
bool writeFile(const std::string& path, const std::string& data);
bool writeFileBytes(const std::string& path, const std::vector<uint8_t>& data);
bool fileExists(const std::string& path);
bool dirExists(const std::string& path);
bool removeFile(const std::string& path);
void createDirectories(const std::string& path);
// File names (not full paths) in dir with the given extension (including the dot, e.g. ".json").
std::vector<std::string> listFiles(const std::string& dir, const std::string& ext);

std::string join(const std::string& a, const std::string& b);
std::string stem(const std::string& fileName);   // "level_1.json" -> "level_1"

} // namespace mg::fs
