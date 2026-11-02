<#
.SYNOPSIS
    Stage W9c's window check (App Designer window plan): the VIEW tab - Collapse, Expand, Collapse All, Expand All, Zoom In, Zoom Out, Reset Zoom, the split document and the display toggles - over the Code View, in the JGraph application.

.DESCRIPTION
    Copies the .mlapp w8\\W8bApp.mlapp into <WorkRoot> and starts JGraph.Application.exe with -r "appdesigner('<app>')". Each button is pressed through UI Automation's Invoke and each
    toggle through Toggle; what the editor shows is read from its ItemStatus ("Ln n, Col m; Folds a/b; Zoom n%; Split ...; Line numbers off; Current line on; Datatips on"), the caret put
    on a line by the Go To dialog (W9a). The zoom factor kept in the user's settings file is read from the file.

    The user's settings file (%APPDATA%\\JGraph\\settings.json) is moved aside before and put back after; the application is ended with Stop-Process. Print is not driven (a system dialog).

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
public static class W9cNative {
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

$bin = Join-Path $WorkRoot 'W9cApp.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\\W8bApp.mlapp') -Destination $bin -Force

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
        foreach ($entry in [W9cNative]::Windows([uint32] $processId)) {
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

# Every element with this id, in tree order (R2025b gives two of the DISPLAY toggles one tag).
function Find-All([IntPtr] $hwnd, [string] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    return @($A::FromHandle($hwnd).FindAll($Tree::Subtree, $condition))
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
    Start-Sleep -Milliseconds 600
    return $true
}

function Toggle-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Toggle()
    Start-Sleep -Milliseconds 600
    return $true
}

