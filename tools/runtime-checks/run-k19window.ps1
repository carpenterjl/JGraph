<#
.SYNOPSIS
    Stage K19's window check (compiler and SDK plan): the compiler apps' windows in JGraph's own
    application, driven through UI Automation patterns (no synthesized input), in two runs.

.DESCRIPTION
    standalone  JGraph.Application -r "standaloneApplicationCompiler('<file>')": the Standalone
                Application Compiler window with the file as its main file, the status line, Show
                Code with the compiler.build script, Build with its packaging dialog ending in
                "Process completed successfully", the program built in the task's output folder, and
                a picture of the window.
    server      JGraph.Application -r "productionServerArchiveCompiler('<file>')": the Production
                Server Archive Compiler window, Build, then the Test Client: Start on a free port, a
                request to the function, the reply, Stop.

    Each run starts the app, ends it with Stop-Process (nothing is saved), and captures the window
    alone with PrintWindow into <WorkRoot>\<run>.png.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runs
    A comma-separated list of 'standalone' and 'server'; both by default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runs = 'standalone,server',
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class K19Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    // Every visible top-level window of a process with its title, by Win32 enumeration: the UI
    // Automation root does not list a window the process opens after the client first read it.
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

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [string] $name, [int] $seconds = $TimeoutSeconds) {
    # By its handle, not through the UI Automation root: the root does not list a window the
    # process opens after the client first read it (the packaging dialog, the test client).
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [K19Native]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of the process: {0}" -f (([K19Native]::Windows([uint32] $processId) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
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

function Set-ById($root, [string] $id, [string] $value) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value); return $true }
    catch { Write-Host "    uia: setting '$id' failed: $($_.Exception.Message)"; return $false }
}

function Toggle-ById($root, [string] $id) {
    $element = Find-ById $root $id
    if (-not $element) { return $false }
    try { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return $true }
    catch { Write-Host "    uia: toggling '$id' failed: $($_.Exception.Message)"; return $false }
}

