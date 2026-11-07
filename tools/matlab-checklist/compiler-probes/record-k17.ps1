<#
.SYNOPSIS
    Records R2025b's Excel targets (compiler and SDK plan, stage K17a): the add-in and Excel client
    builds, the add-in's conversion rules seen from a COM client, Excel's own cells, the client's wire
    format against jgraph-mps, the installers' payloads, and what each build put in the user's
    registry. Writes recordings\k17_excel.out.txt, k17_com.out.txt, k17_cells.out.txt, k17_mps.out.txt,
    k17_payload.out.txt, k17_pe.out.txt, k17_strings.out.txt and k17_registry.out.txt.

.DESCRIPTION
    Steps, each left out with -Skip:

      probe     probe_k17_excel under matlab -batch -noFigureWindows: the options, every build, mcc -W
                excel and mpsxl, the Excel client and its packaging. R2025b registers each add-in it
                builds for the current user (HKCU\Software\Classes); the probe records each delta.
      com       src\k17client\k17com.ps1 in a PowerShell of its own: the add-in R2025b built (k17xl)
                created by its ProgID and called with every Variant shape under every flag, the
                utility library's helpers, the errors. MWInitApplication needs an Excel Application
                object, so an invisible Excel runs for the duration and quits after (decision KO13).
      cells     src\k17client\k17cells.ps1: the .bas imported into a scratch workbook through Excel
                automation where Excel trusts the VBA project object model, formulas calculated and
                each cell's Value2 recorded; the workbook closed unsaved. Where Excel refuses the
                import, the refusal is the recording.
      mps       JGraph's build of the same functions as a server archive, served by jgraph-mps behind
                src\k17client\k17proxy.py, which logs every request and reply on the wire; R2025b's
                Excel client DLL (a .NET Framework automation add-in) reflected and driven from
                src\k17client\k17mps.ps1 against the proxy.
      payload   k18-payload.py over each installer compiler.package.excelClientForProductionServer
                wrote (the _config.txt and payload.zip read from its resources), pe-resources.py over
                each built add-in DLL (the type library, archive and version resources), and
                src\k17client\k17strings.py over the .NET DLLs (their string heaps).
      cleanup   regsvr32 /s /u over every add-in DLL the probe built, the MathWorks server-config key
                the mps step wrote removed, Excel processes this script started ended, and the
                registry compared with its state before the probe (k17_registry.out.txt).

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of the steps above to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [string] $JGraph = '',
    [string] $JGraphMps = '',
    [int] $Port = 9971,
    [int] $ProxyPort = 9972,
    [int] $TimeoutSeconds = 5400
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
if (-not $JGraph) { $JGraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
if (-not $JGraphMps) { $JGraphMps = Join-Path $repoRoot 'src\JGraph.ProductionServer\bin\Debug\net8.0\jgraph-mps.exe' }
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$client = Join-Path $here 'src\k17client'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$probe = Join-Path $WorkRoot 'p'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<UUID>')
    $text
}

# A Python reader's stderr (a traceback for a file it cannot read) must not end this script under
# ErrorActionPreference Stop; it is part of that file's record.
function Run-Python([string[]] $arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { (@(& $Python -I @arguments 2>&1 | ForEach-Object { "$_" })) -join "`n" }
    finally { $ErrorActionPreference = $old }
}

