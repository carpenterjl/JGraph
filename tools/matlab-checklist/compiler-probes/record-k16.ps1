<#
.SYNOPSIS
    Records how R2025b's muserve answers MATLAB Production Server's protobuf interface to MathWorks'
    own client libraries (compiler and SDK plan, stage K16). Writes recordings\k16_server.out.txt.

.DESCRIPTION
    The archive is stage K15's, k15comp.ctf, built by R2025b's mcc from src\k14 and src\k15 (or an
    archive given with -Archive), served by muserve.exe (bin\win64) in two sessions: a full one and
    one with --hide-matlab-error-stack and a 1 KB request limit. Against each run the clients named by
    -Clients: python is src\k16client\k16client.py over MathWorks' Python client package
    (toolbox\compiler_sdk\mps_clients\python\dist), modes proto and raw then hidden and size; dotnet is
    src\k16dotnet\k16dotnet.cs compiled with the .NET Framework's csc.exe against MathWorks' .NET
    client assembly; c is src\k16c\k16c.c compiled with MSVC against MathWorks' C client library; java
    is src\k16java\probe_k16_java.m, MathWorks' Java client driven from R2025b's own JVM under
    matlab -batch. muserve is ended by its id.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Archive
    A k15comp.ctf built by R2025b to serve; built here when not given.

.PARAMETER Clients
    A comma-separated list of python, dotnet, c and java.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Archive = '',
    [string] $Clients = 'python,dotnet,c,java',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [string] $Csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$clientList = $Clients -split ','
$mpsClients = Join-Path $MatlabRoot 'toolbox\compiler_sdk\mps_clients'

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

# --- the archive (stage K15's build) ---------------------------------------------------------------------
$build = Join-Path $WorkRoot 'b'
if ($Archive -and (Test-Path $Archive)) {
    $archive = $Archive
    Write-Host "k15comp.ctf                  reused $archive"
}
else {
    $archive = Join-Path $build 'k15comp.ctf'
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

# --- the compiled clients ------------------------------------------------------------------------------
$dotnetExe = Join-Path $WorkRoot 'dotnet\k16dotnet.exe'
if ($clientList -contains 'dotnet') {
    $folder = Split-Path $dotnetExe
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    $clientDll = Join-Path $mpsClients 'dotnet\MathWorks.MATLAB.ProductionServer.Client.dll'
    Copy-Item $clientDll $folder
    $cscOut = & $Csc /nologo /target:exe ("/out:" + $dotnetExe) ("/reference:" + (Join-Path $folder 'MathWorks.MATLAB.ProductionServer.Client.dll')) `
        (Join-Path $here 'src\k16dotnet\k16dotnet.cs') 2>&1
    Write-Host ("k16dotnet.exe (csc)          exit {0}  exists {1}" -f $LASTEXITCODE, (Test-Path $dotnetExe))
    if (-not (Test-Path $dotnetExe)) { $cscOut; exit 1 }
}

$cExe = Join-Path $WorkRoot 'c\k16c.exe'
if ($clientList -contains 'c') {
    $folder = Split-Path $cExe
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    $cOut = & (Join-Path $here 'src\k16c\build-k16c.ps1') -Out $cExe -MpsClients $mpsClients 2>&1
    Write-Host ("k16c.exe (cl)                exists {0}" -f (Test-Path $cExe))
    if (-not (Test-Path $cExe)) { $cOut; exit 1 }
}

# --- one muserve session: started on a free port, each client run against it, the process ended ----------
function Run-Server([string[]] $options, [string[]] $pythonModes, [string[]] $others, [string] $header) {
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
        if ($clientList -contains 'python' -and $pythonModes.Count -gt 0) {
            $transcript = Join-Path $folder 'python.txt'
            $env:PYTHONIOENCODING = 'utf-8'
            $args = @('-I', "`"$(Join-Path $here 'src\k16client\k16client.py')`"", "http://localhost:$port", 'k15comp', "`"$(Join-Path $mpsClients 'python\dist')`"") + $pythonModes
            $c = Start-Process -FilePath $Python -ArgumentList $args -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'python.err') -NoNewWindow -PassThru -Wait
            $text = [System.IO.File]::ReadAllText($transcript, $utf8)
            foreach ($l in ($text -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
            $lines += "  EXIT`t$($c.ExitCode)"
            Write-Host ("python {0,-12} {1} lines" -f $header, $lines.Count)
        }
        foreach ($other in $others) {
            $transcript = Join-Path $folder "$other.txt"
            switch ($other) {
                'dotnet' { $c = Start-Process -FilePath $dotnetExe -ArgumentList @("http://localhost:$port", 'k15comp') -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'dotnet.err') -NoNewWindow -PassThru -Wait }
                'c' { $c = Start-Process -FilePath $cExe -ArgumentList @("http://localhost:$port", 'k15comp') -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'c.err') -NoNewWindow -PassThru -Wait }
                'java' {
                    $script = (Join-Path $here 'src\k16java\probe_k16_java.m').Replace('\', '/')
                    $jar = (Join-Path $mpsClients 'java\mps_client.jar').Replace('\', '/')
                    $statement = "beep('off'); run('$script');"
                    $env:K16_URL = "http://localhost:$port"
                    $env:K16_COMPONENT = 'k15comp'
                    $env:K16_JAR = $jar
                    $c = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
                        -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'java.err') -NoNewWindow -PassThru -Wait
                }
            }
            $lines += "  == $other"
            $text = [System.IO.File]::ReadAllText($transcript, $utf8)
            foreach ($l in ($text -split "`r?`n")) { if ($l.Length -gt 0 -and $l.Contains("`t")) { $lines += "  $l" } }
            $lines += "  EXIT`t$($c.ExitCode)"
            Write-Host ("{0} {1,-12} {2} lines" -f $other, $header, $lines.Count)
        }
    }
    finally {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Seconds 2
        Get-Process -Name 'mps_worker_app' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    $lines
}

$others = @($clientList | Where-Object { $_ -in @('dotnet', 'c', 'java') })
$all = @()
$all += Run-Server @('--cors-allowed-origins', '*') @('proto', 'raw') $others 'full'
$all += Run-Server @('--request-size-limit', '1KB', '--hide-matlab-error-stack') @('hidden', 'size') @() 'limited'
if ($clientList -contains 'python') {
    # The messages that end or hang muserve's worker, each against a session of its own; recorded as one section.
    $count = [int] ((& $Python -I (Join-Path $here 'src\k16client\k16client.py') 'http://localhost:1' 'k15comp' (Join-Path $mpsClients 'python\dist') 'edge:count') -split "`t")[-1]
    $edge = @('== edge', '  == edge')
    for ($i = 0; $i -lt $count; $i++) {
        $lines = Run-Server @() @("edge:$i") @() "edge$i"
        $edge += @($lines | Where-Object { $_.Contains("`t") -and -not $_.StartsWith("  EXIT") })
    }
    $all += $edge
}
$text = ($all -join "`n") + "`n"
[System.IO.File]::WriteAllText((Join-Path $recordings 'k16_server.out.txt'), (Tokens $text), $utf8)
Write-Host ("k16_server.out.txt           {0} lines" -f $all.Count)
