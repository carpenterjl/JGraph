<#
.SYNOPSIS
    Stage K17g's Excel checks (compiler and SDK plan): the Excel add-in and the Excel client for the
    Production Server as JGraph builds them, exercised where this machine lets them be, with the
    registry put back as it was. Nothing is saved, nothing is elevated, nothing is typed into any
    window (UI Automation patterns only).

.DESCRIPTION
    addin   jgraph -batch builds the K17 functions (tools\matlab-checklist\compiler-probes\src\k17) into
            an add-in with its module and workbook (compiler.build.excelAddIn ... GenerateVisualBasicFile
            on), which registers it for the current user. The add-in is created by its ProgID in this
            process: k17_one(3) answers 4, k17_count counts, k17_err lands an error with the add-in's
            source, and k17_plot('on') raises a JGraph figure window in this process, found by its
            title, pictured with PrintWindow into <WorkRoot>\addin-figure.png and closed through
            WindowPattern.Close. Then an invisible Excel (DisplayAlerts off) opens the workbook as an
            add-in and calculates =k17_one(3) in a scratch workbook: Excel 16 installed by Click-to-Run
            sees no COM registration written under the user's hive since it was set up (stage K17e,
            open item 248), so the cell is expected to hold the module's refusal text, "Error in
            VBAProject: ActiveX component can't create object", exactly as run-k17.ps1's cells step
            recorded it; a number there would mean the registry view had changed and is reported.
            The workbooks are closed unsaved, Excel quits, the add-in is unregistered.
    client  jgraph -batch builds the acceptance set's library (src\k22) into a Production Server archive,
            the Excel client for it (compiler.build.excelClientForProductionServer) and the client's
            installer (compiler.package.excelClientForProductionServer, ServerURL this machine). jgraph-mps
            serves the archive on -Port. The installer is run for the current user with /quiet, /dir and
            /url: the files unpacked, the client and JGraph's server configuration add-in registered, the
            settings written under HKCU\Software\JGraph\Excel Client\<client>, the OPEN entry for the
            workbook under Excel's Options key, the Apps & Features entry. The client is created by its
            ProgID in this process and calls the server (acc_add, acc_greet, acc_count, a failure as
            text). An invisible Excel opens the installed workbook and calculates =acc_add(2,3) (the same
            registry view: the refusal text is expected). Then the installer runs with /uninstall /quiet:
            the settings, the registrations, the OPEN entry, the Apps & Features entry and the folder
            are gone. jgraph-mps is stopped.

    At the end the registry's names under the user's hive are compared with the snapshot taken first:
    nothing added, nothing removed, is the check (JGraph's component category, which the first add-in
    build makes, is reported when it is new).

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1: its bin\win64 holds jgraph.exe,
    jgraph-mps.exe, jgrt.dll, the engine and the window host JGraph.Runtime.Desktop, which the add-in
    needs to show a figure (the CLI's Debug folder has no window host, so a figure stays invisible
    there). Recommended; -Jgraph and -JgraphMps override its two programs.

.PARAMETER Jgraph
    The jgraph.exe that builds; the runtime's, else the CLI's Debug build. The add-in and the client
    find jgrt.dll, the engine and the window host in its folder, which this script puts first on the PATH.

.PARAMETER JgraphMps
    jgraph-mps.exe; the runtime's, else the Production Server's Debug build.

