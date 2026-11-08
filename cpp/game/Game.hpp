#pragma once
#include "SettingsScreen.hpp"
#include "runtime/SceneRenderer.hpp"
#include "core/LevelLibrary.hpp"
#include "core/Overworld.hpp"

namespace mg {

enum class GameState { Title, Settings, Slots, Map, Loading, Playing, Paused, Dialog, Wipe, End };

// The playable game. Menus, loading screen and transitions follow the Scratch project: all menu art is drawn in
// Scratch stage coordinates (480x360, origin in the centre, y up) and animated at the project's 30 ticks per second.
// Flow: title -> save slot -> (overworld map ->) level -> back to the map.
class Game {
public:
    Game(plat::Platform& platform, const std::string& assetsDir, const std::string& levelsDir, const std::string& settingsPath,
         const std::string& saveDir = "");
    ~Game();
    void run();

private:
    static constexpr double Step = 1.0 / GameConstants::TicksPerSecond;

    plat::Platform& plat_;
    rt::Settings settings_;
    std::string settingsPath_;
    std::unique_ptr<rt::AppWindow> win_;
    std::unique_ptr<rt::SpriteLibrary> sprites_;
    std::unique_ptr<rt::SceneRenderer> scene_;
    std::unique_ptr<rt::Ui> ui_;
    std::unique_ptr<rt::AudioEngine> audio_;
    std::unique_ptr<rt::GameInput> input_;
    std::unique_ptr<LevelLibrary> levels_;
    OverworldLibrary worlds_;
    std::unique_ptr<rt::SaveManager> saves_;
    std::unique_ptr<SettingsScreen> settingsScreen_;
    // per-level saved states (F6 / F7). Cleared whenever the overworld is shown.
    std::shared_ptr<std::map<std::string, WorldSnapshot>> states_ = std::make_shared<std::map<std::string, WorldSnapshot>>();
    plat::Texture skyTex_;     // 1x64 gradient stretched over the map background
    std::string toast_;
    float toastT_ = 0;

    std::unique_ptr<World> world_;
    GameState state_ = GameState::Title;
    double acc_ = 0;      // gameplay accumulator
    double uiAcc_ = 0;    // menu animation accumulator
    bool musicOn_ = false;

    // mouse in screen and stage coordinates
    float mouseX_ = 0, mouseY_ = 0;
    double mx_ = 0, my_ = 0;
    bool click_ = false;

    // save slots and overworld
    rt::SaveSlot* slot_ = nullptr;
    OverworldData* map_ = nullptr;
    bool inMap_ = false;                 // the running level was started from the map
    bool slotLoadMode_ = false;
    int slotSel_ = 0;
    int eraseConfirm_ = -1;
    double mapScroll_ = 0, mapScrollY_ = 0, mapTime_ = 0;
    double frX_ = 0, frY_ = 0, frAnim_ = 0;   // free-roam avatar (map pixels)
    int frDir_ = 1;
    double mapPulse_ = 0;

    // title screen (sprite "main menu dilog" slides, the buttons follow it)
    double panelX_ = -571;
    double panelMenuX_ = 5;
    int panelMode_ = 0;                  // 0 = slide in, 1 = slide out to the left
    double titleSize_[3] = {100, 100, 100};
    int kbSel_ = -1;

    // loading screen
    int ldT_ = 0;
    std::string ldLevel_;
    bool ldStarted_ = false;
    int introT_ = 99;                    // ticks since the level appeared (drives the pink fade-out)

    // pause menu ("menu dilog" green shard)
    double pauseX_ = -500;
    bool pauseClosing_ = false;
    int pauseSel_ = 0;                   // controller / keyboard selection in the pause menu

    GameState settingsReturn_ = GameState::Title;
    int endSel_ = 0;
    float lastMouseX_ = 0, lastMouseY_ = 0;

    // menu animation is logic-stepped at 30 Hz; these remember the previous step so drawing can blend between steps
    double prevPanelX_ = -571, prevTitleSize_[3] = {100, 100, 100}, prevPauseX_ = -500, prevWipeX_ = 0, prevMapPulse_ = 0;
    double ip(double prev, double cur) const {
        double a = std::clamp(uiAcc_ / Step, 0.0, 1.0);
        return std::abs(cur - prev) > 200 ? cur : prev + (cur - prev) * a;   // big jumps (resets, wipe restarts) are not blended
    }

    // transitions
    LoadRequest req_;
    double wipeX_ = 0;
    int wipePhase_ = 0;
    bool wipeLoaded_ = false, wipeWin_ = false;

    std::string dialogText_;
    bool quit_ = false;

    void update(float dt);
    void uiTick();
    void stage(const std::string& group, const std::string& key, double x, double y, double size = 100, int alpha = 255);
    double bgSize() const { return GameConstants::widescreen() ? 134 : 100; }
    void bg(const std::string& group, const std::string& key, int alpha = 255) { stage(group, key, 0, 0, bgSize(), alpha); }
    bool hitSprite(const std::string& group, const std::string& key, double x, double y, double size = 100);
    static double ease(double size, bool hot) { return size + 0.2 * ((hot ? 125.0 : 100.0) - size); }
    void clickSfx() { audio_->playSfx("click"); }
    bool pressed(const std::string& action);
    bool held(const std::string& action);
    void ensureMusic();
    void saveSlotNow();

    void startGame(const std::string& id);
    void openDialog(const std::string& text);
    void handlePending();
    void applyLoad(LoadRequest req);
    void completeLevel();
    void toTitle();
    void leaveToMapOrTitle();
    void resetTitle();

    void panelTick();
    bool titleHot(int i);
    void titleTick();
    void updateTitleInput();
    void drawTitleBackdrop(int sceneAlpha = 255);
    void drawTitle(bool buttons);

    void beginLoading(const std::string& level);
    void loadingTick();
    void drawLoading();

    void enterPause();
    void pauseTick();
    void updatePauseInput();
    void drawPause();

    void openSlots(bool loadMode);
    std::string slotLevelName(const rt::SaveSlot& s);
    void enterSlot(rt::SaveSlot& s);
    void updateSlots();
    void tryPlaySlot(int i);
    void leaveSlots();
    void drawSlots();

    bool nodeDone(const std::string& id) const;
    bool nodeOpen(const MapNode& n) const;
    double mapWidth() const;
    double mapHeight() const;
    std::pair<double, double> mapTargetScroll() const;
    void snapMapScroll();
    void placeAvatarAtNode();
    void showMap();
    std::pair<float, float> nodePos(const MapNode& n) const { return {(float)(n.x - mapScroll_), (float)(n.y - mapScrollY_)}; }
    void updateMap(float dt);
    MapNode* nearNode();
    bool avatarFits(double x, double y);
    void updateFreeRoam(float dt);
    void moveCursor(MapNode& cur, int dx, int dy);
    void enterNode(MapNode& n);
    void drawMap();

    void openSettings(GameState from);
    void leaveSettings();
    void drawSettings();

    void wipeTick();

    void updatePlaying(float dt);
    void updateDialogInput();

    void draw();
    void drawEnd();
    void drawPlayfield();
};

} // namespace mg
