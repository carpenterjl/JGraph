<#
.SYNOPSIS
    Records what R2025b's compiler app commands do without a desktop (compiler and SDK plan, stage
    K19): probe_k19_apps under matlab -batch -noFigureWindows. Writes recordings\k19_apps.out.txt.

.DESCRIPTION
    The target-specific apps (standaloneApplicationCompiler and its siblings) and the legacy ones
    (applicationCompiler, libraryCompiler, productionServerCompiler, deploytool, webAppCompiler,
    hadoopCompiler): their counts and help, their warnings, what each does with no argument, a
    missing file, a stub project, a MATLAB file, a text file and bad arguments, the helpers they are
    built on, and the catalog texts their windows are made of. Nothing opens a window: the app
    gallery is published to a desktop that is not there.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text
}

$probe = Join-Path $WorkRoot 'p'
$statement = "cd('$here'); probe_k19_apps('$probe')"
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
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k19_apps.out.txt'), (Tokens $text), $utf8)
    Write-Host ("{0,-28} exit {1}{2}" -f 'k19_apps.out.txt', $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
}
finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