.PARAMETER Runs
    A comma-separated list of 'addin' and 'client'; both by default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $JgraphMps,
    [string] $Runs = 'addin,client',
    [int] $Port = 9975,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class K17ExcelNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    // Unloads the COM DLLs of this process that say they may go (DllCanUnloadNow), so an uninstall can delete them.
    [DllImport("ole32.dll")] public static extern void CoFreeUnusedLibrariesEx(uint delay, uint reserved);
    // Every visible top-level window of a process with its title, by Win32 enumeration: the UI
    // Automation root does not list a window a process opens after the client first read it.
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
$scriptStart = Get-Date
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
if ($Runtime) {
    $Runtime = (Resolve-Path -LiteralPath $Runtime).Path
    if (-not $Jgraph) { $Jgraph = Join-Path $Runtime 'bin\win64\jgraph.exe' }
    if (-not $JgraphMps) { $JgraphMps = Join-Path $Runtime 'bin\win64\jgraph-mps.exe' }
    $env:JGRAPH_RUNTIME_ROOT = $Runtime
}
if (-not $Jgraph) { $Jgraph = Join-Path $repo 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
if (-not (Test-Path $Jgraph)) { throw "Build src\JGraph.Cli first, or give -Runtime: $Jgraph" }
$Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
if (-not $JgraphMps) { $JgraphMps = Join-Path $repo 'src\JGraph.ProductionServer\bin\Debug\net8.0\jgraph-mps.exe' }
$hasDesktop = Test-Path (Join-Path (Split-Path $Jgraph) 'JGraph.Runtime.Desktop.dll')
$jgraphFolder = Split-Path $Jgraph
$env:PATH = "$jgraphFolder;$env:PATH"
$A = [System.Windows.Automation.AutomationElement]
$utf8 = New-Object System.Text.UTF8Encoding($false)
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$failures = New-Object System.Collections.Generic.List[string]
$regsvr = Join-Path $env:SystemRoot 'System32\regsvr32.exe'
$slash = { param($p) $p.Replace('\', '/') }

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}
function Note([string] $what) { Write-Host ("  {0,-4} {1}" -f 'note', $what) }

# The user's registry by the names under each root, snapshotted first and compared last.
$regRoots = @('Software\Classes', 'Software\Classes\CLSID', 'Software\Classes\TypeLib', 'Software\Classes\Interface',
    'Software\Classes\Component Categories', 'Software\JGraph', 'Software\JGraph\Excel Client', 'Software\Microsoft\Office\Excel\Addins',
    'Software\Microsoft\Windows\CurrentVersion\Uninstall')
function Reg-Names() {
    $names = @()
    foreach ($root in $regRoots) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($root)
        if ($key) { foreach ($n in ($key.GetSubKeyNames() | Sort-Object)) { $names += "$root\$n" }; $key.Close() }
    }
    $names
}
function Reg-Value([string] $key, [string] $name) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
    if (-not $k) { return $null }
    try { return $k.GetValue($name) } finally { $k.Close() }
}
function Reg-Exists([string] $key) { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key); if ($k) { $k.Close(); $true } else { $false } }
# Excel's OPEN entries (OPEN, OPEN1, ...) under every Options key of an installed Excel, as the Add-Ins dialog writes them.
function Open-Entries() {
    $found = @()
    $office = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Office')
    if (-not $office) { return $found }
    foreach ($v in $office.GetSubKeyNames()) {
        if ($v -notmatch '^\d') { continue }
        $options = $office.OpenSubKey("$v\Excel\Options")
        if (-not $options) { continue }
        foreach ($n in $options.GetValueNames()) { if ($n -match '^OPEN\d*$') { $found += ('{0}\Excel\Options\{1}={2}' -f $v, $n, $options.GetValue($n)) } }
        $options.Close()
    }
    $office.Close()
    $found
}
$before = @(Reg-Names)
$openBefore = @(Open-Entries)
# A build registers JGraph's COM utility library for the current user when nothing has (decision KO10);
# what this run's build registers, this script unregisters, through the DLL the registration names.
$utilityKey = 'HKCU:\Software\Classes\JGraphComUtil.MWUtil25.2'
$utilityWasRegistered = (Test-Path $utilityKey) -or (Test-Path 'HKLM:\Software\Classes\JGraphComUtil.MWUtil25.2')
function Unregister-Utility() {
    if ($utilityWasRegistered -or -not (Test-Path $utilityKey)) { return }
    $clsid = (Get-ItemProperty "$utilityKey\CLSID" -ErrorAction SilentlyContinue).'(default)'
    $path = if ($clsid) { (Get-ItemProperty "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue).'(default)' } else { $null }
    if ($path -and (Test-Path $path)) { Start-Process -FilePath $regsvr -ArgumentList @('/s', '/u', "`"$path`"") -NoNewWindow -Wait; Note "the build registered the utility library ($path); unregistered" }
}
Write-Host ("registry before: {0} names, {1} OPEN entries" -f $before.Count, $openBefore.Count)

function Run-Jgraph([string] $statement, [string] $label, [int] $timeout = 900) {
    $file = Join-Path $WorkRoot "$label.m"
    [System.IO.File]::WriteAllText($file, $statement + "`n", $utf8)
    $out = Join-Path $WorkRoot "$label.out.txt"
    $err = Join-Path $WorkRoot "$label.err.txt"
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"run('$(& $slash $file)')`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit($timeout * 1000)) { Stop-Process -Id $p.Id -Force; Write-Host "  $label stopped after $timeout s" }
    $text = [System.IO.File]::ReadAllText($out) + [System.IO.File]::ReadAllText($err)
    Write-Host ("  {0,-12} exit {1}, {2} lines" -f $label, $p.ExitCode, ($text -split "`n").Count)
    $text
}

# A method of a COM object in PowerShell's direct syntax built as text (the path that keeps a
# two-dimensional array two-dimensional and surfaces the error text), with nargout and [ref] slots.
function Call($obj, [string] $name, [int] $nout, [object[]] $inputs) {
    $script:__obj = $obj
    $script:__nargout = $nout
    $parts = @()
    if ($nout -gt 0) {
        $parts += '$script:__nargout'
        for ($i = 0; $i -lt $nout; $i++) { Set-Variable -Name "__slot$i" -Scope Script -Value $null; $parts += "[ref]`$script:__slot$i" }
    }
    # -NoEnumerate: written plainly, a two-dimensional array would reach the slot as its elements.
    for ($j = 0; $j -lt $inputs.Count; $j++) { Set-Variable -Name "__in$j" -Scope Script -Value (Write-Output -NoEnumerate $inputs[$j]); $parts += "`$script:__in$j" }
    Invoke-Expression ('$script:__obj.' + $name + '(' + ($parts -join ', ') + ')') | Out-Null
    $outs = @(); for ($i = 0; $i -lt $nout; $i++) { $outs += , (Get-Variable -Name "__slot$i" -Scope Script -ValueOnly) }
    , $outs
}
# The COM objects this process made let go, and their DLLs unloaded, so an uninstall can delete them.
function Release-Com($obj) {
    if ($obj) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($obj) }
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    [K17ExcelNative]::CoFreeUnusedLibrariesEx(0, 0)
}
function Err($e) { $i = $e; while ($i.InnerException) { $i = $i.InnerException }; '0x{0:X8} src=[{1}] msg=[{2}]' -f $i.HResult, $i.Source, $i.Message }

function Find-OwnWindow([string] $title, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [K17ExcelNative]::Windows([uint32] $PID)) { if ($entry.Value -eq $title) { return $entry.Key } }
        Start-Sleep -Milliseconds 250
    }
    Write-Host ("    windows of this process: {0}" -f (([K17ExcelNative]::Windows([uint32] $PID) | ForEach-Object { "'" + $_.Value + "'" }) -join ', '))
    [IntPtr]::Zero
}
function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object K17ExcelNative+RECT
    [void] [K17ExcelNative]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [K17ExcelNative]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $ok
}

