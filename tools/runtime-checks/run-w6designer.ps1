<#
.SYNOPSIS
    Stage W6's window check (App Designer window plan): VIEW > ZOOM, the canvas's floating zoom
    panel, Fit to View and DISPLAY's Resizing Hints, in the JGraph application, driven through UI
    Automation patterns only (no synthesized input).

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot> and starts JGraph.Application.exe with
    -r "appdesigner('<app>')". Checks, on the VIEW tab: Fit to View, Zoom In and Zoom Out enabled and
    Reset Zoom disabled at 100%; the floating zoom panel opened from its magnifier showing "100%";
    Zoom In taking it to 112% (R2025b's next step), enabling Reset Zoom and showing the pan message;
    Zoom Out twice to 90%; Reset Zoom back to 100%; the panel's + and Reset; Fit to View; Zoom In to
    400%, where it is disabled; Resizing Hints turned on and kept as
    matlab.appdesigner.designview.ShowResizingHints. Pictures go into <WorkRoot>.

    What takes a mouse or keys - Ctrl+wheel, Space (a tap fits, a hold pans), the middle button's
    pan, Ctrl+Plus, Ctrl+Minus and Ctrl+Alt+0, the canvas's menu, a resize showing its size - is left
    to AppDesignerZoomTests and the canvas's own code.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back
    after; the application is ended with Stop-Process.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W6Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
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
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$failures = New-Object System.Collections.Generic.List[string]

& dotnet build (Join-Path $repo 'src\JGraph.Application\JGraph.Application.csproj') -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the application did not build" }
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"

# W4's app (w4\W4App.m, a plain-text app JGraph's writer made); a copy is opened.
$app = Join-Path $WorkRoot 'W4App.m'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Destination $app -Force

$settingsFile = Join-Path $env:APPDATA 'JGraph\settings.json'
$settingsCopy = Join-Path $WorkRoot 'settings.before.json'
$hadSettings = Test-Path -LiteralPath $settingsFile
if ($hadSettings) { Move-Item -LiteralPath $settingsFile -Destination $settingsCopy -Force }

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [scriptblock] $match, [int] $seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [W6Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Find-In([IntPtr] $hwnd, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        try {
            $found = $A::FromHandle($hwnd).FindFirst($Tree::Subtree, $condition)
            if ($found) { return $found }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

# An element by id in any of the process's windows (a popup is a window of its own).
function Find-InProcess([string] $id, [int] $seconds = 5) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W6Native]::Windows([uint32] $script:appId)) {
            $found = Find-In $entry.Key $id 0
            if ($found) { return $found }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Pattern($element, $pattern) {
    if (-not $element) { return $null }
    $found = $null
    if ($element.TryGetCurrentPattern($pattern, [ref] $found)) { return $found }
    return $null
}

function Invoke-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Invoke()
    return $true
}

function Enabled([string] $id) {
    $element = Find-In $script:hwnd $id 3
    return ($null -ne $element -and $element.Current.IsEnabled)
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W6Native+RECT
    [void] [W6Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W6Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}


function Percent() {
    $percent = Find-In $script:hwnd 'Canvas.ZoomPanel.Percent' 2
    if ($percent) { return $percent.Current.Name }
    return ''
}

function Click([string] $id) {
    $ok = Invoke-Element (Find-In $script:hwnd $id)
    Start-Sleep -Milliseconds 500
    return $ok
}

$forward = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
$script:appId = $p.Id
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner('W4App.m') brings the designer up on the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 1500

    $viewTab = Pattern (Find-In $script:hwnd 'appDesignerToolstrip.designviewTab') ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $viewTab) 'the VIEW tab'
    if ($viewTab) { $viewTab.Select() }
    Start-Sleep -Milliseconds 600

    Check (Enabled 'appDesignerToolstrip.layoutTab.zoom.fit') 'Fit to View is enabled'
    Check (Enabled 'appDesignerToolstrip.layoutTab.zoom.zoomIn') 'Zoom In is enabled'
    Check (Enabled 'appDesignerToolstrip.layoutTab.zoom.zoomOut') 'Zoom Out is enabled'
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.zoom.resetZoom')) 'Reset Zoom is disabled at 100%'
    $hints = Find-In $script:hwnd 'appDesignerToolstrip.layoutTab.view.size'
    $toggle = Pattern $hints ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($hints -and $hints.Current.IsEnabled -and $toggle -and $toggle.Current.ToggleState -eq 'Off') 'Resizing Hints is enabled, off (R2025b factory)'

    # --- the floating zoom panel -----------------------------------------------------------------------
    Check (Click 'Canvas.ZoomPanel.Toggle') "the zoom panel's magnifier"
    Check ((Percent) -eq '100%') "the panel shows 100% (got '$(Percent)')"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'zoom-100.png')) 'a picture at 100%'

    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.layoutTab.zoom.zoomIn')) 'VIEW > Zoom In'
    # The message shows for 2 seconds: looked for and pictured first.
    $message = Find-In $script:hwnd 'Canvas.PanMessage' 1
    Check ($message -and -not $message.Current.IsOffscreen -and $message.Current.Name -eq 'Hold Space and drag to pan') "a zoom says how to pan ('$($message.Current.Name)')"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'zoom-112.png')) 'a picture at 112% with the pan message'
    Check ((Percent) -eq '112%') "R2025b's next step, 112% (got '$(Percent)')"
    Check (Enabled 'appDesignerToolstrip.layoutTab.zoom.resetZoom') 'Reset Zoom is enabled off 100%'

    Check (Click 'appDesignerToolstrip.layoutTab.zoom.zoomOut') 'VIEW > Zoom Out'
    Check (Click 'appDesignerToolstrip.layoutTab.zoom.zoomOut') 'VIEW > Zoom Out again'
    Check ((Percent) -eq '90%') "100% then 90% (got '$(Percent)')"
    Check (Click 'appDesignerToolstrip.layoutTab.zoom.resetZoom') 'VIEW > Reset Zoom'
    Check ((Percent) -eq '100%') "back to 100% (got '$(Percent)')"
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.zoom.resetZoom')) 'Reset Zoom is disabled again'

    Check (Click 'Canvas.ZoomPanel.ZoomIn') "the panel's +"
    Check ((Percent) -eq '112%') "the panel's + steps as Zoom In does (got '$(Percent)')"
    Check (Click 'Canvas.ZoomPanel.Reset') "the panel's Reset"
    Check ((Percent) -eq '100%') "the panel's Reset (got '$(Percent)')"

    Check (Click 'appDesignerToolstrip.layoutTab.zoom.fit') 'VIEW > Fit to View'
    $fit = Percent
    Check ($fit -match '^\d+%$') "Fit to View gives a level ($fit)"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'fit.png')) 'a picture after Fit to View'

    for ($i = 0; $i -lt 25 -and (Enabled 'appDesignerToolstrip.layoutTab.zoom.zoomIn'); $i++) {
        [void] (Click 'appDesignerToolstrip.layoutTab.zoom.zoomIn')
    }
    Check ((Percent) -eq '400%') "Zoom In stops at 400% (got '$(Percent)')"
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.zoom.zoomIn')) 'Zoom In is disabled at 400%'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'zoom-400.png')) 'a picture at 400%'

    # --- Resizing Hints ------------------------------------------------------------------------------
    if ($toggle) { $toggle.Toggle() }
    Start-Sleep -Milliseconds 600
    Check ($toggle -and $toggle.Current.ToggleState -eq 'On') 'Resizing Hints turned on'
    $saved = if (Test-Path -LiteralPath $settingsFile) { Get-Content -LiteralPath $settingsFile -Raw } else { '' }
    Check ($saved.Contains('matlab.appdesigner.designview.ShowResizingHints')) 'the settings keep it as matlab.appdesigner.designview.ShowResizingHints'
    Check (-not $p.HasExited) 'the window is still up'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
