@echo off
rem Builds the PS4 package with Docker (pacbrew SDK + OpenOrbis headers).  Output: out\maze-game-2-ps4.pkg
setlocal
cd /d "%~dp0"
call scripts\_env.bat
call scripts\_need_docker.bat || exit /b 1
call scripts\_bake.bat hires || exit /b 1
echo [ps4] building in Docker ^(the first run downloads the SDK, a few minutes^)...
docker build -f cpp/docker/Dockerfile.ps4 --output=out . || ( echo [!] PS4 build failed & exit /b 1 )
echo.
echo Built: out\maze-game-2-ps4.pkg
exit /b 0
