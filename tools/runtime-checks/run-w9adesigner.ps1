<#
.SYNOPSIS
    Stage W9a's window check (App Designer window plan): the Code View's Find and Replace strip, Find Next and Match case, Whole word and a regular expression, Replace All passing over generated code, Go To's line and the bookmarks, in the JGraph application.

.DESCRIPTION
    Copies the .mlapp w8\\W8bApp.mlapp into <WorkRoot> and starts JGraph.Application.exe with -r "appdesigner('<app>')". Everything is set through UI Automation patterns
    (the strip's boxes by ValuePattern, its buttons by Invoke and Toggle, the toolstrip's items through their popups); Esc is a key posted to the designer's own window once it is
    checked to be in front. Where the caret stands is read from the editor's ItemStatus, and what Replace All wrote from the saved file.

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
public static class W9aNative {
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

$bin = Join-Path $WorkRoot 'W9aApp.mlapp'
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
        foreach ($entry in [W9aNative]::Windows([uint32] $processId)) {
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
    $rect = New-Object W9aNative+RECT
    [void] [W9aNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W9aNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# A key posted to the designer's own window: its key-down and key-up messages.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101; $VK_ESCAPE = 0x1B
function Post-Key([int] $vk) {
    if ([W9aNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W9aNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W9aNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 700
    return $true
}

# A menu item of a popup, which opens in a window of its own, by its automation id.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W9aNative]::Windows([uint32] $p.Id)) {
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

# A split button's or drop-down's popup opened by its arrow.
function Open-Drop([string] $id) {
    $arrow = Find-In $script:hwnd $id 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Close-Drop([string] $id) {
    $arrow = Find-In $script:hwnd $id 2
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($expand) { try { $expand.Collapse() } catch { } }
    Start-Sleep -Milliseconds 400
}

# What the editor says of its caret and bookmarks (its ItemStatus: "Ln 13, Col 1; Bookmarks 13").
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

function Result-Says {
    $label = Find-In $script:hwnd 'Find.Result' 3
    if (-not $label) { return '' }
    return $label.Current.Name
}

function Message-Says {
    $label = Find-In $script:hwnd 'Find.Message' 2
    if (-not $label) { return '' }
    return $label.Current.Name
}

function Strip-Shown { return ($null -ne (Find-In $script:hwnd 'Find.Text' 2)) }

function Code-Of([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
    try {
        $reader = New-Object System.IO.StreamReader($zip.GetEntry('matlab/document.xml').Open(), [System.Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

# The class's text as the Code View shows it, from the file's CDATA.
function Class-Of([string] $file) {
    $m = [regex]::Match((Code-Of $file), '(?s)<!\[CDATA\[(.*?)\]\]>')
    return $m.Groups[1].Value
}

# The Go To dialog driven: the text set, what the dialog says of it, and OK pressed when asked.
function Go-To([string] $text, [bool] $ok) {
    if (-not (Open-Drop 'appDesignerToolstrip.editorTab.navigate.gotoButton')) { return $null }
    $item = Find-Menu 'appDesignerToolstrip.editorTab.navigate.gotoButton.gotoLine' 5
    if (-not $item -or -not (Invoke-Element $item)) { return $null }
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Go To' } 8
    if (-not $dialog) { return $null }
    $box = Find-In $dialog.Key 'GoToLine.Line' 5
    if (-not (Set-Text $box $text)) { return $null }
    $okButton = Find-In $dialog.Key 'GoToLine.OK' 3
    $error = Find-In $dialog.Key 'GoToLine.Error' 1
    $said = @{ okEnabled = $okButton.Current.IsEnabled; error = $(if ($error) { $error.Current.Name } else { '' }) }
    if ($ok) { [void] (Invoke-Element $okButton) }
    else { [void] (Invoke-Element (Find-In $dialog.Key 'GoToLine.Cancel' 3)) }
    return $said
}

$special = '\s!()\[\]{}\-+*=.?#$/:;%''&<>^@\\~,"|`'
$forward = $bin.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500
    $class = Class-Of $bin

    # --- the strip -----------------------------------------------------------------------------------------
    Check (-not (Strip-Shown)) 'no strip before Find'
    Check (To-Code) 'the Code View'
    Check (Show-Tab 'appDesignerToolstrip.editorTab') 'the EDITOR tab'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.editorTab.navigate.findButton' 5)) 'Find pressed'
    Check (Strip-Shown) 'the strip opens'
    Check ($null -eq (Find-In $script:hwnd 'Find.ReplaceText' 1)) 'without its replace row'
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) 'sprintf') 'sprintf typed'
    Check ((Result-Says) -eq '1 / 1') "one found ($(Result-Says))"
    Check ((Status-Line) -eq 13) "the caret on its line ($(Editor-Status))"
    $apps = ([regex]::Matches($class, '(?i)app')).Count
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) 'app') 'app typed'
    Check ((Result-Says) -match "^\d+ / $apps$") "every app counted, without case ($(Result-Says) of $apps)"
    $before = [int] ((Result-Says) -split ' / ')[0]
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.findButton.arrow') 'the Find drop-down'
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.editorTab.navigate.findButton.findNextListItem' 5)) 'Find Next'
    $after = [int] ((Result-Says) -split ' / ')[0]
    Check ($after -eq $before + 1 -or ($before -eq $apps -and $after -eq 1)) "the next occurrence ($before to $after)"
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.findButton.arrow') 'the drop-down again'
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.editorTab.navigate.findButton.findPrevListItem' 5)) 'Find Previous'
    Check (((Result-Says) -split ' / ')[0] -eq "$before") 'back to the one before'
    Check (Invoke-Element (Find-In $script:hwnd 'Find.Next' 3)) 'the strip''s own next'
    Check (((Result-Says) -split ' / ')[0] -eq "$after") 'goes on'

    # Match case and Whole word, counted as R2025b counts them.
    $appsCase = ([regex]::Matches($class, 'app')).Count
    Check (Toggle-Element (Find-In $script:hwnd 'Find.MatchCase' 3)) 'Match case on'
    Check ((Result-Says) -match "^[\d?]+ / $appsCase$") "only app in that case ($(Result-Says) of $appsCase)"
    $appsWhole = ([regex]::Matches($class, "(?<=^|[$special])app(?=$|[$special])", 'Multiline')).Count
    Check (Toggle-Element (Find-In $script:hwnd 'Find.WholeWord' 3)) 'Whole word on'
    Check ((Result-Says) -match "^[\d?]+ / $appsWhole$") "only whole words ($(Result-Says) of $appsWhole)"
    Check (Toggle-Element (Find-In $script:hwnd 'Find.WholeWord' 3)) 'Whole word off'
    Check (Toggle-Element (Find-In $script:hwnd 'Find.MatchCase' 3)) 'Match case off'

    # A regular expression, refused and taken.
    Check (Toggle-Element (Find-In $script:hwnd 'Find.Regex' 3)) 'Regular expression on'
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) '(') 'an unclosed group typed'
    Check ((Message-Says) -eq 'Invalid regular expression') "refused in R2025b's words ($(Message-Says))"
    Check ((Result-Says) -eq '0 / 0') 'and nothing counted'
    $names = ([regex]::Matches($class, 'app\.(\w+)')).Count
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) 'app\.(\w+)') 'app\.(\w+) typed'
    Check ((Message-Says) -eq '') 'no message'
    Check ((Result-Says) -match "^\d+ / $names$") "every app.name counted ($(Result-Says) of $names)"
    Check (Toggle-Element (Find-In $script:hwnd 'Find.Regex' 3)) 'Regular expression off'

    # Replace All passes over the generated lines: UIFigure.Name stands in the startup function and in createComponents.
    Check (Toggle-Element (Find-In $script:hwnd 'Find.ToggleReplace' 3)) 'the replace row opened'
    Check ($null -ne (Find-In $script:hwnd 'Find.ReplaceText' 3)) 'its box there'
    Check (Set-Text (Find-In $script:hwnd 'Find.Text' 3) 'UIFigure.Name') 'UIFigure.Name typed'
    Check ((Result-Says) -match '^\d+ / 2$') "found twice ($(Result-Says))"
    Check (Set-Text (Find-In $script:hwnd 'Find.ReplaceText' 3) 'UIFigure.Tag') 'UIFigure.Tag to replace with'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'find-replace.png')) 'a picture of the strip'
    Check (Invoke-Element (Find-In $script:hwnd 'Find.ReplaceAll' 3)) 'Replace All'
    Check ((Message-Says) -eq 'Replaced 1 occurrences.') "one replaced, the generated one passed over ($(Message-Says))"
    Check ((Result-Says) -match '^[\d?]+ / 1$') "one left ($(Result-Says))"
    Start-Sleep -Milliseconds 500
    Check (Save-App $bin) 'Save writes it'
    $saved = Class-Of $bin
    Check ($saved.Contains("app.UIFigure.Tag = sprintf('n is %d', n);")) 'the startup function''s line replaced'
    Check ($saved.Contains("app.UIFigure.Name = 'MATLAB App';")) 'createComponents'' line kept'

