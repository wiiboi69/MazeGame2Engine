#include "Game.hpp"
#include "core/Fs.hpp"
#include <cstdarg>
#include <chrono>

namespace mg {

using plat::Color;
namespace Key = plat::Key;
using rt::Pad;
using rt::PadAct;
using rt::SaveManager;
using rt::SaveSlot;
using rt::Screen;
using rt::SceneRenderer;

static const Color White(255, 255, 255);

static std::string sfmt(const char* f, ...) {
    char buf[512];
    va_list ap; va_start(ap, f);
    std::vsnprintf(buf, sizeof buf, f, ap);
    va_end(ap);
    return buf;
}

static const std::pair<const char*, double> TitleButtons[3] = {{"resome", 55}, {"setting", -90}, {"quit", -130}};
static const std::pair<const char*, double> PauseButtons[3] = {{"resome", 80}, {"setting", -20}, {"quit", -120}};

// ================================================================ construction

Game::Game(plat::Platform& platform, const std::string& assetsDir, const std::string& levelsDir, const std::string& settingsPath,
           const std::string& saveDir)
    : plat_(platform), settingsPath_(settingsPath) {
    settings_ = rt::Settings::load(settingsPath);
    win_ = std::make_unique<rt::AppWindow>(plat_, "Maze Game 2", settings_.fullscreen, settings_.widescreen, settings_.windowScale);
    GameConstants::setWidescreen(settings_.widescreen);
    sprites_ = std::make_unique<rt::SpriteLibrary>(assetsDir);
    scene_ = std::make_unique<rt::SceneRenderer>(*sprites_);
    ui_ = std::make_unique<rt::Ui>(fs::join(assetsDir, "fonts"));
    audio_ = std::make_unique<rt::AudioEngine>(fs::join(assetsDir, "audio"));
    // base dir = parent of the levels folder
    std::string lv = levelsDir;
    while (!lv.empty() && (lv.back() == '/' || lv.back() == '\\')) lv.pop_back();
    size_t cut = lv.find_last_of("/\\");
    std::string baseDir = cut == std::string::npos ? "." : lv.substr(0, cut);
    input_ = std::make_unique<rt::GameInput>(settings_);
    levels_ = LevelLibrary::loadDirectory(levelsDir);
    worlds_ = OverworldLibrary::load(fs::join(baseDir, "worlds"));
    saves_ = std::make_unique<SaveManager>(saveDir.empty() ? fs::join(baseDir, "saves") : fs::join(saveDir, "saves"));
    scripting::ScriptLibrary::baseDir() = assetsDir;
    settingsScreen_ = std::make_unique<SettingsScreen>(*win_, *ui_, *audio_, settings_, *saves_);
    settingsScreen_->applyAll();
}

Game::~Game() = default;

// ================================================================ main loop

void Game::run() {
    if (const char* st = std::getenv("MG_STATE")) {   // debug: open a menu directly ("slots", "settings")
        std::string v = st;
        if (v == "slots") openSlots(false);
        else if (v == "settings") openSettings(GameState::Title);
    }
    if (std::getenv("MG_AUTOSTART")) { openSlots(false); tryPlaySlot(std::atoi(std::getenv("MG_AUTOSTART")));
        if (const char* lv = std::getenv("MG_LEVEL")) { map_ = nullptr; inMap_ = false; beginLoading(lv); } }   // debug: skip the menus
    while (!win_->shouldClose()) {
        plat_.pollEvents();
        Pad::update();
        float dt = std::min(plat_.frameTime(), 0.1f);
        audio_->update();
        if (Pad::keyPressed(Key::F11)) { win_->toggleFullscreen(); settings_.fullscreen = plat_.isFullscreen(); }

        update(dt);
        draw();
        if (quit_) break;
    }
    saveSlotNow();
    settings_.save(settingsPath_);
}

void Game::update(float dt) {
    win_->mouse(mouseX_, mouseY_);
    mx_ = (mouseX_ - Screen::VW / 2.0) / 2.0;
    my_ = (Screen::VH / 2.0 - mouseY_) / 2.0;
    click_ = plat_.mousePressed(0);

    uiAcc_ += dt;
    while (uiAcc_ >= Step) {
        uiAcc_ -= Step;
        uiTick();
    }

    switch (state_) {
        case GameState::Title: updateTitleInput(); break;
        case GameState::Paused: updatePauseInput(); break;
        case GameState::Playing: updatePlaying(dt); break;
        case GameState::Map: updateMap(dt); break;
        default: break;
    }
}

// Everything that animates at the original's fixed 30 ticks per second.
void Game::uiTick() {
    prevPanelX_ = panelX_; prevPauseX_ = pauseX_; prevWipeX_ = wipeX_; prevMapPulse_ = mapPulse_;
    for (int i = 0; i < 3; i++) prevTitleSize_[i] = titleSize_[i];
    switch (state_) {
        case GameState::Title: panelTick(); titleTick(); break;
        case GameState::Settings: panelTick(); break;
        case GameState::Slots: panelTick(); break;
        case GameState::Loading: loadingTick(); break;
        case GameState::Paused: pauseTick(); break;
        case GameState::Wipe: wipeTick(); break;
        case GameState::Map: mapPulse_ += 0.2; break;
        case GameState::Playing: if (introT_ < 99) introT_++; break;
        default: break;
    }
}

// ================================================================ helpers

void Game::stage(const std::string& group, const std::string& key, double x, double y, double size, int alpha) {
    scene_->drawSprite(sprites_->get(group, key), x, y, size / 100.0, false, 0, alpha);
}

bool Game::hitSprite(const std::string& group, const std::string& key, double x, double y, double size) {
    rt::Sprite* s = sprites_->get(group, key);
    return s && SceneRenderer::hit(*s, x, y, size / 100.0, mx_, my_);
}

// True when one of the keys bound to the action went down this frame.
bool Game::pressed(const std::string& action) {
    auto it = settings_.keys.find(action);
    if (it == settings_.keys.end()) return false;
    for (int k : it->second) if (k != 0 && plat_.keyPressed(k)) return true;
    if (action == "pause" && Pad::pressed(PadAct::Start)) return true;
    return false;
}

bool Game::held(const std::string& action) {
    auto it = settings_.keys.find(action);
    if (it == settings_.keys.end()) return false;
    for (int k : it->second) if (k != 0 && plat_.keyDown(k)) return true;
    return false;
}

void Game::ensureMusic() {
    if (musicOn_) return;
    audio_->playMusic("my_song_68");
    musicOn_ = true;
}

void Game::saveSlotNow() { if (slot_) saves_->save(*slot_); }

// ================================================================ level flow

void Game::startGame(const std::string& id) {
    LevelData* level = levels_->get(id);
    if (!level) return;
    world_ = std::make_unique<World>(level, id, [this](const std::string& s) { return levels_->get(s); });
    if (slot_) world_->flags = std::shared_ptr<std::map<std::string, std::string>>(std::shared_ptr<void>(), &slot_->flags);
    else world_->flags = std::make_shared<std::map<std::string, std::string>>();
    world_->soundRequested = [this](const std::string& n) { audio_->playSfx(n); };
    world_->dialogRequested = [this](const std::string& t) { openDialog(t); };
    world_->states = states_;
    world_->notice = [this](const std::string& msg) { toast_ = msg; toastT_ = 2.0f; };
    if (slot_ && !map_) slot_->level = id;
    ensureMusic();
    acc_ = 0;
    input_->reset();
}

void Game::openDialog(const std::string& text) {
    dialogText_ = text;
    state_ = GameState::Dialog;
}

void Game::handlePending() {
    if (!world_ || !world_->pending) return;
    LoadRequest req = *world_->pending;
    req_ = req;
    if (req.wipe) {
        wipeWin_ = req.win;
        wipeX_ = req.win ? -811 : -826;   // Scratch fences the sprite just off the left edge
        wipePhase_ = 1;
        wipeLoaded_ = false;
        state_ = GameState::Wipe;
    } else {
        applyLoad(req);
    }
}

void Game::applyLoad(LoadRequest req) {
    if (!world_) return;
    if (req.win) {
        completeLevel();
        if (map_ && inMap_) { showMap(); return; }
    }
    LevelData* level = levels_->get(req.level);
    if (!level) {
        if (req.win) { audio_->stopMusic(); musicOn_ = false; state_ = GameState::End; return; }
        level = world_->level;
        req = LoadRequest{world_->levelId, LoadKind::Respawn, false, 0, false};
    }
    if (slot_ && !map_) slot_->level = req.level;
    world_->load(level, req.level, req.kind, req.entrySide);
    input_->reset();
}

// Records the finished level in the save slot.
void Game::completeLevel() {
    if (!slot_ || !world_) return;
    slot_->gems += world_->coins;
    if (map_ && inMap_) {
        if (MapNode* node = map_->node(slot_->node)) {
            std::string key = map_->id + "/" + node->id;
            if (std::find(slot_->completed.begin(), slot_->completed.end(), key) == slot_->completed.end()) slot_->completed.push_back(key);
        }
    }
    saves_->save(*slot_);
}

void Game::toTitle() {
    states_->clear();
    saveSlotNow();
    audio_->stopMusic();
    musicOn_ = false;
    world_.reset();
    slot_ = nullptr;
    map_ = nullptr;
    inMap_ = false;
    state_ = GameState::Title;
    resetTitle();
}

void Game::leaveToMapOrTitle() {
    audio_->pauseMusic(false);
    if (map_ && inMap_) { world_.reset(); showMap(); } else toTitle();
}

void Game::resetTitle() {
    panelX_ = -571;
    panelMenuX_ = 5;
    panelMode_ = 0;
    kbSel_ = -1;
    for (double& s : titleSize_) s = 100;
}

// ================================================================ title screen

void Game::panelTick() {
    if (panelMode_ == 0) panelX_ += panelMenuX_ - panelX_ / 4.7;
    else if (std::abs(panelX_) <= 400) panelX_ += panelX_ / 32 - 60;
}

bool Game::titleHot(int i) {
    return panelMode_ == 0 && (kbSel_ == i || hitSprite("mainmenu", TitleButtons[i].first, panelX_ - 180, TitleButtons[i].second, titleSize_[i]));
}

void Game::titleTick() {
    for (int i = 0; i < 3; i++) titleSize_[i] = ease(titleSize_[i], titleHot(i));
}

void Game::updateTitleInput() {
    if (Pad::keyPressed(Key::Down) || Pad::keyPressed(Key::S)) kbSel_ = (kbSel_ + 1) % 3;
    if (Pad::keyPressed(Key::Up) || Pad::keyPressed(Key::W)) kbSel_ = (kbSel_ + 2) % 3;

    int act = -1;
    if (click_)
        for (int i = 0; i < 3; i++)
            if (hitSprite("mainmenu", TitleButtons[i].first, panelX_ - 180, TitleButtons[i].second, titleSize_[i])) act = i;
    if (Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space)) act = kbSel_ < 0 ? 0 : kbSel_;

