# Rumble Roses XX (PC recomp) - one-click setup / instalacion guiada.
#
#   setup.bat "D:\path\to\your-copy.iso"        (or just run setup.bat and type the path)
#
# What it does / Que hace:
#   1. asks for the ISO of YOUR copy of the game          pide la ISO de TU copia del juego
#   2. checks/installs build tools with winget (asks)      revisa/instala herramientas (pregunta antes)
#   3. extracts the ISO next to this folder (..\rrxx_data) extrae la ISO junto a esta carpeta
#   4. fetches, patches and builds the ReXGlue SDK         descarga, parchea y compila el SDK
#   5. recompiles default.xex (the SDK decrypts it) and builds rrxx.exe
#   6. creates jugar.bat and a shortcut with the icon
#
# No game data is ever written inside this repository. Nothing is uploaded anywhere.
# Re-running the script is safe: finished steps are skipped.
param(
    [Parameter(Position = 0)][string]$Iso,
    [switch]$Yes,            # answer "yes" to every confirmation (unattended)
    [switch]$NoShortcut,
    [switch]$NoLaunch        # do not offer to start the game at the end (used by rrxx-launcher.exe)
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force "$root\logs" | Out-Null
try { Start-Transcript -Path "$root\logs\setup.log" -Append | Out-Null } catch {}

$es = (Get-Culture).TwoLetterISOLanguageName -eq 'es'
function T([string]$en, [string]$sp) { if ($es) { $sp } else { $en } }
function Say([string]$en, [string]$sp, [string]$color = 'Gray') { Write-Host ("   " + (T $en $sp)) -ForegroundColor $color }
function Step([int]$n, [string]$en, [string]$sp) { Write-Host ""; Write-Host ("[{0}/6] {1}" -f $n, (T $en $sp)) -ForegroundColor Cyan }
function Ask([string]$en, [string]$sp, [bool]$default = $true) {
    if ($Yes) { return $true }
    $hint = if ($default) { '[Y/n]' } else { '[y/N]' }
    $a = Read-Host ("   " + (T $en $sp) + " $hint")
    if (-not $a) { return $default }
    return ($a -match '^(y|s)')
}
function Fail([string]$en, [string]$sp) {
    Write-Host ""
    Write-Host ("   ERROR: " + (T $en $sp)) -ForegroundColor Red
    throw "setup aborted"
}
# Runs a native command; stderr is shown, exit code is checked.
function Native([string]$what, [scriptblock]$cmd) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $cmd
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    if ($code -ne 0) { Fail "$what failed (exit code $code)." "$what fallo (codigo $code)." }
}
function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Find-Tool([string]$exe, [string[]]$dirs) {
    $c = Get-Command $exe -ErrorAction SilentlyContinue
    if ($c -and $c.Source -notlike '*\WindowsApps\*') { return $c.Source }
    foreach ($d in $dirs) { if ($d -and (Test-Path "$d\$exe")) { return "$d\$exe" } }
    $pk = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages"
    if (Test-Path $pk) {
        $f = Get-ChildItem $pk -Recurse -Filter $exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { return $f.FullName }
    }
    return $null
}
function Has-VsCpp {
    $vw = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vw)) { return $false }
    $p = & $vw -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    return [bool]$p -and (Test-Path "${env:ProgramFiles(x86)}\Windows Kits\10\Include")
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host "  Rumble Roses XX - PC port setup" -ForegroundColor Yellow
Write-Host (T "  This will take a while (about 30-90 minutes, mostly compiling)." "  Esto tarda un buen rato (30-90 minutos aprox., sobre todo compilando).") -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow

