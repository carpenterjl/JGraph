<#
.SYNOPSIS
    Runs stage K17's probe under JGraph as record-k17.ps1 ran it under R2025b (compiler and SDK
    plan, stages K17b to K17g) and compares: recordings-jgraph\k17_*.out.txt and k17_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      excel    probe_k17_excel.m, unchanged, under jgraph -batch, as far as -UpTo says ('addin' until
               stage K17f gives JGraph the Excel client for the Production Server; 'all' after): the
               options, every add-in build, mcc -W excel, the server archive and the add-in beside it.
               Each build registers its add-in for the current user, as R2025b's mbuild did (decision
               KO13); the probe records each build's registry delta through .NET where JGraph gives
               it no reg.exe.
      com      src\k17client\k17com.ps1, the K17a oracle, against the add-in JGraph built and
               registered (k17xl.K17Xl.1_0) and JGraph's COM utility library (jgcomutil.dll beside
               jgraph.exe, registered for the current user first; stage K17d): the rows before
               MWInitApplication in a process of their own, then the whole oracle with an invisible
               Excel for its Range rows, then stage K17d's extra rows (k17com_extra.ps1).
      cells    src\k17client\k17cells.ps1 -Xlam over the add-in workbook the probe's 'bas' build wrote
               (k17bas.xlam; stage K17e): an invisible Excel opens it as an add-in, writes the
               formulas into a second workbook, calculates and reads the cells back (k17_cells.out.txt);
               then k17hosting.ps1: the activations from VBA with and without a server name, the
               registry as Excel sees it, the runtime's DLLs and the add-in's loaded into Excel
               (k17_hosting.out.txt).
               Excel is started by COM and inherits none of this script's PATH, so the add-in's DLL
               finds the runtime through the registry key the launchers read, pointed for the run at
               a stand-in layout whose bin\win64 is a junction to jgraph.exe's folder.
      pe       pe-resources.py over every DLL the probe built (k17_pe.out.txt).
      cleanup  regsvr32 /s /u over every add-in the probe built and over the utility library, the
               stand-in runtime's key and junction removed, the registry compared with its state
               before the probe (k17_registry.out.txt), as record-k17.ps1's cleanup did.
      compare  compare-k17.py over both engines' recordings (--only the recordings this stage makes).

    The work root should be as long as record-k17.ps1's (k17_r2025b and k17_jgraph are both ten
    characters under the same folder), so lines that name a path are as long as R2025b's. The built
    add-ins find the JGraph Runtime through the PATH: this script puts jgraph.exe's folder, where
    jgrt.dll and the engine sit, in front of it for the COM client.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Jgraph
    The jgraph.exe that builds; the CLI's Debug build when not given.

.PARAMETER UpTo
    How far the probe runs: 'addin' (the default) or 'all'.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Jgraph,
    [string] $Skip = '',
    [string] $UpTo = 'addin',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe')
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
if (-not $Jgraph) { $Jgraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
$Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
$jgraphFolder = Split-Path $Jgraph
$recordings = Join-Path $here 'recordings-jgraph'
New-Item -ItemType Directory -Force $recordings | Out-Null
$client = Join-Path $here 'src\k17client'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$probe = Join-Path $WorkRoot 'p'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
# The add-ins' template finds jgrt.dll, and jgrt the engine beside it, in a PATH folder (its last resort).
$env:PATH = "$jgraphFolder;$env:PATH"

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($jgraphFolder, '<JGRAPH>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<UUID>')
    $text
}

function Save([string] $target, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
}

function Run-Jgraph([string] $statement, [string] $target, [int] $timeout = 1800) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    if (-not $p.WaitForExit($timeout * 1000)) {
        Stop-Process -Id $p.Id -Force
        Write-Host ("{0,-28} stopped after {1} s" -f $target, $timeout)
    }
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Remove-Item -LiteralPath $out, $err
    Save $target $text
    Write-Host ("{0,-28} {1:F1} s" -f $target, $clock.Elapsed.TotalSeconds)
}