    if (act == 0) openSlots(false);
    else if (act == 1) openSettings(GameState::Title);
    else if (act == 2) openSlots(true);
}

void Game::drawTitleBackdrop(int sceneAlpha) {
    scene_->beginStage();
    bg("dialog", "costume5");
    bg("dialog", "costume2", sceneAlpha);
}

void Game::drawTitle(bool buttons) {
    drawTitleBackdrop();
    if (panelMode_ == 1 && std::abs(ip(prevPanelX_, panelX_)) > 400) return;
    stage("mainmenu", "costume1", (ip(prevPanelX_, panelX_) - 150) * -0.9, 120);
    if (!buttons) return;
    for (int i = 0; i < 3; i++)
        stage("mainmenu", TitleButtons[i].first, ip(prevPanelX_, panelX_) - 180, TitleButtons[i].second, ip(prevTitleSize_[i], titleSize_[i]));
    ui_->text("Start = pick a slot   Load = continue a slot   F11: fullscreen", Screen::VW / 2.f, (float)Screen::VH - 20, 16,
              Color(255, 255, 255, 200), false, 1, true);
}

// ================================================================ loading screen

void Game::beginLoading(const std::string& level) {
    if (!levels_->get(level)) return;
    clickSfx();
    ldLevel_ = level;
    ldT_ = 0;
    ldStarted_ = false;
    state_ = GameState::Loading;
}

