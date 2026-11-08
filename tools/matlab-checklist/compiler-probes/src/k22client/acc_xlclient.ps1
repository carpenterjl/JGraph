<#
.SYNOPSIS
    The acceptance set's Excel client for the Production Server driven outside Excel (compiler and
    SDK plan, stage K17g): R2025b's .NET Framework automation add-in (accxlc.dll from
    compiler.build.excelClientForProductionServer) reflected and its worksheet functions called
    through reflection as k17mps.ps1 calls them, or, with -Native, JGraph's native client created by
    its ProgID (the build registered it for the current user); the server configuration written where
    each client reads it, each library function called with the other clients' samples against the
    server accept-k22.ps1 runs, and the configuration removed. One key<TAB>value line per row.

.PARAMETER Dll
    The client DLL (accxlc.dll).

.PARAMETER Url
    The server URL the configuration names, the archive's name last (http://localhost:<port>/accarch).

.PARAMETER Native
    JGraph's native client in place of R2025b's .NET one.

.PARAMETER ProgId
    Under -Native: the client's ProgID (<add-in>.<Class>).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Dll,
    [Parameter(Mandatory = $true)] [string] $Url,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [switch] $Native,
    [string] $ProgId = 'accxlc.AccXlc',
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Continue'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lines = New-Object System.Collections.Generic.List[string]
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Flat([string] $s) { ($s -replace "`r`n", "`n") -replace "`n", '\n' }
# Every row is written through at once, so what was measured survives a process that ends early.
$writer = New-Object System.IO.StreamWriter($Out, $false, $utf8)
$writer.AutoFlush = $true
function Say([string] $k, $v) { $text = "$k`t$(Flat ([string]$v))"; $lines.Add($text); $script:writer.WriteLine($text); Write-Host $text }
function Err($e) {
    $i = $e; while ($i.InnerException) { $i = $i.InnerException }
    'ERR 0x{0:X8} src=[{1}] msg=[{2}] type={3}' -f $i.HResult, $i.Source, $i.Message, $i.GetType().Name
}
function Case([string] $k, [scriptblock] $b) {
    try { Say $k (& $b) } catch { Say $k (Err $_.Exception) }
}
function Raw($v) {
    if ($v -is [psobject] -and $null -ne $v.psobject.BaseObject) { Write-Output -NoEnumerate $v.psobject.BaseObject } else { Write-Output -NoEnumerate $v }
}
function Show($v) {
    if ($null -eq $v) { return 'null' }
    if ($v -is [System.Reflection.Missing]) { return 'Missing' }
    if ($v -is [System.DBNull]) { return 'DBNull' }
    if ($v -is [array]) {
        $dims = @(); for ($d = 0; $d -lt $v.Rank; $d++) { $dims += ('{0}..{1}' -f $v.GetLowerBound($d), $v.GetUpperBound($d)) }
        $parts = @(); $n = 0
        foreach ($e in $v) { if ($n -ge 24) { $parts += '...'; break }; $parts += (Show $e); $n++ }
        return ('array[{0}] rank {1} [{2}] {{{3}}}' -f $v.GetType().GetElementType().Name, $v.Rank, ($dims -join ','), ($parts -join ', '))
    }
    if ($v -is [string]) { return "string '" + $v + "'" }
    if ($v -is [datetime]) { return 'DateTime ' + $v.ToString('yyyy-MM-dd HH:mm:ss.fff', $inv) }
    if ($v -is [bool]) { return "bool $v" }
    if ($v -is [double]) { return 'double ' + $v.ToString('R', $inv) }
    return ('{0} {1}' -f $v.GetType().Name, [string]$v)
}

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$addin = [System.IO.Path]::GetFileNameWithoutExtension($Dll)
$configRoot = if ($Native) { 'HKCU:\Software\JGraph\Excel Client' } else { 'HKCU:\Software\MathWorks\MATLAB Builder EX\2.5' }
$rootWasThere = Test-Path $configRoot

