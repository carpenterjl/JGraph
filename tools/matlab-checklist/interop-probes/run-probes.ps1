<#
.SYNOPSIS
    Runs the interop probes through R2025b, one MATLAB process each, and writes <probe>.out.txt
    next to each probe (interop plan, step 0).

.DESCRIPTION
    A probe is a script named probe_*.m in this folder. It prints one line per question,
    key<TAB>result, through ip_pr (an expression's value) and ip_px (a statement's display
    text). Each probe runs in a fresh MATLAB, because dotnetenv has to come before the first .NET
    call and a loaded library or assembly cannot be unloaded.

    Run tools/interop/stage-assets.ps1 first: the probes load the staged assets.
    No probe opens a window.

.PARAMETER Probes
    Probe names (without .m) to run. Omit to run all of them.

.PARAMETER MatlabExe
    The MATLAB launcher. Defaults to R2025b's.

.PARAMETER TimeoutSeconds
    How long one probe may run before it is killed. The output so far is kept, with a TIMEOUT line.

.EXAMPLE
    powershell -File tools/matlab-checklist/interop-probes/run-probes.ps1 -Probes probe_net_members
#>
[CmdletBinding()]
param(
    [string[]] $Probes,
    [string] $MatlabExe = 'C:\Program Files\MATLAB\R2025b\bin\matlab.exe',
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$all = Get-ChildItem -LiteralPath $here -Filter 'probe_*.m' | ForEach-Object { $_.BaseName }
if ($Probes) {
    # powershell -File passes "a,b" as one string.
    $Probes = @($Probes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    foreach ($p in $Probes) { if ($all -notcontains $p) { Write-Error "no probe named '$p' in $here" } }
    $all = $Probes
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($probe in $all) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $statement = "cd('$here'); $probe"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        # Not -Wait: in Windows PowerShell 5.1 that also waits for MathWorksServiceHost.
        $proc = Start-Process -FilePath $MatlabExe -ArgumentList @('-batch', "`"$statement`"") `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $timedOut = -not $proc.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            # The launcher starts MATLAB.exe as a child; kill the whole tree or the child keeps
            # running and holds the output files.
            & taskkill.exe /PID $proc.Id /T /F | Out-Null
            Start-Sleep -Seconds 2
        }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += "STDERR`t" + (($errText.Trim() -split "`r?`n") -join ' | ') + "`n" }
        if ($timedOut) { $text += "TIMEOUT`tafter $TimeoutSeconds s`n" }
        $text = ($text -replace "`r`n", "`n")
        [System.IO.File]::WriteAllText((Join-Path $here "$probe.out.txt"), $text, $utf8)
        Write-Host ("{0,-32} {1,6:N1} s  exit {2}{3}" -f $probe, $watch.Elapsed.TotalSeconds, $(if ($timedOut) { '-' } else { $proc.ExitCode }), $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally {
        Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue
    }
}
