<#
.SYNOPSIS
    Runs the full test suite once per (linear-algebra backend x script representation) lane.

.DESCRIPTION
    Two switches decide which code the suite actually exercises, and they are orthogonal:

      * JGRAPH_LINALG    = native | managed  — which DenseLinalg implementation answers
      * JGRAPH_JGS_PACKED = 1 | 0            — packed or boxed script storage

    A milestone is green when all four lanes are, because a lane that is only ever run at its
    default tests one of the four. `native` forces a throw if the library did not load, so a lane
    asked for native can never silently test managed twice.

    Each lane's whole output is kept. Filtering for "Failed"/"Passed!" would report a truncated run
    as a clean one: a testhost that dies takes its remaining tests with it and says so in a line
    that is neither.

.PARAMETER Configuration
    Debug (default) or Release. The suite must already be built for it: this does not build.

.PARAMETER LogDirectory
    Where the per-lane logs go. Defaults to a temp folder printed on the way out.

.NOTES
    `dotnet test` can leave testhost.exe processes alive after it exits, and a later build then
    reports MSB3026, silently declines to update the test assembly, and still says "Build
    succeeded" — so the next run tests stale code. This script clears them first, and you should
    clear them before building too.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [string] $LogDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'jgraph-lanes')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\JGraph.Tests\JGraph.Tests.csproj'
if (-not (Test-Path $project)) { throw "Test project not found at $project" }
$project = (Resolve-Path $project).Path
if (-not (Test-Path $LogDirectory)) { New-Item -ItemType Directory -Path $LogDirectory | Out-Null }

Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$failed = @()
foreach ($linalg in 'native', 'managed') {
    foreach ($packed in '1', '0') {
        $label = "linalg=$linalg packed=$packed"
        $log = Join-Path $LogDirectory "lane-$linalg-$packed.log"
        Write-Host "=== $label ===" -ForegroundColor Cyan

        $env:JGRAPH_LINALG = $linalg
        $env:JGRAPH_JGS_PACKED = $packed

        # Start-Process rather than a direct call: Windows PowerShell wraps a native command's
        # stderr lines in ErrorRecords, and with ErrorActionPreference=Stop the first xUnit [FAIL]
        # line printed to stderr would end the whole sweep on lane two of four.
        $errorLog = [System.IO.Path]::ChangeExtension($log, '.err.log')
        $run = Start-Process -FilePath 'dotnet' -Wait -NoNewWindow -PassThru `
            -ArgumentList @('test', "`"$project`"", '-c', $Configuration, '--no-build', '--nologo') `
            -RedirectStandardOutput $log -RedirectStandardError $errorLog
        $code = $run.ExitCode

        $summary = Select-String -Path $log -Pattern 'Total:\s*\d+' | Select-Object -Last 1
        $aborted = Select-String -Path $log -Pattern 'Aborted|crashed|Test Run Aborted'
        if ($summary) { Write-Host "  $($summary.Line.Trim())" }
        if ($aborted) { Write-Host "  TRUNCATED: $($aborted[0].Line.Trim())" -ForegroundColor Red }

        Select-String -Path $log -Pattern '\[FAIL\]' | Select-Object -First 40 |
            ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Yellow }

        if ($code -ne 0 -or $aborted) { $failed += $label }
        Write-Host "  log: $log"
    }
}

Remove-Item Env:JGRAPH_LINALG, Env:JGRAPH_JGS_PACKED -ErrorAction SilentlyContinue

if ($failed.Count -gt 0) {
    Write-Host "`nLanes not green: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "`nAll four lanes green." -ForegroundColor Green
