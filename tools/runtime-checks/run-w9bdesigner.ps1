<#
.SYNOPSIS
    Stage W9b's window check (App Designer window plan): the EDITOR tab's CODE section - Comment, Uncomment, Wrap Comments, Smart Indent, Increase and Decrease Indent - over the Code View, in the JGraph application.

.DESCRIPTION
    Copies the .mlapp w8\\W8bApp.mlapp into <WorkRoot> and starts JGraph.Application.exe with -r "appdesigner('<app>')". The caret is put on a line by the Go To dialog, each button is pressed
    through UI Automation's Invoke, the app saved and the class read back from the file. A long comment line is written into the startup function by the Find and Replace strip's Replace All
    (W9a) for Wrap Comments to break. With the caret on a generated line the buttons are disabled.

    The user's settings file (%APPDATA%\\JGraph\\settings.json) is moved aside before and put back after; the application is ended with Stop-Process.

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
public static class W9bNative {
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

$bin = Join-Path $WorkRoot 'W9bApp.mlapp'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w8\\W8bApp.mlapp') -Destination $bin -Force

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
        foreach ($entry in [W9bNative]::Windows([uint32] $processId)) {
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

function Toggle-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Toggle()
    Start-Sleep -Milliseconds 600
    return $true
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W9bNative+RECT
    [void] [W9bNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W9bNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W9bNative]::Windows([uint32] $p.Id)) {
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

function Set-Text($element, [string] $text) {
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $false }
    $value.SetValue($text)
    Start-Sleep -Milliseconds 600
    return $true
}

function Save-App([string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

function In-Code {
    $switch = Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)
    return ($null -ne $switch -and $switch.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

function To-Code {
    (Pattern (Find-In $script:hwnd 'CodeView.Switch' 3) ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 1000
    return (In-Code)
}

function Show-Tab([string] $id) {
    $tab = Pattern (Find-In $script:hwnd $id 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $tab) { return $false }
    $tab.Select()
    Start-Sleep -Milliseconds 500
    return $true
}

function Open-Drop([string] $id) {
    $arrow = Find-In $script:hwnd $id 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Editor-Status {
    $editor = Find-In $script:hwnd 'CodeView.Editor' 3
    if (-not $editor) { return '' }
    return $editor.Current.ItemStatus
}

function Status-Line {
    $m = [regex]::Match((Editor-Status), 'Ln (\d+)')
    if ($m.Success) { return [int] $m.Groups[1].Value }
    return -1
}

function Code-Of([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try {
        $reader = New-Object System.IO.StreamReader($zip.GetEntry('matlab/document.xml').Open(), [System.Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

function Class-Lines([string] $file) {
    $m = [regex]::Match((Code-Of $file), '(?s)<!\[CDATA\[(.*?)\]\]>')
    return ($m.Groups[1].Value -replace "`r", '') -split "`n"
}

# The caret put on a line by the Go To dialog.
function Go-To([int] $line) {
    if (-not (Open-Drop 'appDesignerToolstrip.editorTab.navigate.gotoButton')) { return $false }
    $item = Find-Menu 'appDesignerToolstrip.editorTab.navigate.gotoButton.gotoLine' 5
    if (-not $item -or -not (Invoke-Element $item)) { return $false }
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Go To' } 8
    if (-not $dialog) { return $false }
    if (-not (Set-Text (Find-In $dialog.Key 'GoToLine.Line' 5) "$line")) { return $false }
    [void] (Invoke-Element (Find-In $dialog.Key 'GoToLine.OK' 3))
    return ((Status-Line) -eq $line)
}

function Press([string] $name) { return (Invoke-Element (Find-In $script:hwnd "appDesignerToolstrip.editorTab.edit.$name" 5)) }

function Enabled([string] $name) {
    $button = Find-In $script:hwnd "appDesignerToolstrip.editorTab.edit.$name" 5
    return ($null -ne $button -and $button.Current.IsEnabled)
}

function Line13([string] $file) { return (Class-Lines $file)[12] }

# A save, and the thirteenth line of the class as the file holds it.
function Saved-Line13 { if (-not (Save-App $bin)) { return $null }; return (Line13 $bin) }

$forward = $bin.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500
    $statement = "app.UIFigure.Name = sprintf('n is %d', n);"
    Check ((Line13 $bin) -eq ('            ' + $statement)) 'the startup function''s line as the file holds it'

    Check (To-Code) 'the Code View'
    Check (Show-Tab 'appDesignerToolstrip.editorTab') 'the EDITOR tab'
    Check (Go-To 1) 'the caret on the class line'
    foreach ($name in 'commentButton', 'uncommentButton', 'wrapcommentButton', 'smartIndentButton', 'moreIndentButton', 'lessIndentButton') {
        Check (-not (Enabled $name)) "$name disabled on a generated line"
    }

    Check (Go-To 13) 'the caret on the startup function''s line'
    Check (Enabled 'commentButton') 'Comment enabled on the user''s line'

    # --- Comment and Uncomment ------------------------------------------------------------------------------
    Check (Press 'commentButton') 'Comment pressed'
    Check ((Saved-Line13) -eq ('            % ' + $statement)) 'the sign at the line''s own indent, read from the file'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'comment.png')) 'a picture of the CODE section'
    Check (Press 'uncommentButton') 'Uncomment pressed'
    Check ((Saved-Line13) -eq ('            ' + $statement)) 'the sign taken off'

    # --- Increase, Decrease, Smart Indent -------------------------------------------------------------------
    Check (Press 'moreIndentButton') 'Increase Indent pressed'
    Check ((Saved-Line13) -eq ('                ' + $statement)) 'four spaces more'
    Check (Press 'lessIndentButton') 'Decrease Indent pressed'
    Check ((Saved-Line13) -eq ('            ' + $statement)) 'four spaces less'
    Check (Press 'lessIndentButton') 'Decrease Indent again'
    Check ((Saved-Line13) -eq ('        ' + $statement)) 'and again'
    Check (Press 'smartIndentButton') 'Smart Indent pressed'
    Check ((Saved-Line13) -eq ('            ' + $statement)) 'back where the function''s body stands'

    # --- Wrap Comments ------------------------------------------------------------------------------------------
    $comment = '% the quick brown fox jumps over the lazy dog again and again and again until the line is long enough'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.editorTab.navigate.findButton' 5)) 'Find pressed'
    Check ($null -ne (Find-In $script:hwnd 'Find.Text' 3)) 'the strip'
    Check (Toggle-Element (Find-In $script:hwnd 'Find.ToggleReplace' 3)) 'its replace row'
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) $statement) 'the statement looked for'
    Check (Set-Text (Find-In $script:hwnd 'Find.ReplaceText' 3) $comment) 'a long comment to replace it with'
    Check (Invoke-Element (Find-In $script:hwnd 'Find.ReplaceAll' 3)) 'Replace All'
    Check (Invoke-Element (Find-In $script:hwnd 'Find.Close' 3)) 'the strip closed'
    Check ((Saved-Line13) -eq ('            ' + $comment)) 'the comment in the file, past the width'
    Check (Go-To 13) 'the caret on it'
    Check (Enabled 'wrapcommentButton') 'Wrap Comments enabled'
    Check (Press 'wrapcommentButton') 'pressed'
    Check (Save-App $bin) 'saved'
    $lines = Class-Lines $bin
    Check ($lines[12].StartsWith('            % ') -and $lines[13].StartsWith('            % ')) 'two comment lines where one was'
    Check ($lines[12].Length -le 75 -and $lines[13].Length -le 75) "neither past the width ($($lines[12].Length), $($lines[13].Length))"
    $joined = ($lines[12].Substring(14) + ' ' + $lines[13].Substring(14))
    Check ($joined -eq $comment.Substring(2)) 'together the comment as it was'
    Check ($lines[14].Trim() -eq 'end') 'the function''s end follows'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'wrap.png')) 'a picture of the wrapped comment'
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
