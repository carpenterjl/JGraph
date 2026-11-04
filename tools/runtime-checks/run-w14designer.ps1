<#
.SYNOPSIS
    Stage W14's window check (App Designer window plan): the Comparison Tool - visdiff from the command line over two plain-text apps, Replace
    Content and Save Result, and App Designer's Compare To button with its dialog, in the JGraph application.

.DESCRIPTION
    Writes two copies of w4\W4App.m into <WorkRoot>\left and <WorkRoot>\right: both with a private helper method in the editable section
    (r = 1 on the left, r = 2 on the right), the right one with a button moved in its layout. First launch: -r names compare_w4.m, which
    calls visdiff(left, right); the window 'Comparison Tool - W4App.m vs. W4App.m' is found, its summary read ('1 difference found'),
    its Design comparison tab's count, the right pane's title, Replace Content invoked (the right pane is then the result), Save Result
    invoked and the right file read back (r = 1, the moved button kept), the summary then 'No changes detected'. Second launch: -r opens
    the left app in App Designer; the DESIGNER tab's Compare To button is invoked, the Compare To dialog's path set through ValuePattern
    and its Compare button invoked, and the Comparison Tool opens owned by the designer with the design difference listed.

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
public static class W14Native {
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

# The pair: W4App with a helper in the editable section, r = 1 on the left and r = 2 on the right, and button D moved on the right.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Raw
$helper = "    methods (Access = private)`r`n`r`n        function r = helper(app)`r`n            r = VALUE;`r`n        end`r`n    end`r`n`r`n"
$withHelper = $source -replace "(    end\r?\n)(\r?\n)(end\r?\n\r?\n%\[appendix\])", "`$1`$2$helper`$3"
if ($withHelper -eq $source) { throw 'the helper was not placed' }
$left = Join-Path $WorkRoot 'left\W4App.m'
$right = Join-Path $WorkRoot 'right\W4App.m'
New-Item -ItemType Directory -Force (Split-Path $left) | Out-Null
New-Item -ItemType Directory -Force (Split-Path $right) | Out-Null
[System.IO.File]::WriteAllText($left, $withHelper.Replace('VALUE', '1'))
[System.IO.File]::WriteAllText($right, $withHelper.Replace('VALUE', '2').Replace('<Position>[200 300 100 22]</Position>', '<Position>[210 300 100 22]</Position>'))
$compareScript = Join-Path $WorkRoot 'compare_w4.m'
[System.IO.File]::WriteAllText($compareScript, "visdiff('$($left.Replace('\', '/'))', '$($right.Replace('\', '/'))');`n")

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
        foreach ($entry in [W14Native]::Windows([uint32] $processId)) {
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

function Set-Value($element, [string] $value) {
    $pattern = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.SetValue($value)
    Start-Sleep -Milliseconds 300
    return $true
}

# A text block's words, or a text box's value.
function Text-Of($element) {
    if (-not $element) { return $null }
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value) { return $value.Current.Value }
    return $element.Current.Name
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

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W14Native+RECT
    [void] [W14Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W14Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Enabled([IntPtr] $hwnd, [string] $id) {
    $element = Find-In $hwnd $id 5
    return ($null -ne $element -and $element.Current.IsEnabled)
}

function Start-JGraph([string] $statement) {
    $info = New-Object System.Diagnostics.ProcessStartInfo($exe)
    $info.UseShellExecute = $false
    $info.Arguments = "-r `"$statement`""
    return [System.Diagnostics.Process]::Start($info)
}

# --- Launch 1: visdiff from the command line, Replace Content and Save Result -----------------------------------------
$p = Start-JGraph $compareScript.Replace('\', '/')
try {
    $tool = Find-Window $p.Id { param($t) $t -eq 'Comparison Tool - W4App.m vs. W4App.m' }
    Check ($null -ne $tool) "visdiff opened 'Comparison Tool - W4App.m vs. W4App.m'"
    if (-not $tool) { throw 'no comparison window' }
    $hwnd = $tool.Key
    Start-Sleep -Milliseconds 1500
    $summary = Text-Of (Find-In $hwnd 'Compare.Summary' 5)
    Check ($summary -eq '1 difference found. Use the toolstrip buttons to navigate to it.') "the summary is R2025b's for one difference ($summary)"
    Check ((Text-Of (Find-In $hwnd 'Compare.LeftTitle' 5)) -eq $left) 'the left pane is titled with the left file'
    Check ((Text-Of (Find-In $hwnd 'Compare.RightTitle' 5)) -eq $right) 'the right pane is titled with the right file'
    $designTab = Find-In $hwnd 'Compare.DesignTab' 5
    Check ($null -ne $designTab -and $designTab.Current.Name -eq 'Design comparison (1)') "the design comparison tab counts the moved button ($($designTab.Current.Name))"
    Check ($null -ne (Find-In $hwnd 'Compare.MergeableOnly' 3)) 'Mergeable Only is on the toolbar for two apps'
    Check (-not (Enabled $hwnd 'Compare.Next')) 'Next is disabled at the only difference'
    Check (-not (Enabled $hwnd 'Compare.Previous')) 'Previous is disabled at the only difference'
    Check (Enabled $hwnd 'Compare.ReplaceContent') "Replace Content is enabled: the helper's line is the user's"
    Check (-not (Enabled $hwnd 'Compare.SaveResult')) 'Save Result waits for a merge'
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'comparison.png')) 'a picture of the Comparison Tool'

    Check (Invoke-Element (Find-In $hwnd 'Compare.ReplaceContent' 5)) 'Replace Content invoked'
    $rightTitle = Wait-Text $hwnd 'Compare.RightTitle' '(Result)' 10
    Check ($rightTitle -eq "$right (Result)") "the right pane is now the result ($rightTitle)"
    $summary = Text-Of (Find-In $hwnd 'Compare.Summary' 5)
    Check ($summary -eq 'No changes detected') "the merged code shows no changes ($summary)"
    Check (Enabled $hwnd 'Compare.Undo') 'Undo is enabled'
    Check (Enabled $hwnd 'Compare.SaveResult') 'Save Result is enabled'
    Check (Invoke-Element (Find-In $hwnd 'Compare.SaveResult' 5)) 'Save Result invoked'
    $status = Wait-Text $hwnd 'Compare.Status' 'Saved' 20
    Check ($status -and $status.StartsWith('Saved ')) "the status says what was saved ($status)"
    $saved = Get-Content -LiteralPath $right -Raw
    Check ($saved.Contains('r = 1;') -and -not $saved.Contains('r = 2;')) "the right file took the left's line"
    Check ($saved.Contains('<Position>[210 300 100 22]</Position>')) "the right file kept its own layout"
    Check ((Text-Of (Find-In $hwnd 'Compare.RightTitle' 5)) -eq $right) 'the right pane is the file again'
    Check (-not (Enabled $hwnd 'Compare.SaveResult')) 'Save Result rests after the save'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

# --- Launch 2: App Designer's Compare To ------------------------------------------------------------------------------
$p = Start-JGraph "appdesigner('$($left.Replace('\', '/'))')"
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500
    Check (Enabled $hwnd 'appDesignerToolstrip.designerTab.file.compareFilesButton') 'the DESIGNER tab''s Compare To is enabled'
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.designerTab.file.compareFilesButton' 5)) 'Compare To invoked'
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Compare To' } 15
    Check ($null -ne $dialog) "the 'Compare To' dialog opened"
    if ($dialog) {
        $dh = $dialog.Key
        Check (Set-Value (Find-In $dh 'CompareTo.Path' 5) $right) 'the path to compare to set'
        Check (Invoke-Element (Find-In $dh 'CompareTo.OK' 5)) 'Compare invoked'
    }
    $tool = Find-Window $p.Id { param($t) $t -eq 'Comparison Tool - W4App.m vs. W4App.m' } 30
    Check ($null -ne $tool) "the Comparison Tool opened from App Designer"
    if ($tool) {
        $th = $tool.Key
        Start-Sleep -Milliseconds 1500
        $summary = Text-Of (Find-In $th 'Compare.Summary' 5)
        Check ($summary -eq 'No changes detected') "the code is the same after the merge ($summary)"
        $designTab = Find-In $th 'Compare.DesignTab' 5
        Check ($null -ne $designTab -and $designTab.Current.Name -eq 'Design comparison (1)') "the design still differs by the moved button ($($designTab.Current.Name))"
        Check (Save-Picture $th (Join-Path $WorkRoot 'comparison-from-designer.png')) 'a picture of the Comparison Tool from the designer'
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
}

if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
