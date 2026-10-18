# DS0 window session: starts R2025b's full desktop running ds0w_dock.m, waits for a window titled
# "App Designer", then lists every visible top-level window of MATLAB's processes (title, class,
# process, rectangle, owner) and captures each with PrintWindow into rt/. Run with the user's leave.
$dir = $PSScriptRoot
$rt = Join-Path $dir 'rt'
New-Item -ItemType Directory -Force $rt | Out-Null
$log = Join-Path $dir 'ds0w_dock.out'
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> All() {
        var list = new List<IntPtr>();
        EnumWindows((h, p) => { if (IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    public static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
}
"@

$p = Start-Process -FilePath "C:\Program Files\MATLAB\R2025b\bin\matlab.exe" -PassThru `
    -ArgumentList @('-nosplash', '-logfile', "`"$log`"", '-sd', "`"$dir`"", '-r', 'ds0w_dock')

function MatlabPids {
    Get-Process | Where-Object { $_.ProcessName -match 'MATLAB' } | ForEach-Object { $_.Id }
}

$found = $null
$deadline = (Get-Date).AddSeconds(240)
while ((Get-Date) -lt $deadline -and -not $found) {
    Start-Sleep -Seconds 3
    $pids = MatlabPids
    foreach ($h in [Wins]::All()) {
        [uint32]$wp = 0; [void][Wins]::GetWindowThreadProcessId($h, [ref]$wp)
        if ($pids -contains [int]$wp -and [Wins]::Text($h) -match 'App Designer') { $found = $h }
    }
}
if (-not $found) { "no App Designer window within 240 s" | Tee-Object -FilePath (Join-Path $dir 'ds0w_dock.windows.txt'); exit }
Start-Sleep -Seconds 12   # let the start page draw

$pids = MatlabPids
$lines = @()
$i = 0
foreach ($h in [Wins]::All()) {
    [uint32]$wp = 0; [void][Wins]::GetWindowThreadProcessId($h, [ref]$wp)
    if (-not ($pids -contains [int]$wp)) { continue }
    $r = New-Object Wins+RECT; [void][Wins]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -le 1 -or $hh -le 1) { continue }
    $owner = [Wins]::GetWindow($h, 4)
    $proc = (Get-Process -Id $wp).ProcessName
    $i++
    $lines += "{0}: '{1}' class {2} process {3} ({4}) rect [{5} {6} {7} {8}] owner {9}" -f $i, [Wins]::Text($h), [Wins]::Cls($h), $proc, $wp, $r.L, $r.T, $w, $hh, $owner
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [void][Wins]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $rt ("dock_{0}.png" -f $i)), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$lines | Tee-Object -FilePath (Join-Path $dir 'ds0w_dock.windows.txt')
