<#
.SYNOPSIS
    Stage W5b's window check (App Designer window plan): a component's text edited in place on the
    canvas - F2 on a label, a typed character on a button, Enter saving and Escape closing unsaved -
    in the JGraph application, each saved and read back from the file.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot>, with a slider and a switch added, and
    starts JGraph.Application.exe with -r "appdesigner('<app>')". Components are selected in the
    component browser and the canvas given the keyboard through UI Automation patterns; the editor's
    text is read and set through its ValuePattern. The keys that open and close the editor - F2, a
    typed Z, Tab, Enter and Escape - are window messages posted to the designer's own window handle,
    never input sent to whatever window is in front, and only once the designer's window is checked
    to be the foreground one.

    Checks: F2 with L selected opens the editor on L's text; Enter saves the text set, which Save
    writes; a Z typed with A selected opens it on Z; Escape closes an editor unsaved, so that Undo
    after the next save takes back only that one; the editor goes when it closes; F2 on the slider
    opens its minimum on the label drawn at its left end, Tab saves it and opens the maximum at its
    right end; F2 on the switch opens its first item. Pictures go into <WorkRoot>.

    A double click, a hyperlink's URL box and an image's file browser are left to
    AppDesignerInPlaceTests, which drive the same gestures without a window.

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
public static class W5bNative {
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

# W4's app (w4\W4App.m: buttons D, A and B, the label L and an edit field), with a slider S and a
# switch W added to its declarations and its layout.
$app = Join-Path $WorkRoot 'W4App.m'
$text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$text = $text.Replace("        D          matlab.ui.control.Button$nl",
    "        D          matlab.ui.control.Button$nl        S          matlab.ui.control.Slider$nl        W          matlab.ui.control.Switch$nl")
$text = $text.Replace("        </Children>$nl    </UIFigure>",
    ("            <Slider name='S'>$nl                <Position>[350 200 150 3]</Position>$nl            </Slider>$nl" +
     "            <Switch name='W'>$nl                <Position>[400 100 45 20]</Position>$nl            </Switch>$nl" +
     "        </Children>$nl    </UIFigure>"))
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
        foreach ($entry in [W5bNative]::Windows([uint32] $processId)) {
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
    Start-Sleep -Milliseconds 500
    return $true
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W5bNative+RECT
    [void] [W5bNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W5bNative]::PrintWindow($hwnd, $hdc, 2)
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
    return ([W5bNative]::GetForegroundWindow() -eq $script:hwnd)
}

# A key posted to the designer's own window: its key-down and key-up messages, or a typed character's.
$WM_KEYDOWN = 0x100; $WM_KEYUP = 0x101; $WM_CHAR = 0x102
function Post-Key([int] $vk) {
    if ([W5bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W5bNative]::PostMessage($script:hwnd, $WM_KEYDOWN, [IntPtr] $vk, [IntPtr] 1)
    [void] [W5bNative]::PostMessage($script:hwnd, $WM_KEYUP, [IntPtr] $vk, [IntPtr] ([int] 0xC0000001 -bor 0))
    Start-Sleep -Milliseconds 600
    return $true
}

function Post-Char([char] $c) {
    if ([W5bNative]::GetForegroundWindow() -ne $script:hwnd) { return $false }
    [void] [W5bNative]::PostMessage($script:hwnd, $WM_CHAR, [IntPtr] ([int] $c), [IntPtr] 1)
    Start-Sleep -Milliseconds 600
    return $true
}

function Editor { return Find-In $script:hwnd 'Canvas.InPlaceEditor' 3 }

function Editor-Text {
    $value = Pattern (Editor) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $null }
    return $value.Current.Value
}

function Set-EditorText([string] $text) {
    $value = Pattern (Editor) ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $value) { return $false }
    $value.SetValue($text)
    Start-Sleep -Milliseconds 300
    return $true
}

