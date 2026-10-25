<#
.SYNOPSIS
    Runs stage K7's probes with JGraph and compares them with R2025b's recordings (compiler and SDK
    plan, stage K7): probe_k7_switches and probe_k7_more under jgraph -batch, and record-k7.ps1's
    command-line cases through JGraph's jgmcc.cmd.

.DESCRIPTION
    Writes recordings-jgraph\k7_switches.out.txt, k7_more.out.txt and k7_cmd.out.txt, and
    k7_differences.txt from compare-k7.py (the switches and more cases) and a line-by-line
    comparison of the command-line cases. No window is shown.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER JGraphFolder
    The folder holding jgraph.exe and jgmcc.cmd (the CLI's Debug build by default).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $JGraphFolder = '',
    [string] $Python = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
if (-not $JGraphFolder) { $JGraphFolder = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0' }
$jgraph = Join-Path $JGraphFolder 'jgraph.exe'
$jgmcc = Join-Path $JGraphFolder 'jgmcc.cmd'
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$out = Join-Path $here 'recordings-jgraph'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($repoRoot, '<REPO>'), @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text
}

function Write-Out([string] $name, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimStart([char]0xFEFF).TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $out $name), (Tokens $text), $utf8)
}

function Run-Process([string] $file, [string] $arguments, [string] $directory) {
    $o = [System.IO.Path]::GetTempFileName()
    $e = [System.IO.Path]::GetTempFileName()
    try {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $file -ArgumentList $arguments -WorkingDirectory $directory `
            -RedirectStandardOutput $o -RedirectStandardError $e -NoNewWindow -PassThru
        $null = $p.Handle
        $p.WaitForExit()
        $watch.Stop()
        [pscustomobject]@{ Out = [System.IO.File]::ReadAllText($o); Err = [System.IO.File]::ReadAllText($e); Exit = $p.ExitCode; Seconds = $watch.Elapsed.TotalSeconds }
    }
    finally { Remove-Item -LiteralPath $o, $e -ErrorAction SilentlyContinue }
}

$slashed = $WorkRoot.Replace('\', '/')
$probes = $here.Replace('\', '/')
foreach ($probe in 'switches', 'more') {
    $r = Run-Process $jgraph "-batch `"cd('$probes'); probe_k7_$probe('$slashed')`"" $here
    $text = $r.Out
    if ($r.Err.Trim()) { $text += "`n" + ((($r.Err.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Write-Out "k7_$probe.out.txt" $text
    Write-Host ("k7_{0,-10} exit {1}  {2:0.0} s" -f $probe, $r.Exit, $r.Seconds)
}

# The command-line cases, as record-k7.ps1 ran R2025b's mcc.bat (license_Y left out: R2025b's is a
# licensing failure outside MATLAB, and JGraph has no licence to check).
$src = Join-Path $WorkRoot 'src'
$cmdOut = Join-Path $WorkRoot 'cmd'
if (Test-Path -LiteralPath $cmdOut) { Remove-Item -LiteralPath $cmdOut -Recurse -Force }
New-Item -ItemType Directory -Force $cmdOut | Out-Null
$cases = @(
    @('no_args', ''),
    @('question', '-?'),
    @('build', '-m k7_hello.m {D}'),
    @('build_verbose', '-v -m k7_hello.m {D}'),
    @('missing_file', '-m k7_nosuch.m {D}'),
    @('bad_flag', '-Q -m k7_hello.m {D}'),
    @('no_wrapper', 'k7_hello.m {D}'),
    @('single_quotes', "-W 'main:k7q' -T link:exe k7_hello.m {D}"),
    @('double_quotes', '-W "main:k7dq" -T link:exe k7_hello.m {D}'),
    @('license_Y', '-Y k7_license.dat -m k7_hello.m {D}'),
    @('warn_list', '-w list'),
    @('startup_warn', '-m startup\k7_start.m {D}'),
    @('startup_error', '-w error -m startup\k7_start.m {D}')
)
$lines = New-Object System.Collections.Generic.List[string]
foreach ($c in $cases) {
    $dir = Join-Path $cmdOut $c[0]
    $arguments = $c[1].Replace('{D}', "-d `"$dir`"")
    $r = Run-Process $env:ComSpec "/d /c `"`"$jgmcc`" $arguments`"" $src
    $lines.Add("CASE`t$($c[0])")
    $lines.Add("RUN`tmcc $($c[1])")
    $lines.Add("EXIT`t$($r.Exit)")
    $lines.Add(("SECONDS`t{0:0.0}" -f $r.Seconds))
    foreach ($l in (($r.Out -replace "`r`n", "`n").TrimEnd("`n") -split "`n")) { if ($l -ne '') { $lines.Add("  OUT|$l") } }
    foreach ($l in (($r.Err -replace "`r`n", "`n").TrimEnd("`n") -split "`n")) { if ($l -ne '') { $lines.Add("  ERR|$l") } }
    if ($c[1].Contains('{D}')) {
        $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue |
            ForEach-Object { '{0} ({1})' -f $_.FullName.Substring($dir.Length + 1), $_.Length } | Sort-Object)
        $lines.Add("FILES`t" + ($files -join ' | '))
    }
    Write-Host ("  {0,-16} exit {1}  {2:0.0} s" -f $c[0], $r.Exit, $r.Seconds)
}
Write-Out 'k7_cmd.out.txt' ($lines -join "`n")

# The differences: compare-k7.py for the two probes, and for the command line every case whose exit
# code, output lines or file names differ (the sizes and the times are JGraph's own).
$report = New-Object System.Collections.Generic.List[string]
foreach ($probe in 'switches', 'more') {
    $report.Add("== k7_$probe")
    foreach ($l in (& $Python (Join-Path $here 'compare-k7.py') (Join-Path $here "recordings\k7_$probe.out.txt") (Join-Path $out "k7_$probe.out.txt") $WorkRoot)) { $report.Add($l) }
}
$report.Add('== k7_cmd')
function Cases([string] $file) {
    $map = [ordered]@{}
    $key = $null
    foreach ($l in [System.IO.File]::ReadAllLines($file)) {
        if ($l -match '^CASE\t(.*)$') { $key = $Matches[1]; $map[$key] = New-Object System.Collections.Generic.List[string]; continue }
        if ($l -match '^SECONDS\t') { continue }
        if ($l -match '^FILES\t(.*)$') { $l = 'FILES ' + ((($Matches[1] -split ' \| ') | ForEach-Object { $_ -replace ' \(\d+\)$', '' }) -join ' | ') }
        $map[$key].Add($l)
    }
    $map
}
$theirs = Cases (Join-Path $here 'recordings\k7_cmd.out.txt')
$mine = Cases (Join-Path $out 'k7_cmd.out.txt')
$differ = 0
foreach ($k in $theirs.Keys) {
    $a = ($theirs[$k] -join "`n"); $b = ($mine[$k] -join "`n")
    if ($a -ne $b) {
        $differ++
        $report.Add("CASE`t$k"); $report.Add('  R2025b:'); foreach ($l in $theirs[$k]) { $report.Add("    $l") }
        $report.Add('  JGraph:'); foreach ($l in $mine[$k]) { $report.Add("    $l") }
    }
}
$report.Add("$differ of $($theirs.Count) command-line cases differ")
Write-Out 'k7_differences.txt' ($report -join "`n")
Write-Host 'k7_differences.txt written'
