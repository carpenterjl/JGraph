<#
.SYNOPSIS
    Stage W8b's window check (App Designer window plan): Run's drop-down with an app's argument sets, the field, an argument's error on it, a set taken out and put back,
    the picture Run takes, a startup function's error in an alert, in the JGraph application, each edit saved and read back from the file.

.DESCRIPTION
    Copies the .mlapps w8\W8aBin.mlapp, which takes no arguments, and w8\W8bApp.mlapp, which takes one, into <WorkRoot> and starts JGraph.Application.exe with both. Everything is set through UI Automation
    patterns, what a drop-down holds found in the process's popup windows. Nothing is typed into or clicked at whatever window is in front.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back after; the
    application is ended with Stop-Process.

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
public static class W8bNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll")] public static extern bool GetKeyboardState(byte[] keys);
    [DllImport("user32.dll")] public static extern bool SetKeyboardState(byte[] keys);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
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

$none = Join-Path $WorkRoot 'W8aBin.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\W8aBin.mlapp') -Destination $none -Force
$bin = Join-Path $WorkRoot 'W8bApp.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\W8bApp.mlapp') -Destination $bin -Force
$app = $bin

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
        foreach ($entry in [W8bNative]::Windows([uint32] $processId)) {
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

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W8bNative+RECT
    [void] [W8bNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W8bNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# A component of the active app selected in the component browser, by its name.
function Select-Component([string] $name) {
    $browser = Find-In $script:hwnd 'ComponentBrowser'
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

# The canvas given the keyboard (UI Automation's SetFocus), and the designer's window the foreground one.
function Focus-Canvas {
    $canvas = Find-By $script:hwnd $A::ClassNameProperty 'DesignCanvas' 5
    if (-not $canvas) { return $false }
    try { $canvas.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 400
    return ([W8bNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W8bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W8bNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W8bNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W8bNative]::Windows([uint32] $p.Id)) {
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

# The menu key on the canvas, then the item at the end of $path (submenus expanded on the way) invoked.
function Run-Menu([string[]] $path) {
    if (-not (Focus-Canvas)) { return $false }
    if (-not (Post-Key $VK_APPS)) { return $false }
    for ($i = 0; $i -lt $path.Count; $i++) {
        $item = Find-Menu ('Menu.' + $path[$i])
        if (-not $item) { return $false }
        if ($i -lt $path.Count - 1) {
            $expand = Pattern $item ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if (-not $expand) { return $false }
            $expand.Expand()
            Start-Sleep -Milliseconds 500
        }
        elseif (-not (Invoke-Element $item)) { return $false }
    }
    return $true
}

function Save-App([string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

# The text of a component's element in the saved layout, up to its own closing tag.
function Saved-Element([string] $file, [string] $kind, [string] $name) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($file), "(?s)<$kind name='$name'[^>]*>(.*?)</$kind>")
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value
}

# Whether a row stands in the browser now, by its name; waits a little for one wanted.
function Row-Shown-In-Browser([string] $name, [int] $seconds = 2) {
    $browser = Find-In $script:hwnd 'ComponentBrowser' 3
    if (-not $browser) { return $false }
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        if ($browser.FindFirst($Tree::Subtree, $condition)) { return $true }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $false
}

# A row given the keyboard, selected first.
function Focus-Row([string] $name) {
    if (-not (Select-Component $name)) { return $false }
    $browser = Find-In $script:hwnd 'ComponentBrowser' 3
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $item = $browser.FindFirst($Tree::Subtree, $condition)
    if (-not $item) { return $false }
    try { $item.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 300
    return ([W8bNative]::GetForegroundWindow() -eq $script:hwnd)
}

# The browser's menu on a row, by the menu key; then the item at the end of $path invoked.
function Run-RowMenu([string] $row, [string[]] $path) {
    if (-not (Focus-Row $row)) { return $false }
    if (-not (Post-Key $VK_APPS)) { return $false }
    if (-not (Find-Menu ('Menu.' + $path[0]) 2)) {
        if (-not (Focus-Row $row)) { return $false }
        if (-not (Post-Key $VK_APPS)) { return $false }
    }

    for ($i = 0; $i -lt $path.Count; $i++) {
        $item = Find-Menu ('Menu.' + $path[$i])
        if (-not $item) { return $false }
        if ($i -lt $path.Count - 1) {
            (Pattern $item ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
            Start-Sleep -Milliseconds 500
        }
        elseif (-not (Invoke-Element $item)) { return $false }
    }
    return $true
}

function Set-Text($element, [string] $text) {
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $false }
    $value.SetValue($text)
    Start-Sleep -Milliseconds 500
    return $true
}

$VK_RETURN = 0x0D; $VK_ESCAPE = 0x1B
$WM_COMMAND = 0x111; $IDOK = 1; $VK_CONTROL = 0x11; $VK_LCONTROL = 0xA2; $VK_A = 0x41

# A key posted to the designer's own window with Ctrl down for the designer's input thread alone, and put back after.
function Post-CtrlKey([int] $vk) {
    if ([W8bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W8bNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W8bNative]::GetCurrentThreadId()
    if (-not [W8bNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W8bNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W8bNative]::SetKeyboardState($keys)
        [void] [W8bNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W8bNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W8bNative]::SetKeyboardState($keys)
    }
    finally { [void] [W8bNative]::AttachThreadInput($me, $thread, $false) }
    return $true
}

function Tab-Says {
    $tab = Find-In $script:hwnd 'PropertiesPanel.Inspector' 3
    if (-not $tab) { return '' }
    return $tab.Current.Name
}

function Saved-Text { return [System.IO.File]::ReadAllText($app) }

function Save-Popup([string] $id, [string] $path) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    foreach ($entry in [W8bNative]::Windows([uint32] $p.Id)) {
        if ($entry.Value -ne '') { continue }
        try { if ($A::FromHandle($entry.Key).FindFirst($Tree::Subtree, $condition)) { return (Save-Picture $entry.Key $path) } }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    return $false
}

function Show-Callbacks {
    $tab = Pattern (Find-In $script:hwnd 'PropertiesPanel.Callbacks' 3) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 500
    return $true
}

function Row-Box([string] $name, [string] $property, [int] $seconds = 3) { return (Find-In $script:hwnd "Callbacks.$name.$property" $seconds) }

function Row-Value([string] $name, [string] $property) {
    $value = Pattern (Row-Box $name $property) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Row-Shown([string] $name, [string] $property) { return ($null -ne (Row-Box $name $property 1)) }

# Text typed in a row, then Enter posted to the box given the keyboard.
function Edit-Callback([string] $name, [string] $property, [string] $text) {
    $box = Row-Box $name $property
    if (-not $box -or -not (Set-Text $box $text)) { return $false }
    try { $box.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 250
    return (Post-Key $VK_RETURN)
}

function In-Code {
    $switch = Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    return ($null -ne $switch -and $switch.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

function To-Design {
    if (In-Code) {
        (Pattern (Find-In $script:hwnd 'DesignView' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
        Start-Sleep -Milliseconds 700
    }
    return (-not (In-Code))
}

function Select-All {
    if (-not (Focus-Canvas)) { return $false }
    return (Post-CtrlKey $VK_A)
}

function Undo-Step {
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton'))
    Start-Sleep -Milliseconds 500
    return $true
}

function Saved-Has([string] $kind, [string] $name, [string] $what) {
    $saved = Saved-Element $app $kind $name
    return ($null -ne $saved -and $saved.Contains($what))
}

function Close-Menu {
    if ([W8bNative]::GetForegroundWindow() -eq $script:hwnd) { [void] (Post-Key $VK_ESCAPE) }
    Start-Sleep -Milliseconds 300
}

# The items of the menu the menu key brings on the canvas, by whether they stand there; the menu is closed after.
function Menu-Has([string[]] $ids) {
    if (-not (Focus-Canvas)) { return $false }
    if (-not (Post-Key $VK_APPS)) { return $false }
    $all = $true
    foreach ($id in $ids) {
        if (-not (Find-Menu ('Menu.' + $id) 2)) { $all = $false }
    }
    Close-Menu
    return $all
}

function Show-Tab([string] $id) {
    $tab = Pattern (Find-In $script:hwnd $id 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 500
    return $true
}

function Show-App([string] $name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
    $item = $A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)
    $tab = Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 900
    return $true
}

function Dismiss-Alert([string] $title) {
    $alert = Find-Window $p.Id { param($t) $t -eq $title } 6
    if (-not $alert) { return $false }
    [void] [W8bNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    return $true
}

function Open-Run {
    $arrow = Find-In $script:hwnd 'appDesignerToolstrip.editorTab.run.run.arrow' 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Close-Run {
    $arrow = Find-In $script:hwnd 'appDesignerToolstrip.editorTab.run.run.arrow' 2
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($expand) { try { $expand.Collapse() } catch { } }
    Start-Sleep -Milliseconds 400
}

function Set-Name([int] $i) { return "appDesignerToolstrip.editorTab.run.run.ArgumentSet$i" }

function Item-Name([string] $id, [int] $seconds = 3) {
    $item = Find-Menu $id $seconds
    if (-not $item) { return $null }
    return $item.Current.Name
}

# The field's text set, and the item clicked, which runs with it.
function Run-Field([string] $text) {
    $field = Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem.field'
    if (-not (Set-Text $field $text)) { return $false }
    return (Invoke-Element (Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem'))
}

function Status-Says {
    $status = Find-In $script:hwnd 'AppDesigner.Status' 2
    if (-not $status) { return '' }
    return $status.Current.Name
}

function Wait-Status([string] $starts, [int] $seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        if ((Status-Says).StartsWith($starts)) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

function App-Window([string] $title, [int] $seconds = 15) { return (Find-Window $p.Id { param($t) $t -eq $title } $seconds) }

function Parts-Of([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try { return @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
}

$forwardBin = $none.Replace('\', '/')
$forwardApp = $bin.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forwardBin'); appdesigner('$forwardApp')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- the drop-down of an app that takes no arguments ----------------------------------------------
    Check (Show-App 'W8aBin.mlapp') 'the app that takes none'
    Check (Show-Tab 'appDesignerToolstrip.layoutTab') 'the CANVAS tab'
    Check (Open-Run) 'Run''s arrow opens its drop-down'
    Check ((Item-Name (Set-Name 1)) -eq 'Run: W8aBin') 'Run: W8aBin is its first item'
    $field = Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem' 2
    Check ($null -ne $field -and -not $field.Current.IsEnabled) 'the field waits for arguments'
    Check ((Item-Name 'appDesignerToolstrip.run.inputArgumentsItem') -eq 'Add App Input Arguments...') 'Add App Input Arguments...'
    Check ($null -eq (Find-Menu ((Set-Name 1) + '.remove') 1)) 'the empty set has no remove button'
    Check (Save-Popup 'appDesignerToolstrip.run.inputArgumentsItem' (Join-Path $WorkRoot 'run-none.png')) 'a picture of it'

    # App Input Arguments from there, at Code Options.
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.run.inputArgumentsItem')) 'that item'
    $details = Find-Window $p.Id { param($t) $t -eq 'App Details' } 6
    Check ($null -ne $details) 'opens App Details'
    Check ($null -ne $details -and $null -ne (Find-In $details.Key 'CodeOptions.InputArguments' 3)) 'with the arguments'
    if ($details) {
        $cancel = $A::FromHandle($details.Key).FindFirst($Tree::Subtree, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Cancel')))
        [void] (Invoke-Element $cancel)
    }

    # --- the field --------------------------------------------------------------------------------
    Check (Show-App 'W8bApp.mlapp') 'the app that takes one'
    Check (Show-Tab 'appDesignerToolstrip.layoutTab') 'the CANVAS tab'
    Check (Open-Run) 'the drop-down again'
    $field = Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem' 2
    Check ($null -ne $field -and $field.Current.IsEnabled) 'the field is enabled'
    Check ((Item-Name 'appDesignerToolstrip.run.inputArgumentsItem') -eq 'Edit App Input Arguments...') 'Edit App Input Arguments...'
    Check (Save-Popup 'appDesignerToolstrip.run.inputArgumentsItem' (Join-Path $WorkRoot 'run-field.png')) 'a picture of it'
    $stamp = (Get-Item -LiteralPath $bin).LastWriteTimeUtc
    Check (Run-Field '21 * 2') 'a set typed and run'
    Check ($null -ne (App-Window 'n is 42')) 'the app runs with it, evaluated'
    Check ((Get-Item -LiteralPath $bin).LastWriteTimeUtc -ne $stamp) 'Run saved the file first'

    # Run's own picture: the first run after a save.
    Start-Sleep -Milliseconds 1500
    Check ((Parts-Of $bin) -contains 'metadata/appScreenshot.png') 'Run took a picture of the window'

    # The set is first in the drop-down, with a remove button; F5 would run it.
    Check (Open-Run) 'the drop-down'
    Check ((Item-Name (Set-Name 1)) -eq 'Run: W8bApp(21 * 2)') 'the set comes first'
    Check ((Item-Name (Set-Name 2)) -eq 'Run: W8bApp') 'the empty set after it'
    Check ($null -ne (Find-Menu ((Set-Name 1) + '.remove') 2)) 'a set has a remove button'
    Check (Save-Popup 'appDesignerToolstrip.run.inputArgumentsItem' (Join-Path $WorkRoot 'run-sets.png')) 'a picture of them'

    # An error in an argument: the drop-down opens by itself, with the set on the field and the error.
    Check (Run-Field 'notDefinedAnywhere') 'a set that cannot be evaluated'
    $said = Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem.error' 10
    Check ($null -ne $said) 'the drop-down opens with the error'
    Check ($null -ne $said -and $said.Current.Name.Contains('notDefinedAnywhere')) "it says what is wrong ($($said.Current.Name))"
    $value = Pattern (Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem.field') ([System.Windows.Automation.ValuePattern]::Pattern)
    Check ($null -ne $value -and $value.Current.Value -eq 'notDefinedAnywhere') 'with the set in the field'
    Check (Save-Popup 'appDesignerToolstrip.run.inputArgumentsItem' (Join-Path $WorkRoot 'run-error.png')) 'a picture of it'

    # Too many for the startup function: the arguments' fault too.
    Check (Run-Field '1, 2') 'too many'
    $said = Find-Menu 'appDesignerToolstrip.run.runWithArgumentsListItem.error' 10
    Check ($null -ne $said) 'the error on the field again'

    # An item runs its set; its button takes it out and the drop-down comes again.
    Check ((Item-Name (Set-Name 2)) -eq 'Run: W8bApp(notDefinedAnywhere)') 'the set before it second now'
    Check (Invoke-Element (Find-Menu ((Set-Name 2) + '.remove'))) 'its remove button'
    Start-Sleep -Milliseconds 800
    Check ((Item-Name (Set-Name 2)) -eq 'Run: W8bApp(21 * 2)') 'it is gone, the drop-down open again'
    Close-Run
    Check (Undo-Step) 'Undo'
    Check (Open-Run) 'the drop-down'
    Check ((Item-Name (Set-Name 1)) -eq 'Run: W8bApp(notDefinedAnywhere)') 'puts it back, first'
    Check (Invoke-Element (Find-Menu (Set-Name 3))) 'an item runs its set'
    Check ($null -ne (App-Window 'n is 42')) 'the app, one window'
    Check (Save-App $bin) 'Save writes the sets'

    # --- a startup function's error is the app's, in an alert -----------------------------------------
    Check (Open-Run) 'the drop-down'
    $items = 1..8 | ForEach-Object { Find-Menu (Set-Name $_) 1 } | Where-Object { $_ }
    $plain = $items | Where-Object { $_.Current.Name -eq 'Run: W8bApp' } | Select-Object -First 1
    Check ($null -ne $plain) 'Run with no arguments is there'
    Check (Invoke-Element $plain) 'its item'
    Check (Dismiss-Alert 'App Designer') 'a startup function short of its argument: an alert'
    Check ((Status-Says).StartsWith('Cannot run with provided input arguments')) 'R2025b''s words'
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