void Game::loadingTick() {
    ldT_++;
    if (ldT_ == 50 && !ldStarted_) {
        ldStarted_ = true;
        startGame(ldLevel_);
        introT_ = 0;
        state_ = world_ ? GameState::Playing : GameState::Title;
        if (!world_) resetTitle();
    }
}

void Game::drawLoading() {
    scene_->beginStage();
    if (ldT_ < 20) {
        bg("dialog", "costume5");
        bg("dialog", "costume2", 255 - ldT_ * 255 / 20);
    } else {
        bg("dialog", "costume10");
    }
}

// ================================================================ pause menu

void Game::enterPause() {
    state_ = GameState::Paused;
    pauseX_ = -500;
    pauseClosing_ = false;
    pauseSel_ = 0;
    audio_->pauseMusic(true);
    audio_->playSfx("swoosh");
}

void Game::pauseTick() {
    if (!pauseClosing_) pauseX_ += 0 - pauseX_ / 4.7;
    else {
        pauseX_ += pauseX_ / 8 - 20;
        if (pauseX_ < -500) {
            state_ = GameState::Playing;
            audio_->pauseMusic(false);
            acc_ = 0;
            input_->reset();
        }
    }
}

void Game::updatePauseInput() {
    if (pauseClosing_) return;
    if (pressed("pause")) { pauseClosing_ = true; return; }
    // keyboard / controller: up-down to choose, confirm to activate (the C# pause menu was mouse only)
    if (Pad::keyPressed(Key::Down) || Pad::keyPressed(Key::S)) pauseSel_ = (pauseSel_ + 1) % 3;
    if (Pad::keyPressed(Key::Up) || Pad::keyPressed(Key::W)) pauseSel_ = (pauseSel_ + 2) % 3;
    if (Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space)) {
        clickSfx();
        if (pauseSel_ == 0) pauseClosing_ = true;
        else if (pauseSel_ == 1) openSettings(GameState::Paused);
        else leaveToMapOrTitle();
        return;
    }
    if (!click_) return;
    for (int i = 0; i < 3; i++) {
        if (!hitSprite("pause", PauseButtons[i].first, pauseX_ - 180, PauseButtons[i].second)) continue;
        clickSfx();
        if (i == 0) pauseClosing_ = true;
        else if (i == 1) openSettings(GameState::Paused);
        else leaveToMapOrTitle();
        return;
    }
}

void Game::drawPause() {
    scene_->beginStage();
    const double px = ip(prevPauseX_, pauseX_);
    stage("menudialog", "costume2", px, 0);
    for (auto& b : PauseButtons) stage("pause", b.first, px - 180, b.second);
    {
        float mx = Screen::VW / 2.f + (float)(ip(prevPauseX_, pauseX_) - 180) * 2 - 120, my = Screen::VH / 2.f - (float)PauseButtons[pauseSel_].second * 2 + 10;
        ui_->text(">", mx, my, 34, Color(255, 190, 60), true, 1, true);
    }
    // the blank button in the pause art gets its label here
    ui_->text("settings", Screen::VW / 2.f + (float)(ip(prevPauseX_, pauseX_) - 180) * 2, Screen::VH / 2.f - (float)PauseButtons[1].second * 2 + 8, 22,
              Color(30, 30, 30), false, 1);
}

// ================================================================ save slots

void Game::openSlots(bool loadMode) {
    clickSfx();
    slotLoadMode_ = loadMode;
    slotSel_ = std::clamp(settings_.lastSlot, 0, SaveManager::SlotCount - 1);
    eraseConfirm_ = -1;
    state_ = GameState::Slots;
    panelMode_ = 1;
    panelX_ = -25;
}

std::string Game::slotLevelName(const SaveSlot& s) {
    OverworldData* ow = worlds_.get(s.world);
    std::string w = ow ? ow->name : "";
    LevelData* lv = levels_->get(s.level);
    std::string l = (lv && !lv->name.empty()) ? lv->name : s.level;
    return !w.empty() ? w : l;
}