# An invisible Excel over an add-in workbook: the workbook opened as an add-in, one formula calculated
# in a scratch workbook, its Value2 and Text read, everything closed unsaved. Answers the rows.
$refusal = "Error in VBAProject: ActiveX component can't create object"
function Excel-Cell([string] $xlam, [string] $formula, [string] $procedure) {
    $rows = @{}
    $xl = $null; $addin = $null; $wb = $null
    try {
        $xl = New-Object -ComObject 'Excel.Application'
        $xl.Visible = $false
        $xl.DisplayAlerts = $false
        $rows.version = '{0} build {1}' -f $xl.Version, $xl.Build
        $addin = $xl.Workbooks.Open($xlam)
        $rows.isAddin = [bool] $addin.IsAddin
        $names = @()
        try {
            $comp = $addin.VBProject.VBComponents.Item($addin.VBProject.VBComponents.Count)
            $cm = $comp.CodeModule
            $line = $cm.CountOfDeclarationLines + 1
            while ($line -le $cm.CountOfLines) { $kind = 0; $name = $cm.ProcOfLine($line, [ref]$kind); if ($names -notcontains $name) { $names += $name }; $line = $cm.ProcStartLine($name, $kind) + $cm.ProcCountLines($name, $kind) }
        } catch { $rows.moduleError = Err $_.Exception }
        $rows.procedures = $names
        $rows.hasProcedure = $names -contains $procedure
        $wb = $xl.Workbooks.Add()
        $ws = $wb.Worksheets.Item(1)
        $ws.Range('A1').Formula = $formula
        $xl.Calculate()
        $rows.value = $ws.Range('A1').Value2
        $rows.text = [string] $ws.Range('A1').Text
        $comAddIns = @()
        try { foreach ($c in $xl.COMAddIns) { $comAddIns += ('{0} connect={1}' -f $c.ProgId, $c.Connect) } } catch { }
        $rows.comAddIns = $comAddIns
    }
    catch { $rows.error = Err $_.Exception }
    finally {
        try { if ($wb) { $wb.Close($false) } } catch { }
        try { if ($addin) { $addin.Close($false) } } catch { }
        try { if ($xl) { $xl.Quit() } } catch { }
        try { if ($xl) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($xl) } } catch { }
    }
    $rows
}

