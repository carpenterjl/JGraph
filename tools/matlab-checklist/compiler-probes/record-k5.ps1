<#
.SYNOPSIS
    Records what R2025b's runtime does with a library component's events between calls (compiler
    and SDK plan, stage K5): whether a timer a call started ticks while the .NET client makes no
    call, inside a call that pauses, and inside one that computes; and what WaitForFiguresToDie does
    with only an invisible figure open. Writes recordings\k5_build.out.txt and k5_client.out.txt.

.DESCRIPTION
    probe_k5_build builds k5.m as the .NET assembly K5 under matlab -batch -noFigureWindows.
    k5client (src\k5client) is built with dotnet against it and run through run-quiet.ps1. No
    window is shown: k5.m's one figure is invisible, and it turns the beep off first.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build' and 'client' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1800
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
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

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
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-24} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

if ($skipped -notcontains 'build') { Run-Matlab "cd('$here'); probe_k5_build('$WorkRoot\b')" 'k5_build.out.txt' }

if ($skipped -notcontains 'client') {
    $lines = New-Object System.Collections.Generic.List[string]
    $clientSrc = Join-Path $WorkRoot 'clientsrc'
    if (Test-Path $clientSrc) { Remove-Item -LiteralPath $clientSrc -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here 'src\k5client') -Destination $clientSrc -Recurse
    $buildLog = Join-Path $WorkRoot 'client_build.txt'
    & dotnet build (Join-Path $clientSrc 'k5client.csproj') -c Release -o (Join-Path $WorkRoot 'client') `
        "-p:K5Out=$WorkRoot\b\dn" "-p:MatlabRoot=$MatlabRoot" -nologo *> $buildLog
    $lines.Add("BUILD`texit $LASTEXITCODE")
    & dotnet build-server shutdown *> $null
    $exe = Join-Path $WorkRoot 'client\k5client.exe'
    if (Test-Path $exe) {
        $transcript = [System.IO.Path]::GetTempFileName()
        $quiet = @{ Exe = $exe; Environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache_client') };
            TimeoutSeconds = 600; OutFile = $transcript; WorkingDirectory = (Join-Path $WorkRoot 'client') }
        & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
        $out = [System.IO.File]::ReadAllText($transcript)
        Remove-Item -LiteralPath $transcript
        foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
    }
    else { $lines.Add("  no program (see $buildLog)") }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k5_client.out.txt'), (Tokens $text), $utf8)
    Write-Host "k5_client.out.txt        done"
}
