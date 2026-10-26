<#
.SYNOPSIS
    Builds stage K13's Python packages with JGraph and runs the same client against them, recording
    what happened the way record-k13.ps1 recorded R2025b (compiler and SDK plan, stage K13):
    recordings-jgraph\k13_build.out.txt, k13_client.out.txt, k13_out\* and k13_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build    probe_k13_build.m, unchanged, under jgraph -batch: the packages in <WorkRoot>\b, the
               generated text copied into recordings-jgraph\k13_out as record-k13.ps1 copies R2025b's
      client   src\k13client\k13client.py, unchanged, run by Python through run-quiet.ps1 in each of
               record-k13.ps1's modes, JGraph's main build on PYTHONPATH, the runtime's runtime\win64
               first on the PATH (where the package's __init__.py finds jgrt.dll) and R2025b's left off it
      compare  compare-k13.py over both engines' recordings

    The work root should be as long as record-k13.ps1's (scratch\k13 and scratch\j13), so lines that
    name a path break where R2025b's did.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1. Without one, a stand-in is made in
    <WorkRoot>\runtime: bin\win64 a junction to the CLI's build folder, runtime\win64 its jgrt.dll, and
    python a copy of the CLI's python folder.

.PARAMETER Jgraph
    The jgraph.exe that builds; the CLI's Debug build when not given.

.PARAMETER Modes
    The client modes to run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $Skip = '',
    [string] $Modes = 'arrays,calls,convert,background,life,extra,runtime,version',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe')
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
if (-not $Jgraph) { $Jgraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
$Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
$jgraphFolder = Split-Path $Jgraph
$recordings = Join-Path $here 'recordings-jgraph'
New-Item -ItemType Directory -Force $recordings | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$pythonHome = Split-Path -Parent $Python

if (-not $Runtime) {
    $Runtime = Join-Path $WorkRoot 'runtime'
    $bin = Join-Path $Runtime 'bin\win64'
    if (-not (Test-Path $bin)) {
        New-Item -ItemType Directory -Force (Split-Path $bin) | Out-Null
        & cmd.exe /c mklink /J "$bin" "$jgraphFolder" | Out-Null
    }
    $version = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $jgraphFolder 'JGraph.Deploy.dll')).Version.ToString(3)
    [System.IO.File]::WriteAllText((Join-Path $Runtime 'jgraph-runtime.json'), "{`n  `"version`": `"$version`",`n  `"release`": `"25.2`"`n}", $utf8)
    New-Item -ItemType Directory -Force (Join-Path $Runtime 'runtime\win64') | Out-Null
    Copy-Item (Join-Path $jgraphFolder 'jgrt.dll') (Join-Path $Runtime 'runtime\win64\jgrt.dll') -Force
    $pyFolder = Join-Path $Runtime 'python'
    if (Test-Path $pyFolder) { Remove-Item -LiteralPath $pyFolder -Recurse -Force }
    Copy-Item (Join-Path $jgraphFolder 'python') $pyFolder -Recurse -Force
}
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($jgraphFolder, '<JGRAPH>'), @($repoRoot, '<REPO>'), @($pythonHome, '<PYTHON>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Run-Jgraph([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    $p.WaitForExit()
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Remove-Item -LiteralPath $out, $err
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1:F1} s" -f $target, $clock.Elapsed.TotalSeconds)
}

# One client run through run-quiet.ps1: its transcript's lines, indented under a header line.
function Run-Client([string[]] $arguments, [string] $header, [string] $packageRoot, [string] $folder, [int] $timeout = 900) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject (@((Join-Path $here 'src\k13client\k13client.py')) + $arguments)
    $environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache'); PYTHONPATH = $packageRoot; PYTHONIOENCODING = 'utf-8';
        PYTHONUNBUFFERED = '1'; PYTHONDONTWRITEBYTECODE = '1' }
    $quiet = @{ Exe = $Python; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); Environment = $environment;
        PathFirst = @(Join-Path $Runtime 'runtime\win64'); NoMatlabRuntime = $true; TimeoutSeconds = $timeout; OutFile = $transcript;
        WorkingDirectory = $folder }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $lines = @("== $header")
    foreach ($l in ($out -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
    Write-Host ("client {0,-24} {1} lines" -f $header, $lines.Count)
    $lines
}

function Save([string[]] $lines, [string] $target) {
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1} lines" -f $target, $lines.Count)
}

