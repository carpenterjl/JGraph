<#
.SYNOPSIS
    The acceptance set of the compiler and SDK plan (stage K22): one small program per target, built
    from src\k22 by R2025b (-Engine R2025b) or by JGraph (-Engine JGraph), and each exercised by the
    same client program, so the two engines' transcripts compare line for line. Writes
    recordings\k22_build.out.txt and k22_accept.out.txt for R2025b, recordings-jgraph\ for JGraph,
    and for JGraph the comparison recordings-jgraph\k22_differences.txt (compare-k22.py).

.DESCRIPTION
    Steps (each can be left out with -Skip, or chosen alone with -Only):

      build     build_k22.m, the same file under matlab -batch -noFigureWindows or jgraph -batch:
                the console tool with its data file, the windowed app, the .NET assembly for the
                MWArray API, the .NET archive for the MATLAB Data API with its strongly typed
                interface, the C and C++ shared libraries, the Python package, the Production
                Server archive with its discovery file, and the Excel add-in (R2025b builds it;
                JGraph has no such function until stage K17)
      console   acc_tool.exe with arguments and on its error, through run-quiet.ps1
      windowed  acc_app.exe watched by k6-windows.ps1, its Compute button pressed through UI
                Automation's InvokePattern. JGraph's window runs as JGraph windows do; R2025b's
                is a MATLAB window and runs only with -AllowMatlabWindows, with the user's leave
      net       src\k22client\accnetclient built with dotnet against the engine's MWArray assembly
      data      src\k22client\accdataclient built with dotnet against the engine's MATLAB Data API
      c         src\k22client\acc_c.c compiled with MSVC against the generated acclib.h
      cpp       src\k22client\acc_cpp.cpp compiled with MSVC against the generated acccppv2.hpp
      python    src\k22client\acc_py.py under CPython with the package on PYTHONPATH
      server    src\k22client\acc_server.py against R2025b's muserve or jgraph-mps serving accarch.ctf
      compare   compare-k22.py (JGraph only)

    A client's using lines name MathWorks' namespaces; for JGraph they are turned to JGraph's
    before the build, and nothing else in any client changes between the engines.

.PARAMETER WorkRoot
    A scratch folder outside the repo. The builds go in <WorkRoot>\b.

.PARAMETER Runtime
    JGraph: a JGraph Runtime laid out by installer\publish-runtime.ps1.

.PARAMETER Jgraph
    JGraph: the jgraph.exe that builds; the runtime's when not given.

.PARAMETER JgraphMps
    JGraph: jgraph-mps.exe; the runtime's when not given.

.PARAMETER AllowMatlabWindows
    R2025b: run the windowed program. Only with the user's leave.
