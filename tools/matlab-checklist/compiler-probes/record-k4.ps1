<#
.SYNOPSIS
    Records what R2025b's runtime does that the JGraph Runtime's facade has to match (compiler and
    SDK plan, stage K4): each -R option on its own, and what two instances of one .NET component
    and a second component share in one process. Writes recordings\k4_build.out.txt,
    k4_ropts.out.txt and k4_client.out.txt.

.DESCRIPTION
    probe_k4_build builds k4r_main once per -R option and k4.m as the .NET assemblies K4A and K4B,
    under matlab -batch -noFigureWindows. Each k4r_main then runs through run-quiet.ps1 under its
    own MCR_CACHE_ROOT, from a current folder that is not the program's, so a relative -logfile
    shows which folder it is read against; the -logfile build runs twice (the second time ending
    on an error) to show whether the log is replaced or appended to. k4client (src\k4client) is
    built with dotnet against the two assemblies and run through run-quiet.ps1. k4r_main and k4.m
    turn the beep off first; nothing fails before them.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build', 'ropts' and 'client' to leave out.
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

function Run-Quiet([string] $exe, [string[]] $arguments, [string] $cwd, [hashtable] $envs, $lines) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $quiet = @{ Exe = $exe; Environment = $envs; TimeoutSeconds = 600; OutFile = $transcript }
    if ($arguments.Count) { $quiet.Arguments = $arguments }
    if ($cwd) { $quiet.WorkingDirectory = $cwd }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
}

if ($skipped -notcontains 'build') { Run-Matlab "cd('$here'); probe_k4_build('$WorkRoot\b')" 'k4_build.out.txt' }

if ($skipped -notcontains 'ropts') {
    $lines = New-Object System.Collections.Generic.List[string]
    $runs = @(@('none', @()), @('start', @()), @('complete', @()), @('both', @()), @('log', @()), @('log', @('err')),
        @('single', @()), @('nojvm', @()), @('bogus', @()), @('startbare', @()))
    foreach ($r in $runs) {
        $name = $r[0]
        $exe = Join-Path $WorkRoot "b\r_$name\k4r_main.exe"
        $cwd = Join-Path $WorkRoot "cwd_$name"
        New-Item -ItemType Directory -Force $cwd | Out-Null
        $lines.Add("RUN`t$name $($r[1] -join ' ')".TrimEnd())
        if (-not (Test-Path $exe)) { $lines.Add("  no program"); continue }
        Run-Quiet $exe $r[1] $cwd @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot "cache_$name") } $lines
    }
    # Where the relative -logfile landed after both runs, and what it holds.
    foreach ($candidate in @((Join-Path $WorkRoot 'cwd_log\k4r.log'), (Join-Path $WorkRoot 'b\r_log\k4r.log'))) {
        if (Test-Path $candidate) {
            $lines.Add("LOG`t$candidate")
            foreach ($l in ([System.IO.File]::ReadAllText($candidate) -split "`r?`n")) { $lines.Add("  |$l") }
        }
        else { $lines.Add("NOLOG`t$candidate") }
    }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k4_ropts.out.txt'), (Tokens $text), $utf8)
    Write-Host "k4_ropts.out.txt         $($runs.Count) runs"
}

if ($skipped -notcontains 'client') {
    $lines = New-Object System.Collections.Generic.List[string]
    $clientSrc = Join-Path $WorkRoot 'clientsrc'
    if (Test-Path $clientSrc) { Remove-Item -LiteralPath $clientSrc -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here 'src\k4client') -Destination $clientSrc -Recurse
    $buildLog = Join-Path $WorkRoot 'client_build.txt'
    & dotnet build (Join-Path $clientSrc 'k4client.csproj') -c Release -o (Join-Path $WorkRoot 'client') `
        "-p:K4Out=$WorkRoot\b\dn" "-p:MatlabRoot=$MatlabRoot" -nologo *> $buildLog
    $lines.Add("BUILD`texit $LASTEXITCODE")
    & dotnet build-server shutdown *> $null
    $exe = Join-Path $WorkRoot 'client\k4client.exe'
    if (Test-Path $exe) {
        Run-Quiet $exe @() (Join-Path $WorkRoot 'client') @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache_client') } $lines
    }
    else { $lines.Add("  no program (see $buildLog)") }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k4_client.out.txt'), (Tokens $text), $utf8)
    Write-Host "k4_client.out.txt        done"
}
