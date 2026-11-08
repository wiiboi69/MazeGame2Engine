@echo off
rem Builds everything: PC, PS4, PSP, Vita.  Stops at the first failure.
setlocal
cd /d "%~dp0"
call build_pc.bat   || exit /b 1
call build_ps4.bat  || exit /b 1
call build_psp.bat  || exit /b 1
call build_vita.bat || exit /b 1
echo.
echo All builds done. Packages are in out\ and cpp\build\Release\.
exit /b 0
