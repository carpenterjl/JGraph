<#
.SYNOPSIS
    Stage W5d's window check (App Designer window plan): a figure's menus, toolbars and context menus
    built on the canvas - the bars drawn, Add Menu Below and to Right, Move, Paste to Right, Add Push
    Tool and Add Toggle Tool, a context menu made, assigned and taken off, the tray - in the JGraph
    application, each saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a menu bar (M1 holding I1, and M2) and
    two toolbars (TB holding PT, and TB2 holding PT2) added, and starts JGraph.Application.exe with
    -r "appdesigner('<app>')". Components are selected in the component browser and the canvas given
    the keyboard through UI Automation patterns; the canvas's menu items are invoked, and its submenus
    expanded, through their patterns; the editor's text is set through its ValuePattern. The keys - the
    menu key, Enter, F2, Escape, P and T - are window messages posted to the designer's own window
    handle, never input sent to whatever window is in front, and only once the designer's window is
    checked to be the foreground one.

    Checks: Enter on a menu adds an item after it; F2 opens the editor on its text and Enter saves the
    text set; the menu key's Add Menu puts a top-level menu to the right; Move > Last orders the bar;
    Copy and Paste to Right; P and T on a tool add tools after it; Move on a toolbar orders the
    toolbars; Context Menu > Add New Context Menu makes one, assigned, and shows the tray; Assign gives
    it to another component and Unassign takes it off; a context menu's item selected opens it in the
    tray; Undo. Pictures go into <WorkRoot>.

    A press on the bars, their +s, a drag of a menu or tool, the tray's own presses and a context
    menu's drag to a component need a mouse, and the Ctrl+Shift keys held modifiers, which window
    messages do not carry; they are left to AppDesignerBarsTests, which drive the same gestures
    without a window.

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
public static class W5dNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
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

# W4's app (w4\W4App.m: buttons D, A and B, the label L and an edit field), with a menu bar M1 (holding
# I1) and M2 and toolbars TB (holding PT) and TB2 (holding PT2) added.
$app = Join-Path $WorkRoot 'W4App.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$declared = @('M1 matlab.ui.container.Menu', 'I1 matlab.ui.container.Menu', 'M2 matlab.ui.container.Menu',
    'TB matlab.ui.container.Toolbar', 'PT matlab.ui.container.toolbar.PushTool', 'TB2 matlab.ui.container.Toolbar', 'PT2 matlab.ui.container.toolbar.PushTool') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = $text.Replace("        D          matlab.ui.control.Button$nl", "        D          matlab.ui.control.Button$nl" + ($declared -join ''))