function Save-App([string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    [void] (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.saveButton'))
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

# A component's Text as the saved file's layout section writes it.
function Saved-Text([string] $file, [string] $name) {
    $m = [regex]::Match([System.IO.File]::ReadAllText($file), "name='$name'>[\s\S]*?<Text>'([^']*)'</Text>")
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value
}

$VK_RETURN = 0x0D; $VK_ESCAPE = 0x1B; $VK_F2 = 0x71; $VK_TAB = 0x09

function Editor-Box {
    $editor = Editor
    if (-not $editor) { return $null }
    return $editor.Current.BoundingRectangle
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

    # --- F2 on a label, Enter saves ----------------------------------------------------------------------
    Check (Select-Component 'L') 'L selected in the component browser'
    $front = Focus-Canvas
    Check $front 'the canvas has the keyboard, the designer in front'
    if (-not $front) { throw 'the designer is not the foreground window: no key is posted' }
    Check ($null -eq (Editor)) 'no editor before F2'
    Check (Post-Key $VK_F2) 'F2'
    Check ($null -ne (Editor)) 'F2 opens the in-place editor'
    Check ((Editor-Text) -eq 'L') "on L's text (got '$(Editor-Text)')"
    Check (Set-EditorText 'Hello') 'the editor set to Hello'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'editing.png')) 'a picture of the open editor'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check ($null -eq (Editor)) 'Enter closes the editor'
    Check (Save-App $app) 'Save writes the app'
    Check ((Saved-Text $app 'L') -eq 'Hello') "L's text is saved (got '$(Saved-Text $app 'L')')"

    # --- Escape closes unsaved ---------------------------------------------------------------------------
    Check (Select-Component 'A') 'A selected'
    Check (Focus-Canvas) 'the canvas has the keyboard again'
    Check (Post-Key $VK_F2) 'F2 on A'
    Check ((Editor-Text) -eq 'A') "the editor is on A's text (got '$(Editor-Text)')"
    Check (Set-EditorText 'nope') 'the editor set to nope'
    Check (Post-Key $VK_ESCAPE) 'Escape'
    Check ($null -eq (Editor)) 'Escape closes the editor'

    # --- a typed character -------------------------------------------------------------------------------
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Char 'Z') 'Z typed'
    Check ($null -ne (Editor)) 'a typed character opens the editor'
    Check ((Editor-Text) -eq 'Z') "on what was typed (got '$(Editor-Text)')"
    Check (Set-EditorText 'Zed') 'the editor set to Zed'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check (Save-App $app) 'Save writes it'
    Check ((Saved-Text $app 'A') -eq 'Zed') "A's text is Zed, not nope (got '$(Saved-Text $app 'A')')"

    # Escape saved nothing: one Undo takes A back to its own text.
    Check (Invoke-Element (Find-In $script:hwnd 'appDesignerToolstrip.qab.undoButton')) 'Undo'
    Check (Save-App $app) 'Save writes it again'
    Check ((Saved-Text $app 'A') -eq 'A') "A's text is back (got '$(Saved-Text $app 'A')')"
    Check ((Saved-Text $app 'L') -eq 'Hello') "L's stays (got '$(Saved-Text $app 'L')')"

    # --- a slider's limits, Tab from one to the other --------------------------------------------------------
    Check (Select-Component 'S') 'S selected'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_F2) 'F2 on S'
    Check ((Editor-Text) -eq '0') "the editor is on S's minimum (got '$(Editor-Text)')"
    $low = Editor-Box
    Check (Set-EditorText '10') 'the minimum set to 10'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'slider-minimum.png')) "a picture of the minimum's editor"
    Check (Post-Key $VK_TAB) 'Tab'
    Start-Sleep -Milliseconds 600
    Check ((Editor-Text) -eq '100') "Tab opens the maximum (got '$(Editor-Text)')"
    $high = Editor-Box
    Check ($null -ne $low -and $null -ne $high -and ($high.X - $low.X) -gt 100) "the maximum's editor is at the slider's other end (minimum at $($low.X), maximum at $($high.X))"
    Check (Post-Key $VK_ESCAPE) 'Escape'
    Check ($null -eq (Editor)) 'Escape closes it'
    Check (Save-App $app) 'Save writes it'
    Check ([System.IO.File]::ReadAllText($app) -match "name='S'>[\s\S]*?<Limits>\[10 100\]</Limits>") 'S''s limits are [10 100]'

    # --- a switch's item -----------------------------------------------------------------------------
    Check (Select-Component 'W') 'W selected'
    Check (Focus-Canvas) 'the canvas has the keyboard'
    Check (Post-Key $VK_F2) 'F2 on W'
    Check ((Editor-Text) -eq 'Off') "the editor is on W's first item (got '$(Editor-Text)')"
    Check (Set-EditorText 'Stop') 'the item set to Stop'
    Check (Post-Key $VK_RETURN) 'Enter'
    Check (Save-App $app) 'Save writes it'
    Check ([System.IO.File]::ReadAllText($app) -match "name='W'>[\s\S]*?<Items>\{'Stop', 'On'\}</Items>") 'W''s items are {''Stop'', ''On''}'
    Check (Save-Picture $script:hwnd (Join-Path $WorkRoot 'saved.png')) 'a picture after the saves'
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
