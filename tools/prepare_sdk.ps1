# Prepares third_party/rexglue-sdk: fetches submodules, fixes Windows symlink stubs and applies
# third_party/patches/*.patch in order. Safe to re-run (already-applied patches are skipped).
# Prepara el SDK: submodulos, enlaces simbolicos y parches. Se puede repetir sin problema.
$ErrorActionPreference = 'Continue'   # native stderr (git apply --check probes) must not abort
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root
$sdk = "$root\third_party\rexglue-sdk"

# core.autocrlf=false so the SDK is checked out with LF endings and the patches (LF) apply cleanly.
if (-not (Test-Path "$sdk\CMakeLists.txt")) {
    git -c core.autocrlf=false submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw "git submodule update failed / fallo la descarga del SDK (revisa tu conexion)" }
}

# Git on Windows (without symlink rights) checks symlinks out as tiny text files holding the target
# path. The SDK needs libmspack's real files: replace each stub with a copy of its target.
$ms = "$sdk\thirdparty\libmspack"
if (Test-Path $ms) {
    Push-Location $ms
    git ls-files -s | Where-Object { $_ -match '^120000' } | ForEach-Object {
        $rel = ($_ -split "`t", 2)[1]
        if ($rel -notlike 'cabextract/mspack/*') { return }
        $item = Get-Item $rel -ErrorAction SilentlyContinue
        if ($item -and -not $item.PSIsContainer -and $item.Length -lt 200) {
            $target = (Get-Content $rel -Raw).Trim()
            $src = Join-Path (Split-Path $rel) $target
            if (Test-Path $src -PathType Leaf) { Copy-Item $src $rel -Force }
        }
    }
    Pop-Location
}

# Patches. Idempotent: if the LAST patch is already applied everything is; otherwise restore the pristine
# SDK sources (src/ include/ only; submodules are untouched) and apply the whole series in order.
Push-Location $sdk
$patches = @(Get-ChildItem "$root\third_party\patches\*.patch" | Sort-Object Name)
git apply --reverse --check --whitespace=nowarn $patches[-1].FullName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  all patches already applied"
} else {
    git checkout -- src include 2>$null
    foreach ($p in $patches) {
        git apply --whitespace=nowarn $p.FullName 2>$null
        if ($LASTEXITCODE -ne 0) {
            git apply --whitespace=nowarn --ignore-whitespace $p.FullName
            if ($LASTEXITCODE -ne 0) { Pop-Location; throw "patch failed / fallo el parche: $($p.Name)" }
        }
        Write-Host "  applied: $($p.Name)"
    }
}
Pop-Location