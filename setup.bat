@echo off
rem Rumble Roses XX (PC recomp) - guided setup / instalacion guiada.
rem Usage: setup.bat "C:\path\to\your-copy.iso"   (or run it and type the path)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
set RC=%ERRORLEVEL%
echo.
pause
exit /b %RC%
