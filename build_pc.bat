@echo off
rem Builds the desktop game (Windows, SDL2, MSVC via CMake).   Usage: build_pc.bat [run]
setlocal
cd /d "%~dp0"
call scripts\_env.bat
where cmake >nul 2>&1
if errorlevel 1 ( echo [!] CMake not found. Install CMake and Visual Studio's C++ tools. & exit /b 1 )
call scripts\_bake.bat hires || exit /b 1
echo [pc] configuring...
cmake -S cpp -B cpp\build || ( echo [!] configure failed & exit /b 1 )
echo [pc] building...
cmake --build cpp\build --config Release || ( echo [!] build failed & exit /b 1 )
echo.
echo Built: cpp\build\Release\maze_game.exe   ^(run it from this folder so it finds assets\ and levels\^)
if /I "%~1"=="run" cpp\build\Release\maze_game.exe
exit /b 0
