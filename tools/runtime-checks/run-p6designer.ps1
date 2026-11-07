<#
.SYNOPSIS
    Stage P6's window check (packages and class folders plan, decision PD4): an app saved inside a +package folder opened in
    App Designer and run by its dotted name from the folder above the package, in the JGraph application.

.DESCRIPTION
    Copies tools\runtime-checks\w4\W4App.m (a plain-text app) into <WorkRoot>\pkgs\+demo\P6App.m with its class renamed, and a
    second copy into <WorkRoot>\pkgs\+demo\+inner\P6Inner.m. One launch: -r runs <WorkRoot>\p6session.m, which opens the first in
    App Designer, puts the folder above the packages on the path and writes the session's answers to a file; the designer window
    is found (its title names the file); the answers are read: which('demo.P6App') naming the file under +demo and
    exist('demo.P6App') 8, and the same for demo.inner.P6Inner; Run is invoked from the quick access bar; the status line is waited
    for ('Running P6App.'); the app's window 'MATLAB App' is found, pictured and closed through WindowPattern; Run again makes it
    again; the designer is ended with Stop-Process. The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside
    before and put back after.

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
public static class P6Native {
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

# The app under +demo, and one under +demo\+inner: W4's plain-text app with its class renamed.
$pkgs = Join-Path $WorkRoot 'pkgs'
if (Test-Path -LiteralPath $pkgs) { Remove-Item -LiteralPath $pkgs -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $pkgs '+demo\+inner') | Out-Null
$template = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'w4\W4App.m'))
$app = Join-Path $pkgs '+demo\P6App.m'
[System.IO.File]::WriteAllText($app, $template.Replace('W4App', 'P6App'))
$inner = Join-Path $pkgs '+demo\+inner\P6Inner.m'
[System.IO.File]::WriteAllText($inner, $template.Replace('W4App', 'P6Inner'))
$answers = Join-Path $WorkRoot 'answers.txt'
if (Test-Path -LiteralPath $answers) { Remove-Item -LiteralPath $answers -Force }

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
        foreach ($entry in [P6Native]::Windows([uint32] $processId)) {
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
    Start-Sleep -Milliseconds 600
    return $true
}

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
    $rect = New-Object P6Native+RECT
    [void] [P6Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [P6Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Close-Window([IntPtr] $hwnd) {
    $pattern = Pattern ($A::FromHandle($hwnd)) ([System.Windows.Automation.WindowPattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Close()
    Start-Sleep -Milliseconds 800
    return $true
}

# The launch opens the app in App Designer, then asks the session what the dotted names stand for, with the
# folder above the packages on the path as Run puts it, and writes the answers to a file this check reads.
$slash = { param($p) $p.Replace('\', '/') }
$partial = Join-Path $WorkRoot 'answers.part'
# A bare -r statement runs in the JGS dialect (W13's finding); a MATLAB file run by name does not.
$session = Join-Path $WorkRoot 'p6session.m'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($session, @(
    "appdesigner('$(& $slash $app)');",
    "addpath('$(& $slash $pkgs)');",
    "fid = fopen('$(& $slash $partial)', 'w');",
    "fprintf(fid, 'which\t%s\nexist\t%d\nwhichinner\t%s\nexistinner\t%d\n', which('demo.P6App'), exist('demo.P6App'), which('demo.inner.P6Inner'), exist('demo.inner.P6Inner'));",
    "fclose(fid);",
    "movefile('$(& $slash $partial)', '$(& $slash $answers)');"
) -join "`n", $utf8)
$info = New-Object System.Diagnostics.ProcessStartInfo($exe)
$info.UseShellExecute = $false
$info.Arguments = "-r `"run('$(& $slash $session)')`""
$p = [System.Diagnostics.Process]::Start($info)
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opened the app under +demo in App Designer ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Check ($designer.Value.Contains('P6App')) 'the title names the file'
    Start-Sleep -Milliseconds 2500

    Write-Host '== the session'
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $answers) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    $lines = @()
    if (Test-Path -LiteralPath $answers) {
        Start-Sleep -Milliseconds 300
        $lines = @([System.IO.File]::ReadAllLines($answers)) # written whole under another name and moved here
    }
    $byKey = @{}
    foreach ($l in $lines) { $parts = $l -split "`t", 2; if ($parts.Count -eq 2) { $byKey[$parts[0]] = $parts[1] } }
    Check ($byKey['which'] -eq $app) "which('demo.P6App') names the file under +demo ($($byKey['which']))"
    Check ($byKey['exist'] -eq '8') "exist('demo.P6App') is 8 ($($byKey['exist']))"
    Check ($byKey['whichinner'] -eq $inner) "which('demo.inner.P6Inner') names the file under +demo\+inner ($($byKey['whichinner']))"
    Check ($byKey['existinner'] -eq '8') "exist('demo.inner.P6Inner') is 8 ($($byKey['existinner']))"

    Write-Host '== Run'
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.qab.runButton' 5)) 'Run invoked from the quick access bar'
    $status = Wait-Text $hwnd 'AppDesigner.Status' 'Running P6App.' 40
    Check ($status -eq 'Running P6App.') "the status says the app runs ($status)"
    $running = Find-Window $p.Id { param($t) $t -eq 'MATLAB App' } 30
    Check ($null -ne $running) 'the app''s window "MATLAB App" is up: demo.P6App ran from the folder above the package'
    if ($running) {
        Check (Save-Picture $running.Key (Join-Path $WorkRoot 'running-p6app.png')) 'a picture of the running app'
        Check (Close-Window $running.Key) 'the app''s window closed through WindowPattern'
    }

    # Run again: the designer closes the instance it launched and makes one.
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.qab.runButton' 5)) 'Run invoked again'
    $again = Find-Window $p.Id { param($t) $t -eq 'MATLAB App' } 30
    Check ($null -ne $again) 'the app''s window is up again'
    if ($again) { Check (Close-Window $again.Key) 'and closed' }
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'designer-p6app.png')) 'a picture of the designer over the packaged app'
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
