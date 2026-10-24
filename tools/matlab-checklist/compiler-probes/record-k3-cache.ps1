<#
.SYNOPSIS
    Measures how R2025b's MATLAB Runtime keeps its extraction cache (compiler and SDK plan, stage
    K3) and writes recordings\k3_cache.out.txt.

.DESCRIPTION
    Runs K0's k0_env.exe and k0_path.exe through run-quiet.ps1, each case under its own
    MCR_CACHE_ROOT inside WorkRoot, and records what the cache holds after each run: the files at
    the cache root (.deploy_lock, .max_size, .mcr_cache_index), each entry folder's top level, and
    what the program said ctfroot was. The cases: a first and a second run, an entry file deleted,
    an entry file changed, the entry's .created.ts deleted, the entry folder deleted, the index
    deleted, MCR_CACHE_SIZE's forms, and eviction under a small MCR_CACHE_SIZE. Both programs turn
    the beep off first; nothing here fails before main.

.PARAMETER BuildRoot
    K0's build folder (it holds k0_env\k0_env.exe and k0_path\k0_path.exe).

.PARAMETER WorkRoot
    A scratch folder outside the repo for the caches.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BuildRoot,
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Cases = ''
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$lines = New-Object System.Collections.Generic.List[string]

function Run-Program([string] $name, [string] $cacheRoot, [hashtable] $extra = @{}) {
    $exe = Join-Path $BuildRoot "$name\$name.exe"
    $envs = @{ MCR_CACHE_ROOT = $cacheRoot }
    foreach ($k in $extra.Keys) { $envs[$k] = $extra[$k] }
    $transcript = [System.IO.Path]::GetTempFileName()
    & (Join-Path $here 'run-quiet.ps1') -Exe $exe -Environment $envs -TimeoutSeconds 300 -OutFile $transcript *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $ctf = ($out -split "`r?`n" | Where-Object { $_ -like 'ctfroot*' }) -join ''
    $show = ($out -split "`r?`n" | Where-Object { $_ -like 'which.k0_show*' }) -join ''
    $exit = ($out -split "`r?`n" | Where-Object { $_ -like 'EXIT*' }) -join ''
    $lines.Add("run`t$name`t$exit")
    if ($ctf) { $lines.Add("  $ctf") }
    if ($show) { $lines.Add("  $show") }
    $stderr = $out -split "`r?`n" | Where-Object { $_ -like 'STDERR*' }
    foreach ($e in $stderr) { $lines.Add("  $e") }
}

function Show-Cache([string] $cacheRoot) {
    $rel = Join-Path $cacheRoot 'R2025b'
    if (-not (Test-Path $rel)) { $lines.Add("  cache: no R2025b folder"); return }
    foreach ($f in Get-ChildItem -LiteralPath $rel -Force | Sort-Object Name) {
        if ($f.PSIsContainer) {
            $top = (Get-ChildItem -LiteralPath $f.FullName -Force | Sort-Object Name | ForEach-Object {
                if ($_.PSIsContainer) { $_.Name + '/' } else { $_.Name + '(' + $_.Length + ')' } }) -join ' '
            $bytes = (Get-ChildItem -LiteralPath $f.FullName -Recurse -Force -File | Measure-Object Length -Sum).Sum
            $count = (Get-ChildItem -LiteralPath $f.FullName -Recurse -Force -File | Measure-Object).Count
            $lines.Add("  entry`t$($f.Name)`tfiles $count`tbytes $bytes`t$top")
        }
        else {
            $text = [System.IO.File]::ReadAllText($f.FullName).Replace("`r", '\r').Replace("`n", '\n')
            $lines.Add("  file`t$($f.Name)`t$($f.Length)`t$text")
        }
    }
}

function Entry([string] $cacheRoot) {
    Get-ChildItem -LiteralPath (Join-Path $cacheRoot 'R2025b') -Directory | Select-Object -First 1 -ExpandProperty FullName
}

function Want([string] $case) { -not $Cases -or ($Cases -split ',') -contains $case }

function Fresh([string] $case) {
    $root = Join-Path $WorkRoot $case
    if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    $lines.Add("CASE`t$case")
    return $root
}

if (Want 'twice') {
    $c = Fresh 'twice'
    Run-Program 'k0_env' $c; Show-Cache $c
    Start-Sleep -Seconds 2
    Run-Program 'k0_env' $c; Show-Cache $c
}

if (Want 'deleted_file') {
    $c = Fresh 'deleted_file'
    Run-Program 'k0_env' $c
    Remove-Item -LiteralPath (Join-Path (Entry $c) 'k0_env\k0_show.m')
    $lines.Add('  deleted k0_env\k0_show.m')
    Run-Program 'k0_env' $c; Show-Cache $c
    $lines.Add("  k0_show.m present: $(Test-Path (Join-Path (Entry $c) 'k0_env\k0_show.m'))")
}