void Game::enterSlot(SaveSlot& s) {
    clickSfx();
    slot_ = &s;
    settings_.lastSlot = s.index;
    if (!s.used) {
        s.completed.clear(); s.flags.clear(); s.gems = 0; s.playTime = 0;
        const auto& sw = levels_->startWorldId;
        std::string first = worlds_.ids().empty() ? "" : worlds_.ids().front();
        s.world = (sw && worlds_.get(*sw)) ? *sw : first;
        OverworldData* ow = worlds_.get(s.world);
        s.node = ow ? ow->start : "";
        s.level = levels_->startLevel().value_or("");
        saves_->save(s);
    }
    map_ = worlds_.get(s.world);
    if (map_) {
        if (!map_->node(s.node)) s.node = map_->start;
        inMap_ = true;
        showMap();
    } else {
        inMap_ = false;
        std::string id = levels_->has(s.level) ? s.level : levels_->startLevel().value_or("");
        beginLoading(id);
    }
}

void Game::updateSlots() {
    if (eraseConfirm_ >= 0) return;
    int n = SaveManager::SlotCount;
    if (Pad::keyPressed(Key::Down) || Pad::keyPressed(Key::S)) slotSel_ = (slotSel_ + 1) % n;
    if (Pad::keyPressed(Key::Up) || Pad::keyPressed(Key::W)) slotSel_ = (slotSel_ + n - 1) % n;
    if (Pad::keyPressed(Key::Delete) && saves_->slots[slotSel_].used) { eraseConfirm_ = slotSel_; return; }
    if (Pad::keyPressed(Key::Escape)) { clickSfx(); leaveSlots(); return; }
    if (Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space)) tryPlaySlot(slotSel_);
}

void Game::tryPlaySlot(int i) {
    SaveSlot& s = saves_->slots[i];
    if (slotLoadMode_ && !s.used) return;
    enterSlot(s);
}

void Game::leaveSlots() {
    state_ = GameState::Title;
    resetTitle();
    panelMenuX_ = 0;
}

void Game::drawSlots() {
    drawTitle(false);
    float W = 700, X = (Screen::VW - W) / 2.f;
    ui_->rect(0, 0, (float)Screen::VW, (float)Screen::VH, Color(0, 0, 0, 120));
    ui_->text(slotLoadMode_ ? "Load game" : "Start game", Screen::VW / 2.f, 100, 48, White, true, 1, true);
    ui_->text("Pick a save slot", Screen::VW / 2.f, 134, 20, Color(255, 255, 255, 200), false, 1);

    float mdx, mdy;
    plat_.mouseDelta(mdx, mdy);
    for (int i = 0; i < SaveManager::SlotCount; i++) {
        SaveSlot& s = saves_->slots[i];
        float y = 170 + i * 150;
        bool disabled = slotLoadMode_ && !s.used;
        bool hot = eraseConfirm_ < 0 && mouseX_ >= X && mouseX_ < X + W && mouseY_ >= y && mouseY_ < y + 130;
        if (hot && click_ && !(mouseX_ > X + W - 140)) { slotSel_ = i; tryPlaySlot(i); return; }
        if (hot && (mdx * mdx + mdy * mdy > 0.5f)) slotSel_ = i;
        bool sel = slotSel_ == i;
        ui_->rect(X, y, W, 130, sel ? Color(50, 54, 100, 240) : Color(24, 26, 56, 235), 16,
                  sel ? Color(255, 190, 60) : Color(255, 255, 255, 100), sel ? 4.f : 2.f);
        Color txt = disabled ? Color(255, 255, 255, 110) : White;
        ui_->text(sfmt("Slot %d", i + 1), X + 28, y + 44, 30, txt, true, 0, true);
        if (s.used) {
            ui_->text(slotLevelName(s), X + 28, y + 78, 22, Color(255, 230, 130), true);
            ui_->text(sfmt("%d gems   -   %s   -   %d levels done", s.gems, SaveSlot::formatTime(s.playTime).c_str(), (int)s.completed.size()),
                      X + 28, y + 108, 18, Color(255, 255, 255, 200));
            bool eraseHot = eraseConfirm_ < 0 && mouseX_ > X + W - 130 && mouseX_ < X + W - 20 && mouseY_ > y + 20 && mouseY_ < y + 62;
            ui_->rect(X + W - 130, y + 20, 110, 42, eraseHot ? Color(220, 70, 70) : Color(120, 40, 50), 10, Color(255, 255, 255, 90));
            ui_->text("Erase", X + W - 75, y + 48, 20, White, true, 1);
            if (eraseHot && click_) { clickSfx(); eraseConfirm_ = i; }
        } else {
            ui_->text(disabled ? "empty" : "New game", X + 28, y + 86, 24, disabled ? Color(255, 255, 255, 110) : Color(160, 255, 160), true);
        }
    }
    ui_->text("Enter: play   Delete: erase   Esc: back", Screen::VW / 2.f, 650, 18, Color(255, 255, 255, 190), false, 1);

    if (eraseConfirm_ >= 0) {
        float cx = Screen::VW / 2.f, cy = Screen::VH / 2.f;
        ui_->rect(0, 0, (float)Screen::VW, (float)Screen::VH, Color(0, 0, 0, 150));
        ui_->rect(cx - 220, cy - 100, 440, 200, Color(20, 22, 50, 250), 16, Color(255, 255, 255, 140), 3);
        ui_->text(sfmt("Erase slot %d?", eraseConfirm_ + 1), cx, cy - 40, 34, White, true, 1, true);
        ui_->text("This cannot be undone.", cx, cy - 8, 18, Color(255, 255, 255, 210), false, 1);
        bool yes = ui_->button("Yes", cx - 170, cy + 30, 150, 46, mouseX_, mouseY_) && click_;
        bool no = (ui_->button("No", cx + 20, cy + 30, 150, 46, mouseX_, mouseY_) && click_)
                  || Pad::keyPressed(Key::Escape) || Pad::keyPressed(Key::N);
        if (yes || Pad::keyPressed(Key::Y)) { saves_->erase(eraseConfirm_); audio_->playSfx("pop"); eraseConfirm_ = -1; }
        else if (no) { clickSfx(); eraseConfirm_ = -1; }
    }
}

