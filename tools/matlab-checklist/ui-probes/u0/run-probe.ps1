param([Parameter(Mandatory)][string]$Name, [int]$TimeoutSec = 240)
# Runs one headless R2025b probe in this folder; kills only the MATLAB process it started on timeout.
$dir = $PSScriptRoot
$out = Join-Path $dir "$Name.out"
$err = Join-Path $dir "$Name.err"
$p = Start-Process -FilePath "C:\Program Files\MATLAB\R2025b\bin\matlab.exe" `
    -ArgumentList @('-noFigureWindows', '-batch', $Name, '-sd', "`"$dir`"") `
    -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    # matlab.exe launches MATLAB.exe children; stop the tree we started.
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    "TIMEOUT after $TimeoutSec s" | Add-Content $out
}
Get-Content $out
if ((Get-Item $err).Length -gt 0) { '--- stderr ---'; Get-Content $err }
