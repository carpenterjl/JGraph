<#
.SYNOPSIS
    Stage W10's window check (App Designer window plan): the Code Analyzer in App Designer's Code View - the analyzer's messages
    and App Designer's coding alert marked in the code view, listed on the message bar, counted in the editor's status, the
    Fix button rewriting char(10) to newline and Save writing it to the file, the bar's square taking the caret to the next
    message - in the JGraph application.

.DESCRIPTION
    Writes W10App.m into <WorkRoot>\app: w4\W4App.m renamed, with a startupFcn registered in its run configuration whose
    lines raise the messages: plot(x) without an axes (the coding alert), y = char(10) (CHARTEN) and an unused z (NASGU).
    One launch: -r opens the app in App Designer; the designer window is found; the Code View is switched to and its editor's
    status read (Messages N); the message bar's status read (each message's id and span: NASGU, CHARTEN and the tip on plot);
    the bar's square read (warning); the caret put on the char(10) line through Go To and the bar's Fix button invoked; the
    bar read again (CHARTEN gone); Save invoked and the file read (newline where char(10) was); the square invoked and the
    editor's caret read (on a message's line).

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
public static class W10Native {
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

# The app: W4App renamed W10App, with a startupFcn whose lines raise the messages, registered in the run configuration.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Raw
$source = $source.Replace('W4App', 'W10App')
$startup = "    methods (Access = private)`r`n        function startupFcn(app)`r`n            x = 1:10;`r`n            plot(x)`r`n            y = char(10);`r`n            z = 1;`r`n            app.L.Text = ['ready' y];`r`n        end`r`n    end`r`n`r`n"
$withStartup = $source -replace "(    end\r?\n)(\r?\n)(end\r?\n\r?\n%\[appendix\])", "`$1`$2$startup`$3"
if ($withStartup -eq $source) { throw 'the startup function was not placed' }
$runConfiguration = "%---`r`n%[app:runConfiguration]`r`n%{`r`n<?xml version='1.0' encoding='UTF-8'?>`r`n<RunConfiguration>`r`n    <StartupFcn>startupFcn</StartupFcn>`r`n</RunConfiguration>`r`n%}`r`n`r`n%---`r`n%[app:appDetails]"
$withRun = $withStartup -replace "%---\r?\n%\[app:appDetails\]", $runConfiguration
if ($withRun -eq $withStartup) { throw 'the run configuration was not placed' }
$appFolder = Join-Path $WorkRoot 'app'
New-Item -ItemType Directory -Force $appFolder | Out-Null
$app = Join-Path $appFolder 'W10App.m'
[System.IO.File]::WriteAllText($app, $withRun)
$openScript = Join-Path $appFolder 'open_w10.m'
[System.IO.File]::WriteAllText($openScript, "appdesigner('$($app.Replace('\', '/'))');`n")

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
        foreach ($entry in [W10Native]::Windows([uint32] $processId)) {
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
        foreach ($entry in [W10Native]::Windows([uint32] $processId)) {
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
    $rect = New-Object W10Native+RECT
    [void] [W10Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W10Native]::PrintWindow($hwnd, $hdc, 2)
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

function Wait-StatusWithout([IntPtr] $hwnd, [string] $id, [string] $gone, [int] $seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $text = $null
    do {
        $text = Status-Of (Find-In $hwnd $id 2)
        if ($null -ne $text -and -not $text.Contains($gone)) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

$p = Start-JGraph $openScript.Replace('\', '/')
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "the -r file opened the app in App Designer ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- the messages and the coding alert in the Code View ----------------------------------------------------------------
    $switch = Pattern (Find-In $hwnd 'CodeView.Switch' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $switch) 'the Code View switch is there'
    if ($switch) { $switch.Toggle(); Start-Sleep -Milliseconds 1500 }
    $editorStatus = Wait-Status $hwnd 'CodeView.Editor' 'Messages ' 15
    Check ($editorStatus -and $editorStatus.Contains('Messages ')) "the code view's status counts the analyzer's messages ($editorStatus)"
    $strip = Wait-Status $hwnd 'CodeAnalyzer.Status' 'NASGU ' 10
    Check ($strip -and $strip.Contains('NASGU ')) "the message bar lists the unused variable z ($strip)"
    Check ($strip -and $strip.Contains('CHARTEN ')) 'the message bar lists char(10)'
    Check ($strip -and $strip.Contains('APPAXES ')) 'the message bar lists the coding alert on plot(x) without an axes'
    Check ($strip -and $strip.StartsWith('warning')) "the bar's square says warning: warnings and no error"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'messages.png')) 'a picture of the code view with the marks'

    # --- Fix: the caret on the char(10) line through Go To, then the bar's Fix button ------------------------------------
    $m = [regex]::Match([string] $strip, 'CHARTEN (\d+):')
    $chartenLine = 0
    if ($m.Success) { $chartenLine = [int] $m.Groups[1].Value }
    Check ($chartenLine -gt 0) "the bar says which line char(10) is on ($chartenLine)"
    $editorTab = Pattern (Find-In $hwnd 'appDesignerToolstrip.editorTab' 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($editorTab) { $editorTab.Select(); Start-Sleep -Milliseconds 500 }
    Check (Go-To-Line $hwnd $p.Id $chartenLine) "the caret put on the char(10) line through Go To"
    Start-Sleep -Milliseconds 600
    $fixEnabled = Enabled $hwnd 'CodeAnalyzer.Fix'
    Check $fixEnabled 'the Fix button is enabled with the caret on a message that has a fix'
    Check ($fixEnabled -and (Invoke-Element (Find-In $hwnd 'CodeAnalyzer.Fix' 5))) 'Fix invoked'
    $strip = Wait-StatusWithout $hwnd 'CodeAnalyzer.Status' 'CHARTEN' 10
    Check ($null -ne $strip -and -not $strip.Contains('CHARTEN')) "the char(10) message is gone after the fix ($strip)"
    Check ($strip -and $strip.Contains('NASGU ')) 'the unused variable is still listed'
    Check (Save-App $hwnd $app) 'Save wrote the app'
    Start-Sleep -Milliseconds 800
    $saved = Get-Content -LiteralPath $app -Raw
    Check ($saved.Contains('y = newline;')) 'the saved class has newline where char(10) was'
    Check (-not $saved.Contains('char(10)')) 'and no char(10) is left in it'

    # --- the square takes the caret to the next message -------------------------------------------------------------------
    Check (Invoke-Element (Find-In $hwnd 'CodeAnalyzer.Status' 5)) "the bar's square invoked"
    Start-Sleep -Milliseconds 500
    $editorStatus = Status-Of (Find-In $hwnd 'CodeView.Editor' 5)
    $caret = [regex]::Match([string] $editorStatus, 'Ln (\d+)')
    $caretLine = 0
    if ($caret.Success) { $caretLine = [int] $caret.Groups[1].Value }
    $strip = Status-Of (Find-In $hwnd 'CodeAnalyzer.Status' 5)
    $messageLines = @([regex]::Matches([string] $strip, ' (\d+):') | ForEach-Object { [int] $_.Groups[1].Value })
    Check ($caretLine -gt 0 -and $messageLines -contains $caretLine) "the caret is on a message's line after the square ($editorStatus; messages on $($messageLines -join ' '))"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'fixed.png')) 'a picture of the code view after the fix'
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
