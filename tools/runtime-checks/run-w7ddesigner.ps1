<#
.SYNOPSIS
    Stage W7d's window check (App Designer window plan): callbacks over several components - the Callbacks tab over a selection, what they
    have in common and nothing in common, a name typed for them all as one step, the Callbacks submenu in the canvas's and the browser's menus with
    its Add, Go to and Select existing callback..., a group's submenu, the app's own row - in the JGraph application, each edit saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot> as W7dApp.m with its components replaced by two sliders Sa and Sb, a knob Kn, and a panel Pn holding
    a tree Tr and a lamp Lp, and starts JGraph.Application.exe with -r "appdesigner('<app>')". Rows and menu items are set through UI Automation patterns. Keys are window
    messages posted to the designer's own window handle, never input sent to whatever window is in front, and only once the designer's window is checked to be
    the foreground one; Ctrl+A is held down in the key state of that window's input thread alone.

    Help on Selection is not invoked, since it opens the guide in the machine's browser. A bar's menu is not reached in a window: the tests have those.

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
public static class W7dNative {
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

# W4's app (w4\W4App.m) with its components replaced.
$app = Join-Path $WorkRoot 'W7dApp.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$text = $text.Replace('W4App', 'W7dApp')
$declared = @('Sa matlab.ui.control.Slider', 'Sb matlab.ui.control.Slider', 'Kn matlab.ui.control.Knob', 'Pn matlab.ui.container.Panel', 'Tr matlab.ui.container.Tree', 'Lp matlab.ui.control.Lamp') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = [regex]::Replace($text, '(?s)(        UIFigure   matlab\.ui\.Figure\r?\n).*?(    end)', { param($m) $m.Groups[1].Value + ($declared -join '') + $m.Groups[2].Value })
$layout = @(
    "        <Children>",
    "            <Slider name='Sa'>",
    "                <Position>[40 400 150 3]</Position>",
    "            </Slider>",
    "            <Slider name='Sb'>",
    "                <Position>[40 340 150 3]</Position>",
    "            </Slider>",
    "            <Knob name='Kn'>",
    "                <Position>[260 340 60 60]</Position>",
    "            </Knob>",
    "            <Panel name='Pn'>",
    "                <Position>[40 40 300 220]</Position>",
    "                <Children>",
    "                    <Tree name='Tr'>",
    "                        <Position>[10 10 150 150]</Position>",
    "                    </Tree>",
    "                    <Lamp name='Lp'>",
    "                        <Position>[200 60 20 20]</Position>",
    "                    </Lamp>",
    "                </Children>",
    "            </Panel>",
    "        </Children>") -join $nl
$text = [regex]::Replace($text, '(?s)        <Children>.*</Children>(\r?\n    </UIFigure>)', { param($m) $layout + $m.Groups[1].Value })
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
        foreach ($entry in [W7dNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W7dNative+RECT
    [void] [W7dNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W7dNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W7dNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W7dNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W7dNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W7dNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W7dNative]::Windows([uint32] $p.Id)) {
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
    return ([W7dNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W7dNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W7dNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W7dNative]::GetCurrentThreadId()
    if (-not [W7dNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W7dNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W7dNative]::SetKeyboardState($keys)
        [void] [W7dNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W7dNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W7dNative]::SetKeyboardState($keys)
    }
    finally { [void] [W7dNative]::AttachThreadInput($me, $thread, $false) }
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
    foreach ($entry in [W7dNative]::Windows([uint32] $p.Id)) {
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
    if ([W7dNative]::GetForegroundWindow() -eq $script:hwnd) { [void] (Post-Key $VK_ESCAPE) }
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
    Check (Show-Callbacks) 'the Callbacks tab'

    # --- the figure and the app's own row ------------------------------------------------------------
    Check (Select-Component 'UIFigure') 'the figure selected'
    Check (Row-Shown 'UIFigure' 'CloseRequestFcn') 'its CloseRequestFcn'
    Check (Row-Shown 'UIFigure' 'KeyPressFcn') 'its KeyPressFcn'
    Check (-not (Row-Shown 'UIFigure' 'StartupFcn')) 'StartupFcn is the app''s, not the figure''s'
    Check (-not (Row-Box 'UIFigure' 'SizeChangedFcn').Current.IsEnabled) 'SizeChangedFcn is not edited here'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'figure.png')) 'a picture of the figure''s rows'
    Check (Run-Menu @('Callbacks', 'KeyboardCallbacks', 'AddKeyPressFcncallback')) 'the menu''s Callbacks, Keyboard Callbacks, Add KeyPressFcn callback'
    Check (In-Code) 'the Code View shows the new function'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Text).Contains('function UIFigureKeyPress(app, event)')) 'the function is named for the figure'
    Check ((Saved-Text).Contains('UIFigureKeyPress')) 'and is in the file'
    Check (To-Design) 'back to the Design View'
    Check (Select-Component 'UIFigure') 'the figure selected'
    Check ((Row-Value 'UIFigure' 'KeyPressFcn') -eq 'UIFigureKeyPress') 'the row says so'
    Check (Menu-Has @('Callbacks', 'HelponSelection', 'ContextMenu', 'ApplyGridLayout')) 'the canvas''s menu has Callbacks, the Context Menu and Help on Selection for the figure'

