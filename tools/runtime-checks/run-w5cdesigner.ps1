<#
.SYNOPSIS
    Stage W5c's window check (App Designer window plan): tabs, a button group and a tree's nodes
    built on the canvas - Add Tab, Move First, Add Radio Button, a tree's Enter, F2 on a node and
    Increase Indent - in the JGraph application, each saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a tab group (two tabs), a radio
    button group (two buttons) and a tree (a node holding two) added, and starts
    JGraph.Application.exe with -r "appdesigner('<app>')". Components are selected in the component
    browser and the canvas given the keyboard through UI Automation patterns; the canvas's menu
    items are invoked, and its submenus expanded, through their patterns; the editor's text is set
    through its ValuePattern. The keys - the menu key, Enter and F2 - are window messages posted to
    the designer's own window handle, never input sent to whatever window is in front, and only
    once the designer's window is checked to be the foreground one.

    Checks: the menu key on a tab brings Add Tab, which adds a tab after it, and Move > First, which
    puts it first; on the button group, Add Radio Button, which puts a button under the lowest at
    R2025b's place and size; Enter on a node adds a sibling after it; F2 on it opens the editor on
    its text, Enter saves the text set; the menu key on it brings the node's menu, whose Increase
    Indent moves it into the node before it; Undo takes that back. Pictures go into <WorkRoot>.

    The Ctrl+Shift and Ctrl+bracket keys, a heading's drag, the strip's +, the tree's + and a
    node's twisty need a mouse or held modifiers, which window messages do not carry; they are left
    to AppDesignerStructureTests, which drive the same gestures without a window.

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
public static class W5cNative {
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

# W4's app (w4\W4App.m: buttons D, A and B, the label L and an edit field), with a tab group TG
# (tabs T1 and T2), a radio button group G (R1 and R2) and a tree TR (N holding N2 and N3) added.
$app = Join-Path $WorkRoot 'W4App.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$declared = @('TG matlab.ui.container.TabGroup', 'T1 matlab.ui.container.Tab', 'T2 matlab.ui.container.Tab',
    'G matlab.ui.container.ButtonGroup', 'R1 matlab.ui.control.RadioButton', 'R2 matlab.ui.control.RadioButton',
    'TR matlab.ui.container.Tree', 'N matlab.ui.container.TreeNode', 'N2 matlab.ui.container.TreeNode', 'N3 matlab.ui.container.TreeNode') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = $text.Replace("        D          matlab.ui.control.Button$nl", "        D          matlab.ui.control.Button$nl" + ($declared -join ''))
