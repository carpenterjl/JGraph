<#
.SYNOPSIS
    How an Excel add-in reaches Excel's cells (compiler and SDK plan, stage K17e): the utility
    library and the component created from VBA the way the generated module creates them,
    CreateObject(progid, "localhost") (a DCOM activation, which reads the machine's registrations
    alone), and with no server name (in Excel's process), from VBA and from this script's own process;
    the registry as Excel sees it (the add-in's and the utility's keys, keys this script writes under
    the user's hive before Excel starts and while it runs, an old key of the user's, the machine's);
    the runtime's DLLs and the add-in's loaded by name into Excel, each in an Excel of its own since a
    DLL that ends the host ends the rows after it.

.PARAMETER ProgId
    The add-in's versioned ProgID (k17bas.K17Bas.1_0), registered for the current user by its build.

.PARAMETER UtilProgId
    The utility library's versioned ProgID (MWComUtil.MWUtil25.2 or JGraphComUtil.MWUtil25.2).

.PARAMETER Dll
    The add-in's DLL, loaded by its path.

.PARAMETER RuntimeDlls
    The runtime's DLLs loaded by name (through the PATH Excel has), comma-separated.

.PARAMETER Python
    The python.exe that runs k17xlam.py, which wraps the probe module into the add-in workbook.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ProgId,
    [Parameter(Mandatory = $true)] [string] $UtilProgId,
    [Parameter(Mandatory = $true)] [string] $Dll,
    [string] $RuntimeDlls = 'mclmcrrt25_2.dll,mwcomutil.dll,mclxlmain25_2.dll',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Continue'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lines = New-Object System.Collections.Generic.List[string]
function Flat([string] $s) { ($s -replace "`r`n", "`n") -replace "`n", '\n' }
function Say([string] $k, $v) { $text = "$k`t$(Flat ([string]$v))"; $lines.Add($text); Write-Host $text; [System.IO.File]::WriteAllLines($Out, $lines, $utf8) }
function Err($e) {
    $i = $e; while ($i.InnerException) { $i = $i.InnerException }
    'ERR 0x{0:X8} src=[{1}] msg=[{2}] type={3}' -f $i.HResult, $i.Source, $i.Message, $i.GetType().Name
}
function Case([string] $k, [scriptblock] $b) {
    try { Say $k (& $b) } catch { Say $k (Err $_.Exception) }
}
Add-Type -Namespace K17 -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);'

# The probe module around the names, wrapped into an add-in workbook beside the recording.
$here = $PSScriptRoot
$work = Split-Path $Out
$bas = Join-Path $work 'k17hosting.bas'
$xlam = Join-Path $work 'k17hosting.xlam'
[System.IO.File]::WriteAllText($bas, [System.IO.File]::ReadAllText((Join-Path $here 'k17hosting.bas.in')), $utf8)
$wrote = (& $Python -I (Join-Path $here 'k17xlam.py') $xlam $bas 2>&1 | Out-String).Trim()
Say 'xlam.written' $wrote

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$script:excelPids = @()

# Keys of this script's own under the user's hive, written before Excel starts (removed at the end):
# a class name, a CLSID and a plain software key.
$probeKeys = @('Software\Classes\JGraphK17Probe', 'Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}', 'Software\JGraphK17Probe', 'Software\Classes\JGraphK17ProbeLate', 'Software\Classes\JGraphK17ProbeReg')
foreach ($pk in $probeKeys[0..2]) { $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($pk); $key.SetValue('', 'probe'); $key.Close() }
# One written by another process (reg.exe), as an installer would write it.
& reg.exe add "HKCU\$($probeKeys[4])" /ve /d probe /f | Out-Null
# A class key of the user's that was there before this script: the first file type with a default value.
$oldClass = ''
$classes = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes')
foreach ($n in $classes.GetSubKeyNames()) {
    if ($n -notlike '.*' -or $n -like '*Probe*') { continue }
    $sub = $classes.OpenSubKey($n); $v = $sub.GetValue(''); $sub.Close()
    if ($v) { $oldClass = $n; break }
}
$classes.Close()

# One Excel over a list of formulas: each written into A1 of a scratch workbook, calculated, read back.
function Excel-Rows([string] $prefix, [System.Collections.Specialized.OrderedDictionary] $formulas) {
    $xl = $null; $addin = $null; $wb = $null
    try {
        $xl = New-Object -ComObject 'Excel.Application'
        $xl.Visible = $false
        $xl.DisplayAlerts = $false
        $xl.AutomationSecurity = 1
        $pid2 = 0; [void][K17.Win]::GetWindowThreadProcessId([IntPtr]$xl.Hwnd, [ref]$pid2)
        $script:excelPids += $pid2
        Case "$prefix.open" { $script:addin = $xl.Workbooks.Open($xlam); 'opened {0}: IsAddin {1}' -f $script:addin.Name, $script:addin.IsAddin }
        $addin = $script:addin
        $wb = $xl.Workbooks.Add()
        $ws = $wb.Worksheets.Item(1)
        foreach ($k in $formulas.Keys) {
            $f = $formulas[$k]
            Case "$prefix.$k" { $ws.Range('A1').Formula = $f; $xl.Calculate(); $v = $ws.Range('A1').Value2; if ($null -eq $v) { 'null' } else { [string]$v } }
        }
    }
    catch { Say "$prefix.FATAL" (Err $_.Exception) }
    finally {
        try { if ($wb) { $wb.Close($false) } } catch { }
        try { if ($addin) { $addin.Close($false) } } catch { }
        try { if ($xl) { $xl.Quit() } } catch { }
        try { if ($xl) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) } } catch { }
    }
}