// ================================================================ overworld map

bool Game::nodeDone(const std::string& id) const {
    if (!slot_ || !map_) return false;
    std::string key = map_->id + "/" + id;
    return std::find(slot_->completed.begin(), slot_->completed.end(), key) != slot_->completed.end();
}

bool Game::nodeOpen(const MapNode& n) const {
    for (auto& r : n.requires_) if (!nodeDone(r)) return false;
    return true;
}

double Game::mapWidth() const {
    double mx = 0;
    if (map_) for (auto& n : map_->nodes) mx = std::max(mx, n.x);
    return std::max<double>(map_ ? map_->pixelWidth() : 1280, mx + 160);
}

double Game::mapHeight() const {
    double my = 0;
    if (map_) for (auto& n : map_->nodes) my = std::max(my, n.y);
    return std::max<double>(map_ ? map_->pixelHeight() : 720, my + 160);
}

// Where the map should be scrolled to: centred on the cursor node (path mode) or the avatar (free roam).
std::pair<double, double> Game::mapTargetScroll() const {
    double fx = 0, fy = 0;
    if (map_ && map_->freeRoam()) { fx = frX_; fy = frY_; }
    else if (map_ && slot_) {
        for (auto& n : map_->nodes) if (n.id == slot_->node) { fx = n.x; fy = n.y; break; }
    }
    return {std::clamp(fx - Screen::VW / 2.0, 0.0, std::max(0.0, mapWidth() - Screen::VW)),
            std::clamp(fy - Screen::VH / 2.0, 0.0, std::max(0.0, mapHeight() - Screen::VH))};
}

void Game::snapMapScroll() {
    placeAvatarAtNode();
    auto t = mapTargetScroll();
    mapScroll_ = t.first; mapScrollY_ = t.second;
}

void Game::placeAvatarAtNode() {
    if (!map_) return;
    MapNode* n = slot_ ? map_->node(slot_->node) : nullptr;
    if (!n && !map_->nodes.empty()) n = &map_->nodes.front();
    if (!n) return;
    frX_ = n->x; frY_ = n->y + 14;
    if (!avatarFits(frX_, frY_)) { frX_ = n->x; frY_ = n->y; }
}

void Game::showMap() {
    states_->clear();
    saveSlotNow();
    ensureMusic();
    state_ = GameState::Map;
    world_.reset();
    snapMapScroll();
    input_->reset();
}

void Game::updateMap(float dt) {
    if (!map_ || !slot_ || map_->nodes.empty()) { toTitle(); return; }
    slot_->playTime += dt;
    mapTime_ += dt;
    auto t = mapTargetScroll();
    double k = std::min(1.0, dt * 6.0);
    mapScroll_ += (t.first - mapScroll_) * k;
    mapScrollY_ += (t.second - mapScrollY_) * k;

    if (Pad::keyPressed(Key::Escape) || pressed("pause")) { clickSfx(); toTitle(); return; }
    if (map_->freeRoam()) { updateFreeRoam(dt); return; }

    MapNode* curp = map_->node(slot_->node);
    MapNode& cur = curp ? *curp : map_->nodes[0];
    int dx = ((pressed("right") || Pad::keyPressed(Key::Right)) ? 1 : 0) - ((pressed("left") || Pad::keyPressed(Key::Left)) ? 1 : 0);
    int dy = ((pressed("down") || Pad::keyPressed(Key::Down)) ? 1 : 0) - ((pressed("up") || Pad::keyPressed(Key::Up)) ? 1 : 0);
    if (dx != 0 || dy != 0) moveCursor(cur, dx, dy);

    if (click_) {
        for (auto& n : map_->nodes) {
            auto p = nodePos(n);
            if ((mouseX_ - p.first) * (mouseX_ - p.first) + (mouseY_ - p.second) * (mouseY_ - p.second) > 28 * 28) continue;
            if (&n == &cur) { enterNode(n); return; }
            if (nodeOpen(n)) { slot_->node = n.id; audio_->playSfx("click"); }
            return;
        }
    }
    if (pressed("use") || Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space)) enterNode(cur);
}

// The node the free-roam avatar is standing close enough to enter, if any.
MapNode* Game::nearNode() {
    MapNode* best = nullptr;
    double bd = 34 * 34;
    for (auto& n : map_->nodes) {
        double d = (n.x - frX_) * (n.x - frX_) + (n.y - (frY_ - 14)) * (n.y - (frY_ - 14));
        if (d < bd) { bd = d; best = &n; }
    }
    return best;
}

bool Game::avatarFits(double x, double y) {
    // the avatar's feet box: 18 wide, 12 tall
    return map_->walkableAtPixel(x - 9, y - 4) && map_->walkableAtPixel(x + 9, y - 4) &&
           map_->walkableAtPixel(x - 9, y + 8) && map_->walkableAtPixel(x + 9, y + 8);
}