$selected = $Runs -split ','
$probes = Join-Path $repo 'tools\matlab-checklist\compiler-probes\src'
$built = Join-Path $WorkRoot 'b'

if ($selected -contains 'addin') {
    Write-Host '== addin'
    $src = Join-Path $WorkRoot 'src17'
    New-Item -ItemType Directory -Force $src | Out-Null
    Copy-Item (Join-Path $probes 'k17\*.m') $src -Force
    $out = Join-Path $built 'addin'
    $files = @(Get-ChildItem $src -Filter '*.m' | Sort-Object Name | ForEach-Object { "'" + (& $slash $_.FullName) + "'" }) -join ', '
    $text = Run-Jgraph "r = compiler.build.excelAddIn({$files}, 'AddInName', 'k17gxl', 'ClassName', 'K17gXl', 'GenerateVisualBasicFile', 'on', 'OutputDir', '$(& $slash $out)'); disp(strjoin(r.Files, ' | '));" 'addin_build'
    $dll = Join-Path $out 'k17gxl_1_0.dll'
    Check (Test-Path $dll) "the add-in was built: $dll"
    Check (Test-Path (Join-Path $out 'k17gxl.bas')) 'the module k17gxl.bas was written'
    $xlam = Join-Path $out 'k17gxl.xlam'
    Check (Test-Path $xlam) 'the add-in workbook k17gxl.xlam was written'
    Check (Reg-Exists 'Software\Classes\k17gxl.K17gXl.1_0') 'the build registered k17gxl.K17gXl.1_0 for the current user'
    $obj = $null
    try {
        try { $obj = New-Object -ComObject 'k17gxl.K17gXl.1_0' } catch { Write-Host ("    create: {0}" -f (Err $_.Exception)) }
        Check ($null -ne $obj) 'the add-in is created by its ProgID in this process'
        if ($obj) {
            $r = Call $obj 'k17_one' 1 @(3.0)
            Check ($r[0] -is [double] -and $r[0] -eq 6) "k17_one(3) answers 6, its double (got $($r[0]))"
            $c1 = (Call $obj 'k17_count' 1 @())[0]; $c2 = (Call $obj 'k17_count' 1 @())[0]
            Check ($c1 -eq 1 -and $c2 -eq 2) "k17_count counts 1, 2 (got $c1, $c2)"
            $errText = ''
            try { [void](Call $obj 'k17_err' 1 @('id')) } catch { $errText = Err $_.Exception }
            Check ($errText.Contains('src=[k17gxl.K17gXl.1_0]') -and $errText.Contains('Custom text with an identifier')) "k17_err('id') fails with the add-in's source and R2025b's description (got $errText)"
            $r = Call $obj 'k17_plot' 1 @('on')
            Check ($r[0] -eq 1) "k17_plot('on') answers 1 figure (got $($r[0]))"
            if (-not $hasDesktop) { Note "no JGraph.Runtime.Desktop.dll beside ${Jgraph}: the figure has no window host here (give -Runtime)" }
            $hwnd = Find-OwnWindow 'Figure 1'
            Check ($hwnd -ne [IntPtr]::Zero) "the figure's window 'Figure 1' is up in this process"
            if ($hwnd -ne [IntPtr]::Zero) {
                Start-Sleep -Seconds 1
                Check (Save-Picture $hwnd (Join-Path $WorkRoot 'addin-figure.png')) 'a picture of the figure window was saved'
                $element = $A::FromHandle($hwnd)
                $element.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                $deadline = (Get-Date).AddSeconds(15)
                while ((Get-Date) -lt $deadline -and ([K17ExcelNative]::Windows([uint32] $PID) | Where-Object { $_.Key -eq $hwnd })) { Start-Sleep -Milliseconds 250 }
                Check (-not ([K17ExcelNative]::Windows([uint32] $PID) | Where-Object { $_.Key -eq $hwnd })) 'the figure window closed through WindowPattern.Close'
            }
            $after = (Call $obj 'k17_plot' 1 @('off'))[0]
            Check ($after -eq 1) "an invisible figure after the close: 1 figure exists (got $after)"
        }
    }
    finally { Release-Com $obj }

    # Excel's own cells over the workbook.
    $rows = Excel-Cell $xlam '=k17_one(3)' 'k17_one'
    if ($rows.error) { Write-Host ("    excel: {0}" -f $rows.error) }
    Check ($rows.isAddin -eq $true) "Excel $($rows.version) opened k17gxl.xlam as an add-in"
    if ($rows.moduleError) { Note "the module's procedures cannot be read: Excel does not trust programmatic access to the VBA project (AccessVBOM unset, never changed), as K17a and K17e found" }
    else { Check ($rows.hasProcedure -eq $true) "its module lists k17_one ($(@($rows.procedures).Count) procedures)" }
    if ($rows.value -is [double] -and $rows.value -eq 4) {
        Check $true '=k17_one(3) in a cell answers 4: Excel sees the per-user registration (new; open item 248 is then closed)'
    } else {
        Check ($rows.text -eq $refusal) "=k17_one(3) in a cell holds the module's refusal as recorded (open item 248): '$($rows.text)'"
    }
    $r = Start-Process -FilePath $regsvr -ArgumentList @('/s', '/u', "`"$dll`"") -NoNewWindow -PassThru -Wait
    Check ($r.ExitCode -eq 0 -and -not (Reg-Exists 'Software\Classes\k17gxl.K17gXl.1_0')) "the add-in is unregistered (regsvr32 /u exit $($r.ExitCode))"
}

if ($selected -contains 'client') {
    Write-Host '== client'
    $src = Join-Path $WorkRoot 'src22'
    New-Item -ItemType Directory -Force $src | Out-Null
    Copy-Item (Join-Path $probes 'k22\acc_*.m') $src -Force
    $lib = @('acc_add', 'acc_stats', 'acc_greet', 'acc_scale', 'acc_count', 'acc_fail', 'acc_quiet' | ForEach-Object { "'" + (& $slash (Join-Path $src "$_.m")) + "'" }) -join ', '
    $server = Join-Path $built 'server'; $client = Join-Path $built 'client'; $installerOut = Join-Path $built 'installer'
    $text = Run-Jgraph ("rs = compiler.build.productionServerArchive({$lib}, 'ArchiveName', 'accarch', 'OutputDir', '$(& $slash $server)');`n" +
        "rc = compiler.build.excelClientForProductionServer(rs, 'AddInName', 'k17gxc', 'ClassName', 'K17gXc', 'OutputDir', '$(& $slash $client)');`n" +
        "compiler.package.excelClientForProductionServer(rc, 'ServerURL', 'http://localhost:$Port', 'OutputDir', '$(& $slash $installerOut)');`n" +
        "disp(strjoin(rc.Files, ' | '));") 'client_build'
    $archive = Join-Path $server 'accarch.ctf'
    Check (Test-Path $archive) "the archive was built: $archive"
    Check (Test-Path (Join-Path $client 'k17gxc.dll')) 'the client k17gxc.dll was built'
    $installer = Join-Path $installerOut 'k17gxcExcelClientInstaller.exe'
    Check (Test-Path $installer) "the installer was packaged: $installer"

    $mps = $null; $inst = Join-Path $WorkRoot 'mps'
    $folder = Join-Path $WorkRoot 'installed\k17gxc'
    $url = "http://localhost:$Port/accarch"
    try {
        if (Test-Path $archive) {
            if (Test-Path $inst) { Remove-Item -LiteralPath $inst -Recurse -Force }
            & $JgraphMps new $inst --port $Port | Out-Null
            Copy-Item $archive (Join-Path $inst 'auto_deploy\accarch.ctf')
            $log = Join-Path $WorkRoot 'mps.log'
            $mps = Start-Process -FilePath $JgraphMps -ArgumentList @('run', "`"$inst`"") -RedirectStandardOutput $log -RedirectStandardError (Join-Path $WorkRoot 'mps.err') -NoNewWindow -PassThru
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $ready = $false
            while (-not $ready -and $watch.Elapsed.TotalSeconds -lt $TimeoutSeconds -and -not $mps.HasExited) {
                Start-Sleep -Milliseconds 500
                $t = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
                $ready = $t -and $t.Contains('accarch: deployed')
            }
            Check $ready "jgraph-mps serves accarch on port $Port"
        }
        if (Test-Path $installer) {
            # The installer, for the current user, into a folder of this script's, pointed at the server.
            $p = Start-Process -FilePath $installer -ArgumentList @('/quiet', '/dir', "`"$folder`"", '/url', $url) -PassThru
            $null = $p.Handle
            if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $p.Id -Force }
            Check ($p.ExitCode -eq 0) "the installer ran with /quiet /dir /url (exit $($p.ExitCode))"
            foreach ($f in @('k17gxc.dll', 'k17gxc.bas', 'k17gxc.xlam', 'jgxlconfig.dll', 'k17gxcExcelClientInstaller.exe')) {
                Check (Test-Path (Join-Path $folder $f)) "the installed folder holds $f"
            }
            $settings = 'Software\JGraph\Excel Client\k17gxc'
            Check ((Reg-Value $settings 'ServerURL') -eq $url) "the settings hold ServerURL $url (got '$(Reg-Value $settings 'ServerURL')')"
            Check ((Reg-Value $settings 'timeoutSeconds') -eq 60 -and (Reg-Value $settings 'responseSizeLimit') -eq 750) "timeoutSeconds 60 and responseSizeLimit 750 as packaged (got $(Reg-Value $settings 'timeoutSeconds'), $(Reg-Value $settings 'responseSizeLimit'))"
            Check (Reg-Exists 'Software\Classes\k17gxc.K17gXc') 'the client is registered (k17gxc.K17gXc)'
            Check (Reg-Exists 'Software\Classes\JGraph.ServerConfig') "the server configuration add-in is registered (JGraph.ServerConfig)"
            Check ((Reg-Value 'Software\Microsoft\Office\Excel\Addins\JGraph.ServerConfig' 'LoadBehavior') -eq 3) "Excel's COM add-in entry for it has LoadBehavior 3"
            $wanted = '/R "' + (Join-Path $folder 'k17gxc.xlam') + '"'
            $opens = @(Open-Entries | Where-Object { $_.EndsWith('=' + $wanted) })
            Check ($opens.Count -ge 1) "an OPEN entry under Excel's Options key names the workbook ($($opens -join '; '))"
            $uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\JGraphExcelClient_k17gxc'
            Check ((Reg-Value $uninstallKey 'DisplayName') -eq 'k17gxc Excel client (JGraph)') "the Apps & Features entry says 'k17gxc Excel client (JGraph)' (got '$(Reg-Value $uninstallKey 'DisplayName')')"

            # The installed client from this process, against the server.
            $obj = $null
            try {
                try { $obj = New-Object -ComObject 'k17gxc.K17gXc' } catch { Write-Host ("    create: {0}" -f (Err $_.Exception)) }
                Check ($null -ne $obj) 'the client is created by its ProgID in this process'
                if ($obj) {
                    $r = (Call $obj 'acc_add' 1 @(2.0, 3.0))[0]
                    Check ($r -is [double] -and $r -eq 5) "acc_add(2, 3) over the server answers 5 (got $r)"
                    $r = (Call $obj 'acc_greet' 1 @('Ada'))[0]
                    Check ($r -eq 'Hello, Ada!') "acc_greet('Ada') answers 'Hello, Ada!' (got '$r')"
                    $c1 = (Call $obj 'acc_count' 1 @())[0]; $c2 = (Call $obj 'acc_count' 1 @())[0]
                    Check ($c2 -eq $c1 + 1) "acc_count counts on the server ($c1, $c2)"
                    $r = (Call $obj 'acc_add' 1 @(2.0))[0]
                    Check (([string]$r).StartsWith('MATLABException:MATLAB:minrhs')) "a missing argument lands as text in the cell (got '$r')"
                    $m = New-Object 'double[,]' 2, 2; $m[0, 0] = 1; $m[0, 1] = 2; $m[1, 0] = 3; $m[1, 1] = 4
                    $r = (Call $obj 'acc_scale' 1 @($m, 2.0))[0]
                    Check (($r -is [array]) -and $r.Rank -eq 2 -and $r[1, 1] -eq 8) "acc_scale of a 2-by-2 answers a two-dimensional array (got $($r.GetType().Name))"
                }
            }
            finally { Release-Com $obj }

            # Excel's own cells over the installed workbook, and its view of the configuration add-in.
            $rows = Excel-Cell (Join-Path $folder 'k17gxc.xlam') '=acc_add(2,3)' 'acc_add'
            if ($rows.error) { Write-Host ("    excel: {0}" -f $rows.error) }
            Check ($rows.isAddin -eq $true) "Excel $($rows.version) opened the installed k17gxc.xlam as an add-in"
            if ($rows.moduleError) { Note "the module's procedures cannot be read (AccessVBOM unset, as above)" }
            else { Check ($rows.hasProcedure -eq $true) "its module lists acc_add ($(@($rows.procedures).Count) procedures)" }
            if ($rows.value -is [double] -and $rows.value -eq 5) {
                Check $true '=acc_add(2,3) in a cell answers 5: Excel sees the per-user registration (new; open item 249 is then closed)'
            } else {
                Check ($rows.text -eq $refusal) "=acc_add(2,3) in a cell holds the module's refusal as recorded (open item 249): '$($rows.text)'"
            }
            $seen = @($rows.comAddIns | Where-Object { $_ -like 'JGraph.ServerConfig*' })
            Note ("Excel's COMAddIns: {0} listed, JGraph.ServerConfig {1}" -f @($rows.comAddIns).Count, $(if ($seen.Count) { $seen[0] } else { 'not among them (the per-user Addins entry is behind the same registry view)' }))

            # Uninstall, quietly. The client's DLL was loaded into this process by the calls above and is let
            # go first (Release-Com), as a user would close Excel first: a DLL still in use is left behind.
            Release-Com $null
            $kept = Join-Path $folder 'k17gxcExcelClientInstaller.exe'
            $p = Start-Process -FilePath $kept -ArgumentList @('/uninstall', '/quiet') -PassThru
            $null = $p.Handle
            if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $p.Id -Force }
            Check ($p.ExitCode -eq 0) "the installer ran with /uninstall /quiet (exit $($p.ExitCode))"
            Check (-not (Reg-Exists $settings)) 'the settings key is gone'
            Check (-not (Reg-Exists 'Software\Classes\k17gxc.K17gXc')) 'the client is unregistered'
            Check (-not (Reg-Exists 'Software\Classes\JGraph.ServerConfig') -and -not (Reg-Exists 'Software\Microsoft\Office\Excel\Addins\JGraph.ServerConfig')) 'the server configuration add-in and its Excel entry are gone'
            Check (@(Open-Entries | Where-Object { $_.EndsWith('=' + $wanted) }).Count -eq 0) 'the OPEN entry is gone'
            Check (-not (Reg-Exists $uninstallKey)) 'the Apps & Features entry is gone'
            $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline -and (Test-Path $folder)) { Start-Sleep -Milliseconds 500 }
            Check (-not (Test-Path $folder)) "the installed folder is gone ($folder)"
        }
    }
    finally {
        if ($mps) { try { & $JgraphMps stop $inst | Out-Null } catch { }; try { if (-not $mps.HasExited) { $mps.WaitForExit(10000) | Out-Null } } catch { }; if (-not $mps.HasExited) { Stop-Process -Id $mps.Id -Force -ErrorAction SilentlyContinue } }
        # The build's own registration of the client (the same keys the installer's copy wrote and removed).
        if (Test-Path (Join-Path $client 'k17gxc.dll')) { Start-Process -FilePath $regsvr -ArgumentList @('/s', '/u', "`"$(Join-Path $client 'k17gxc.dll')`"") -NoNewWindow -Wait }
    }
}

# The registry after: the one before. An invisible Excel this script could not quit is ended.
Unregister-Utility
$excels = @(Get-Process excel -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -eq 0 -and $_.StartTime -gt $scriptStart })
foreach ($e in $excels) { try { Stop-Process -Id $e.Id -Force; Note "an invisible Excel of this run ($($e.Id)) was ended" } catch { } }
$after = @(Reg-Names)
$added = @($after | Where-Object { $before -notcontains $_ })
$removed = @($before | Where-Object { $after -notcontains $_ })
$category = @($added | Where-Object { $_ -like 'Software\Classes\Component Categories\*' })
if ($category.Count -and $added.Count -eq $category.Count) { Note "JGraph's component category is new under the user's hive: $($category -join ', ')" }
Check (($added.Count -eq $category.Count) -and $removed.Count -eq 0) "the registry after is the one before (+$($added.Count) -$($removed.Count)$(if ($added.Count) { ': ' + ($added -join ', ') })$(if ($removed.Count) { ': ' + ($removed -join ', ') }))"
Check (@(Open-Entries).Count -eq $openBefore.Count) "Excel's OPEN entries are as before ($(@(Open-Entries).Count))"

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
