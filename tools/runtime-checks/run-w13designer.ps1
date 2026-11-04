<#
.SYNOPSIS
    Stage W13's window check (App Designer window plan): Share > MATLAB App - the "Configure MATLAB App for Sharing" window over a saved app and Package writing
    the .mltbx - then the shell's Apps menu installing, running and uninstalling the packaged app, in the JGraph application.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot> and points JGraph's add-ons folder at <WorkRoot>\addons through JGRAPH_ADDONS. First launch: JGraph.Application.exe
    with -r "appdesigner('<app>')"; the SHARE drop-down is opened through UI Automation's ExpandCollapse and MATLAB App invoked; in the share window the app card's title and
    version are read, the analysis's end waited for, Show Code toggled and the script's lines read, and Package invoked; the packaging dialog's step and result are read, and the
    toolbox looked for at <WorkRoot>\release\W4App.mltbx. Second launch: -r names a .m file that installs that toolbox with matlab.addons.toolbox.installToolbox (a bare -r statement runs as JGS, where a dotted name does not parse); the shell's Apps menu is expanded,
    the app's item invoked (the app's window "MATLAB App" waited for, pictured and closed through WindowPattern), and its Uninstall item invoked, after which the toolbox's folder
    is gone. The user's settings file is moved aside before and put back after; each application is ended with Stop-Process. The file dialogs are not driven.

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
public static class W13Native {
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

$app = Join-Path $WorkRoot 'W4App.m'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'w4\W4App.m') -Destination $app -Force
$release = Join-Path $WorkRoot 'release'
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
$addons = Join-Path $WorkRoot 'addons'
if (Test-Path -LiteralPath $addons) { Remove-Item -LiteralPath $addons -Recurse -Force }
$mltbx = Join-Path $release 'W4App.mltbx'

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
        foreach ($entry in [W13Native]::Windows([uint32] $processId)) {
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

function Expand-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Expand()
    Start-Sleep -Milliseconds 700
    return $true
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value) { return $value.Current.Value }
    return $element.Current.Name
}