function Save([string] $target, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
        -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    try {
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        Save $target $text
        Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

# A child PowerShell for the COM work, so a hung runtime cannot hold this script; its own lines are
# the recording, its console (the MATLAB Runtime's error text among it) is kept beside the work.
function Run-Child([string] $script, [string[]] $arguments, [string] $target, [int] $timeout) {
    $outFile = Join-Path $WorkRoot ($target + '.lines')
    $console = Join-Path $WorkRoot ($target + '.console')
    $errFile = Join-Path $WorkRoot ($target + '.err')
    if (Test-Path $outFile) { Remove-Item -LiteralPath $outFile }
    $p = Start-Process -FilePath $powershell `
        -ArgumentList (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Out', "`"$outFile`"") + $arguments) `
        -RedirectStandardOutput $console -RedirectStandardError $errFile -NoNewWindow -PassThru
    $timedOut = -not $p.WaitForExit($timeout * 1000)
    if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
    $text = if (Test-Path $outFile) { [System.IO.File]::ReadAllText($outFile, $utf8) } else { '' }
    $errText = if (Test-Path $errFile) { [System.IO.File]::ReadAllText($errFile) } else { '' }
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    if ($timedOut) { $text += "`nTIMEOUT`tafter $timeout s" }
    Save $target $text
    Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
}

# --- the registry before anything, by the names under each root --------------------------------------
$regRoots = @('Software\Classes', 'Software\Classes\CLSID', 'Software\Classes\TypeLib', 'Software\Classes\Interface',
    'Software\Classes\Record', 'Software\Classes\AppID', 'Software\Classes\Component Categories',
    'Software\Classes\Wow6432Node\CLSID', 'Software\MathWorks', 'Software\Microsoft\Office\16.0\Excel')
function Reg-Names() {
    $lines = @()
    foreach ($root in $regRoots) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($root)
        if ($key) {
            foreach ($n in ($key.GetSubKeyNames() | Sort-Object)) { $lines += "$root\$n" }
            $key.Close()
        }
    }
    $lines
}
$before = Join-Path $WorkRoot 'registry_before.txt'
if ($skipped -notcontains 'probe') {
    # The snapshot of a work root's first run is kept: a later probe run on the same root starts with the
    # add-ins registered, and the cleanup's comparison must still reach back to before the first.
    if (-not (Test-Path $before)) {
        [System.IO.File]::WriteAllLines($before, (Reg-Names), $utf8)
        & reg.exe export 'HKCU\Software\Classes' (Join-Path $WorkRoot 'classes_before.reg') /y | Out-Null
    }
    Write-Host ("registry before: {0} names" -f (Get-Content $before).Count)
    Run-Matlab "cd('$here'); probe_k17_excel('$probe')" 'k17_excel.out.txt'
}

# --- the add-in from a COM client ---------------------------------------------------------------------
if ($skipped -notcontains 'com') {
    # The rows before MWInitApplication in a process of their own (a failed call there left the main run's
    # first call failing), then the oracle proper.
    Run-Child (Join-Path $client 'k17com.ps1') @('-Phase', 'preinit', '-ProgId', 'k17xl.K17Xl.1_0') 'k17_com_preinit.out.txt' 600
    Run-Child (Join-Path $client 'k17com.ps1') @('-ProgId', 'k17xl.K17Xl.1_0', '-OtherProgIds', 'k17xl.K17Xl,k17bas.K17Bas.1_0,k17v2.K17V2.2_0,k17v05.K17V05.1_0,k17v3456.K17V3456.3_4,k17_one.k17_oneClass.1_0,k17sep.K17Sep.1_0') 'k17_com.out.txt' 1800
}

# --- Excel's cells -------------------------------------------------------------------------------------
if ($skipped -notcontains 'cells') {
    $bas = Join-Path $probe 'b\bas\k17bas.bas'
    Run-Child (Join-Path $client 'k17cells.ps1') @('-Bas', "`"$bas`"") 'k17_cells.out.txt' 900
}

# --- the Excel client's wire format against jgraph-mps behind a logging proxy --------------------------
if ($skipped -notcontains 'mps') {
    $lines = @()
    $mpsWork = Join-Path $WorkRoot 'mps'
    if (Test-Path $mpsWork) { Remove-Item -LiteralPath $mpsWork -Recurse -Force }
    New-Item -ItemType Directory $mpsWork | Out-Null
    # JGraph's archive of the same functions, for the server to serve.
    $src17 = Join-Path $here 'src\k17'
    $build = Join-Path $mpsWork 'b'
    New-Item -ItemType Directory $build | Out-Null
    $names = @('k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', 'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make')
    $files = $names | ForEach-Object { "'" + (Join-Path $src17 "$_.m").Replace('\', '/') + "'" }
    $statement = "mcc('-W', 'CTF:k17arch', '-U', '-d', '$($build.Replace('\', '/'))', $($files -join ', '));"
    $script = Join-Path $mpsWork 'build_k17arch.m'
    [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
    $p = Start-Process -FilePath $JGraph -ArgumentList @('-batch', "`"run('$($script.Replace('\', '/'))')`"") -RedirectStandardOutput (Join-Path $mpsWork 'build.stdout') -RedirectStandardError (Join-Path $mpsWork 'build.stderr') -NoNewWindow -PassThru -Wait
    $archive = Join-Path $build 'k17arch.ctf'
    Write-Host ("k17arch.ctf (jgraph)         exit {0}  exists {1}" -f $p.ExitCode, (Test-Path $archive))
    $lines += "build.jgraph`texit $($p.ExitCode) archive $(Test-Path $archive)"
    $server = $null; $proxy = $null
    if (Test-Path $archive) {
        $inst = Join-Path $mpsWork 'inst'
        & $JGraphMps new $inst --port $Port | Out-Null
        Copy-Item $archive (Join-Path $inst 'auto_deploy\k17arch.ctf')
        $log = Join-Path $mpsWork 'server.log'
        $server = Start-Process -FilePath $JGraphMps -ArgumentList @('run', "`"$inst`"") -RedirectStandardOutput $log -RedirectStandardError (Join-Path $mpsWork 'server.err') -NoNewWindow -PassThru
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $ready = $false
        while (-not $ready -and $watch.Elapsed.TotalSeconds -lt 120 -and -not $server.HasExited) {
            Start-Sleep -Milliseconds 500
            $text = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
            $ready = $text -and $text.Contains('k17arch: deployed')
        }
        $lines += "server.ready`t$ready"
        Write-Host ("jgraph-mps on {0} ready {1} after {2:n1} s" -f $Port, $ready, $watch.Elapsed.TotalSeconds)
        $wire = Join-Path $mpsWork 'wire.txt'
        $proxy = Start-Process -FilePath $Python -ArgumentList @('-I', "`"$(Join-Path $client 'k17proxy.py')`"", $ProxyPort, $Port, "`"$wire`"") -RedirectStandardOutput (Join-Path $mpsWork 'proxy.stdout') -RedirectStandardError (Join-Path $mpsWork 'proxy.stderr') -NoNewWindow -PassThru
        Start-Sleep -Seconds 2
        $dll = Join-Path $probe 'c\fromresults\k17mps.dll'
        # The configured URL names the archive, as ServerConfig's dialog stores host, port and archive together;
        # without it the client posts to /<function> and the server answers 404 (the first run).
        Run-Child (Join-Path $client 'k17mps.ps1') @('-Dll', "`"$dll`"", '-Url', "http://localhost:$ProxyPort/k17arch", '-Archive', 'k17arch', '-MatlabRoot', "`"$MatlabRoot`"") 'k17_mps.out.txt' 300
        Start-Sleep -Seconds 1
        try { if ($proxy -and -not $proxy.HasExited) { Stop-Process -Id $proxy.Id -Force } } catch { }
        & $JGraphMps stop $inst | Out-Null
        try { if ($server -and -not $server.HasExited) { $server.WaitForExit(10000) | Out-Null } } catch { }
        if (Test-Path $wire) {
            $lines += '== wire'
            $lines += (([System.IO.File]::ReadAllText($wire, $utf8)) -split "`r?`n") | Where-Object { $_.Length -gt 0 } | ForEach-Object { "  $_" }
        }
        if (Test-Path $log) {
            $lines += '== server log'
            $lines += (Get-Content $log) | Where-Object { $_ -notmatch '^\s*$' } | ForEach-Object { "  $_" }
        }
    }
    $existing = Join-Path $recordings 'k17_mps.out.txt'
    $head = if (Test-Path $existing) { [System.IO.File]::ReadAllText($existing, $utf8) } else { '' }
    Save 'k17_mps.out.txt' ($head.TrimEnd("`n") + "`n" + ($lines -join "`n"))
}

# --- what the installers and the DLLs carry --------------------------------------------------------------
if ($skipped -notcontains 'payload') {
    $lines = @()
    $installers = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.exe' | Where-Object { $_.FullName -match '\\p\\p\\|ExcelClientInstaller' } | Sort-Object FullName)
    foreach ($f in $installers) {
        $label = $f.DirectoryName.Substring($probe.Length + 1).Replace('\', '.') + '.' + $f.BaseName
        $text = Run-Python @((Join-Path $here 'k18-payload.py'), $label, $f.FullName)
        $lines += ($text -replace "`r`n", "`n").TrimEnd("`n")
        Write-Host ("payload {0,-50} {1} bytes" -f $label, $f.Length)
    }
    Save 'k17_payload.out.txt' ($lines -join "`n")
    $lines = @()
    $dlls = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.dll' | Sort-Object FullName)
    foreach ($f in $dlls) {
        $label = $f.DirectoryName.Substring($probe.Length + 1).Replace('\', '.') + '.' + $f.BaseName
        $text = Run-Python @((Join-Path $here 'pe-resources.py'), $f.FullName)
        $lines += (($text -replace "`r`n", "`n").TrimEnd("`n") -split "`n") | ForEach-Object { "$label.$_" }
    }
    Save 'k17_pe.out.txt' ($lines -join "`n")
    $lines = @()
    $managed = @((Join-Path $MatlabRoot 'toolbox\matlabxl\bin\win64\v4.0\ServerConfig.dll')) + @($dlls | Where-Object { $_.FullName -match '\\p\\c\\' } | ForEach-Object { $_.FullName })
    foreach ($f in $managed) {
        $label = if ($f.StartsWith($probe)) { $f.Substring($probe.Length + 1).Replace('\', '.') } else { 'ServerConfig.dll' }
        $text = Run-Python @((Join-Path $client 'k17strings.py'), $label, $f)
        $lines += ($text -replace "`r`n", "`n").TrimEnd("`n")
    }
    Save 'k17_strings.out.txt' ($lines -join "`n")
    Write-Host ("{0,-28} {1} installers, {2} DLLs" -f 'payload/pe/strings', $installers.Count, $dlls.Count)
}

# --- cleanup: unregister, remove the config key, compare the registry ----------------------------------
if ($skipped -notcontains 'cleanup') {
    $lines = @()
    # reg.exe's complaint about a key a parent's deletion already took is not this script's error.
    $ErrorActionPreference = 'Continue'
    # Everything the stage added, dumped whole before it is unregistered (the consolidated view; the probe
    # records each build's own delta).
    $was = if (Test-Path $before) { @(Get-Content $before) } else { @() }
    $now = @(Reg-Names)
    $added = @($now | Where-Object { $was -notcontains $_ })
    $lines += "registry.added.before.cleanup`t$($added.Count)"
    foreach ($a in $added) {
        $text = & reg.exe query "HKCU\$a" /s 2>$null | Out-String
        foreach ($l in ($text -split "`r?`n")) { if ($l.Trim().Length -gt 0) { $lines += "  |$($l.TrimEnd())" } }
    }
    $dlls = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.dll' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '_\d+_\d+\.dll$' } | Sort-Object FullName)
    # One add-in registered again by regsvr32 itself (per user, as the build did) and unregistered, its
    # ProgID and class keys dumped at each step: what DllRegisterServer writes and what DllUnregisterServer
    # takes away.
    $sample = Join-Path $probe 'b\plain\k17xl_1_0.dll'
    if (Test-Path $sample) {
        # regsvr32 itself wants machine-wide rights (exit 5 here); the build registers per user through
        # R2025b's MWRegsvr /useronly, which is used when it is there.
        $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', "`"$sample`"") -NoNewWindow -PassThru -Wait
        $lines += "regsvr32.register`tk17xl_1_0.dll exit $($r.ExitCode)"
        $mwregsvr = Join-Path $MatlabRoot 'runtime\win64\mwregsvr.exe'
        if (Test-Path $mwregsvr) {
            $r = Start-Process -FilePath $mwregsvr -ArgumentList @('/useronly', "`"$sample`"") -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $WorkRoot 'mwregsvr.out') -RedirectStandardError (Join-Path $WorkRoot 'mwregsvr.err')
            $said = ((Get-Content (Join-Path $WorkRoot 'mwregsvr.out') -ErrorAction SilentlyContinue) + (Get-Content (Join-Path $WorkRoot 'mwregsvr.err') -ErrorAction SilentlyContinue)) -join ' / '
            $lines += "mwregsvr.useronly`tk17xl_1_0.dll exit $($r.ExitCode) $said"
        } else { $lines += "mwregsvr.useronly`tnot found at $mwregsvr" }
        foreach ($k in @('Software\Classes\k17xl.K17Xl', 'Software\Classes\k17xl.K17Xl.1_0')) {
            $text = & reg.exe query "HKCU\$k" /s 2>$null | Out-String
            $lines += "regsvr32.registered.$($k.Split('\')[-1])"
            foreach ($l in ($text -split "`r?`n")) { if ($l.Trim().Length -gt 0) { $lines += "  |$($l.TrimEnd())" } }
        }
        $clsid = (& reg.exe query 'HKCU\Software\Classes\k17xl.K17Xl\CLSID' /ve 2>$null | Out-String) -replace '(?s).*(\{[0-9A-Fa-f-]{36}\}).*', '$1'
        if ($clsid -match '^\{') {
            $text = & reg.exe query "HKCU\Software\Classes\CLSID\$clsid" /s 2>$null | Out-String
            $lines += 'regsvr32.registered.CLSID'
            foreach ($l in ($text -split "`r?`n")) { if ($l.Trim().Length -gt 0) { $lines += "  |$($l.TrimEnd())" } }
        }
        $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', '/u', "`"$sample`"") -NoNewWindow -PassThru -Wait
        $lines += "regsvr32.unregister`tk17xl_1_0.dll exit $($r.ExitCode)"
        foreach ($k in @('Software\Classes\k17xl.K17Xl', 'Software\Classes\k17xl.K17Xl.1_0', "Software\Classes\CLSID\$clsid")) {
            & reg.exe query "HKCU\$k" 2>$null | Out-Null
            $lines += "regsvr32.after.$($k.Split('\')[-1])`t$(if ($LASTEXITCODE -eq 0) { 'still there' } else { 'gone' })"
        }
    }
    foreach ($f in $dlls) {
        $p = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', '/u', "`"$($f.FullName)`"") -NoNewWindow -PassThru -Wait
        $lines += "unregister`t$($f.FullName.Substring($probe.Length + 1))`texit $($p.ExitCode)"
    }
    # regsvr32 /u takes an add-in's ProgIDs away and leaves its CLSID, TypeLib and Interface keys; the
    # .NET Excel clients were registered through mscoree.dll (their .reg files) and have no unregister
    # of their own here. Every key added since the first snapshot whose contents name one of this
    # stage's components (all named k17...) is removed, deepest first; anything else added is left.
    $after1 = @(Reg-Names)
    $stillAdded = @($after1 | Where-Object { $was -notcontains $_ })
    $removedKeys = 0
    foreach ($a in $stillAdded) {
        $text = & reg.exe query "HKCU\$a" /s 2>$null | Out-String
        if ($text -match '(?i)k17') {
            $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\reg.exe') -ArgumentList @('delete', "`"HKCU\$a`"", '/f') -NoNewWindow -PassThru -Wait
            if ($r.ExitCode -eq 0) { $removedKeys++ } else { $lines += "leftover.kept`t$a (reg delete exit $($r.ExitCode))" }
        } else {
            $lines += "leftover.notours`t$a"
        }
    }
    $lines += "leftover.removed`t$removedKeys keys of this stage's that unregistration left"
    $configKey = 'HKCU:\Software\MathWorks\MATLAB Builder EX'
    if (Test-Path $configKey) {
        $lines += "config.key`tremoved $configKey"
        Remove-Item -Path $configKey -Recurse -Force
    }
    # An invisible Excel a crashed oracle run could not quit (no window of its own, started after this
    # script began) is ended; an Excel with a window is the user's and is left alone.
    $excels = @(Get-Process excel -ErrorAction SilentlyContinue)
    $orphans = @($excels | Where-Object { $_.MainWindowHandle -eq 0 -and $_.StartTime -gt $scriptStart })
    foreach ($e in $orphans) { try { Stop-Process -Id $e.Id -Force; $lines += "excel.orphan.ended`t$($e.Id) started $($e.StartTime.ToString('HH:mm:ss'))" } catch { $lines += "excel.orphan.left`t$($e.Id)" } }
    $lines += "excel.processes`t$($excels.Count), $($orphans.Count) invisible ones of this run's ended"
    $after = @(Reg-Names)
    $was = if (Test-Path $before) { @(Get-Content $before) } else { @() }
    $added = @($after | Where-Object { $was -notcontains $_ })
    $removed = @($was | Where-Object { $after -notcontains $_ })
    $lines += "registry.added`t$($added.Count)"
    $lines += ($added | ForEach-Object { "  + $_" })
    $lines += "registry.removed`t$($removed.Count)"
    $lines += ($removed | ForEach-Object { "  - $_" })
    Save 'k17_registry.out.txt' ($lines -join "`n")
    Write-Host ("{0,-28} {1} unregistered, registry +{2} -{3}" -f 'k17_registry.out.txt', $dlls.Count, $added.Count, $removed.Count)
}
