<#
.SYNOPSIS
    The Hello World app (examples\hello-app) built into a standalone Windows application by JGraph
    and driven as an end user would drive it, through UI Automation patterns (no synthesized input).

.DESCRIPTION
    Builds examples\hello-app\build_hello_app under the runtime's jgraph -batch into <WorkRoot>\build,
    starts HelloApp.exe with JGRAPH_RUNTIME_ROOT naming the runtime, and in its window: reads the
    title, waits for the first plot's status line, sets a name through ValuePattern and presses
    Greet, reads the greeting, sets a frequency and presses Plot, reads the status line, saves a
    picture of the window alone with PrintWindow into <WorkRoot>\hello-app.png (the uiaxes and its
    plot are seen there; the axes has no UI Automation element), and closes the window through
    WindowPattern. The program's exit code and the lines it logged through HELLO_LOG are checked
    and printed.

    A text field commits its text on Return or when the keyboard focus leaves it, so after
    ValuePattern.SetValue the focus is moved to the button with AutomationElement.SetFocus before the
    button is invoked.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1; its jgraph.exe builds the app.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [Parameter(Mandatory = $true)] [string] $Runtime,
    [string] $Jgraph,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class HelloNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    // Every visible top-level window of a process with its title, by Win32 enumeration: the UI
    // Automation root does not list a window the process opens after the client first read it.
    public static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, string>> Windows(uint processId) {
        var found = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, string>>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h)) {
                var sb = new System.Text.StringBuilder(512); GetWindowText(h, sb, sb.Capacity);
                found.Add(new System.Collections.Generic.KeyValuePair<IntPtr, string>(h, sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path
if (-not $Jgraph) { $Jgraph = Join-Path $Runtime 'bin\win64\jgraph.exe' }
if (-not (Test-Path $Jgraph)) { throw "No jgraph.exe at $Jgraph" }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$A = [System.Windows.Automation.AutomationElement]
$failures = New-Object System.Collections.Generic.List[string]
$example = Join-Path $repo 'examples\hello-app'
$build = Join-Path $WorkRoot 'build'

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [string] $name, [int] $seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [HelloNative]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of the process: {0}" -f (([HelloNative]::Windows([uint32] $processId) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
    return $null
}

function Find-ById($root, [string] $id, [int] $seconds = 15) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { return $pattern.Current.Value }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref] $pattern)) { return $pattern.DocumentRange.GetText(-1) }
    return $element.Current.Name
}

function Wait-Text($root, [string] $id, [string] $expected, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $text = Text-Of (Find-ById $root $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 300
    }
    return $text
}

function Invoke-ById($root, [string] $id) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.SetFocus(); $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true }
    catch { Write-Host "    uia: invoking '$id' failed: $($_.Exception.Message)"; return $false }
}

function Set-ById($root, [string] $id, [string] $value) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value); return $true }
    catch { Write-Host "    uia: setting '$id' failed: $($_.Exception.Message)"; return $false }
}