if (Want 'changed_file') {
    $c = Fresh 'changed_file'
    Run-Program 'k0_env' $c
    $f = Join-Path (Entry $c) 'k0_env\k0_show.m'
    $before = (Get-Item $f).Length
    [System.IO.File]::AppendAllText($f, 'tampered')
    $lines.Add("  appended to k0_env\k0_show.m ($before bytes)")
    Run-Program 'k0_env' $c; Show-Cache $c
    $lines.Add("  k0_show.m length now: $((Get-Item $f).Length)")
}

if (Want 'no_created_ts') {
    $c = Fresh 'no_created_ts'
    Run-Program 'k0_env' $c
    Remove-Item -LiteralPath (Join-Path (Entry $c) '.created.ts')
    $lines.Add('  deleted .created.ts')
    Run-Program 'k0_env' $c; Show-Cache $c
}

if (Want 'no_entry') {
    $c = Fresh 'no_entry'
    Run-Program 'k0_env' $c
    Remove-Item -LiteralPath (Entry $c) -Recurse -Force
    $lines.Add('  deleted the entry folder')
    Run-Program 'k0_env' $c; Show-Cache $c
}

if (Want 'no_index') {
    $c = Fresh 'no_index'
    Run-Program 'k0_env' $c
    Remove-Item -LiteralPath (Join-Path $c 'R2025b\.mcr_cache_index')
    $lines.Add('  deleted .mcr_cache_index')
    Run-Program 'k0_env' $c; Show-Cache $c
}

if (Want 'size_forms') {
    foreach ($form in @('150M', '150m', '2G', '64K', '500000', '0', 'abc', '10MB', ' 20M', '1.5M')) {
        $c = Fresh ("size_" + ($form.Trim() -replace '[^A-Za-z0-9]', '_'))
        $lines.Add("  MCR_CACHE_SIZE=[$form]")
        Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = $form }
        $max = Join-Path $c 'R2025b\.max_size'
        $lines.Add("  .max_size: " + $(if (Test-Path $max) { [System.IO.File]::ReadAllText($max) } else { '(none)' }))
    }
}

if (Want 'evict') {
    $c = Fresh 'evict'
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '300K' }; Show-Cache $c
    Run-Program 'k0_path' $c @{ MCR_CACHE_SIZE = '300K' }; Show-Cache $c
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '300K' }; Show-Cache $c
}

if (Want 'evict_unset') {
    # The size limit is written once; a later run without MCR_CACHE_SIZE keeps it or not.
    $c = Fresh 'evict_unset'
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '300K' }
    Run-Program 'k0_path' $c; Show-Cache $c
}

if (Want 'evict_units') {
    # 1000K holds both entries (about 350 KB) if the unit counts, and neither if 1000 is bytes.
    $c = Fresh 'evict_units'
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '1000K' }
    Run-Program 'k0_path' $c @{ MCR_CACHE_SIZE = '1000K' }; Show-Cache $c
}

if (Want 'evict_bytes') {
    # 400000 with no unit: both entries fit (about 355,000 bytes).
    $c = Fresh 'evict_bytes'
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '400000' }
    Run-Program 'k0_path' $c @{ MCR_CACHE_SIZE = '400000' }; Show-Cache $c
}

if (Want 'size_rewrite') {
    $c = Fresh 'size_rewrite'
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '300K' }
    Run-Program 'k0_env' $c @{ MCR_CACHE_SIZE = '2000000' }
    $lines.Add("  .max_size: " + [System.IO.File]::ReadAllText((Join-Path $c 'R2025b\.max_size')))
}

if (Want 'no_created_ts_file') {
    # Whether a missing .created.ts makes the runtime extract again: a deleted file comes back if so.
    $c = Fresh 'no_created_ts_file'
    Run-Program 'k0_env' $c
    Remove-Item -LiteralPath (Join-Path (Entry $c) '.created.ts')
    Remove-Item -LiteralPath (Join-Path (Entry $c) 'k0_env\k0_show.m')
    $lines.Add('  deleted .created.ts and k0_env\k0_show.m')
    Run-Program 'k0_env' $c; Show-Cache $c
}

$text = ($lines -join "`n") + "`n"
foreach ($t in @(@($WorkRoot, '<WORK>'), @($BuildRoot, '<BUILD>'), @($env:LOCALAPPDATA, '<LOCALAPPDATA>'), @($env:USERPROFILE, '<HOME>'))) {
    $text = $text.Replace($t[0], $t[1])
}
$target = Join-Path $here 'recordings\k3_cache.out.txt'
if ($Cases) { $target = Join-Path $WorkRoot 'k3_cache.partial.txt' }
[System.IO.File]::WriteAllText($target, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host $text
