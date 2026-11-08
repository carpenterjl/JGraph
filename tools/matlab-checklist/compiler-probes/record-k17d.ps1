<#
.SYNOPSIS
    Records R2025b's answers to stage K17d's extra COM rows (compiler and SDK plan): the flags' effect
    on more shapes, Range objects in more forms, and the utility library's value classes in detail.

.DESCRIPTION
    Builds the K17 probe's add-in once more with R2025b's mcc (whose mbuild registers it for the
    current user), runs the K17a oracle's extra phase against it (src\k17client\k17com.ps1 -Phase
    extra, the rows in k17com_extra.ps1) with an invisible Excel, and unregisters it again, sweeping
    the keys regsvr32 /u leaves as record-k17.ps1's cleanup does. The recording is
    recordings\k17_com_extra.out.txt; run-k17.ps1 records JGraph's with the same name and
    compare-k17.py holds the two to each other.

      powershell -File record-k17d.ps1 -WorkRoot <scratch>

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
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

# --- the add-in, built and registered per user by R2025b's mcc -------------------------------------------
if ($skipped -notcontains 'build') {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    New-Item -ItemType Directory $build | Out-Null
    $src17 = Join-Path $here 'src\k17'
    $names = @('k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', 'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make')
    $files = $names | ForEach-Object { "'" + (Join-Path $src17 "$_.m").Replace('\', '/') + "'" }
    $statement = "mcc('-W', 'excel:k17xl,K17Xl,version=1.0', '-T', 'link:lib', '-d', '$($build.Replace('\', '/'))', $($files -join ', '));"
    $script = Join-Path $WorkRoot 'build_k17xl.m'
    [System.IO.File]::WriteAllText($script, $statement + "`n", $utf8)
    $out = Join-Path $WorkRoot 'build.stdout'
    $err = Join-Path $WorkRoot 'build.stderr'
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') -ArgumentList @('-batch', "`"run('$($script.Replace('\', '/'))')`"", '-noFigureWindows') -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { } }
    $dll = Join-Path $build 'k17xl_1_0.dll'
    Write-Host ("k17xl_1_0.dll (R2025b)        exit {0}  exists {1}  {2:F0} s" -f $p.ExitCode, (Test-Path $dll), $clock.Elapsed.TotalSeconds)
    if (-not (Test-Path $dll)) { Write-Host (Get-Content $out -Raw); Write-Host (Get-Content $err -Raw); throw 'The add-in was not built.' }
}

# A run that skips the build registers the add-in built before, as record-k17.ps1's cleanup step did
# (regsvr32 itself wants machine-wide rights; R2025b's MWRegsvr /useronly registers per user).
if ($skipped -contains 'build') {
    $dll = Join-Path $build 'k17xl_1_0.dll'
    $r = Start-Process -FilePath (Join-Path $MatlabRoot 'runtime\win64\mwregsvr.exe') -ArgumentList @('/useronly', "`"$dll`"") -NoNewWindow -PassThru -Wait
    Write-Host ("mwregsvr /useronly           exit {0}" -f $r.ExitCode)
}

# --- the extra rows ---------------------------------------------------------------------------------------
if ($skipped -notcontains 'com') {
    Run-Child (Join-Path $client 'k17com.ps1') @('-Phase', 'extra', '-ProgId', 'k17xl.K17Xl.1_0') 'k17_com_extra.out.txt' 1800
}

# --- cleanup: unregister, sweep what regsvr32 /u leaves, compare the registry -----------------------------
if ($skipped -notcontains 'cleanup') {
    $ErrorActionPreference = 'Continue'
    $was = @(Get-Content $before)
    $dll = Join-Path $build 'k17xl_1_0.dll'
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
