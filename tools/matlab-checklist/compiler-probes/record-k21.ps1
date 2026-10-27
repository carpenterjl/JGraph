<#
.SYNOPSIS
    Records what R2025b does with the legacy deployment projects of R2024b and before (compiler and
    SDK plan, stage K21): probe_k21_legacy under matlab -batch -noFigureWindows. Writes
    recordings\k21_legacy.out.txt.

.DESCRIPTION
    A sample <deployment-project> of every sub-target written in a folder of its own; what
    compiler.internal.readPRJStruct reads; the script createDeploymentScript writes for each and
    what it refuses; what deploytool -build and -package print and leave behind (real mcc builds,
    about ten); the upgrade into a MATLAB project through createProjectModelFromPRJ (the project,
    its task, its labels, the task's deployment script); deploytool's one-argument form, the legacy
    apps' and open on fresh copies; and the catalog texts. Nothing opens a window under -batch.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1200
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
$statement = "cd('$here'); probe_k21_legacy('$probe')"
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
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k21_legacy.out.txt'), (Tokens $text), $utf8)
    Write-Host ("{0,-28} exit {1}{2}" -f 'k21_legacy.out.txt', $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
}
finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