function Save-Picture($window, [string] $path) {
    $hwnd = [IntPtr] $window.Current.NativeWindowHandle
    $rect = New-Object K19Native+RECT
    [void] [K19Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [K19Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Stop-Leftover($process) {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}

# The task's source: a function with an input and an output, so the test client can call it.
$src = Join-Path $WorkRoot 'src'
New-Item -ItemType Directory -Force $src | Out-Null
$main = Join-Path $src 'k19win_hello.m'
[System.IO.File]::WriteAllText($main, "function y = k19win_hello(x)`n%K19WIN_HELLO Adds one.`nif nargin < 1, x = 0; end`ny = x + 1;`nfprintf('k19win_hello %g\n', y);`nend`n")
$mainForward = $main.Replace('\', '/')
$selected = $Runs -split ','

if ($selected -contains 'standalone') {
    Write-Host "== standalone"
    $p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"standaloneApplicationCompiler('$mainForward')`"") -WorkingDirectory $src -PassThru
    $null = $p.Handle
    try {
        $window = Find-Window $p.Id 'Standalone Application Compiler'
        Check ($null -ne $window) "a window titled 'Standalone Application Compiler'"
        if ($window) {
            Start-Sleep -Seconds 2
            $mainText = Text-Of (Find-ById $window 'MainFile')
            Check ($mainText -eq $main) "the main file is $main (got '$mainText')"
            $status = Text-Of (Find-ById $window 'Status')
            Check ($status -eq 'The compiler task is ready') "the status says the task is ready (got '$status')"
            Check (Toggle-ById $window 'ShowCode') 'Show Code toggled'
            $code = Wait-Text $window 'Code' 'compiler.build.standaloneApplication(buildOpts)' 10
            Check ($code -and $code.Contains('buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(projectRoot, "k19win_hello.m"));')) 'the code view shows the build options over the main file'
            Check ($code -and $code.Contains('compiler.package.installer(buildResults, "Options", packageOpts);')) 'the code view shows the installer'
            Check (Invoke-ById $window 'Build') 'Build invoked'
            $dialogRoot = Find-Window $p.Id 'Standalone App Packaging Progress'
            Check ($null -ne $dialogRoot) "the packaging dialog 'Standalone App Packaging Progress' opened"
            if ($dialogRoot) {
                $step = Text-Of (Find-ById $dialogRoot 'Step.Build')
                Check ($step -eq 'Building Standalone App') "its step is 'Building Standalone App' (got '$step')"
                $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 300
                Check ($result -eq 'Process completed successfully') "the build ended with 'Process completed successfully' (got '$result')"
                $built = Join-Path $src 'k19win_hello\output\build\k19win_hello.exe'
                Check (Test-Path $built) "the program was built at $built"
                Check (Save-Picture $window (Join-Path $WorkRoot 'standalone.png')) 'a picture of the window was saved'
                Check (Invoke-ById $dialogRoot 'ClosePackaging') 'the dialog closed'
            }
        }
    }
    finally { Stop-Leftover $p }
}

if ($selected -contains 'server') {
    Write-Host "== server"
    $p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"productionServerArchiveCompiler('$mainForward')`"") -WorkingDirectory $src -PassThru
    $null = $p.Handle
    try {
        $window = Find-Window $p.Id 'Production Server Archive Compiler'
        Check ($null -ne $window) "a window titled 'Production Server Archive Compiler'"
        if ($window) {
            Start-Sleep -Seconds 2
            Check ($null -ne (Find-ById $window 'ExportedFunctions')) 'the exported functions list is there'
            Check ($null -eq (Find-ById $window 'BuildAndPackage' 1)) 'an archive has no Build and Package'
            Check (Invoke-ById $window 'Build') 'Build invoked'
            $dialogRoot = Find-Window $p.Id 'Deployable Archive (.ctf) Packaging Progress'
            Check ($null -ne $dialogRoot) "the packaging dialog 'Deployable Archive (.ctf) Packaging Progress' opened"
            if ($dialogRoot) {
                $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 300
                Check ($result -eq 'Process completed successfully') "the build ended with 'Process completed successfully' (got '$result')"
                Check (Test-Path (Join-Path $src 'k19win_hello\output\build\k19win_hello.ctf')) 'the archive was built'
                [void] (Invoke-ById $dialogRoot 'ClosePackaging')
            }
            Check (Invoke-ById $window 'TestClient') 'Test Client invoked'
            $client = Find-Window $p.Id 'Test Client'
            Check ($null -ne $client) "a window titled 'Test Client'"
            if ($client) {
                Check (Set-ById $client 'Port' '0') 'the port set to 0 (any free port)'
                Check (Invoke-ById $client 'Start') 'Start invoked'
                $address = Wait-Text $client 'Address' 'http://' 90
                Check ($address -and $address.StartsWith('http://')) "the server listens (got '$address')"
                Check (Set-ById $client 'Inputs' '[41]') 'the inputs set to [41]'
                Check (Invoke-ById $client 'Send') 'Send invoked'
                $reply = Wait-Text $client 'Reply' 'HTTP 200' 120
                Check ($reply -and $reply.Contains('HTTP 200') -and $reply.Contains('42')) "the reply is HTTP 200 with 42 (got '$reply')"
                Check (Save-Picture $client (Join-Path $WorkRoot 'server.png')) 'a picture of the test client was saved'
                Check (Invoke-ById $client 'Stop') 'Stop invoked'
                Start-Sleep -Seconds 1
                $leftover = Get-Process -Name 'jgraph-mps' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $p.StartTime }
                Check ($null -eq $leftover) 'no jgraph-mps process is left'
            }
        }
    }
    finally { Stop-Leftover $p; Get-Process -Name 'jgraph-mps' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-10) } | Stop-Process -Force -ErrorAction SilentlyContinue }
}

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