# R2025b's generated class shows a MessageBox for every failure (a web failure too, with the same text
# it puts in the cell), which would block this process until someone clicks it: a runspace of this
# process watches, through Win32 alone, for dialog windows (class #32770) owned by this very process,
# records each one's static texts and closes it with WM_CLOSE posted to that window - a message to a
# window of this process, not input to the desktop, so nothing of the user's is touched. (UI
# Automation from inside the process saw the box with no children and could not press OK.) JGraph's
# client lands its failures in the cell and shows nothing.
$boxLog = Join-Path (Split-Path $Out) 'acc_xlclient_boxes.txt'
if (Test-Path $boxLog) { Remove-Item $boxLog }
$stopFile = $boxLog + '.stop'
if (Test-Path $stopFile) { Remove-Item $stopFile }
$closer = [powershell]::Create()
[void]$closer.AddScript({
    param($ownerPid, $logPath, $stopPath)
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class AccBoxes {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    static string ClassOf(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    static string TextOf(IntPtr h) { var sb = new StringBuilder(2048); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    // The visible dialog boxes of a process: each one's handle, title and the texts of its static and button children.
    public static List<string> CloseDialogs(uint processId) {
        var found = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != processId || !IsWindowVisible(h) || ClassOf(h) != "#32770") return true;
            var texts = new List<string>();
            EnumChildWindows(h, (c, l2) => { string cls = ClassOf(c); if (cls == "Static" || cls == "Button") texts.Add(cls + ":" + TextOf(c)); return true; }, IntPtr.Zero);
            found.Add(TextOf(h) + " | " + string.Join(" / ", texts.ToArray()));
            PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
    $n = 0
    while (-not (Test-Path $stopPath)) {
        try {
            foreach ($box in [AccBoxes]::CloseDialogs([uint32] $ownerPid)) { $n++; Add-Content -Path $logPath -Value ("{0}`t{1}" -f $n, $box) }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
})
[void]$closer.AddArgument($PID).AddArgument($boxLog).AddArgument($stopFile)
$closerHandle = $closer.BeginInvoke()

$samples = [ordered]@{
    'quiet' = @('acc_quiet', 0, @())
    'add' = @('acc_add', 1, @(2.0, 3.0))
    'add.char' = @('acc_add', 1, @('x', 3.0))
    'add.missing' = @('acc_add', 1, @(2.0))
    'stats' = @('acc_stats', 1, @(, [double[]]@(1, 2, 3, 4)))
    'greet' = @('acc_greet', 1, @('Ada'))
    'scale' = @('acc_scale', 1, @($null, 2.0))
    'scale.scalar' = @('acc_scale', 1, @(2.5, 2.0))
    'count.1' = @('acc_count', 1, @())
    'count.2' = @('acc_count', 1, @())
}
# acc_fail is not called: a function with no outputs has no cell to land its failure in, and R2025b's
# generated class shows a MessageBox the watcher below cannot press (its UI Automation tree is empty
# from inside the process), which held the first run until its timeout. A failure with an output slot
# lands as text (call.add.missing).
$m = New-Object 'double[,]' 2, 2; $m[0, 0] = 1; $m[0, 1] = 2; $m[1, 0] = 3; $m[1, 1] = 4
$samples['scale'][2][0] = $m

try {
    Say 'dll' (Split-Path $Dll -Leaf)
    Say 'dll.exists' (Test-Path -LiteralPath $Dll)
    if ($Native) {
        # JGraph's client (jgxlmps.cpp): Software\JGraph\Excel Client\<add-in>, ServerURL a text value.
        Case 'config.write' {
            $key = Join-Path $configRoot $addin
            New-Item -Path $key -Force | Out-Null
            Set-ItemProperty -Path $key -Name 'ServerURL' -Value $Url
            Set-ItemProperty -Path $key -Name 'responseSizeLimit' -Value 64 -Type DWord
            Set-ItemProperty -Path $key -Name 'timeoutSeconds' -Value 120 -Type DWord
            'written: ' + $addin + ' [ServerURL=' + $Url + ', timeoutSeconds=120, responseSizeLimit=64]'
        }
    } else {
        # As R2025b's generated class reads it (getServerConfig): MPS Server Config\<add-in>, the URL a value name with DWORD 1.
        Case 'config.write' {
            $key = Join-Path $configRoot ('MPS Server Config\' + $addin)
            New-Item -Path $key -Force | Out-Null
            Set-ItemProperty -Path $key -Name $Url -Value 1 -Type DWord
            Set-ItemProperty -Path $key -Name 'responseSizeLimit' -Value 64 -Type DWord
            Set-ItemProperty -Path $key -Name 'timeoutSeconds' -Value 120 -Type DWord
            'written: ' + $addin + ' [ServerURL=' + $Url + ', timeoutSeconds=120, responseSizeLimit=64]'
        }
    }

    if ($Native) {
        $outputs = @{ 'acc_quiet' = 0; 'acc_add' = 1; 'acc_stats' = 1; 'acc_greet' = 1; 'acc_scale' = 1; 'acc_count' = 1; 'acc_fail' = 0 }
        function Invoke-Native([string] $name, [int] $nout, [object[]] $inputs) {
            $parts = @()
            if ($nout -gt 0) {
                $script:__nargout = $nout
                $parts += '$script:__nargout'
                for ($i = 0; $i -lt $nout; $i++) { Set-Variable -Name "__slot$i" -Scope Script -Value $null; $parts += "[ref]`$script:__slot$i" }
            }
            for ($j = 0; $j -lt $inputs.Count; $j++) { Set-Variable -Name "__in$j" -Scope Script -Value (Raw $inputs[$j]); $parts += "`$script:__in$j" }
            Invoke-Expression ('$script:__obj.' + $name + '(' + ($parts -join ', ') + ')') | Out-Null
            if ($nout -eq 0) { return 'null' }
            $outs = @(); for ($i = 0; $i -lt $nout; $i++) { $outs += (Show (Get-Variable -Name "__slot$i" -Scope Script -ValueOnly)) }
            $outs -join ' | '
        }
        $script:__obj = $null
        Case 'create' { $script:__obj = New-Object -ComObject $ProgId; 'ok' }
        if ($script:__obj) {
            foreach ($k in $samples.Keys) {
                $name, $nout, $given = $samples[$k]
                Case "call.$k" { Invoke-Native $name $nout $given }
            }
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:__obj)
            $script:__obj = $null
        }
    } else {
        $asm = $null
        Case 'deps.mpsclient' { [System.Reflection.Assembly]::LoadFrom((Join-Path $MatlabRoot 'toolbox\compiler_sdk\mps_clients\dotnet\MathWorks.MATLAB.ProductionServer.Client.dll')).GetName().Name }
        Case 'deps.excelinterop' { [System.Reflection.Assembly]::Load('Microsoft.Office.Interop.Excel, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c').GetName().Name }
        Case 'deps.visualbasic' { [System.Reflection.Assembly]::Load('Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a').GetName().Name }
        Case 'asm.load' { $script:asm = [System.Reflection.Assembly]::LoadFrom($Dll); $asm.GetName().Name }
        if ($asm) {
            $t = $asm.GetTypes() | Where-Object { $_.IsClass -and $_.IsPublic -and -not $_.IsAbstract -and $null -ne $_.GetConstructor([Type]::EmptyTypes) } | Select-Object -First 1
            $obj = $null
            Case 'create' { $script:obj = [System.Activator]::CreateInstance($t); 'ok' }
            # The generated signatures mirror the add-in's: void f(int nargout, ref object out1..., params object[] inputs),
            # or void f(params object[] inputs) for a function with no outputs.
            function Invoke-Udf([string] $name, [object[]] $inputs) {
                $method = $t.GetMethod($name)
                if ($null -eq $method) { throw "no method $name" }
                $ps = $method.GetParameters()
                $a = [object[]]::new($ps.Count)
                $refs = @()
                $nout = @($ps | Where-Object { $_.ParameterType.IsByRef }).Count
                $raw = [object[]]::new($inputs.Count)
                for ($j = 0; $j -lt $inputs.Count; $j++) {
                    $v = $inputs[$j]
                    if ($v -is [psobject] -and $null -ne $v.psobject.BaseObject) { $v = $v.psobject.BaseObject }
                    $raw[$j] = $v
                }
                for ($i = 0; $i -lt $ps.Count; $i++) {
                    $p = $ps[$i]
                    if ($p.ParameterType.IsByRef) { $a[$i] = $null; $refs += $i }
                    elseif ($p.Name -eq 'nargout' -and $p.ParameterType -eq [int]) { $a[$i] = [int]$nout }
                    elseif ($p.ParameterType -eq [object[]]) { $a[$i] = $raw }
                    else { $a[$i] = $null }
                }
                $r = $method.Invoke($obj, $a)
                if ($refs.Count -eq 0) { return (Show $r) }
                (@($refs | ForEach-Object { Show $a[$_] })) -join ' | '
            }
            if ($obj) {
                foreach ($k in $samples.Keys) {
                    $name, $nout, $given = $samples[$k]
                    Case "call.$k" { Invoke-Udf $name $given }
                }
            }
        }
    }
}
catch {
    Say 'FATAL' (Err $_.Exception)
    Say 'FATAL.where' $_.ScriptStackTrace
}
finally {
    # No forced collection here: with the .NET client's logging off, finalizing its objects ended the
    # process (exit 2, nothing on standard error) before the rows below were written.
    $obj = $null
    Start-Sleep -Milliseconds 600
    New-Item -ItemType File -Path $stopFile -Force | Out-Null
    try { [void]$closer.EndInvoke($closerHandle) } catch { }
    $closer.Dispose()
    if (Test-Path $boxLog) {
        foreach ($l in (Get-Content $boxLog)) { $num, $rest = $l -split "`t", 2; Say "messagebox.$num" $rest }
    } else { Say 'messagebox.count' 0 }
    # The configuration this script wrote goes; the root key too when this script made it.
    try {
        $key = if ($Native) { Join-Path $configRoot $addin } else { Join-Path $configRoot ('MPS Server Config\' + $addin) }
        if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force }
        if (-not $rootWasThere -and (Test-Path $configRoot)) { Remove-Item -Path $(if ($Native) { $configRoot } else { Split-Path $configRoot }) -Recurse -Force }
        Say 'config.removed' (-not (Test-Path $key))
    } catch { Say 'config.removed' (Err $_.Exception) }
    Write-Host ('{0:N1} s' -f $watch.Elapsed.TotalSeconds)
    $writer.Close()
}
