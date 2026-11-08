#pragma once
#include "Platform.hpp"
#include <map>
#include <memory>
#include <string>
#include <vector>

struct stb_vorbis;

namespace mg::rt {

// Software mixer on top of Platform::openAudio. Sound effects (.wav / .ogg) are decoded once into stereo PCM at the
// device rate and played on a pool of voices; music (.ogg) is streamed through stb_vorbis. Same API as the C# AudioEngine.
class AudioEngine {
public:
    explicit AudioEngine(const std::string& audioDir);
    ~AudioEngine();

    bool available = false;
    std::string soundDir;
    float sfxVolume = 0.8f;

    float master() const { return master_; }
    void setMaster(float v);
    float musicVolume() const { return musicVolume_; }
    void setMusicVolume(float v);

    void playSfx(const std::string& name, float volume = 1.f);
    void playMusic(const std::string& name, bool loop = true);
    bool isMusicPlaying() const { return music_ != nullptr && !musicPaused_; }
    void stopMusic();
    void pauseMusic(bool paused);
    void update() {}   // streaming happens in the mixer callback; kept for API parity

private:
    struct Pcm { std::vector<int16_t> stereo; };   // interleaved L R at the device rate
    struct Voice { const Pcm* pcm = nullptr; size_t pos = 0; float gain = 0; };
    static constexpr int NumVoices = 16;

    std::map<std::string, std::unique_ptr<Pcm>> sfx_;
    Voice voices_[NumVoices];
    int nextVoice_ = 0;
    int rate_ = 44100;
    float master_ = 1.f, musicVolume_ = 0.5f;

    stb_vorbis* music_ = nullptr;
    std::string musicPath_;
    bool musicLoop_ = true, musicPaused_ = false;
    int musicChannels_ = 2, musicRate_ = 44100;
    std::vector<int16_t> mbuf_;
    size_t mbufFrames_ = 0, mbufPos_ = 0;
    double mfrac_ = 0;

    const Pcm* loadSfx(const std::string& name);
    static std::string findFile(const std::string& dir, const std::string& name);
    void mix(int16_t* out, int frames);
    bool refillMusic();
};

} // namespace mg::rt
