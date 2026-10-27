<#
.SYNOPSIS
    Runs stage K16's protobuf probe against JGraph (compiler and SDK plan): the archive built by
    JGraph's mcc, served by jgraph-mps in the sessions record-k16.ps1 ran muserve in, with MathWorks'
    own client libraries against each (the Python client through src\k16client\k16client.py, the .NET
    client through src\k16dotnet, the C client through src\k16c, the Java client driven from R2025b's
    JVM through src\k16java); then compare-k16.py against R2025b's recording.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Clients
    A comma-separated list of python, dotnet, c and java.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $JGraph = '',
    [string] $JGraphMps = '',
    [int] $Port = 9981,
    [string] $Clients = 'python,dotnet,c,java',
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [string] $Csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
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
$clientList = $Clients -split ','
$mpsClients = Join-Path $MatlabRoot 'toolbox\compiler_sdk\mps_clients'

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'), @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

# --- the archive (stage K15's build, by JGraph) ------------------------------------------------------
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

# --- the compiled clients, as record-k16.ps1 compiles them ---------------------------------------------
$dotnetExe = Join-Path $WorkRoot 'dotnet\k16dotnet.exe'
if ($clientList -contains 'dotnet' -and $skipped -notcontains 'clients') {
    $folder = Split-Path $dotnetExe
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    Copy-Item (Join-Path $mpsClients 'dotnet\MathWorks.MATLAB.ProductionServer.Client.dll') $folder
    $cscOut = & $Csc /nologo /target:exe ("/out:" + $dotnetExe) ("/reference:" + (Join-Path $folder 'MathWorks.MATLAB.ProductionServer.Client.dll')) (Join-Path $here 'src\k16dotnet\k16dotnet.cs') 2>&1
    Write-Host ("k16dotnet.exe (csc)          exit {0}  exists {1}" -f $LASTEXITCODE, (Test-Path $dotnetExe))
    if (-not (Test-Path $dotnetExe)) { $cscOut; exit 1 }
}
$cExe = Join-Path $WorkRoot 'c\k16c.exe'
if ($clientList -contains 'c' -and $skipped -notcontains 'clients') {
    $folder = Split-Path $cExe
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    $cOut = & (Join-Path $here 'src\k16c\build-k16c.ps1') -Out $cExe -MpsClients $mpsClients 2>&1
    Write-Host ("k16c.exe (cl)                exists {0}" -f (Test-Path $cExe))
    if (-not (Test-Path $cExe)) { $cOut; exit 1 }
}

# --- one jgraph-mps session on a port, each client run against it, the server stopped -----------------
function Run-Server([int] $port, [string[]] $options, [string[]] $pythonModes, [string[]] $others, [string] $header) {
    $inst = Join-Path $WorkRoot "inst_$header"
    if (Test-Path $inst) { Remove-Item -LiteralPath $inst -Recurse -Force }
    & $JGraphMps new $inst --port $port | Out-Null
    $config = Join-Path $inst 'config\main_config'
    [System.IO.File]::WriteAllLines($config, (@(Get-Content $config) + $options), $utf8)
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
    $lines = @("== $header")
    if (-not $ready) {
        $lines += '  NOTREADY'
        Write-Host "jgraph-mps ($header) never deployed the archive"
        Get-Content (Join-Path $WorkRoot "server_$header.err") -ErrorAction SilentlyContinue
    }
    else {
        Write-Host ("jgraph-mps ({0}) on {1} ready after {2:n1} s" -f $header, $port, $watch.Elapsed.TotalSeconds)
        if ($clientList -contains 'python' -and $pythonModes.Count -gt 0) {
            $transcript = Join-Path $WorkRoot "python_$header.txt"
            $env:PYTHONIOENCODING = 'utf-8'
            $arguments = @('-I', "`"$(Join-Path $here 'src\k16client\k16client.py')`"", "http://localhost:$port", 'k15comp', "`"$(Join-Path $mpsClients 'python\dist')`"") + $pythonModes
            $p = Start-Process -FilePath $Python -ArgumentList $arguments -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "python_$header.err") -NoNewWindow -PassThru -Wait
            foreach ($l in (([System.IO.File]::ReadAllText($transcript, $utf8)) -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
            $lines += "  EXIT`t$($p.ExitCode)"
            Write-Host ("python {0,-12} {1} lines" -f $header, $lines.Count)
        }
        foreach ($other in $others) {
            $transcript = Join-Path $WorkRoot "${other}_$header.txt"
            switch ($other) {
                'dotnet' { $p = Start-Process -FilePath $dotnetExe -ArgumentList @("http://localhost:$port", 'k15comp') -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "dotnet_$header.err") -NoNewWindow -PassThru -Wait }
                'c' { $p = Start-Process -FilePath $cExe -ArgumentList @("http://localhost:$port", 'k15comp') -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "c_$header.err") -NoNewWindow -PassThru -Wait }
                'java' {
                    $env:K16_URL = "http://localhost:$port"
                    $env:K16_COMPONENT = 'k15comp'
                    $env:K16_JAR = (Join-Path $mpsClients 'java\mps_client.jar').Replace('\', '/')
                    $statement = "beep('off'); run('$((Join-Path $here 'src\k16java\probe_k16_java.m').Replace('\', '/'))');"
                    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $WorkRoot "java_$header.err") -NoNewWindow -PassThru -Wait
                }
            }
            $lines += "  == $other"
            foreach ($l in (([System.IO.File]::ReadAllText($transcript, $utf8)) -split "`r?`n")) { if ($l.Length -gt 0 -and $l.Contains("`t")) { $lines += "  $l" } }
            $lines += "  EXIT`t$($p.ExitCode)"
            Write-Host ("{0} {1,-12} {2} lines" -f $other, $header, $lines.Count)
        }
    }
    & $JGraphMps stop $inst | Out-Null
    try { if (-not $server.HasExited) { $server.WaitForExit(10000) | Out-Null } } catch { }
    $lines
}

$serverOut = Join-Path $WorkRoot 'k16_server.out.txt'
if ($skipped -notcontains 'server') {
    $others = @($clientList | Where-Object { $_ -in @('dotnet', 'c', 'java') })
    $all = @()
    $all += Run-Server $Port @('--cors-allowed-origins *') @('proto', 'raw') $others 'full'
    $all += Run-Server ($Port + 1) @('--request-size-limit 1KB', '--hide-matlab-error-stack') @('hidden', 'size') @() 'limited'
    if ($clientList -contains 'python') {
        # The edge cases in one session: JGraph refuses each and goes on (muserve's worker ends or hangs).
        $all += @(Run-Server ($Port + 2) @() @('edge:all') @() 'edge' | ForEach-Object { if ($_ -eq '== edge') { @('== edge', '  == edge') } else { $_ } })
    }
    [System.IO.File]::WriteAllText($serverOut, (Tokens (($all -join "`n") + "`n")), $utf8)
    Write-Host ("k16_server (jgraph)          {0} lines" -f $all.Count)
}

$recordings = Join-Path $here 'recordings-jgraph'
if (Test-Path $serverOut) { Copy-Item $serverOut (Join-Path $recordings 'k16_server.out.txt') -Force }
$report = & $Python -I (Join-Path $here 'compare-k16.py') $serverOut 2>&1 | ForEach-Object { "$_" }
[System.IO.File]::WriteAllLines((Join-Path $recordings 'k16_differences.txt'), [string[]] $report, $utf8)
$report
exit $LASTEXITCODE
