<#
.SYNOPSIS
    Stage W2's window check (App Designer window plan): the designer window on R2025b's App Designer
    toolstrip, in the JGraph application, driven through UI Automation patterns only (no synthesized
    input).

.DESCRIPTION
    Starts JGraph.Application.exe with -r "appdesigner('<a copy of a fixture app>')" and checks:
    the title R2025b writes ("App Designer - <file>"); the Design View's tabs DESIGNER, CANVAS, VIEW
    and the quick access bar; Run alone in RUN's first column (Pause, Continue, Step In and Step Out
    out of it); actions JGraph runs enabled and those of later stages disabled with R2025b's words;
    Grid toggled and the grid interval set on the VIEW tab, kept as settings; the Code View's tabs
    DESIGNER, EDITOR, VIEW; a private function added from the Function split button's popup (the
    title gains "*") and undone from the quick access bar; the library's search box; the prompt when
    the app's file is written outside, answered No; pictures of each step into <WorkRoot>.

    The user's settings file (%APPDATA%\JGraph\settings.json) is copied before and put back after,
    since the toggles are settings; the application is ended with Stop-Process, so nothing else is
    saved.

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
public static class W2Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    // A message to one dialog's own handle (WM_COMMAND with a button's id): no input is synthesized.
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
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
$failures = New-Object System.Collections.Generic.List[string]

& dotnet build (Join-Path $repo 'src\JGraph.Application\JGraph.Application.csproj') -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the application did not build" }
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"

# The app: a copy, so the check's edits and the outside write touch nothing in the repo.
$app = Join-Path $WorkRoot 'W2App.mlapp'
Copy-Item -LiteralPath (Join-Path $repo 'tests\JGraph.Tests\MatlabParity\fixtures\helpers\U7MlApp.mlapp') -Destination $app -Force
$settingsFile = Join-Path $env:APPDATA 'JGraph\settings.json'
$settingsCopy = Join-Path $WorkRoot 'settings.before.json'
$hadSettings = Test-Path -LiteralPath $settingsFile
if ($hadSettings) { Copy-Item -LiteralPath $settingsFile -Destination $settingsCopy -Force }

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [scriptblock] $match) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [W2Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Find-InProcess([int] $processId, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W2Native]::Windows([uint32] $processId)) {
            try {
                $found = $A::FromHandle($entry.Key).FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
                if ($found) { return $found }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
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

function Invoke-ById([string] $id) {
    $pattern = Pattern (Find-InProcess $script:appId $id) ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Invoke()
    return $true
}

function Title() {
    foreach ($entry in [W2Native]::Windows([uint32] $script:appId)) {
        if ($entry.Value.StartsWith('App Designer -')) { return $entry.Value }
    }
    return ''
}

function Wait-Title([scriptblock] $match, [int] $seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $match (Title)) { return $true }; Start-Sleep -Milliseconds 200 } while ((Get-Date) -lt $deadline)
    return $false
}

function Visible-Tabs() {
    $tabs = Find-InProcess $script:appId 'Toolstrip.Tabs'
    if (-not $tabs) { return '' }
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    return (@($tabs.FindAll([System.Windows.Automation.TreeScope]::Children, $condition) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) -join ',')
}

