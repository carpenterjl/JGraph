<#
.SYNOPSIS
    Stage W8a's window check (App Designer window plan): App Details in an .mlapp and in a plain-text app, a custom thumbnail and its refusals, Code Options, the Help Text
    dialog and the app's own rows in the inspector, in the JGraph application, each edit saved and read back from the file.

.DESCRIPTION
    Copies the .mlapp w8\W8aBin.mlapp and the plain-text app w4\W4App.m (as W8aPlain.m) into <WorkRoot>, and starts JGraph.Application.exe with
    -r "appdesigner('<bin>'); appdesigner('<plain>')". Fields are set through UI Automation patterns, and the image is named in the common dialog's own name box and Open button.
    Keys are window messages posted to the designer's own window handle, never input sent to whatever window is in front, and only once the designer's window is checked to be the
    foreground one.

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
public static class W8aNative {
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
    public static IntPtr SendText(IntPtr edit, string text) { return SendMessage(edit, 0x000C, IntPtr.Zero, text); }
    // The Edit of a common dialog's name box: the Edit under the combo box whose control id is 1148.
    public static IntPtr FindEdit(IntPtr dialog) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dialog, (h, l) => {
            var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() == "Edit" && GetDlgCtrlID(h) == 1148) { found = h; return false; }
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

$bin = Join-Path $WorkRoot 'W8aBin.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\W8aBin.mlapp') -Destination $bin -Force
Set-ItemProperty -LiteralPath $bin -Name IsReadOnly -Value $false
$app = Join-Path $WorkRoot 'W8aPlain.m'
[System.IO.File]::WriteAllText($app, [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m')).Replace('W4App', 'W8aPlain'))

# The pictures the image button is given.
$picture = Join-Path $WorkRoot 'picture.png'
$bitmap = Join-Path $WorkRoot 'picture.bmp'
$image = New-Object System.Drawing.Bitmap(300, 100)
$painter = [System.Drawing.Graphics]::FromImage($image)
$painter.Clear([System.Drawing.Color]::SteelBlue)
$painter.Dispose()
$image.Save($picture, [System.Drawing.Imaging.ImageFormat]::Png)
$image.Save($bitmap, [System.Drawing.Imaging.ImageFormat]::Bmp)
$image.Dispose()

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
        foreach ($entry in [W8aNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W8aNative+RECT
    [void] [W8aNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W8aNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W8aNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W8aNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W8aNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W8aNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W8aNative]::Windows([uint32] $p.Id)) {
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
    return ([W8aNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W8aNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W8aNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W8aNative]::GetCurrentThreadId()
    if (-not [W8aNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W8aNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W8aNative]::SetKeyboardState($keys)
        [void] [W8aNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W8aNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W8aNative]::SetKeyboardState($keys)
    }
    finally { [void] [W8aNative]::AttachThreadInput($me, $thread, $false) }
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
    foreach ($entry in [W8aNative]::Windows([uint32] $p.Id)) {
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
    if ([W8aNative]::GetForegroundWindow() -eq $script:hwnd) { [void] (Post-Key $VK_ESCAPE) }
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

function Open-Dialog($button, [string] $title) {
    if (-not (Invoke-Element $button)) { return $null }
    return (Find-Window $p.Id { param($t) $t -eq $title } 6)
}

function Field([string] $id) { return (Find-In $script:dialog.Key $id 3) }

function Field-Value([string] $id) {
    $value = Pattern (Field $id) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Put([string] $id, [string] $text) { return (Set-Text (Field $id) $text) }

function Ok-Enabled([string] $id = 'AppDetails.OK') {
    $ok = Field $id
    return ($null -ne $ok -and $ok.Current.IsEnabled)
}

function Press-Ok([string] $id = 'AppDetails.OK') {
    [void] (Invoke-Element (Field $id))
    Start-Sleep -Milliseconds 700
    return (-not (Find-Window $p.Id { param($t) $t -eq 'App Details' -or $t -eq 'App Help Text' } 1))
}

function Cancel-Dialog {
    $cancel = $A::FromHandle($script:dialog.Key).FindFirst($Tree::Subtree, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Cancel')))
    [void] (Invoke-Element $cancel)
    Start-Sleep -Milliseconds 500
}

# A file named in the common dialog the image button opens, through its own name box and Open button.
# A file named in the common dialog the image button opens: the text set in its own name box and its Open pressed, by messages to the dialog's own handles.
function Pick-File([string] $file) {
    $open = Find-Window $p.Id { param($t) $t -eq 'Open Image File' } 6
    if (-not $open) { return $false }
    $name = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $name = [W8aNative]::FindEdit($open.Key)
        if ($name -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
    } while ($name -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    if ($name -eq [IntPtr]::Zero) { return $false }
    [void] [W8aNative]::SendText($name, $file)
    Start-Sleep -Milliseconds 300
    [void] [W8aNative]::PostMessage($open.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
    return $true
}

function Dismiss-Alert([string] $title) {
    $alert = Find-Window $p.Id { param($t) $t -eq $title } 6
    if (-not $alert) { return $false }
    [void] [W8aNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    return $true
}

function Part-Of([string] $file, [string] $name) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try {
        $entry = $zip.GetEntry($name)
        if (-not $entry) { return $null }
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

function Parts-Of([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try { return @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
}

function Code-Of([string] $file) {
    $document = Part-Of $file 'matlab/document.xml'
    return $document
}

$forwardBin = $bin.Replace('\', '/')
$forwardPlain = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forwardBin'); appdesigner('$forwardPlain')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the apps ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- an .mlapp's App Details -------------------------------------------------------------------
    Check (Show-App 'W8aBin.mlapp') 'the .mlapp''s tab'
    Check (Show-Tab 'appDesignerToolstrip.designerTab') 'the DESIGNER tab'
    $details = Find-In $script:hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton'
    Check ($null -ne $details -and $details.Current.IsEnabled) 'App Details is live'
    $script:dialog = Open-Dialog $details 'App Details'
    Check ($null -ne $script:dialog) 'App Details opens'
    if (-not $script:dialog) { throw 'no dialog' }
    $sharing = Pattern (Field 'AppDetails.SharingDetails') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $coding = Pattern (Field 'AppDetails.CodeOptions') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Check ($sharing.Current.ExpandCollapseState -eq 'Expanded') 'Sharing Details is open'
    Check ($coding.Current.ExpandCollapseState -eq 'Collapsed') 'and Code Options closed'
    Check ((Field-Value 'AppDetails.Version') -eq '1.0') 'the version shows 1.0'
    Check ($null -eq (Field 'AppDetails.Source.Auto')) 'an .mlapp has no thumbnail source'
    Check ((Find-In $script:dialog.Key 'AppDetails.ThumbnailWords' 2).Current.Name -eq 'App thumbnail is captured whenever the app runs unless you select a custom image') 'the thumbnail''s words'
    Check (Save-Picture $script:dialog.Key (Join-Path $WorkRoot 'details-mlapp.png')) 'a picture of the dialog'

    Check (Put 'AppDetails.Name' 'A <name> & more') 'a name'
    Check (Put 'AppDetails.Author' 'An author') 'an author'
    Check (Put 'AppDetails.Summary' 'A summary') 'a summary'
    Check (Put 'AppDetails.Description' "Line one`r`nLine two") 'a description'
    Check (Put 'AppDetails.Version' 'a') 'a version that cannot become one'
    Check (-not (Ok-Enabled)) 'holds OK back'
    Check (Put 'AppDetails.Version' '3') 'part of a version'
    Check (Ok-Enabled) 'lets it go while typed'
    Check (Put 'AppDetails.Version' '2.5') 'a whole version'
    Check (Ok-Enabled) 'and OK'

    # A picture refused by its extension, then one taken.
    Check (Invoke-Element (Field 'AppDetails.SelectImage')) 'Select a custom image'
    Check (Pick-File $bitmap) 'a .bmp named'
    Check (Dismiss-Alert 'App Designer') 'is refused in an alert'
    Check (Invoke-Element (Field 'AppDetails.SelectImage')) 'Select a custom image again'
    Check (Pick-File $picture) 'a .png named'
    Start-Sleep -Milliseconds 500
    Check ($null -eq (Find-In $script:dialog.Key 'AppDetails.ThumbnailWords' 1)) 'the picture in place of the words'
    Check (Save-Picture $script:dialog.Key (Join-Path $WorkRoot 'details-picture.png')) 'a picture of it'
    Check (Press-Ok) 'OK closes it'
    Check (Save-App $bin) 'Save writes the file'
    $core = Part-Of $bin 'metadata/coreProperties.xml'
    Check ($core.Contains('<dc:title>A &lt;name&gt; &amp; more</dc:title>')) 'the name in the package, escaped'
    Check ($core.Contains('<cp:version>2.5</cp:version>')) 'the version'
    Check ($core.Contains('<dc:creator>An author</dc:creator>')) 'the author'
    Check ($core.Contains('<dc:description>A summary</dc:description>')) 'the summary'
    $meta = Part-Of $bin 'metadata/appMetadata.xml'
    Check ($meta.Contains('Line two</description>')) 'the description'
    Check ($meta.Contains('<screenshotMode>manual</screenshotMode>')) 'the mode manual'
    Check ((Parts-Of $bin) -contains 'metadata/appScreenshot.png') 'the picture a part of it'
    Check ((Part-Of $bin '_rels/.rels').Contains('metadata/appScreenshot.png')) 'under its relationships'

    # One Undo takes them all back; they stay in the dialog's next opening.
    Check (Undo-Step) 'Undo'
    $script:dialog = Open-Dialog (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details'
    Check ((Field-Value 'AppDetails.Version') -eq '1.0') 'one Undo takes the version back'
    Check ((Field-Value 'AppDetails.Author') -eq '') 'and the author'
    Cancel-Dialog

    # --- Code Options --------------------------------------------------------------------------------
    if (-not (In-Code)) { (Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle(); Start-Sleep -Milliseconds 800 }
    Check (In-Code) 'the Code View shows'
    Check (Show-Tab 'appDesignerToolstrip.editorTab') 'the EDITOR tab'
    $arguments = Find-In $script:hwnd 'appDesignerToolstrip.editorTab.edit.addInputArgumentsButton'
    $script:dialog = Open-Dialog $arguments 'App Details'
    Check ($null -ne $script:dialog) 'App Input Arguments opens App Details'
    $coding = Pattern (Field 'AppDetails.CodeOptions') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Check ($coding.Current.ExpandCollapseState -eq 'Expanded') 'at Code Options'
    Check (Put 'CodeOptions.InputArguments' '1a') 'a name that will not do'
    Check (-not (Ok-Enabled)) 'holds OK back'
    Check ((Field 'AppDetails.ArgumentsError').Current.Name.Length -gt 0) 'and says why'
    Check (Put 'CodeOptions.InputArguments' 'a, b') 'two names'
    Check (Ok-Enabled) 'let it go'
    (Pattern (Field 'CodeOptions.SingleInstance') ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Check (Press-Ok) 'OK'
    Check (Save-App $bin) 'Save writes the file'
    $code = Code-Of $bin
    Check ($code.Contains('function startupFcn(app, a, b)')) 'a startup function takes them'
    Check ($code.Contains('(varargin)')) 'the constructor takes varargin'
    Check ($code.Contains('isequal(runningApp')  -or $code.Contains('runningApp = getRunningApp(app)')) 'a single running instance'

    # --- the Help Text -------------------------------------------------------------------------------
    $help = Find-In $script:hwnd 'appDesignerToolstrip.editorTab.edit.addHelpCommentsButton'
    Check ($null -ne $help -and $help.Current.IsEnabled) 'App Help Text is live'
    Check ($help.Current.Name -match 'App') 'says App'
    $script:dialog = Open-Dialog $help 'App Help Text'
    Check ($null -ne $script:dialog) 'its dialog opens'
    Check (Save-Picture $script:dialog.Key (Join-Path $WorkRoot 'helptext.png')) 'a picture of it'
    Check (Put 'HelpText.Summary' 'Sums two numbers') 'a summary'
    Check (Put 'HelpText.Description' "It sums them.`r`nThen it shows the sum.") 'a description'
    Check (Press-Ok 'HelpText.OK') 'OK'
    Check (Save-App $bin) 'Save writes the file'
    $code = Code-Of $bin
    Check ($code.Contains('%W8ABIN Sums two numbers')) 'the summary under the class line'
    Check ($code.Contains('%   It sums them.')) 'the description'
    Check ($code.Contains('%   Then it shows the sum.')) 'on its own line'
    $script:dialog = Open-Dialog (Find-In $script:hwnd 'appDesignerToolstrip.editorTab.edit.addHelpCommentsButton') 'App Help Text'
    Check ((Field-Value 'HelpText.Summary') -eq 'Sums two numbers') 'the dialog reads it back'
    Cancel-Dialog

    # --- the app's own rows --------------------------------------------------------------------------
    Check (To-Design) 'back to the Design View'
    Check (Select-Component 'W8aBin') 'the app''s row'
    Check ((Tab-Says) -eq 'App') 'the tab says App'
    Start-Sleep -Milliseconds 400
    $row = Find-In $script:hwnd 'Inspector.App.Author' 3
    Check ($null -ne $row) 'an Author row'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'approw.png')) 'a picture of the rows'
    Check (Set-Text $row 'Row author') 'typed in it'
    try { (Find-In $script:hwnd 'Inspector.App.Name' 3).SetFocus() } catch { }
    Start-Sleep -Milliseconds 500
    Check (Save-App $bin) 'Save writes it'
    Check ((Part-Of $bin 'metadata/coreProperties.xml').Contains('<dc:creator>Row author</dc:creator>')) 'the row''s author in the package'
    $version = Find-In $script:hwnd 'Inspector.App.Version' 3
    Check (Set-Text $version '7') 'a version that will not do in its row'
    try { (Find-In $script:hwnd 'Inspector.App.Name' 3).SetFocus() } catch { }
    Check (Dismiss-Alert 'App Designer') 'is refused in an alert'
    Start-Sleep -Milliseconds 400
    $kebab = Find-In $script:hwnd 'Inspector.App.Kebab.CodeOptions' 3
    $script:dialog = Open-Dialog $kebab 'App Details'
    Check ($null -ne $script:dialog) 'a group''s kebab opens App Details'
    $coding = Pattern (Field 'AppDetails.CodeOptions') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Check ($coding.Current.ExpandCollapseState -eq 'Expanded') 'at its section'
    Check ((Field-Value 'CodeOptions.InputArguments') -eq 'a, b') 'with the arguments'
    Cancel-Dialog

    # What R2025b's readers are then shown: the details put back, and the picture.
    Check (Show-Tab 'appDesignerToolstrip.designerTab') 'the DESIGNER tab again'
    $script:dialog = Open-Dialog (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details'
    Check (Put 'AppDetails.Name' 'A <name> & more') 'the name again'
    Check (Put 'AppDetails.Version' '2.5') 'the version again'
    Check (Put 'AppDetails.Summary' 'A summary') 'the summary again'
    Check (Put 'AppDetails.Description' "Line one`r`nLine two") 'the description again'
    Check (Invoke-Element (Field 'AppDetails.SelectImage')) 'the image button again'
    Check (Pick-File $picture) 'the picture again'
    Check (Press-Ok) 'OK'
    Check (Save-App $bin) 'Save writes the file'

    # --- a plain-text app's App Details ---------------------------------------------------------------
    Check (Show-App 'W8aPlain.m') 'the plain-text app''s tab'
    Check (Show-Tab 'appDesignerToolstrip.designerTab') 'the DESIGNER tab'
    $script:dialog = Open-Dialog (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details'
    Check ($null -ne $script:dialog) 'App Details opens'
    $auto = Pattern (Field 'AppDetails.Source.Auto') ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $auto -and $auto.Current.IsSelected) 'its source Auto'
    Check (Save-Picture $script:dialog.Key (Join-Path $WorkRoot 'details-plain.png')) 'a picture of the dialog'
    (Pattern (Field 'AppDetails.Source.None') ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 300
    Check (Put 'AppDetails.Author' 'Plain author') 'an author'
    Check (Press-Ok) 'OK'
    Check (Save-App $app) 'Save writes the file'
    Check ((Saved-Text).Contains("<Thumbnail autoCapture='false'></Thumbnail>")) 'None holds no thumbnail'
    Check ((Saved-Text).Contains('Plain author')) 'the author in the appendix'
    $script:dialog = Open-Dialog (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details'
    Check ((Pattern (Field 'AppDetails.Source.None') ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected) 'and says None when opened again'
    Check (Invoke-Element (Field 'AppDetails.Source.Browse')) 'Custom''s button'
    Check (Pick-File $picture) 'a picture named'
    Start-Sleep -Milliseconds 500
    Check ((Pattern (Field 'AppDetails.Source.Custom') ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected) 'the source follows'
    Check (Press-Ok) 'OK'
    Check (Save-App $app) 'Save writes the file'
    Check ((Saved-Text) -match "<Thumbnail autoCapture='false'>data:image/jpg;base64,[A-Za-z0-9+/=]+</Thumbnail>") 'the picture a JPEG in the appendix'
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
