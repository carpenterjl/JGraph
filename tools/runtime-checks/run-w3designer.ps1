<#
.SYNOPSIS
    Stage W3's window check (App Designer window plan): the Start Page, new apps, JGraph's example
    apps, the recent files and the shell's entry points, in the JGraph application, driven through
    UI Automation patterns only (no synthesized input).

.DESCRIPTION
    Starts JGraph.Application.exe with -r "appdesigner" and checks: the designer window comes up on
    the Start Page (title "App Designer", only the DESIGNER tab); the Apps tiles enabled and the
    Custom UI Components tiles disabled until W15; "No Recent Files"; the examples hidden until
    "Show examples"; Blank App opens app1.mlapp (R2025b's title and tab name, the Start Page gone,
    the Design View's tabs); DESIGNER > New shows the "App Designer Start Page" dialog, whose
    3-Panel tile opens app2.mlapp and closes it; an example opened from the dialog is copied into
    the check's folder as .mlapp files and opened; the Open drop-down lists it under Recent Files and
    the settings keep it by R2025b's name; closing every tab with its close button brings the Start
    Page back with the example as its recent file, which opens again; the shell's Design App button
    and File > New Script > "MATLAB app (.mlapp)" (a new app in the designer, app3.mlapp). Pictures
    of each step go into <WorkRoot>.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back
    after, so the check starts from R2025b's factory settings with no recent files; the examples
    are copied into <WorkRoot>\examples (JGRAPH_APP_EXAMPLES), never into Documents; the
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
public static class W3Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
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
$examples = Join-Path $WorkRoot 'examples'
if (Test-Path -LiteralPath $examples) { Remove-Item -LiteralPath $examples -Recurse -Force }
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$failures = New-Object System.Collections.Generic.List[string]

& dotnet build (Join-Path $repo 'src\JGraph.Application\JGraph.Application.csproj') -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the application did not build" }
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"

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
        foreach ($entry in [W3Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# The first element with the id under one window (its handle), waiting for it.
function Find-In([IntPtr] $hwnd, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
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

function Find-Named([IntPtr] $hwnd, [string] $name, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
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
    return $true
}

function Designer-Title() {
    foreach ($entry in [W3Native]::Windows([uint32] $script:appId)) {
        if ($entry.Value -eq 'App Designer' -or $entry.Value.StartsWith('App Designer - ')) { return $entry.Value }
    }
    return ''
}

function Wait-Title([string] $wanted, [int] $seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if ((Designer-Title) -eq $wanted) { return $true }; Start-Sleep -Milliseconds 200 } while ((Get-Date) -lt $deadline)
    return $false
}

function Visible-Tabs() {
    $tabs = Find-In $script:hwnd 'Toolstrip.Tabs'
    if (-not $tabs) { return '' }
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    return (@($tabs.FindAll($Tree::Children, $condition) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) -join ',')
}

function Shown([IntPtr] $hwnd, [string] $id) {
    $element = Find-In $hwnd $id 2
    return ($null -ne $element -and -not $element.Current.IsOffscreen)
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W3Native+RECT
    [void] [W3Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W3Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# DESIGNER > New: the Start Page dialog, found by its title.
function Open-StartDialog() {
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.file.newButton'))
    return Find-Window $script:appId { param($t) $t -eq 'App Designer Start Page' } 10
}

# The open tabs' close buttons, front to back.
function Close-AllTabs() {
    for ($i = 0; $i -lt 10; $i++) {
        $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'AppTab.CloseButton')
        $close = $A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)
        if (-not $close) { return }
        [void] (Invoke-Element $close)
        Start-Sleep -Milliseconds 700
    }
}

$previousExamples = $env:JGRAPH_APP_EXAMPLES
$env:JGRAPH_APP_EXAMPLES = $examples
$p = Start-Process -FilePath $exe -ArgumentList @('-r', '"appdesigner"') -PassThru
$env:JGRAPH_APP_EXAMPLES = $previousExamples
$null = $p.Handle
$script:appId = $p.Id
try {
    $designer = Find-Window $p.Id { param($t) $t -eq 'App Designer' }
    Check ($null -ne $designer) 'appdesigner with no app brings the designer window up'
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    # UI Automation reads a WPF window's children once: wait for its components first.
    Start-Sleep -Milliseconds 1500

    # --- the Start Page in place -----------------------------------------------------------------
    Check (Shown $script:hwnd 'StartPage') 'the window shows the Start Page'
    Check ((Visible-Tabs) -eq 'DESIGNER') "no app: only the DESIGNER tab (got $(Visible-Tabs))"
    foreach ($id in @('AD_NEW', 'AD_NEW_2COL_APP', 'AD_NEW_3COL_APP')) {
        $tile = Find-In $script:hwnd "StartPage.Tile.$id"
        Check ($tile -and $tile.Current.IsEnabled) "the $id tile is enabled ($($tile.Current.Name))"
    }
    $blank = Find-In $script:hwnd 'StartPage.Tile.AD_NEW'
    Check ($blank.Current.Name -eq 'Blank App' -and $blank.Current.HelpText -eq 'Create new blank app') "Blank App has R2025b's words"
    $component = Find-In $script:hwnd 'StartPage.Tile.AD_NEW_COMP_APP'
    Check ($component -and -not $component.Current.IsEnabled -and $component.Current.HelpText.Contains('W15')) 'Blank Component is disabled until W15'
    Check (Shown $script:hwnd 'OpenPanel.NoRecentFiles') "the open menu says 'No Recent Files'"
    Check ($null -ne (Find-In $script:hwnd 'StartPage.Link.apps-designer')) 'the links bar has Getting Started'
    Check (-not (Shown $script:hwnd 'StartPage.Example.TableExample')) 'the examples start hidden'
    Check (Invoke-Element (Find-In $script:hwnd 'StartPage.Examples.0')) "'Show examples' clicked"
    Start-Sleep -Milliseconds 500
    Check (Shown $script:hwnd 'StartPage.Example.TableExample') 'the examples show'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'start-page.png')) 'a picture of the Start Page'

    # --- Blank App ---------------------------------------------------------------------------------
    Check (Invoke-Element $blank) 'Blank App clicked'
    Check (Wait-Title 'App Designer - app1.mlapp') "the title is R2025b's for a new app (got '$(Designer-Title)')"
    Start-Sleep -Milliseconds 800
    Check (-not (Shown $script:hwnd 'StartPage')) 'the Start Page gives way to the app'
    Check ((Visible-Tabs) -eq 'DESIGNER,CANVAS,VIEW') "the Design View's tabs (got $(Visible-Tabs))"
    Check ($null -ne (Find-Named $script:hwnd 'app1.mlapp')) "the tab is named 'app1.mlapp'"
    $save = Find-In $script:hwnd 'appDesignerToolstrip.designerTab.file.saveSplitButton'
    Check ($save -and $save.Current.IsEnabled) 'Save is enabled for the new app (it asks for a file)'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'new-app.png')) 'a picture of the new app'

    # --- DESIGNER > New: the dialog --------------------------------------------------------------
    $dialog = Open-StartDialog
    Check ($null -ne $dialog) "DESIGNER > New shows the 'App Designer Start Page' dialog"
    if ($dialog) {
        Start-Sleep -Milliseconds 800
        Check (Save-Picture $dialog.Key (Join-Path $WorkRoot 'start-dialog.png')) 'a picture of the dialog'
        Check (Invoke-Element (Find-In $dialog.Key 'StartPage.Tile.AD_NEW_3COL_APP')) '3-Panel App with Auto-Reflow clicked'
        Check (Wait-Title 'App Designer - app2.mlapp') "the dialog closes and app2.mlapp opens (got '$(Designer-Title)')"
        Check (-not [bool] ([W3Native]::Windows([uint32] $p.Id) | Where-Object { $_.Value -eq 'App Designer Start Page' })) 'the dialog is gone'
    }

    # --- an example from the dialog -----------------------------------------------------------------
    $dialog = Open-StartDialog
    if ($dialog) {
        Start-Sleep -Milliseconds 800
        [void] (Invoke-Element (Find-In $dialog.Key 'StartPage.Examples.0'))
        Start-Sleep -Milliseconds 400
        Check (Invoke-Element (Find-In $dialog.Key 'StartPage.Example.TableExample')) "'Create a Table' clicked"
    }
    $tableApp = Join-Path $examples 'TableExample\TableApp.mlapp'
    Check (Wait-Title "App Designer - $tableApp") "the example opens from its copy (got '$(Designer-Title)')"
    Check (Test-Path -LiteralPath $tableApp) 'the example was copied as an .mlapp into the examples folder'
    Start-Sleep -Milliseconds 800
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'example.png')) 'a picture of the example on the canvas'

    # --- the recent files ------------------------------------------------------------------------
    $arrow = Pattern (Find-In $script:hwnd 'appDesignerToolstrip.designerTab.file.openButton.arrow') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($arrow) { $arrow.Expand() }
    $recent = $null
    $deadline = (Get-Date).AddSeconds(5)
    while (-not $recent -and (Get-Date) -lt $deadline) {
        foreach ($entry in [W3Native]::Windows([uint32] $p.Id)) {
            $recent = Find-In $entry.Key 'appDesignerToolstrip.designerTab.file.openButton.menu.recentFiles.item.0' 0
            if ($recent) { break }
        }
        if (-not $recent) { Start-Sleep -Milliseconds 250 }
    }
    Check ($recent -and $recent.Current.Name -eq 'TableApp.mlapp') "Open's drop-down lists the example under Recent Files (got '$($recent.Current.Name)')"
    if ($arrow) { try { $arrow.Collapse() } catch { } }
    $saved = if (Test-Path -LiteralPath $settingsFile) { Get-Content -LiteralPath $settingsFile -Raw } else { '' }
    Check ($saved.Contains('matlab.appdesigner.history.FileList') -and $saved.Contains('TableApp.mlapp')) 'the settings keep it as matlab.appdesigner.history.FileList'

    # --- closing every app: the Start Page again ----------------------------------------------------
    Close-AllTabs
    Check (Wait-Title 'App Designer') "with every app closed the title is 'App Designer' (got '$(Designer-Title)')"
    Start-Sleep -Milliseconds 600
    Check (Shown $script:hwnd 'StartPage') 'the Start Page is back'
    $first = Find-In $script:hwnd 'OpenPanel.RecentFile.0'
    Check ($first -and $first.Current.Name -eq 'TableApp') "the example is the recent file (got '$($first.Current.Name)')"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'start-page-recent.png')) 'a picture of the Start Page with its recent file'
    Check (Invoke-Element $first) 'the recent file clicked'
    Check (Wait-Title "App Designer - $tableApp") 'it opens again'

    # --- the shell ------------------------------------------------------------------------------------
    $shell = Find-Window $p.Id { param($t) $t -ne 'App Designer' -and -not $t.StartsWith('App Designer') -and $t.Length -gt 0 } 10
    Check ($null -ne $shell) "the shell's window ($($shell.Value))"
    if ($shell) {
        Check (Invoke-Element (Find-In $shell.Key 'DesignAppButton')) "the shell's Design App button"
        $file = Pattern (Find-Named $shell.Key 'File') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($file) { $file.Expand() }
        Start-Sleep -Milliseconds 400
        $newScript = Pattern (Find-Named $shell.Key 'New Script') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($newScript) { $newScript.Expand() }
        Start-Sleep -Milliseconds 400
        $appItem = $null
        foreach ($entry in [W3Native]::Windows([uint32] $p.Id)) {
            $appItem = Find-Named $entry.Key 'MATLAB app (.mlapp)' 1
            if ($appItem) { break }
        }
        Check (Invoke-Element $appItem) "File > New Script > 'MATLAB app (.mlapp)'"
        Check (Wait-Title 'App Designer - app3.mlapp') "it makes app3.mlapp in the designer (got '$(Designer-Title)')"
    }
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'from-shell.png')) 'a picture of the app made from the shell'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
