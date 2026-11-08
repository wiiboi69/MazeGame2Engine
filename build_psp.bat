@echo off
rem Builds the PSP game with Docker (pspdev).  Output: out\maze-game-2-psp.zip  (unzip -> copy MazeGame2\ to ms0:/PSP/GAME/)
setlocal
cd /d "%~dp0"
call scripts\_env.bat
call scripts\_need_docker.bat || exit /b 1
call scripts\_bake.bat psp || exit /b 1
echo [psp] building in Docker...
docker build -f cpp/docker/Dockerfile.psp --output=out . || ( echo [!] PSP build failed & exit /b 1 )
echo.
echo Built: out\maze-game-2-psp.zip
exit /b 0
