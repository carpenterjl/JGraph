# Runs dispatch_probe12.m once per argument pattern (see its header) and writes dispatch-probe12.csv.
# A crashing MATLAB writes to stderr, which must not stop the loop.
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$matlab = 'C:\Program Files\MATLAB\R2025b\bin\matlab.exe'
$header = (Get-Content (Join-Path $here 'dispatch-probe11.csv') -TotalCount 1)
$verdicts = @()
foreach ($p in 1..37) {
    $out = Join-Path $here 'probe12-verdict.txt'
    if (Test-Path $out) { Remove-Item $out }
    & $matlab -noFigureWindows -batch "P = $p; run('$here\dispatch_probe12.m')" *> $null
    $v = if (Test-Path $out) { Get-Content $out -Raw } else { 'builtin-err' }
    Write-Host "pattern $p : $v"
    $verdicts += $v.Trim()
}
if (Test-Path (Join-Path $here 'probe12-verdict.txt')) { Remove-Item (Join-Path $here 'probe12-verdict.txt') }
$file = Join-Path $here 'dispatch_work12\vrjoystick.m'
if (Test-Path $file) { Remove-Item $file }
[IO.File]::WriteAllText((Join-Path $here 'dispatch-probe12.csv'), $header + "`n" + 'vrjoystick,' + ($verdicts -join ',') + "`n")