    Check (Select-Component 'W7dApp') 'the app''s own row selected'
    Check (Show-Callbacks) 'the Callbacks tab'
    Check (Row-Shown 'app' 'StartupFcn') 'its one row'
    Check (-not (Row-Shown 'UIFigure' 'CloseRequestFcn')) 'and not the figure''s'
    Check (Run-RowMenu 'W7dApp' @('Callbacks', 'AddStartupFcncallback')) 'the browser''s Callbacks, Add StartupFcn callback'
    Check (In-Code) 'the Code View shows it'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Text).Contains('function startupFcn(app)')) 'the startup function is in the file'
    Check (To-Design) 'back to the Design View'

    # --- one component ------------------------------------------------------------------------------------
    Check (Select-Component 'Sa') 'one slider selected'
    Check (Show-Callbacks) 'the Callbacks tab'
    Check ((Row-Value 'Sa' 'ValueChangedFcn') -eq '') 'its ValueChangedFcn is empty'
    Check (Row-Shown 'Sa' 'ValueChangingFcn') 'its ValueChangingFcn'
    Check (-not (Find-In $script:hwnd 'Callbacks.Sa.ValueChangedFcn.GoTo').Current.IsEnabled) 'nothing to go to yet'

    # --- several with nothing in common ----------------------------------------------------------------
    Check (Select-All) 'Ctrl+A selects them all'
    Check ((Tab-Says) -eq 'Multiple Components') 'the first tab says Multiple Components'
    $message = Find-In $script:hwnd 'Callbacks.Message' 3
    Check ($null -ne $message -and $message.Current.Name -eq 'The selected components have no callbacks in common.') 'the panel has nothing in common with a slider'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'nothing-in-common.png')) 'a picture of it'

    # --- several sharing ----------------------------------------------------------------------------------
    # (A row already among the selected ones takes no Select through UI Automation.)
    Check (Select-Component 'UIFigure') 'the figure selected'
    Check (Select-Component 'Pn') 'the panel selected'
    Check (Focus-Canvas) 'the canvas in front'
    Check (Post-Key 0x2E) 'Delete'
    Check (Select-All) 'Ctrl+A selects the slider, the slider and the knob'
    Check (Row-Shown 'Kn' 'ValueChangedFcn') 'a row for what they share, the last one''s'
    Check (Row-Shown 'Kn' 'ValueChangingFcn') 'and the other'
    Check ((Row-Value 'Kn' 'ValueChangedFcn') -eq '') 'it holds nothing'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'shared.png')) 'a picture of the shared rows'

    # A name typed goes to them all, as one step.
    Check (Edit-Callback 'Kn' 'ValueChangedFcn' 'shared') 'a name typed in the shared row'
    Check (In-Code) 'the Code View shows the function made'
    Check (Save-App $app) 'Save writes it'
    Check (Saved-Has 'Slider' 'Sa' 'shared') 'Sa holds it'
    Check (Saved-Has 'Slider' 'Sb' 'shared') 'Sb holds it'
    Check (Saved-Has 'Knob' 'Kn' 'shared') 'Kn holds it'
    Check (([regex]::Matches((Saved-Text), 'function shared\(app, event\)')).Count -eq 1) 'one function'
    Check ((Saved-Text).Contains('% Value changed function: Kn, Sa, Sb')) 'whose comment names them all'
    Check (To-Design) 'back to the Design View'
    Check ((Tab-Says) -eq 'Multiple Components') 'they stay selected'
    Check (Undo-Step) 'Undo'
    Check (Save-App $app) 'Save writes it'
    Check (-not (Saved-Has 'Slider' 'Sa' 'shared')) 'one Undo takes it off Sa'
    Check (-not (Saved-Has 'Knob' 'Kn' 'shared')) 'and off Kn'
    Check (-not (Saved-Text).Contains('function shared(')) 'and the function goes'

    # The menu's Add: named for the first.
    Check (Run-Menu @('Callbacks', 'AddValueChangedFcncallback')) 'the menu''s Add ValueChangedFcn callback over the three'
    Check (In-Code) 'the Code View shows it'
    Check (Save-App $app) 'Save writes it'
    Check (Saved-Has 'Slider' 'Sa' 'SaValueChanged') 'named for the first, Sa holds it'
    Check (Saved-Has 'Slider' 'Sb' 'SaValueChanged') 'Sb holds it'
    Check (Saved-Has 'Knob' 'Kn' 'SaValueChanged') 'Kn holds it'
    Check (To-Design) 'back to the Design View'

    # Go to.
    Check (Run-Menu @('Callbacks', 'GotoSaValueChangedcallback')) 'the menu''s Go to SaValueChanged callback'
    Check (In-Code) 'the Code View shows it'
    Check (To-Design) 'back to the Design View'

    # Select existing: the dialog.
    Check (Focus-Canvas) 'the canvas in front'
    Check (Post-Key $VK_APPS) 'the menu key'
    $callbacks = Find-Menu 'Menu.Callbacks'
    (Pattern $callbacks ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 500
    Check (Save-Popup 'Menu.Selectexistingcallback...' (Join-Path $WorkRoot 'submenu.png')) 'a picture of the submenu'
    Check (Invoke-Element (Find-Menu 'Menu.Selectexistingcallback...')) 'Select existing callback...'
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Select Callback Function' } 6
    Check ($null -ne $dialog) 'the dialog opens'
    if ($dialog) {
        $property = Find-In $dialog.Key 'SelectCallback.Callback' 3
        $function = Find-In $dialog.Key 'SelectCallback.Name' 3
        Check ($null -ne $property) 'its Callback'
        Check ($null -ne $function) 'its Name'
        $shown = Pattern $property ([System.Windows.Automation.SelectionPattern]::Pattern)
        Check ($shown.Current.GetSelection()[0].Current.Name -eq 'ValueChangingFcn') 'it offers what the first has free'
        Check (Save-Picture $dialog.Key (Join-Path $WorkRoot 'dialog.png')) 'a picture of the dialog'
        $ok = Find-By $dialog.Key $A::NameProperty 'OK' 3
        Check (Invoke-Element $ok) 'OK'
        Start-Sleep -Milliseconds 500
    }
    Check (-not (In-Code)) 'binding what the app has switches nothing'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Element $app 'Slider' 'Sb') -match 'ValueChangingFcn') 'Sb''s ValueChangingFcn is bound'
    Check ((Saved-Element $app 'Knob' 'Kn') -match 'ValueChangingFcn') 'Kn''s is bound'
    Check (([regex]::Matches((Saved-Text), 'function SaValueChanged\(app, event\)')).Count -eq 1) 'to the one function'
    Check (Show-Callbacks) 'the Callbacks tab'
    Check ((Row-Value 'Kn' 'ValueChangingFcn') -eq 'SaValueChanged') 'the row says so'
    Check ((Find-In $script:hwnd 'Callbacks.Kn.ValueChangedFcn.GoTo').Current.IsEnabled) 'there is something to go to'

    # A name refused.
    Check (Edit-Callback 'Kn' 'ValueChangingFcn' 'for') 'a keyword typed'
    Start-Sleep -Milliseconds 700
    $alert = Find-Window $p.Id { param($t) $t -eq 'Invalid Name' } 6
    Check ($null -ne $alert) 'App Designer''s alert says Invalid Name'
    if ($alert) {
        Check (Save-Picture $alert.Key (Join-Path $WorkRoot 'alert.png')) 'a picture of the alert'
        [void] [W7dNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 900
    }
    Check ((Row-Value 'Kn' 'ValueChangingFcn') -eq 'SaValueChanged') 'the row goes back'

    # None: all unbound, the function kept.
    Check (Edit-Callback 'Kn' 'ValueChangingFcn' '') 'the shared row emptied'
    Check (Save-App $app) 'Save writes it'
    Check (-not ((Saved-Element $app 'Slider' 'Sa') -match 'ValueChangingFcn')) 'Sa''s ValueChangingFcn is unbound'
    Check (-not ((Saved-Element $app 'Knob' 'Kn') -match 'ValueChangingFcn')) 'Kn''s is unbound'
    Check ((Saved-Text).Contains('function SaValueChanged(app, event)')) 'the function stays'

    # The go-to button.
    Check (Invoke-Element (Find-In $script:hwnd 'Callbacks.Kn.ValueChangedFcn.GoTo')) 'the go-to button'
    Check (In-Code) 'the Code View shows it'
    Check (To-Design) 'back to the Design View'

    # --- the browser's menu, a group's submenu -------------------------------------------------------------
    Check (Undo-Step) 'Undo, the row emptied'
    Check (Undo-Step) 'Undo, the dialog'
    Check (Undo-Step) 'Undo, the menu''s Add'
    Check (Undo-Step) 'Undo, the delete: the panel is back'
    Check (Row-Shown-In-Browser 'Tr') 'the tree is back'
    Check (Run-RowMenu 'Tr' @('Callbacks', 'MouseCallbacks', 'AddClickedFcncallback')) 'the browser''s Callbacks, Mouse Callbacks, Add ClickedFcn callback'
    Check (In-Code) 'the Code View shows it'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Text).Contains('function TrClicked(app, event)')) 'the tree''s function'
    Check (Saved-Has 'Tree' 'Tr' 'TrClicked') 'bound to it'
    Check (To-Design) 'back to the Design View'

    # A lamp has none: Help on Selection all the same.
    Check (Select-Component 'Lp') 'the lamp selected'
    Check (Show-Callbacks) 'the Callbacks tab'
    $message = Find-In $script:hwnd 'Callbacks.Message' 3
    Check ($null -ne $message -and $message.Current.Name -eq 'Lamp objects do not have callback properties.') 'a lamp has no callback properties'
    Check (Focus-Canvas) 'the canvas in front'
    Check (Post-Key $VK_APPS) 'the menu key'
    Check ($null -ne (Find-Menu 'Menu.HelponSelection' 3)) 'Help on Selection'
    Check ($null -eq (Find-Menu 'Menu.Callbacks' 1)) 'and no Callbacks'
    Close-Menu
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
