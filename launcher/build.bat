@echo off
rem Builds rrxx-launcher.exe with the C# compiler that ships with Windows (.NET Framework 4, no SDK needed).
rem Output: launcher\out\rrxx-launcher.exe  (git-ignored; it is published in GitHub Releases, not in the repo)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found: %CSC%
  exit /b 1
)
if not exist out mkdir out
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /debug- /platform:anycpu ^
  /win32icon:"..\resources\rrxx-icono\rrxx.ico" ^
  /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /out:out\rrxx-launcher.exe Launcher.cs
if errorlevel 1 exit /b 1
echo Built: %CD%\out\rrxx-launcher.exe
