#include "Fs.hpp"
#include <fstream>
#include <sstream>

// PS4 (and any toolchain without <filesystem>) uses plain POSIX calls for the few directory operations.
#if defined(__PS4__) || defined(__PSP__) || defined(__vita__) || defined(MG_NO_STD_FILESYSTEM)
#define MG_POSIX_FS 1
#include <dirent.h>
#include <sys/stat.h>
#include <unistd.h>
#else
#include <filesystem>
#endif

namespace mg::fs {

#ifndef MG_POSIX_FS
namespace stdfs = std::filesystem;
#endif

bool readFile(const std::string& path, std::string& out) {
    std::ifstream f(path, std::ios::binary);
    if (!f) return false;
    std::ostringstream ss;
    ss << f.rdbuf();
    out = ss.str();
    if (out.size() >= 3 && (uint8_t)out[0] == 0xEF && (uint8_t)out[1] == 0xBB && (uint8_t)out[2] == 0xBF) out.erase(0, 3);   // UTF-8 BOM
    return true;
}

bool readFileBytes(const std::string& path, std::vector<uint8_t>& out) {
    std::ifstream f(path, std::ios::binary);
    if (!f) return false;
    out.assign(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
    return true;
}

bool writeFile(const std::string& path, const std::string& data) {
    std::ofstream f(path, std::ios::binary | std::ios::trunc);
    if (!f) return false;
    f.write(data.data(), (std::streamsize)data.size());
    return (bool)f;
}

bool writeFileBytes(const std::string& path, const std::vector<uint8_t>& data) {
    std::ofstream f(path, std::ios::binary | std::ios::trunc);
    if (!f) return false;
    f.write((const char*)data.data(), (std::streamsize)data.size());
    return (bool)f;
}

#ifdef MG_POSIX_FS
bool fileExists(const std::string& path) { struct stat st; return stat(path.c_str(), &st) == 0 && S_ISREG(st.st_mode); }
bool dirExists(const std::string& path) { struct stat st; return stat(path.c_str(), &st) == 0 && S_ISDIR(st.st_mode); }
bool removeFile(const std::string& path) { return unlink(path.c_str()) == 0; }
void createDirectories(const std::string& path) {
    std::string cur;
    for (size_t i = 0; i <= path.size(); i++) {
        if (i == path.size() || path[i] == '/' || path[i] == '\\') {
            if (!cur.empty() && !dirExists(cur)) mkdir(cur.c_str(), 0777);
        }
        if (i < path.size()) cur += path[i];
    }
}

std::vector<std::string> listFiles(const std::string& dir, const std::string& ext) {
    std::vector<std::string> r;
    DIR* d = opendir(dir.c_str());
    if (!d) return r;
    while (dirent* e = readdir(d)) {
        std::string name = e->d_name;
        if (name.size() >= ext.size() && name.compare(name.size() - ext.size(), ext.size(), ext) == 0 && fileExists(join(dir, name)))
            r.push_back(name);
    }
    closedir(d);
    return r;
}
#else
bool fileExists(const std::string& path) { std::error_code ec; return stdfs::is_regular_file(path, ec); }
bool dirExists(const std::string& path) { std::error_code ec; return stdfs::is_directory(path, ec); }
bool removeFile(const std::string& path) { std::error_code ec; return stdfs::remove(path, ec); }
void createDirectories(const std::string& path) { std::error_code ec; stdfs::create_directories(path, ec); }

std::vector<std::string> listFiles(const std::string& dir, const std::string& ext) {
    std::vector<std::string> r;
    std::error_code ec;
    for (auto& e : stdfs::directory_iterator(dir, ec)) {
        if (!e.is_regular_file()) continue;
        if (e.path().extension().string() == ext) r.push_back(e.path().filename().string());
    }
    return r;
}
#endif

std::string join(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    char last = a.back();
    return (last == '/' || last == '\\') ? a + b : a + "/" + b;
}

std::string stem(const std::string& fileName) {
    auto dot = fileName.rfind('.');
    return dot == std::string::npos ? fileName : fileName.substr(0, dot);
}

} // namespace mg::fs
