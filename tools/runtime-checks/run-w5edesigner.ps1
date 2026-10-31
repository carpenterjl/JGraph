<#
.SYNOPSIS
    Stage W5e's window check (App Designer window plan): Replace With on the canvas's menu and a tool's
    menu - a labelled slider made a knob, a button a state button, a label a hyperlink, an edit field a
    text area, a tree a check box tree with its nodes, a push tool a toggle tool - in the JGraph
    application, each saved and read back from the file, one undone and redone.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a slider, a tree holding a node and a
    toolbar holding a tool added, and starts JGraph.Application.exe with -r "appdesigner('<app>')".
    Components are selected in the component browser and the canvas given the keyboard through UI
    Automation patterns; the canvas's menu items are invoked, and its submenus expanded, through their
    patterns. The menu key is a window message posted to the designer's own window handle, never input
    sent to whatever window is in front, and only once the designer's window is checked to be the
    foreground one.

    A callback's fate, a name following into the user's code, a grid child and a member of a group are
    left to AppDesignerReplaceTests, which drive the same edit without a window.

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
public static class W5eNative {
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

# W4's app (w4\W4App.m: buttons D, A and B, the label L and an edit field), with a slider, a tree Tr
# holding a node N1 and a toolbar TB holding a tool PT added.
$app = Join-Path $WorkRoot 'W4App.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$declared = @('Slider matlab.ui.control.Slider', 'Tr matlab.ui.container.Tree', 'N1 matlab.ui.container.TreeNode',
    'TB matlab.ui.container.Toolbar', 'PT matlab.ui.container.toolbar.PushTool') |
    ForEach-Object { $parts = $_.Split(' '); '        {0,-10} {1}{2}' -f $parts[0], $parts[1], $nl }
$text = $text.Replace("        D          matlab.ui.control.Button$nl", "        D          matlab.ui.control.Button$nl" + ($declared -join ''))
$layout = @(
    "            <Slider name='Slider'>",
    "                <Position>[300 300 150 3]</Position>",
    "            </Slider>",
    "            <Tree name='Tr'>",
    "                <Position>[30 30 150 100]</Position>",
    "                <Children>",
    "                    <TreeNode name='N1'>",
    "                        <Text>'One'</Text>",
    "                    </TreeNode>",
    "                </Children>",
    "            </Tree>",
    "            <Toolbar name='TB'>",
    "                <Children>",
    "                    <PushTool name='PT'>",
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
        foreach ($entry in [W5eNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W5eNative+RECT
    [void] [W5eNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W5eNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W5eNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W5eNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W5eNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W5eNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W5eNative]::Windows([uint32] $p.Id)) {
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

    # --- a labelled slider: the menu's items, a knob, undone and redone ---------------------------------
    Check (Select-Component 'Slider') 'Slider selected'
    Check (Run-Menu @('AddLabel')) 'Add Label makes it a labelled component'
    Check (Save-App $app) 'Save writes it'
    $madeLabel = ([regex]::Match([System.IO.File]::ReadAllText($app), "<Slider name='Slider' label='([^']+)'>")).Groups[1].Value
    Check ($madeLabel.Length -gt 0) "the slider's label is $madeLabel"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'slider.png')) 'a picture of the slider'

    Check (Select-Component 'Slider') 'Slider selected'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_APPS) 'the menu key'
    $replace = Find-Menu 'Menu.ReplaceWith'
    Check ($null -ne $replace) 'the menu has Replace With'
    (Pattern $replace ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 500
    foreach ($id in 'Knob', 'EditField(Numeric)', 'Slider(Range)', 'Spinner') {
        Check ($null -ne (Find-Menu "Menu.$id" 2)) "it offers $id"
    }

    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'menu.png')) 'a picture with the menu open'
    Check (Invoke-Element (Find-Menu 'Menu.Knob')) 'Knob invoked'
    Check (Save-App $app) 'Save writes it'
    Check ($null -ne (Saved-Element $app 'Knob' 'Knob')) 'the slider is a Knob called Knob'
    Check ($null -eq (Saved-Element $app 'Slider' 'Slider')) 'no slider is left'
    $label = Saved-Element $app 'Label' 'KnobLabel'
    Check ("$label" -match "<Text>'Knob'</Text>") "the label is KnobLabel, saying Knob (got '$label')"
    $code = [System.IO.File]::ReadAllText($app)
    Check ($code -match 'Knob\s+matlab\.ui\.control\.Knob') 'the class declares it'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'knob.png')) 'a picture of the knob'

    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (Save-App $app) 'Save writes it'
    Check ($null -ne (Saved-Element $app 'Slider' 'Slider')) 'Undo gives the slider back'
    Check ($null -eq (Saved-Element $app 'Knob' 'Knob')) 'and no knob'
    Check ($null -ne (Saved-Element $app 'Label' $madeLabel)) "and its label its name, $madeLabel"
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.redoButton')) 'Redo'
    Check (Save-App $app) 'Save writes it'
    Check ($null -ne (Saved-Element $app 'Knob' 'Knob')) 'Redo makes the knob again'

    # --- the families -----------------------------------------------------------------------------------
    Check (Select-Component 'D') 'D selected'
    Check (Run-Menu @('ReplaceWith', 'StateButton')) 'Replace With > State Button on D'
    Check (Save-App $app) 'Save writes it'
    $state = Saved-Element $app 'StateButton' 'D'
    Check ("$state" -match "<Text>'D'</Text>") "a state button called D by hand, saying D (got '$state')"

    Check (Select-Component 'L') 'L selected'
    Check (Run-Menu @('ReplaceWith', 'Hyperlink')) 'Replace With > Hyperlink on L'
    Check (Save-App $app) 'Save writes it'
    $link = Saved-Element $app 'Hyperlink' 'L'
    Check ("$link" -match "<Text>'L'</Text>") "a hyperlink saying L (got '$link')"

    Check (Select-Component 'EditField') 'EditField selected'
    Check (Run-Menu @('ReplaceWith', 'TextArea')) 'Replace With > Text Area on EditField'
    Check (Save-App $app) 'Save writes it'
    Check ($null -ne (Saved-Element $app 'TextArea' 'TextArea')) 'a text area called TextArea'

    Check (Select-Component 'Tr') 'Tr selected'
    Check (Run-Menu @('ReplaceWith', 'Tree(CheckBox)')) 'Replace With > Tree (Check Box) on Tr'
    Check (Save-App $app) 'Save writes it'
    Check ("$(Saved-Element $app 'CheckBoxTree' 'Tr')" -match "<TreeNode name='N1'>") 'a check box tree holding N1'

    Check (Select-Component 'PT') 'PT selected'
    Check (Run-Menu @('ReplaceWith', 'ToggleTool')) "Replace With > Toggle Tool on the tool's own menu"
    Check (Save-App $app) 'Save writes it'
    Check ("$(Saved-Element $app 'Toolbar' 'TB')" -match "<ToggleTool name='PT'>") 'a toggle tool in the toolbar'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'after.png')) 'a picture of them all'
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
