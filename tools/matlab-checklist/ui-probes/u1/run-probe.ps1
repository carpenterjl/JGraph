param([Parameter(Mandatory)][string]$Name, [int]$TimeoutSec = 240, [switch]$WithWindows, [switch]$Interactive)
# Runs one R2025b probe in this folder; kills only the MATLAB process it started on timeout.
# Headless (-noFigureWindows -batch) unless asked otherwise. The u1w_* probes need a display, open
# windows on screen and drive them, and run only with the user's leave:
#   -WithWindows  plain -batch, which displays figures;
#   -Interactive  -nodesktop -r, because -batch is "non-interactive" even with a display and the
#                 blocking dialogs (questdlg, inputdlg, listdlg) refuse there
#                 (MATLAB:hg:NonInteractiveFunctionSupport). Output comes through -logfile.
$dir = $PSScriptRoot
$out = Join-Path $dir "$Name.out"
$err = Join-Path $dir "$Name.err"
if ($Interactive) {
    Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue
    $statement = "try, $Name; catch e, disp(getReport(e)); end; exit"
    $arguments = @('-nodesktop', '-nosplash', '-wait', '-logfile', "`"$out`"", '-sd', "`"$dir`"", '-r', "`"$statement`"")
    $p = Start-Process -FilePath "C:\Program Files\MATLAB\R2025b\bin\matlab.exe" -ArgumentList $arguments -PassThru `
        -RedirectStandardError $err
} else {
    $arguments = @('-batch', $Name, '-sd', "`"$dir`"")
    if (-not $WithWindows) { $arguments = @('-noFigureWindows') + $arguments }
    $p = Start-Process -FilePath "C:\Program Files\MATLAB\R2025b\bin\matlab.exe" `
        -ArgumentList $arguments `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
}
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    # matlab.exe launches MATLAB.exe children; stop the tree we started.
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    "TIMEOUT after $TimeoutSec s" | Add-Content $out
}
Get-Content $out
if ((Test-Path $err) -and (Get-Item $err).Length -gt 0) { '--- stderr ---'; Get-Content $err }
