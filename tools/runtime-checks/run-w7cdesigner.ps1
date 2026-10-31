<#
.SYNOPSIS
    Stage W7c's window check (App Designer window plan): the inspector's editors - a text area's lines, toggle buttons, a
    font's lists, an alignment's buttons, a colour's picker, a position's fields, and behind their kebabs the tables of Items,
    ticks, a gauge's colours, a table's columns and a date picker's dates, and View Format - in the JGraph application, each edit
    saved and read back from the file; a label sized to its text.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a drop-down, a discrete knob, a gauge, a table, a date picker
    and a numeric field added, and starts JGraph.Application.exe with -r "appdesigner('<app>')". Rows and cells are set
    through UI Automation patterns. Keys are window messages posted to the designer's own window handle, never input sent to
    whatever window is in front, and only once the designer's window is checked to be the foreground one.

    A file picker's dialog is not driven, nor is a mouse: what opens from a kebab is reached through patterns.

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
public static class W7cNative {
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

# W4's app (w4\W4App.m: buttons D, A and B, the label L and an edit field), with a spinner Sp and a lamp Lp added.
$app = Join-Path $WorkRoot 'W4App.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$declared = @('Dd matlab.ui.control.DropDown', 'Kn matlab.ui.control.DiscreteKnob', 'Gg matlab.ui.control.Gauge', 'Tb matlab.ui.control.Table', 'Dp matlab.ui.control.DatePicker', 'Nf matlab.ui.control.NumericEditField') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = $text.Replace("        D          matlab.ui.control.Button$nl", "        D          matlab.ui.control.Button$nl" + ($declared -join ''))
$layout = @(
    "            <DropDown name='Dd'>",
    "                <Position>[300 400 100 22]</Position>",
    "            </DropDown>",
    "            <DiscreteKnob name='Kn'>",
    "                <Position>[300 300 60 60]</Position>",
    "            </DiscreteKnob>",
    "            <Gauge name='Gg'>",
    "                <Position>[420 300 120 120]</Position>",
    "            </Gauge>",
    "            <Table name='Tb'>",
    "                <ColumnName>{'Column 1'; 'Column 2'; 'Column 3'; 'Column 4'}</ColumnName>",
    "                <Position>[20 20 300 120]</Position>",
    "            </Table>",
    "            <DatePicker name='Dp'>",
    "                <Position>[340 200 150 22]</Position>",
    "            </DatePicker>",
    "            <NumericEditField name='Nf'>",
    "                <Position>[340 160 100 22]</Position>",
    "            </NumericEditField>") -join $nl
$text = $text.Replace("        </Children>$nl    </UIFigure>", "$layout$nl        </Children>$nl    </UIFigure>")
[System.IO.File]::WriteAllText($app, $text)

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
        foreach ($entry in [W7cNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W7cNative+RECT
    [void] [W7cNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W7cNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W7cNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W7cNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W7cNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W7cNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W7cNative]::Windows([uint32] $p.Id)) {
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
function Row-Shown([string] $name, [int] $seconds = 2) {
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
    return ([W7cNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W7cNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W7cNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W7cNative]::GetCurrentThreadId()
    if (-not [W7cNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W7cNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W7cNative]::SetKeyboardState($keys)
        [void] [W7cNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W7cNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W7cNative]::SetKeyboardState($keys)
    }
    finally { [void] [W7cNative]::AttachThreadInput($me, $thread, $false) }
    return $true
}

function Tab-Says {
    $tab = Find-In $script:hwnd 'PropertiesPanel.Inspector' 3
    if (-not $tab) { return '' }
    return $tab.Current.Name
}

function Inspector-Value([string] $name) {
    $value = Pattern (Find-In $script:hwnd ('Inspector.' + $name) 3) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Shown-In-Inspector([string] $name, [int] $seconds = 1) { return ($null -ne (Find-In $script:hwnd ('Inspector.' + $name) $seconds)) }

# Text typed in a row of the inspector: set, then Enter posted to the box given the keyboard.
function Edit-Row([string] $name, [string] $text) {
    $box = Find-In $script:hwnd ('Inspector.' + $name) 3
    if (-not $box -or -not (Set-Text $box $text)) { return $false }
    try { $box.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 250
    return (Post-Key $VK_RETURN)
}

# An Expander of the inspector opened, by its title.
function Open-Group([string] $title) {
    $inspector = Find-In $script:hwnd 'Inspector' 3
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $title)
    $group = $inspector.FindFirst($Tree::Subtree, $condition)
    $expand = Pattern $group ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    return $true
}

function Saved-Text { return [System.IO.File]::ReadAllText($app) }

function Set-Toggled([string] $id) {
    $toggle = Pattern (Find-In $script:hwnd $id 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $toggle) { return $false }
    $toggle.Toggle()
    Start-Sleep -Milliseconds 500
    return $true
}

function Toggled([string] $id) {
    $toggle = Pattern (Find-In $script:hwnd $id 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    return ($null -ne $toggle -and $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

# A row's text set and given up: the focus moves off it to the search, which commits it.
function Edit-Lines([string] $name, [string] $text) {
    $box = Find-In $script:hwnd ('Inspector.' + $name) 3
    if (-not $box) { return $false }
    try { $box.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 250
    if (-not (Set-Text $box $text)) { return $false }
    (Find-In $script:hwnd 'Inspector.Search').SetFocus()
    Start-Sleep -Milliseconds 700
    return $true
}

# A cell of what a kebab opened, reached through the windows of the application.
function Find-Editor([string] $id, [int] $seconds = 4) { return Find-Menu $id $seconds }

function Open-Rich([string] $name) {
    $toggle = Pattern (Find-In $script:hwnd ('Inspector.' + $name + '.Rich') 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $toggle) { return $false }
    if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle(); Start-Sleep -Milliseconds 300 }
    $toggle.Toggle()
    Start-Sleep -Milliseconds 700
    return $true
}

function Close-Rich([string] $name) {
    $toggle = Pattern (Find-In $script:hwnd ('Inspector.' + $name + '.Rich') 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle -and $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle(); Start-Sleep -Milliseconds 400 }
}

# A cell typed into, committed by moving the keyboard to another cell.
function Edit-Cell([string] $id, [string] $text, [string] $away) {
    $cell = Find-Editor $id
    if (-not $cell) { return $false }
    try { $cell.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 200
    if (-not (Set-Text $cell $text)) { return $false }
    $next = Find-Editor $away
    if (-not $next) { return $false }
    try { $next.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 600
    return $true
}

function Cell-Text([string] $id) {
    $value = Pattern (Find-Editor $id 2) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Press([string] $id) {
    $e = Find-Editor $id
    if (-not $e) { return $false }
    return (Invoke-Element $e)
}

function Pick([string] $id) {
    $select = Pattern (Find-Editor $id) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $select) { return $false }
    $select.Select()
    Start-Sleep -Milliseconds 600
    return $true
}

function Tick([string] $id) {
    $toggle = Pattern (Find-Editor $id) ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $toggle) { return $false }
    $toggle.Toggle()
    Start-Sleep -Milliseconds 600
    return $true
}

# The keyboard given to a cell, which selects its row.
function Focus-Cell([string] $id) {
    $cell = Find-Editor $id
    if (-not $cell) { return $false }
    try { $cell.SetFocus() } catch { return $false }
    Start-Sleep -Milliseconds 300
    return $true
}

# What a kebab opened stands in a window of its own, with no title: a picture of the one holding $id.
function Save-Popup([string] $id, [string] $path) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    foreach ($entry in [W7cNative]::Windows([uint32] $p.Id)) {
        if ($entry.Value -ne '') { continue }
        try { if ($A::FromHandle($entry.Key).FindFirst($Tree::Subtree, $condition)) { return (Save-Picture $entry.Key $path) } }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    return $false
}

function Undo-Step {
    if (-not (Focus-Canvas)) { return $false }
    return (Post-CtrlKey 0x5A)
}

function Saved-Has([string] $kind, [string] $name, [string] $what) {
    if (-not (Save-App $app)) { return $false }
    $saved = Saved-Element $app $kind $name
    return ($null -ne $saved -and $saved.Contains($what))
}

function Saved-Lacks([string] $kind, [string] $name, [string] $what) {
    if (-not (Save-App $app)) { return $false }
    $saved = Saved-Element $app $kind $name
    return ($null -ne $saved -and -not $saved.Contains($what))
}

function Position-Of([string] $name) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($app), "(?s)name='$name'[^>]*>.*?<Position>\[([^\]]*)\]</Position>")
    if (-not $m.Success) { return $null }
    return @($m.Groups[1].Value.Split(' ') | ForEach-Object { [double] $_ })
}

$forward = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    $front = Focus-Canvas
    Check $front 'the canvas has the keyboard, the designer in front'
    if (-not $front) { throw 'the designer is not the foreground window: no key is posted' }

    # --- a label's text, sized to it; its font's editors ------------------------------------------------------
    Check (Select-Component 'L') 'the label selected'
    Check (Save-App $app) 'saved as it stands'
    $was = Position-Of 'L'
    Check (Edit-Lines 'Text' 'A label a good deal longer than it was') 'its text typed'
    Check (Saved-Has 'Label' 'L' 'A label a good deal longer') 'saved'
    $now = Position-Of 'L'
    Check ($now[2] -gt $was[2]) "it grew to its text ($($was[2]) to $($now[2]))"
    Check ($now[0] -eq $was[0]) 'from where it was'
    Check (Undo-Step) 'Ctrl+Z'
    Check (Select-Component 'L') 'selected again'
    Check (Saved-Lacks 'Label' 'L' 'A label a good deal longer') 'the text is off the file'
    $back = Position-Of 'L'
    Check ($back[2] -eq $was[2]) 'and its size is back, in one step'
    Check (Edit-Lines 'Text' "two`nlines") 'two lines typed'
    Check (Saved-Has 'Label' 'L' 'two') 'both are saved'
    Check ((Inspector-Value 'Text') -eq 'two,lines') "the row says them with a comma ($(Inspector-Value 'Text'))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'label.png')) 'a picture'
    Check (Undo-Step) 'taken off'

    Check (Select-Component 'L') 'the label again'
    Check (Open-Group 'Font and Color') 'its Font and Color group'
    Check (Set-Toggled 'Inspector.FontWeight') 'bold pressed'
    Check (Toggled 'Inspector.FontWeight') 'it is on'
    Check (Saved-Has 'Label' 'L' 'bold') 'bold saved'
    Check (Set-Toggled 'Inspector.FontAngle') 'italic pressed'
    Check (Saved-Has 'Label' 'L' 'italic') 'italic saved'
    $names = Find-In $script:hwnd 'Inspector.FontName' 3
    Check ($null -ne $names -and $names.Current.ClassName -eq 'ComboBox') 'the font''s name is a list'
    $sizes = Find-In $script:hwnd 'Inspector.FontSize' 3
    Check ($null -ne $sizes -and $sizes.Current.ClassName -eq 'ComboBox') 'its size too'
    Check (Edit-Lines 'FontSize' '24') 'size 24 typed'
    Check (Saved-Has 'Label' 'L' '24') 'saved'
    $tall = Position-Of 'L'
    Check ($tall[3] -gt 22) "it is taller ($($tall[3]))"

    # An alignment's buttons.
    Check (Open-Group 'Text') 'its Text group'
    $center = Find-In $script:hwnd 'Inspector.HorizontalAlignment.center' 3
    Check ($null -ne $center) 'the alignment''s center button'
    Check (Set-Toggled 'Inspector.HorizontalAlignment.center') 'pressed'
    Check (Toggled 'Inspector.HorizontalAlignment.center') 'it is on'
    Check (-not (Toggled 'Inspector.HorizontalAlignment.left')) 'left is off'
    Check (Saved-Has 'Label' 'L' 'center') 'saved'

    # A colour: by its numbers, a word, and the picker.
    Check (Edit-Lines 'FontColor' '0.2,0.4,0.6') 'a colour typed'
    Check (Saved-Has 'Label' 'L' '0.2 0.4 0.6') 'saved'
    Check ((Inspector-Value 'FontColor') -eq '0.20,0.40,0.60') "the row says it as R2025b does ($(Inspector-Value 'FontColor'))"
    Check (Edit-Lines 'FontColor' 'red') 'a colour''s name'
    Check (Saved-Has 'Label' 'L' '1 0 0') 'saved as numbers'
    Check (Invoke-Element (Find-In $script:hwnd 'Inspector.FontColor.Picker' 3)) 'the picker opened'
    Check (Save-Popup 'Inspector.FontColor.Standard.#0000ff' (Join-Path $WorkRoot 'picker.png')) 'a picture'
    Check (Press 'Inspector.FontColor.Standard.#0000ff') 'blue chosen'
    Check (Saved-Has 'Label' 'L' '0 0 1') 'saved'

    # A position's fields.
    Check (Open-Group 'Position') 'its Position group'
    Check (Open-Rich 'Position') 'its kebab'
    Check (Edit-Cell 'Editor.Fields.x' '33' 'Editor.Fields.y') 'x typed'
    Check (Saved-Has 'Label' 'L' '[33 ') 'saved'
    Close-Rich 'Position'

    # --- a drop-down's items ---------------------------------------------------------------------------------
    Check (Select-Component 'Dd') 'the drop-down selected'
    Check ((Inspector-Value 'Items') -like 'Option 1,Option 2*') "its items run together ($(Inspector-Value 'Items'))"
    Check (Open-Rich 'Items') 'the Items table'
    Check ((Cell-Text 'Editor.Items.Row0.Text') -eq 'Option 1') 'its first row'
    Check (Save-Popup 'Editor.Items.Add' (Join-Path $WorkRoot 'items.png')) 'a picture'
    Check (Focus-Cell 'Editor.Items.Row3.Text') 'its last row picked'
    Check (Press 'Editor.Items.Add') 'plus'
    Check ((Cell-Text 'Editor.Items.Row4.Text') -eq 'Option 5') 'Option 5 was added'
    Check (Saved-Has 'DropDown' 'Dd' 'Option 5') 'saved'
    Check (Edit-Cell 'Editor.Items.Row4.Text' 'Five' 'Editor.Items.Row0.Text') 'it is named'
    Check (Saved-Has 'DropDown' 'Dd' 'Five') 'saved'
    Check (Pick 'Editor.Items.Row4.Selected') 'its radio button'
    Check (Saved-Has 'DropDown' 'Dd' "Value>'Five'") 'the value follows it'
    Check (Edit-Cell 'Editor.Items.Row4.Text' 'Cinq' 'Editor.Items.Row0.Text') 'the selected one is renamed'
    Check (Saved-Has 'DropDown' 'Dd' "Value>'Cinq'") 'the value follows, as one step'
    Check (Press 'Editor.Items.Delete') 'minus on the row picked last'
    Close-Rich 'Items'
    Check (Undo-Step) 'Ctrl+Z'
    Check (Select-Component 'Dd') 'selected again'

    # A text area for the items as well.
    Check (Edit-Lines 'Items' "Red`nGreen`nBlue") 'three lines typed in the row'
    Check (Saved-Has 'DropDown' 'Dd' "{'Red', 'Green', 'Blue'}") 'saved as a row of items'

    # --- a knob's states: at least two ---------------------------------------------------------------------------
    Check (Select-Component 'Kn') 'the discrete knob selected'
    Check (Open-Rich 'Items') 'its table'
    Check (Press 'Editor.Items.Add') 'plus'
    Check (Saved-Has 'DiscreteKnob' 'Kn' 'State 1') 'named State 1, the word for a knob'
    Close-Rich 'Items'

    # --- a gauge's ticks and colours --------------------------------------------------------------------------------
    Check (Select-Component 'Gg') 'the gauge selected'
    Check (Open-Rich 'MajorTicks') 'the ticks table'
    Check ((Cell-Text 'Editor.Ticks.Row1.Tick') -eq '20') 'its second tick'
    Check (Save-Popup 'Editor.Ticks.Add' (Join-Path $WorkRoot 'ticks.png')) 'a picture'
    Check (Edit-Cell 'Editor.Ticks.Row1.Tick' '25' 'Editor.Ticks.Row0.Tick') 'a tick typed'
    Check (Saved-Has 'Gauge' 'Gg' '[0 25 40') 'saved'
    Check (Edit-Cell 'Editor.Ticks.Row1.Tick' '50' 'Editor.Ticks.Row0.Tick') 'one out of order'
    Check ((Cell-Text 'Editor.Ticks.Row1.Tick') -eq '50') 'the table holds it'
    Check (Saved-Has 'Gauge' 'Gg' '[0 25 40') 'and it does not go out'
    Check (Edit-Cell 'Editor.Ticks.Row1.Tick' '30' 'Editor.Ticks.Row0.Tick') 'put right'
    Check (Saved-Has 'Gauge' 'Gg' '[0 30 40') 'it goes'
    Check (Invoke-Element (Find-Editor 'Editor.Ticks.Header0')) 'the ticks'' menu'
    Check (Press 'Editor.Ticks.Auto0') 'Auto'
    Check (Saved-Lacks 'Gauge' 'Gg' 'MajorTicks') 'the ticks are not written'
    Close-Rich 'MajorTicks'

    Check (Open-Rich 'ScaleColors') 'the colours table'
    Check (Press 'Editor.Scale.Add') 'plus'
    Check (Saved-Has 'Gauge' 'Gg' 'ScaleColors') 'a colour saved'
    Check (Saved-Has 'Gauge' 'Gg' '[90 100]') 'with its limits, the top tenth'
    Check (Save-Popup 'Editor.Scale.Add' (Join-Path $WorkRoot 'scale.png')) 'a picture'
    Check (Edit-Cell 'Editor.Scale.Row0.Start' 'abc' 'Editor.Scale.Row0.End') 'a Start that is no number'
    Check ((Cell-Text 'Editor.Scale.Row0.Start') -eq '90') 'goes back'
    Check (Edit-Cell 'Editor.Scale.Row0.Start' '80' 'Editor.Scale.Row0.End') 'a Start typed'
    Check (Saved-Has 'Gauge' 'Gg' '[80 100]') 'saved'
    Close-Rich 'ScaleColors'
    Check (Undo-Step) 'Ctrl+Z'
    Check (Undo-Step) 'and again'
    Check (Select-Component 'Gg') 'selected again'
    Check (Saved-Lacks 'Gauge' 'Gg' 'ScaleColors') 'the colours and limits go together'

    # --- View Format -----------------------------------------------------------------------------------------------
    Check (Select-Component 'Nf') 'the numeric field selected'
    Check (Open-Rich 'ValueDisplayFormat') 'View Format'
    Check (Save-Popup 'Editor.Format.Default' (Join-Path $WorkRoot 'format.png')) 'a picture'
    Check (Pick 'Editor.Format.Integer') 'Integer'
    Check (Saved-Has 'NumericEditField' 'Nf' '%.0f') 'saved'
    Check (Pick 'Editor.Format.Decimal') 'Exactly'
    Check (Saved-Has 'NumericEditField' 'Nf' '%.2f') 'two decimals, saved'
    Check (Edit-Cell 'Editor.Format.Decimals' '4' 'Editor.Format.Default') 'four typed'
    Check (Saved-Has 'NumericEditField' 'Nf' '%.4f') 'saved'
    Close-Rich 'ValueDisplayFormat'
    Check (Edit-Lines 'ValueDisplayFormat' 'nonsense') 'a format that is none'
    $alert = Find-Window $p.Id { param($t) $t -eq 'App Designer' } 5
    Check ($null -ne $alert) 'an alert says so'
    if ($alert) {
        [void] [W7cNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 700
    }
    Check ((Inspector-Value 'ValueDisplayFormat') -eq '%.4f') 'and the row goes back'

    # --- a table's columns ------------------------------------------------------------------------------------------
    Check (Select-Component 'Tb') 'the table selected'
    Check (Open-Rich 'ColumnName') 'the columns table'
    Check ((Cell-Text 'Editor.Columns.Row0.Name') -eq 'Column 1') 'its first column'
    Check (Save-Popup 'Editor.Columns.Add' (Join-Path $WorkRoot 'columns.png')) 'a picture'
    Check (Edit-Cell 'Editor.Columns.Row0.Name' 'Volts' 'Editor.Columns.Row1.Name') 'a name typed'
    Check (Saved-Has 'Table' 'Tb' 'Volts') 'saved'
    Check (Tick 'Editor.Columns.Row1.Auto') 'Auto Width off'
    Check (Saved-Has 'Table' 'Tb' '75') 'the column is 75 wide'
    Check (Tick 'Editor.Columns.Row2.Editable') 'Editable on'
    Check (Saved-Has 'Table' 'Tb' 'ColumnEditable') 'saved'
    Check (Focus-Cell 'Editor.Columns.Row0.Name') 'its first row picked'
    Check (Press 'Editor.Columns.Add') 'plus'
    Check (Saved-Has 'Table' 'Tb' 'Column 2_1') 'a new column after it, its name kept apart'
    Close-Rich 'ColumnName'

    # --- a date picker ------------------------------------------------------------------------------------------------
    Check (Select-Component 'Dp') 'the date picker selected'
    Check (Open-Rich 'DisabledDaysOfWeek') 'the days'
    Check (Save-Popup 'Editor.Days.ClearAll' (Join-Path $WorkRoot 'days.png')) 'a picture'
    Check (Tick 'Editor.Days.Sunday') 'Sunday ticked'
    Check (Tick 'Editor.Days.Saturday') 'Saturday ticked'
    Check (Saved-Has 'DatePicker' 'Dp' '[1 7]') 'saved'
    Check (Press 'Editor.Days.ClearAll') 'Clear All'
    Check (Saved-Lacks 'DatePicker' 'Dp' '[1 7]') 'saved'
    Close-Rich 'DisabledDaysOfWeek'
    Check (Open-Rich 'DisabledDates') 'the dates table'
    Check (Press 'Editor.Dates.Add') 'plus: today'
    Check (Saved-Has 'DatePicker' 'Dp' 'DisabledDates') 'saved'
    Close-Rich 'DisabledDates'

    Check ((Get-Process -Id $p.Id -ErrorAction SilentlyContinue) -ne $null) 'still running'
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force -ErrorAction SilentlyContinue }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
    & dotnet build-server shutdown 2>&1 | Out-Null
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host ("{0} check(s) failed:" -f $failures.Count)
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host ''
Write-Host 'All W7c window checks passed.'
exit 0
