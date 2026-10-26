<#
.SYNOPSIS
    Runs stage K15's server probe against JGraph (compiler and SDK plan): the same archive built by
    JGraph's mcc, served by jgraph-mps in the two sessions record-k15.ps1 ran muserve in, with
    src\k15client\k15client.py against each; then compare-k15.py against R2025b's recording.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER JGraph
    jgraph.exe to build the archive with (the Debug build when not given).

.PARAMETER JGraphMps
    jgraph-mps.exe (the Debug build when not given).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $JGraph = '',
    [string] $JGraphMps = '',
    [int] $Port = 9971,
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

$src14 = Join-Path $here 'src\k14'
$src15 = Join-Path $here 'src\k15'
$build = Join-Path $WorkRoot 'b'
$archive = Join-Path $build 'k15comp.ctf'
if ($skipped -notcontains 'build') {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    New-Item -ItemType Directory $build | Out-Null
    $files = @('k14add', 'k14echo', 'k14multi', 'k14err', 'k14noout', 'k14print', 'k14slow', 'k14nargs', 'k14state', 'k14quiet', 'k14describe', 'k14make', 'k14struct') |
        ForEach-Object { "'" + (Join-Path $src14 "$_.m").Replace('\', '/') + "'" }
    $files += @('k15web', 'k15webbad', 'k15metric', 'k15env') | ForEach-Object { "'" + (Join-Path $src15 "$_.m").Replace('\', '/') + "'" }
    $statement = "mcc('-W', 'CTF:k15comp,DISCOVERY:$((Join-Path $src15 'k15sig.json').Replace('\', '/')),ROUTES:$((Join-Path $src15 'k15archroutes.json').Replace('\', '/'))', '-U', '-d', '$($build.Replace('\', '/'))', $($files -join ', '));"
    $script = Join-Path $WorkRoot 'build_k15comp.m'
    [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
    $p = Start-Process -FilePath $JGraph -ArgumentList @('-batch', "`"run('$($script.Replace('\', '/'))')`"") -RedirectStandardOutput (Join-Path $WorkRoot 'build.stdout') -RedirectStandardError (Join-Path $WorkRoot 'build.stderr') -NoNewWindow -PassThru -Wait
    Write-Host ("k15comp.ctf (jgraph)         exit {0}  exists {1}" -f $p.ExitCode, (Test-Path $archive))
    if (-not (Test-Path $archive)) { Get-Content (Join-Path $WorkRoot 'build.stdout'); Get-Content (Join-Path $WorkRoot 'build.stderr'); exit 1 }
}

function Run-Server([int] $port, [string[]] $options, [string[]] $modes, [string] $header) {
    $inst = Join-Path $WorkRoot "inst_$header"
    if (Test-Path $inst) { Remove-Item -LiteralPath $inst -Recurse -Force }
    & $JGraphMps new $inst --port $port | Out-Null
    $config = Join-Path $inst 'config\main_config'
    $lines = @(Get-Content $config) + $options
    [System.IO.File]::WriteAllLines($config, $lines, $utf8)
    Copy-Item $archive (Join-Path $inst 'auto_deploy\k15comp.ctf')
    $log = Join-Path $WorkRoot "server_$header.log"
    $server = Start-Process -FilePath $JGraphMps -ArgumentList @('run', "`"$inst`"") -RedirectStandardOutput $log -RedirectStandardError (Join-Path $WorkRoot "server_$header.err") -NoNewWindow -PassThru
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    while (-not $ready -and $watch.Elapsed.TotalSeconds -lt 120 -and -not $server.HasExited) {
        Start-Sleep -Milliseconds 500
        $text = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
        $ready = $text -and $text.Contains('k15comp: deployed')
    }
    $out = @("== $header")
    if (-not $ready) {
        $out += '  NOTREADY'
        Write-Host "jgraph-mps ($header) never deployed the archive"
        Get-Content (Join-Path $WorkRoot "server_$header.err") -ErrorAction SilentlyContinue
    }
    else {
        Write-Host ("jgraph-mps ({0}) on {1} ready after {2:n1} s" -f $header, $port, $watch.Elapsed.TotalSeconds)
        $transcript = Join-Path $WorkRoot "client_$header.txt"
        $env:PYTHONIOENCODING = 'utf-8'
        $arguments = @('-I', "`"$(Join-Path $here 'src\k15client\k15client.py')`"", "http://localhost:$port", 'k15comp') + $modes
        $p = Start-Process -FilePath $Python -ArgumentList $arguments -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "client_$header.err") -NoNewWindow -PassThru -Wait
        foreach ($l in (([System.IO.File]::ReadAllText($transcript, $utf8)) -split "`r?`n")) { if ($l.Length -gt 0) { $out += "  $l" } }
        $out += "  EXIT`t$($p.ExitCode)"
        Write-Host ("client {0,-12} {1} lines" -f $header, $out.Count)
    }
    try { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue } catch { }
    Start-Sleep -Seconds 1
    Get-Process -Name 'jgraph-mps' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-30) } | Stop-Process -Force -ErrorAction SilentlyContinue
    $out
}

$serverOut = Join-Path $WorkRoot 'k15_server.out.txt'
if ($skipped -notcontains 'server') {
    $all = @()
    $all += Run-Server $Port @('--cors-allowed-origins *', '--enable-discovery', '--enable-metrics', "--routes-file $(Join-Path $src15 'k15routes.json')") `
        @('sync', 'cors', 'api', 'async', 'routes', 'metrics', 'env') 'full'
    $all += Run-Server ($Port + 1) @('--request-size-limit 1KB', '--hide-matlab-error-stack') @('disabled', 'size', 'hidden', 'routes') 'limited'
    [System.IO.File]::WriteAllText($serverOut, (Tokens (($all -join "`n") + "`n")), $utf8)
    Write-Host ("k15_server (jgraph)          {0} lines" -f $all.Count)
}

$recordings = Join-Path $here 'recordings-jgraph'
if (Test-Path $serverOut) { Copy-Item $serverOut (Join-Path $recordings 'k15_server.out.txt') -Force }
$report = & $Python -I (Join-Path $here 'compare-k15.py') $serverOut 2>&1 | ForEach-Object { "$_" }
[System.IO.File]::WriteAllLines((Join-Path $recordings 'k15_differences.txt'), [string[]] $report, $utf8)
$report
exit $LASTEXITCODE
