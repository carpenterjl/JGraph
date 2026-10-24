<#
.SYNOPSIS
    Records what R2025b's code protection does (compiler and SDK plan, stage K3b): pcode and P-files,
    the secret functions in a -batch session, mcc's -s, -j, -k and -J builds, and how the programs
    those builds make run. Writes recordings\k3b_pcode.out.txt, k3b_secrets.out.txt,
    k3b_build.out.txt and k3b_run.out.txt; and k3b_secrets_build.out.txt and k3b_secrets_run.out.txt
    from probe_k3b_secrets_build's program, run with and without K3B_SECRET.

.DESCRIPTION
    probe_k3b_pcode and probe_k3b_secrets run under matlab -batch -noFigureWindows; probe_k3b_build
    builds the programs (nothing is run there); then each program runs through run-quiet.ps1 under
    its own MCR_CACHE_ROOT. The builds use -C, so each archive is extracted beside its program
    (<exedir>\k3b_main_mcr), and the main lists what the extracted copy holds. k3b_main turns the
    beep off first; nothing fails before main.

    -J cannot embed a secret here: R2025b's vault is unavailable under -batch ("Failed to embed
    secrets into CTF"). The run of the plain build with K3B_SECRET in its environment measures the
    override a deployed getSecret honours.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'pcode', 'secrets', 'build', 'run' and 'secretsrun' to leave out.
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
    # A build's uuid and an -s container's names change every build.
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

if ($skipped -notcontains 'pcode') { Run-Matlab "cd('$here'); probe_k3b_pcode('$WorkRoot\pw')" 'k3b_pcode.out.txt' }
if ($skipped -notcontains 'secrets') { Run-Matlab "cd('$here'); probe_k3b_secrets" 'k3b_secrets.out.txt' }
if ($skipped -notcontains 'build') { Run-Matlab "cd('$here'); probe_k3b_build('$WorkRoot\bw')" 'k3b_build.out.txt' }

if ($skipped -notcontains 'run') {
    $lines = New-Object System.Collections.Generic.List[string]
    $runs = @(
        @('plain', @{}), @('plain', @{ K3B_SECRET = 'from-environment' }), @('s', @{}), @('j', @{}),
        @('sj', @{}), @('k', @{}), @('Jnone', @{}), @('Jempty', @{}))
    foreach ($r in $runs) {
        $name = $r[0]
        $exe = Join-Path $WorkRoot "bw\b_$name\k3b_main.exe"
        $envs = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot "cache_$name") }
        foreach ($k in $r[1].Keys) { $envs[$k] = $r[1][$k] }
        $label = $name + $(if ($r[1].Count) { ' ' + (($r[1].Keys | ForEach-Object { "$_=$($r[1][$_])" }) -join ' ') } else { '' })
        $lines.Add("RUN`t$label")
        if (-not (Test-Path $exe)) { $lines.Add("  no program"); continue }
        $mcr = Join-Path $WorkRoot "bw\b_$name\k3b_main_mcr"
        if (Test-Path $mcr) { Remove-Item -LiteralPath $mcr -Recurse -Force }
        $transcript = [System.IO.Path]::GetTempFileName()
        & (Join-Path $here 'run-quiet.ps1') -Exe $exe -Environment $envs -TimeoutSeconds 300 -OutFile $transcript *> $null
        $out = [System.IO.File]::ReadAllText($transcript)
        Remove-Item -LiteralPath $transcript
        foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
    }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k3b_run.out.txt'), (Tokens $text), $utf8)
    Write-Host "k3b_run.out.txt          $($runs.Count) runs"
}

# Every secret function in a compiled program, with and without the environment's value.
if ($skipped -notcontains 'secretsrun') {
    Run-Matlab "cd('$here'); probe_k3b_secrets_build('$WorkRoot\sw')" 'k3b_secrets_build.out.txt'
    $lines = New-Object System.Collections.Generic.List[string]
    $exe = Join-Path $WorkRoot 'sw\b\k3bs_main.exe'
    foreach ($r in @(@('none', @{}), @('set', @{ K3B_SECRET = 'from-environment' }))) {
        $envs = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot "cache_s_$($r[0])") }
        foreach ($k in $r[1].Keys) { $envs[$k] = $r[1][$k] }
        $lines.Add("RUN`t$($r[0])")
        if (-not (Test-Path $exe)) { $lines.Add("  no program"); continue }
        $transcript = [System.IO.Path]::GetTempFileName()
        & (Join-Path $here 'run-quiet.ps1') -Exe $exe -Environment $envs -TimeoutSeconds 300 -OutFile $transcript *> $null
        $out = [System.IO.File]::ReadAllText($transcript)
        Remove-Item -LiteralPath $transcript
        foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
    }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k3b_secrets_run.out.txt'), (Tokens $text), $utf8)
    Write-Host "k3b_secrets_run.out.txt  2 runs"
}