$layout = @(
    "            <Menu name='M1'>",
    "                <Text>'File'</Text>",
    "                <Children>",
    "                    <Menu name='I1'>",
    "                        <Text>'Open'</Text>",
    "                    </Menu>",
    "                </Children>",
    "            </Menu>",
    "            <Menu name='M2'>",
    "                <Text>'Edit'</Text>",
    "            </Menu>",
    "            <Toolbar name='TB'>",
    "                <Children>",
    "                    <PushTool name='PT'>",
    "                    </PushTool>",
    "                </Children>",
    "            </Toolbar>",
    "            <Toolbar name='TB2'>",
    "                <Children>",
    "                    <PushTool name='PT2'>",
    "                    </PushTool>",
    "                </Children>",
    "            </Toolbar>") -join $nl
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
        foreach ($entry in [W5dNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W5dNative+RECT
    [void] [W5dNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W5dNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W5dNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W5dNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W5dNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W5dNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_RETURN = 0x0D; $VK_F2 = 0x71; $VK_APPS = 0x5D; $VK_ESCAPE = 0x1B; $VK_P = 0x50; $VK_T = 0x54

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W5dNative]::Windows([uint32] $p.Id)) {
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

function Editor { return Find-In $script:hwnd 'Canvas.InPlaceEditor' 3 }

function Editor-Text {
    $value = Pattern (Editor) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Set-EditorText([string] $text) {
    $value = Pattern (Editor) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $false }
    $value.SetValue($text)
    Start-Sleep -Milliseconds 300
    return $true
}

function Save-App([string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

# The names of the components the saved layout holds, in its order, of one element kind.
function Saved-Names([string] $file, [string] $kind) {
    return @([regex]::Matches([System.IO.File]::ReadAllText($file), "<$kind name='([^']+)'>") | ForEach-Object { $_.Groups[1].Value })
}

# The text of a component's element in the saved layout, up to its own closing tag.
function Saved-Element([string] $file, [string] $kind, [string] $name) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($file), "(?s)<$kind name='$name'>(.*?)</$kind>")
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value
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

    # --- a menu: Enter, F2 ------------------------------------------------------------------------------
    Check (Select-Component 'I1') 'I1 selected in the component browser'
    $front = Focus-Canvas
    Check $front 'the canvas has the keyboard, the designer in front'
    if (-not $front) { throw 'the designer is not the foreground window: no key is posted' }
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'bars.png')) 'a picture of the bars, the list open under M1'
    Check (Post-Key $VK_RETURN) 'Enter on I1'
    Check (Save-App $app) 'Save writes the app'
    $all = [System.IO.File]::ReadAllText($app)
    Check ($all -match "(?s)<Menu name='I1'>.*?</Menu>\s*<Menu name='[^']+'>\s*<Text>'Menu'</Text>") "Enter adds an item after I1 under M1"
    $added = [regex]::Matches($all, "<Menu name='([^']+)'>") | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notin @('M1', 'I1', 'M2') } | Select-Object -First 1
    Check ($null -ne $added) "the item is $added"
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_F2) 'F2 on it'
    Check ((Editor-Text) -eq 'Menu') "the editor is on its text (got '$(Editor-Text)')"
    Check (Set-EditorText 'Save') 'the editor set to Save'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check ($null -eq (Editor)) 'Enter closes the editor'
    Check (Save-App $app) 'Save writes it'
    $all = [System.IO.File]::ReadAllText($app)
    Check ($all -match "<Text>'Save'</Text>") 'its text is Save'

    # --- top-level: Add Menu, Move, Paste to Right ------------------------------------------------------
    Check (Select-Component 'M1') 'M1 selected'
    Check (Run-Menu @('AddMenu')) "the menu key on M1 brings M1's menu, Add Menu invoked"
    Check (Save-App $app) 'Save writes it'
    $all = [System.IO.File]::ReadAllText($app)
    $tops = @([regex]::Matches($all, "(?m)^            <Menu name='([^']+)'>") | ForEach-Object { $_.Groups[1].Value })
    Check ($tops.Count -eq 3 -and $tops[0] -eq 'M1' -and $tops[2] -eq 'M2') "a menu to M1's right (got $($tops -join ','))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'menu-added.png')) 'a picture of the new menu'

    Check (Select-Component 'M1') 'M1 selected'
    Check (Run-Menu @('Move', 'Last')) 'Move > Last'
    Check (Save-App $app) 'Save writes it'
    $all = [System.IO.File]::ReadAllText($app)
    $tops = @([regex]::Matches($all, "(?m)^            <Menu name='([^']+)'>") | ForEach-Object { $_.Groups[1].Value })
    Check ($tops[-1] -eq 'M1') "M1 is last (got $($tops -join ','))"

    Check (Select-Component 'M2') 'M2 selected'
    Check (Run-Menu @('Copy')) 'Copy'
    Check (Select-Component 'M1') 'M1 selected'
    Check (Run-Menu @('PastetoRight')) 'Paste to Right'
    Check (Save-App $app) 'Save writes it'
    $all = [System.IO.File]::ReadAllText($app)
    $tops = @([regex]::Matches($all, "(?m)^            <Menu name='([^']+)'>") | ForEach-Object { $_.Groups[1].Value })
    Check ($tops.Count -eq 4 -and $tops[2] -eq 'M1') "the copy after M1 (got $($tops -join ','))"

    # --- tools and toolbars ------------------------------------------------------------------------------
    Check (Select-Component 'PT') 'PT selected'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_P) 'P on it'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_T) 'T on the new tool'
    Check (Save-App $app) 'Save writes it'
    $tb = Saved-Element $app 'Toolbar' 'TB'
    Check ($tb -match "(?s)<PushTool name='PT'>.*<PushTool name='[^']+'>.*<ToggleTool name='") "a push and a toggle tool after PT"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tools.png')) 'a picture of the tools'

    Check (Select-Component 'TB2') 'TB2 selected'
    Check (Run-Menu @('Move', 'First')) 'Move > First on the toolbar'
    Check (Save-App $app) 'Save writes it'
    $bars = Saved-Names $app 'Toolbar'
    Check (($bars -join ',') -eq 'TB2,TB') "TB2 is first (got $($bars -join ','))"

    # --- a context menu ----------------------------------------------------------------------------------
    Check ($null -eq (Find-In $script:hwnd 'Canvas.ContextMenuTray' 1)) 'no tray with no context menu'
    Check (Select-Component 'D') 'D selected'
    Check (Run-Menu @('ContextMenu', 'AddNewContextMenu')) 'Context Menu > Add New Context Menu'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Names $app 'ContextMenu') -contains 'ContextMenu') 'the context menu is in the app'
    $tray = Find-In $script:hwnd 'Canvas.ContextMenuTray' 3
    Check ($null -ne $tray) 'the tray shows'
    Check ($tray -and $tray.Current.Name -eq 'Context Menus: ContextMenu (1)') "assigned to one (got '$($tray.Current.Name)')"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tray.png')) 'a picture of the tray'

    Check (Select-Component 'A') 'A selected'
    Check (Run-Menu @('ContextMenu', 'Assign', 'app.ContextMenu')) 'Context Menu > Assign > app.ContextMenu'
    $tray = Find-In $script:hwnd 'Canvas.ContextMenuTray' 3
    Check ($tray.Current.Name -eq 'Context Menus: ContextMenu (2)') "assigned to two (got '$($tray.Current.Name)')"
    Check (Run-Menu @('ContextMenu', 'UnassignContextMenu')) 'Context Menu > Unassign Context Menu'
    $tray = Find-In $script:hwnd 'Canvas.ContextMenuTray' 3
    Check ($tray.Current.Name -eq 'Context Menus: ContextMenu (1)') "one again (got '$($tray.Current.Name)')"

    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    $tray = Find-In $script:hwnd 'Canvas.ContextMenuTray' 3
    Check ($tray.Current.Name -eq 'Context Menus: ContextMenu (2)') "Undo gives it back (got '$($tray.Current.Name)')"

    # A context menu's item selected opens it in the tray; Escape goes up to it, shut.
    $item = ([regex]::Match([System.IO.File]::ReadAllText($app), "(?s)<ContextMenu name='ContextMenu'>.*?<Menu name='([^']+)'>")).Groups[1].Value
    Check ($item.Length -gt 0) "its first item is $item"
    Check (Select-Component $item) "$item selected"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tray-editing.png')) 'a picture of the tray open on it'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_RETURN) 'Enter on the item'
    Check (Save-App $app) 'Save writes it'
    $context = Saved-Element $app 'ContextMenu' 'ContextMenu'
    Check (([regex]::Matches("$context", "<Menu name=")).Count -eq 3) 'three items now'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_ESCAPE) 'Escape'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tray-after.png')) 'a picture of the tray shut again'
    Check (-not $p.HasExited) 'the window is still up'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
