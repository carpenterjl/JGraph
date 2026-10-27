<#
.SYNOPSIS
    Runs stage K19's probe against JGraph (compiler and SDK plan): probe_k19_apps, unchanged, under
    jgraph -batch, and compares its transcript with R2025b's recording.

.DESCRIPTION
    The probe asks the compiler app commands every question a session without windows can answer:
    their counts and help, the legacy apps' warnings and refusals, the forms that take a file. Under
    jgraph -batch there is no window either, so what R2025b publishes to its app gallery is JGraph's
    refusal that the session has no window, recorded as such.

    Writes recordings-jgraph\k19_apps.out.txt and k19_differences.txt (compare-k19.py).

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Jgraph
    The jgraph.exe that runs the probe; the CLI's Debug build when not given.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Jgraph,
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [switch] $SkipCompare
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

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($jgraphFolder, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text
}

$probe = Join-Path $WorkRoot 'p'
$statement = "cd('$($here.Replace('\', '/'))'); probe_k19_apps('$($probe.Replace('\', '/'))')"
$out = Join-Path $WorkRoot 'probe.stdout'
$err = Join-Path $WorkRoot 'probe.stderr'
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru -Wait
$text = [System.IO.File]::ReadAllText($out)
$errText = [System.IO.File]::ReadAllText($err)
if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
$text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
$target = Join-Path $recordings 'k19_apps.out.txt'
[System.IO.File]::WriteAllText($target, (Tokens $text), $utf8)
Write-Host ("k19_apps (jgraph)            exit {0}  {1:F1} s" -f $p.ExitCode, $clock.Elapsed.TotalSeconds)

if (-not $SkipCompare) {
    $differences = Join-Path $recordings 'k19_differences.txt'
    & $Python -I (Join-Path $here 'compare-k19.py') $target *> $differences
    $code = $LASTEXITCODE
    Get-Content $differences | Select-Object -First 3
    Write-Host ("k19_differences.txt          {0} unexplained" -f $code)
}
