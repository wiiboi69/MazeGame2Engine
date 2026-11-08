#define STB_VORBIS_HEADER_ONLY
#include "../third_party/stb/stb_vorbis.c"
#undef STB_VORBIS_HEADER_ONLY
#include "Audio.hpp"
#include "Sprites.hpp"   // for rt::P()
#include "core/Fs.hpp"
#include <algorithm>
#include <cstring>

namespace mg::rt {

static const char* const Extensions[] = {".ogg", ".mp3", ".wav"};

std::string AudioEngine::findFile(const std::string& dir, const std::string& name) {
    std::string direct = fs::join(dir, name);
    for (const char* ext : Extensions) {
        std::string n = lower(name);
        std::string e = ext;
        if (n.size() > e.size() && n.compare(n.size() - e.size(), e.size(), e) == 0 && fs::fileExists(direct)) return direct;
    }
    // .ogg and .wav only: the mixer has no MP3 decoder (every shipped song has an .ogg twin)
    for (const char* ext : {".ogg", ".wav"})
        if (fs::fileExists(direct + ext)) return direct + ext;
    return "";
}

AudioEngine::AudioEngine(const std::string& audioDir) : soundDir(audioDir) {
    available = P().openAudio(44100, [this](int16_t* out, int frames) { mix(out, frames); });
    if (available) rate_ = P().audioSampleRate();
}

AudioEngine::~AudioEngine() {
    if (!available) return;
    P().lockAudio();
    if (music_) { stb_vorbis_close(music_); music_ = nullptr; }
    P().unlockAudio();
}

void AudioEngine::setMaster(float v) { master_ = std::clamp(v, 0.f, 1.f); }
void AudioEngine::setMusicVolume(float v) { musicVolume_ = std::clamp(v, 0.f, 1.f); }

// ---------------------------------------------------------------- sound effects

static bool parseWav(const std::vector<uint8_t>& d, std::vector<int16_t>& pcm, int& channels, int& rate) {
    auto u16 = [&](size_t o) { return (int)(d[o] | (d[o + 1] << 8)); };
    auto u32 = [&](size_t o) { return (uint32_t)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | ((uint32_t)d[o + 3] << 24)); };
    if (d.size() < 44 || std::memcmp(d.data(), "RIFF", 4) != 0 || std::memcmp(d.data() + 8, "WAVE", 4) != 0) return false;
    int fmt = 0, bits = 0;
    size_t p = 12;
    while (p + 8 <= d.size()) {
        uint32_t len = u32(p + 4);
        if (std::memcmp(d.data() + p, "fmt ", 4) == 0 && p + 8 + 16 <= d.size()) {
            fmt = u16(p + 8); channels = u16(p + 10); rate = (int)u32(p + 12); bits = u16(p + 22);
        } else if (std::memcmp(d.data() + p, "data", 4) == 0) {
            size_t n = std::min<size_t>(len, d.size() - p - 8);
            if (fmt != 1 || channels < 1 || channels > 2) return false;
            if (bits == 16) {
                pcm.resize(n / 2);
                std::memcpy(pcm.data(), d.data() + p + 8, pcm.size() * 2);
                return true;
            }
            if (bits == 8) {
                pcm.resize(n);
                for (size_t i = 0; i < n; i++) pcm[i] = (int16_t)((d[p + 8 + i] - 128) << 8);
                return true;
            }
            return false;
        }
        p += 8 + len + (len & 1);
    }
    return false;
}

const AudioEngine::Pcm* AudioEngine::loadSfx(const std::string& name) {
    auto it = sfx_.find(name);
    if (it != sfx_.end()) return it->second.get();
    std::unique_ptr<Pcm> out;
    std::string path = findFile(fs::join(soundDir, "sfx"), name);
    if (!path.empty()) {
        std::vector<int16_t> pcm;
        int channels = 0, rate = 0;
        bool ok = false;
        if (path.size() > 4 && lower(path.substr(path.size() - 4)) == ".wav") {
            std::vector<uint8_t> bytes;
            ok = fs::readFileBytes(path, bytes) && parseWav(bytes, pcm, channels, rate);
        } else {
            short* data = nullptr;
            int n = stb_vorbis_decode_filename(path.c_str(), &channels, &rate, &data);
            if (n > 0 && data) { pcm.assign(data, data + (size_t)n * channels); ok = true; }
            free(data);
        }
        if (ok) {
            size_t frames = pcm.size() / channels;
            // to stereo
            std::vector<int16_t> st(frames * 2);
            for (size_t i = 0; i < frames; i++) {
                st[i * 2] = pcm[i * channels];
                st[i * 2 + 1] = pcm[i * channels + (channels - 1)];
            }
            // resample (linear) to the device rate
            out = std::make_unique<Pcm>();
            if (rate == rate_ || rate <= 0) {
                out->stereo = std::move(st);
            } else {
                size_t outFrames = (size_t)((double)frames * rate_ / rate);
                out->stereo.resize(outFrames * 2);
                for (size_t i = 0; i < outFrames; i++) {
                    double src = (double)i * rate / rate_;
                    size_t i0 = (size_t)src, i1 = std::min(i0 + 1, frames - 1);
                    double f = src - i0;
                    for (int c = 0; c < 2; c++)
                        out->stereo[i * 2 + c] = (int16_t)(st[i0 * 2 + c] * (1 - f) + st[i1 * 2 + c] * f);
                }
            }
        } else {
            std::fprintf(stderr, "[audio] cannot load %s\n", path.c_str());
        }
    }
    const Pcm* raw = out.get();
    sfx_[name] = std::move(out);
    return raw;
}

