<#
.SYNOPSIS
    Records how R2025b packages installers (compiler and SDK plan, stage K18): what
    compiler.package.installer, compiler.package.InstallerOptions and compiler.runtime.customInstaller
    print, write and refuse, what mcrinstaller and compiler.runtime.version answer, and what each
    installer R2025b wrote carries. Writes recordings\k18_package.out.txt and k18_payload.out.txt.

.DESCRIPTION
    probe_k18_package runs under matlab -batch -noFigureWindows over a standalone build of
    src\k6\k6_hello.m, a windowed one and a Python package; the installers it writes (R2025b's setup.exe
    with the payload embedded as resources, or a .zip) are read by k18-payload.py: the bundle's files,
    its ApplicationInstallerManifest.xml, the install agent URL, the splash and the icon. No installer
    is run. No MATLAB Runtime installer is on this machine, so RuntimeDelivery 'installer' and 'none'
    end in R2025b's refusals, recorded as such.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'probe' and 'payload' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [int] $TimeoutSeconds = 2400
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
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

$probe = Join-Path $WorkRoot 'p'
if ($skipped -notcontains 'probe') {
    Run-Matlab "cd('$here'); probe_k18_package('$probe')" 'k18_package.out.txt'
}

if ($skipped -notcontains 'payload') {
    # Every installer the probe wrote, by the folder it was written to; the builds' own programs left out.
    $lines = @()
    $installers = @(Get-ChildItem -LiteralPath $probe -Recurse -File -Include '*.exe', '*.zip' |
        Where-Object { $_.FullName -notmatch '\\p\\(b|bw|bp)\\' -and $_.Name -ne 'k18hello.exe' -and $_.Name -ne 'k18win.exe' } |
        Sort-Object FullName)
    foreach ($f in $installers) {
        $label = $f.DirectoryName.Substring($probe.Length + 1).Replace('\', '.') + '.' + $f.BaseName
        $text = & $Python -I (Join-Path $here 'k18-payload.py') $label $f.FullName 2>&1 | Out-String
        $lines += ($text -replace "`r`n", "`n").TrimEnd("`n")
        Write-Host ("payload {0,-40} {1} bytes" -f $label, $f.Length)
    }
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k18_payload.out.txt'), (Tokens (($lines -join "`n") + "`n")), $utf8)
    Write-Host ("{0,-28} {1} installers" -f 'k18_payload.out.txt', $installers.Count)
}
