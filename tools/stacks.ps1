# Resume compacto de crash_dumps/stacks_*.txt (pilas de todos los hilos). Agrupa hilos iguales.
# Uso: .\tools\stacks.ps1 [archivo]   (por defecto el mas reciente)
param([string]$File)
$root = (Resolve-Path "$PSScriptRoot\..").Path
if (-not $File) { $File = (Get-ChildItem "$root\crash_dumps\stacks_*.txt" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName }
$blocks = (Get-Content $File -Raw) -split '(?m)^thread ' | Where-Object { $_.Trim() }
$groups = @{}
foreach ($b in $blocks) {
    $ls = $b -split "`r?`n"
    $frames = $ls | Select-Object -Skip 1 | Where-Object { $_ -match '#\d+' } | ForEach-Object {
        ($_ -replace '^\s*#\d+ 0x[0-9A-F]{16} ', '' -replace '\(C:\\[^)]*\\([^\\)]+)\)', '($1)' -replace '__imp__', '' -replace '<.*', '').Trim()
    }
    # saltar frames de espera del SO / runtime para quedarnos con los significativos
    $sig = $frames | Where-Object { $_ -notmatch '^(Nt|Zw|Rtl|Wait|Sleep|BaseThread|std::|rex::thread::|rex::ppc::Host|Cnd_wait|TpCallback)' } | Select-Object -First 5
    $key = ($sig -join ' < ')
    if (-not $key) { $key = '(solo SO)' }
    if (-not $groups.ContainsKey($key)) { $groups[$key] = 0 }
    $groups[$key]++
}
$groups.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { '{0,3}x {1}' -f $_.Value, $_.Key.Substring(0, [Math]::Min(330, $_.Key.Length)) }
