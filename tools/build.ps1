# Builds the game. All output goes to logs/build.log; only an error summary is printed.
# Compila el juego. Uso / Usage:  .\tools\build.ps1 [-Preset dev-release-opt] [-Target rrxx] [-Codegen] [-Jobs 6]
param(
    [string]$Preset = 'dev-release-opt',
    [string]$Target = 'rrxx',
    [switch]$Codegen,
    [int]$Jobs = [Math]::Max(1, [Environment]::ProcessorCount - 2)
)
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root
New-Item -ItemType Directory -Force logs | Out-Null
. "$PSScriptRoot\env.ps1"
$log = "$root\logs\build.log"
"== build $(Get-Date -Format s) preset=$Preset target=$Target ==" | Out-File $log -Encoding utf8
$buildDir = "$root\build\$Preset"

function Run($label, [scriptblock]$cmd) {
    "== $label ==" | Out-File $log -Append -Encoding utf8
    & $cmd *>&1 | Out-File -FilePath $log -Append -Encoding utf8
    return $LASTEXITCODE
}

if (-not (Test-Path "$env:REXSDK_PREFIX\bin\rexglue.exe")) {
    Write-Host "SDK not built yet / el SDK no esta compilado: run .\tools\build_sdk.ps1"; exit 1
}
if ($Codegen -or -not (Test-Path generated\default\sources.cmake)) {
    # rexglue reads default.xex directly (it decrypts/decompresses it itself).
    $rc = Run 'codegen' { & "$env:REXSDK_PREFIX\bin\rexglue.exe" codegen rrxx_manifest.toml --force --log-file logs\codegen.log }
    if ($rc -ne 0) { Write-Host "codegen failed ($rc); see logs/build.log"; exit $rc }
}
if (-not (Test-Path "$buildDir\build.ninja")) {
    $rc = Run 'configure' { cmake --preset $Preset }
    if ($rc -ne 0) { Select-String $log -Pattern 'CMake Error|error' | Select-Object -First 50 | ForEach-Object Line; exit $rc }
}
$rc = Run 'build' { cmake --build $buildDir --target $Target -j $Jobs }
if ($rc -ne 0) {
    Write-Host "BUILD FAILED ($rc). First errors:"
    Select-String $log -Pattern 'error:|error LNK|undefined symbol|FAILED:' | Select-Object -First 50 | ForEach-Object Line
    exit $rc
}
Write-Host "BUILD OK -> $buildDir"
