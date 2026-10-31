<#
.SYNOPSIS
    Stage W7a's window check (App Designer window plan): the component browser - the app's row and rows
    saying app.Name, labels left out and put back by its menu, the search, the tab order's view, a name
    edited in place, its menu's Delete, Reorder, Duplicate and Insert at Cursor, and its breadcrumbs - in the
    JGraph application, each edit saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a slider, a tree holding a node and a toolbar
    holding a tool added, and starts JGraph.Application.exe with -r "appdesigner('<app>')". Rows are selected
    and the search and the editor set through UI Automation patterns; the menu's items are invoked, and its
    submenus expanded, through their patterns. The menu key, F2, Enter and Escape are window messages posted
    to the designer's own window handle, never input sent to whatever window is in front, and only once the
    designer's window is checked to be the foreground one.

    Ctrl and Shift clicks and a row's drag need a held button or key, which a posted message cannot give, so
    they are left to AppDesignerBrowserTests and DesignBrowserOracleTests, which run the same rules without a
    window.

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
public static class W7aNative {
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
        foreach ($entry in [W7aNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W7aNative+RECT
    [void] [W7aNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W7aNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W7aNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101
function Post-Key([int] $vk) {
    if ([W7aNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W7aNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W7aNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

$VK_APPS = 0x5D

# A menu item of the canvas's menu, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W7aNative]::Windows([uint32] $p.Id)) {
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
    return ([W7aNative]::GetForegroundWindow() -eq $script:hwnd)
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

$VK_RETURN = 0x0D; $VK_ESCAPE = 0x1B; $VK_F2 = 0x71

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

    # --- the rows ---------------------------------------------------------------------------------------
    foreach ($row in 'W4App', 'UIFigure', 'D', 'A', 'B', 'L', 'EditField', 'Slider', 'Tr', 'N1', 'TB', 'PT') {
        Check (Row-Shown $row) "the browser lists $row"
    }

    $browser = Find-In $script:hwnd 'ComponentBrowser'
    $said = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'app.D')
    Check ($null -ne $browser.FindFirst($Tree::Subtree, $said)) 'a row says app.D'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'rows.png')) 'a picture of the rows'

    # --- labels: left out, and put back by the menu -------------------------------------------------------
    Check (Select-Component 'Slider') 'Slider selected'
    Check (Run-Menu @('AddLabel')) 'Add Label on the canvas'
    Check (Save-App $app) 'Save writes it'
    $madeLabel = ([regex]::Match([System.IO.File]::ReadAllText($app), "<Slider name='Slider' label='([^']+)'>")).Groups[1].Value
    Check ($madeLabel.Length -gt 0) "the slider's label is $madeLabel"
    Check (-not (Row-Shown $madeLabel 1)) 'a labelled component''s label is left out'
    Check (Run-RowMenu 'Slider' @('IncludecomponentlabelsinComponentBrowser')) 'Include labels on'
    Check (Row-Shown $madeLabel) 'the label is listed'
    Check (Run-RowMenu 'Slider' @('IncludecomponentlabelsinComponentBrowser')) 'Include labels off'
    Check (-not (Row-Shown $madeLabel 1)) 'the label is left out again'

    # --- the search -------------------------------------------------------------------------------------
    $search = Find-In $script:hwnd 'ComponentBrowser.Search'
    Check ($null -ne $search) 'the search field'
    Check (Set-Text $search 'zzz') 'a search for what is not there'
    Check ($null -ne (Find-In $script:hwnd 'ComponentBrowser.NoMatches' 3)) 'it says no matches'
    Check (-not (Row-Shown 'D' 1)) 'and lists nothing'
    Check (Set-Text $search 'SLID') 'a search in the wrong case'
    Check (Row-Shown 'Slider') 'it finds Slider'
    Check (Row-Shown 'UIFigure') 'with the figure above it'
    Check (-not (Row-Shown 'D' 1)) 'and not D'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'search.png')) 'a picture of the search'
    Check (Invoke-Element (Find-In $script:hwnd 'ComponentBrowser.ClearSearch')) 'the clear button'
    Check (Row-Shown 'D') 'every row is back'

    # --- the tab order's view -----------------------------------------------------------------------------
    Check (Invoke-Element (Find-In $script:hwnd 'ComponentBrowser.View')) 'the View drop-down'
    Check (Invoke-Element (Find-Menu 'ComponentBrowser.View.Tab')) 'Sort & Filter by Tab Order'
    Check (-not (Row-Shown 'L' 1)) 'the tab order leaves a label out'
    Check (Row-Shown 'D') 'and lists a button'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'taborder.png')) 'a picture of the tab order'
    Check (Run-RowMenu 'D' @('SortbyStackOrder')) 'the menu''s Sort by Stack Order'
    Check (Row-Shown 'L') 'the stack order lists the label'

    # --- a name in place ------------------------------------------------------------------------------------
    Check (Run-RowMenu 'D' @('Rename')) 'Rename on D'
    $editor = Find-In $script:hwnd 'ComponentBrowser.Rename' 3
    Check ($null -ne $editor) 'the row has an editor'
    Check ((Pattern $editor ([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value -eq 'D') 'it holds the name'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'rename.png')) 'a picture of the editor'
    Check (Set-Text $editor 'Done') 'a new name'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check (Row-Shown 'Done') 'the row has it'
    Check (Save-App $app) 'Save writes it'
    Check ($null -ne (Saved-Element $app 'Button' 'Done')) 'the file holds Done'
    Check ($null -eq (Saved-Element $app 'Button' 'D')) 'and no D'

    Check (Focus-Row 'Done') 'Done has the keyboard'
    Check (Post-Key $VK_F2) 'F2'
    $editor = Find-In $script:hwnd 'ComponentBrowser.Rename' 3
    Check ($null -ne $editor) 'F2 opens the editor'
    Check (Set-Text $editor 'Unwanted') 'a name not wanted'
    Check (Post-Key $VK_ESCAPE) 'Escape'
    Check (Row-Shown 'Done') 'Escape keeps the name'
    Check (-not (Row-Shown 'Unwanted' 1)) 'and sets none'

    Check (Focus-Row 'Done') 'Done has the keyboard'
    Check (Post-Key $VK_F2) 'F2'
    $editor = Find-In $script:hwnd 'ComponentBrowser.Rename' 3
    Check (Set-Text $editor '') 'an empty name'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check (Row-Shown 'Done') 'an empty name changes nothing'

    # --- the menu's edits -------------------------------------------------------------------------------
    Check (Run-RowMenu 'B' @('Delete')) 'Delete on B'
    Check (-not (Row-Shown 'B' 1)) 'B is gone'
    Check (Save-App $app) 'Save writes it'
    Check ($null -eq (Saved-Element $app 'Button' 'B')) 'the file has no B'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (Row-Shown 'B') 'B is back'

    Check (Run-RowMenu 'Done' @('Reorder', 'BringtoFront')) 'Reorder > Bring to Front on Done'
    Check (Save-App $app) 'Save writes it'
    $text = [System.IO.File]::ReadAllText($app)
    $last = [regex]::Matches($text, "(?m)^\s{12}<\w+ name='(\w+)'") | ForEach-Object { $_.Groups[1].Value }
    Check (($last | Where-Object { $_ -ne 'TB' } | Select-Object -Last 1) -eq 'Done') "Done is made last but for the toolbar (got $($last -join ','))"
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'

    Check (Run-RowMenu 'L' @('Duplicate')) 'Duplicate on L'
    Check (Row-Shown 'L_2') 'the copy is listed'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (-not (Row-Shown 'L_2' 1)) 'the copy is gone'

    # --- the breadcrumbs -------------------------------------------------------------------------------
    Check (Select-Component 'Slider') 'Slider selected'
    $browser = Find-In $script:hwnd 'ComponentBrowser'
    $appRow = $browser.FindFirst($Tree::Subtree, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'W4App')))
    (Pattern $appRow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse()
    Start-Sleep -Milliseconds 700
    Check ($null -ne (Find-In $script:hwnd 'ComponentBrowser.Expand' 3)) 'the app''s row closes the browser to its breadcrumbs'
    Check (-not (Row-Shown 'Slider' 1)) 'and the rows go'
    $shownName = Pattern (Find-In $script:hwnd 'Inspector.CodeName' 3) ([System.Windows.Automation.ValuePattern]::Pattern)
    Check ($null -ne $shownName -and $shownName.Current.Value -eq 'Slider') 'closing it leaves the selection on Slider'
    Check ($null -ne (Find-In $script:hwnd 'ComponentBrowser.Crumb.W4App' 3)) 'they start at the app'
    Check ($null -ne (Find-In $script:hwnd 'ComponentBrowser.Crumb.Slider' 3)) 'and end at the selection'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'breadcrumbs.png')) 'a picture of the breadcrumbs'
    Check (Invoke-Element (Find-In $script:hwnd 'ComponentBrowser.Crumb.UIFigure')) 'a crumb clicked'
    Check ($null -eq (Find-In $script:hwnd 'ComponentBrowser.Crumb.Slider' 1)) 'it selects the figure, whose crumbs end there'
    Check (Invoke-Element (Find-In $script:hwnd 'ComponentBrowser.Expand')) 'the expander'
    Check (Row-Shown 'Slider') 'the tree is back'

    # --- the app's own row -----------------------------------------------------------------------------
    Check (Focus-Row 'W4App') 'the app''s row'
    Check (Post-Key $VK_APPS) 'the menu key'
    $rename = Find-Menu 'Menu.Rename'
    Check ($null -ne $rename -and -not $rename.Current.IsEnabled) 'the app''s row is not renamed'
    Check ($null -ne (Find-Menu 'Menu.VisualizeTabOrder')) 'it has the tab order'
    Check ($null -eq (Find-Menu 'Menu.Delete' 1)) 'and no Delete'
    Check (Post-Key $VK_ESCAPE) 'Escape closes the menu'

    # --- the Code View ---------------------------------------------------------------------------------
    $codeSwitch = Pattern (Find-In $script:hwnd 'CodeView.Switch') ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $codeSwitch) 'the Code View switch'
    $codeSwitch.Toggle()
    Start-Sleep -Milliseconds 1200
    Check (Focus-Row 'Slider') 'Slider has the keyboard in the Code View'
    Check (Post-Key $VK_APPS) 'the menu key'
    Check ($null -ne (Find-Menu 'Menu.InsertatCursor')) 'the menu has Insert at Cursor'
    Check ($null -eq (Find-Menu 'Menu.Cut' 1)) 'and no Cut'
    Check ($null -eq (Find-Menu 'Menu.Rename' 1)) 'and no Rename'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'code.png')) 'a picture in the Code View'
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