void Game::updateFreeRoam(float dt) {
    double dx = ((held("right") || plat_.keyDown(Key::Right)) ? 1 : 0) - ((held("left") || plat_.keyDown(Key::Left)) ? 1 : 0);
    double dy = ((held("down") || plat_.keyDown(Key::Down)) ? 1 : 0) - ((held("up") || plat_.keyDown(Key::Up)) ? 1 : 0);
    if (dx != 0 || dy != 0) {
        double len = std::sqrt(dx * dx + dy * dy);
        double sp = 150 * map_->speedAtPixel(frX_, frY_) * dt / len;
        double nx = frX_ + dx * sp, ny = frY_ + dy * sp;
        bool stuck = !avatarFits(frX_, frY_);      // standing on a blocked tile (placed by the editor): let them walk out
        if (stuck || avatarFits(nx, frY_)) frX_ = nx;
        if (stuck || avatarFits(frX_, ny)) frY_ = ny;
        if (dx != 0) frDir_ = dx > 0 ? 1 : -1;
        frAnim_ += dt * 10;
    } else frAnim_ = 0;

    MapNode* near = nearNode();
    if (near && (pressed("use") || Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space))) enterNode(*near);
}

void Game::moveCursor(MapNode& cur, int dx, int dy) {
    MapNode* best = nullptr;
    double bestScore = 0.3;
    double len = std::sqrt((double)(dx * dx + dy * dy));
    for (auto& id : cur.links) {
        MapNode* n = map_->node(id);
        if (!n || !nodeOpen(*n)) continue;
        double vx = n->x - cur.x, vy = n->y - cur.y, vl = std::sqrt(vx * vx + vy * vy);
        if (vl < 1) continue;
        double score = (vx * dx + vy * dy) / (vl * len);
        if (score > bestScore) { bestScore = score; best = n; }
    }
    if (best) { slot_->node = best->id; audio_->playSfx("click"); }
}

void Game::enterNode(MapNode& n) {
    if (!slot_) return;
    if (!nodeOpen(n)) { audio_->playSfx("wood_tap"); return; }
    if (!n.portal.empty()) {
        OverworldData* target = worlds_.get(n.portal);
        if (!target) return;
        map_ = target;
        slot_->world = target->id;
        slot_->node = (!n.portalNode.empty() && target->node(n.portalNode)) ? n.portalNode : target->start;
        snapMapScroll();
        audio_->playSfx("swoosh");
        saveSlotNow();
        return;
    }
    if (!n.level.empty() && levels_->has(n.level)) {
        slot_->node = n.id;
        inMap_ = true;
        beginLoading(n.level);
    }
}

void Game::drawMap() {
    if (!map_ || !slot_) return;
    bool free = map_->freeRoam();
    int VW = Screen::VW, VH = Screen::VH;
    // sky gradient (drawn directly so it sits under the map art, like the raylib call it replaces)
    if (!skyTex_.valid()) {
        uint8_t px[64 * 4];
        for (int i = 0; i < 64; i++) {
            float t = i / 63.f;
            px[i * 4] = (uint8_t)(90 + (200 - 90) * t); px[i * 4 + 1] = (uint8_t)(170 + (235 - 170) * t); px[i * 4 + 2] = (uint8_t)(235 + (255 - 235) * t); px[i * 4 + 3] = 255;
        }
        skyTex_ = plat_.createTexture(px, 1, 64, true);
    }
    plat_.drawTexture(skyTex_, 0, 0, 1, 64, 0, 0, (float)VW, (float)VH, 0, 0, 0, Color(255, 255, 255));
    for (auto& layer : map_->layers) scene_->drawMapLayer(layer, mapScroll_, mapScrollY_, 0, 0, (float)VW, (float)VH);
    scene_->drawOverworldTiles(*map_, mapScroll_, mapScrollY_, 0, 0, (float)VW, (float)VH, scene_->animTime);

    // paths
    for (auto& a : map_->nodes)
        for (auto& id : a.links) {
            MapNode* b = map_->node(id);
            if (!b || a.id.compare(b->id) > 0) continue;
            auto pa = nodePos(a); auto pb = nodePos(*b);
            bool open = nodeOpen(a) && nodeOpen(*b);
            ui_->line(pa.first, pa.second, pb.first, pb.second, Color(60, 40, 20, 230), 12);
            ui_->line(pa.first, pa.second, pb.first, pb.second, open ? Color(250, 225, 150) : Color(150, 130, 110), 7);
        }

    MapNode* cur = map_->node(slot_->node);
    for (auto& n : map_->nodes) {
        auto p = nodePos(n);
        bool open = nodeOpen(n), done = nodeDone(n.id), portal = !n.portal.empty();
        Color fill = !open ? Color(120, 120, 130) : portal ? Color(170, 110, 255) : done ? Color(90, 200, 100) : Color(255, 190, 60);
        ui_->circle(p.first, p.second, 26, Color(40, 30, 20));
        ui_->circle(p.first, p.second, 21, fill);
        ui_->text(portal ? ">" : done ? "v" : open ? "" : "x", p.first, p.second + 8, 22, White, true, 1);
        ui_->text(n.name, p.first, p.second + 52, 18, White, true, 1, true);
    }
    if (free) {
        float ax = (float)(frX_ - mapScroll_), ay = (float)(frY_ - mapScrollY_);
        float bob = (float)std::abs(std::sin(frAnim_)) * 3;
        ui_->circle(ax, ay + 6, 11, Color(0, 0, 0, 90));
        ui_->circle(ax, ay - 12 - bob, 13, Color(40, 30, 20));
        ui_->circle(ax, ay - 12 - bob, 11, Color(255, 150, 60));
        ui_->circle(ax + 4 * frDir_, ay - 15 - bob, 3, White);
        ui_->circle(ax + 5 * frDir_, ay - 15 - bob, 1.5f, Color(0, 0, 0));
        MapNode* nn = nearNode();
        if (nn) ui_->text(nodeOpen(*nn) ? nn->name + "  -  Enter" : nn->name + "  (locked)", ax, ay - 42, 18, White, true, 1, true);
    } else if (cur) {
        auto p = nodePos(*cur);
        const double pulse = ip(prevMapPulse_, mapPulse_);
        float r = 32 + (float)std::sin(pulse) * 3;
        ui_->circle(p.first, p.second - 54 + (float)std::sin(pulse * 1.3) * 4, 10, Color(255, 80, 80));
        ui_->line(p.first - 0, p.second - 44, p.first, p.second - 30, Color(255, 80, 80), 5);
        ui_->rect(p.first - r, p.second - r, r * 2, r * 2, Color(0, 0, 0, 0), r, White, 3);
    }

    // header + footer
    ui_->rect(12, 12, 360, 52, Color(0, 0, 0, 130), 12);
    ui_->text(map_->name, 28, 48, 30, White, true, 0, true);
    ui_->rect((float)VW - 292, 12, 280, 52, Color(0, 0, 0, 130), 12);
    ui_->text(sfmt("%d gems   %s", slot_->gems, SaveSlot::formatTime(slot_->playTime).c_str()), (float)VW - 28, 46, 22, Color(255, 230, 90), true, 2, true);
    MapNode* focus = free ? nearNode() : cur;
    std::string status = !focus ? "explore" : !nodeOpen(*focus) ? "locked" : !focus->portal.empty() ? "Enter: travel" : "Enter: play";
    ui_->rect(VW / 2.f - 260, (float)VH - 74, 520, 58, Color(0, 0, 0, 150), 14);
    ui_->text(focus ? focus->name : map_->name, VW / 2.f, (float)VH - 46, 28, White, true, 1, true);
    ui_->text(sfmt("%s    arrows: %s    Esc: save & exit", status.c_str(), free ? "walk" : "move"), VW / 2.f, (float)VH - 24, 16, Color(255, 255, 255, 200), false, 1);
}

