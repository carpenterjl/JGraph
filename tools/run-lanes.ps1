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
    that is neither. That line lands on stderr as often as stdout, so both logs are read.

.PARAMETER Configuration
    Debug (default) or Release. The suite must already be built for it: this does not build.

.PARAMETER LogDirectory
    Where the per-lane logs go. Defaults to a temp folder printed on the way out.

.PARAMETER Filter
    Passed to `dotnet test --filter`. For a smoke run of the four lanes over one class, not for a
    gate.

.PARAMETER NoKill
    Never stop a test host, not even one this run started and `dotnet test` left behind.

.NOTES
    `dotnet test` can leave testhost.exe processes alive after it exits, and a later build then
    reports MSB3026, silently declines to update the test assembly, and still says "Build
    succeeded" — so the next run tests stale code.

    This script stops only the test hosts it started. While each lane's `dotnet test` runs, the
    process tree under it is sampled and every testhost descendant is recorded by process id and
    creation time; when `dotnet test` has exited, any recorded host still alive with the same
    creation time is stopped. Nothing else is touched. A test host is not identifiable by name:
    several sessions run this suite from the same tree at once, and a host killed from outside
    reports "Test host process crashed" to its owner while its `dotnet test` still exits 0. If a
    build reports MSB3026, find the host holding the file and read its parent chain
    (Get-CimInstance Win32_Process: ProcessId, ParentProcessId, CommandLine) before stopping it;
    if it is not yours, wait.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [string] $LogDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'jgraph-lanes'),

    [string] $Filter,

    [switch] $NoKill
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\JGraph.Tests\JGraph.Tests.csproj'
if (-not (Test-Path $project)) { throw "Test project not found at $project" }
$project = (Resolve-Path $project).Path
if (-not (Test-Path $LogDirectory)) { New-Item -ItemType Directory -Path $LogDirectory | Out-Null }

# The test hosts under a process: every descendant of $RootId whose image name starts with $Name,
# as (ProcessId, CreationDate) pairs. Parent ids are read in one snapshot, so a chain broken by an
# already-exited intermediate is not followed; that is why the tree is sampled while the root
# runs rather than once after it has exited.
function Get-TestHostsUnder {
    param([int] $RootId, [string] $Name = 'testhost')
    $all = Get-CimInstance Win32_Process -Verbose:$false
    $children = @{}
    foreach ($p in $all) {
        if (-not $children.ContainsKey([int]$p.ParentProcessId)) { $children[[int]$p.ParentProcessId] = @() }
        $children[[int]$p.ParentProcessId] += $p
    }
    $found = @()
    $queue = [System.Collections.Generic.Queue[int]]::new()
    $queue.Enqueue($RootId)
    $seen = @{ $RootId = $true }
    while ($queue.Count -gt 0) {
        $id = $queue.Dequeue()
        if (-not $children.ContainsKey($id)) { continue }
        foreach ($c in $children[$id]) {
            if ($seen.ContainsKey([int]$c.ProcessId)) { continue }
            $seen[[int]$c.ProcessId] = $true
            if ($c.Name -like "$Name*") {
                $found += [pscustomobject]@{ ProcessId = [int]$c.ProcessId; CreationDate = $c.CreationDate }
            }
            $queue.Enqueue([int]$c.ProcessId)
        }
    }
    return $found
}

# Stops each recorded host that is still alive with the same creation time (a reused process id
# is a different process) and returns how many were stopped.
function Stop-RecordedTestHosts {
    param([hashtable] $Recorded)
    $stopped = 0
    foreach ($id in @($Recorded.Keys)) {
        $live = Get-CimInstance Win32_Process -Filter "ProcessId = $id" -Verbose:$false -ErrorAction SilentlyContinue
        if (-not $live) { continue }
        if ($live.CreationDate -ne $Recorded[$id]) { continue }
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        $stopped++
    }
    return $stopped
}

# Runs `dotnet test` with the given arguments, sampling its process tree once a second, and
# returns the exit code and the test hosts that were left running afterwards.
function Invoke-TestLane {
    param([string[]] $Arguments, [string] $OutLog, [string] $ErrLog, [bool] $Kill)
    $run = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru `
        -ArgumentList $Arguments -RedirectStandardOutput $OutLog -RedirectStandardError $ErrLog
    $null = $run.Handle   # without this, ExitCode is null after a wait that is not -Wait
    $recorded = @{}
    while (-not $run.HasExited) {
        foreach ($h in Get-TestHostsUnder -RootId $run.Id) { $recorded[[int]$h.ProcessId] = $h.CreationDate }
        $run.WaitForExit(1000) | Out-Null
    }
    $run.WaitForExit()
    $left = 0
    if ($Kill) { $left = Stop-RecordedTestHosts -Recorded $recorded }
    return [pscustomobject]@{ ExitCode = $run.ExitCode; Tracked = $recorded.Count; Stopped = $left }
}

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
        $arguments = @('test', "`"$project`"", '-c', $Configuration, '--no-build', '--nologo')
        if ($Filter) { $arguments += @('--filter', "`"$Filter`"") }
        $lane = Invoke-TestLane -Arguments $arguments -OutLog $log -ErrLog $errorLog -Kill (-not $NoKill)
        $code = $lane.ExitCode
        Write-Verbose "tracked $($lane.Tracked) test host(s) under this lane"

        $summary = Select-String -Path $log -Pattern 'Total:\s*\d+' | Select-Object -Last 1
        $aborted = Select-String -Path $log, $errorLog -Pattern 'Aborted|crashed|Test Run Aborted'
        if ($summary) { Write-Host "  $($summary.Line.Trim())" }
        if ($aborted) { Write-Host "  TRUNCATED: $($aborted[0].Line.Trim())" -ForegroundColor Red }
        if ($lane.Stopped -gt 0) {
            Write-Host "  stopped $($lane.Stopped) test host(s) this lane left behind" -ForegroundColor Yellow
        }

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
