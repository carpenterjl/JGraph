<#
.SYNOPSIS
    Stage W4's window check (App Designer window plan): CANVAS > ALIGN, ARRANGE and SPACE as the
    selection enables them, Reorder and Tab Order run from their drop-downs, in the JGraph
    application, driven through UI Automation patterns only (no synthesized input).

.DESCRIPTION
    Copies a plain-text app into <WorkRoot> - three buttons and a label made out of their places'
    order, and an edit field - and starts JGraph.Application.exe with -r "appdesigner('<app>')". Checks:
    the app opens; on the CANVAS tab, with the figure selected, Align, Same Size, Reorder and the
    SPACE box are disabled and Tab Order enabled; with one button selected in the component browser,
    Reorder is enabled and Align still disabled (it takes two); Reorder > Bring to Front moves the
    button last in the code's order, which Save writes; Tab Order > Apply Auto Tab Order puts the
    components in the order of their rows, left to right, which Save writes; Visualize Tab Order
    runs. Pictures go into <WorkRoot>.

    What takes a mouse or keys - two or more selected on the canvas, the figure's edges, the canvas's
    menu, Ctrl+L and the font keys - is left to AppDesignerArrangeTests, which drive the same
    gestures and commands without a window.

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
public static class W4Native {
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

# The app (w4\W4App.m, a plain-text app JGraph's writer made): D, A, L and B made in that order,
# in two rows - A and B above, L and D below - and an edit field under them. A copy is edited.
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
        foreach ($entry in [W4Native]::Windows([uint32] $processId)) {
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
        foreach ($entry in [W4Native]::Windows([uint32] $script:appId)) {
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
    $rect = New-Object W4Native+RECT
    [void] [W4Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W4Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# A drop-down's item: the drop-down opened, its item invoked in the popup.
function Invoke-DropDownItem([string] $button, [string] $item) {
    $expand = Pattern (Find-In $script:hwnd $button) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    $entry = Find-InProcess $item
    $ok = Invoke-Element $entry
    Start-Sleep -Milliseconds 500
    return $ok
}

# The order the app's components are made in, from the saved file's layout section.
function Saved-Order() {
    $text = [System.IO.File]::ReadAllText($app)
    return (@([regex]::Matches($text, "name='(\w+)'") | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne 'UIFigure' }) -join ',')
}

# CANVAS > Save: the DESIGNER tab's is not in the tree while CANVAS is shown.
function Save-App() {
    $before = (Get-Item -LiteralPath $app).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.layoutTab.file.saveSplitButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $app).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $app).LastWriteTimeUtc -ne $before)
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

    $canvasTab = Pattern (Find-In $script:hwnd 'appDesignerToolstrip.layoutTab') ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $canvasTab) 'the CANVAS tab'
    if ($canvasTab) { $canvasTab.Select() }
    Start-Sleep -Milliseconds 600

    # --- the figure selected ---------------------------------------------------------------------
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.align.left')) 'with the figure selected, Align Left is disabled'
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.size.sizeSplitButton')) 'Same Size is disabled'
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.arrange.reorderButton')) 'Reorder is disabled (the figure does not restack)'
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.spacing.combo')) "SPACE's box is disabled"
    Check (Enabled 'appDesignerToolstrip.layoutTab.arrange.tabOrderButton') 'Tab Order is enabled'
    $combo = Find-In $script:hwnd 'appDesignerToolstrip.layoutTab.spacing.combo'
    $comboValue = Pattern $combo ([System.Windows.Automation.ValuePattern]::Pattern)
    Check ($comboValue -and $comboValue.Current.Value -eq 'Evenly') "SPACE's box says 'Evenly' (got '$($comboValue.Current.Value)')"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'canvas-tab.png')) 'a picture of the CANVAS tab'

    # --- one button selected in the browser --------------------------------------------------------
    $browser = Find-In $script:hwnd 'ComponentBrowser'
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'A')
    $item = if ($browser) { $browser.FindFirst($Tree::Subtree, $condition) } else { $null }
    $select = Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $select) "the component browser lists 'A'"
    if ($select) { $select.Select() }
    Start-Sleep -Milliseconds 600
    Check (Enabled 'appDesignerToolstrip.layoutTab.arrange.reorderButton') 'with A selected, Reorder is enabled'
    Check (-not (Enabled 'appDesignerToolstrip.layoutTab.align.left')) 'Align Left still is not (it takes two)'

    Check (Invoke-DropDownItem 'appDesignerToolstrip.layoutTab.arrange.reorderButton' 'appDesignerToolstrip.layoutTab.arrange.reorder.frontItem') 'Reorder > Bring to Front'
    Check (Save-App) 'Save writes the app'
    Check ((Saved-Order) -eq 'D,L,B,EditField,A') "A is made last, in front (got $(Saved-Order))"
    Check (Enabled 'appDesignerToolstrip.layoutTab.arrange.reorderButton') 'Reorder stays enabled: A can go back again'

    # --- the tab order ---------------------------------------------------------------------------------
    Check (Invoke-DropDownItem 'appDesignerToolstrip.layoutTab.arrange.tabOrderButton' 'appDesignerToolstrip.layoutTab.arrange.tabOrder.autoTabOrderItem') 'Tab Order > Apply Auto Tab Order'
    Start-Sleep -Milliseconds 300
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tab-order.png')) 'a picture of the tab order being shown'
    Check (Save-App) 'Save writes it again'
    Check ((Saved-Order) -eq 'A,B,L,D,EditField') "the components are made row by row, left to right (got $(Saved-Order))"
    Check (Invoke-DropDownItem 'appDesignerToolstrip.layoutTab.arrange.tabOrderButton' 'appDesignerToolstrip.layoutTab.arrange.tabOrder.visualizeTabOrderItem') 'Tab Order > Visualize Tab Order'
    Start-Sleep -Milliseconds 700
    Check (-not $p.HasExited) 'the window is still up'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'visualize.png')) 'a picture of the tab order shown again'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
