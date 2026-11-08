@echo off
rem Builds the PS Vita game with Docker (VitaSDK).  Output: out\maze-game-2-vita.vpk  (install with VitaShell)
setlocal
cd /d "%~dp0"
call scripts\_env.bat
call scripts\_need_docker.bat || exit /b 1
call scripts\_bake.bat vita || exit /b 1
echo [vita] building in Docker...
docker build -f cpp/docker/Dockerfile.vita --output=out . || ( echo [!] Vita build failed & exit /b 1 )
echo.
echo Built: out\maze-game-2-vita.vpk
exit /b 0
