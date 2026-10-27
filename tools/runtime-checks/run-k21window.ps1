<#
.SYNOPSIS
    Stage K21's window check (compiler and SDK plan): a legacy deployment project given to deploytool in
    the Release build of JGraph.Application, driven through UI Automation patterns.

.DESCRIPTION
    A <deployment-project plugin="plugin.ezdeploy"> standalone project of R2024b's kind is written by
    this script (a main file, an author, a version, a summary). Then JGraph.Application starts with
    -r "deploytool('<file>')": the project upgrades as R2025b upgrades one (the file renamed .bak, its
    folder a MATLAB project named after it) and the Standalone Application Compiler window comes up
    over the task "<name> Standalone" with the legacy main file and author; Show Code shows the
    compiler.build script with the legacy for_testing folder as the output folder; Build ends in
    "Process completed successfully" with the program under that folder. A picture of the window is
    saved in the work folder. Every control is reached by its automation id; the app is ended with
    Stop-Process, so nothing is saved.

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
public static class K21Native {
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
    # process opens after the client first read it (the packaging dialog).
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [K21Native]::Windows([uint32] $processId)) {
            if ($entry.Value -eq $name) { return $A::FromHandle($entry.Key) }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of the process: {0}" -f (([K21Native]::Windows([uint32] $processId) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
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
    $rect = New-Object K21Native+RECT
    [void] [K21Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [K21Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Stop-Leftover($process) {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}

# The legacy project: written here as the Application Compiler of R2024b wrote one.
$root = Join-Path $WorkRoot 'k21legacy'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Force $root | Out-Null
$main = Join-Path $root 'k21win_hello.m'
[System.IO.File]::WriteAllText($main, "function y = k21win_hello(x)`n%K21WIN_HELLO Adds one.`nif nargin < 1, x = 0; end`ny = x + 1;`nfprintf('k21win_hello %g\n', y);`nend`n")
$prj = Join-Path $root 'k21win.prj'
$xml = @'
<?xml version="1.0" encoding="UTF-8"?>
<deployment-project plugin="plugin.ezdeploy" plugin-version="1.0">
  <configuration build-checksum="1" file="__PRJ__" location="__ROOT__" name="k21win" target="target.ezdeploy.standalone" target-name="Application Compiler">
    <param.appname>k21win</param.appname>
    <param.icon />
    <param.icons />
    <param.version>1.2</param.version>
    <param.authnamewatermark>K21 Author</param.authnamewatermark>
    <param.email />
    <param.company />
    <param.summary>Adds one</param.summary>
    <param.description />
    <param.screenshot />
    <param.guid />
    <param.installpath.string>\k21win\</param.installpath.string>
    <param.installpath.combo>option.installpath.programfiles</param.installpath.combo>
    <param.logo />
    <param.install.notes />
    <param.target.install.notes />
    <param.intermediate>${PROJECT_ROOT}\k21win\for_testing</param.intermediate>
    <param.files.only>${PROJECT_ROOT}\k21win\for_redistribution_files_only</param.files.only>
    <param.output>${PROJECT_ROOT}\k21win\for_redistribution</param.output>
    <param.logdir>${PROJECT_ROOT}\k21win</param.logdir>
    <param.enable.clean.build>false</param.enable.clean.build>
    <param.user.defined.mcr.options />
    <param.target.type>subtarget.standalone</param.target.type>
    <param.support.packages />
    <param.web.mcr>true</param.web.mcr>
    <param.package.mcr>false</param.package.mcr>
    <param.no.mcr>false</param.no.mcr>
    <param.web.mcr.name>MyAppInstaller_web</param.web.mcr.name>
    <param.package.mcr.name>MyAppInstaller_mcr</param.package.mcr.name>
    <param.no.mcr.name>MyAppInstaller_app</param.no.mcr.name>
    <param.windows.command.prompt>false</param.windows.command.prompt>
    <param.create.log>false</param.create.log>
    <param.log.file />
    <param.native.matlab>false</param.native.matlab>
    <param.checkbox>false</param.checkbox>
    <param.example />
    <param.help.text />
    <unset>
      <param.icon />
      <param.icons />
      <param.email />
      <param.company />
      <param.description />
      <param.screenshot />
      <param.guid />
      <param.logo />
      <param.install.notes />
      <param.target.install.notes />
      <param.user.defined.mcr.options />
      <param.support.packages />
      <param.log.file />
      <param.example />
      <param.help.text />
    </unset>
    <fileset.main>
      <file>${PROJECT_ROOT}\k21win_hello.m</file>
    </fileset.main>
    <fileset.resources />
    <fileset.package />
    <fileset.depfun />
    <build-deliverables />
    <workflow />
    <matlab>
      <root>C:\Program Files\MATLAB\R2025b</root>
      <toolboxes />
    </matlab>
    <platform>
      <unix>false</unix>
      <mac>false</mac>
      <windows>true</windows>
      <win2k>false</win2k>
      <winxp>false</winxp>
      <vista>false</vista>
      <linux>false</linux>
      <solaris>false</solaris>
      <osver>10.0</osver>
      <os32>false</os32>
      <os64>true</os64>
      <arch>win64</arch>
      <matlab>true</matlab>
    </platform>
  </configuration>
</deployment-project>
'@
[System.IO.File]::WriteAllText($prj, $xml.Replace('__PRJ__', $prj).Replace('__ROOT__', $root))
$prjForward = $prj.Replace('\', '/')

Write-Host "== deploytool over a legacy project"
$p = Start-Process -FilePath $exe -ArgumentList @('-r', "`"deploytool('$prjForward')`"") -WorkingDirectory $root -PassThru
$null = $p.Handle
try {
    $window = Find-Window $p.Id 'Standalone Application Compiler - k21win Standalone'
    Check ($null -ne $window) "a window titled 'Standalone Application Compiler - k21win Standalone'"
    Check (Test-Path ($prj + '.bak')) 'the legacy file was renamed .bak'
    Check (Test-Path (Join-Path $root 'resources\project\Project.xml')) 'the folder became a MATLAB project'
    Check ((Get-Content $prj -Raw).Contains('<MATLABProject')) 'the project''s own .prj took the legacy file''s name'
    $configFiles = Get-ChildItem (Join-Path $root 'resources\project\root') -Filter '*d.xml' | Where-Object { (Get-Content $_.FullName -Raw).Contains('StandaloneAppConfigModel') }
    Check ($configFiles.Count -eq 1) 'the task is one definition file under resources\project\root'
    Check (($configFiles.Count -eq 1) -and (Get-Content $configFiles[0].FullName -Raw).Contains('authorName&quot;:&quot;K21 Author')) 'the legacy author is in the task'
    if ($window) {
        Start-Sleep -Seconds 2
        $mainText = Text-Of (Find-ById $window 'MainFile')
        Check ($mainText -eq $main) "the main file is $main (got '$mainText')"
        $author = Text-Of (Find-ById $window 'AuthorName')
        Check ($author -eq 'K21 Author') "the author is 'K21 Author' (got '$author')"
        $status = Text-Of (Find-ById $window 'Status')
        Check ($status -eq 'The compiler task is ready') "the status says the task is ready (got '$status')"
        Check (Toggle-ById $window 'ShowCode') 'Show Code toggled'
        $code = Wait-Text $window 'Code' 'compiler.build.standaloneApplication(buildOpts)' 10
        Check ($code -and $code.Contains('buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(projectRoot, "k21win_hello.m"));')) 'the code view shows the build options over the legacy main file'
        Check ($code -and $code.Contains('buildOpts.OutputDir = fullfile(projectRoot, "k21win", "for_testing");')) 'the code view puts the output under the legacy for_testing folder'
        Check ($code -and $code.Contains('buildOpts.ExecutableName = "k21win";')) 'the executable is named after the legacy application'
        Check ($code -and $code.Contains('packageOpts.AuthorName = "K21 Author";')) 'the code view carries the legacy author'
        Check (Invoke-ById $window 'Build') 'Build invoked'
        $dialogRoot = Find-Window $p.Id 'Standalone App Packaging Progress'
        Check ($null -ne $dialogRoot) "the packaging dialog 'Standalone App Packaging Progress' opened"
        if ($dialogRoot) {
            $result = Wait-Text $dialogRoot 'PackagingResult' 'Process completed' 300
            Check ($result -eq 'Process completed successfully') "the build ended with 'Process completed successfully' (got '$result')"
            $built = Join-Path $root 'k21win\for_testing\k21win.exe'
            Check (Test-Path $built) "the program was built at $built"
            Check (Save-Picture $window (Join-Path $WorkRoot 'legacy.png')) 'a picture of the app window was saved'
            [void] (Invoke-ById $dialogRoot 'ClosePackaging')
        }
    }
}
finally { Stop-Leftover $p }

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
