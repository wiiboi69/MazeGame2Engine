#include "Player.hpp"
#include "World.hpp"

namespace mg {

static bool has(const std::string& s, const char* sub) { return s.find(sub) != std::string::npos; }
static bool hasCh(const std::string& s, char c) { return s.find(c) != std::string::npos; }

bool Player::swim() const { return w_->level->underwater(); }
const InputState& Player::in() const { return w_->input; }

void Player::reset(real px, real py) {
    x = px; y = py;
    width = 8; height = 18;
    action = ""; forcedAction = ""; allForced = 0;
    speedX = speedY = 0; playerSpeedX = playerSpeedY = 0;
    jumping = 99; falling = 99;
    dir = 90; frame = 0; timer1 = 0; bopY.reset();
    visible = true; rotation = RotationStyle::LeftRight; rotationDegrees = 0;
    paint();
}

void Player::getTile(real px, real py) {
    w_->tileAt(px, py, tile_, tileIndex_);
    tileShape_ = TileInfo::shape(tile_);
}

void Player::movePlayer() {
    w_->bumpIndex = 0;
    if (w_->godMode) {
        handleGodMode();
    } else {
        if (w_->level->width * 32 + 16 < x) x = w_->level->width * 32 + 16 - 20;
        if (falling > 2 && speedY < -1) bopY = y; else bopY.reset();

        if (swim() || action != "slide") {
            handleKeysLeftRight();
            handleKeysJumpCrouch();
        } else {
            handleKeysJumpCrouchSlide();
            handleKeysLeftRightSlide();
        }
        moveSpriteX();
        moveSpriteY();
        checkAroundPlayer();
        handleLevelTransition();
    }
    paint();
}

void Player::afterEnemy() {
    if (w_->bouncePlayer > 0) {
        w_->bouncePlayer--;
        speedY = 13;
        falling = 2;
        jumping = 1;
    }
    if (std::abs(playerSpeedY) > 0) { speedY += playerSpeedY; playerSpeedY = 0; }
    if (std::abs(playerSpeedX) > 0) { speedX += playerSpeedX; playerSpeedX = 0; }
    w_->moveCamera();
    paint();
}

void Player::handleGodMode() {
    action = "";
    speedX += 7 * in().xAxis();
    speedY += 7 * in().yAxis();
    speedX *= 0.7;
    speedY *= 0.7;
    x += speedX;
    y += speedY;
}

// ================================================================ collision

void Player::fixCollisionAtPoint(real px, real py, bool feet) {
    getTile(px, py);
    if (hasCh(tileShape_, 'L') || (hasCh(tileShape_, 'R') && (in().down || has(action, "ladder"))))
        return;
    if (tileShape_.empty()) return;

    modX_ = fmodPos(px, 32);
    modY_ = fmodPos(py, 32);
    char t = tileShape_[0];
    temp_ = t;

    if (t == '\\' || t == '/') {
        real m = 1.0 / (tileShape_[1] - '0');
        real c = (tileShape_[2] - '0') * (32 * m);
        if (t == '\\') { m = -m; c = 32 - c; }
        offsetY_ = modY_ - (modX_ * m + c);
        if (action == "crouch") {
            action = "slide";
            speedX = (dir / 90) * 12;
            speedY = -2;
        }
        if (!(offsetY_ < 0)) return;
        if (fixDy_ < 0) {
            y += -offsetY_;
            solid_ = 10;
            return;
        }
        if (fixDy_ == 0 && ((fixDx_ > 0) == (m > 0))) {
            x += offsetY_ / m;
            solid_ = 10;
            return;
        }
    }

    if (tileShape_ == "=") {
        if (!feet || (modY_ - fixDy_) < 32) return;
        if (in().down) return;
    }

    solid_ = 10;
    if (fixDy_ < 0) { allForced = 0; y += 32 - modY_; }
    if (fixDx_ < 0) x += 32 - modX_;
    if (fixDy_ > 0) { allForced = 0; y += T - modY_; }
    if (fixDx_ > 0) x += T - modX_;
}

void Player::fixCollisionInDirection(real dx, real dy) {
    fixDx_ = dx;
    fixDy_ = dy;
    solid_ = 0;
    for (int pass = 0; pass < 2; pass++) {
        fixCollisionAtPoint(x - width, y - height, true);
        fixCollisionAtPoint(x + (width + T), y - height, true);
        fixCollisionAtPoint(x - width, y, false);
        fixCollisionAtPoint(x + (width + T), y, false);
        if (!(action == "crouch" || action == "aircrouch")) {
            fixCollisionAtPoint(x - width, y + height, false);
            fixCollisionAtPoint(x + (width + T), y + height, false);
        }
        if (solid_ < 1) return;
    }
}

void Player::moveSpriteX() {
    real origY = y;
    x += speedX;
    fixCollisionInDirection(0, -1);
    if (y > origY + (std::abs(speedX) + 4)) {
        y = origY;
        fixCollisionInDirection(speedX, 0);
        if (solid_ > 0) speedX = 0;
    } else if (y > origY) {
        speedX *= 0.85;
    }
}

void Player::moveSpriteY() {
    y += speedY;
    falling++;
    fixCollisionInDirection(0, speedY);
    if (solid_ > 0) {
        if (allForced == 0) forcedAction = "";
        if (speedY < 0) {
            falling = 0;
        } else {
            jumping = 99;
            bumpHead();
        }
        speedY = 0;
    }
}

// ================================================================ keys

void Player::makeSkidSmoke() {
    frame += 1;
    if (fmodPos(frame, 3) < 1) {
        w_->addParticle(ParticleKind::Smoke, x, y - 20);
        w_->playSound("shaker");
    }
}

void Player::handleGetUp() {
    action = "";
    real origY = y;
    fixCollisionInDirection(0, 1);
    if (solid_ > 0) {
        action = "crouch";
        y = origY;
    }
}

void Player::moveWalk() {
    getTile(x, y);   // ladder_check
    if (keyWalk == 0) {
        if (falling < 2) {
            if (speedX > .8) speedX += -1;
            else if (speedX < -.8) speedX += 1;
            else { speedX = 0; frame = 0; }
        }
    } else {
        dir = 90 * keyWalk;
        if (keyWalk * speedX < 10) {
            if (keyWalk * speedX < 0) {
                speedX += keyWalk * .8;
                if (falling < 2 && !swim()) {
                    action = "turn";
                    makeSkidSmoke();
                }
            } else {
                speedX += swim() ? keyWalk * 1.2 : keyWalk * .8;
            }
        }
    }
    real temp = std::abs(speedX) / (swim() ? 30 : 19);
    temp_ = 0;
    if (!has(action, "ladder")) {
        if (temp < 0.2) temp = 0.2;
        frame += temp;
    }
}

void Player::moveCrouch() {
    if (keyWalk == 0) {
        if (falling < 2) {
            if (speedX > .8) speedX += -1;
            else if (speedX < -.8) speedX += 1;
            else { speedX = 0; frame = 0; }
        }
    } else {
        dir = 90 * keyWalk;
        if (keyWalk * speedX < 10) {
            if (keyWalk * speedX < 0) {
                speedX += keyWalk * .8;
                if (falling < 2) {
                    action = "turn";
                    makeSkidSmoke();
                }
            } else {
                speedX += keyWalk * 1.2;
            }
        }
    }
    real temp = std::abs(speedX) / 19;
    temp_ = 0;
    if (temp < 0.2) temp = 0.2;
    frame += temp;
}

void Player::handleKeysLeftRight() {
    if (action == "crouch") {
        if (std::abs(speedX) > 0.4) {
            speedX += -.8 * (std::abs(speedX) / speedX);
            if (!swim()) makeSkidSmoke();
        } else {
            speedX = 0;
            frame = 0;
            keyWalk = in().xAxis();
            moveCrouch();
        }
    } else {
        if (swim()) {
            if (falling < 1) action = "swim_walk";
        } else {
            action = "walk";
        }
        keyWalk = in().xAxis();
        moveWalk();
    }
}

void Player::handleKeysLeftRightSlide() {
    if (std::abs(speedX) > 0.4) {
        makeSkidSmoke();
    } else {
        frame = 0;
        keyWalk = in().xAxis();
        if (speedX > 11) speedX = 11;
        else if (speedX < -11) speedX = -11;
        else frame = 0;
    }
    getTile(x, y - 8);
    char first = !tileShape_.empty() ? tileShape_[0] : 0;
    if (first == '\\') speedX += 3.5;
    if (first == '/') speedX += -3.5;
    if (speedX > 11) speedX = 11;
    if (speedX < -11) speedX = -11; else frame = 0;
    if (speedX > 6) dir = 90;
    if (speedX < -6) dir = -90;
}

void Player::handleKeysJumpCrouch() {
    getTile(x, y - 32);
    if (tileShape_ == "#" && falling == 1) {
        rotation = RotationStyle::LeftRight;
        if (in().down && allForced == 0) {
            forcedAction = "";
        } else if (!swim()) {
            action = "walk";
            if (allForced == 0) forcedAction = "walk";
        }
    } else if (allForced == 0) {
        forcedAction = "";
    }

    if (swim()) {
        waterMovement();
        return;
    }

    getTile(x, y - 16);
    if ((action == "ladder" || tileShape_ == "L" || tileShape_ == "R") &&
        (!tileShape_.empty() || action != "crouch"))
        ladderMovement();
    else
        jumpCrouch();
}

void Player::handleKeysJumpCrouchSlide() {
    getTile(x, y - 8);
    if (temp_ == '#') {
        if ((timer1 > 0 && timer1 < 12) && in().up) {
            if (dir > speedX) { speedY = 18; speedX = 18; }
            else if (dir < speedX) { speedY = 18; speedX = -18; }
            timer1 = 0;
            forcedAction = "aircrouch";
            action = "aircrouch";
            return;
        }
        timer1++;
        if (timer1 > 11) {
            timer1 = 0;
            action = "crouch";
            handleKeysJumpCrouch();
            return;
        }
    } else {
        timer1 = 0;
    }
    speedY += -2;
    if (speedY < -22) speedY = -22;
    if (in().down) {
        jumping = 0;
    } else if (action == "crouch") {
        handleGetUp();
    } else if (in().up) {
        action = "walk";
        if (falling < 2 || jumping > 0) {
            jumping += 2;
            if (jumping < 11) {
                speedY = 13;
                if (jumping == 1) w_->playSound("jump");
            }
        }
    } else {
        jumping = 0;
    }
}

void Player::jumpCrouch() {
    speedY += -2;
    if (speedY < -22) speedY = -22;
    if (!(jumping > 0) && in().down) {
        if (falling > 0) action = "aircrouch";
        if (falling < 2) action = "crouch";
        if (in().up) {
            forcedAction = "aircrouch";
            allForced = 1;
            if (falling < 2 || jumping > 0) {
                jumping += 2;
                if (jumping < 11) {
                    speedY = 30;       // crouch + jump = super jump
                    if (jumping == 1) w_->playSound("jump");
                }
            }
        } else {
            forcedAction = "aircrouch";
            jumping = 0;
        }
    } else {
        if (action == "crouch") {
            handleGetUp();
        } else if (in().up) {
            if (falling < 2 || jumping > 0) {
                jumping += 2;
                if (jumping < 11) {
                    speedY = 13;
                    if (jumping == 1) w_->playSound("jump");
                }
            }
        } else {
            jumping = 0;
        }
    }
}

void Player::snapTo(bool useX, bool useY) {
    if (useX) x = std::floor(x / 32) * 32 + 16;
    if (useY) y = std::floor(y / 32) * 32 + 16;
}

void Player::ladderMovement() {
    if (!(action == "aircrouch" || forcedAction == "aircrouch")) {
        if (in().yAxis() < 0) {
            if (in().xAxis() == 0 && action != "crouch") {
                snapTo(true, false);
                speedX = 0;
            }
            frame += 0.5;
            if (!(action == "lose life" || action == "wark" || (action == "crouch" && tileShape_ != "R"))) {
                forcedAction = "";
                action = "ladder-down";
            }
            jumping = 0;
            speedY = -6;
            return;
        }
        if (in().yAxis() > 0) {
            if (in().xAxis() == 0 && action != "crouch") {
                snapTo(true, false);
                speedX = 0;
            }
            frame += 0.5;
            if (!(action == "lose life" || action == "wark" || action == "aircrouch" || forcedAction == "aircrouch")) {
                forcedAction = "";
                action = "ladder-up";
            }
            jumping = 0;
            speedY = 6;
            return;
        }
    }
    if (!(action == "lose life" || action == "wark" || (action == "crouch" && tileShape_ != "R")))
        action = "ladder";
    if (!(action == "aircrouch" || forcedAction == "aircrouch")) {
        speedY = 0;
        jumping = 0;
        falling = 0;
    }
}

void Player::waterMovement() {
    speedY += -1.5;
    if (speedY < -5) speedY = -5;
    if (in().up) {
        action = "swim";
        if (jumping == 0) {
            jumping += 2;
            if (jumping < 11) speedY = 15;
        }
    } else {
        jumping = 0;
    }
}

// ================================================================ environment

void Player::checkAroundPlayer() {
    if (y < 0 && w_->level->cameraMode != 7)
        w_->killPlayer();
    collectAt(x, y - 8);
    collectAt(x, y + 8);
}

void Player::collectAt(real px, real py) {
    getTile(px, py);
    if (TileInfo::isDeadly(tile_)) w_->killPlayer();
    if (TileInfo::isGem(tile_) && tileIndex_ >= 0) {
        w_->tiles[tileIndex_] = TileInfo::Air;
        w_->coins += TileInfo::gemValue(tile_);
        w_->fireEvent("gem");
        w_->playSound("coin");
    }
}

void Player::handleLevelTransition() {
    if (x > -100 && x < 10 && w_->hasLevel(w_->level->left)) {
        w_->requestConnect(*w_->level->left, -1);
    } else if (x > 32 * w_->level->width && x < 32 * w_->level->width + 100 && w_->hasLevel(w_->level->right)) {
        w_->requestConnect(*w_->level->right, +1);
    }
}

// ================================================================ pipe + death + completion helpers

void Player::pipeSettleTick() {
    handleKeysJumpCrouch();
    moveSpriteX();
    moveSpriteY();
    paint();
}

void Player::pipeSinkTick() {
    y += -3;
    frame += 0.6;
    paint();
}

void Player::pipeBegin(real offsetX) {
    snapTo(true, false);
    x += offsetX;
}

void Player::completeTick() {
    speedY += -1;
    speedX = 0;
    moveSpriteX();
    moveSpriteY();
    paint();
}

void Player::beginDeath() {
    lastDir = dir;
    action = "lose life";
    speedY = 18.23;
    rotation = RotationStyle::AllAround;
    dir = 90;
    costumeIndex = lastDir > 0 ? CosLoseLife : CosLoseLife2;
    rotationDegrees = 0;
}

bool Player::deathTick() {
    rotation = RotationStyle::AllAround;
    if (lastDir > 0) { x += -2; dir -= 8; }
    else { x += 2; dir += 8; }
    rotationDegrees = dir - 90;
    speedY += -1;
    y += speedY;
    costumeIndex = lastDir > 0 ? CosLoseLife : CosLoseLife2;
    return !(y - w_->camY < -220);
}

bool Player::deathCaught() {
    getTile(x, y);
    return TileInfo::catchesDeath(tile_);
}

void Player::recover() {
    rotation = RotationStyle::LeftRight;
    rotationDegrees = 0;
    dir = 90;
    action = "";
    speedX = speedY = 0;
    falling = 99;
    jumping = 99;
}

// ================================================================ costume selection

void Player::paint() {
    if (!forcedAction.empty()) { forcedPlayerAct(); return; }
    playerAct();
}

void Player::forcedPlayerAct() {
    if (forcedAction == "slide") { costumeIndex = CosSlide6; return; }
    if (forcedAction == "aircrouch") { costumeIndex = CosSlide4; return; }
    if (forcedAction == "walk") {
        costumeIndex = CosWalk1;
        if (std::abs(in().xAxis()) > 0 || std::abs(in().yAxis()) > 0)
            costumeIndex = 1 + (int)std::fmod(std::floor(frame * .25), 4);
        return;
    }
    costumeIndex = 1 + (int)std::fmod(std::floor(frame), 8);
}

void Player::playerAct() {
    if (!swim()) {
        if (!(std::abs(speedX) > 4) && has(action, "ladder")) {
            rotation = RotationStyle::DontRotate;
            if (dir == 90) costumeIndex = CosLadderUp2;
            else if (dir == -90) costumeIndex = CosLadderUp1;
            return;
        }
        if (action != "lose life") rotation = RotationStyle::LeftRight;
        if (action == "slide") { costumeIndex = CosSlide6; return; }
        if (action == "") { costumeIndex = CosWalk1; return; }
        if (action == "crouch") { costumeIndex = std::abs(speedX) > 4 ? CosSlide6 : CosCrouch; return; }
        if (action == "aircrouch") { costumeIndex = CosAirCrouch; return; }
        if (action == "lose life") { costumeIndex = lastDir > 0 ? CosLoseLife : CosLoseLife2; return; }
        if (action == "turn") { costumeIndex = CosTurn; return; }
        if (action == "walk" || has(action, "ladder")) {
            costumeIndex = CosWalk1;
            if (std::abs(in().xAxis()) > 0 || std::abs(in().yAxis()) > 0)
                costumeIndex = 1 + (int)std::fmod(std::floor(frame * .25), 4);
        } else {
            costumeIndex = 1 + (int)std::fmod(std::floor(frame), 4);
        }
    } else {
        if (action != "lose life") rotation = RotationStyle::LeftRight;
        if (action == "" || action == "swim") { costumeIndex = CosSwim4; return; }
        if (action == "lose life") { costumeIndex = lastDir > 0 ? CosLoseLife : CosLoseLife2; return; }
        if (action == "swim_walk" || has(action, "ladder")) {
            costumeIndex = CosSwim1 + (int)std::fmod(std::floor(frame), 5);
        } else {
            if (std::abs(in().xAxis()) > 0 || std::abs(in().yAxis()) > 0)
                costumeIndex = CosSwim1 + (int)std::fmod(std::floor(frame * .25), 5);
        }
    }
}

} // namespace mg
