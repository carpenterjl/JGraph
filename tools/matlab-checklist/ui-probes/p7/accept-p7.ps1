<#
.SYNOPSIS
    The acceptance of the packages and class folders plan (stage P7, ADR 0313): the library under
    lib\ (+signal with its sub-package, handle class, script and private folder; @Series with its
    method files and private folder) run by the top-level script, built with mcc and run as a
    program, and opened as a project, in R2025b (-Engine R2025b) or in JGraph (-Engine JGraph),
    so that the two engines' transcripts compare line for line (compare-p7.py).

.DESCRIPTION
    Steps (each can be left out with -Skip, or chosen alone with -Only):

      run       p7_accept.m: the library through p7_main.m, then the tools over it  -> p7_accept.out
      build     p7_build.m: mcc -m p7_tool.m under <WorkRoot>\build; the archive's entries and
                its manifest                                                        -> p7_build.out
      tool      the built p7_tool.exe run with the trace file and a factor, through
                ..\..\compiler-probes\run-quiet.ps1                                 -> p7_tool.out
      project   p7_project.m: the library as a MATLAB project under <WorkRoot>\proj -> p7_project.out
      compare   compare-p7.py (JGraph only)                                         -> p7_differences.txt

    JGraph's outputs are written as <name>.jgraph.out beside R2025b's.

.PARAMETER WorkRoot
    A scratch folder outside the repo for the build and the project.

.PARAMETER Runtime
    JGraph: a JGraph Runtime laid out by installer\publish-runtime.ps1 (the built program runs
    under it, and its jgraph.exe builds unless -Jgraph names another).
#>
[CmdletBinding()]
param(
    [ValidateSet('R2025b', 'JGraph')] [string] $Engine = 'R2025b',
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Skip = '',
    [string] $Only = '',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$probes = (Resolve-Path -LiteralPath (Join-Path $here '..\..\compiler-probes')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$jg = $Engine -eq 'JGraph'
$suffix = if ($jg) { '.jgraph.out' } else { '.out' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$onlySteps = @($Only -split ',' | Where-Object { $_ })
function Wanted([string] $step) { ($skipped -notcontains $step) -and ($onlySteps.Count -eq 0 -or $onlySteps -contains $step) }
$slash = { param($p) $p.Replace('\', '/') }

if ($jg) {
    if (-not $Runtime) { throw 'JGraph needs -Runtime, a JGraph Runtime laid out by installer\publish-runtime.ps1.' }
    $Runtime = (Resolve-Path -LiteralPath $Runtime).Path
    if (-not $Jgraph) { $Jgraph = Join-Path $Runtime 'bin\win64\jgraph.exe' }
    $Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
    $env:JGRAPH_RUNTIME_ROOT = $Runtime
}

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($here, '<P7>'), @($env:USERPROFILE, '<HOME>'))
    if ($jg) { $tokens += , @($Runtime, '<RUNTIME>') }
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text
}

function Save([string] $text, [string] $stem) {
    $lines = ($text -replace "`r`n", "`n").TrimEnd("`n")
    [System.IO.File]::WriteAllText((Join-Path $here ($stem + $suffix)), (Tokens $lines) + "`n", $utf8)
    Write-Host ("{0,-24} {1} lines" -f ($stem + $suffix), ($lines -split "`n").Count)
}

# The engine running a statement in batch mode: its text, STDERR lines and EXIT line.
function Run-Engine([string] $statement) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        if ($jg) {
            $script = Join-Path $WorkRoot 'statement.m'
            [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
            $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"run('$(& $slash $script)')`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        }
        else {
            $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        }
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`nEXIT`t$($p.ExitCode)"
        $text
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

$cd = "cd('$(& $slash $here)'); "
$work = & $slash $WorkRoot

if (Wanted 'run') { Save (Run-Engine ($cd + 'p7_accept')) 'p7_accept' }
if (Wanted 'build') { Save (Run-Engine ($cd + "p7_build('$work')")) 'p7_build' }

if (Wanted 'tool') {
    $exe = Join-Path $WorkRoot 'build\p7_tool.exe'
    if (-not (Test-Path $exe)) { Save "tool`tnot built" 'p7_tool' }
    else {
        $transcript = [System.IO.Path]::GetTempFileName()
        $json = ConvertTo-Json -Compress -InputObject @((Join-Path $here 'rt\trace.txt'), '2')
        $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); Environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache') };
            TimeoutSeconds = 600; OutFile = $transcript; WorkingDirectory = (Join-Path $WorkRoot 'build'); MatlabRoot = $MatlabRoot }
        if ($jg) { $quiet.PathFirst = @(Join-Path $Runtime 'runtime\win64'); $quiet.NoMatlabRuntime = $true; $quiet.Environment['JGRAPH_RUNTIME_ROOT'] = $Runtime }
        & (Join-Path $probes 'run-quiet.ps1') @quiet *> $null
        $text = (([System.IO.File]::ReadAllText($transcript) -split "`r?`n") | Where-Object { $_.Length -gt 0 -and $_ -notlike 'SECONDS*' }) -join "`n"
        Remove-Item -LiteralPath $transcript
        Save $text 'p7_tool'
    }
}

if (Wanted 'project') { Save (Run-Engine ($cd + "p7_project('$work')")) 'p7_project' }

if ($jg -and (Wanted 'compare')) {
    & $Python -I (Join-Path $here 'compare-p7.py')
    exit $LASTEXITCODE
}
