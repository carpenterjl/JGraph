<#
.SYNOPSIS
    Runs the MATLAB stress scripts through jgraph -batch and reports pass/fail.

.DESCRIPTION
    Each script's gate has two halves, and both matter:

      * the process must exit 0, and
      * stdout must contain no line beginning with "Fail:".

    The second half is not redundant. Every self-checking section in those scripts wraps its work in
    try/catch and prints "Fail: ..." rather than rethrowing, so a script full of failures still exits
    0. Checking only $LASTEXITCODE would call that a pass.

    Exits 1 if any script fails, so it can gate a build.

.PARAMETER Exe
    The jgraph CLI to run. Defaults to the Release build in this repo.

.PARAMETER Dir
    The folder holding stess_*.m. Defaults to the demo workspace beside the repo.

.PARAMETER Only
    Run just these script numbers (e.g. -Only 22,23). Omit to run all of them.

.EXAMPLE
    ./tools/run-stress.ps1
    ./tools/run-stress.ps1 -Only 24
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [string] $Dir,
    [int[]] $Only
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not $Exe) {
    $Exe = Join-Path $repo 'src\JGraph.Cli\bin\Release\net8.0\jgraph.exe'
}

if (-not $Dir) {
    $Dir = Join-Path (Split-Path -Parent $repo) 'JGraph_demo_workspace\Matlab Stress Test'
}

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Error "jgraph.exe not found at '$Exe'. Build it first: dotnet build src/JGraph.Cli -c Release"
}

if (-not (Test-Path -LiteralPath $Dir)) {
    Write-Error "Stress-script folder not found at '$Dir'."
}

# Numeric order, not lexicographic: stess_9 comes before stess_10.
$scripts =
    Get-ChildItem -LiteralPath $Dir -Filter 'stess_*.m' |
    ForEach-Object {
        if ($_.BaseName -match '^stess_(\d+)$') {
            [pscustomobject]@{ Number = [int]$Matches[1]; File = $_ }
        }
    } |
    Where-Object { -not $Only -or $Only -contains $_.Number } |
    Sort-Object Number

if (-not $scripts) {
    Write-Error "No stress scripts matched in '$Dir'."
}

Write-Host "Running $($scripts.Count) stress script(s) from $Dir" -ForegroundColor Cyan
Write-Host ''

$failed = @()

foreach ($entry in $scripts) {
    $name = $entry.File.Name
    $output = & $Exe -batch $name -sd $Dir 2>&1 | Out-String
    $code = $LASTEXITCODE

    # -match on a multi-line string with (?m) finds a Fail: line anywhere in the output.
    $failLines = @($output -split "`r?`n" | Where-Object { $_ -match '^\s*Fail:' })

    if ($code -eq 0 -and $failLines.Count -eq 0) {
        Write-Host ("  {0,-14} pass" -f $name) -ForegroundColor Green
        continue
    }

    $reason = if ($code -ne 0) { "exit $code" } else { "$($failLines.Count) Fail: line(s)" }
    Write-Host ("  {0,-14} FAIL  ({1})" -f $name, $reason) -ForegroundColor Red
    foreach ($fail in $failLines) {
        Write-Host "      $($fail.Trim())" -ForegroundColor DarkYellow
    }

    if ($code -ne 0) {
        # The closing "jgraph: script failed — ..." line says what went wrong.
        $tail = @($output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 3)
        foreach ($line in $tail) {
            Write-Host "      $($line.Trim())" -ForegroundColor DarkYellow
        }
    }

    $failed += $name
}

Write-Host ''
if ($failed.Count -eq 0) {
    Write-Host "All $($scripts.Count) script(s) passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($failed.Count) of $($scripts.Count) script(s) failed: $($failed -join ', ')" -ForegroundColor Red
exit 1