// ================================================================ settings

void Game::openSettings(GameState from) {
    clickSfx();
    settingsReturn_ = from;
    settingsScreen_->open();
    state_ = GameState::Settings;
    if (from == GameState::Title) { panelMode_ = 1; panelX_ = -25; }
}

void Game::leaveSettings() {
    settings_.save(settingsPath_);
    if (settingsReturn_ == GameState::Title) {
        state_ = GameState::Title;
        resetTitle();
        panelMenuX_ = 0;
    } else {
        state_ = GameState::Paused;
        pauseClosing_ = false;
    }
}

void Game::drawSettings() {
    if (settingsReturn_ == GameState::Paused && world_) {
        scene_->drawWorld(*world_);
        scene_->beginStage();
        stage("dialog", "costume5", 0, 0, bgSize(), 215);
    } else {
        drawTitle(false);
    }
    if (settingsScreen_->run(mouseX_, mouseY_)) leaveSettings();
}

// ================================================================ transitions

void Game::wipeTick() {
    if (wipePhase_ == 1) {
        int steps = wipeWin_ ? 2 : 3;
        for (int i = 0; i < steps; i++)
            wipeX_ += wipeWin_ ? (-2 - wipeX_ / 16) / 0.8 : (-2 - wipeX_ / 15) / 1.3;

        if (!wipeLoaded_ && wipeX_ > -280) {
            wipeLoaded_ = true;
            applyLoad(req_);
            if (state_ != GameState::Wipe) return;
        }
        double tol = wipeWin_ ? 40 : 30;
        if (wipeX_ > -2 - tol) { wipePhase_ = 2; wipeX_ = -25; }
    } else {
        wipeX_ += wipeX_ / 8 + 20;
        if (std::abs(wipeX_) > 780) {
            state_ = GameState::Playing;
            acc_ = 0;
            input_->reset();
        }
    }
}

// ================================================================ playing

void Game::updatePlaying(float dt) {
    if (pressed("pause")) { enterPause(); return; }
    if (!world_) return;
    if (slot_) slot_->playTime += dt;
    if (Pad::keyPressed(Key::F1)) world_->godMode = !world_->godMode;
    if (pressed("savestate")) world_->requestSaveState();
    if (pressed("loadstate")) world_->requestLoadState();
    if (toastT_ > 0) toastT_ -= dt;
    if (Pad::keyPressed(Key::F5)) world_->load(world_->level, world_->levelId, LoadKind::Respawn, 0);
    if (introT_ < 6) return;   // the pink loading fade is still fully opaque

    acc_ += dt;
    while (acc_ >= Step && state_ == GameState::Playing && world_) {
        acc_ -= Step;
        scene_->animTime += Step;
        scene_->captureInterp(*world_);
        world_->tick(input_->poll());
        handlePending();
    }
}

