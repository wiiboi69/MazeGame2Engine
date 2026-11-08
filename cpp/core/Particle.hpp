#pragma once
#include "Common.hpp"
namespace mg {

enum class ParticleKind { Smoke, Coin, Score100 };

struct Particle {
    ParticleKind kind;
    real x, y;
    real frame = 1;
    real speedY = 0;
    real ghost = 0;
    bool dead = false;

    Particle(ParticleKind k, real px, real py) : kind(k), x(px), y(py) { if (k == ParticleKind::Coin) speedY = 12; }

    int costume() const {
        switch (kind) {
            case ParticleKind::Smoke: return std::clamp((int)std::floor(frame), 1, 3);
            case ParticleKind::Coin: return 4 + ((int)std::floor(frame) % 4);
            default: return 9;
        }
    }

    void tick() {
        switch (kind) {
            case ParticleKind::Smoke:
                if (!(frame < 4)) { dead = true; return; }
                frame += 0.4;
                break;
            case ParticleKind::Coin:
                speedY -= 1;
                if (speedY < -12) { dead = true; return; }
                y += speedY;
                frame += 0.5;
                break;
            case ParticleKind::Score100:
                y += 1;
                frame += 0.8;
                if (frame > 50) { ghost += 10; if (frame > 60) dead = true; }
                break;
        }
    }
};

} // namespace mg