function Save-Picture($window, [string] $path) {
    $hwnd = [IntPtr] $window.Current.NativeWindowHandle
    $rect = New-Object HelloNative+RECT
    [void] [HelloNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [HelloNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# The build, under the runtime's jgraph -batch.
Write-Host '== build'
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
$statement = Join-Path $WorkRoot 'build_statement.m'
[System.IO.File]::WriteAllText($statement, "cd('$($example.Replace('\', '/'))');`nr = build_hello_app('$($build.Replace('\', '/'))');`nfprintf('build\t%s\n', r.BuildType);`n", $utf8)
$out = Join-Path $WorkRoot 'build.out.txt'
$err = Join-Path $WorkRoot 'build.err.txt'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$b = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"run('$($statement.Replace('\', '/'))')`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
$null = $b.Handle
if (-not $b.WaitForExit(300000)) { try { & taskkill.exe /PID $b.Id /T /F 2>&1 | Out-Null } catch { } }
$buildText = (Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content $err -Raw -ErrorAction SilentlyContinue)
foreach ($l in ($buildText -split "`r?`n")) { if ($l) { Write-Host "    $l" } }
Check ($b.ExitCode -eq 0) "jgraph -batch exited 0 (got $($b.ExitCode)) in $([int] $sw.Elapsed.TotalSeconds) s"
$exe = Join-Path $build 'HelloApp.exe'
Check (Test-Path $exe) "the program was built at $exe"
Write-Host ("    files: {0}" -f ((Get-ChildItem $build -Recurse -File | ForEach-Object { $_.FullName.Substring($build.Length + 1) }) -join ' | '))

# The run.
Write-Host '== run'
$log = Join-Path $WorkRoot 'hello_log.txt'
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
$cache = Join-Path $WorkRoot 'cache'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.WorkingDirectory = $build
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['JGRAPH_RUNTIME_ROOT'] = $Runtime
$psi.EnvironmentVariables['MCR_CACHE_ROOT'] = $cache
$psi.EnvironmentVariables['HELLO_LOG'] = $log
$started = Get-Date
$p = [System.Diagnostics.Process]::Start($psi)
try {
    $window = Find-Window $p.Id 'Hello World' 60
    Check ($null -ne $window) "a window titled 'Hello World' opened"
    if ($window) {
        Start-Sleep -Seconds 2
        $title = Text-Of (Find-ById $window 'TitleLabel')
        Check ($title -eq 'Hello, World!') "the title label says 'Hello, World!' (got '$title')"
        $status = Wait-Text $window 'StatusLabel' 'plot sine f=1 a=1 n=400'
        Check ($status -eq 'plot sine f=1 a=1 n=400') "the first plot was drawn at start (status '$status')"
        Check (Set-ById $window 'NameField' 'JGraph') "the name field set to 'JGraph'"
        Check (Invoke-ById $window 'GreetButton') 'Greet invoked'
        $greeting = Wait-Text $window 'GreetingLabel' 'Hello, JGraph!'
        Check ($greeting -eq 'Hello, JGraph!') "the greeting says 'Hello, JGraph!' (got '$greeting')"
        Check (Set-ById $window 'FrequencyField' '3') "the frequency field set to 3"
        Check (Invoke-ById $window 'PlotButton') 'Plot invoked'
        $status = Wait-Text $window 'StatusLabel' 'plot sine f=3 a=1 n=400'
        Check ($status -eq 'plot sine f=3 a=1 n=400') "the plot was drawn again at 3 Hz (status '$status')"
        Start-Sleep -Milliseconds 500
        Check (Save-Picture $window (Join-Path $WorkRoot 'hello-app.png')) "a picture of the window was saved to $(Join-Path $WorkRoot 'hello-app.png')"
        try { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Check $true 'the window closed through WindowPattern' }
        catch { Check $false "the window closed through WindowPattern ($($_.Exception.Message))" }
    }
    $exited = $p.WaitForExit(30000)
    Check $exited 'the program ended after its window closed'
    if ($exited) { Check ($p.ExitCode -eq 0) "exit code 0 (got $($p.ExitCode))" }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
Write-Host '== log'
if (Test-Path $log) {
    $lines = [System.IO.File]::ReadAllLines($log)
    foreach ($l in $lines) {
        $parts = $l.Split("`t", 2)
        $at = $started.Date.Add(([datetime]::ParseExact($parts[0], 'HH:mm:ss.fff', $null)).TimeOfDay)
        Write-Host ("    t={0,6:N2} {1}" -f ($at - $started).TotalSeconds, $parts[1])
    }
    $texts = @($lines | ForEach-Object { $_.Split("`t", 2)[1] })
    # The frequency field commits when the focus moves to Plot, and its ValueChangedFcn draws the
    # wave once before the button draws it again, as R2025b's field would.
    $expected = @('started', 'isdeployed 1', 'plot sine f=1 a=1 n=400', 'greeting Hello, JGraph!', 'plot sine f=3 a=1 n=400', 'plot sine f=3 a=1 n=400', 'closing')
    Check ((($texts -join "`n")) -eq ($expected -join "`n")) 'the log holds the seven expected lines in order'
}
else { Check $false 'the program wrote its log' }

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