function Toggle-State($element) {
    $pattern = Pattern $element ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $pattern) { return $null }
    return $pattern.Current.ToggleState
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W9cNative+RECT
    [void] [W9cNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W9cNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W9cNative]::Windows([uint32] $p.Id)) {
            try {
                $found = $A::FromHandle($entry.Key).FindFirst($Tree::Subtree, $condition)
                if ($found) { return $found }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Set-Text($element, [string] $text) {
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $false }
    $value.SetValue($text)
    Start-Sleep -Milliseconds 600
    return $true
}

function In-Code {
    $switch = Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    return ($null -ne $switch -and $switch.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

function To-Code {
    (Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 1000
    return (In-Code)
}

function Show-Tab([string] $id) {
    $tab = Pattern (Find-In $script:hwnd $id 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 500
    return $true
}

function Open-Drop([string] $id) {
    $arrow = Find-In $script:hwnd $id 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Editor-Status {
    $editor = Find-In $script:hwnd 'CodeView.Editor' 3
    if (-not $editor) { return '' }
    return $editor.Current.ItemStatus
}

function Status-Line {
    $m = [regex]::Match((Editor-Status), 'Ln (\d+)')
    if ($m.Success) { return [int] $m.Groups[1].Value }
    return -1
}

# The folds closed and the folds there are, from the status; -1, -1 with none.
function Folds {
    $m = [regex]::Match((Editor-Status), 'Folds (\d+)/(\d+)')
    if ($m.Success) { return @([int] $m.Groups[1].Value, [int] $m.Groups[2].Value) }
    return @(-1, -1)
}

function Zoom-Percent {
    $m = [regex]::Match((Editor-Status), 'Zoom (\d+)%')
    if ($m.Success) { return [int] $m.Groups[1].Value }
    return 100
}

function Split-Shown {
    $m = [regex]::Match((Editor-Status), 'Split ([^;]+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return 'Off'
}

# The caret put on a line by the Go To dialog.
function Go-To([int] $line) {
    if (-not (Show-Tab 'appDesignerToolstrip.editorTab')) { return $false }
    if (-not (Open-Drop 'appDesignerToolstrip.editorTab.navigate.gotoButton')) { return $false }
    $item = Find-Menu 'appDesignerToolstrip.editorTab.navigate.gotoButton.gotoLine' 5
    if (-not $item -or -not (Invoke-Element $item)) { return $false }
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Go To' } 8
    if (-not $dialog) { return $false }
    if (-not (Set-Text (Find-In $dialog.Key 'GoToLine.Line' 5) "$line")) { return $false }
    [void] (Invoke-Element (Find-In $dialog.Key 'GoToLine.OK' 3))
    if (-not (Show-Tab 'appDesignerToolstrip.viewTab')) { return $false }
    return ((Status-Line) -eq $line)
}

function Press([string] $id) { return (Invoke-Element (Find-In $script:hwnd $id 5)) }

function Enabled([string] $id) {
    $button = Find-In $script:hwnd $id 5
    return ($null -ne $button -and $button.Current.IsEnabled)
}

function Settings-Zoom {
    if (-not (Test-Path -LiteralPath $settingsFile)) { return $null }
    $m = [regex]::Match((Get-Content -LiteralPath $settingsFile -Raw), '"matlab\.editor\.displaysettings\.CurrentZoomFactor":\s*"([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

$forward = $bin.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    Check (To-Code) 'the Code View'
    Check (Show-Tab 'appDesignerToolstrip.viewTab') 'the VIEW tab'
    $status = Editor-Status
    Check ($status -match 'Folds \d+/\d+') "the status says the folds ($status)"
    $folds = Folds
    Check ($folds[1] -ge 3) "the class has folds to offer ($($folds[1]))"
    Check ($folds[0] -gt 0 -and $folds[0] -lt $folds[1]) "App Designer's default folds start closed, not all ($($folds[0]) of $($folds[1]))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'folds-default.png')) 'a picture of the default folds'

    # --- Collapse All, Expand All ---------------------------------------------------------------------------
    Check (Press 'AD_EDITOR_FOLD_ALL') 'Collapse All pressed'
    $folds = Folds
    Check ($folds[0] -eq $folds[1]) "every fold closed ($($folds[0]) of $($folds[1]))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'folds-all.png')) 'a picture with every fold closed'
    Check (Press 'AD_EDITOR_UNFOLD_ALL') 'Expand All pressed'
    $folds = Folds
    Check ($folds[0] -eq 0) "every fold open ($($folds[0]) of $($folds[1]))"

    # --- Collapse, Expand at the caret ------------------------------------------------------------------------
    Check (Go-To 13) 'the caret in the startup function'
    Check (Enabled 'AD_EDITOR_FOLD_CURRENT') 'Collapse enabled'
    Check (Press 'AD_EDITOR_FOLD_CURRENT') 'Collapse pressed'
    $folds = Folds
    Check ($folds[0] -eq 1) "one fold closed ($($folds[0]))"
    $line = Status-Line
    Check ($line -lt 13 -and $line -gt 0) "the caret moved up to the fold's first line ($line)"
    Check ((Editor-Status) -match '^Ln \d+, Col 9;') "and to the function keyword past the indent ($(Editor-Status))"
    Check (Press 'AD_EDITOR_FOLD_CURRENT') 'Collapse again on the folded block''s line'
    $folds = Folds
    Check ($folds[0] -eq 1) "nothing more folded, as R2025b folds nothing already folded ($($folds[0]))"
    Check (Press 'AD_EDITOR_UNFOLD_CURRENT') 'Expand pressed'
    $folds = Folds
    Check ($folds[0] -eq 0) "the fold and those around it open ($($folds[0]))"

    # --- Zoom -----------------------------------------------------------------------------------------------
    # The ZOOM section's buttons carry R2025b's tags, the CODE FOLDING section's only their action ids.
    Check ((Zoom-Percent) -eq 100) 'the zoom at 100% to begin with'
    Check (-not (Enabled 'appDesignerToolstrip.editorTab.zoom.resetZoom')) 'Reset Zoom disabled at 100%'
    Check (Press 'appDesignerToolstrip.editorTab.zoom.zoomIn') 'Zoom In pressed'
    Check ((Zoom-Percent) -eq 110) "110% ($(Zoom-Percent))"
    Check (Press 'appDesignerToolstrip.editorTab.zoom.zoomIn') 'Zoom In again'
    Check ((Zoom-Percent) -eq 125) "125% ($(Zoom-Percent))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'zoom-125.png')) 'a picture at 125%'
    Start-Sleep -Milliseconds 800
    Check ((Settings-Zoom) -eq '1.25') "the factor kept in the settings file ($(Settings-Zoom))"
    Check (Press 'appDesignerToolstrip.editorTab.zoom.zoomOut') 'Zoom Out pressed'
    Check ((Zoom-Percent) -eq 110) "110% ($(Zoom-Percent))"
    Check (Enabled 'appDesignerToolstrip.editorTab.zoom.resetZoom') 'Reset Zoom enabled'
    Check (Press 'appDesignerToolstrip.editorTab.zoom.resetZoom') 'Reset Zoom pressed'
    Check ((Zoom-Percent) -eq 100) "100% ($(Zoom-Percent))"
    Start-Sleep -Milliseconds 800
    Check ($null -eq (Settings-Zoom)) 'the factory factor not written'

    # --- Split Document -------------------------------------------------------------------------------------
    Check ((Split-Shown) -eq 'Off') 'one view to begin with'
    Check (Toggle-Element (Find-In $script:hwnd 'appDesignerToolstrip.codeViewTab.split.leftRight' 5)) 'Left/Right pressed'
    Check ((Split-Shown) -eq 'Left/Right') "two views side by side ($(Split-Shown))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'split-leftright.png')) 'a picture of the split'
    Check (Toggle-Element (Find-In $script:hwnd 'appDesignerToolstrip.codeViewTab.split.topBottom' 5)) 'Top/Bottom pressed'
    Check ((Split-Shown) -eq 'Top/Bottom') "one above the other ($(Split-Shown))"
    Check ((Toggle-State (Find-In $script:hwnd 'appDesignerToolstrip.codeViewTab.split.leftRight' 5)) -eq [System.Windows.Automation.ToggleState]::Off) 'Left/Right released'
    Check (Toggle-Element (Find-In $script:hwnd 'appDesignerToolstrip.codeViewTab.split.none' 5)) 'Off pressed'
    Check ((Split-Shown) -eq 'Off') "one view again ($(Split-Shown))"

    # --- The DISPLAY toggles --------------------------------------------------------------------------------
    $display = Find-All $script:hwnd 'appDesignerToolstrip.CodeViewTab.display.highlightCurrentline'
    Check ($display.Count -eq 2) "R2025b's one tag on two toggles ($($display.Count))"
    Check ((Editor-Status) -notmatch 'Line numbers|Current line|Datatips') 'the defaults: numbers on, no highlight, no datatips'
    Check (Toggle-Element (Find-In $script:hwnd 'appDesignerToolstrip.CodeViewTab.display.linenumbers' 5)) 'Line Numbers pressed'
    Check ((Editor-Status) -match 'Line numbers off') "the numbers off ($(Editor-Status))"
    Check (Toggle-Element $display[0]) 'Highlight Current Line pressed'
    Check ((Editor-Status) -match 'Current line on') "the line shaded ($(Editor-Status))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'display.png')) 'a picture without numbers, the line shaded'
    if ($display.Count -eq 2) {
        Check (Toggle-Element $display[1]) 'Datatips pressed'
        Check ((Editor-Status) -match 'Datatips on') "datatips on ($(Editor-Status))"
        Check (Toggle-Element $display[1]) 'Datatips released'
    }
    Check (Toggle-Element (Find-In $script:hwnd 'appDesignerToolstrip.CodeViewTab.display.linenumbers' 5)) 'Line Numbers pressed again'
    Check (Toggle-Element $display[0]) 'Highlight Current Line released'
    Check ((Editor-Status) -notmatch 'Line numbers|Current line|Datatips') "the defaults back ($(Editor-Status))"
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