function Q([string] $s) { '"' + $s.Replace('"', '""') + '"' }
function Late() {
    # A key written while Excel runs.
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($probeKeys[3]); $key.SetValue('', 'probe'); $key.Close()
    '=jg_regread(' + (Q "HKCU\$($probeKeys[3])\") + ')'
}

# 1. The activations from VBA, and the registry as Excel's process sees it.
$rows = [ordered]@{
    'path.has.dllfolder' = '=jg_path_has(' + (Q (Split-Path $Dll)) + ')'
    'regread.hkcu.probe.classes' = '=jg_regread(' + (Q "HKCU\$($probeKeys[0])\") + ')'
    'regread.hkcu.probe.clsid' = '=jg_regread(' + (Q "HKCU\$($probeKeys[1])\") + ')'
    'regread.hkcu.probe.software' = '=jg_regread(' + (Q "HKCU\$($probeKeys[2])\") + ')'
    'regread.hkcr.probe.classes' = '=jg_regread("HKCR\JGraphK17Probe\")'
    'regread.hkcu.probe.reg' = '=jg_regread(' + (Q "HKCU\$($probeKeys[4])\") + ')'
    'regread.hkcu.old.class' = $(if ($oldClass) { '=jg_regread(' + (Q "HKCU\Software\Classes\$oldClass\") + ')' } else { '="no file type of the user''s own"' })
    'regread.hkcu.old.desktop' = '=jg_regread("HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Desktop")'
    'regread.hkcu.old.username' = '=jg_regread("HKCU\Volatile Environment\USERNAME")'
    'regread.hkcu.missing' = '=jg_regread("HKCU\Software\Classes\NoSuchKeyK17\")'
    'regread.hkcu.progid' = '=jg_regread(' + (Q "HKCU\Software\Classes\$ProgId\CLSID\") + ')'
    'regread.hkcr.progid' = '=jg_regread(' + (Q "HKCR\$ProgId\CLSID\") + ')'
    'regread.hklm.progid' = '=jg_regread(' + (Q "HKLM\Software\Classes\$ProgId\CLSID\") + ')'
    'regread.hkcr.util' = '=jg_regread(' + (Q "HKCR\$UtilProgId\CLSID\") + ')'
    'regread.hklm.util' = '=jg_regread(' + (Q "HKLM\Software\Classes\$UtilProgId\CLSID\") + ')'
    'util.local' = '=jg_create(' + (Q $UtilProgId) + ', "")'
    'util.localhost' = '=jg_create(' + (Q $UtilProgId) + ', "localhost")'
    'addin.localhost' = '=jg_create(' + (Q $ProgId) + ', "localhost")'
    'addin.dot' = '=jg_create(' + (Q $ProgId) + ', ".")'
    'addin.computername' = '=jg_create(' + (Q $ProgId) + ', ' + (Q $env:COMPUTERNAME) + ')'
    'addin.empty' = '=jg_create(' + (Q $ProgId) + ', "")'
    'call.localhost' = '=jg_call_one(' + (Q $UtilProgId) + ', ' + (Q $ProgId) + ', "localhost")'
    'call.local' = '=jg_call_one(' + (Q $UtilProgId) + ', ' + (Q $ProgId) + ', "")'
    'call.local.again' = '=jg_call_one(' + (Q $UtilProgId) + ', ' + (Q $ProgId) + ', "")'
    'regread.hkcu.probe.late' = (Late)
    'create.probe' = '=jg_create("JGraphK17Probe", "")'
}
Excel-Rows 'vba' $rows
# The same keys from this process and from another (reg.exe), while they are still there.
Say 'ps.old.class' $(if ($oldClass) { "HKCU\Software\Classes\$oldClass" } else { 'none' })
foreach ($pk in $probeKeys) {
    $leaf = $pk.Split('\')[-1]
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($pk)
    Say "ps.regread.$leaf" $(if ($key) { $v = $key.GetValue(''); $key.Close(); "ok $v" } else { 'missing' })
    $text = (& reg.exe query "HKCU\$pk" /ve 2>&1 | Out-String)
    Say "reg.query.$leaf" $(if ($text -match 'REG_SZ\s+(\S+)') { 'ok ' + $Matches[1] } else { 'missing' })
}

# 2. The same activations from this process.
Case 'ps.util.local' { $o = New-Object -ComObject $UtilProgId; 'ok ' + $o.GetType().Name }
Case 'ps.util.localhost' { $t = [type]::GetTypeFromProgID($UtilProgId, 'localhost', $true); $o = [Activator]::CreateInstance($t); 'ok ' + $o.GetType().Name }
Case 'ps.addin.local' { $o = New-Object -ComObject $ProgId; 'ok ' + $o.GetType().Name }
Case 'ps.addin.localhost' { $t = [type]::GetTypeFromProgID($ProgId, 'localhost', $true); $o = [Activator]::CreateInstance($t); 'ok ' + $o.GetType().Name }

# 3. The DLLs loaded by name into Excel, the runtime's first, each in an Excel of its own.
foreach ($name in ($RuntimeDlls -split ',' | Where-Object { $_ })) {
    Excel-Rows "load.$([System.IO.Path]::GetFileNameWithoutExtension($name))" ([ordered]@{ 'dll' = '=jg_load(' + (Q $name) + ')' })
}
Excel-Rows 'load.addin' ([ordered]@{ 'dll' = '=jg_load(' + (Q $Dll) + ')'; 'again' = '=jg_load(' + (Q $Dll) + ')' })

# Any Excel this script started that is still alive (a DLL that ended one leaves no process; a hung one would).
Start-Sleep -Seconds 1
foreach ($p in $script:excelPids) {
    $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
    if ($proc) { try { Stop-Process -Id $p -Force; Say "excel.$p" 'still alive, ended' } catch { Say "excel.$p" 'still alive, not ended' } }
}
foreach ($pk in $probeKeys) { try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($pk, $false) } catch { } }
Say 'seconds' ('{0:N1}' -f $watch.Elapsed.TotalSeconds)
