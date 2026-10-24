<#
.SYNOPSIS
    Runs probe_k2_depend.m in R2025b (matlab -batch -noFigureWindows) and writes
    recordings\k2_rfp.out.txt and recordings\k2_mcc.out.txt (compiler and SDK plan, stage K2).

.DESCRIPTION
    The probe writes its tree under WorkRoot, asks matlab.codetools.requiredFilesAndProducts about
    it and builds it with mcc -m -C several ways. Nothing built is run. Paths in the recordings are
    tokens: <T> for the tree, <OUT> for WorkRoot, <MATLABROOT>.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Part
    'rfp' or 'mcc' to record one part only.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Part = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)

foreach ($p in @('rfp', 'mcc')) {
    if ($Part -and $Part -ne $p) { continue }
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $statement = "cd('$here'); probe_k2_depend('$WorkRoot', '$p')"
        $proc = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $timedOut = -not $proc.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { & taskkill.exe /PID $proc.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        # The archive keeps a file from outside the main's folder under its full path without the drive.
        $noDrive = $WorkRoot.Substring([System.IO.Path]::GetPathRoot($WorkRoot).Length).Replace('\', '/')
        foreach ($t in @(@($WorkRoot, '<OUT>'), @($WorkRoot.Replace('\', '/'), '<OUT>'), @($noDrive, '<OUT-NODRIVE>'),
                @($MatlabRoot, '<MATLABROOT>'), @($env:USERPROFILE, '<HOME>'))) {
            $text = $text.Replace($t[0], $t[1])
        }
        $target = "k2_$p.out.txt"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), $text, $utf8)
        Write-Host ("{0,-16} exit {1}{2}" -f $target, $proc.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}
