<#
.SYNOPSIS
    Stage K17g's window check (compiler and SDK plan): the Excel Add-in Compiler app's Build and
    Build and Package in JGraph's own application, driven through UI Automation patterns (no
    synthesized input), as K19's check drives the other compiler apps.

.DESCRIPTION
    JGraph.Application -r "excelAddinCompiler('<file>')": the Excel Add-in Compiler window with the
    file among its exported functions, the status line, Show Code with the compiler.build.excelAddIn
    script, Build with its packaging dialog ending in "Process completed successfully" and the
    add-in's DLL, module and workbook in the task's output folder (the build registers the add-in for
    the current user, as mcc does; this script unregisters it at the end), then Build and Package
    ending the same way with the installer in the task's package folder (inspected by name, never
    run), and a picture of the window.

    The app is ended with Stop-Process (nothing is saved); the add-in is unregistered with regsvr32 /u
    and the keys its registration left are swept, so the registry after is the one before.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class K17WinNative {
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
$failures = New-Object System.Collections.Generic.List[string]
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"
if (-not (Test-Path $exe)) { throw "Build src\JGraph.Application in $Configuration first: $exe" }
$regsvr = Join-Path $env:SystemRoot 'System32\regsvr32.exe'

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [string] $name, [int] $seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [K17WinNative]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of the process: {0}" -f (([K17WinNative]::Windows([uint32] $processId) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
    return $null
}

function Find-ById($root, [string] $id, [int] $seconds = 15) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { return $pattern.Current.Value }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref] $pattern)) { return $pattern.DocumentRange.GetText(-1) }
    return $element.Current.Name
}

function Wait-Text($root, [string] $id, [string] $expected, [int] $seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $text = Text-Of (Find-ById $root $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 500
    }
    return $text
}

function Invoke-ById($root, [string] $id) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true }
    catch { Write-Host "    uia: invoking '$id' failed: $($_.Exception.Message)"; return $false }
}

function Toggle-ById($root, [string] $id) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return $true }
    catch { Write-Host "    uia: toggling '$id' failed: $($_.Exception.Message)"; return $false }
}

