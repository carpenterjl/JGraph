<#
.SYNOPSIS
    Stage K20's window check (compiler and SDK plan): the Compiler Task Manager and a compiler app over
    a project's task, in the Release build of JGraph.Application, driven through UI Automation patterns.

.DESCRIPTION
    A project with a standalone application task is made first by the Debug jgraph CLI (-batch), the
    task's main file set from the prompt. Then JGraph.Application starts with
    -r "openProject('<root>'); standaloneApplicationCompiler": with a project open the command brings up
    the Compiler Task Manager, whose list carries the task; Open brings up the Standalone Application
    Compiler window over it, with the main file and the task's name in its title; an author typed into
    the window lands in the project's definition files at once; Show Code shows the compiler.build
    script with the task's output folder; Build ends in "Process completed successfully" with the
    program under <root>\myApp\output\build. New Task in the manager makes a second task in the
    project and opens its window. A picture of the manager and of the app window is saved in the work
    folder. Every control is reached by its automation id; the app is ended with Stop-Process, so
    nothing is saved.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Configuration
    The build of JGraph.Application to run; Release by default.
#>
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
public static class K20Native {
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
        foreach ($entry in [K20Native]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of the process: {0}" -f (([K20Native]::Windows([uint32] $processId) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
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
    $rect = New-Object K20Native+RECT
    [void] [K20Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [K20Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Stop-Leftover($process) {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}

# The project: made headless by the CLI, with a standalone task whose main file and executable name are set.
$cli = Join-Path $repo 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe'
if (-not (Test-Path $cli)) { throw "Build src\JGraph.Cli in Debug first: $cli" }
$root = Join-Path $WorkRoot 'k20proj'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Force $root | Out-Null
$main = Join-Path $root 'k20win_hello.m'
[System.IO.File]::WriteAllText($main, "function y = k20win_hello(x)`n%K20WIN_HELLO Adds one.`nif nargin < 1, x = 0; end`ny = x + 1;`nfprintf('k20win_hello %g\n', y);`nend`n")
$rootForward = $root.Replace('\', '/')
$prep = Join-Path $WorkRoot 'prep.m'
[System.IO.File]::WriteAllText($prep, "proj = matlab.project.createProject('$rootForward');`naddFile(proj, 'k20win_hello.m');`nt = compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myApp');`nt.MainFile = 'k20win_hello.m';`nt.ExecutableName = 'k20win_hello';`nclose(proj);`ndisp('prepared');`n")
$prepOut = & $cli -batch "run('$($prep.Replace('\', '/'))')" 2>&1
Check (($prepOut | Out-String).Contains('prepared')) "the project and its task were made by the CLI ($($prepOut | Out-String))"
$configFiles = Get-ChildItem (Join-Path $root 'resources\project\root') -Filter '*d.xml' | Where-Object { (Get-Content $_.FullName -Raw).Contains('StandaloneAppConfigModel') }
Check ($configFiles.Count -eq 1) 'the task is one definition file under resources\project\root'

Write-Host "== task manager and app"
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"openProject('$rootForward'); standaloneApplicationCompiler`"") -WorkingDirectory $root -PassThru
$null = $p.Handle
try {
    $manager = Find-Window $p.Id 'Compiler Task Manager'
    Check ($null -ne $manager) "a window titled 'Compiler Task Manager'"
    if ($manager) {
        Start-Sleep -Seconds 2
        $heading = Text-Of (Find-ById $manager 'Heading')
        Check ($heading -eq 'Tasks in blank_project') "the heading is 'Tasks in blank_project' (got '$heading')"
        $note = Text-Of (Find-ById $manager 'Note')
        Check ($note -and $note.StartsWith('The following tasks exist in the open project')) "the note says tasks exist (got '$note')"
        $item = Find-ById $manager 'Task.myApp' 5
        Check ($null -ne $item) 'the list carries Task.myApp'
        Check (Invoke-ById $manager 'OpenTask') 'Open invoked'
        $window = Find-Window $p.Id 'Standalone Application Compiler - myApp'
        Check ($null -ne $window) "a window titled 'Standalone Application Compiler - myApp'"
        if ($window) {
            Start-Sleep -Seconds 2
            $mainText = Text-Of (Find-ById $window 'MainFile')
            Check ($mainText -eq $main) "the main file is $main (got '$mainText')"
            $status = Text-Of (Find-ById $window 'Status')
            Check ($status -eq 'The compiler task is ready') "the status says the task is ready (got '$status')"
            Check (Set-ById $window 'AuthorName' 'K20 Author') 'an author typed into the window'
            Start-Sleep -Seconds 2
            $stored = (Get-Content $configFiles[0].FullName -Raw).Contains('authorName&quot;:&quot;K20 Author')
            Check $stored 'the author landed in the project''s definition file'
            Check (Toggle-ById $window 'ShowCode') 'Show Code toggled'
            $code = Wait-Text $window 'Code' 'compiler.build.standaloneApplication(buildOpts)' 10
            Check ($code -and $code.Contains('buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(projectRoot, "k20win_hello.m"));')) 'the code view shows the build options over the main file'
            Check ($code -and $code.Contains('buildOpts.OutputDir = fullfile(projectRoot, "myApp", "output", "build");')) 'the code view puts the output under the task''s folder'
            Check ($code -and $code.Contains('packageOpts.AuthorName = "K20 Author";')) 'the code view carries the author'
            Check (Invoke-ById $window 'Build') 'Build invoked'
            $dialogRoot = Find-Window $p.Id 'Standalone App Packaging Progress'
            Check ($null -ne $dialogRoot) "the packaging dialog 'Standalone App Packaging Progress' opened"
            if ($dialogRoot) {
                $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 300
                Check ($result -eq 'Process completed successfully') "the build ended with 'Process completed successfully' (got '$result')"
                $built = Join-Path $root 'myApp\output\build\k20win_hello.exe'
                Check (Test-Path $built) "the program was built at $built"
                Check (Save-Picture $window (Join-Path $WorkRoot 'task.png')) 'a picture of the app window was saved'
                [void] (Invoke-ById $dialogRoot 'ClosePackaging')
            }
        }

        # A second task made in the manager: its window opens, the project holds it.
        Check (Set-ById $manager 'NewTaskName' 'second') 'a name for a new task typed'
        Check (Invoke-ById $manager 'Create') 'Create invoked'
        $second = Find-Window $p.Id 'Standalone Application Compiler - second'
        Check ($null -ne $second) "a window titled 'Standalone Application Compiler - second'"
        Start-Sleep -Seconds 2
        Check ($null -ne (Find-ById $manager 'Task.second' 5)) 'the list carries Task.second'
        $map = Get-ChildItem (Join-Path $root 'resources\project\root') -Filter '*d.xml' | Where-Object { (Get-Content $_.FullName -Raw).Contains('configMapData') }
        Check (($map.Count -eq 1) -and (Get-Content $map[0].FullName -Raw).Contains('second')) 'the project''s task map names the second task'
        Check (Save-Picture $manager (Join-Path $WorkRoot 'manager.png')) 'a picture of the manager was saved'
    }
}
finally { Stop-Leftover $p }

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
