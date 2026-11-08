#pragma once
#include "Common.hpp"
namespace mg {

class World;

enum class RotationStyle { LeftRight, AllAround, DontRotate };

// Faithful port of the "player" sprite: movement, jumping, crouch/slide, ladders, swimming and
// tile collision (including 1:1, 1:2 slopes and one-way platforms). Variable names follow the
// original Scratch variables. Copyable on purpose: a copy is the level-state snapshot.
class Player {
public:
    static constexpr int CosWalk1 = 1, CosTurn = 5, CosCrouch = 7, CosAirCrouch = 8, CosLoseLife = 9, CosLoseLife2 = 10,
        CosSlide4 = 14, CosSlide6 = 16, CosLadderUp1 = 18, CosLadderUp2 = 19, CosSwim1 = 20, CosSwim4 = 23;

    real x = 0, y = 0;
    real speedX = 0, speedY = 0;
    real width = 8, height = 18;      // half extents of the hit box
    int falling = 99, jumping = 99;
    std::string action;               // '', walk, crouch, aircrouch, slide, turn, ladder*, swim, swim_walk, lose life
    std::string forcedAction;         // player_action_forced
    int allForced = 0;                // 'player all forsed'
    real dir = 90;
    real frame = 0;
    int keyWalk = 0;
    std::optional<real> bopY;         // set while falling fast: enables stomping
    int timer1 = 0;                   // slide timer
    real playerSpeedX = 0, playerSpeedY = 0;   // external pushes applied after enemies
    real lastDir = 0;
    bool visible = true;

    // outputs for the renderer
    int costumeIndex = 1;
    RotationStyle rotation = RotationStyle::LeftRight;
    real rotationDegrees = 0;
    bool flipX() const { return rotation == RotationStyle::LeftRight && dir < 0; }

    explicit Player(World* w) : w_(w) {}
    void setWorld(World* w) { w_ = w; }

    void reset(real px, real py);
    void getTile(real px, real py);
    void movePlayer();
    void afterEnemy();
    void paint();

    void pipeSettleTick();
    void pipeSinkTick();
    void pipeBegin(real offsetX);
    void completeTick();
    void beginDeath();
    bool deathTick();
    bool deathCaught();
    void recover();

private:
    static constexpr real T = GameConstants::Tiny;
    World* w_;

    int solid_ = 0;
    real fixDx_ = 0, fixDy_ = 0, modX_ = 0, modY_ = 0, offsetY_ = 0;
    char temp_ = 0;               // first letter of the last tile shape that was hit ($temp)
    int tile_ = 0;
    std::string tileShape_;
    int tileIndex_ = -1;

    bool swim() const;
    const InputState& in() const;

    void handleGodMode();
    void fixCollisionAtPoint(real px, real py, bool feet);
    void fixCollisionInDirection(real dx, real dy);
    void moveSpriteX();
    void moveSpriteY();
    void bumpHead() {}
    void makeSkidSmoke();
    void handleGetUp();
    void moveWalk();
    void moveCrouch();
    void handleKeysLeftRight();
    void handleKeysLeftRightSlide();
    void handleKeysJumpCrouch();
    void handleKeysJumpCrouchSlide();
    void jumpCrouch();
    void snapTo(bool useX, bool useY);
    void ladderMovement();
    void waterMovement();
    void checkAroundPlayer();
    void collectAt(real px, real py);
    void handleLevelTransition();
    void forcedPlayerAct();
    void playerAct();
};

inline real fmodPos(real a, real b) {   // C# Mod(): floor-style modulo
    real r = std::fmod(a, b);
    return (r != 0 && (r < 0) != (b < 0)) ? r + b : r;
}

} // namespace mg