# A child PowerShell for the COM work, as record-k17.ps1 ran it: its own lines are the recording.
function Run-Child([string] $script, [string[]] $arguments, [string] $target, [int] $timeout) {
    $outFile = Join-Path $WorkRoot ($target + '.lines')
    $console = Join-Path $WorkRoot ($target + '.console')
    $errFile = Join-Path $WorkRoot ($target + '.err')
    if (Test-Path $outFile) { Remove-Item -LiteralPath $outFile }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
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
    Write-Host ("{0,-28} exit {1} {2:F1} s{3}" -f $target, $p.ExitCode, $clock.Elapsed.TotalSeconds, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
}

function Run-Python([string[]] $arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { (@(& $Python -I @arguments 2>&1 | ForEach-Object { "$_" })) -join "`n" }
    finally { $ErrorActionPreference = $old }
}

# --- the registry before anything, by the names under each root (record-k17.ps1's roots) ----------------
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
$slash = { param($p) $p.Replace('\', '/') }
if ($skipped -notcontains 'excel') {
    if (-not (Test-Path $before)) { [System.IO.File]::WriteAllLines($before, (Reg-Names), $utf8) }
    Write-Host ("registry before: {0} names" -f (Get-Content $before).Count)
    Run-Jgraph "cd('$(& $slash $here)'); probe_k17_excel('$(& $slash $probe)', '$UpTo')" 'k17_excel.out.txt'
}

# --- the add-in from a COM client: the K17a oracle against JGraph's registered add-in --------------------
$utility = Join-Path $jgraphFolder 'jgcomutil.dll'
if ($skipped -notcontains 'com') {
    # The utility library registered for the current user (its DllRegisterServer falls back to the user's
    # hive when it may not write the machine's), as the runtime's installer would have done; a build of
    # the probe's registered it already when it found it unregistered (stage K17d).
    $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', "`"$utility`"") -NoNewWindow -PassThru -Wait
    Write-Host ("{0,-28} exit {1}" -f 'jgcomutil.dll registered', $r.ExitCode)
    $others = 'k17xl.K17Xl,k17bas.K17Bas.1_0,k17v2.K17V2.2_0,k17v05.K17V05.1_0,k17v3456.K17V3456.3_4,k17_one.k17_oneClass.1_0,k17sep.K17Sep.1_0'
    $util = @('-UtilPrefix', 'JGraphComUtil')
    Run-Child (Join-Path $client 'k17com.ps1') (@('-Phase', 'preinit', '-ProgId', 'k17xl.K17Xl.1_0') + $util) 'k17_com_preinit.out.txt' 600
    Run-Child (Join-Path $client 'k17com.ps1') (@('-ProgId', 'k17xl.K17Xl.1_0', '-OtherProgIds', $others) + $util) 'k17_com.out.txt' 1800
    Run-Child (Join-Path $client 'k17com.ps1') (@('-Phase', 'extra', '-ProgId', 'k17xl.K17Xl.1_0') + $util) 'k17_com_extra.out.txt' 1800
}

# --- Excel's cells over the add-in workbook JGraph wrote (stage K17e) ---------------------------------------
$runtimeKey = 'SOFTWARE\JGraph\JGraph Runtime'
$runtimeVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $jgraphFolder 'JGraph.Deploy.dll')).Version.ToString(3)
$standIn = Join-Path $WorkRoot 'rt'
$standInLink = Join-Path $standIn 'bin\win64'
$runtimeKeyMarker = Join-Path $WorkRoot 'runtime_key_made.txt'
if ($skipped -notcontains 'cells') {
    New-Item -ItemType Directory -Force (Join-Path $standIn 'bin') | Out-Null
    if (-not (Test-Path $standInLink)) { & cmd.exe /c mklink /J "`"$standInLink`"" "`"$jgraphFolder`"" | Out-Null }
    [System.IO.File]::WriteAllText((Join-Path $standIn 'jgraph-runtime.json'), "{`n  `"version`": `"$runtimeVersion`"`n}`n", $utf8)
    $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$runtimeKey\$runtimeVersion")
    if ($null -eq $existing) {
        $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$runtimeKey\$runtimeVersion")
        $k.SetValue('Location', $standIn)
        $k.Close()
        [System.IO.File]::WriteAllText($runtimeKeyMarker, "$runtimeKey\$runtimeVersion", $utf8)
        Write-Host ("{0,-28} HKCU\{1}\{2} -> {3}" -f 'runtime key', $runtimeKey, $runtimeVersion, $standIn)
    } else {
        $existing.Close()
        Write-Host ("{0,-28} HKCU\{1}\{2} was there already and is left" -f 'runtime key', $runtimeKey, $runtimeVersion)
    }
    $xlam = Join-Path $probe 'b\bas\k17bas.xlam'
    Run-Child (Join-Path $client 'k17cells.ps1') @('-Xlam', "`"$xlam`"") 'k17_cells.out.txt' 900
    # How the add-in reaches Excel (record-k17e.ps1's hosting rows): the activations from VBA with and
    # without the server name, the registry as Excel sees it, jgrt.dll, jgcomutil.dll and the add-in
    # loaded into Excel.
    $runtimeDlls = @('jgrt.dll', 'jgcomutil.dll') | ForEach-Object { Join-Path $jgraphFolder $_ }
    Run-Child (Join-Path $client 'k17hosting.ps1') @('-ProgId', 'k17bas.K17Bas.1_0', '-UtilProgId', 'JGraphComUtil.MWUtil25.2', '-Dll', "`"$(Join-Path $probe 'b\bas\k17bas_1_0.dll')`"", '-RuntimeDlls', "`"$($runtimeDlls -join ',')`"", '-Python', "`"$Python`"") 'k17_hosting.out.txt' 900
}

# --- what the DLLs carry ------------------------------------------------------------------------------------
if ($skipped -notcontains 'pe') {
    $lines = @()
    $dlls = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.dll' -ErrorAction SilentlyContinue | Sort-Object FullName)
    foreach ($f in $dlls) {
        $label = $f.DirectoryName.Substring($probe.Length + 1).Replace('\', '.') + '.' + $f.BaseName
        $text = Run-Python @((Join-Path $here 'pe-resources.py'), $f.FullName)
        $lines += (($text -replace "`r`n", "`n").TrimEnd("`n") -split "`n") | ForEach-Object { "$label.$_" }
    }
    Save 'k17_pe.out.txt' ($lines -join "`n")
    Write-Host ("{0,-28} {1} DLLs" -f 'k17_pe.out.txt', $dlls.Count)
}

# --- cleanup: unregister, compare the registry ---------------------------------------------------------------
if ($skipped -notcontains 'cleanup') {
    $lines = @()
    $ErrorActionPreference = 'Continue'
    $was = if (Test-Path $before) { @(Get-Content $before) } else { @() }
    $now = @(Reg-Names)
    $added = @($now | Where-Object { $was -notcontains $_ })
    $lines += "registry.added.before.cleanup`t$($added.Count)"
    foreach ($a in $added) {
        $text = & reg.exe query "HKCU\$a" /s 2>$null | Out-String
        foreach ($l in ($text -split "`r?`n")) { if ($l.Trim().Length -gt 0) { $lines += "  |$($l.TrimEnd())" } }
    }
    $dlls = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.dll' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '_\d+_\d+\.dll$' } | Sort-Object FullName)
    # One add-in registered again by regsvr32 itself and unregistered, its keys dumped at each step, as
    # record-k17.ps1 did with R2025b's (there regsvr32 wanted machine-wide rights and MWRegsvr /useronly
    # registered per user; JGraph's DllRegisterServer registers per user itself when it may not for all).
    $sample = Join-Path $probe 'b\plain\k17xl_1_0.dll'
    if (Test-Path $sample) {
        $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', "`"$sample`"") -NoNewWindow -PassThru -Wait
        $lines += "regsvr32.register`tk17xl_1_0.dll exit $($r.ExitCode)"
        $lines += "mwregsvr.useronly`tnot needed: JGraph's DllRegisterServer registers for the current user when it may not for all"
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
    if (Test-Path $utility) {
        $p = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', '/u', "`"$utility`"") -NoNewWindow -PassThru -Wait
        $lines += "unregister`tjgcomutil.dll`texit $($p.ExitCode)"
    }
    # Whatever this stage's add-ins left (every one is named k17...) is removed, deepest first; JGraph's
    # own component category, which the first build made, is left as the runtime's installer would leave it.
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
    # The stand-in runtime's key (when this script made it) and junction.
    if (Test-Path $runtimeKeyMarker) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($runtimeKey, $false)
        Remove-Item -LiteralPath $runtimeKeyMarker
        $lines += "runtime.key`tHKCU\$runtimeKey removed"
    }
    if (Test-Path $standInLink) { [System.IO.Directory]::Delete($standInLink); $lines += "runtime.standin`tjunction removed" }
    $excels = @(Get-Process excel -ErrorAction SilentlyContinue)
    $orphans = @($excels | Where-Object { $_.MainWindowHandle -eq 0 -and $_.StartTime -gt $scriptStart })
    foreach ($e in $orphans) { try { Stop-Process -Id $e.Id -Force; $lines += "excel.orphan.ended`t$($e.Id) started $($e.StartTime.ToString('HH:mm:ss'))" } catch { $lines += "excel.orphan.left`t$($e.Id)" } }
    $lines += "excel.processes`t$($excels.Count), $($orphans.Count) invisible ones of this run's ended"
    $after = @(Reg-Names)
    $added = @($after | Where-Object { $was -notcontains $_ })
    $removed = @($was | Where-Object { $after -notcontains $_ })
    $lines += "registry.added`t$($added.Count)"
    $lines += ($added | ForEach-Object { "  + $_" })
    $lines += "registry.removed`t$($removed.Count)"
    $lines += ($removed | ForEach-Object { "  - $_" })
    Save 'k17_registry.out.txt' ($lines -join "`n")
    Write-Host ("{0,-28} {1} unregistered, registry +{2} -{3}" -f 'k17_registry.out.txt', $dlls.Count, $added.Count, $removed.Count)
    $ErrorActionPreference = 'Stop'
}

if ($skipped -notcontains 'compare') {
    $only = if ($UpTo -eq 'all') { @() } else { @('--only', 'excel,com_preinit,com,com_extra,cells,hosting,pe,registry') }
    $text = (& $Python -I (Join-Path $here 'compare-k17.py') $recordings @only 2>&1 | Out-String) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k17_differences.txt'), $text, $utf8)
    Write-Host $text
}
