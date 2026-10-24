<#
.SYNOPSIS
    Runs a windowed program and watches its windows (compiler and SDK plan, stage K6), for
    record-k6.ps1 (R2025b's programs, with the user's leave) and run-k6.ps1 (JGraph's).

.DESCRIPTION
    Dot-source it, then call Invoke-K6Windows with the cases. Each program runs through
    run-quiet.ps1 -AllowWindowed in a PowerShell of its own, while this one lists, every 100 ms, the
    top-level windows of the program and of every process under it and prints each change with the
    seconds since the start. A case's action closes its window only through UI Automation patterns
    (WindowPattern.Close, InvokePattern on a named button), never by synthesized input:

      none                     the program ends on its own
      close:<title>            WindowPattern.Close once the window has been shown 1.5 s
      press:<title>:<button>   InvokePattern on the button, or WindowPattern.Close after 8 s without one
      dismiss                  any window shown 4 s: its texts, then WindowPattern.Close

    The program's transcript (stdout, STDERR, EXIT, SECONDS) and its K6_LOG lines follow, the log's
    clock turned into seconds since the start.
#>

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('JGraphK6.Windows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JGraphK6
{
public sealed class Seen
{
    public IntPtr Handle; public int Pid; public string Process; public string Class; public string Title;
    public bool Visible; public int Width; public int Height;
    public string Key { get { return string.Format("{0:x} {1} {2} '{3}' {4} {5}x{6}", Handle.ToInt64(), Process, Class, Title, Visible ? "shown" : "hidden", Width, Height); } }
}

public static class Windows
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Entry
    {
        public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr s, ref Entry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr s, ref Entry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    // The processes named rootName and every process under them, with their image names.
    public static Dictionary<int, string> Tree(string rootName)
    {
        var parent = new Dictionary<int, int>();
        var names = new Dictionary<int, string>();
        IntPtr snap = CreateToolhelp32Snapshot(2, 0);
        var e = new Entry { Size = (uint)Marshal.SizeOf(typeof(Entry)) };
        if (Process32FirstW(snap, ref e))
        {
            do { parent[(int)e.Pid] = (int)e.Parent; names[(int)e.Pid] = e.Exe; } while (Process32NextW(snap, ref e));
        }
        CloseHandle(snap);
        var tree = new Dictionary<int, string>();
        foreach (var p in names) if (string.Equals(p.Value, rootName, StringComparison.OrdinalIgnoreCase)) tree[p.Key] = p.Value;
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var p in parent)
                if (!tree.ContainsKey(p.Key) && tree.ContainsKey(p.Value) && p.Key != p.Value) { tree[p.Key] = names[p.Key]; grew = true; }
        }
        return tree;
    }

    public static List<Seen> Of(Dictionary<int, string> tree)
    {
        var found = new List<Seen>();
        EnumWindows((h, l) =>
        {
            int pid; GetWindowThreadProcessId(h, out pid);
            string name;
            if (!tree.TryGetValue(pid, out name)) return true;
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            var t = new StringBuilder(512); GetWindowText(h, t, 512);
            Rect r; GetWindowRect(h, out r);
            bool visible = IsWindowVisible(h);
            if (!visible && t.Length == 0) return true;
            found.Add(new Seen { Handle = h, Pid = pid, Process = name, Class = c.ToString(), Title = t.ToString(),
                Visible = visible, Width = r.R - r.L, Height = r.B - r.T });
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
}
'@
}

function Texts([System.Windows.Automation.AutomationElement] $element) {
    $all = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    @(foreach ($e in $all) {
        $n = $e.Current.Name
        if ($n) { "{0}:{1}" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $n }
    }) | Select-Object -Unique
}

# Each case: name, program (under -Build), arguments, action. Answers the lines to record.
function Invoke-K6Windows([object[]] $Cases, [string] $Build, [string] $WorkRoot, [string] $Cache, [string[]] $Only = @()) {
    $here = $PSScriptRoot
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $onlyCases = @($Only | Where-Object { $_ })
    $build = $Build
    $cache = $Cache
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($c in $cases) {
        if ($onlyCases.Count -and $onlyCases -notcontains $c[0]) { continue }
        $exe = Join-Path $build $c[1]
        $lines.Add("CASE`t$($c[0])")
        $lines.Add("RUN`t$($c[1]) $($c[2] -join ' ')")
        $lines.Add("ACTION`t$($c[3])")
        if (-not (Test-Path -LiteralPath $exe)) { $lines.Add("  not built"); continue }
        $log = Join-Path $WorkRoot ("log_{0}.txt" -f $c[0])
        Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
        $transcript = Join-Path $WorkRoot ("transcript_{0}.txt" -f $c[0])
        Remove-Item -LiteralPath $transcript -ErrorAction SilentlyContinue
        $json = ConvertTo-Json -InputObject @($c[2]) -Compress
        $envJson = ConvertTo-Json -InputObject @{ MCR_CACHE_ROOT = $cache; K6_LOG = $log } -Compress
        # run-quiet in a process of its own, so this one can watch the windows meanwhile.
        $runner = Join-Path $WorkRoot 'run-one.ps1'
        [System.IO.File]::WriteAllText($runner, @"
`$e = ConvertFrom-Json '$envJson'
`$h = @{}; foreach (`$p in `$e.PSObject.Properties) { `$h[`$p.Name] = `$p.Value }
& '$here\run-quiet.ps1' -Exe '$exe' -ArgumentsBase64 '$([Convert]::ToBase64String($utf8.GetBytes($json)))' -Environment `$h -TimeoutSeconds 240 -OutFile '$transcript' -AllowWindowed *> `$null
"@, $utf8)
        $started = Get-Date
        $proc = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner) -WindowStyle Hidden -PassThru
        $null = $proc.Handle
        $rootName = Split-Path -Leaf $exe
        $previous = @{}
        $shownSince = @{}
        $acted = $false
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        while (-not $proc.HasExited) {
            $tree = [JGraphK6.Windows]::Tree($rootName)
            $now = @{}
            foreach ($w in [JGraphK6.Windows]::Of($tree)) { $now[$w.Handle.ToInt64()] = $w }
            $t = $watch.Elapsed.TotalSeconds
            foreach ($k in $now.Keys) {
                $w = $now[$k]
                if (-not $previous.ContainsKey($k) -or $previous[$k].Key -ne $w.Key) { $lines.Add(("  t={0,6:N2} + {1}" -f $t, $w.Key)) }
                if ($w.Visible -and -not $shownSince.ContainsKey($k)) { $shownSince[$k] = $t }
                if (-not $w.Visible) { $shownSince.Remove($k) }
            }
            foreach ($k in @($previous.Keys)) { if (-not $now.ContainsKey($k)) { $lines.Add(("  t={0,6:N2} - {1:x}" -f $t, $k)); $shownSince.Remove($k) } }
            $previous = $now
            $action = $c[3]
            foreach ($k in @($shownSince.Keys)) {
                $w = $now[$k]
                $age = $t - $shownSince[$k]
                try {
                    if ($action -like 'close:*' -and $w.Title -eq $action.Substring(6) -and $age -ge 1.5 -and -not $acted) {
                        $el = [System.Windows.Automation.AutomationElement]::FromHandle($w.Handle)
                        $lines.Add(("  t={0,6:N2} ACTION WindowPattern.Close '{1}'" -f $t, $w.Title))
                        $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                        $acted = $true
                    }
                    elseif ($action -like 'press:*' -and -not $acted) {
                        $parts = $action.Split(':')
                        if ($w.Title -eq $parts[1] -and $age -ge 1.5) {
                            $el = [System.Windows.Automation.AutomationElement]::FromHandle($w.Handle)
                            $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $parts[2])
                            $button = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
                            if ($button) {
                                $lines.Add(("  t={0,6:N2} ACTION InvokePattern '{1}' in '{2}'" -f $t, $parts[2], $w.Title))
                                $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                                $acted = $true
                            }
                            elseif ($age -ge 8) {
                                $lines.Add(("  t={0,6:N2} ACTION no '{1}' found: {2}; WindowPattern.Close" -f $t, $parts[2], ((Texts $el) -join ' | ')))
                                $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                                $acted = $true
                            }
                        }
                    }
                    elseif ($action -eq 'dismiss' -and $age -ge 4) {
                        $el = [System.Windows.Automation.AutomationElement]::FromHandle($w.Handle)
                        $lines.Add(("  t={0,6:N2} ACTION dismiss '{1}': {2}" -f $t, $w.Title, ((Texts $el) -join ' | ')))
                        $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                        $shownSince.Remove($k)
                    }
                }
                catch { $lines.Add(("  t={0,6:N2} ACTION failed: {1}" -f $t, $_.Exception.Message)) }
            }
            Start-Sleep -Milliseconds 100
        }
        $lines.Add(("  t={0,6:N2} runner exited" -f $watch.Elapsed.TotalSeconds))
        if (Test-Path -LiteralPath $transcript) {
            foreach ($l in ([System.IO.File]::ReadAllText($transcript) -split "`r?`n")) { if ($l -ne '') { $lines.Add("  $l") } }
        }
        else { $lines.Add("  no transcript") }
        if (Test-Path -LiteralPath $log) {
            foreach ($l in [System.IO.File]::ReadAllLines($log)) {
                $parts = $l.Split("`t", 2)
                $at = [datetime]::ParseExact($parts[0], 'HH:mm:ss.fff', $null)
                $at = $started.Date.Add($at.TimeOfDay)
                $lines.Add(("  LOG t={0,6:N2} {1}" -f ($at - $started).TotalSeconds, $parts[1]))
            }
        }
        else { $lines.Add("  LOG none") }
        Write-Host ("  {0}" -f $c[0])
    }
    return , $lines
}
