# Lanza el juego: log por ejecucion en logs/run_FECHA.log; si crashea, guarda copia en crash_dumps/
# y deja el resumen de tools/triage.py en logs/ultimo_crash.txt.
# Uso:  .\tools\run.ps1 [-Preset dev-release-opt] [-Seconds 0] [-NoBuild] [-GameArgs 'arg1','arg2']
param(
    [string]$Preset = 'dev-release-opt',
    [int]$Seconds = 0,          # 0 = hasta que el juego termine; >0 = cerrar tras N segundos (pruebas)
    [switch]$NoBuild,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArgs
)
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root
New-Item -ItemType Directory -Force logs, crash_dumps | Out-Null

if (-not $NoBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\build.ps1" -Preset $Preset
    if ($LASTEXITCODE -ne 0) { Write-Host "La compilacion fallo; ver logs/build.log"; exit $LASTEXITCODE }
}

$exe = "$root\build\$Preset\rrxx.exe"
if (-not (Test-Path $exe)) { Write-Host "rrxx.exe not found / no existe: $exe. Run setup.bat (or tools\build.ps1)."; exit 2 }
# Config del usuario: se copia junto al exe solo si no existe (no pisa cambios locales).
$cfgDst = Join-Path (Split-Path $exe) 'rrxx.toml'
if (-not (Test-Path $cfgDst) -and (Test-Path "$root\config\rrxx.toml")) { Copy-Item "$root\config\rrxx.toml" $cfgDst }
# Game files extracted by setup.bat live next to the repo folder, outside the repo.
$data = (Resolve-Path "$root\..\rrxx_data\extraido" -ErrorAction SilentlyContinue).Path
if (-not $data) { Write-Host "Game files not found / no se encontraron los archivos del juego en ..\rrxx_data\extraido. Run setup.bat."; exit 2 }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$log = "$root\logs\run_$stamp.log"
$args2 = @('--game_data_root', $data, '--log_file', $log) + @($GameArgs | Where-Object { $_ })
$env:RRXX_CRASH_DIR = "$root\crash_dumps"
Write-Host "Lanzando juego... log: $log"
$startTime = Get-Date
$p = Start-Process $exe -ArgumentList $args2 -WorkingDirectory (Split-Path $exe) -PassThru
if ($Seconds -gt 0) {
    if (-not $p.WaitForExit($Seconds * 1000)) { $p.Kill(); $p.WaitForExit(); $code = 0; $killed = $true }
} else { $p.WaitForExit() }
if (-not $killed) { $code = $p.ExitCode }

# Python para triage (instalado con winget en %LOCALAPPDATA%; si no, el del PATH).
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
if (-not (Test-Path $py)) { $py = (Get-Command python -ErrorAction SilentlyContinue).Source }

$triageFile = "$root\logs\ultimo_crash.txt"
$crashed = ($code -ne 0)
if (-not $crashed -and (Test-Path $log) -and (Select-String $log -Pattern '\[(critical|fatal)\]' -Quiet)) { $crashed = $true }
if ($crashed) {
    $dumpLog = "$root\crash_dumps\crash_$stamp.log"
    Copy-Item $log $dumpLog -ErrorAction SilentlyContinue
    Get-ChildItem (Split-Path $exe) -Filter *.dmp -ErrorAction SilentlyContinue | ForEach-Object { Move-Item $_.FullName "$root\crash_dumps\" -Force }
    $native = Get-ChildItem "$root\crash_dumps" -Filter 'native_*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $startTime } | Sort-Object LastWriteTime | Select-Object -Last 1
    $nativeArgs = @(); if ($native) { $nativeArgs = @('--native', $native.FullName) }
    $summary = if ($py) { & $py -I "$PSScriptRoot\triage.py" $log --exit-code $code @nativeArgs } else { "python no encontrado; log en $log" }
    $summary | Out-File $triageFile -Encoding utf8
    Write-Host "`n=== CRASH (codigo $code) ==="
    $summary | ForEach-Object { Write-Host $_ }
    Write-Host "`nGuardado: $triageFile  |  copia del log: $dumpLog"
} else {
    "Sin crash en $stamp (codigo $code)." | Out-File $triageFile -Encoding utf8
    Write-Host "El juego termino sin crash (codigo $code)."
}
exit $code
