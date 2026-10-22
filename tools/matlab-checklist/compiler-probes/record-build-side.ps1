<#
.SYNOPSIS
    Runs build_k0.m and record_k0_build.m in R2025b (matlab -batch -noFigureWindows) and writes
    recordings\build_k0.out.txt and recordings\build_side.out.txt with machine paths replaced
    by tokens (compiler and SDK plan, stage K0).

.DESCRIPTION
    Nothing built here is run: build_k0 only compiles, and record_k0_build records what the
    build side prints (mcc refusals, the options class, a compiler.build.Results). Run
    run-k0.ps1 afterwards to run the programs.

.PARAMETER BuildRoot
    A scratch folder outside the repo to build into.

.PARAMETER Skip
    'build' to skip build_k0, 'record' to skip record_k0_build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BuildRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $BuildRoot | Out-Null
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$recordings = Join-Path $here 'recordings'
New-Item -ItemType Directory -Force $recordings | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        $tokens = @(@($BuildRoot, '<BUILD>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
            @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
        # mcc's warnings spell paths with forward slashes.
        $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
        $tokens = $tokens | Sort-Object { - $_[0].Length }
        foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), $text, $utf8)
        Write-Host ("{0,-24} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

if ($Skip -ne 'build') { Run-Matlab "cd('$here'); build_k0('$BuildRoot')" 'build_k0.out.txt' }
if ($Skip -ne 'record') { Run-Matlab "cd('$here'); record_k0_build('$BuildRoot')" 'build_side.out.txt' }
