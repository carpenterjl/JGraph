<#
.SYNOPSIS
    Stage W12's window check (App Designer window plan): Share > Standalone Desktop App - the "Configure Standalone App for Sharing" window over a saved app,
    its App Appearance, files required, package type and Show Code, and Package building the program and its installer under the packaging dialog, in the JGraph application.

.DESCRIPTION
    Copies the plain-text app w4\W4App.m into <WorkRoot> (an app that takes no input arguments, so the built program can start it; w8\W8bApp.mlapp takes some and the program refuses in an error dialog) and starts JGraph.Application.exe with -r "appdesigner('<app>')". The SHARE drop-down is opened
    through UI Automation's ExpandCollapse, its Web App item read as disabled (R2025b disables it without a compiler; web apps are out of JGraph's scope) and Standalone
    Desktop App invoked. In the share window the app's name and version are read, the analysis's end waited for, the package type set to "Do not include MATLAB Runtime"
    through SelectionItem (so the check needs no runtime installer), Show Code toggled and the script read, and Package invoked; the packaging dialog's two steps and
    result are read, Open Output Folder's enabling checked, and the program and the installer looked for under <WorkRoot>\release. With -Runtime, the built program is
    started with JGRAPH_RUNTIME_ROOT naming that runtime, its window waited for, pictured and closed through WindowPattern, and its exit code checked.

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back after; the application is ended with Stop-Process.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1 (optional): the built program is run under it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W12Native {
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
        foreach ($entry in [W12Native]::Windows([uint32] $processId)) {
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

function Select-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Select()
    Start-Sleep -Milliseconds 400
    return $true
}

# A text block's words, or a text box's value.
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
    $rect = New-Object W12Native+RECT
    [void] [W12Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W12Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

# An item of an open drop-down, which is its own window.
function Find-Menu([string] $id, [int] $seconds = 5) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W12Native]::Windows([uint32] $p.Id)) {
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

function Open-Drop([string] $id) {
    $arrow = Find-In $script:hwnd $id 5
    $expand = Pattern $arrow ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if (-not $expand) { return $false }
    $expand.Expand()
    Start-Sleep -Milliseconds 600
    return $true
}

function Enabled([IntPtr] $hwnd, [string] $id) {
    $element = Find-In $hwnd $id 5
    return ($null -ne $element -and $element.Current.IsEnabled)
}

$forward = $app.Replace('\', '/')
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"appdesigner('$forward')`"") -PassThru
$null = $p.Handle
$built = $null

try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opens the app ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $script:hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- The SHARE drop-down ---------------------------------------------------------------------------------
    Check (Enabled $script:hwnd 'appDesignerToolstrip.designerTab.share.shareAllOptions') 'the SHARE drop-down enabled with an app open'
    # The popup is its own window, read once it is up; a drop-down that shut before it was read is opened again.
    $desktop = $null
    for ($try = 0; $try -lt 4 -and -not $desktop; $try++) {
        [void] (Open-Drop 'appDesignerToolstrip.designerTab.share.shareAllOptions')
        $desktop = Find-Menu 'appDesignerToolstrip.designerTab.share.compileDesktop' 5
    }
    Check ($null -ne $desktop) "the SHARE drop-down opened (after $try tries)"
    $web = Find-Menu 'appDesignerToolstrip.designerTab.share.compileWeb' 5
    Check ($null -ne $web -and -not $web.Current.IsEnabled) 'Web App shown and disabled, as R2025b shows it without a compiler'
    $matlabApp = Find-Menu 'appDesignerToolstrip.designerTab.file.packageButton' 3
    Check ($null -ne $matlabApp) 'MATLAB App shown'
    Check ($null -ne $desktop -and $desktop.Current.IsEnabled) 'Standalone Desktop App enabled'
    Check (Invoke-Element $desktop) 'Standalone Desktop App invoked'

    # --- The share window -------------------------------------------------------------------------------------
    $share = Find-Window $p.Id { param($t) $t -eq 'Configure Standalone App for Sharing' } 20
    Check ($null -ne $share) "the window 'Configure Standalone App for Sharing' opened"
    if (-not $share) { throw 'no share window' }
    $sh = $share.Key
    Start-Sleep -Milliseconds 1500
    $name = Text-Of (Find-In $sh 'Share.AppName' 5)
    Check ($name -eq 'W4App') "App Name is the app's, as App Details holds it ($name)"
    $version = Text-Of (Find-In $sh 'Share.Version' 5)
    Check ($version -eq '1.0') "Version 1.0 from App Details ($version)"
    $deadline = (Get-Date).AddSeconds(60)
    do { $status = Text-Of (Find-In $sh 'Share.Status' 2); Start-Sleep -Milliseconds 400 } while ($status -and $status.StartsWith('Analyzing') -and (Get-Date) -lt $deadline)
    Check ([string]::IsNullOrEmpty($status)) "the analysis ended without a word ($status)"
    $noFiles = Find-In $sh 'Share.NoFiles' 3
    Check ($null -ne $noFiles) "no files required beyond the app itself: 'No custom files currently added' shown"
    Check ((Text-Of (Find-In $sh 'Share.SplashScreenFile' 3)) -eq 'Default') 'the default splash screen'
    Check ((Text-Of (Find-In $sh 'Share.IconFile' 3)) -eq 'Default') 'the default icon'
    Check (Toggle-Element (Find-In $sh 'Share.ShowCode' 5)) 'Show Code toggled'
    $code = Wait-Text $sh 'Share.Code' 'compiler.build.standaloneWindowsApplication(buildOpts)' 10
    Check ($code -and $code.Contains("buildOpts=compiler.build.StandaloneApplicationOptions(`"$app`");")) 'the script opens with the options over the app'
    Check ($code -and $code.Contains("buildOpts.OutputDir = `"$release\build`";")) 'the build goes to release\build'
    Check ($code -and $code.Contains("packageOpts.OutputDir =`"$release\package`";")) 'the installer goes to release\package'
    Check ($code -and $code.Contains('packageOpts.ApplicationName = "W4App";')) 'the installer takes the name'
    Check ($code -and -not $code.Contains('RuntimeDelivery')) "no RuntimeDelivery line at R2025b's default"
    Check (Select-Element (Find-In $sh 'Share.RuntimeNone' 5)) '"Do not include MATLAB Runtime" chosen'
    $code = Wait-Text $sh 'Share.Code' 'packageOpts.RuntimeDelivery = "none";' 5
    Check ($code -and $code.Contains('packageOpts.RuntimeDelivery = "none";')) 'the script says so'
    Check (Save-Picture $sh (Join-Path $WorkRoot 'share-standalone.png')) 'a picture of the share window'

    # --- Package ------------------------------------------------------------------------------------------------
    Check (Invoke-Element (Find-In $sh 'Share.Package' 5)) 'Package invoked'
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Standalone App Packaging Progress' } 20
    Check ($null -ne $dialog) "the packaging dialog 'Standalone App Packaging Progress' opened"
    if ($dialog) {
        $dh = $dialog.Key
        Check ((Text-Of (Find-In $dh 'PackagingName' 5)) -eq 'W4App') 'the dialog names the app'
        Check ((Text-Of (Find-In $dh 'Step.Build' 5)) -eq 'Building Standalone App') "its first step is 'Building Standalone App'"
        Check ((Text-Of (Find-In $dh 'Step.Package' 5)) -eq 'Creating Installer') "its second step is 'Creating Installer'"
        $result = Wait-Text $dh 'PackagingResult' 'Process completed' 600
        if ($result -ne 'Process completed successfully') {
            [void] (Invoke-Element (Find-In $dh 'ToggleLog' 3))
            Write-Host "    log: $(Text-Of (Find-In $dh 'PackagingLog' 3))"
        }
        Check ($result -eq 'Process completed successfully') "the package ended with 'Process completed successfully' (got '$result')"
        $built = Join-Path $release 'build\W4App.exe'
        Check (Test-Path -LiteralPath $built) "the program was built at $built"
        $installer = Join-Path $release 'package\MyAppInstaller.exe'
        Check (Test-Path -LiteralPath $installer) "the installer was written at $installer"
        Check (Enabled $dh 'OpenOutputFolder') 'Open Output Folder enabled'
        Check (Save-Picture $dh (Join-Path $WorkRoot 'share-progress.png')) 'a picture of the packaging dialog'
        Check (Invoke-Element (Find-In $dh 'ClosePackaging' 5)) 'the dialog closed'
    }
    Check ((Text-Of (Find-In $sh 'Share.Status' 5)) -eq 'Process completed successfully') 'the share window says the process completed'
    Check (Enabled $sh 'Share.Package') 'Package enabled again'
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
    if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }
}

# --- The built program under a runtime ----------------------------------------------------------------------------
if ($Runtime -and $built -and (Test-Path -LiteralPath $built)) {
    Write-Host "== the built program under $Runtime"
    $Runtime = (Resolve-Path -LiteralPath $Runtime).Path
    $info = New-Object System.Diagnostics.ProcessStartInfo($built)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $built
    $info.EnvironmentVariables['JGRAPH_RUNTIME_ROOT'] = $Runtime
    $run = [System.Diagnostics.Process]::Start($info)
    try {
        $window = Find-Window $run.Id { param($t) $t -eq 'MATLAB App' } 120
        Check ($null -ne $window) "the program opens the app's window 'MATLAB App'"
        if ($window) {
            Start-Sleep -Milliseconds 1500
            Check (Save-Picture $window.Key (Join-Path $WorkRoot 'built-app.png')) 'a picture of the running program'
            $close = Pattern ($A::FromHandle($window.Key)) ([System.Windows.Automation.WindowPattern]::Pattern)
            Check ($null -ne $close) 'the window closes through WindowPattern'
            if ($close) { $close.Close() }
            Check ($run.WaitForExit(30000)) 'the program ended after its window closed'
            Check ($run.HasExited -and $run.ExitCode -eq 0) "exit code 0 (got $(if ($run.HasExited) { $run.ExitCode } else { 'running' }))"
        }
    }
    finally {
        if (-not $run.HasExited) { Stop-Process -Id $run.Id -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
