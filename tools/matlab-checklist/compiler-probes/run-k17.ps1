<#
.SYNOPSIS
    Runs stage K17's probe under JGraph as record-k17.ps1 ran it under R2025b (compiler and SDK
    plan, stages K17b to K17g) and compares: recordings-jgraph\k17_excel.out.txt and k17_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      excel    probe_k17_excel.m, unchanged, under jgraph -batch, as far as -UpTo says ('addin' until
               stage K17f gives JGraph the Excel client for the Production Server; 'all' after): the
               options, every add-in build, mcc -W excel, the server archive and the add-in beside it
      compare  compare-k17.py over both engines' recordings (--only excel until the other clients run)

    The work root should be as long as record-k17.ps1's (k17_r2025b and k17_jgraph are both ten
    characters under the same folder), so lines that name a path are as long as R2025b's. The
    probe reads the user's registry through reg.exe before and after each build, as R2025b's run
    did; JGraph's build registers nothing until stage K17c, so every delta is empty.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Jgraph
    The jgraph.exe that builds; the CLI's Debug build when not given.

.PARAMETER UpTo
    How far the probe runs: 'addin' (the default) or 'all'.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Jgraph,
    [string] $Skip = '',
    [string] $UpTo = 'addin',
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

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($jgraphFolder, '<JGRAPH>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Run-Jgraph([string] $statement, [string] $target, [int] $timeout = 1800) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    if (-not $p.WaitForExit($timeout * 1000)) {
        Stop-Process -Id $p.Id -Force
        Write-Host ("{0,-28} stopped after {1} s" -f $target, $timeout)
    }
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Remove-Item -LiteralPath $out, $err
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1:F1} s" -f $target, $clock.Elapsed.TotalSeconds)
}

$probe = Join-Path $WorkRoot 'p'
$slash = { param($p) $p.Replace('\', '/') }
if ($skipped -notcontains 'excel') {
    Run-Jgraph "cd('$(& $slash $here)'); probe_k17_excel('$(& $slash $probe)', '$UpTo')" 'k17_excel.out.txt'
}

if ($skipped -notcontains 'compare') {
    $only = if ($UpTo -eq 'all') { @() } else { @('--only', 'excel') }
    $text = (& $Python -I (Join-Path $here 'compare-k17.py') $recordings @only 2>&1 | Out-String) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k17_differences.txt'), $text, $utf8)
    Write-Host $text
}