void Game::updateDialogInput() {
    if (Pad::keyPressed(Key::E) || Pad::keyPressed(Key::Enter) || pressed("use") || Pad::keyPressed(Key::Escape) || click_) {
        state_ = GameState::Playing;
        if (world_) world_->dialogClosed();
        input_->reset();
        acc_ = 0;
    }
}

// ================================================================ drawing

void Game::draw() {
    static const bool prof = std::getenv("MG_PROF") != nullptr;
    static double accUpd = 0, accDraw = 0, accEnd = 0; static int n = 0;
    auto t0 = std::chrono::steady_clock::now();
    win_->beginVirtual();
    ui_->beginFrame();

    switch (state_) {
        case GameState::Title: drawTitle(true); break;
        case GameState::Settings: drawSettings(); break;
        case GameState::Slots: updateSlots(); drawSlots(); break;
        case GameState::Map: drawMap(); break;
        case GameState::Loading: drawLoading(); break;
        case GameState::End: drawEnd(); break;
        default: drawPlayfield(); break;
    }

    if (settings_.showStats)
        ui_->text(sfmt("%d fps", plat_.fps()), 12, (float)Screen::VH - 12, 16, White, false, 0, true);
    auto t1 = std::chrono::steady_clock::now();
    ui_->endFrame();
    auto t2 = std::chrono::steady_clock::now();
    win_->endVirtual();
    auto t3 = std::chrono::steady_clock::now();
    if (prof) {
        accDraw += std::chrono::duration<double, std::milli>(t1 - t0).count();
        accUpd += std::chrono::duration<double, std::milli>(t2 - t1).count();
        accEnd += std::chrono::duration<double, std::milli>(t3 - t2).count();
        if (++n == 120) { std::printf("[prof] state=%d scene+record %.2f ms | ui replay %.2f ms | present+wait %.2f ms (avg/frame)\n", (int)state_, accDraw / n, accUpd / n, accEnd / n); accUpd = accDraw = accEnd = 0; n = 0; }
    }
}

void Game::drawEnd() {
    drawTitleBackdrop();
    float cx = Screen::VW / 2.f;
    ui_->rect(cx - 300, 170, 600, 330, Color(10, 10, 40, 220), 18, Color(255, 255, 255, 120));
    ui_->text("You finished the last level!", cx, 270, 40, White, true, 1, true);
    ui_->text("Thanks for playing.", cx, 320, 24, Color(255, 255, 255, 220), false, 1);
    // the one-item UiMenu: keyboard (Enter / Space / pad confirm) or click
    bool hot = ui_->button("Back to title", cx - 150, 380, 300, 52, mouseX_, mouseY_, true);
    bool activated = (hot && click_) || Pad::keyPressed(Key::Enter) || Pad::keyPressed(Key::Space);
    if (activated) { clickSfx(); toTitle(); }
}

void Game::drawPlayfield() {
    if (!world_) return;
    scene_->interpAlpha = state_ == GameState::Playing ? std::clamp(acc_ / Step, 0.0, 1.0) : 1;
    scene_->drawWorld(*world_);
    scene_->interpAlpha = 1;
    scene_->beginStage();

    // pink loading fade (only just after the level appeared)
    if (state_ == GameState::Playing && introT_ < 26) {
        int a = introT_ < 6 ? 255 : std::max(0, 255 - (introT_ - 6) * 255 / 20);
        if (a > 0) bg("dialog", "costume10", a);
    }

    // HUD (enlarged on the PSP, where the 1280x720 screen is shown at 480x272)
#ifdef __PSP__
    const float K = 1.8f;
#else
    const float K = 1.f;
#endif
    std::string name = (!world_->level->name.empty() && world_->level->name != "test") ? world_->level->name : world_->levelId;
    ui_->rect(12, 12, 360 * K, 44 * K, Color(0, 0, 0, 120), 10 * K);
    ui_->text(sfmt("Gems %d", world_->coins), 26, 12 + 31 * K, 26 * K, Color(255, 230, 90), true, 0, true);
    ui_->text(name, 358 * K, 12 + 31 * K, 20 * K, White, false, 2, true);
    if (toastT_ > 0) ui_->text(toast_, Screen::VW / 2.f, 90 * K, 24 * K, White, true, 1, true);
    if (world_->godMode) ui_->text("FLY MODE (F1)", Screen::VW / 2.f, (float)Screen::VH - 20, 18 * K, Color(255, 120, 120), true, 1, true);

    switch (state_) {
        case GameState::Paused:
            drawPause();
            break;
        case GameState::Dialog: {
            float bw = 720 * (K > 1 ? 1.4f : 1.f), bx = (Screen::VW - bw) / 2.f;
            float bh = 190 * (K > 1 ? 1.25f : 1.f), by = Screen::VH - 30 - bh;
            ui_->rect(bx, by, bw, bh, Color(10, 10, 40, 235), 16, Color(255, 255, 255, 160), 3);
            ui_->text(dialogText_, bx + 40, by + 70 * (K > 1 ? 1.4f : 1.f), 32 * (K > 1 ? 1.5f : 1.f), White, false, 0, true);
            ui_->text("press cross / Enter", bx + bw - 20, by + bh - 20, 16 * K, Color(255, 255, 255, 170), false, 2);
            updateDialogInput();
            break;
        }
        case GameState::Wipe:
            { const double wx = ip(prevWipeX_, wipeX_);
              stage("transition", wipeWin_ ? "costume2" : "costume1", wx, 0, bgSize());
              if (wipeWin_) stage("transition", "costume4", wx, 0, bgSize()); }
            break;
        default: break;
    }
}

} // namespace mg
