@echo off
rem Stops with a clear message when Docker Desktop is missing or its engine is not running.
where docker >nul 2>&1
if errorlevel 1 (
    echo [!] Docker was not found. Install Docker Desktop, start it, and run this again.
    exit /b 1
)
docker info >nul 2>&1
if errorlevel 1 (
    echo [!] Docker is installed but its engine is not running. Open Docker Desktop and wait for "Engine running".
    exit /b 1
)
exit /b 0
