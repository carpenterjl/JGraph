<#
.SYNOPSIS
    Stage W8d's window check (App Designer window plan): the Code View's App Layout pane, its Go To list, what opens for the caret, and the editor's Undo through Add Function, in the JGraph application.

.DESCRIPTION
    Copies the .mlapp w8\\W8bApp.mlapp into <WorkRoot> and starts JGraph.Application.exe with -r "appdesigner('<app>')". Everything is set through UI Automation patterns; Ctrl+Z is a key posted to the
    designer's own window with Ctrl held for that window's input thread alone, once the window is checked to be in front.

    The user's settings file (%APPDATA%\\JGraph\\settings.json) is moved aside before and put back after; the
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
public static class W8dNative {
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

$bin = Join-Path $WorkRoot 'W8bApp.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\\W8bApp.mlapp') -Destination $bin -Force
$app = $bin
$shown = Join-Path $WorkRoot 'W8dShown.m'
[System.IO.File]::WriteAllText($shown, [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m')).Replace('W4App', 'W8dShown'))

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
        foreach ($entry in [W8dNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W8dNative+RECT
    [void] [W8dNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W8dNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W8dNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W8dNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W8dNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W8dNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W8dNative]::Windows([uint32] $p.Id)) {
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
    return ([W8dNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W8dNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W8dNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W8dNative]::GetCurrentThreadId()
    if (-not [W8dNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W8dNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W8dNative]::SetKeyboardState($keys)
        [void] [W8dNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W8dNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W8dNative]::SetKeyboardState($keys)
    }
    finally { [void] [W8dNative]::AttachThreadInput($me, $thread, $false) }
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
    foreach ($entry in [W8dNative]::Windows([uint32] $p.Id)) {
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
    if ([W8dNative]::GetForegroundWindow() -eq $script:hwnd) { [void] (Post-Key $VK_ESCAPE) }
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

function Open-Drop([string] $id) {
    $expand = Pattern (Find-In $script:hwnd $id 5) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function To-Code {
    (Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 1000
    return (In-Code)
}

function Status-Says {
    $status = Find-In $script:hwnd 'AppDesigner.Status' 2
    if (-not $status) { return '' }
    return $status.Current.Name
}

function Code-Of([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try {
        $reader = New-Object System.IO.StreamReader($zip.GetEntry('matlab/document.xml').Open(), [System.Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

$forward = $bin.Replace('\', '/')
$forwardShown = $shown.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forwardShown'); appdesigner('$forward')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- App Layout ------------------------------------------------------------------------------------
    Check ($null -eq (Find-In $script:hwnd 'AppLayout' 1)) 'no App Layout in the Design View'
    Check (To-Code) 'the Code View'
    Check ($null -ne (Find-In $script:hwnd 'AppLayout.Title' 3)) 'App Layout under the code browser'
    $picture = Find-In $script:hwnd 'AppLayout' 3
    Check ($null -ne $picture) 'its picture'
    if ($picture) {
        $box = $picture.Current.BoundingRectangle
        Check ($box.Width -gt 100 -and $box.Width -lt 640) "scaled to the pane ($($box.Width) wide)"
    }
    Check ($null -ne (Find-In $script:hwnd 'CodeBrowser' 2)) 'the code browser still there'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'applayout.png')) 'a picture of the Code View, with its folds'

    # An app with components: the picture has them, and follows a change.
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'W8dShown.m')))
    (Pattern ($A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)) ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 900
    Check (To-Code) 'its Code View'
    Check ($null -ne (Find-In $script:hwnd 'AppLayout' 3)) 'its picture'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'applayout-shown.png')) 'a picture of it'
    (Pattern (Find-In $script:hwnd 'DesignView' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 500
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'W8bApp.mlapp')))
    (Pattern ($A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)) ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 900

    # --- Go To -------------------------------------------------------------------------------------------
    Check (Show-Tab 'appDesignerToolstrip.editorTab') 'the EDITOR tab'
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.gotoButton') 'Go To opens'
    $prefix = 'appDesignerToolstrip.editorTab.navigate.gotoButton.function.'
    foreach ($name in 'W8bApp', 'createComponents', 'delete', 'startupFcn') {
        Check ($null -ne (Find-Menu ($prefix + $name) 8)) "it lists $name"
    }
    Check (Save-Popup ($prefix + 'delete') (Join-Path $WorkRoot 'goto.png')) 'a picture of it'
    $stamp = Status-Says
    Check (Invoke-Element (Find-Menu ($prefix + 'startupFcn'))) 'startupFcn chosen'
    Check (In-Code) 'the Code View stays'
    Check (-not (Status-Says).StartsWith('The code has no function')) 'it found the function'
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.gotoButton') 'Go To again'
    Check (Invoke-Element (Find-Menu ($prefix + 'createComponents'))) 'createComponents chosen, in a closed block'
    Check (-not (Status-Says).StartsWith('The code has no function')) 'found too'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'goto-open.png')) 'a picture of the block opened for the caret'

    # --- Undo through Add Function --------------------------------------------------------------------
    $add = Find-In $script:hwnd 'appDesignerToolstrip.editorTab.insert.addFunctionSplitButton' 3
    if (-not $add) { $add = Find-By $script:hwnd $A::NameProperty 'Function' 3 }
    Check ($null -ne $add) 'Add Function'
    Check (Invoke-Element $add) 'pressed'
    Start-Sleep -Milliseconds 800
    Check (Save-App $bin) 'Save writes it'
    Check ((Code-Of $bin).Contains('function results = func(app)')) 'the function is in the file'
    Check ([W8dNative]::GetForegroundWindow() -eq $script:hwnd) 'the designer in front'
    Check (Post-CtrlKey 0x5A) 'Ctrl+Z, posted to the designer''s window'
    Start-Sleep -Milliseconds 900
    Check (Save-App $bin) 'Save writes it'
    Check (-not (Code-Of $bin).Contains('function results = func(app)')) 'the editor''s Undo took the function back'
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
