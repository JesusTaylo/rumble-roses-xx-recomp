# Build environment (Windows): MSVC/Windows SDK environment, LLVM clang, CMake and Ninja.
# Entorno de build (Windows). Uso / Usage:  . .\tools\env.ps1
# Everything is discovered (vswhere, PATH, winget packages); nothing is hard-coded to a user.

$script:RrxxRoot = (Resolve-Path "$PSScriptRoot\..").Path

function Find-RrxxTool([string]$exe, [string[]]$extraDirs) {
    $c = Get-Command $exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($d in $extraDirs) { if ($d -and (Test-Path "$d\$exe")) { return "$d\$exe" } }
    $pk = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages"
    if (Test-Path $pk) {
        $f = Get-ChildItem $pk -Recurse -Filter $exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { return $f.FullName }
    }
    return $null
}

# 1) MSVC + Windows SDK environment (INCLUDE, LIB, PATH) imported from VsDevCmd.
if (-not $env:VSCMD_VER) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $vsPath = $null
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }
    if (-not $vsPath) { throw "Visual Studio Build Tools with the C++ workload was not found / No se encontro Visual Studio Build Tools con C++. Run setup.bat." }
    $vsDev = "$vsPath\Common7\Tools\VsDevCmd.bat"
    cmd /c "`"$vsDev`" -arch=x64 -no_logo >nul 2>&1 && set" | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] }
    }
}

# 2) clang, cmake, ninja (PATH, standard install dirs, winget packages).
$clang = Find-RrxxTool 'clang.exe' @("$env:ProgramFiles\LLVM\bin")
$cmake = Find-RrxxTool 'cmake.exe' @("$env:ProgramFiles\CMake\bin")
$ninja = Find-RrxxTool 'ninja.exe' @()
foreach ($t in @($clang, $cmake, $ninja)) {
    if ($t) { $env:Path = (Split-Path $t) + ";" + $env:Path }
}
if (-not $clang) { throw "clang not found / no se encontro clang (winget install LLVM.LLVM). Run setup.bat." }
if (-not $cmake) { throw "cmake not found / no se encontro cmake (winget install Kitware.CMake). Run setup.bat." }
if (-not $ninja) { throw "ninja not found / no se encontro ninja (winget install Ninja-build.Ninja). Run setup.bat." }

# 3) ReXGlue SDK built from third_party/rexglue-sdk (tools\build_sdk.ps1).
$env:REXSDK_PREFIX = "$script:RrxxRoot\build\sdk-install"