#>
[CmdletBinding()]
param(
    [ValidateSet('R2025b', 'JGraph')] [string] $Engine = 'R2025b',
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $JgraphMps,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Skip = '',
    [string] $Only = '',
    [switch] $AllowMatlabWindows,
    [int] $Port = 9981,
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$jg = $Engine -eq 'JGraph'
$recordings = Join-Path $here $(if ($jg) { 'recordings-jgraph' } else { 'recordings' })
New-Item -ItemType Directory -Force $recordings | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$onlySteps = @($Only -split ',' | Where-Object { $_ })
function Wanted([string] $step) { ($skipped -notcontains $step) -and ($onlySteps.Count -eq 0 -or $onlySteps -contains $step) }
$build = Join-Path $WorkRoot 'b'
$cache = Join-Path $WorkRoot 'cache'
$slash = { param($p) $p.Replace('\', '/') }

if ($jg) {
    if (-not $Runtime) { throw 'JGraph needs -Runtime, a JGraph Runtime laid out by installer\publish-runtime.ps1.' }
    $Runtime = (Resolve-Path -LiteralPath $Runtime).Path
    if (-not $Jgraph) { $Jgraph = Join-Path $Runtime 'bin\win64\jgraph.exe' }
    if (-not $JgraphMps) { $JgraphMps = Join-Path $Runtime 'bin\win64\jgraph-mps.exe' }
    $Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
    $JgraphMps = (Resolve-Path -LiteralPath $JgraphMps).Path
    $env:JGRAPH_RUNTIME_ROOT = $Runtime
    # The two .NET API assemblies alone in their folders, as a client references them: the client's
    # build then copies them and no engine assembly, and finds the engine in the runtime at run time.
    $arrays = Join-Path $Runtime 'toolbox\dotnetbuilder\bin\win64\net8.0\JGraph.MATLAB.NET.Arrays.dll'
    if (-not (Test-Path $arrays)) { New-Item -ItemType Directory -Force (Split-Path $arrays) | Out-Null; Copy-Item (Join-Path $Runtime 'bin\win64\JGraph.MATLAB.NET.Arrays.dll') $arrays }
    $dataApi = Join-Path $Runtime 'extern\dotnet\net8.0\JGraph.MATLAB.Runtime.dll'
    if (-not (Test-Path $dataApi)) { New-Item -ItemType Directory -Force (Split-Path $dataApi) | Out-Null; Copy-Item (Join-Path $Runtime 'bin\win64\JGraph.MATLAB.Runtime.dll') $dataApi }
}

function Tokens([string] $text) {
    $tokens = @(@($build, '<B>'), @($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    if ($jg) { $tokens += , @($Runtime, '<RUNTIME>') }
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Save([string[]] $lines, [string] $target) {
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-24} {1} lines" -f $target, $lines.Count)
}

# The recording's sections ("== header" and the lines under it): the ones just run replace their old
# copies and new ones go at the end, so one step can be run again on its own.
function Merge([string[]] $lines, [string] $target) {
    $path = Join-Path $recordings $target
    $order = New-Object System.Collections.Generic.List[string]
    $sections = @{}
    $current = $null
    if (Test-Path $path) {
        foreach ($l in [System.IO.File]::ReadAllLines($path)) {
            if ($l.StartsWith('== ')) { $current = $l; $order.Add($l); $sections[$l] = New-Object System.Collections.Generic.List[string] }
            if ($current) { $sections[$current].Add($l) }
        }
    }
    $current = $null
    $fresh = @{}
    foreach ($l in (Tokens ($lines -join "`n")) -split "`n") {
        if ($l.StartsWith('== ')) { $current = $l; if (-not $order.Contains($l)) { $order.Add($l) }; $fresh[$l] = New-Object System.Collections.Generic.List[string] }
        if ($current) { $fresh[$current].Add($l) }
    }
    $all = @()
    foreach ($h in $order) { $all += @($(if ($fresh.ContainsKey($h)) { $fresh[$h] } else { $sections[$h] })) }
    Save $all $target
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

# One program through run-quiet.ps1: its transcript's lines, indented under a header line.
function Run-Quiet([string] $header, [string] $exe, [string[]] $arguments, [string] $folder, [hashtable] $environment = @{}, [int] $timeout = 600) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject @($arguments)
    $env2 = @{ MCR_CACHE_ROOT = $cache }
    foreach ($k in $environment.Keys) { $env2[$k] = $environment[$k] }
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); Environment = $env2;
        TimeoutSeconds = $timeout; OutFile = $transcript; WorkingDirectory = $folder; MatlabRoot = $MatlabRoot }
    if ($jg) { $quiet.PathFirst = @(Join-Path $Runtime 'runtime\win64'); $quiet.NoMatlabRuntime = $true; $env2['JGRAPH_RUNTIME_ROOT'] = $Runtime }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $lines = @("== $header")
    foreach ($l in ($out -split "`r?`n")) { if ($l.Length -gt 0 -and $l -notlike 'SECONDS*') { $lines += "  $l" } }
    Write-Host ("  {0,-20} {1} lines" -f $header, ($lines.Count - 1))
    $lines
}

function Invoke-Msvc([string[]] $commands, [string] $log) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'MSVC was not found (vswhere found no installation with the x64 C++ tools).' }
    $cmd = Join-Path $WorkRoot ('msvc_' + [guid]::NewGuid().ToString('N') + '.cmd')
    $lines = @('@echo off', "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul") +
        @($commands | ForEach-Object { "$_ || exit /b 1" })
    [System.IO.File]::WriteAllLines($cmd, $lines)
    & cmd.exe /c $cmd *> $log
    $code = $LASTEXITCODE
    Remove-Item -LiteralPath $cmd -ErrorAction SilentlyContinue
    $code -eq 0
}

# A .NET client built from a copy of its source, the using lines turned to JGraph's for JGraph.
function Build-DotNet([string] $name, [string] $outFolder, [string[]] $properties) {
    $src = Join-Path $WorkRoot "src_$name"
    if (Test-Path $src) { Remove-Item -LiteralPath $src -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here "src\k22client\$name") -Destination $src -Recurse
    if ($jg) {
        $program = Join-Path $src 'Program.cs'
        $text = [System.IO.File]::ReadAllText($program).Replace('using MathWorks.MATLAB.', 'using JGraph.MATLAB.')
        [System.IO.File]::WriteAllText($program, $text, $utf8)
    }
    if (Test-Path $outFolder) { Remove-Item -LiteralPath $outFolder -Recurse -Force }
    $log = Join-Path $WorkRoot "build_$name.txt"
    & dotnet build (Join-Path $src "$name.csproj") -c Release -o $outFolder @properties -nologo *> $log
    $code = $LASTEXITCODE
    & dotnet build-server shutdown *> $null
    if ($code -ne 0) { Write-Host "  $name did not build; see $log" }
    $code -eq 0
}

$all = @()

if (Wanted 'build') {
    $statement = "cd('$(& $slash $here)'); build_k22('$(& $slash $build)')"
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $text = Run-Engine $statement
    Save ($text -split "`n") 'k22_build.out.txt'
    Write-Host ("build: {0:F0} s" -f $clock.Elapsed.TotalSeconds)
}

if (Wanted 'console') {
    $exe = Join-Path $build 'console\acc_tool.exe'
    if (Test-Path $exe) {
        $all += Run-Quiet 'console.args' $exe @('Ada', '2') (Join-Path $build 'console')
        $all += Run-Quiet 'console.none' $exe @() (Join-Path $build 'console')
        $all += Run-Quiet 'console.fail' $exe @('-fail') (Join-Path $build 'console')
    }
    else { $all += @('== console.args', '  not built', '== console.none', '  not built', '== console.fail', '  not built') }
}

if (Wanted 'windowed') {
    $exe = Join-Path $build 'windowed\acc_app.exe'
    $lines = @('== windowed')
    if (-not (Test-Path $exe)) { $lines += '  not built' }
    elseif (-not $jg -and -not $AllowMatlabWindows) { $lines += "  not run: an R2025b windowed program needs the user's leave (-AllowMatlabWindows)" }
    else {
        . (Join-Path $here 'k6-windows.ps1')
        $cases = @(, @('acc_app', 'windowed\acc_app.exe', @(), 'press:acc app:Compute'))
        $watched = Invoke-K6Windows -Cases $cases -Build $build -WorkRoot $WorkRoot -Cache $cache
        # The window timeline's handles and helper windows are the machine's; the program's own
        # window, the action, the transcript and the log lines are kept.
        foreach ($l in $watched) {
            if ($l -match "^\s+t=\s*[\d.]+ [+-] " -and $l -notmatch "'acc app' (shown|hidden)") { continue }
            $l = [regex]::Replace($l, '^\s+t=\s*[\d.]+ ', '  t=<T> ')
            $l = [regex]::Replace($l, '^  t=<T> \+ [0-9a-f]+ ', '  t=<T> + <HWND> ')
            $l = [regex]::Replace($l, '^  t=<T> - [0-9a-f]+$', '  t=<T> - <HWND>')
            $l = [regex]::Replace($l, 'HwndWrapper\[[^\]]*\]', 'HwndWrapper[<APP>]')
            $l = [regex]::Replace($l, "  LOG t=\s*[\d.]+ ", '  LOG t=<T> ')
            if ($l -match '^(CASE|RUN|ACTION)\t') { continue }
            $lines += $l
        }
    }
    $all += $lines
}

if (Wanted 'net') {
    $out = Join-Path $WorkRoot 'accnetclient'
    $props = if ($jg) { @("-p:AccOut=$build\net", '-p:JGraph=true', "-p:JGraphArrays=$arrays") } else { @("-p:AccOut=$build\net", "-p:MatlabRoot=$MatlabRoot") }
    $lines = @('== net')
    if (-not (Test-Path (Join-Path $build 'net\AccNet.dll'))) { $lines += '  not built'; $all += $lines }
    elseif (Build-DotNet 'accnetclient' $out $props) { $all += Run-Quiet 'net' (Join-Path $out 'accnetclient.exe') @() $out }
    else { $lines += '  the client did not build'; $all += $lines }
}

if (Wanted 'data') {
    $out = Join-Path $WorkRoot 'accdataclient'
    $ctf = Join-Path $build 'data\AccData.ctf'
    $props = if ($jg) { @("-p:AccOut=$build\data", '-p:JGraph=true', "-p:JGraphRuntime=$dataApi") } else { @("-p:AccOut=$build\data", "-p:MatlabRoot=$MatlabRoot") }
    $lines = @('== data')
    if (-not (Test-Path (Join-Path $build 'data\AccData.dll'))) { $lines += '  not built'; $all += $lines }
    elseif (Build-DotNet 'accdataclient' $out $props) { $all += Run-Quiet 'data' (Join-Path $out 'accdataclient.exe') @($ctf) $out }
    else { $lines += '  the client did not build'; $all += $lines }
}

if (Wanted 'c') {
    $lib = Join-Path $build 'c'
    $out = Join-Path $WorkRoot 'acc_c'
    $lines = @('== c')
    if (-not (Test-Path (Join-Path $lib 'acclib.dll'))) { $lines += '  not built'; $all += $lines }
    else {
        if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        New-Item -ItemType Directory -Force $out | Out-Null
        if ($jg) { $include = "/I`"$Runtime\include`" /I`"$lib`""; $link = "/LIBPATH:`"$Runtime\lib\win64`" /LIBPATH:`"$lib`" acclib.lib jgrt.lib" }
        else { $include = "/I`"$MatlabRoot\extern\include`" /I`"$lib`""; $link = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" /LIBPATH:`"$lib`" acclib.lib mclmcrrt.lib" }
        $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /DMATLAB_DEFAULT_RELEASE=R2017b'
        $ok = Invoke-Msvc @("cd /d `"$out`"", "cl $flags $include `"$here\src\k22client\acc_c.c`" /Fe:acc_c.exe /link $link") (Join-Path $WorkRoot 'build_acc_c.txt')
        if ($ok) {
            Copy-Item -LiteralPath (Join-Path $lib 'acclib.dll') -Destination $out -Force
            $all += Run-Quiet 'c' (Join-Path $out 'acc_c.exe') @() $out
        }
        else { $lines += '  the client did not compile'; $all += $lines }
    }
}

if (Wanted 'cpp') {
    $generic = Join-Path $build 'cpp\v2\generic_interface'
    $out = Join-Path $WorkRoot 'acc_cpp'
    $lines = @('== cpp')
    if (-not (Test-Path (Join-Path $generic 'acccpp.ctf'))) { $lines += '  not built'; $all += $lines }
    else {
        if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        New-Item -ItemType Directory -Force $out | Out-Null
        if ($jg) { $include = "/I`"$Runtime\include`" /I`"$generic`""; $link = "`"$Runtime\lib\win64\jgrt.lib`"" }
        else { $include = "/I`"$MatlabRoot\extern\include`" /I`"$generic`""; $link = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" libMatlabDataArray.lib libMatlabCppSharedLib.lib mclmcrrt.lib delayimp.lib /DELAYLOAD:libMatlabDataArray.dll" }
        $flags = '/nologo /O2 /MD /EHsc /std:c++17 /utf-8 /W3 /Zc:__cplusplus /bigobj /D_CRT_SECURE_NO_DEPRECATE'
        $ok = Invoke-Msvc @("cd /d `"$out`"", "cl $flags $include `"$here\src\k22client\acc_cpp.cpp`" /Fe:acc_cpp.exe /link $link") (Join-Path $WorkRoot 'build_acc_cpp.txt')
        if ($ok) { $all += Run-Quiet 'cpp' (Join-Path $out 'acc_cpp.exe') @($generic) $out }
        else { $lines += '  the client did not compile'; $all += $lines }
    }
}

if (Wanted 'python') {
    $pkg = Join-Path $build 'python'
    $lines = @('== python')
    if (-not (Test-Path (Join-Path $pkg 'accpkg\__init__.py'))) { $lines += '  not built'; $all += $lines }
    else {
        $folder = Join-Path $WorkRoot 'acc_py'
        New-Item -ItemType Directory -Force $folder | Out-Null
        $environment = @{ PYTHONPATH = $pkg; PYTHONIOENCODING = 'utf-8'; PYTHONUNBUFFERED = '1'; PYTHONDONTWRITEBYTECODE = '1' }
        $all += Run-Quiet 'python' $Python @((Join-Path $here 'src\k22client\acc_py.py')) $folder $environment 900
    }
}

if (Wanted 'server') {
    $archive = Join-Path $build 'server\accarch.ctf'
    $lines = @('== server')
    if (-not (Test-Path $archive)) { $lines += '  not built'; $all += $lines }
    else {
        $folder = Join-Path $WorkRoot 'server'
        if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
        New-Item -ItemType Directory -Force $folder | Out-Null
        $server = $null
        $readyPort = $null
        $log = Join-Path $folder 'server.log'
        if ($jg) {
            $inst = Join-Path $folder 'inst'
            & $JgraphMps new $inst --port $Port | Out-Null
            $config = Join-Path $inst 'config\main_config'
            [System.IO.File]::WriteAllLines($config, (@(Get-Content $config) + @('--enable-discovery')), $utf8)
            Copy-Item $archive (Join-Path $inst 'auto_deploy\accarch.ctf')
            $server = Start-Process -FilePath $JgraphMps -ArgumentList @('run', "`"$inst`"") -RedirectStandardOutput $log -RedirectStandardError (Join-Path $folder 'server.err') -NoNewWindow -PassThru
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            while ($watch.Elapsed.TotalSeconds -lt 180 -and -not $server.HasExited) {
                Start-Sleep -Milliseconds 500
                $text = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
                if ($text -and $text.Contains('accarch: deployed')) { $readyPort = $Port; break }
            }
        }
        else {
            $arguments = @('--archive', "`"$archive`"", '--http', '0', '--pid-root', "`"$folder`"", '--endpoint-root', "`"$folder`"", '--log-severity', 'trace')
            $server = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\win64\muserve.exe') -ArgumentList $arguments -RedirectStandardOutput $log -RedirectStandardError (Join-Path $folder 'server.err') -NoNewWindow -PassThru
            $endpoint = Join-Path $folder 'http'
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path $endpoint) -and $watch.Elapsed.TotalSeconds -lt 300 -and -not $server.HasExited) { Start-Sleep -Milliseconds 300 }
            if (Test-Path $endpoint) { $readyPort = ((Get-Content $endpoint -Raw).Trim() -split ':')[-1]; Start-Sleep -Seconds 2 }
        }
        if (-not $readyPort) { $lines += '  NOTREADY'; $all += $lines; Write-Host "  the server never became ready" }
        else {
            Write-Host ("  server on {0} after {1:n1} s" -f $readyPort, $watch.Elapsed.TotalSeconds)
            $transcript = Join-Path $folder 'client.txt'
            $env:PYTHONIOENCODING = 'utf-8'
            $arguments = @('-I', "`"$(Join-Path $here 'src\k22client\acc_server.py')`"", "http://localhost:$readyPort", 'accarch')
            $c = Start-Process -FilePath $Python -ArgumentList $arguments -RedirectStandardOutput $transcript -RedirectStandardError (Join-Path $folder 'client.err') -NoNewWindow -PassThru -Wait
            foreach ($l in (([System.IO.File]::ReadAllText($transcript, $utf8)) -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
            $lines += "  EXIT`t$($c.ExitCode)"
            $all += $lines
            Write-Host ("  {0,-20} {1} lines" -f 'server', ($lines.Count - 1))
        }
        try { if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue } } catch { }
        Start-Sleep -Seconds 2
        if (-not $jg) { Get-Process -Name 'mps_worker_app' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    }
}

if (Wanted 'excel') {
    $all += @('== excel', $(if (Test-Path (Join-Path $build 'excel')) { '  built; not run (stage K17: no Excel client here)' } else { '  not built' }))
}

if ($all.Count) { Merge $all 'k22_accept.out.txt' }

if ($jg -and (Wanted 'compare')) {
    & $Python -I (Join-Path $here 'compare-k22.py')
    exit $LASTEXITCODE
}