function Shown([string] $id) {
    $element = Find-InProcess $script:appId $id 2
    return ($null -ne $element -and -not $element.Current.IsOffscreen)
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W2Native+RECT
    [void] [W2Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W2Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

$forward = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
$script:appId = $p.Id
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) 'the designer window came up'
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    # UI Automation reads a WPF window's children once: wait for its components first.
    Start-Sleep -Milliseconds 1500
    Check ((Title) -eq "App Designer - $app") "the title is R2025b's (got '$(Title)')"
    Check ((Visible-Tabs) -eq 'DESIGNER,CANVAS,VIEW') "the Design View shows DESIGNER, CANVAS, VIEW (got $(Visible-Tabs))"
    $qab = Find-InProcess $p.Id 'Toolstrip.QuickAccessBar'
    Check ($null -ne $qab) 'the quick access bar'
    Check (Shown 'appDesignerToolstrip.editorTab.run.run') 'Run shows'
    foreach ($gone in @('pause', 'continue', 'stepin', 'stepout')) {
        Check (-not (Shown "appDesignerToolstrip.editorTab.run.$gone")) "$gone is out of RUN's columns"
    }
    $save = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.file.saveSplitButton'
    Check ($save -and $save.Current.IsEnabled) 'Save is enabled'
    $details = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.share.appDetailsButton'
    Check ($details -and -not $details.Current.IsEnabled -and $details.Current.HelpText -eq 'Specify details about your app') "App Details is shown disabled with R2025b's words (W8)"
    $new = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.file.newButton'
    Check ($new -and $new.Current.IsEnabled) 'New is enabled (W3: the Start Page)'
    $share = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.share.shareAllOptions'
    Check ($share -and -not $share.Current.IsEnabled) 'Share is disabled (W12, W13)'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'designer.png')) 'a picture of the DESIGNER tab'

    $canvasTab = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab') ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $canvasTab.Select()
    Start-Sleep -Milliseconds 500
    $align = Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.align.left'
    Check ($align -and -not $align.Current.IsEnabled) 'Align Left is disabled (W4)'
    $grouping = Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.arrange.groupButton'
    Check ($grouping -and $grouping.Current.IsEnabled) 'Grouping is enabled'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'canvas.png')) 'a picture of the CANVAS tab'

    $viewTab = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.designviewTab') ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $viewTab.Select()
    Start-Sleep -Milliseconds 500
    $grid = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.view.showGrid') ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($grid -and $grid.Current.ToggleState -eq 'Off') 'Grid starts off (R2025b factory)'
    if ($grid) { $grid.Toggle() }
    Start-Sleep -Milliseconds 300
    Check ($grid -and $grid.Current.ToggleState -eq 'On') 'Grid toggled on'
    $interval = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.view.intervalSpinner') ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($interval) { $interval.SetValue('30') }
    (Find-InProcess $p.Id 'Palette.Search').SetFocus()
    Start-Sleep -Milliseconds 400
    Check ($interval -and $interval.Current.Value -eq '30') "the interval is 30 (got $($interval.Current.Value))"
    $resize = Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.view.size'
    Check ($resize -and -not $resize.Current.IsEnabled) 'Resizing Hints is disabled (W6)'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'view.png')) 'a picture of the VIEW tab with the grid'
    $saved = if (Test-Path -LiteralPath $settingsFile) { Get-Content -LiteralPath $settingsFile -Raw } else { '' }
    Check ($saved.Contains('matlab.appdesigner.designview.ShowGrid') -and $saved.Contains('matlab.appdesigner.designview.GridInterval')) 'the grid and its interval were kept as settings by their R2025b names'

    # The library's search box.
    $search = Pattern (Find-InProcess $p.Id 'Palette.Search') ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($search) { $search.SetValue('gauge') }
    Start-Sleep -Milliseconds 400
    Check ((Shown 'Palette.Gauge') -and -not (Shown 'Palette.Button')) "searching 'gauge' shows Gauge and hides Button"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'library-search.png')) 'a picture of the library searched'
    if ($search) { $search.SetValue('') }

    # The Code View.
    $codeSwitch = Pattern (Find-InProcess $p.Id 'CodeView.Switch') ([System.Windows.Automation.TogglePattern]::Pattern)
    if ($codeSwitch) { $codeSwitch.Toggle() }
    Start-Sleep -Milliseconds 800
    Check ((Visible-Tabs) -eq 'DESIGNER,EDITOR,VIEW') "the Code View shows DESIGNER, EDITOR, VIEW (got $(Visible-Tabs))"
    $editorTab = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab'
    Check ((Pattern $editorTab ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected) 'EDITOR is selected'
    $comment = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.edit.commentButton'
    Check ($comment -and -not $comment.Current.IsEnabled -and $comment.Current.Name -eq 'Comment') "Comment is shown, disabled (W9), named from R2025b's description"
    $arrow = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.edit.addFunctionSplitButton.arrow') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($arrow) { $arrow.Expand() }
    Start-Sleep -Milliseconds 400
    Check (Invoke-ById 'appDesignerToolstrip.editorTab.edit.addFunctionPrivateButton') 'Private Function chosen from the popup'
    Check (Wait-Title { param($t) $t.EndsWith('*') }) "the title gains '*' (got '$(Title)')"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'editor.png')) 'a picture of the EDITOR tab after the insertion'
    $undo = Find-InProcess $p.Id 'appDesignerToolstrip.qab.undoButton'
    Check ($undo -and $undo.Current.IsEnabled) 'Undo is enabled in the quick access bar'
    if ($undo -and $undo.Current.IsEnabled) { (Pattern $undo ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    Check (Wait-Title { param($t) -not $t.EndsWith('*') }) "the title loses '*' (got '$(Title)')"

    # A write from outside: App Designer asks to reload; No keeps the app as it is.
    $bytes = [System.IO.File]::ReadAllBytes($app)
    [System.IO.File]::WriteAllBytes($app, $bytes)
    $prompt = Find-Window $p.Id { param($t) $t -eq 'App Designer' }
    Check ($null -ne $prompt) 'a prompt came up for the outside write'
    if ($prompt) {
        $promptElement = $A::FromHandle($prompt.Key)
        # A Win32 message box: the managed client sees its controls as panes, so they are found by
        # name; a button's name may carry its '&'.
        $all = @($promptElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
        $texts = @($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
        $said = ($texts -join ' ')
        Check ($said.Contains('W2App.mlapp has been modified outside of App Designer.') -and $said.Contains('Reload the file?') -and -not $said.Contains('Unsaved changes')) "it says R2025b's words (got '$said')"
        Check (Save-Picture $prompt.Key (Join-Path $WorkRoot 'reload-prompt.png')) 'a picture of the prompt'
        $no = @($all | Where-Object { $_.Current.Name.Replace('&', '') -eq 'No' }) | Select-Object -First 1
        Check ($null -ne $no) 'it has a No button'
        # The managed client gives a Win32 button no Invoke: No is answered by the dialog's IDNO (7).
        [void] [W2Native]::PostMessage($prompt.Key, 0x0111, [IntPtr] 7, [IntPtr]::Zero)
        # One write is asked about once, however many changes the watch reports for it.
        $deadline = (Get-Date).AddSeconds(5); $open = $true
        while ($open -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
            $open = [bool] ([W2Native]::Windows([uint32] $p.Id) | Where-Object { $_.Value -eq 'App Designer' })
        }
        Start-Sleep -Milliseconds 1500
        $open = [bool] ([W2Native]::Windows([uint32] $p.Id) | Where-Object { $_.Value -eq 'App Designer' })
        Check (-not $open) 'No closed it, and no second prompt followed'
        Check ((Title) -eq "App Designer - $app") 'the app is still open as it was'
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    if ($hadSettings) { Copy-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
    elseif (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