try {
    # ---------------------------------------------------------------- 0. sanity checks
    if (-not [Environment]::Is64BitOperatingSystem) { Fail "A 64-bit Windows is required." "Se necesita Windows de 64 bits." }
    if ($root -match '\s') {
        Say "Warning: this folder path has spaces. If a step fails, move the repo to a short path such as C:\rrxx\rrxx-recomp." `
            "Aviso: la ruta de esta carpeta tiene espacios. Si algo falla, mueve el repo a una ruta corta como C:\rrxx\rrxx-recomp." 'Yellow'
    }
    $drive = Get-PSDrive -Name $root.Substring(0, 1) -ErrorAction SilentlyContinue
    if ($drive -and $drive.Free -lt 25GB) {
        Say ("Only {0:N0} GB free on this drive; about 25 GB are needed." -f ($drive.Free / 1GB)) `
            ("Solo hay {0:N0} GB libres en este disco; se necesitan unos 25 GB." -f ($drive.Free / 1GB)) 'Yellow'
        if (-not (Ask "Continue anyway?" "Continuar de todos modos?" $false)) { throw "setup aborted" }
    }
    $gameDir = [IO.Path]::GetFullPath("$root\..\rrxx_data\extraido")

    # ---------------------------------------------------------------- 1. ISO
    Step 1 "Your copy of the game" "Tu copia del juego"
    if (Test-Path "$gameDir\default.xex") {
        Say "Game files already extracted in $gameDir - skipping the ISO question." "Los archivos ya estan extraidos en $gameDir - no hace falta la ISO." 'Green'
        $Iso = ''
    } else {
        if (-not $Iso) {
            Say "Type or drag the path of your game ISO (a legal copy that you own). Nothing is uploaded." `
                "Escribe o arrastra aqui la ruta de la ISO de tu copia del juego (legal, tuya). No se sube nada."
            $Iso = Read-Host "   ISO"
        }
        $Iso = $Iso.Trim().Trim('"').Trim("'")
        if (-not $Iso -or -not (Test-Path -LiteralPath $Iso -PathType Leaf)) { Fail "ISO file not found: '$Iso'" "No se encontro la ISO: '$Iso'" }
        $Iso = (Resolve-Path -LiteralPath $Iso).Path
        if ((Get-Item -LiteralPath $Iso).Length -lt 1GB) { Fail "That file is too small to be a game disc image." "Ese archivo es demasiado pequeno para ser una imagen de disco del juego." }
        Say "ISO: $Iso" "ISO: $Iso" 'Green'
    }

    # ---------------------------------------------------------------- 2. prerequisites
    Step 2 "Checking build tools" "Revisando herramientas de compilacion"
    Refresh-Path
    $need = @()   # { id, name, extra }
    if (-not (Find-Tool 'git.exe' @("$env:ProgramFiles\Git\cmd")))      { $need += ,@('Git.Git', 'Git', '') }
    if (-not (Has-VsCpp))                                               { $need += ,@('Microsoft.VisualStudio.2022.BuildTools', 'Visual Studio Build Tools (C++)', '--override "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"') }
    if (-not (Find-Tool 'clang.exe' @("$env:ProgramFiles\LLVM\bin")))  { $need += ,@('LLVM.LLVM', 'LLVM / clang', '') }
    if (-not (Find-Tool 'cmake.exe' @("$env:ProgramFiles\CMake\bin"))) { $need += ,@('Kitware.CMake', 'CMake', '') }
    if (-not (Find-Tool 'ninja.exe' @()))                               { $need += ,@('Ninja-build.Ninja', 'Ninja', '') }
    if (-not (Find-Tool 'python.exe' @()))                              { $need += ,@('Python.Python.3.12', 'Python 3 (for tools/triage.py)', '') }

    if ($need.Count -eq 0) {
        Say "Everything is already installed." "Todo esta instalado." 'Green'
    } else {
        Say "Missing:" "Faltan:" 'Yellow'
        $need | ForEach-Object { Write-Host ("      - " + $_[1]) }
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            Fail "winget is not available. Install the missing tools by hand (see README) or update 'App Installer' from the Microsoft Store, then run setup again." `
                 "winget no esta disponible. Instala las herramientas a mano (ver README) o actualiza 'Instalador de aplicacion' desde la Microsoft Store, y vuelve a ejecutar setup."
        }
        if (-not (Ask "Install them now with winget? (Windows may ask for administrator permission)" "Instalarlas ahora con winget? (Windows puede pedir permiso de administrador)")) {
            Fail "Cannot continue without these tools." "No se puede continuar sin estas herramientas."
        }
        foreach ($n in $need) {
            Say ("Installing " + $n[1] + " ...") ("Instalando " + $n[1] + " ...")
            $cmdline = "winget install --id $($n[0]) -e --accept-package-agreements --accept-source-agreements $($n[2])"
            Native "winget ($($n[0]))" { Invoke-Expression $cmdline }
        }
        Refresh-Path
        $still = @()
        if (-not (Find-Tool 'git.exe' @("$env:ProgramFiles\Git\cmd")))      { $still += 'git' }
        if (-not (Has-VsCpp))                                               { $still += 'Visual Studio Build Tools' }
        if (-not (Find-Tool 'clang.exe' @("$env:ProgramFiles\LLVM\bin")))  { $still += 'clang' }
        if (-not (Find-Tool 'cmake.exe' @("$env:ProgramFiles\CMake\bin"))) { $still += 'cmake' }
        if (-not (Find-Tool 'ninja.exe' @()))                               { $still += 'ninja' }
        if ($still.Count -gt 0) {
            Fail ("Installed, but not detected yet: " + ($still -join ', ') + ". Close this window, open setup.bat again and it will continue.") `
                 ("Instalado, pero aun no se detecta: " + ($still -join ', ') + ". Cierra esta ventana y vuelve a abrir setup.bat; continuara donde se quedo.")
        }
    }
    $gitExe = Find-Tool 'git.exe' @("$env:ProgramFiles\Git\cmd"); $env:Path = (Split-Path $gitExe) + ';' + $env:Path
    . "$root\tools\env.ps1"
    Say "clang: $((& clang --version | Select-Object -First 1))" "clang: $((& clang --version | Select-Object -First 1))" 'Green'

    # ---------------------------------------------------------------- 3. extract ISO
    Step 3 "Extracting the game files (outside this repository)" "Extrayendo los archivos del juego (fuera de este repositorio)"
    if (Test-Path "$gameDir\default.xex") {
        Say "Already extracted: $gameDir" "Ya extraido: $gameDir" 'Green'
    } else {
        $xdir = "$root\build\tools\extract-xiso"
        $xexe = "$xdir\build\extract-xiso.exe"
        if (-not (Test-Path $xexe)) {
            Say "Building extract-xiso (small open-source ISO extractor) ..." "Compilando extract-xiso (extractor de ISO pequeno y de codigo abierto) ..."
            if (-not (Test-Path "$xdir\.git")) {
                New-Item -ItemType Directory -Force (Split-Path $xdir) | Out-Null
                Native "git clone extract-xiso" { git clone https://github.com/XboxDev/extract-xiso $xdir }
            }
            Native "git checkout extract-xiso" { git -C $xdir checkout -q 3f5b62c }
            Native "cmake (extract-xiso)" { cmake -S $xdir -B "$xdir\build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=cl "-DCMAKE_POLICY_VERSION_MINIMUM=3.5" }
            Native "build extract-xiso" { cmake --build "$xdir\build" }
        }
        New-Item -ItemType Directory -Force $gameDir | Out-Null
        Say "Extracting (about 4 GB, a few minutes) ..." "Extrayendo (unos 4 GB, unos minutos) ..."
        Native "extract-xiso" { & $xexe -q -d $gameDir -x $Iso }
        if (-not (Test-Path "$gameDir\default.xex")) {
            Fail "default.xex was not found after extraction. Is this the right disc image (Rumble Roses XX, Xbox 360)?" `
                 "No aparece default.xex tras extraer. Es la imagen correcta (Rumble Roses XX, Xbox 360)?"
        }
        Say "Done: $gameDir" "Listo: $gameDir" 'Green'
    }

    # ---------------------------------------------------------------- 4. SDK
    Step 4 "ReXGlue SDK: download, patch and build (the slow part, 20-60 min)" "SDK ReXGlue: descarga, parches y compilacion (la parte lenta, 20-60 min)"
    $patchHash = (Get-ChildItem "$root\third_party\patches\*.patch" | Sort-Object Name | Get-FileHash -Algorithm SHA256 | ForEach-Object Hash) -join ''
    $stampFile = "$root\build\sdk-install\.rrxx_patch_stamp"
    $stamp = if (Test-Path $stampFile) { (Get-Content $stampFile -Raw).Trim() } else { '' }
    if ((Test-Path "$root\build\sdk-install\bin\rexglue.exe") -and $stamp -eq $patchHash) {
        Say "SDK already built with the current patches." "SDK ya compilado con los parches actuales." 'Green'
    } else {
        Say "Fetching the SDK and applying patches ..." "Descargando el SDK y aplicando parches ..."
        & "$root\tools\prepare_sdk.ps1"
        Say "Building the SDK (log: logs\build_sdk.log). Please wait ..." "Compilando el SDK (log: logs\build_sdk.log). Espera, por favor ..."
        & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\build_sdk.ps1"
        if ($LASTEXITCODE -ne 0) { Fail "SDK build failed. See logs\build_sdk.log." "Fallo la compilacion del SDK. Mira logs\build_sdk.log." }
        Set-Content -Path $stampFile -Value $patchHash
    }

    # ---------------------------------------------------------------- 5. recompile + build
    Step 5 "Recompiling default.xex to C++ and building the game (20-60 min)" "Recompilando default.xex a C++ y compilando el juego (20-60 min)"
    Say "The generated C++ stays in generated\ on your PC only (it is git-ignored and must never be shared)." `
        "El C++ generado se queda solo en generated\ en tu PC (esta ignorado por git y no debe compartirse)."
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\build.ps1" -Codegen
    if ($LASTEXITCODE -ne 0) { Fail "Game build failed. See logs\build.log and logs\codegen.log." "Fallo la compilacion del juego. Mira logs\build.log y logs\codegen.log." }
    if (-not (Test-Path "$root\build\dev-release-opt\rrxx.exe")) { Fail "rrxx.exe was not produced." "No se genero rrxx.exe." }

    # ---------------------------------------------------------------- 6. launcher
    Step 6 "Creating the launcher" "Creando el lanzador"
    $bat = @(
        '@echo off',
        'rem Generated by setup.ps1. Launches the game; log in logs\run_DATE.log.',
        'cd /d "%~dp0"',
        'powershell -NoProfile -ExecutionPolicy Bypass -File "tools\run.ps1" -NoBuild %*',
        'set RC=%ERRORLEVEL%',
        'if not "%RC%"=="0" (',
        '  echo.',
        '  echo === The game exited with code %RC% ===',
        '  echo A summary for bug reports is in logs\ultimo_crash.txt',
        ')',
        'pause',
        'exit /b %RC%'
    )
    Set-Content -Path "$root\jugar.bat" -Value $bat -Encoding ASCII
    Say "Created jugar.bat" "Creado jugar.bat" 'Green'
    if (-not $NoShortcut) {
        try {
            $ico = "$root\resources\rrxx-icono\rrxx.ico"
            $sh = New-Object -ComObject WScript.Shell
            $lnk = $sh.CreateShortcut("$root\Rumble Roses XX.lnk")
            $lnk.TargetPath = "$root\jugar.bat"; $lnk.WorkingDirectory = $root; $lnk.IconLocation = $ico; $lnk.Description = 'Rumble Roses XX (PC recomp)'
            $lnk.Save()
            Say "Created the shortcut 'Rumble Roses XX.lnk' (with the game icon) in this folder." "Creado el acceso directo 'Rumble Roses XX.lnk' (con el icono del juego) en esta carpeta." 'Green'
            if (Ask "Also put a copy on the Desktop?" "Poner tambien una copia en el Escritorio?") {
                $d = [Environment]::GetFolderPath('Desktop')
                $l2 = $sh.CreateShortcut("$d\Rumble Roses XX.lnk")
                $l2.TargetPath = "$root\jugar.bat"; $l2.WorkingDirectory = $root; $l2.IconLocation = $ico; $l2.Save()
            }
        } catch { Say "Could not create the shortcut (not important): $_" "No se pudo crear el acceso directo (no importa): $_" 'Yellow' }
    }

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host (T "  ALL DONE. Double-click jugar.bat (or the shortcut) to play." "  LISTO. Haz doble clic en jugar.bat (o en el acceso directo) para jugar.") -ForegroundColor Green
    Write-Host (T "  Settings: config\rrxx.toml   Problems: see README (Reporting bugs)." "  Opciones: config\rrxx.toml   Problemas: ver README (Reporting bugs).") -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    if (-not $NoLaunch -and (Ask "Launch the game now?" "Iniciar el juego ahora?" $false)) { Start-Process "$root\jugar.bat" }
    $exit = 0
} catch {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host (T "  Setup stopped: $($_.Exception.Message)" "  Setup detenido: $($_.Exception.Message)") -ForegroundColor Red
    Write-Host (T "  Logs: $root\logs  (setup.log, build_sdk.log, build.log, codegen.log)" "  Logs: $root\logs  (setup.log, build_sdk.log, build.log, codegen.log)") -ForegroundColor Red
    Write-Host (T "  You can run setup.bat again: finished steps are skipped." "  Puedes volver a ejecutar setup.bat: los pasos terminados se saltan.") -ForegroundColor Red
    Write-Host (T "  Still stuck? Open an issue and attach logs\setup.log (no game data is in it)." "  Sigues atascado? Abre un issue y adjunta logs\setup.log (no contiene datos del juego).") -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    $exit = 1
}
try { Stop-Transcript | Out-Null } catch {}
exit $exit