void AudioEngine::playSfx(const std::string& name, float volume) {
    if (!available) return;
    P().lockAudio();
    const Pcm* pcm = loadSfx(name);
    if (pcm) {
        int idx = -1;
        for (int i = 0; i < NumVoices; i++) {
            int cand = (nextVoice_ + i) % NumVoices;
            if (!voices_[cand].pcm) { idx = cand; break; }
        }
        if (idx < 0) idx = nextVoice_;
        nextVoice_ = (idx + 1) % NumVoices;
        voices_[idx] = Voice{pcm, 0, std::clamp(volume, 0.f, 1.f)};
    }
    P().unlockAudio();
}

// ---------------------------------------------------------------- music

void AudioEngine::playMusic(const std::string& name, bool loop) {
    if (!available) return;
    std::string path = findFile(fs::join(soundDir, "music"), name);
    if (path.empty()) return;
    P().lockAudio();
    if (music_ && musicPath_ == path) { P().unlockAudio(); return; }
    if (music_) { stb_vorbis_close(music_); music_ = nullptr; }
    int err = 0;
    music_ = stb_vorbis_open_filename(path.c_str(), &err, nullptr);
    if (!music_) {
        std::fprintf(stderr, "[audio] cannot open music %s (%d)\n", path.c_str(), err);
    } else {
        stb_vorbis_info info = stb_vorbis_get_info(music_);
        musicChannels_ = std::min(info.channels, 2);
        musicRate_ = (int)info.sample_rate;
        musicPath_ = path;
        musicLoop_ = loop;
        musicPaused_ = false;
        mbuf_.assign(4096 * 2, 0);
        mbufFrames_ = mbufPos_ = 0;
        mfrac_ = 0;
    }
    P().unlockAudio();
}

void AudioEngine::stopMusic() {
    if (!available) return;
    P().lockAudio();
    if (music_) { stb_vorbis_close(music_); music_ = nullptr; }
    musicPath_.clear();
    P().unlockAudio();
}

void AudioEngine::pauseMusic(bool paused) {
    if (!available) return;
    P().lockAudio();
    musicPaused_ = paused;
    P().unlockAudio();
}

bool AudioEngine::refillMusic() {
    for (int attempt = 0; attempt < 2; attempt++) {
        int n = stb_vorbis_get_samples_short_interleaved(music_, musicChannels_, mbuf_.data(), 4096 * musicChannels_);
        if (n > 0) { mbufFrames_ = (size_t)n; mbufPos_ = 0; return true; }
        if (!musicLoop_) return false;
        stb_vorbis_seek_start(music_);
    }
    return false;
}

// ---------------------------------------------------------------- mixer (audio thread)

void AudioEngine::mix(int16_t* out, int frames) {
    static thread_local std::vector<int32_t> sum;
    sum.assign((size_t)frames * 2, 0);

    float sfxGain = sfxVolume * master_;
    for (auto& v : voices_) {
        if (!v.pcm) continue;
        size_t total = v.pcm->stereo.size() / 2;
        int n = (int)std::min<size_t>(frames, total - v.pos);
        float g = v.gain * sfxGain;
        for (int i = 0; i < n; i++) {
            sum[i * 2] += (int32_t)(v.pcm->stereo[(v.pos + i) * 2] * g);
            sum[i * 2 + 1] += (int32_t)(v.pcm->stereo[(v.pos + i) * 2 + 1] * g);
        }
        v.pos += n;
        if (v.pos >= total) v.pcm = nullptr;
    }

    if (music_ && !musicPaused_) {
        float g = musicVolume_ * master_;
        double step = (double)musicRate_ / rate_;
        for (int i = 0; i < frames; i++) {
            if (mbufPos_ >= mbufFrames_) {
                if (!refillMusic()) { stb_vorbis_close(music_); music_ = nullptr; musicPath_.clear(); break; }
            }
            int16_t l = mbuf_[mbufPos_ * musicChannels_];
            int16_t r = musicChannels_ == 2 ? mbuf_[mbufPos_ * 2 + 1] : l;
            sum[i * 2] += (int32_t)(l * g);
            sum[i * 2 + 1] += (int32_t)(r * g);
            mfrac_ += step;
            size_t adv = (size_t)mfrac_;
            mfrac_ -= adv;
            mbufPos_ += adv;
        }
    }

    for (int i = 0; i < frames * 2; i++) out[i] = (int16_t)std::clamp<int32_t>(sum[i], -32768, 32767);
}

} // namespace mg::rt
