<#
.SYNOPSIS
    Stage W7b's window check (App Designer window plan): the inspector - the tab saying what is selected, several
    components' shared rows edited as one step that undoes, the search and its two kinds of match, the alphabetical
    view kept for a class, a sub-group behind its bar, a refused value's alert - in the JGraph application, each edit
    saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a spinner and a lamp added, and starts
    JGraph.Application.exe with -r "appdesigner('<app>')". Rows are selected and text set through UI Automation
    patterns. Keys are window messages posted to the designer's own window handle, never input sent to whatever window
    is in front, and only once the designer's window is checked to be the foreground one. Several components are
    selected by Ctrl+A: while the A is posted, Ctrl is marked as down in the key state of the designer's own input
    thread (AttachThreadInput, SetKeyboardState), and put back straight after, so no other window sees it.

    Mouse messages posted to the window do not reach the canvas, so a rubber band and a Ctrl or Shift click are left to
    the tests.

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
public static class W7bNative {
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
$declared = @('Sp matlab.ui.control.Spinner', 'Lp matlab.ui.control.Lamp') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = $text.Replace("        D          matlab.ui.control.Button$nl", "        D          matlab.ui.control.Button$nl" + ($declared -join ''))
$layout = @(
    "            <Spinner name='Sp'>",
    "                <Position>[300 200 100 22]</Position>",
    "            </Spinner>",
    "            <Lamp name='Lp'>",
    "                <Position>[420 200 20 20]</Position>",
    "            </Lamp>") -join $nl
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
        foreach ($entry in [W7bNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W7bNative+RECT
    [void] [W7bNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W7bNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W7bNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W7bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W7bNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W7bNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W7bNative]::Windows([uint32] $p.Id)) {
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
    return ([W7bNative]::GetForegroundWindow() -eq $script:hwnd)
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
    if ([W7bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    $owner = [uint32] 0
    $thread = [W7bNative]::GetWindowThreadProcessId($script:hwnd, [ref] $owner)
    $me = [W7bNative]::GetCurrentThreadId()
    if (-not [W7bNative]::AttachThreadInput($me, $thread, $true)) { return $false }
    try {
        $keys = New-Object byte[] 256
        [void] [W7bNative]::GetKeyboardState($keys)
        $before = $keys[$VK_CONTROL]; $beforeLeft = $keys[$VK_LCONTROL]

        # WPF asks after the left and right keys, not VK_CONTROL.
        $keys[$VK_CONTROL] = [byte] ($before -bor 0x80)
        $keys[$VK_LCONTROL] = [byte] ($beforeLeft -bor 0x80)
        [void] [W7bNative]::SetKeyboardState($keys)
        [void] [W7bNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
        [void] [W7bNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
        Start-Sleep -Milliseconds 700
        $keys[$VK_CONTROL] = $before; $keys[$VK_LCONTROL] = $beforeLeft
        [void] [W7bNative]::SetKeyboardState($keys)
    }
    finally { [void] [W7bNative]::AttachThreadInput($me, $thread, $false) }
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

    # --- one component: the tab, its rows, its help ---------------------------------------------------
    Check (Select-Component 'D') 'D selected'
    Check ((Tab-Says) -eq 'Button') "the tab says Button ($(Tab-Says))"
    Check ((Inspector-Value 'CodeName') -eq 'D') 'the code name row says D'
    Check ((Inspector-Value 'Text') -eq 'D') 'its Text'
    Check (Shown-In-Inspector 'Search') 'the search'
    Check (Toggled 'Inspector.GroupView') 'the group view is on'
    Check (-not (Toggled 'Inspector.AlphabeticalView')) 'the alphabetical one is off'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'one.png')) 'a picture of one component'

    # --- the search ------------------------------------------------------------------------------------
    $search = Find-In $script:hwnd 'Inspector.Search'
    Check (Set-Text $search 'font') 'font typed in the search'
    Check (Shown-In-Inspector 'FontSize') 'FontSize is found'
    Check (-not (Shown-In-Inspector 'Text')) 'Text is not'
    $groupButton = Find-In $script:hwnd 'Inspector.GroupView'
    Check (-not $groupButton.Current.IsEnabled) 'the group view''s button is off during a search'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'search.png')) 'a picture of the search'

    # A word only in a row's help.
    Check (Set-Text $search 'operational') 'a word from the help'
    Check (Shown-In-Inspector 'Enable') 'Enable is found by its help'
    $words = Find-In $script:hwnd 'Inspector.SearchWords' 2
    Check ($null -ne $words -and $words.Current.Name -eq 'Help documentation results') 'under the help''s heading'
    Check (Set-Text $search 'zzqzz') 'a word nowhere'
    $words = Find-In $script:hwnd 'Inspector.SearchWords' 2
    Check ($null -ne $words -and $words.Current.Name -eq 'No matches found') 'No matches found'
    Check (Set-Text $search '(') 'a bracket, which R2025b''s search trips on'
    Check ((Get-Process -Id $p.Id -ErrorAction SilentlyContinue) -ne $null) 'and nothing breaks'
    Check (Invoke-Element (Find-In $script:hwnd 'Inspector.ClearSearch')) 'the search''s clear button'
    Check (Shown-In-Inspector 'Text') 'the rows are back'
    Check ((Find-In $script:hwnd 'Inspector.GroupView').Current.IsEnabled) 'and the button'

    # A selection clears the search.
    Check (Set-Text $search 'font') 'font typed again'
    Check (Select-Component 'A') 'A selected'
    Check ((Pattern (Find-In $script:hwnd 'Inspector.Search') ([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value -eq '') 'the selection clears the search'

    # --- the alphabetical view, kept for the class --------------------------------------------------------
    Check (Set-Toggled 'Inspector.AlphabeticalView') 'Alphabetical View'
    Check (Toggled 'Inspector.AlphabeticalView') 'it is on'
    Check ((Inspector-Value 'Tag') -ne $null) 'a row of a closed group shows in it'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'alphabetical.png')) 'a picture of it'
    Check (Select-Component 'L') 'a label selected'
    Check (Toggled 'Inspector.GroupView') 'another class has its own view'
    Check (Select-Component 'B') 'another button selected'
    Check (Toggled 'Inspector.AlphabeticalView') 'the button''s class kept its'
    Check (Set-Toggled 'Inspector.GroupView') 'Group View'

    # --- a sub-group, behind its bar -----------------------------------------------------------------------
    Check (Select-Component 'Sp') 'the spinner selected'
    Check ((Tab-Says) -eq 'Spinner') 'the tab says Spinner'
    Check (Shown-In-Inspector 'Value') 'its Value'
    Check (Shown-In-Inspector 'ValueDisplayFormat') 'its display format'
    Check (-not (Shown-In-Inspector 'LowerLimitInclusive')) 'LowerLimitInclusive is behind the bar'
    $bar = Find-In $script:hwnd 'Inspector.More.Value.0'
    Check ($null -ne $bar -and $bar.Current.Name -eq 'More Properties') 'the bar is there'
    Check (Invoke-Element $bar) 'the bar pressed'
    Check (Shown-In-Inspector 'LowerLimitInclusive' 3) 'it shows'
    Check ((Find-In $script:hwnd 'Inspector.More.Value.0').Current.Name -eq 'Fewer Properties') 'the bar says so'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'subgroup.png')) 'a picture of the sub-group'
    Check (Select-Component 'Lp') 'the lamp selected'
    Check (Select-Component 'Sp') 'the spinner again'
    Check (Shown-In-Inspector 'LowerLimitInclusive' 3) 'the class kept the bar open'

    # --- a value refused -----------------------------------------------------------------------------------
    Check (Select-Component 'D') 'D selected'
    Check (Edit-Row 'FontSize' '-3') 'a font size no component takes'
    Start-Sleep -Milliseconds 700
    $alert = Find-Window $p.Id { param($t) $t -eq 'App Designer' } 6
    Check ($null -ne $alert) 'App Designer''s alert says so'
    if ($alert) {
        Check (Save-Picture $alert.Key (Join-Path $WorkRoot 'alert.png')) 'a picture of the alert'
        [void] [W7bNative]::PostMessage($alert.Key, $WM_COMMAND, [IntPtr] $IDOK, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 900
    }
    Check ((Inspector-Value 'FontSize') -eq '12') "the row goes back to 12 ($(Inspector-Value 'FontSize'))"

    # --- several, by Ctrl+A on the canvas ---------------------------------------------------------------
    Check (Focus-Canvas) 'the canvas in front'
    Check (Post-CtrlKey $VK_A) 'Ctrl+A'
    Check ((Tab-Says) -eq 'Multiple Components') "the tab says Multiple Components ($(Tab-Says))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'several.png')) 'a picture of several'
    Check (-not (Shown-In-Inspector 'CodeName')) 'no code name row'
    Check (-not (Shown-In-Inspector 'Text')) 'the lamp and the spinner have no Text, so nor do they'
    Check (Open-Group 'Interactivity') 'Interactivity opened'
    Check (Shown-In-Inspector 'Visible') 'Visible is shared'
    Check (Open-Group 'Identifiers') 'Identifiers opened'
    Check (Edit-Row 'Tag' 'all') 'Tag typed for them all'
    Check (Save-App $app) 'Save writes it'
    foreach ($entry in @(@('Button', 'D'), @('Button', 'A'), @('Button', 'B'), @('Label', 'L'), @('EditField', 'EditField'), @('Spinner', 'Sp'), @('Lamp', 'Lp'))) {
        $element = Saved-Element $app $entry[0] $entry[1]
        Check ($null -ne $element -and $element.Contains("<Tag>'all'</Tag>")) "$($entry[1])'s Tag is saved"
    }

    Check ((Tab-Says) -eq 'Multiple Components') 'they stay selected'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (Save-App $app) 'Save writes it'
    Check (-not ((Saved-Text) -match "<Tag>'all'</Tag>")) 'one Undo takes it off them all'
    Check ((Tab-Says) -eq 'Multiple Components') 'and they stay selected'

    # A search over several.
    $search = Find-In $script:hwnd 'Inspector.Search'
    Check (Set-Text $search 'tag') 'tag typed in the search'
    Check (Shown-In-Inspector 'Tag') 'Tag is found among what they share'
    Check (Invoke-Element (Find-In $script:hwnd 'Inspector.ClearSearch')) 'cleared'

    # --- one again, and nothing selected ------------------------------------------------------------------
    # (A row already among the selected ones takes no Select through UI Automation.)
    Check (Select-Component 'UIFigure') 'the figure selected'
    Check ((Tab-Says) -eq 'UI Figure') "the tab says UI Figure ($(Tab-Says))"
    Check (Select-Component 'Lp') 'the lamp selected'
    Check ((Tab-Says) -eq 'Lamp') 'the tab says Lamp'
    Check ((Inspector-Value 'CodeName') -eq 'Lp') 'and shows it'
    Check (Focus-Canvas) 'the canvas in front'
    Check (Post-Key 0x2E) 'Delete'
    $hidden = Find-In $script:hwnd 'Inspector.Search' 1
    Check ($null -eq $hidden -or $hidden.Current.IsOffscreen) 'with nothing selected the panel hides'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'nothing.png')) 'a picture of nothing selected'
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
