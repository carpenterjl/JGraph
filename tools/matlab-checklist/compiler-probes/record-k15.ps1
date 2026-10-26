<#
.SYNOPSIS
    Records how R2025b's muserve, the local Production Server it ships for matlabtest, answers the
    whole HTTP interface (compiler and SDK plan, stage K15). Writes recordings\k15_server.out.txt.

.DESCRIPTION
    MATLAB Production Server itself is not installed here, but R2025b carries muserve.exe
    (bin\win64), a one-archive server built from MPS's own libraries that answers the discovery,
    health and metrics API, routes files and CORS as MPS does; it has no asynchronous requests
    (400 "mode=async is not supported."), so that part of the client records the absence and the
    protocol is built from the RESTful API's documentation. The archive is built by R2025b's mcc
    from src\k14 and src\k15 with a discovery file and an archive routes file, then served in two
    sessions: one with everything on (CORS, an instance routes file, user data), one with the API
    off, a 1 KB request limit, the error stack hidden and a 15 s worker restart. The client,
    src\k15client\k15client.py, runs its modes against each; muserve is ended by its id.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build' and 'server' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

$build = Join-Path $WorkRoot 'b'
$archive = Join-Path $build 'k15comp.ctf'
if ($skipped -notcontains 'build') {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    New-Item -ItemType Directory $build | Out-Null
    $src14 = (Join-Path $here 'src\k14').Replace('\', '/')
    $src15 = (Join-Path $here 'src\k15').Replace('\', '/')
    $statement = "beep('off'); cd('$($build.Replace('\', '/'))'); mcc('-W', 'CTF:k15comp,DISCOVERY:$src15/k15sig.json,ROUTES:$src15/k15archroutes.json', '-U', '-v', " +
        "'$src14/k14add.m', '$src14/k14echo.m', '$src14/k14multi.m', '$src14/k14err.m', '$src14/k14noout.m', '$src14/k14print.m', '$src14/k14slow.m', " +
        "'$src14/k14nargs.m', '$src14/k14state.m', '$src14/k14quiet.m', '$src14/k14describe.m', '$src14/k14make.m', '$src14/k14struct.m', " +
        "'$src15/k15web.m', '$src15/k15webbad.m', '$src15/k15metric.m', '$src15/k15env.m')"
    $out = Join-Path $WorkRoot 'build.out'
    $err = Join-Path $WorkRoot 'build.err'
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { } }
    Write-Host ("k15comp.ctf (mcc)            exit {0}  exists {1}" -f $p.ExitCode, (Test-Path $archive))
    if (-not (Test-Path $archive)) { Get-Content $out; Get-Content $err; exit 1 }
}

# One muserve session: started on a free port, the client run against it, the process ended.
function Run-Server([string[]] $options, [string[]] $modes, [string] $header) {
    $folder = Join-Path $WorkRoot ("s_" + $header)
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    $arguments = @('--archive', "`"$archive`"", '--http', '0', '--pid-root', "`"$folder`"", '--endpoint-root', "`"$folder`"", '--log-severity', 'trace') + $options
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\win64\muserve.exe') -ArgumentList $arguments `
        -RedirectStandardOutput (Join-Path $folder 'muserve.out') -RedirectStandardError (Join-Path $folder 'muserve.err') -NoNewWindow -PassThru
    $endpoint = Join-Path $folder 'http'
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $endpoint) -and $watch.Elapsed.TotalSeconds -lt 300 -and -not $p.HasExited) { Start-Sleep -Milliseconds 300 }
    $lines = @("== $header")
    if ($p.HasExited -or -not (Test-Path $endpoint)) {
        Write-Host "muserve ($header) never became ready"
        $lines += '  NOTREADY'
        return $lines
    }
    $port = ((Get-Content $endpoint -Raw).Trim() -split ':')[-1]
    Write-Host ("muserve ({0}) on {1} ready after {2:n1} s (pid {3})" -f $header, $port, $watch.Elapsed.TotalSeconds, $p.Id)
    Start-Sleep -Seconds 2
    try {
        $transcript = Join-Path $folder 'client.txt'
        $env:PYTHONIOENCODING = 'utf-8'
        $args = @('-I', "`"$(Join-Path $here 'src\k15client\k15client.py')`"", "http://localhost:$port", 'k15comp') + $modes
        $c = Start-Process -FilePath $Python -ArgumentList $args -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'client.err') -NoNewWindow -PassThru -Wait
        $text = [System.IO.File]::ReadAllText($transcript, $utf8)
        foreach ($l in ($text -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
        $lines += "  EXIT`t$($c.ExitCode)"
        Write-Host ("client {0,-12} {1} lines" -f $header, $lines.Count)
    }
    finally {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Seconds 2
        Get-Process -Name 'mps_worker_app' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    $lines
}

if ($skipped -notcontains 'server') {
    $src15 = Join-Path $here 'src\k15'
    $all = @()
    $all += Run-Server @('--cors-allowed-origins', '*', '--routes-file', "`"$(Join-Path $src15 'k15routes.json')`"", '--user-data', '"k15key k15value"') `
        @('sync', 'cors', 'api', 'async', 'routes', 'metrics', 'env') 'full'
    # muserve refuses every form of --worker-restart-interval ("bad lexical cast"), so the restart is not measured.
    $all += Run-Server @('--disable-discovery', '--disable-metrics', '--request-size-limit', '1KB', '--hide-matlab-error-stack') `
        @('disabled', 'size', 'hidden', 'routes') 'limited'
    $text = ($all -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k15_server.out.txt'), (Tokens $text), $utf8)
    Write-Host ("k15_server.out.txt           {0} lines" -f $all.Count)
}
