<#
.SYNOPSIS
    Stage W11's window check (App Designer window plan): the debugger in App Designer's Code View - a breakpoint set by dbstop shown
    in the code view, Run pausing in the app's startup function, the RUN section's Continue, Step, Step In and Step Out taking Run's
    place, Step moving the paused line, the shell's prompt turning K>>, Continue finishing the run, and the BREAKPOINTS and ERROR
    HANDLING groups in Run's drop-down - in the JGraph application.

.DESCRIPTION
    Writes W11App.m into <WorkRoot>\app: w4\W4App.m renamed, with a startupFcn of three lines registered in its run configuration.
    Beside it, debug_w11.m sets a breakpoint with dbstop in W11App at the startup function's first line and opens the app in App
    Designer. One launch: -r names debug_w11.m; the designer window is found; the Code View is switched to and its editor's status
    read (the breakpoint's line); Run is invoked and the status line waited for ('Paused in W11App.startupFcn at line N.'); the
    execution controls read (Run gone, Continue, Step In and Step Out there and enabled, Stop enabled); the editor's status says the
    paused line; the shell's prompt reads K>>; Step moves the paused line by one; Continue ends the pause and the app's window appears;
    the Run drop-down is opened and its Clear All Breakpoints item invoked (the editor's status loses the breakpoint), its Pause on
    Errors item toggled on and off, and, the caret put on the startup function's first line through Go To, its Set/Clear item invoked.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back after; the application is ended with Stop-Process.

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
public static class W11Native {
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

# The app: W4App renamed W11App, with a startupFcn of three statements, registered in the run configuration.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Raw
$source = $source.Replace('W4App', 'W11App')
# Without button D's property line, which App Designer generates from the layout: a save writes it back, and every line below
# moves down one - which is what the breakpoints must follow.
$source = $source -replace "        D          matlab\.ui\.control\.Button?
", ''
if ($source.Contains('D          matlab.ui.control.Button')) { throw "button D's property line was not taken out" }
# The methods block as a hand writes it, tighter than App Designer's (no blank line, no comment): a save writes the class as the
# designer writes it, and the startup function's lines move - which is what the breakpoints must follow.
$startup = "    methods (Access = private)`r`n        function startupFcn(app)`r`n            x = 1;`r`n            y = x + 1;`r`n            app.L.Text = sprintf('%d', y);`r`n        end`r`n    end`r`n`r`n"
$withStartup = $source -replace "(    end\r?\n)(\r?\n)(end\r?\n\r?\n%\[appendix\])", "`$1`$2$startup`$3"
if ($withStartup -eq $source) { throw 'the startup function was not placed' }
$runConfiguration = "%---`r`n%[app:runConfiguration]`r`n%{`r`n<?xml version='1.0' encoding='UTF-8'?>`r`n<RunConfiguration>`r`n    <StartupFcn>startupFcn</StartupFcn>`r`n</RunConfiguration>`r`n%}`r`n`r`n%---`r`n%[app:appDetails]"
$withRun = $withStartup -replace "%---\r?\n%\[app:appDetails\]", $runConfiguration
if ($withRun -eq $withStartup) { throw 'the run configuration was not placed' }
$appFolder = Join-Path $WorkRoot 'app'
New-Item -ItemType Directory -Force $appFolder | Out-Null
$app = Join-Path $appFolder 'W11App.m'
[System.IO.File]::WriteAllText($app, $withRun)
$lines = $withRun -split "\r?\n"
$startLine = 0
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -eq 'x = 1;') { $startLine = $i + 1; break } }
if ($startLine -eq 0) { throw 'the startup line was not found' }
$debugScript = Join-Path $appFolder 'debug_w11.m'
[System.IO.File]::WriteAllText($debugScript, "dbstop in W11App at $startLine`nappdesigner('$($app.Replace('\', '/'))');`n")

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
        foreach ($entry in [W11Native]::Windows([uint32] $processId)) {
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

function Find-InProcess([int] $processId, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W11Native]::Windows([uint32] $processId)) {
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

function Set-Value($element, [string] $value) {
    $pattern = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.SetValue($value)
    Start-Sleep -Milliseconds 300
    return $true
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value) { return $value.Current.Value }
    return $element.Current.Name
}

function Status-Of($element) {
    if (-not $element) { return $null }
    return $element.Current.ItemStatus
}

function Wait-Text([IntPtr] $hwnd, [string] $id, [string] $expected, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $text = $null
    do {
        $text = Text-Of (Find-In $hwnd $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Wait-Status([IntPtr] $hwnd, [string] $id, [string] $expected, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $text = $null
    do {
        $text = Status-Of (Find-In $hwnd $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W11Native+RECT
    [void] [W11Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W11Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Enabled([IntPtr] $hwnd, [string] $id, [int] $seconds = 5) {
    $element = Find-In $hwnd $id $seconds
    return ($null -ne $element -and $element.Current.IsEnabled)
}

function Start-JGraph([string] $statement) {
    $info = New-Object System.Diagnostics.ProcessStartInfo($exe)
    $info.UseShellExecute = $false
    $info.Arguments = "-r `"$statement`""
    return [System.Diagnostics.Process]::Start($info)
}

function Open-Run([IntPtr] $hwnd) {
    $arrow = Find-In $hwnd 'appDesignerToolstrip.editorTab.run.run.arrow' 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 700
    return $true
}

function Close-Run([IntPtr] $hwnd) {
    $arrow = Find-In $hwnd 'appDesignerToolstrip.editorTab.run.run.arrow' 2
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($expand) { try { $expand.Collapse() } catch { } }
    Start-Sleep -Milliseconds 400
}

function Save-App([IntPtr] $hwnd, [string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    $save = Find-In $hwnd 'appDesignerToolstrip.qab.saveButton' 5
    if (-not $save -or -not $save.Current.IsEnabled) { Write-Host '       (Save is not enabled: the app is not dirty)'; return $false }
    try { [void] (Invoke-Element $save) } catch { Write-Host "       (Save: $($_.Exception.Message))"; return $false }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

# The Go To dialog: the editor's caret put on a line (W9a's dialog).
function Go-To-Line([IntPtr] $hwnd, [int] $processId, [int] $line) {
    $arrow = Find-In $hwnd 'appDesignerToolstrip.editorTab.navigate.gotoButton' 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { Write-Host '       (Go To: the button has no expand pattern)'; return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    $item = Find-InProcess $processId 'appDesignerToolstrip.editorTab.navigate.gotoButton.gotoLine' 5
    if (-not $item) { Write-Host '       (Go To: no gotoLine item)'; return $false }
    if (-not (Invoke-Element $item)) { Write-Host '       (Go To: the gotoLine item has no invoke pattern)'; return $false }
    $dialog = Find-Window $processId { param($t) $t -eq 'Go To' } 8
    if (-not $dialog) { Write-Host '       (Go To: no dialog)'; return $false }
    $box = Find-In $dialog.Key 'GoToLine.Line' 5
    if (-not (Set-Value $box "$line")) { Write-Host '       (Go To: the line box took no value)'; return $false }
    return (Invoke-Element (Find-In $dialog.Key 'GoToLine.OK' 3))
}

$p = Start-JGraph $debugScript.Replace('\', '/')
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "the -r file set the breakpoint and opened the app in App Designer ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- the breakpoint dbstop set at the file's line shows in the Code View, through the line map, then follows the save ----
    $switch = Pattern (Find-In $hwnd 'CodeView.Switch' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $switch) 'the Code View switch is there'
    if ($switch) { $switch.Toggle(); Start-Sleep -Milliseconds 1200 }
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' 'Breakpoints ' 10
    Check ($editorStatus -and $editorStatus.Contains('Breakpoints ')) "the code view's margin shows the breakpoint dbstop set at the file's line $startLine, mapped to the class as the designer writes it ($editorStatus)"
    Check (Save-App $hwnd $app) 'Save wrote the class as the designer writes it'
    Start-Sleep -Milliseconds 1200
    $saved = Get-Content -LiteralPath $app
    $line = 0
    for ($i = 0; $i -lt $saved.Count; $i++) { if ($saved[$i].Trim() -eq 'x = 1;') { $line = $i + 1; break } }
    Check ($line -eq $startLine + 1) "the save wrote button D's property line back and the startup function's first line moved down one (from $startLine to $line)"
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' "Breakpoints $line" 10
    Check ($editorStatus -and $editorStatus.Contains("Breakpoints $line")) "the breakpoint followed its function across the save ($editorStatus)"
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.run') 'Run is enabled'
    Check ($null -eq (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.continue' 2)) 'Continue is out of the RUN section before the run'
    Check ($null -eq (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.stepin' 2)) 'Step In is out of the RUN section before the run'

    # --- Run pauses in the startup function ----------------------------------------------------------------------------------
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.run' 5)) 'Run invoked'
    $status = Wait-Text $hwnd 'AppDesigner.Status' "Paused in W11App.startupFcn at line $line." 30
    Check ($status -eq "Paused in W11App.startupFcn at line $line.") "the status says where the run paused ($status)"
    Start-Sleep -Milliseconds 800
    Check ($null -eq (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.run' 2)) 'Run left the RUN section while paused'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.continue') 'Continue took its place, enabled'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.stepin') 'Step In is shown and enabled while paused'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.stepout') 'Step Out is shown and enabled while paused'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.step') 'Step is enabled while paused'
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.stop') 'Stop is enabled while paused'
    Check (-not (Enabled $hwnd 'appDesignerToolstrip.editorTab.edit.addCallbackButton' 2)) 'the Callback insertion rests while paused'
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' "Paused at line $line" 10
    Check ($editorStatus -and $editorStatus.Contains("Paused at line $line")) "the code view marks the paused line ($editorStatus)"
    $prompt = Text-Of (Find-InProcess $p.Id 'Console.Prompt' 5)
    Check ($prompt -eq 'K>>') "the shell's prompt is K>> ($prompt)"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'paused.png')) 'a picture of the designer paused'

    # --- Step, then Continue ---------------------------------------------------------------------------------------------------
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.step' 5)) 'Step invoked'
    $next = $line + 1
    $status = Wait-Text $hwnd 'AppDesigner.Status' "Paused in W11App.startupFcn at line $next." 20
    Check ($status -eq "Paused in W11App.startupFcn at line $next.") "Step moved the pause to the next line ($status)"
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' "Paused at line $next" 10
    Check ($editorStatus -and $editorStatus.Contains("Paused at line $next")) "the code view marks the new line ($editorStatus)"
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.continue' 5)) 'Continue invoked'
    $status = Wait-Text $hwnd 'AppDesigner.Status' 'Running W11App.' 30
    Check ($status -eq 'Running W11App.') "the run finished after Continue ($status)"
    $appWindow = Find-Window $p.Id { param($t) $t -eq 'MATLAB App' } 20
    Check ($null -ne $appWindow) "the app's window is up"
    Start-Sleep -Milliseconds 800
    Check (Enabled $hwnd 'appDesignerToolstrip.editorTab.run.run') 'Run is back in the RUN section'
    $prompt = Text-Of (Find-InProcess $p.Id 'Console.Prompt' 5)
    Check ($prompt -eq '>>') "the shell's prompt is >> again ($prompt)"

    # --- BREAKPOINTS and ERROR HANDLING in Run's drop-down ----------------------------------------------------------------
    Check (Open-Run $hwnd) "Run's drop-down opened"
    Check (Invoke-Element (Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.breakpoint.clear_all_breakpoints' 5)) 'Clear All Breakpoints invoked'
    Close-Run $hwnd
    Start-Sleep -Milliseconds 600
    $editorStatus = Status-Of (Find-In $hwnd 'CodeView.Editor' 5)
    Check ($editorStatus -and -not $editorStatus.Contains('Breakpoints')) "the code view's breakpoint is gone ($editorStatus)"
    Check (Open-Run $hwnd) "Run's drop-down opened again"
    $pauseOnError = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.breakpoint.pause_on_error' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $pauseOnError -and $pauseOnError.Current.ToggleState -eq 'Off') 'Pause on Errors is a check item, off'
    if ($pauseOnError) { $pauseOnError.Toggle(); Start-Sleep -Milliseconds 500 }
    Close-Run $hwnd
    Check (Open-Run $hwnd) "Run's drop-down opened a third time"
    $pauseOnError = Pattern (Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.breakpoint.pause_on_error' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $pauseOnError -and $pauseOnError.Current.ToggleState -eq 'On') 'Pause on Errors stays checked when the drop-down opens again'
    if ($pauseOnError) { $pauseOnError.Toggle(); Start-Sleep -Milliseconds 500 }
    Close-Run $hwnd
    $editorTab = Pattern (Find-In $hwnd 'appDesignerToolstrip.editorTab' 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($editorTab) { $editorTab.Select(); Start-Sleep -Milliseconds 500 }
    Check (Go-To-Line $hwnd $p.Id $line) "the caret put on the startup function's first line through Go To"
    Check (Open-Run $hwnd) "Run's drop-down opened a fourth time"
    Check (Invoke-Element (Find-InProcess $p.Id 'appDesignerToolstrip.editorTab.breakpoint.toggle_breakpoint' 5)) 'Set/Clear invoked at the caret'
    Close-Run $hwnd
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' "Breakpoints $line" 10
    Check ($editorStatus -and $editorStatus.Contains("Breakpoints $line")) "Set/Clear set a breakpoint on the caret's line ($editorStatus)"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'breakpoints.png')) 'a picture of the designer with the breakpoint set'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