# record-k13.ps1's merge: the sections just run in place of their old copies, new ones at the end.
function Merge([string[]] $lines, [string] $target) {
    $path = Join-Path $recordings $target
    $order = New-Object System.Collections.Generic.List[string]
    $sections = @{}
    $current = $null
    if (Test-Path $path) {
        foreach ($l in [System.IO.File]::ReadAllLines($path)) {
            if ($l.StartsWith('== ')) { $current = $l; $order.Add($l); $sections[$l] = New-Object System.Collections.Generic.List[string] }
            if ($current) { $sections[$current].Add($l) }
        }
    }
    $current = $null
    $fresh = @{}
    foreach ($l in $lines) {
        if ($l.StartsWith('== ')) { $current = $l; if (-not $order.Contains($l)) { $order.Add($l) }; $fresh[$l] = New-Object System.Collections.Generic.List[string] }
        if ($current) { $fresh[$current].Add($l) }
    }
    $all = @()
    foreach ($h in $order) { $all += @($(if ($fresh.ContainsKey($h)) { $fresh[$h] } else { $sections[$h] })) }
    Save $all $target
}

$build = Join-Path $WorkRoot 'b'
$main = Join-Path $build 'py\k13pkg'
$slash = { param($p) $p.Replace('\', '/') }
if ($skipped -notcontains 'build') {
    Run-Jgraph "cd('$(& $slash $here)'); probe_k13_build('$(& $slash $build)')" 'k13_build.out.txt'
    $outCopy = Join-Path $recordings 'k13_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    $copied = 0
    foreach ($pair in @(@($main, 'main'), @((Join-Path $build 'defaults'), 'defaults'), @((Join-Path $build 'dotted'), 'dotted'),
            @((Join-Path $build 'samples'), 'samples'), @((Join-Path $build 'mcc_default'), 'mcc_default'))) {
        if (-not (Test-Path $pair[0])) { continue }
        foreach ($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File | Where-Object { $_.Extension -in '.py', '.toml', '.txt', '.json', '.log', '.html' }) {
            $rel = $f.FullName.Substring($pair[0].Length + 1).Replace('\', '_')
            [System.IO.File]::WriteAllText((Join-Path $outCopy ($pair[1] + '_' + $rel)), (Tokens ([System.IO.File]::ReadAllText($f.FullName))), $utf8)
            $copied++
        }
    }
    Write-Host "k13_out                      $copied files"
}

if ($skipped -notcontains 'client') {
    $folder = Join-Path $WorkRoot 'run'
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory -Force $folder | Out-Null
    # An earlier build's extraction would take the folder name the archive's first one gets (k13pkg0).
    $cache = Join-Path $WorkRoot 'cache'
    if (Test-Path $cache) { Remove-Item -LiteralPath $cache -Recurse -Force }
    $lines = @()
    foreach ($mode in ($Modes -split ',')) {
        switch ($mode) {
            'runtime' {
                foreach ($case in 'nojvm', 'str', 'tuple', 'bogus', 'empty', 'None', 'twice', 'number', 'logfile', 'terminate') {
                    $lines += Run-Client @('runtime', $case) "runtime $case" $main $folder $(if ($case -eq 'terminate') { 60 } else { 900 })
                }
            }
            'version' {
                foreach ($v in @(@('3', '8'), @('3', '9'), @('3', '13'), @('3', '14'), @('4', '0'), @('2', '7'))) {
                    $lines += Run-Client @('version', $v[0], $v[1]) "version $($v[0]).$($v[1])" $main $folder
                }
            }
            default { $lines += Run-Client @($mode) $mode $main $folder }
        }
    }
    Merge $lines 'k13_client.out.txt'
}

if ($skipped -notcontains 'compare') {
    & $Python (Join-Path $here 'compare-k13.py')
}