$layout = @(
    "            <TabGroup name='TG'>",
    "                <Position>[330 250 290 200]</Position>",
    "                <Children>",
    "                    <Tab name='T1'>",
    "                        <Title>'One'</Title>",
    "                    </Tab>",
    "                    <Tab name='T2'>",
    "                        <Title>'Two'</Title>",
    "                    </Tab>",
    "                </Children>",
    "            </TabGroup>",
    "            <ButtonGroup name='G'>",
    "                <Position>[330 20 140 120]</Position>",
    "                <Title>'Mode'</Title>",
    "                <Children>",
    "                    <RadioButton name='R1'>",
    "                        <Position>[11 70 60 22]</Position>",
    "                        <Text>'Fast'</Text>",
    "                        <Value>true</Value>",
    "                    </RadioButton>",
    "                    <RadioButton name='R2'>",
    "                        <Position>[11 48 60 22]</Position>",
    "                        <Text>'Slow'</Text>",
    "                    </RadioButton>",
    "                </Children>",
    "            </ButtonGroup>",
    "            <Tree name='TR'>",
    "                <Position>[150 20 150 160]</Position>",
    "                <Children>",
    "                    <TreeNode name='N'>",
    "                        <Text>'Node'</Text>",
    "                        <Children>",
    "                            <TreeNode name='N2'>",
    "                                <Text>'Node2'</Text>",
    "                            </TreeNode>",
    "                            <TreeNode name='N3'>",
    "                                <Text>'Node3'</Text>",
    "                            </TreeNode>",
    "                        </Children>",
    "                    </TreeNode>",
    "                </Children>",
    "            </Tree>") -join $nl
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
        foreach ($entry in [W5cNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W5cNative+RECT
    [void] [W5cNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W5cNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W5cNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W5cNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W5cNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W5cNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_RETURN = 0x0D; $VK_F2 = 0x71; $VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W5cNative]::Windows([uint32] $p.Id)) {
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

    # --- Add Tab after a tab, Move > First ---------------------------------------------------------------
    Check (Select-Component 'T1') 'T1 selected in the component browser'
    $front = Focus-Canvas
    Check $front 'the canvas has the keyboard, the designer in front'
    if (-not $front) { throw 'the designer is not the foreground window: no key is posted' }
    Check (Run-Menu @('AddTab')) 'the menu key on T1 brings Add Tab, invoked'
    Check (Save-App $app) 'Save writes the app'
    $tabs = Saved-Names $app 'Tab'
    Check (($tabs -join ',') -eq 'T1,Tab,T2') "the new tab is after T1 (got $($tabs -join ','))"
    Check ((Saved-Element $app 'Tab' 'Tab') -match "<Title>'Tab'</Title>") 'titled Tab among One and Two'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'tab-added.png')) 'a picture of the new tab'

    Check (Select-Component 'Tab') 'the new tab selected'
    Check (Run-Menu @('Move', 'First')) 'Move > First'
    Check (Save-App $app) 'Save writes it'
    $tabs = Saved-Names $app 'Tab'
    Check (($tabs -join ',') -eq 'Tab,T1,T2') "it is first (got $($tabs -join ','))"

    # --- Add Radio Button ----------------------------------------------------------------------------
    Check (Select-Component 'G') 'G selected'
    Check (Run-Menu @('AddRadioButton')) 'the menu key on G brings Add Radio Button, invoked'
    Check (Save-App $app) 'Save writes it'
    $radios = Saved-Names $app 'RadioButton'
    Check (($radios -join ',') -eq 'R1,R2,Button') "a button after R1 and R2 (got $($radios -join ','))"
    $made = Saved-Element $app 'RadioButton' 'Button'
    Check ($made -match '<Position>\[11 25 58 22\]</Position>') "under R2 at [11 25 58 22] (got $([regex]::Match("$made", '<Position>[^<]*').Value))"
    Check ($made -match "<Text>'Button'</Text>" -and $made -notmatch '<Value>') 'its text Button, not selected'

    # --- a tree's node: Enter, F2, Increase Indent ----------------------------------------------------
    Check (Select-Component 'N2') 'N2 selected'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'node-selected.png')) 'a picture of the node selected, with the tree''s +'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check (Save-App $app) 'Save writes it'
    $nodes = Saved-Names $app 'TreeNode'
    Check (($nodes -join ',') -eq 'N,N2,Node4,N3') "Enter adds Node4 after N2 (got $($nodes -join ','))"

    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_F2) 'F2 on the new node'
    Check ((Editor-Text) -eq 'Node4') "the editor is on its text (got '$(Editor-Text)')"
    Check (Set-EditorText 'Leaf') 'the editor set to Leaf'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check ($null -eq (Editor)) 'Enter closes the editor'
    Check (Save-App $app) 'Save writes it'
    $all = [System.IO.File]::ReadAllText($app)
    $leaf = [regex]::Match($all, "<TreeNode name='([^']+)'>\s*<Text>'Leaf'</Text>")
    Check $leaf.Success 'its text is Leaf'
    $leafName = $leaf.Groups[1].Value

    Check (Select-Component $leafName) "$leafName selected"
    Check (Run-Menu @('IncreaseIndent')) "the menu key on $leafName brings the node's menu, Increase Indent invoked"
    Check (Save-App $app) 'Save writes it'
    $n2 = Saved-Element $app 'TreeNode' 'N2'
    Check ($n2 -match "<TreeNode name='$leafName'>") "$leafName is inside N2"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'indented.png')) 'a picture after the indent'

    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (Save-App $app) 'Save writes it again'
    $nodes = Saved-Names $app 'TreeNode'
    Check (($nodes -join ',') -eq "N,N2,$leafName,N3") "the node is back after N2 (got $($nodes -join ','))"
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
