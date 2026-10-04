param([string[]]$Titles = @('JG-DPI-figure','JG-DPI-uifigure'), [int]$WaitSec = 60)
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
"@
[void][W]::SetProcessDpiAwarenessContext([IntPtr](-4))  # per-monitor v2: physical pixels
$deadline = (Get-Date).AddSeconds($WaitSec); $found = @{}
while ((Get-Date) -lt $deadline -and $found.Count -lt $Titles.Count) {
  [W]::EnumWindows({ param($h, $l)
    if ([W]::IsWindowVisible($h)) { $sb = New-Object Text.StringBuilder 256; [void][W]::GetWindowText($h, $sb, 256); $t = $sb.ToString()
      if ($Titles -contains $t -and -not $found.ContainsKey($t)) { $r = New-Object W+RECT; $c = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r); [void][W]::GetClientRect($h, [ref]$c)
        $script:found[$t] = "$t window=$($r.R-$r.L)x$($r.B-$r.T) at ($($r.L),$($r.T)) client=$($c.R)x$($c.B) dpi=$([W]::GetDpiForWindow($h))" } }
    $true }, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 500
}
$found.Values
