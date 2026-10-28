<#
.SYNOPSIS
    Stage W1's window check (App Designer window plan): the toolstrip control showing R2025b's App
    Designer toolstrip as stage W0 recorded it, in the sample host toolstrip-sample, driven through UI
    Automation patterns only (no synthesized input).

.DESCRIPTION
    selftest  ToolstripSample.exe -selftest, in the light and the dark theme: the checks that need
              WPF's real layout (the CANVAS tab fitted at nine widths, Undo with the document focused
              sent to the window, key tips, contexts, a button group, the spinner's limits).
    window    The sample at 1280 pixels: tabs selected (SelectionItem), App Details invoked (Invoke),
              Grid toggled (Toggle), the SHARE drop-down and Save's arrow expanded (ExpandCollapse) and
              an item of each popup invoked, the grid interval set (Value), Code View's tabs, the
              window narrowed (Transform) until sections collapse and a collapsed section's popup
              opened, the toolstrip minimized and a tab opened over the document, and pictures of
              each; then the dark theme's picture.

    Every action is checked in the sample's log (key<TAB>value lines). Pictures are PrintWindow
    captures of the sample's own windows (a popup is a window of its own) into <WorkRoot>.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Configuration = 'Debug',
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W1Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    // Every visible top-level window of a process with its title: the main window and its popups.
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
$project = Join-Path $PSScriptRoot 'toolstrip-sample\ToolstripSample.csproj'
& dotnet build $project -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the sample did not build" }
$exe = Join-Path $PSScriptRoot "toolstrip-sample\bin\$Configuration\net8.0-windows\ToolstripSample.exe"

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [string] $name) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [W1Native]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# An element by automation id in any window of the process: the main window or a popup.
function Find-InProcess([int] $processId, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W1Native]::Windows([uint32] $processId)) {
            # A popup closing while it is read is gone, not an error.
            try {
                $root = $A::FromHandle($entry.Key)
                $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
                if ($found) { return $found }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

# The popup window (an untitled window of the process) holding the element of that id.
function Popup-Of([int] $processId, [string] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    foreach ($entry in [W1Native]::Windows([uint32] $processId)) {
        if ($entry.Value -ne '') { continue }
        try {
            $root = $A::FromHandle($entry.Key)
            if ($root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)) { return $root }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    return $null
}

function Pattern($element, $pattern) {
    if (-not $element) { return $null }
    $found = $null
    if ($element.TryGetCurrentPattern($pattern, [ref] $found)) { return $found }
    return $null
}

function Wait-Log([string] $log, [string] $text, [int] $seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        if (Test-Path $log) {
            # Shared, as the sample writes it.
            $stream = New-Object System.IO.FileStream($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try { if ((New-Object System.IO.StreamReader($stream)).ReadToEnd().Contains($text)) { return $true } }
            finally { $stream.Dispose() }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Save-Picture($element, [string] $path) {
    $hwnd = [IntPtr] $element.Current.NativeWindowHandle
    $rect = New-Object W1Native+RECT
    [void] [W1Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W1Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Select-Tab($window, [string] $tag) {
    $tab = Find-InProcess $script:appId $tag
    $pattern = Pattern $tab ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Select()
    return Wait-Log $script:log "tab`t$tag"
}

function Expand-ById([string] $id) {
    $pattern = Pattern (Find-InProcess $script:appId $id) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Expand()
    return $true
}

function Invoke-ById([string] $id) {
    $pattern = Pattern (Find-InProcess $script:appId $id) ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Invoke()
    return $true
}

# --- selftest --------------------------------------------------------------------------------------
foreach ($theme in @('light', 'dark')) {
    Write-Host "== selftest ($theme)"
    $log = Join-Path $WorkRoot "selftest-$theme.log"
    $arguments = @('-log', $log, '-selftest') + $(if ($theme -eq 'dark') { @('-dark') } else { @() })
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $p.Id -Force; Check $false "the $theme self-test ended in time" }
    else {
        Check ($p.ExitCode -eq 0) "the $theme self-test passed (exit $($p.ExitCode))"
        Get-Content $log | Where-Object { $_ -match "`tFAIL" } | ForEach-Object { Write-Host "    $_" }
        Get-Content $log | Where-Object { $_ -match '^fit' } | ForEach-Object { Write-Host "    $_" }
    }
}

# --- window ----------------------------------------------------------------------------------------
Write-Host "== window"
$script:log = Join-Path $WorkRoot 'window.log'
$p = Start-Process -FilePath $exe -ArgumentList @('-log', $script:log, '-width', '1280') -PassThru
$null = $p.Handle
$script:appId = $p.Id
try {
    $window = Find-Window $p.Id 'Toolstrip sample'
    Check ($null -ne $window) "a window titled 'Toolstrip sample'"
    Check (Wait-Log $script:log "ready`t1") 'the sample is ready'
    Start-Sleep -Milliseconds 500
    Check (Save-Picture $window (Join-Path $WorkRoot 'designer.png')) 'a picture of the DESIGNER tab'

    $tabs = Find-InProcess $p.Id 'Toolstrip.Tabs'
    $names = @()
    if ($tabs) {
        $condition = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
        $names = @($tabs.FindAll([System.Windows.Automation.TreeScope]::Children, $condition) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name })
    }
    Check (($names -join ',') -eq 'DESIGNER,CANVAS,VIEW') "Design View shows DESIGNER, CANVAS, VIEW (got $($names -join ','))"
    $qab = Find-InProcess $p.Id 'Toolstrip.QuickAccessBar'
    Check ($qab -and $qab.Current.Name -eq 'Quick Access Toolbar' -and $qab.Current.ControlType -eq [System.Windows.Automation.ControlType]::ToolBar) 'the quick access bar is a toolbar named Quick Access Toolbar'

    Check (Invoke-ById 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details invoked'
    Check (Wait-Log $script:log "command`tAD_APP_DETAILS`twindow") 'its command ran in the window'
    $details = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.share.appDetailsButton'
    Check ($details.Current.Name -eq 'App Details' -and $details.Current.HelpText -eq 'Specify details about your app') "named 'App Details' with R2025b's description (got '$($details.Current.Name)', '$($details.Current.HelpText)')"

    Check (Expand-ById 'appDesignerToolstrip.designerTab.share.shareAllOptions') 'SHARE expanded'
    $item = Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.share.compileDesktop'
    Check ($null -ne $item -and $item.Current.Name -eq 'Standalone Desktop App') "its popup has 'Standalone Desktop App'"
    $menu = Popup-Of $p.Id 'appDesignerToolstrip.designerTab.share.compileDesktop'
    if ($menu) { Check (Save-Picture $menu (Join-Path $WorkRoot 'share-menu.png')) 'a picture of the SHARE popup' }
    Check (Invoke-ById 'appDesignerToolstrip.designerTab.share.compileDesktop') 'Standalone Desktop App invoked'
    Check (Wait-Log $script:log "invoked`tappDesignerToolstrip.designerTab.share.compileDesktop`tAD_SHARE_DESKTOP") 'it was raised with its action'
    Start-Sleep -Milliseconds 300
    $state = (Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.designerTab.share.shareAllOptions') ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Current.ExpandCollapseState
    Check ($state -eq 'Collapsed') "SHARE collapsed again after the choice (got $state)"

    Check (Expand-ById 'appDesignerToolstrip.designerTab.file.saveSplitButton.arrow') "Save's arrow expanded"
    Check (Invoke-ById 'AD_SAVEAS') 'Save As... invoked from its popup'
    Check (Wait-Log $script:log "command`tAD_SAVEAS`twindow") 'its command ran in the window'

    Check (Select-Tab $window 'appDesignerToolstrip.layoutTab') 'CANVAS selected'
    Start-Sleep -Milliseconds 400
    Check (Save-Picture $window (Join-Path $WorkRoot 'canvas-wide.png')) 'a picture of the CANVAS tab'
    Check (Select-Tab $window 'appDesignerToolstrip.designviewTab') 'VIEW selected'
    $grid = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.view.showGrid') ([System.Windows.Automation.TogglePattern]::Pattern)
    if ($grid) { $grid.Toggle() }
    Check (Wait-Log $script:log "invoked`tappDesignerToolstrip.layoutTab.view.showGrid`tAD_DESIGN_GRID_SHOW`tTrue") 'Grid toggled on'
    $interval = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.layoutTab.view.intervalSpinner') ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($interval) { $interval.SetValue('30') }
    (Find-InProcess $p.Id 'Document').SetFocus()
    Check (Wait-Log $script:log "invoked`tappDesignerToolstrip.layoutTab.view.intervalSpinner`tAD_DESIGN_GRID_INTERVAL`t30") 'the interval set to 30'
    Start-Sleep -Milliseconds 300
    Check (Save-Picture $window (Join-Path $WorkRoot 'view.png')) 'a picture of the VIEW tab'

    Check (Invoke-ById 'CodeView') 'Code View'
    Check (Wait-Log $script:log "tab`tappDesignerToolstrip.editorTab") 'EDITOR selected when CANVAS and VIEW went'
    Start-Sleep -Milliseconds 400
    Check (Save-Picture $window (Join-Path $WorkRoot 'editor.png')) 'a picture of the EDITOR tab'
    Check (Invoke-ById 'Debugging') 'debugging on (the sample swaps Run for Continue and shows Step In and Step Out)'
    Start-Sleep -Milliseconds 400
    $continue = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.run.continue' 3
    $stepIn = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.run.stepin' 3
    Check ($continue -and -not $continue.Current.IsOffscreen -and $stepIn -and -not $stepIn.Current.IsOffscreen) 'Continue and Step In show'
    $run = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.run.run' 1
    Check (-not $run -or $run.Current.IsOffscreen) 'Run is gone'
    Check (Save-Picture $window (Join-Path $WorkRoot 'editor-debugging.png')) 'a picture of the EDITOR tab while debugging'
    Check (Invoke-ById 'Debugging') 'debugging off'
    Check (Invoke-ById 'DesignView') 'Design View again'

    # Narrow the window until CANVAS's sections collapse.
    Check (Select-Tab $window 'appDesignerToolstrip.layoutTab') 'CANVAS selected again'
    $transform = Pattern $window ([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Resize(560, 420)
    Start-Sleep -Milliseconds 800
    $runDrop = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.run.dropDown' 3
    Check ($runDrop -and -not $runDrop.Current.IsOffscreen) 'at 560 pixels RUN has collapsed to its drop-down'
    Check (Save-Picture $window (Join-Path $WorkRoot 'canvas-narrow.png')) 'a picture of the narrow CANVAS tab'
    Check (Expand-ById 'appDesignerToolstrip.editorTab.run.dropDown') "RUN's drop-down expanded"
    Start-Sleep -Milliseconds 400
    $popup = Popup-Of $p.Id 'appDesignerToolstrip.editorTab.run.run'
    Check ($null -ne $popup) "RUN's controls are in its popup"
    if ($popup) { Check (Save-Picture $popup (Join-Path $WorkRoot 'run-popup.png')) "a picture of RUN's popup" }
    Check (Invoke-ById 'appDesignerToolstrip.editorTab.run.run') 'Run invoked from the popup'
    Check (Wait-Log $script:log "command`tAD_RUN`twindow") "Run's command ran in the window"
    $transform.Resize(1280, 420)
    Start-Sleep -Milliseconds 800
    $runDrop = Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.run.dropDown' 1
    Check (-not $runDrop -or $runDrop.Current.IsOffscreen) 'widened again, RUN is expanded'

    # Minimize, open a tab over the document, restore.
    Check (Invoke-ById 'Toolstrip.MinimizeButton') 'Minimize Toolstrip'
    Start-Sleep -Milliseconds 400
    $minimize = Find-InProcess $p.Id 'Toolstrip.MinimizeButton'
    Check ($minimize.Current.Name -eq 'Restore Toolstrip') "the button now reads 'Restore Toolstrip'"
    Check (Save-Picture $window (Join-Path $WorkRoot 'minimized.png')) 'a picture of the minimized toolstrip'
    Check (Select-Tab $window 'appDesignerToolstrip.designerTab') 'DESIGNER selected while minimized'
    Start-Sleep -Milliseconds 500
    $overlay = Popup-Of $p.Id 'appDesignerToolstrip.designerTab.file.newButton'
    Check ($null -ne $overlay) "DESIGNER's controls opened over the document"
    if ($overlay) { Check (Save-Picture $overlay (Join-Path $WorkRoot 'minimized-overlay.png')) 'a picture of the overlay' }
    Check (Invoke-ById 'appDesignerToolstrip.designerTab.file.newButton') 'New invoked from the overlay'
    Check (Wait-Log $script:log "command`tOPEN_STARTSCREEN_DIALOG`twindow") "New's command ran in the window"
    Check (Invoke-ById 'Toolstrip.MinimizeButton') 'Restore Toolstrip'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host "== dark"
$darkLog = Join-Path $WorkRoot 'dark.log'
$p = Start-Process -FilePath $exe -ArgumentList @('-log', $darkLog, '-width', '1280', '-dark') -PassThru
$null = $p.Handle
try {
    $window = Find-Window $p.Id 'Toolstrip sample'
    Check (Wait-Log $darkLog "ready`t1") 'the dark sample is ready'
    Start-Sleep -Milliseconds 500
    Check (Save-Picture $window (Join-Path $WorkRoot 'designer-dark.png')) 'a picture in the dark theme'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