function Save-Picture($window, [string] $path) {
    $hwnd = [IntPtr] $window.Current.NativeWindowHandle
    $rect = New-Object K17WinNative+RECT
    [void] [K17WinNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [K17WinNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Stop-Leftover($process) {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}

# The user's classes before, for the sweep after (the build registers the add-in for the current user).
$regRoots = @('Software\Classes', 'Software\Classes\CLSID', 'Software\Classes\TypeLib', 'Software\Classes\Interface')
function Reg-Names() {
    $names = @()
    foreach ($root in $regRoots) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($root)
        if ($key) { foreach ($n in ($key.GetSubKeyNames() | Sort-Object)) { $names += "$root\$n" }; $key.Close() }
    }
    $names
}
$before = @(Reg-Names)
# A build registers JGraph's COM utility library for the current user when nothing has (decision KO10);
# what this run's build registers, this script unregisters, through the DLL the registration names.
$utilityKey = 'HKCU:\Software\Classes\JGraphComUtil.MWUtil25.2'
$utilityWasRegistered = (Test-Path $utilityKey) -or (Test-Path 'HKLM:\Software\Classes\JGraphComUtil.MWUtil25.2')
function Unregister-Utility() {
    if ($utilityWasRegistered -or -not (Test-Path $utilityKey)) { return }
    $clsid = (Get-ItemProperty "$utilityKey\CLSID" -ErrorAction SilentlyContinue).'(default)'
    $path = if ($clsid) { (Get-ItemProperty "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue).'(default)' } else { $null }
    if ($path -and (Test-Path $path)) { Start-Process -FilePath $regsvr -ArgumentList @('/s', '/u', "`"$path`"") -NoNewWindow -Wait; Write-Host "  note the build registered the utility library ($path); unregistered" }
}

# excelAddinCompiler takes no file (R2025b's nargin is 0, and a file is "Too many input arguments" in both
# engines; K19 recorded it), so the task comes from a project made headless by the CLI, as K20's check does:
# an Excel add-in task whose exported function and add-in name are set, then openProject and the command,
# which brings up the Compiler Task Manager over the project's tasks and, from it, the app over the task.
$cli = Join-Path $repo 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe'
if (-not (Test-Path $cli)) { throw "Build src\JGraph.Cli in Debug first: $cli" }
$root = Join-Path $WorkRoot 'k17proj'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Force $root | Out-Null
$main = Join-Path $root 'k17win_hello.m'
[System.IO.File]::WriteAllText($main, "function y = k17win_hello(x)`n%K17WIN_HELLO Adds one.`nif nargin < 1, x = 0; end`ny = x + 1;`nend`n")
$rootForward = $root.Replace('\', '/')
$prep = Join-Path $WorkRoot 'prep.m'
[System.IO.File]::WriteAllText($prep, "proj = matlab.project.createProject('$rootForward');`naddFile(proj, 'k17win_hello.m');`nt = compiler.project.getExcelAddInConfiguration(proj, 'myXl');`nt.FunctionFileList = {'k17win_hello.m'};`nt.AddInName = 'k17win_hello';`nclose(proj);`ndisp('prepared');`n")
$prepOut = & $cli -batch "run('$($prep.Replace('\', '/'))')" 2>&1
Check (($prepOut | Out-String).Contains('prepared')) "the project and its Excel task were made by the CLI ($($prepOut | Out-String))"

Write-Host "== excel"
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"openProject('$rootForward'); excelAddinCompiler`"") -WorkingDirectory $root -PassThru
$null = $p.Handle
$build = Join-Path $root 'myXl\output\build'
$dll = Join-Path $build 'k17win_hello_1_0.dll'
try {
    $manager = Find-Window $p.Id 'Compiler Task Manager'
    Check ($null -ne $manager) "a window titled 'Compiler Task Manager'"
    $window = $null
    if ($manager) {
        Start-Sleep -Seconds 2
        Check ($null -ne (Find-ById $manager 'Task.myXl' 5)) 'the list carries Task.myXl'
        Check (Invoke-ById $manager 'OpenTask') 'Open invoked'
        $window = Find-Window $p.Id 'Excel Add-in Compiler - myXl'
        Check ($null -ne $window) "a window titled 'Excel Add-in Compiler - myXl'"
    }
    if ($window) {
        Start-Sleep -Seconds 2
        Check ($null -ne (Find-ById $window 'ExportedFunctions')) 'the exported functions list is there'
        $status = Text-Of (Find-ById $window 'Status')
        Check ($status -eq 'The compiler task is ready') "the status says the task is ready (got '$status')"
        Check (Toggle-ById $window 'ShowCode') 'Show Code toggled'
        $code = Wait-Text $window 'Code' 'compiler.build.excelAddIn(buildOpts)' 10
        Check ($code -and $code.Contains('buildOpts = compiler.build.ExcelAddInOptions(')) 'the code view shows the add-in options'
        Check ($code -and $code.Contains('buildOpts.ClassName = "Class1";') -and $code.Contains('buildOpts.GenerateVisualBasicFile = true;')) 'the code view shows the class name and the module switch'
        Check ($code -and $code.Contains('compiler.package.installer(buildResult, "Options", packageOpts);')) 'the code view shows the installer'
        Check (Invoke-ById $window 'Build') 'Build invoked'
        $dialogRoot = Find-Window $p.Id 'Excel Add-in Packaging Progress'
        Check ($null -ne $dialogRoot) "the packaging dialog 'Excel Add-in Packaging Progress' opened"
        if ($dialogRoot) {
            $step = Text-Of (Find-ById $dialogRoot 'Step.Build')
            Check ($step -eq 'Building Excel Add-In') "its step is 'Building Excel Add-In' (got '$step')"
            $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 300
            Check ($result -eq 'Process completed successfully') "the build ended with 'Process completed successfully' (got '$result')"
            Check (Test-Path $dll) "the add-in was built at $dll"
            Check (Test-Path (Join-Path $build 'k17win_hello.bas')) 'the module was written beside it'
            Check (Test-Path (Join-Path $build 'k17win_hello.xlam')) 'the add-in workbook was written beside it'
            Check (Test-Path 'HKCU:\Software\Classes\k17win_hello.Class1.1_0') 'the build registered k17win_hello.Class1.1_0 for the current user'
            Check (Save-Picture $window (Join-Path $WorkRoot 'excel.png')) 'a picture of the window was saved'
            Check (Invoke-ById $dialogRoot 'ClosePackaging') 'the dialog closed'
        }
        Check (Invoke-ById $window 'BuildAndPackage') 'Build and Package invoked'
        $dialogRoot = Find-Window $p.Id 'Excel Add-in Packaging Progress'
        Check ($null -ne $dialogRoot) 'the packaging dialog opened again'
        if ($dialogRoot) {
            $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 600
            Check ($result -eq 'Process completed successfully') "the build and package ended with 'Process completed successfully' (got '$result')"
            $installers = @(Get-ChildItem (Join-Path $root 'myXl\output\package') -Filter '*.exe' -ErrorAction SilentlyContinue)
            Check ($installers.Count -eq 1) "one installer is in the package folder ($(($installers | ForEach-Object { $_.Name }) -join ', ')); inspected by name, never run"
            Check (Save-Picture $dialogRoot (Join-Path $WorkRoot 'excel-package.png')) 'a picture of the dialog was saved'
            [void] (Invoke-ById $dialogRoot 'ClosePackaging')
        }
    }
}
finally {
    Stop-Leftover $p
    if (Test-Path $dll) { Start-Process -FilePath $regsvr -ArgumentList @('/s', '/u', "`"$dll`"") -NoNewWindow -Wait }
    Unregister-Utility
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    foreach ($a in @((Reg-Names) | Where-Object { $before -notcontains $_ } | Sort-Object { - $_.Length })) {
        $text = & reg.exe query "HKCU\$a" /s 2>$null | Out-String
        if ($text -match '(?i)k17win_hello') { & reg.exe delete "HKCU\$a" /f 2>$null | Out-Null }
    }
    $ErrorActionPreference = $old
}
$after = @(Reg-Names)
$added = @($after | Where-Object { $before -notcontains $_ })
$removed = @($before | Where-Object { $after -notcontains $_ })
Check ($added.Count -eq 0 -and $removed.Count -eq 0) "the registry after is the one before (+$($added.Count) -$($removed.Count)$(if ($added.Count) { ': ' + ($added -join ', ') }))"

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
