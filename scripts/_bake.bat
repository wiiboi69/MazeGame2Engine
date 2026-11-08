@echo off
rem Bakes the SVG art to PNG (the C++ runtime never renders SVG).
rem   call scripts\_bake.bat hires|psp|vita      (set REBAKE=1 to force a re-bake)
setlocal
call "%~dp0_env.bat"
set "PROFILE=%~1"
set "MARK=assets\backgrounds\001_Background-1-1.svg.png"
set "FLAG="
if /I "%PROFILE%"=="psp"  ( set "MARK=assets\backgrounds\001_Background-1-1.svg.psp.png"  & set "FLAG=--psp" )
if /I "%PROFILE%"=="vita" ( set "MARK=assets\backgrounds\001_Background-1-1.svg.vita.png" & set "FLAG=--vita" )
cd /d "%ROOT%"
if exist "%MARK%" if not "%REBAKE%"=="1" (
    echo [bake] %PROFILE% art already baked ^(set REBAKE=1 to redo^).
    exit /b 0
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [!] The .NET SDK is needed once to bake the SVG art ^(dotnet not found^). Install .NET 8 from https://dotnet.microsoft.com/download
    exit /b 1
)
echo [bake] rasterising SVGs ^(%PROFILE%^)...
dotnet run -c Release --project cpp\tools\BakeSvgs -- assets %FLAG%
if errorlevel 1 ( echo [!] baking failed & exit /b 1 )
exit /b 0
