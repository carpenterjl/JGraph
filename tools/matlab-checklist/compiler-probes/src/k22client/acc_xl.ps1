<#
.SYNOPSIS
    The acceptance set's Excel add-in driven from a COM client (compiler and SDK plan, stage K17g):
    the add-in a build of build_k22.m registered for the current user (accxl.AccXl.1_0, R2025b's or
    JGraph's) created by its ProgID outside Excel, after the engine's COM utility library has been
    given an invisible Excel's Application (R2025b's add-in refuses every call until
    MWUtil.MWInitApplication has one, stage K17a; JGraph's takes it the same way), and each library
    function called in PowerShell's direct COM syntax with the samples the other clients use. One
    key<TAB>value line per row; Excel is quit and the objects released at the end, nothing saved.

.PARAMETER ProgId
    The add-in's versioned ProgID.

.PARAMETER UtilProgId
    The utility library's MWUtil: MWComUtil.MWUtil25.2 for R2025b's add-in, JGraphComUtil.MWUtil25.2 for JGraph's.
#>
[CmdletBinding()]
param(
    [string] $ProgId = 'accxl.AccXl.1_0',
    [string] $UtilProgId = 'MWComUtil.MWUtil25.2',
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Continue'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lines = New-Object System.Collections.Generic.List[string]
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Flat([string] $s) { ($s -replace "`r`n", "`n") -replace "`n", '\n' }
function Say([string] $k, $v) { $text = "$k`t$(Flat ([string]$v))"; $lines.Add($text); Write-Host $text }
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
function Prop($obj, [string] $name, [object[]] $index) {
    $obj.GetType().InvokeMember($name, [System.Reflection.BindingFlags]::GetProperty, $null, $obj, $index)
}
function Show($v, [int] $depth = 0) {
    if ($null -eq $v) { return 'null' }
    if ($v -is [System.Reflection.Missing]) { return 'Missing' }
    if ($v -is [System.DBNull]) { return 'DBNull' }
    if ($v -is [array]) {
        $dims = @(); for ($d = 0; $d -lt $v.Rank; $d++) { $dims += ('{0}..{1}' -f $v.GetLowerBound($d), $v.GetUpperBound($d)) }
        $parts = @(); $n = 0
        foreach ($e in $v) { if ($n -ge 24) { $parts += '...'; break }; $parts += $(if ($depth -lt 3) { Show $e ($depth + 1) } else { '?' }); $n++ }
        return ('array[{0}] rank {1} [{2}] {{{3}}}' -f $v.GetType().GetElementType().Name, $v.Rank, ($dims -join ','), ($parts -join ', '))
    }
    if ($v -is [string]) { return "string '" + $v + "'" }
    if ($v -is [datetime]) { return 'DateTime ' + $v.ToString('yyyy-MM-dd HH:mm:ss.fff', $inv) }
    if ($v -is [bool]) { return "bool $v" }
    if ($v -is [double]) { return 'double ' + $v.ToString('R', $inv) }
    if ($v -is [System.__ComObject]) {
        # A struct answer is an MWStruct of the utility library: its fields by name.
        try {
            $nf = $v.NumberOfFields; $names = @($v.FieldNames); $dims = @($v.Dims)
            $fields = @()
            foreach ($nm in $names) {
                try { $f = Prop $v 'Item' @([string]$nm); $fields += ('{0}={1}' -f $nm, (Show $f.Value ($depth + 1))) } catch { $fields += ('{0}=?' -f $nm) }
            }
            return ('MWStruct dims [{0}] fields {1} {{{2}}}' -f ($dims -join ','), $nf, ($fields -join '; '))
        } catch { }
        try { $members = @($v | Get-Member -MemberType Property, Method | ForEach-Object { $_.Name }); return ('COM object [{0}]' -f ($members -join ',')) } catch { return 'COM object' }
    }
    return ('{0} {1}' -f $v.GetType().Name, [string]$v)
}
# A method with nargout, the output slots and the inputs, in PowerShell's direct syntax built as text
# (the one path that keeps a two-dimensional array two-dimensional and surfaces the add-in's error text).
function Call([string] $name, [int] $nout, [object[]] $inputs) {
    $script:__nargout = $nout
    $parts = @()
    if ($nout -gt 0) {
        $parts += '$script:__nargout'
        for ($i = 0; $i -lt $nout; $i++) { Set-Variable -Name "__slot$i" -Scope Script -Value $null; $parts += "[ref]`$script:__slot$i" }
    }
    for ($j = 0; $j -lt $inputs.Count; $j++) { Set-Variable -Name "__in$j" -Scope Script -Value (Raw $inputs[$j]); $parts += "`$script:__in$j" }
    Invoke-Expression ('$script:__obj.' + $name + '(' + ($parts -join ', ') + ')') | Out-Null
    if ($nout -eq 0) { return 'ok' }
    $outs = @(); for ($i = 0; $i -lt $nout; $i++) { $outs += (Show (Get-Variable -Name "__slot$i" -Scope Script -ValueOnly)) }
    $outs -join ' | '
}

$xl = $null; $wb = $null; $u = $null; $script:__obj = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $xl = New-Object -ComObject 'Excel.Application'
    $xl.Visible = $false
    $xl.DisplayAlerts = $false
    Say 'excel.version' ('{0} build {1}' -f $xl.Version, $xl.Build)
    $wb = $xl.Workbooks.Add()
    $ws = $wb.Worksheets.Item(1)
    $ws.Range('A1').Value2 = 1; $ws.Range('B1').Value2 = 2; $ws.Range('A2').Value2 = 3; $ws.Range('B2').Value2 = 4
    Case 'util.create' { $script:u = New-Object -ComObject $UtilProgId; 'ok' }
    Case 'init.excel' { $u.MWInitApplication($xl); 'ok' }
    Case 'create' { $script:__obj = New-Object -ComObject $ProgId; 'ok' }
    if ($script:__obj) {
        Case 'quiet' { Call 'acc_quiet' 0 @() }
        Case 'add' { Call 'acc_add' 1 @(2.0, 3.0) }
        Case 'add.char' { Call 'acc_add' 1 @('x', 3.0) }
        Case 'add.missing' { Call 'acc_add' 1 @(2.0) }
        Case 'stats' { Call 'acc_stats' 1 @(, [double[]]@(1, 2, 3, 4)) }
        Case 'greet' { Call 'acc_greet' 1 @('Ada') }
        $m = New-Object 'double[,]' 2, 2; $m[0, 0] = 1; $m[0, 1] = 2; $m[1, 0] = 3; $m[1, 1] = 4
        Case 'scale' { Call 'acc_scale' 1 @($m, 2.0) }
        Case 'scale.range' { Call 'acc_scale' 1 @($ws.Range('A1:B2'), 10.0) }
        Case 'scale.scalar' { Call 'acc_scale' 1 @(2.5, 2.0) }
        Case 'count.1' { Call 'acc_count' 1 @() }
        Case 'count.2' { Call 'acc_count' 1 @() }
        Case 'fail' { Call 'acc_fail' 0 @('from COM') }
        Case 'fail.nargs' { Call 'acc_fail' 0 @() }
        Case 'count.3' { Call 'acc_count' 1 @() }
        Case 'flags' { $f = $script:__obj.MWFlags; 'InputArrayFormat={0} OutputArrayFormat={1} DateBias={2}' -f $f.ArrayFormatFlags.InputArrayFormat, $f.ArrayFormatFlags.OutputArrayFormat, $f.DataConversionFlags.DateBias }
    }
}
catch {
    Say 'FATAL' (Err $_.Exception)
    Say 'FATAL.where' $_.ScriptStackTrace
}
finally {
    try { if ($script:__obj) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:__obj) } } catch { }
    try { if ($u) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($u) } } catch { }
    try { if ($wb) { $wb.Close($false) } } catch { }
    try { if ($xl) { $xl.Quit() } } catch { }
    try { if ($xl) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($xl) } } catch { }
    Write-Host ('{0:N1} s' -f $watch.Elapsed.TotalSeconds)
    [System.IO.File]::WriteAllLines($Out, $lines, $utf8)
}
