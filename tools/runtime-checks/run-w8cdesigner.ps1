<#
.SYNOPSIS
    Stage W8c's window check (App Designer window plan): Convert to two panels and back, each a new app with the old one left, the result saved as an .mlapp for R2025b's loader,
    Save Copy As with its refusal, and Export to .m File, in the JGraph application, each read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot> as W8cApp.m with two components in it and starts JGraph.Application.exe with -r "appdesigner('<app>')". Everything is set through
    UI Automation patterns; the common dialogs' name boxes and Open or Save take messages to their own handles. Nothing is typed into or clicked at whatever window is in front.

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
public static class W8cNative {
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
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessage")] public static extern IntPtr SendRead(IntPtr hWnd, uint msg, IntPtr wParam, System.Text.StringBuilder lParam);
    public static IntPtr SendText(IntPtr edit, string text) { return SendMessage(edit, 0x000C, IntPtr.Zero, text); }
    public static string ReadText(IntPtr edit) { var sb = new System.Text.StringBuilder(1024); SendRead(edit, 0x000D, (IntPtr) sb.Capacity, sb); return sb.ToString(); }
    // The Edit of a common dialog's name box: the Open dialog's control id is 1148, the Save dialog's 1001.
    public static IntPtr FindEdit(IntPtr dialog) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dialog, (h, l) => {
            var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, sb.Capacity);
            int id = GetDlgCtrlID(h);
            if (sb.ToString() == "Edit" && (id == 1148 || id == 1001)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
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

$app = Join-Path $WorkRoot 'W8cApp.m'
$bin = Join-Path $WorkRoot 'W8cBin.mlapp'
foreach ($stale in @($bin, (Join-Path $WorkRoot 'W8cCopy.m'), (Join-Path $WorkRoot 'W8cExported.m'))) { if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force } }
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$text = $text.Replace('W4App', 'W8cApp')
$declared = @('Lf matlab.ui.control.Slider', 'Rg matlab.ui.control.Knob') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = [regex]::Replace($text, '(?s)(        UIFigure   matlab\.ui\.Figure\r?\n).*?(    end)', { param($m) $m.Groups[1].Value + ($declared -join '') + $m.Groups[2].Value })
$layout = @(
    "        <Children>",
    "            <Slider name='Lf'>",
    "                <Position>[40 400 150 3]</Position>",
    "            </Slider>",
    "            <Knob name='Rg'>",
    "                <Position>[460 340 60 60]</Position>",
    "            </Knob>",
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
        foreach ($entry in [W8cNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W8cNative+RECT
    [void] [W8cNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W8cNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W8cNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W8cNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W8cNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W8cNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W8cNative]::Windows([uint32] $p.Id)) {
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
    return ([W8cNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W8cNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W8cNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W8cNative]::GetCurrentThreadId()
    if (-not [W8cNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W8cNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W8cNative]::SetKeyboardState($keys)
        [void] [W8cNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W8cNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W8cNative]::SetKeyboardState($keys)
    }
    finally { [void] [W8cNative]::AttachThreadInput($me, $thread, $false) }
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
    foreach ($entry in [W8cNative]::Windows([uint32] $p.Id)) {
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
    if ([W8cNative]::GetForegroundWindow() -eq $script:hwnd) { [void] (Post-Key $VK_ESCAPE) }
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

function Find-AppTab([string] $name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
    return $A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)
}

function Show-App([string] $name) {
    $tab = Pattern (Find-AppTab $name) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 900
    return $true
}

function Open-Drop([string] $id) {
    $expand = Pattern (Find-In $script:hwnd $id 5) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Close-Drop([string] $id) {
    $expand = Pattern (Find-In $script:hwnd $id 2) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($expand) { try { $expand.Collapse() } catch { } }
    Start-Sleep -Milliseconds 400
}

$ConvertDrop = 'appDesignerToolstrip.layoutTab.file.responsifyPushButton'
$Two = 'appDesignerToolstrip.layoutTab.file.responsify2ColumnMenuItem'
$Three = 'appDesignerToolstrip.layoutTab.file.responsify3ColumnMenuItem'
$Standard = 'AD_UNRESPONSIFY_TO_STANDARD'
$SaveDrop = 'appDesignerToolstrip.layoutTab.file.saveSplitButton.arrow'

# A file named in the common dialog with this title, by messages to the dialog's own handles.
function Name-File([string] $title, [string] $file) {
    $dialog = Find-Window $p.Id { param($t) $t -eq $title } 6
    if (-not $dialog) { return $false }
    $name = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $name = [W8cNative]::FindEdit($dialog.Key)
        if ($name -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
    } while ($name -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    if ($name -eq [IntPtr]::Zero) { return $false }
    [void] [W8cNative]::SendText($name, $file)
    Start-Sleep -Milliseconds 300
    [void] [W8cNative]::PostMessage($dialog.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
    return $true
}

function Default-Name([string] $title) {
    $dialog = Find-Window $p.Id { param($t) $t -eq $title } 6
    if (-not $dialog) { return $null }
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $name = [W8cNative]::FindEdit($dialog.Key)
        if ($name -ne [IntPtr]::Zero) { return [W8cNative]::ReadText($name) }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Dismiss-Alert([string] $title, [int] $code = 1) {
    $alert = Find-Window $p.Id { param($t) $t -eq $title } 6
    if (-not $alert) { return $false }
    [void] [W8cNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $code, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 600
    return $true
}

function Tabs-Named([string] $starts) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    return @($A::FromHandle($script:hwnd).FindAll($Tree::Subtree, $condition) | ForEach-Object { $_.Current.Name } | Where-Object { $_.StartsWith($starts) })
}

$forwardPlain = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forwardPlain')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- Convert's drop-down -------------------------------------------------------------------------
    Check (Show-Tab 'appDesignerToolstrip.layoutTab') 'the CANVAS tab'
    $drop = Find-In $script:hwnd $ConvertDrop 5
    Check ($null -ne $drop -and $drop.Current.IsEnabled) 'Convert is live'
    Check (Open-Drop $ConvertDrop) 'its drop-down opens'
    Check ((Find-Menu $Two).Current.IsEnabled) 'to 2 panels is enabled'
    Check ((Find-Menu $Three).Current.IsEnabled) 'to 3 panels too'
    Check (-not (Find-Menu $Standard).Current.IsEnabled) 'back to none is not, with no regions'
    Check (Save-Popup $Two (Join-Path $WorkRoot 'convert-menu.png')) 'a picture of it'

    # To two panels: a new app, the old one left.
    Check (Invoke-Element (Find-Menu $Two)) '2-Panel App with Auto-Reflow'
    Start-Sleep -Milliseconds 1500
    Check ((Tabs-Named 'W8cApp_converted').Count -eq 1) "a new tab, $((Tabs-Named 'W8cApp').Count) in all"
    Check ((Tabs-Named 'W8cApp.m').Count -eq 1) 'the old one still there'
    Check (Row-Shown-In-Browser 'app.LeftPanel') 'its left panel in the browser'
    Check (Row-Shown-In-Browser 'app.RightPanel') 'and its right'
    Check (Row-Shown-In-Browser 'app.Lf') 'a component came over'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'converted-2.png')) 'a picture of the new app'
    Check (Open-Drop $ConvertDrop) 'its Convert'
    Check (-not (Find-Menu $Two).Current.IsEnabled) 'to 2 panels no longer'
    Check ((Find-Menu $Standard).Current.IsEnabled) 'back to none now'
    Close-Drop $ConvertDrop

    # Saved as an .mlapp, for R2025b's loader.
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    Check (Name-File 'Save app as' $bin) 'Save asks for a file: a .mlapp named'
    Start-Sleep -Milliseconds 1500
    Check (Test-Path -LiteralPath $bin) 'the converted app in its file'

    # And back: another new app, named for the first.
    Check (Open-Drop $ConvertDrop) 'Convert again'
    Check (Invoke-Element (Find-Menu $Standard)) 'App without Auto-Reflow'
    Start-Sleep -Milliseconds 1500
    Check ((Tabs-Named 'W8cBin_converted').Count -eq 1) "a new tab named for it ($((Tabs-Named 'W8c') -join ', '))"
    Check (Row-Shown-In-Browser 'app.Lf') 'the component still'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'converted-back.png')) 'a picture of it'

    # --- Save Copy As ----------------------------------------------------------------------------------
    Check (Show-App 'W8cApp.m') 'the first app'
    Check (Show-Tab 'appDesignerToolstrip.layoutTab') 'the CANVAS tab'
    Check (Open-Drop $SaveDrop) 'Save''s drop-down'
    Check ($null -ne (Find-Menu 'AD_SAVECOPY_AS')) 'Save Copy As...'
    Check ((Find-Menu 'AD_SAVECOPY_AS').Current.IsEnabled) 'is live'
    Check (Invoke-Element (Find-Menu 'AD_SAVECOPY_AS')) 'chosen'
    Check (Name-File 'Save Copy As' $app) 'its own file named'

    # The common dialog asks first whether to replace it, in a box of the same title on top of it.
    $boxes = @([W8cNative]::Windows([uint32] $p.Id) | Where-Object { $_.Value -eq 'Save Copy As' })
    Check ($boxes.Count -eq 2) 'the dialog asks to replace it'
    if ($boxes.Count -eq 2) {
        [void] [W8cNative]::PostMessage($boxes[0].Key, $WM_COMMAND, [IntPtr] 6, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 900
    }
    Check ($null -ne (Find-Window $p.Id { param($t) $t -eq 'App Designer' } 5)) 'is refused in an alert'
    Check (Dismiss-Alert 'App Designer' 2) 'Cancel'
    Check (Open-Drop $SaveDrop) 'the drop-down again'
    Check (Invoke-Element (Find-Menu 'AD_SAVECOPY_AS')) 'chosen again'
    $copy = Join-Path $WorkRoot 'W8cCopy.m'
    Check (Name-File 'Save Copy As' $copy) 'another file named'
    Start-Sleep -Milliseconds 800
    Check (Test-Path -LiteralPath $copy) 'the copy in its file'
    Check ([System.IO.File]::ReadAllText($copy).Contains('classdef W8cCopy <')) 'its class named for it'
    Check ((Tabs-Named 'W8cApp.m').Count -eq 1) 'the app''s tab stays'
    Check ((Tabs-Named 'W8cCopy').Count -eq 0) 'no tab for the copy'

    # --- Export to .m File ------------------------------------------------------------------------------
    Check (Open-Drop $SaveDrop) 'the drop-down'
    Check (Invoke-Element (Find-Menu 'AD_EXPORT')) 'Export to .m File...'
    Check ((Default-Name 'Save As') -like 'W8cApp_exported*') "it starts at the app's name (got $(Default-Name 'Save As'))"
    $exported = Join-Path $WorkRoot 'W8cExported.m'
    Check (Name-File 'Save As' $exported) 'a file named'
    Start-Sleep -Milliseconds 800
    Check (Test-Path -LiteralPath $exported) 'the class in its file'
    $text = [System.IO.File]::ReadAllText($exported)
    Check ($text.Contains('classdef W8cExported < matlab.apps.AppBase')) 'a class deriving from AppBase'
    Check (-not $text.Contains('%[app:')) 'with no appendix'
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
