<#
.SYNOPSIS
    Records how R2025b builds a Python package (compiler and SDK plan, stage K13): what
    compiler.build.pythonPackage and mcc -W python print, write and refuse, and a Python client of
    the package run every way. Writes recordings\k13_build.out.txt, k13_client.out.txt and k13_out\*.

.DESCRIPTION
    probe_k13_build runs under matlab -batch -noFigureWindows over copies of stage K9's functions
    (src\k9) and K13's (src\k13). src\k13client\k13client.py is then run by Python through
    run-quiet.ps1 in each of its modes, the main build's folder on PYTHONPATH (nothing is installed
    with pip), R2025b's runtime\win64 first on the PATH as the package's __init__.py looks for it
    there. The generated text is copied into recordings\k13_out with the paths replaced by tokens.
    No window is shown; the client turns the beep off (k9quiet) before its first error.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build' and 'client' to leave out.

.PARAMETER Modes
    The client modes to run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $Modes = 'arrays,calls,convert,background,life,extra,runtime,version',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [int] $TimeoutSeconds = 2400
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$pythonHome = Split-Path -Parent $Python

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'), @($pythonHome, '<PYTHON>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text = [regex]::Replace($text, 'mex_[0-9]+_[0-9]+', 'mex_<N>')
    $text
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
        -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    try {
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

# One client run through run-quiet.ps1: its transcript's lines, indented under a header line.
function Run-Client([string[]] $arguments, [string] $header, [string] $packageRoot, [string] $folder, [int] $timeout = 900) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject (@((Join-Path $here 'src\k13client\k13client.py')) + $arguments)
    $environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache'); PYTHONPATH = $packageRoot; PYTHONIOENCODING = 'utf-8';
        PYTHONUNBUFFERED = '1'; PYTHONDONTWRITEBYTECODE = '1' }
    $quiet = @{ Exe = $Python; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json));
        Environment = $environment; MatlabRoot = $MatlabRoot; TimeoutSeconds = $timeout; OutFile = $transcript; WorkingDirectory = $folder }
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

# The recording's sections ("== header" and the lines under it), the ones just run in place of
# their old copies and new ones added at the end, so one mode can be recorded again on its own.
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
if ($skipped -notcontains 'build') {
    Run-Matlab "cd('$here'); probe_k13_build('$build')" 'k13_build.out.txt'
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
    if ($skipped -notcontains 'build') {
        $cache = Join-Path $WorkRoot 'cache'
        if (Test-Path $cache) { Remove-Item -LiteralPath $cache -Recurse -Force }
    }
    $lines = @()
    foreach ($mode in ($Modes -split ',')) {
        switch ($mode) {
            'runtime' {
                foreach ($case in 'nojvm', 'str', 'tuple', 'bogus', 'empty', 'None', 'twice', 'number', 'logfile', 'terminate') {
                    # R2025b's initialize() after terminate_runtime() never returns: a minute is enough to show it.
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