function Wait-Text([IntPtr] $hwnd, [string] $id, [string] $expected, [int] $seconds = 60) {
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
    $rect = New-Object W13Native+RECT
    [void] [W13Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W13Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# An item of an open drop-down or menu, which is its own window of the process.
function Find-Menu([int] $processId, $property, [string] $value, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W13Native]::Windows([uint32] $processId)) {
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

function Start-JGraph([string] $statement) {
    $info = New-Object System.Diagnostics.ProcessStartInfo($exe)
    $info.UseShellExecute = $false
    $info.Arguments = "-r `"$statement`""
    $info.EnvironmentVariables['JGRAPH_ADDONS'] = $addons
    return [System.Diagnostics.Process]::Start($info)
}

# --- Launch 1: Share > MATLAB App ---------------------------------------------------------------------------------
$forward = $app.Replace('\', '/')
$p = Start-JGraph "appdesigner('$forward')"
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    $item = $null
    for ($try = 0; $try -lt 4 -and -not $item; $try++) {
        [void] (Expand-Element (Find-In $hwnd 'appDesignerToolstrip.designerTab.share.shareAllOptions' 5))
        $item = Find-Menu $p.Id $A::AutomationIdProperty 'appDesignerToolstrip.designerTab.file.packageButton' 5
    }
    Check ($null -ne $item -and $item.Current.IsEnabled) "the SHARE drop-down opened with MATLAB App enabled (after $try tries)"
    Check (Invoke-Element $item) 'MATLAB App invoked'

    $share = Find-Window $p.Id { param($t) $t -eq 'Configure MATLAB App for Sharing' } 20
    Check ($null -ne $share) "the window 'Configure MATLAB App for Sharing' opened"
    if (-not $share) { throw 'no share window' }
    $sh = $share.Key
    Start-Sleep -Milliseconds 1500
    Check ((Text-Of (Find-In $sh 'Share.CardTitle' 5)) -eq 'W4App') "the app card's title is the app's name ($(Text-Of (Find-In $sh 'Share.CardTitle' 2)))"
    Check ((Text-Of (Find-In $sh 'Share.CardVersion' 5)) -eq 'version 1.0') "the card says 'version 1.0'"
    $deadline = (Get-Date).AddSeconds(60)
    do { $status = Text-Of (Find-In $sh 'Share.Status' 2); Start-Sleep -Milliseconds 400 } while ($status -and $status.StartsWith('Analyzing') -and (Get-Date) -lt $deadline)
    Check ([string]::IsNullOrEmpty($status)) "the analysis ended without a word ($status)"
    Check ($null -ne (Find-In $sh 'Share.NoFiles' 3)) "no files required beyond the app itself"
    Check ($null -eq (Find-In $sh 'Share.RuntimeNone' 2)) 'no package type on the MATLAB app page'
    Check (Toggle-Element (Find-In $sh 'Share.ShowCode' 5)) 'Show Code toggled'
    $code = Wait-Text $sh 'Share.Code' 'matlab.addons.toolbox.packageToolbox(packageOpts)' 10
    Check ($code -and $code.Contains("packageOpts=matlab.addons.toolbox.ToolboxOptions(`"$WorkRoot`", ")) "the script opens the options over the app's folder"
    Check ($code -and $code.Contains("packageOpts.AppGalleryFiles = {`"$app`"};")) 'the app is the gallery file'
    Check ($code -and $code.Contains("packageOpts.OutputFile = `"$mltbx`";")) 'the toolbox goes to release\W4App.mltbx'
    Check (Save-Picture $sh (Join-Path $WorkRoot 'share-matlabapp.png')) 'a picture of the share window'

    Check (Invoke-Element (Find-In $sh 'Share.Package' 5)) 'Package invoked'
    $dialog = Find-Window $p.Id { param($t) $t -eq 'MATLAB App Packaging Progress' } 20
    Check ($null -ne $dialog) "the packaging dialog 'MATLAB App Packaging Progress' opened"
    if ($dialog) {
        $dh = $dialog.Key
        Check ((Text-Of (Find-In $dh 'Step.Build' 5)) -eq 'Packaging MATLAB App') "its step is 'Packaging MATLAB App'"
        $result = Wait-Text $dh 'PackagingResult' 'Process completed' 300
        if ($result -ne 'Process completed successfully') {
            [void] (Invoke-Element (Find-In $dh 'ToggleLog' 3))
            Write-Host "    log: $(Text-Of (Find-In $dh 'PackagingLog' 3))"
        }
        Check ($result -eq 'Process completed successfully') "the package ended with 'Process completed successfully' (got '$result')"
        Check (Test-Path -LiteralPath $mltbx) "the toolbox was written at $mltbx"
        Check (Save-Picture $dh (Join-Path $WorkRoot 'share-matlabapp-progress.png')) 'a picture of the packaging dialog'
        Check (Invoke-Element (Find-In $dh 'ClosePackaging' 5)) 'the dialog closed'
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

# --- Launch 2: the Apps menu over the installed toolbox ----------------------------------------------------------
if (Test-Path -LiteralPath $mltbx) {
    # -r runs a bare statement as JGS, where a dotted name does not parse; a .m file named by -r runs as MATLAB.
    $installScript = Join-Path $WorkRoot 'install_w4.m'
    [System.IO.File]::WriteAllText($installScript, "matlab.addons.toolbox.installToolbox('$($mltbx.Replace('\', '/'))');`n")
    $p = Start-JGraph $installScript.Replace('\', '/')
    try {
        $shell = Find-Window $p.Id { param($t) $t.StartsWith('JGraph') }
        Check ($null -ne $shell) "the shell window opened ($($shell.Value))"
        if (-not $shell) { throw 'no shell window' }
        $hwnd = $shell.Key
        $installed = Join-Path $addons 'Toolboxes\W4App'
        $deadline = (Get-Date).AddSeconds(60)
        while (-not (Test-Path -LiteralPath (Join-Path $installed 'resources\addons_core.xml')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Check (Test-Path -LiteralPath (Join-Path $installed 'resources\addons_core.xml')) "installToolbox laid the toolbox out under $installed"
        Check (Test-Path -LiteralPath (Join-Path $installed 'resources\mltbx_app_gallery_registration.xml')) 'with its gallery registration'
        Check (Test-Path -LiteralPath (Join-Path $installed 'W4App.m')) 'and the app itself'
        Start-Sleep -Milliseconds 1500

        $apps = Find-In $hwnd 'Menu.Apps' 10
        Check ($null -ne $apps) 'the Apps menu is there'
        Check (Expand-Element $apps) 'the Apps menu opened'
        $entry = Find-Menu $p.Id $A::NameProperty 'W4App' 5
        Check ($null -ne $entry) "the gallery lists W4App"
        Check (Invoke-Element $entry) 'W4App invoked'
        $window = Find-Window $p.Id { param($t) $t -eq 'MATLAB App' } 60
        Check ($null -ne $window) "the app's window 'MATLAB App' opened from the gallery"
        if ($window) {
            Start-Sleep -Milliseconds 1000
            Check (Save-Picture $window.Key (Join-Path $WorkRoot 'gallery-app.png')) 'a picture of the running app'
            $close = Pattern ($A::FromHandle($window.Key)) ([System.Windows.Automation.WindowPattern]::Pattern)
            if ($close) { $close.Close(); Start-Sleep -Milliseconds 800 }
            Check ($null -eq (Find-Window $p.Id { param($t) $t -eq 'MATLAB App' } 3)) 'the app closed'
        }

        Check (Expand-Element (Find-In $hwnd 'Menu.Apps' 10)) 'the Apps menu opened again'
        $uninstall = Find-Menu $p.Id $A::AutomationIdProperty 'Apps.Uninstall' 5
        Check ($null -ne $uninstall) 'its Uninstall submenu'
        Check (Expand-Element $uninstall) 'Uninstall opened'
        $target = Find-Menu $p.Id $A::NameProperty 'W4App' 5
        Check ($null -ne $target -and (Invoke-Element $target)) "Uninstall > W4App invoked"
        $deadline = (Get-Date).AddSeconds(30)
        while ((Test-Path -LiteralPath $installed) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Check (-not (Test-Path -LiteralPath $installed)) "the toolbox's folder is gone"
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
    }
}

if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
