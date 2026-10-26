<#
.SYNOPSIS
    Runs stage K14's probes against JGraph (compiler and SDK plan): probe_k14_build under jgraph -batch,
    and src\k14client\k14client.py against a JGraph Production Server (jgraph-mps) serving the archive
    JGraph built, then compare-k14.py against R2025b's recordings.

.DESCRIPTION
    The build probe runs unchanged under jgraph -batch into <WorkRoot>\b; its output goes to
    <WorkRoot>\k14_build.out.txt with the same tokens record-k14.ps1 uses. Then a server instance is
    made in <WorkRoot>\inst with jgraph-mps new, the main build's archive (k14arch.ctf, renamed
    k14comp.ctf so the client's component name is R2025b's) is copied into its auto_deploy folder,
    jgraph-mps run serves it, and the client runs its sync, cors and api modes against it twice: once
    with CORS and discovery on, once with both off, as the recording has them.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER JGraph
    jgraph.exe to run the probe with (the Debug build when not given).

.PARAMETER JGraphMps
    jgraph-mps.exe (the Debug build when not given).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $JGraph = '',
    [string] $JGraphMps = '',
    [int] $Port = 9961,
    [string] $Skip = '',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe')
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
if (-not $JGraph) { $JGraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
if (-not $JGraphMps) { $JGraphMps = Join-Path $repoRoot 'src\JGraph.ProductionServer\bin\Debug\net8.0\jgraph-mps.exe' }
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($repoRoot, '<REPO>'), @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

$buildOut = Join-Path $WorkRoot 'k14_build.out.txt'
$serverOut = Join-Path $WorkRoot 'k14_server.out.txt'
$build = Join-Path $WorkRoot 'b'

if ($skipped -notcontains 'build') {
    $statement = "cd('$($here.Replace('\', '/'))'); probe_k14_build('$($build.Replace('\', '/'))')"
    $out = Join-Path $WorkRoot 'build.stdout'
    $err = Join-Path $WorkRoot 'build.stderr'
    $p = Start-Process -FilePath $JGraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru -Wait
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText($buildOut, (Tokens $text), $utf8)
    Write-Host ("k14_build (jgraph)           exit {0}" -f $p.ExitCode)
}

$routesOut = Join-Path $WorkRoot 'k14_routes.out.txt'
if ($skipped -notcontains 'routes') {
    $statement = "cd('$($here.Replace('\', '/'))'); probe_k14_routes('$((Join-Path $WorkRoot 'r').Replace('\', '/'))')"
    $out = Join-Path $WorkRoot 'routes.stdout'
    $err = Join-Path $WorkRoot 'routes.stderr'
    $p = Start-Process -FilePath $JGraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru -Wait
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText($routesOut, (Tokens $text), $utf8)
    Write-Host ("k14_routes (jgraph)          exit {0}" -f $p.ExitCode)
}

# The archive the server probe's functions are deployed as: the twelve functions the in-MATLAB server
# exported, with the discovery file it was given, so GET /api/discovery has the same to describe.
$comp = Join-Path $WorkRoot 'comp'
if ($skipped -notcontains 'server') {
    $src = Join-Path $here 'src\k14'
    $files = @('k14add', 'k14echo', 'k14multi', 'k14err', 'k14noout', 'k14print', 'k14slow', 'k14nargs', 'k14state', 'k14quiet', 'k14describe', 'k14make') |
        ForEach-Object { "'" + (Join-Path $src "$_.m").Replace('\', '/') + "'" }
    # A dotted name is MATLAB dialect, which -batch reads from a file, not from its statement.
    $statement = "compiler.build.productionServerArchive({$($files -join ', ')}, 'ArchiveName', 'k14comp', 'FunctionSignatures', '$((Join-Path $src 'k14sig.json').Replace('\', '/'))', 'OutputDir', '$($comp.Replace('\', '/'))');"
    $script = Join-Path $WorkRoot 'build_k14comp.m'
    [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
    $p = Start-Process -FilePath $JGraph -ArgumentList @('-batch', "`"run('$($script.Replace('\', '/'))')`"") -RedirectStandardOutput (Join-Path $WorkRoot 'comp.stdout') -RedirectStandardError (Join-Path $WorkRoot 'comp.stderr') -NoNewWindow -PassThru -Wait
    Write-Host ("k14comp.ctf (jgraph)         exit {0}" -f $p.ExitCode)
}

function Run-Server([int] $port, [bool] $cors, [bool] $discovery, [string[]] $modes, [string] $header) {
    $inst = Join-Path $WorkRoot "inst_$port"
    if (Test-Path $inst) { Remove-Item -LiteralPath $inst -Recurse -Force }
    & $JGraphMps new $inst --port $port | Out-Null
    $config = Join-Path $inst 'config\main_config'
    $lines = @(Get-Content $config)
    if ($cors) { $lines += '--cors-allowed-origins *' }
    if ($discovery) { $lines += '--enable-discovery' }
    [System.IO.File]::WriteAllLines($config, $lines, $utf8)
    Copy-Item (Join-Path $comp 'k14comp.ctf') (Join-Path $inst 'auto_deploy\k14comp.ctf')
    $log = Join-Path $WorkRoot "server_$port.log"
    $server = Start-Process -FilePath $JGraphMps -ArgumentList @('run', "`"$inst`"") -RedirectStandardOutput $log -RedirectStandardError (Join-Path $WorkRoot "server_$port.err") -NoNewWindow -PassThru
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    while (-not $ready -and $watch.Elapsed.TotalSeconds -lt 120) {
        Start-Sleep -Milliseconds 500
        $text = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
        $ready = $text -and $text.Contains('k14comp: deployed')
    }
    $lines = @("== $header")
    if (-not $ready) {
        $lines += '  NOTREADY'
        Write-Host "jgraph-mps on $port never deployed the archive"
    }
    else {
        Write-Host ("jgraph-mps on {0} ready after {1:n1} s" -f $port, $watch.Elapsed.TotalSeconds)
        $transcript = Join-Path $WorkRoot "client_$port.txt"
        $env:PYTHONIOENCODING = 'utf-8'
        $arguments = @('-I', "`"$(Join-Path $here 'src\k14client\k14client.py')`"", "http://localhost:$port", 'k14comp') + $modes
        $p = Start-Process -FilePath $Python -ArgumentList $arguments -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "client_$port.err") -NoNewWindow -PassThru -Wait
        foreach ($l in (([System.IO.File]::ReadAllText($transcript, $utf8)) -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
        $lines += "  EXIT`t$($p.ExitCode)"
        Write-Host ("client {0,-24} {1} lines" -f $header, $lines.Count)
    }
    try { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue } catch { }
    Get-Process -Name 'jgraph-mps' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-30) } | Stop-Process -Force -ErrorAction SilentlyContinue
    $lines
}

if ($skipped -notcontains 'server') {
    $all = @()
    $all += Run-Server $Port $true $true @('sync', 'cors', 'api') 'cors+discovery'
    $all += Run-Server ($Port + 1) $false $false @('cors', 'api') 'plain'
    [System.IO.File]::WriteAllText($serverOut, (Tokens (($all -join "`n") + "`n")), $utf8)
    Write-Host ("k14_server (jgraph)          {0} lines" -f $all.Count)
}

# JGraph's recordings beside the other stages', and the comparison's report.
$recordings = Join-Path $here 'recordings-jgraph'
foreach ($out in $buildOut, $serverOut, $routesOut) {
    if (Test-Path $out) { Copy-Item $out (Join-Path $recordings (Split-Path $out -Leaf)) -Force }
}
$report = & $Python -I (Join-Path $here 'compare-k14.py') $buildOut $serverOut $routesOut 2>&1 | ForEach-Object { "$_" }
[System.IO.File]::WriteAllLines((Join-Path $recordings 'k14_differences.txt'), [string[]] $report, $utf8)
$report
exit $LASTEXITCODE
