# Builds and installs the ReXGlue SDK (submodule third_party/rexglue-sdk) into build/sdk-install.
# Full output goes to logs/build_sdk.log.  Uso / Usage:  .\tools\build_sdk.ps1 [-Jobs 6]
param([int]$Jobs = [Math]::Max(1, [Environment]::ProcessorCount - 2), [string[]]$Configs = @('Release', 'RelWithDebInfo'))
$root = (Resolve-Path "$PSScriptRoot\..").Path
New-Item -ItemType Directory -Force "$root\logs" | Out-Null
. "$PSScriptRoot\env.ps1"
Set-Location "$root\third_party\rexglue-sdk"
$log = "$root\logs\build_sdk.log"
"== build sdk $(Get-Date -Format s) configs=$($Configs -join ',') ==" | Out-File $log -Encoding utf8
$bin = ($root -replace '\\', '/') + '/build'
cmake --preset win-amd64 -B "$bin/sdk" -DCMAKE_INSTALL_PREFIX="$bin/sdk-install" *>&1 | Out-File -FilePath $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) { Write-Host "configure failed"; Select-String $log -Pattern 'CMake Error' | Select-Object -First 20 | ForEach-Object Line; exit 1 }
# Release provides rexglue.exe and the libs used by the dev-release-opt preset; RelWithDebInfo is for debugging.
foreach ($Config in $Configs) {
    cmake --build "$bin/sdk" --config $Config --target install -j $Jobs *>&1 | Out-File -FilePath $log -Append -Encoding utf8
    if ($LASTEXITCODE -ne 0) { Write-Host "build failed ($Config)"; Select-String $log -Pattern 'error:|FAILED:' | Select-Object -First 30 | ForEach-Object Line; exit 1 }
}
Write-Host "SDK OK -> $root\build\sdk-install"