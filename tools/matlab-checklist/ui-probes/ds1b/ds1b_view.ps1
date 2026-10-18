param([int]$TimeoutSec = 2400, [string]$Only = '')
# DS1b window check (run with the user's leave, 2026-10-18): starts R2025b's full desktop running
# ds1b_view.m, and each time it says an app is open (rt/view/sync/ready.txt) captures App Designer's
# window with PrintWindow into rt/view/shots/<App>__<variant>.png and acknowledges (sync/ack.txt).
# Stops only the MATLAB it started, if it outlives the timeout.
$dir = $PSScriptRoot
$view = Join-Path $dir 'rt\view'
$sync = Join-Path $view 'sync'
$shots = Join-Path $view 'shots'
New-Item -ItemType Directory -Force $sync, $shots | Out-Null
Remove-Item (Join-Path $sync '*') -ErrorAction SilentlyContinue
$log = Join-Path $dir 'ds1b_view.out'
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Wins {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> All() {
        var list = new List<IntPtr>();
        EnumWindows((h, p) => { if (IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
}
"@

if ($Only) { $env:DS1B_ONLY = $Only } else { Remove-Item Env:DS1B_ONLY -ErrorAction SilentlyContinue }
$before = Get-Process | Where-Object { $_.ProcessName -match 'MATLAB' } | ForEach-Object { $_.Id }
$p = Start-Process -FilePath "C:\Program Files\MATLAB\R2025b\bin\matlab.exe" -PassThru `
    -ArgumentList @('-nosplash', '-logfile', "`"$log`"", '-sd', "`"$dir`"", '-r', 'ds1b_view')

function Designer {
    $pids = Get-Process | Where-Object { $_.ProcessName -match 'MATLAB' } | ForEach-Object { $_.Id }
    foreach ($h in [Wins]::All()) {
        [uint32]$wp = 0; [void][Wins]::GetWindowThreadProcessId($h, [ref]$wp)
        if ($pids -contains [int]$wp -and [Wins]::Text($h) -match 'App Designer') { return $h }
    }
    return $null
}

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$ready = Join-Path $sync 'ready.txt'
while ((Get-Date) -lt $deadline -and -not (Test-Path (Join-Path $sync 'done.txt'))) {
    Start-Sleep -Milliseconds 500
    if (-not (Test-Path $ready)) { continue }
    $name = (Get-Content -LiteralPath $ready -Raw).Trim()
    Remove-Item -LiteralPath $ready
    $h = Designer
    if ($h) {
        $r = New-Object Wins+RECT; [void][Wins]::GetWindowRect($h, [ref]$r)
        $w = $r.R - $r.L; $hh = $r.B - $r.T
        $bmp = New-Object System.Drawing.Bitmap $w, $hh
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $dc = $g.GetHdc(); [void][Wins]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
        $bmp.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    } else {
        "$name`: no App Designer window" | Add-Content (Join-Path $view 'ds1b_view.capture.txt')
    }
    New-Item -ItemType File (Join-Path $sync 'ack.txt') | Out-Null
}
if (-not (Test-Path (Join-Path $sync 'done.txt'))) {
    # the launcher hands over to MATLAB.exe; stop only the MATLAB processes that started with this run
    Get-Process | Where-Object { $_.ProcessName -match 'MATLAB' -and $before -notcontains $_.Id } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    "TIMEOUT after $TimeoutSec s" | Add-Content (Join-Path $view 'ds1b_view.log')
}
Get-Content (Join-Path $view 'ds1b_view.log')
