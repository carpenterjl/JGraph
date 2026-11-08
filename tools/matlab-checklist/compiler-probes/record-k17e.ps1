<#
.SYNOPSIS
    Records Excel's own cells over R2025b's Excel add-in (compiler and SDK plan, stage K17e): the rows
    stage K17a could not record because Excel refuses the .bas import without the trust setting.

.DESCRIPTION
    Builds the K17 probe's add-in with R2025b's compiler.build.excelAddIn and GenerateVisualBasicFile
    on (its mbuild registers k17bas.K17Bas.1_0 for the current user), wraps the k17bas.bas R2025b
    wrote into an add-in workbook with src\k17client\k17xlam.py (the .xlam JGraph's own builds
    write, here around R2025b's module so that R2025b's add-in and MATLAB Runtime's MWComUtil serve
    the cells), opens it in an invisible Excel with src\k17client\k17cells.ps1 -Xlam, which writes
    the formulas and reads the cells back (recordings\k17_cells.out.txt), then runs
    src\k17client\k17hosting.ps1 over the same add-in (recordings\k17_hosting.out.txt: the component
    and the utility created from VBA with the module's server name "localhost" and with none, the
    registry as Excel sees it, the runtime's DLLs and the add-in's loaded into Excel one Excel each),
    and unregisters the add-in again, sweeping the keys regsvr32 /u leaves as record-k17.ps1's cleanup
    does. run-k17.ps1 records JGraph's with the same names and compare-k17.py holds them to each other.

    Measured 2026-11-07 (Excel 16, Click-to-Run, x64): every cell of R2025b's add-in answers "Error in
    VBAProject: Class not registered on local machine", since CreateObject(progid, "localhost") is a
    DCOM activation that reads the machine's registrations alone and mbuild registered the add-in
    for the current user; and R2025b's add-in DLL ends Excel's process when loaded into it (its
    runtime's mclmcrrt, mwcomutil and mclxlmain load), where MathWorks' machine-wide AppID
    ({64571771-...}, DllSurrogate) would host it in dllhost.exe instead. So Excel's cells over
    R2025b's add-in are not measurable on this machine; its conversions are K17a's and K17d's COM
    recordings.

      powershell -File record-k17e.ps1 -WorkRoot <scratch>

.PARAMETER WorkRoot
    A scratch folder outside the repo.
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
$scriptStart = Get-Date
$here = $PSScriptRoot
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$client = Join-Path $here 'src\k17client'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$build = Join-Path $WorkRoot 'b'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Tokens([string] $text) {
    $text = $text.Replace($WorkRoot, '<W>').Replace($MatlabRoot, '<MATLABROOT>').Replace($WorkRoot.Replace('\', '/'), '<W>')
    $text = $text.Replace($env:USERPROFILE, '<HOME>')
    [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<UUID>')
}

function Save([string] $target, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
}

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

$regRoots = @('Software\Classes', 'Software\Classes\CLSID', 'Software\Classes\TypeLib', 'Software\Classes\Interface',
    'Software\Classes\Record', 'Software\Classes\AppID', 'Software\Classes\Component Categories')
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
if (-not (Test-Path $before)) { [System.IO.File]::WriteAllLines($before, (Reg-Names), $utf8) }

# --- the add-in with its .bas, built and registered per user by R2025b ------------------------------------
$dll = Join-Path $build 'k17bas_1_0.dll'
$bas = Join-Path $build 'k17bas.bas'
$xlam = Join-Path $build 'k17bas.xlam'
if ($skipped -notcontains 'build') {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    New-Item -ItemType Directory $build | Out-Null
    $src17 = Join-Path $here 'src\k17'
    $names = @('k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', 'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make')
    $files = $names | ForEach-Object { "'" + (Join-Path $src17 "$_.m").Replace('\', '/') + "'" }
    $statement = "compiler.build.excelAddIn({$($files -join ', ')}, 'AddInName', 'k17bas', 'ClassName', 'K17Bas', 'GenerateVisualBasicFile', 'on', 'OutputDir', '$($build.Replace('\', '/'))');"
    $script = Join-Path $WorkRoot 'build_k17bas.m'
    [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
    $out = Join-Path $WorkRoot 'build.stdout'
    $err = Join-Path $WorkRoot 'build.stderr'
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"run('$($script.Replace('\', '/'))')`"", '-noFigureWindows') -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { } }
    Write-Host ("k17bas_1_0.dll (R2025b)       exit {0}  dll {1}  bas {2}  {3:F0} s" -f $p.ExitCode, (Test-Path $dll), (Test-Path $bas), $clock.Elapsed.TotalSeconds)
    if (-not (Test-Path $dll) -or -not (Test-Path $bas)) { Write-Host (Get-Content $out -Raw); Write-Host (Get-Content $err -Raw); throw 'The add-in was not built.' }
}

# A run that skips the build registers the add-in built before (R2025b's MWRegsvr /useronly).
if ($skipped -contains 'build') {
    $r = Start-Process -FilePath (Join-Path $MatlabRoot 'runtime\win64\mwregsvr.exe') -ArgumentList @('/useronly', "`"$dll`"") -NoNewWindow -PassThru -Wait
    Write-Host ("mwregsvr /useronly           exit {0}" -f $r.ExitCode)
}

# --- the workbook around R2025b's module, and Excel's cells over it ---------------------------------------
if ($skipped -notcontains 'cells') {
    $text = (& $Python -I (Join-Path $client 'k17xlam.py') $xlam $bas 2>&1 | Out-String).Trim()
    Write-Host $text
    if (-not (Test-Path $xlam)) { throw 'The add-in workbook was not written.' }
    Run-Child (Join-Path $client 'k17cells.ps1') @('-Xlam', "`"$xlam`"") 'k17_cells.out.txt' 900
}

# --- how the add-in reaches Excel: the activations, the registry, the DLLs loaded into Excel ----------------
if ($skipped -notcontains 'hosting') {
    Run-Child (Join-Path $client 'k17hosting.ps1') @('-ProgId', 'k17bas.K17Bas.1_0', '-UtilProgId', 'MWComUtil.MWUtil25.2', '-Dll', "`"$dll`"", '-Python', "`"$Python`"") 'k17_hosting.out.txt' 900
}

# --- cleanup: unregister, sweep what regsvr32 /u leaves, compare the registry -----------------------------
if ($skipped -notcontains 'cleanup') {
    $ErrorActionPreference = 'Continue'
    $was = @(Get-Content $before)
    if (Test-Path $dll) {
        $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s', '/u', "`"$dll`"") -NoNewWindow -PassThru -Wait
        Write-Host ("regsvr32 /u                  exit {0}" -f $r.ExitCode)
    }
    $removed = 0
    foreach ($a in @(@(Reg-Names) | Where-Object { $was -notcontains $_ })) {
        $text = & reg.exe query "HKCU\$a" /s 2>$null | Out-String
        if ($text -match '(?i)k17') {
            $r = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\reg.exe') -ArgumentList @('delete', "`"HKCU\$a`"", '/f') -NoNewWindow -PassThru -Wait
            if ($r.ExitCode -eq 0) { $removed++ } else { Write-Host "leftover kept: $a" }
        } else {
            Write-Host "leftover not ours: $a"
        }
    }
    $configKey = 'HKCU:\Software\MathWorks\MATLAB Builder EX'
    if (Test-Path $configKey) { Remove-Item -Path $configKey -Recurse -Force }
    $excels = @(Get-Process excel -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -eq 0 -and $_.StartTime -gt $scriptStart })
    foreach ($e in $excels) { try { Stop-Process -Id $e.Id -Force; Write-Host "invisible Excel $($e.Id) ended" } catch { } }
    $after = @(Reg-Names)
    $added = @($after | Where-Object { $was -notcontains $_ })
    $gone = @($was | Where-Object { $after -notcontains $_ })
    Write-Host ("cleanup: {0} keys swept, registry +{1} -{2}" -f $removed, $added.Count, $gone.Count)
    foreach ($a in $added) { Write-Host "  + $a" }
    foreach ($g in $gone) { Write-Host "  - $g" }
}
