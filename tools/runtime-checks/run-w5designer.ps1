<#
.SYNOPSIS
    Stage W5's window check (App Designer window plan): the clipboard on the canvas - Copy, Paste,
    Cut and a cut's paste, undone - and a paste into another app, from the quick access bar, in the
    JGraph application, driven through UI Automation patterns only (no synthesized input).

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, and a second one renamed W4Other, and starts
    JGraph.Application.exe with -r "appdesigner('<app>'); appdesigner('<other>')". Checks: with the
    figure selected, Cut and Copy are disabled and Paste too (nothing copied); with A selected in the
    component browser, Cut and Copy are enabled; Copy enables Paste; Paste makes A_2, 20 pixels
    along and down from A, which Save writes; Cut takes A_2 away, and its paste puts it back where
    it was under its name; Undo takes the paste back; in the other app's tab, Paste puts A there at
    its place, which Save writes. Pictures go into <WorkRoot>.

    What takes a mouse or keys - Ctrl+C, Ctrl+X, Ctrl+V and Ctrl+D, a Ctrl-drag, Ctrl+A, the canvas's
    menu and its Paste at the pointer - is left to AppDesignerClipboardTests, which drive the same
    gestures and commands without a window.

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
public static class W5Native {
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
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$failures = New-Object System.Collections.Generic.List[string]

& dotnet build (Join-Path $repo 'src\JGraph.Application\JGraph.Application.csproj') -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the application did not build" }
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"

# W4's app (w4\W4App.m, a plain-text app JGraph's writer made: D, A, L, B and an edit field), and a
# copy of it as the class W4Other for the paste into another app.
$app = Join-Path $WorkRoot 'W4App.m'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Destination $app -Force
$other = Join-Path $WorkRoot 'W4Other.m'
[System.IO.File]::WriteAllText($other, [System.IO.File]::ReadAllText($app).Replace('W4App', 'W4Other'))

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
        foreach ($entry in [W5Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

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
    Start-Sleep -Milliseconds 500
    return $true
}

function Enabled([string] $id) {
    $element = Find-In $script:hwnd $id 3
    return ($null -ne $element -and $element.Current.IsEnabled)
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W5Native+RECT
    [void] [W5Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W5Native]::PrintWindow($hwnd, $hdc, 2)
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

function Listed([string] $name) {
    $browser = Find-In $script:hwnd 'ComponentBrowser'
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    return ($null -ne $browser -and $null -ne $browser.FindFirst($Tree::Subtree, $condition))
}

function Save-App([string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

# A component's Position as the saved file's layout section writes it.
function Saved-Position([string] $file, [string] $name) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($file), "name='$name'>[\s\S]*?<Position>\[([\d\. ]+)\]")
    if (-not $m.Success) { return $null }
    return @($m.Groups[1].Value.Trim() -split '\s+' | ForEach-Object { [double] $_ })
}

function Same([object[]] $a, [object[]] $b) { return ($null -ne $a -and $null -ne $b -and ($a -join ',') -eq ($b -join ',')) }

$forward = $app.Replace('\', '/')
$otherForward = $other.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward'); appdesigner('$otherForward')`"") -PassThru
$null = $p.Handle
$script:appId = $p.Id
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the two apps ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # The first app's tab.
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'W4App.m')))
    $firstTab = Pattern ($A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $firstTab) "the W4App.m tab"
    if ($firstTab) { $firstTab.Select() }
    Start-Sleep -Milliseconds 800

    # --- nothing to copy, nothing to paste -------------------------------------------------------------
    $cut = 'appDesignerToolstrip.qab.cutButton'
    $copy = 'appDesignerToolstrip.qab.copyButton'
    $paste = 'appDesignerToolstrip.qab.pasteButton'
    $undo = 'appDesignerToolstrip.qab.undoButton'
    Check (-not (Enabled $copy)) 'with the figure selected, Copy is disabled'
    Check (-not (Enabled $cut)) 'Cut is disabled'
    Check (-not (Enabled $paste)) 'Paste is disabled: nothing copied'

    # --- copy and paste ------------------------------------------------------------------------------
    Check (Select-Component 'A') "A selected in the component browser"
    Check (Enabled $copy) 'with A selected, Copy is enabled'
    Check (Enabled $cut) 'Cut is enabled'
    Check (Invoke-Element (Find-In $script:hwnd $copy)) 'Copy'
    Check (Enabled $paste) 'Paste is enabled now'
    Check (Invoke-Element (Find-In $script:hwnd $paste)) 'Paste'
    Check (Listed 'A_2') 'the copy is A_2'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'pasted.png')) 'a picture after the paste'
    Check (Save-App $app) 'Save writes the app'
    # (PowerShell's names ignore case: $A is the automation element class.)
    $placeA = Saved-Position $app 'A'
    $placeA2 = Saved-Position $app 'A_2'
    Check ($null -ne $placeA -and (Same $placeA2 @(($placeA[0] + 20), ($placeA[1] - 20), $placeA[2], $placeA[3]))) "A_2 is 20 pixels along and down from A (A $($placeA -join ' '), A_2 $($placeA2 -join ' '))"

    # --- cut and its paste -------------------------------------------------------------------------------
    Check (Select-Component 'A_2') 'A_2 selected'
    Check (Invoke-Element (Find-In $script:hwnd $cut)) 'Cut'
    Check (-not (Listed 'A_2')) 'A_2 is gone'
    Check (Invoke-Element (Find-In $script:hwnd $paste)) "the cut's paste"
    Check (Listed 'A_2') 'A_2 is back under its name'
    Check (Save-App $app) 'Save writes it again'
    Check (Same (Saved-Position $app 'A_2') $placeA2) "where it was (got $((Saved-Position $app 'A_2') -join ' '))"
    Check (Invoke-Element (Find-In $script:hwnd $undo)) 'Undo'
    Check (-not (Listed 'A_2')) 'the paste undone'

    # --- into the other app ------------------------------------------------------------------------------
    Check (Select-Component 'B') 'B selected'
    Check (Invoke-Element (Find-In $script:hwnd $copy)) 'Copy B'
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'W4Other.m')))
    $otherTab = Pattern ($A::FromHandle($script:hwnd).FindFirst($Tree::Subtree, $condition)) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Check ($null -ne $otherTab) 'the W4Other.m tab'
    if ($otherTab) { $otherTab.Select() }
    Start-Sleep -Milliseconds 800
    Check (Enabled $paste) "Paste is enabled in the other app"
    Check (Invoke-Element (Find-In $script:hwnd $paste)) 'Paste into W4Other'
    Check (Listed 'B_2') "the copy is B_2 there (B is W4Other's own)"
    Check (Save-App $other) 'Save writes W4Other'
    $placeB = Saved-Position $app 'B'
    Check (Same (Saved-Position $other 'B_2') $placeB) "into its figure at B's place (got $((Saved-Position $other 'B_2') -join ' '))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'other-app.png')) 'a picture of the other app'
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
