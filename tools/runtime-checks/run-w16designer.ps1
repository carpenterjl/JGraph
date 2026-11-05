<#
.SYNOPSIS
    Stage W16's window check (App Designer window plan): the acceptance app that uses a custom component opened in App Designer,
    the toolstrip's every tab walked - DESIGNER, CANVAS, EDITOR (in the Code View) and VIEW, each read and pictured - the library's
    custom section, the Component Browser, Run making the app's window with the component's button pushed and its event reaching
    the app's callback, and, with -Runtime, the standalone program Share > Standalone Desktop App's script built from the blank
    acceptance app run under the JGraph Runtime, its Scale button pushed and its label read - in the JGraph application.

.DESCRIPTION
    Copies tools\matlab-checklist\ui-probes\w16\rt\accept - W16Counter.mlapp, W16Blank.mlapp, W16Panels.mlapp and W16Composite.mlapp
    with resources\appDesigner.json, written by AppDesignerWindowAcceptanceTests with JGRAPH_W16_OUT naming rt\ (the test is run here when
    the files are missing) - into <WorkRoot>\accept. One launch: -r opens W16Composite.mlapp in App Designer; the designer window is
    found; on the DESIGNER tab New, Open, Save, App Details, Share and Compare are read enabled and the tab pictured; on the CANVAS
    tab the align buttons rest with nothing selected, the component's row selected in the Component Browser enables Reorder and the
    grid toggles are read, pictured; the Code View switched on, the EDITOR tab's Callback, Function, Property, App Input Arguments,
    Find, Go To and Run read enabled, pictured; back in the Design View the VIEW tab pictured; the library's "W16 (Custom)" section holds
    Counter; Run invoked, the app's window "W16 Composite" found, its Count button pushed through InvokePattern and the label read as 5
    (the component's event reached the app's callback), the window closed through WindowPattern. With -Runtime, the program
    rt\accept\release-jgraph\build\W16Blank.exe the JGraph run of w16_share.m built (with W16_RELEASE=release-jgraph) is started with JGRAPH_RUNTIME_ROOT naming the runtime, its
    window "W16 Blank" found, Scale pushed, the label read as 6, pictured, closed, and its exit code checked.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back after; the application is ended with Stop-Process.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1 (optional): the built program is run under it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W16Native {
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

$made = Join-Path $repo 'tools\matlab-checklist\ui-probes\w16\rt\accept'
if (-not (Test-Path -LiteralPath (Join-Path $made 'W16Composite.mlapp'))) {
    $env:JGRAPH_W16_OUT = Split-Path -Parent $made
    & dotnet test (Join-Path $repo 'tests\JGraph.Tests\JGraph.Tests.csproj') -c Debug --nologo --filter 'FullyQualifiedName~AppDesignerWindowAcceptanceTests' | Out-Null
    Remove-Item Env:\JGRAPH_W16_OUT
    if (-not (Test-Path -LiteralPath (Join-Path $made 'W16Composite.mlapp'))) { throw "the test did not write $made" }
}
$accept = Join-Path $WorkRoot 'accept'
if (Test-Path -LiteralPath $accept) { Remove-Item -LiteralPath $accept -Recurse -Force }
New-Item -ItemType Directory -Force $accept | Out-Null
foreach ($name in 'W16Counter.mlapp', 'W16Blank.mlapp', 'W16Panels.mlapp', 'W16Composite.mlapp') { Copy-Item -LiteralPath (Join-Path $made $name) -Destination $accept }
New-Item -ItemType Directory -Force (Join-Path $accept 'resources') | Out-Null
Copy-Item -LiteralPath (Join-Path $made 'resources\appDesigner.json') -Destination (Join-Path $accept 'resources')
$app = Join-Path $accept 'W16Composite.mlapp'

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
        foreach ($entry in [W16Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Find-By([IntPtr] $hwnd, $property, [string] $value, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
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

function Find-In([IntPtr] $hwnd, [string] $id, [int] $seconds = 10) { return Find-By $hwnd $A::AutomationIdProperty $id $seconds }

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
    Start-Sleep -Milliseconds 600
    return $true
}

function Select-Tab([IntPtr] $hwnd, [string] $id) {
    $tab = Pattern (Find-In $hwnd $id 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 700
    return $true
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value) { return $value.Current.Value }
    return $element.Current.Name
}

function Wait-Text([IntPtr] $hwnd, [string] $id, [string] $expected, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $text = $null
    do {
        $text = Text-Of (Find-In $hwnd $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W16Native+RECT
    [void] [W16Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W16Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Enabled([IntPtr] $hwnd, [string] $id, [int] $seconds = 5) {
    $element = Find-In $hwnd $id $seconds
    return ($null -ne $element -and -not $element.Current.IsOffscreen -and $element.Current.IsEnabled)
}

function Shown([IntPtr] $hwnd, [string] $id, [int] $seconds = 5) {
    $element = Find-In $hwnd $id $seconds
    return ($null -ne $element -and -not $element.Current.IsOffscreen)
}

function Disabled([IntPtr] $hwnd, [string] $id, [int] $seconds = 5) {
    $element = Find-In $hwnd $id $seconds
    return ($null -ne $element -and -not $element.Current.IsEnabled)
}

function Start-JGraph([string] $statement) {
    $info = New-Object System.Diagnostics.ProcessStartInfo($exe)
    $info.UseShellExecute = $false
    $info.Arguments = "-r `"$statement`""
    return [System.Diagnostics.Process]::Start($info)
}

# A row of the Component Browser selected by its name.
function Select-Row([IntPtr] $hwnd, [string] $name) {
    $browser = Find-In $hwnd 'ComponentBrowser' 5
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $item = if ($browser) { $browser.FindFirst($Tree::Subtree, $condition) } else { $null }
        $select = Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($select) { $select.Select(); Start-Sleep -Milliseconds 600; return $true }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $false
}

# A control of a running app's window by its name (a button's text), optionally of one control type.
function Find-Named([IntPtr] $hwnd, [string] $name, $controlType = $null, [int] $seconds = 10) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $condition = if ($controlType) { New-Object System.Windows.Automation.AndCondition($nameCondition, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $controlType))) } else { $nameCondition }
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

function Close-Window([IntPtr] $hwnd) {
    $pattern = Pattern ($A::FromHandle($hwnd)) ([System.Windows.Automation.WindowPattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Close()
    Start-Sleep -Milliseconds 800
    return $true
}

$p = Start-JGraph "appdesigner('$($app.Replace('\', '/'))')"
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opened the composite app in App Designer ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500
    # Maximised, so that every section of every tab is laid out in full rather than collapsed into a popup.
    $windowPattern = Pattern ($A::FromHandle($hwnd)) ([System.Windows.Automation.WindowPattern]::Pattern)
    if ($windowPattern -and $windowPattern.Current.CanMaximize) { $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized); Start-Sleep -Milliseconds 1500 }

    # --- DESIGNER ------------------------------------------------------------------------------------------------------------
    Write-Host '== DESIGNER'
    Check (Select-Tab $hwnd 'appDesignerToolstrip.designerTab') 'the DESIGNER tab selected'
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.file.newButton') 'FILE > New'
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.file.openButton') 'FILE > Open'
    Check (Shown $hwnd 'appDesignerToolstrip.designerTab.file.saveSplitButton') 'FILE > Save split button'
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.file.compareFilesButton') 'FILE > Compare'
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'SHARE > App Details'
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.share.shareAllOptions') 'SHARE > Share'
    Check (Enabled $hwnd 'appDesignerToolstrip.qab.runButton') 'the quick access bar''s Run'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'designer-tab.png')) 'a picture of the DESIGNER tab'

    # --- the library and the browser -----------------------------------------------------------------------------------------
    Write-Host '== the library and the Component Browser'
    $counterEntry = Find-In $hwnd 'Palette.W16Counter' 5
    Check ($null -ne $counterEntry) 'the library holds the registered Counter'
    $browser = Find-In $hwnd 'ComponentBrowser' 5
    $rows = if ($browser) { $browser.FindAll($Tree::Subtree, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem))) } else { $null }
    $rowNames = @(); if ($rows) { foreach ($r in $rows) { $rowNames += $r.Current.Name } }
    Check (($rowNames -contains 'W16Counter') -and ($rowNames -contains 'TotalLabel')) "the Component Browser lists the component and the label ($($rowNames -join ', '))"

    # --- CANVAS --------------------------------------------------------------------------------------------------------------
    Write-Host '== CANVAS'
    Check (Select-Tab $hwnd 'appDesignerToolstrip.layoutTab') 'the CANVAS tab selected'
    Check (Disabled $hwnd 'appDesignerToolstrip.layoutTab.align.left') 'ALIGN rests with nothing selected'
    Check (Select-Row $hwnd 'W16Counter') 'the component''s row selected in the Component Browser'
    Check (Enabled $hwnd 'appDesignerToolstrip.layoutTab.arrange.reorderButton') 'ARRANGE > Reorder enabled for the selection'
    Check (Shown $hwnd 'appDesignerToolstrip.layoutTab.arrange.tabOrderButton') 'ARRANGE > Tab Order'
    Check (Enabled $hwnd 'appDesignerToolstrip.layoutTab.file.responsifyPushButton') 'FILE > Convert'
    $inspectorTab = Find-In $hwnd 'PropertiesPanel.Inspector' 5
    Check ($inspectorTab -and $inspectorTab.Current.Name.Contains('W16Counter')) "the inspector's tab names the component's class ($($inspectorTab.Current.Name))"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'canvas-tab.png')) 'a picture of the CANVAS tab with the component selected'

    # --- EDITOR (the Code View) ----------------------------------------------------------------------------------------------
    Write-Host '== EDITOR'
    $switch = Pattern (Find-In $hwnd 'CodeView.Switch' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $switch) 'the Code View switch is there'
    if ($switch) { $switch.Toggle(); Start-Sleep -Milliseconds 1200 }
    Check (Select-Tab $hwnd 'appDesignerToolstrip.editorTab') 'the EDITOR tab selected'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.edit.addCallbackButton') 'INSERT > Callback'
    Check (Shown $hwnd 'appDesignerToolstrip.editorTab.edit.addFunctionSplitButton') 'INSERT > Function'
    Check (Shown $hwnd 'appDesignerToolstrip.editorTab.edit.addPropertySplitButton') 'INSERT > Property'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.edit.addInputArgumentsButton') 'INSERT > App Input Arguments'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.navigate.findButton') 'NAVIGATE > Find'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.navigate.gotoButton') 'NAVIGATE > Go To'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.run') 'RUN > Run'
    Check (Disabled $hwnd 'appDesignerToolstrip.editorTab.run.step') 'RUN > Step rests while nothing is paused'
    Check (Disabled $hwnd 'appDesignerToolstrip.editorTab.run.stop') 'RUN > Stop rests too (the BREAKPOINTS and ERROR HANDLING groups are in the section''s drop-down)'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'editor-tab.png')) 'a picture of the EDITOR tab over the code'

    # --- VIEW ----------------------------------------------------------------------------------------------------------------
    Write-Host '== VIEW'
    $codeSwitch = Pattern (Find-In $hwnd 'CodeView.Switch' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($codeSwitch -and $codeSwitch.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) 'the band reads Code View'
    $designSwitch = Pattern (Find-In $hwnd 'DesignView' 5) ([System.Windows.Automation.TogglePattern]::Pattern) # the band's other button brings the Design View back
    if ($designSwitch -and $designSwitch.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $designSwitch.Toggle(); Start-Sleep -Milliseconds 1200 }
    Check (Select-Tab $hwnd 'appDesignerToolstrip.designviewTab') 'the VIEW tab selected in the Design View'
    Check (Shown $hwnd 'appDesignerToolstrip.layoutTab.view.showGrid') 'VIEW > Show grid'
    Check (Shown $hwnd 'appDesignerToolstrip.layoutTab.view.snapGrid') 'VIEW > Snap to grid'
    Check (Shown $hwnd 'appDesignerToolstrip.layoutTab.view.intervalSpinner') 'VIEW > Interval'
    Check (Enabled $hwnd 'appDesignerToolstrip.layoutTab.zoom.fit') 'ZOOM > Fit to View'
    Check (Enabled $hwnd 'appDesignerToolstrip.layoutTab.zoom.zoomIn') 'ZOOM > Zoom In'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'view-tab.png')) 'a picture of the VIEW tab'

    # --- Run: the component's button pushed in the app's own window --------------------------------------------------------------
    Write-Host '== Run'
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.qab.runButton' 5)) 'Run invoked from the quick access bar'
    $status = Wait-Text $hwnd 'AppDesigner.Status' 'Running W16Composite.' 40
    Check ($status -eq 'Running W16Composite.') "the status says the app runs ($status)"
    $running = Find-Window $p.Id { param($t) $t -eq 'W16 Composite' } 30
    Check ($null -ne $running) 'the app''s window "W16 Composite" is up'
    if ($running) {
        # The button's text is the count: update wrote 0 over 'Count' when the component was made.
        $count = Find-Named $running.Key '0' ([System.Windows.Automation.ControlType]::Button) 10
        Check ($null -ne $count) 'the component''s button is in the window, reading 0 (update ran at construction)'
        Check (Invoke-Element $count) 'the button pushed'
        $label = Find-Named $running.Key '5' ([System.Windows.Automation.ControlType]::Text) 10
        Check ($null -ne $label) 'the label says 5: the component''s event reached the app''s callback'
        $text = Find-Named $running.Key '5' ([System.Windows.Automation.ControlType]::Button) 5
        Check ($null -ne $text) 'the button says 5: the component''s update ran in the same drain'
        Check (Invoke-Element (Find-Named $running.Key '5' ([System.Windows.Automation.ControlType]::Button) 5)) 'the button pushed again'
        Check ($null -ne (Find-Named $running.Key '10' ([System.Windows.Automation.ControlType]::Text) 10)) 'the label says 10'
        Check ($null -ne (Find-Named $running.Key '10' ([System.Windows.Automation.ControlType]::Button) 5)) 'the button says 10'
        Check (Save-Picture $running.Key (Join-Path $WorkRoot 'running-composite.png')) 'a picture of the running app'
        Check (Close-Window $running.Key) 'the app''s window closed through WindowPattern'
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }

# --- the standalone program under the runtime ------------------------------------------------------------------------------------
$built = Join-Path $made 'release-jgraph\build\W16Blank.exe' # w16_share.m run by JGraph with W16_RELEASE=release-jgraph
if ($Runtime) {
    Write-Host "== the standalone program under $Runtime"
    $Runtime = (Resolve-Path -LiteralPath $Runtime).Path
    Check (Test-Path -LiteralPath $built) "w16_share.m's JGraph run built $built"
    if (Test-Path -LiteralPath $built) {
        $info = New-Object System.Diagnostics.ProcessStartInfo($built)
        $info.UseShellExecute = $false
        $info.EnvironmentVariables['JGRAPH_RUNTIME_ROOT'] = $Runtime
        $program = [System.Diagnostics.Process]::Start($info)
        try {
            $window = Find-Window $program.Id { param($t) $t -eq 'W16 Blank' } 60
            Check ($null -ne $window) 'the program''s window "W16 Blank" is up'
            if ($window) {
                Start-Sleep -Milliseconds 1500
                $scale = Find-Named $window.Key 'Scale' ([System.Windows.Automation.ControlType]::Button) 10
                Check ($null -ne $scale) 'the Scale button is in the window'
                Check (Invoke-Element $scale) 'Scale pushed'
                $result = Find-Named $window.Key '6' ([System.Windows.Automation.ControlType]::Text) 10
                Check ($null -ne $result) 'the label says 6 (2 x 3)'
                Check (Save-Picture $window.Key (Join-Path $WorkRoot 'running-blank.png')) 'a picture of the program'
                Check (Close-Window $window.Key) 'the program''s window closed through WindowPattern'
            }
            Check ($program.WaitForExit(30000)) 'the program ended'
            Check ($program.HasExited -and $program.ExitCode -eq 0) "exit code 0 ($($program.ExitCode))"
        }
        finally {
            if (-not $program.HasExited) { Stop-Process -Id $program.Id -Force -ErrorAction SilentlyContinue }
        }
    }
}
else {
    Write-Host '(no -Runtime: the standalone program is not run)'
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