    # Esc closes the strip from its box.
    $box = Find-In $script:hwnd 'Find.Text' 3
    try { $box.SetFocus() } catch { }
    Start-Sleep -Milliseconds 300
    Check ([W9aNative]::GetForegroundWindow() -eq $script:hwnd) 'the designer in front'
    Check (Post-Key $VK_ESCAPE) 'Esc posted'
    Check (-not (Strip-Shown)) 'the strip closed'

    # --- Go To ---------------------------------------------------------------------------------------------
    $said = Go-To 'abc' $false
    Check ($null -ne $said) 'the Go To dialog'
    Check ($said -and -not $said.okEnabled) 'OK disabled for abc'
    Check ($said -and $said.error -eq 'The value entered is not valid.') "R2025b's words ($($said.error))"
    $said = Go-To '3.0' $false
    Check ($said -and $said.okEnabled) 'OK takes 3.0'
    $said = Go-To '13' $true
    Check ($null -ne $said) 'line 13 asked for'
    Check ((Status-Line) -eq 13) "the caret on line 13 ($(Editor-Status))"
    $said = Go-To '9999' $true
    Check ((Status-Line) -eq ($class -split "`n").Count) "a line past the end held to the last ($(Editor-Status))"

    # --- bookmarks -----------------------------------------------------------------------------------------
    [void] (Go-To '13' $true)
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.arrow') 'the Bookmark drop-down'
    $next = Find-Menu 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.bookmarkNextListItem' 5
    Check ($null -ne $next -and -not $next.Current.IsEnabled) 'Next disabled with no bookmark'
    Close-Drop 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.arrow'
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.editorTab.navigate.bookmarkButton' 5)) 'Bookmark pressed'
    Check ((Editor-Status).Contains('Bookmarks 13')) "line 13 marked ($(Editor-Status))"
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'bookmark.png')) 'a picture of the margin'
    [void] (Go-To '3' $true)
    Check ((Status-Line) -eq 3) 'the caret on line 3'
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.arrow') 'the drop-down'
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.bookmarkNextListItem' 5)) 'Next'
    Check ((Status-Line) -eq 13) "the caret on the bookmark ($(Editor-Status))"
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.arrow') 'the drop-down'
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.bookmarkPrevListItem' 5)) 'Previous'
    Check ((Status-Line) -eq 13) 'round to the same one'
    Check (Open-Drop 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.arrow') 'the drop-down'
    Check (Invoke-Element (Find-Menu 'appDesignerToolstrip.editorTab.navigate.bookmarkButton.bookmarkSetClearListItem' 5)) 'Set/Clear'
    Check (-not (Editor-Status).Contains('Bookmarks')) "the mark cleared ($(Editor-Status))"
    [void] (Go-To '2' $true)
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.editorTab.navigate.bookmarkButton' 5)) 'Bookmark on an empty line'
    Check (-not (Editor-Status).Contains('Bookmarks')) 'takes none'
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
