<#
.SYNOPSIS
    The conversion oracle of stage K17a (compiler and SDK plan): an Excel add-in R2025b built from
    src\k17 is created by its ProgID from PowerShell, outside Excel, and every function is called
    with every Variant shape under every flag; what comes back, and every error's HRESULT, source
    and description, is one line each. record-k17.ps1 runs it in a PowerShell of its own.

.DESCRIPTION
    The add-in's CallComFcn refuses every call until MWUtil.MWInitApplication has been given an
    Excel Application object (a Scripting.Dictionary is refused with E_FAIL, recorded first), so an
    invisible Excel runs for the duration, lends a worksheet range for the Range rows, and quits at
    the end with its workbook closed unsaved (decision KO13). The first call is k17_quiet, so the
    errors raised on purpose do not beep.

    Output: label<TAB>value lines in -Out (UTF-8), the same on the console. A value is described by
    its .NET type after the COM marshaller: array[<element type>] rank, bounds and elements, an
    MWStruct, MWComplex or MWSparse by its members, a scalar by its type and text.

.PARAMETER ProgId
    The add-in's versioned ProgID (k17xl.K17Xl.1_0).

.PARAMETER OtherProgIds
    A comma-separated list of other ProgIDs the probe's builds registered; each is created and
    called once.
#>
[CmdletBinding()]
param(
    [string] $ProgId = 'k17xl.K17Xl.1_0',
    [string] $OtherProgIds = '',
    [string] $UtilProgId = 'MWComUtil.MWUtil25.2',
    [ValidateSet('main', 'preinit')] [string] $Phase = 'main',
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Continue'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lines = New-Object System.Collections.Generic.List[string]
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Flat([string] $s) { ($s -replace "`r`n", "`n") -replace "`n", '\n' }
# Every line is written through at once: one of the recorded cases (a negative nargout) ends the
# process with a stack overflow, and nothing after that runs.
$writer = New-Object System.IO.StreamWriter($Out, $false, $utf8)
$writer.AutoFlush = $true
function Say([string] $k, $v) { $text = "$k`t$(Flat ([string]$v))"; $lines.Add($text); $script:writer.WriteLine($text); Write-Host $text }
# A value that went through a function's $args comes back wrapped in a PSObject, which the COM
# marshaller would send as an IDispatch of its own (the add-in refuses it with E_FAIL and no text);
# the bare object is what goes on the wire.
function Raw($v) {
    # -NoEnumerate: written plainly, a two-dimensional array would leave this function as its elements.
    if ($v -is [psobject] -and $null -ne $v.psobject.BaseObject) { Write-Output -NoEnumerate $v.psobject.BaseObject } else { Write-Output -NoEnumerate $v }
}
function Err($e) {
    $i = $e; while ($i.InnerException) { $i = $i.InnerException }
    'ERR 0x{0:X8} src=[{1}] msg=[{2}] type={3}' -f $i.HResult, $i.Source, $i.Message, $i.GetType().Name
}
function Case([string] $k, [scriptblock] $b) {
    try { Say $k (& $b) } catch { Say $k (Err $_.Exception) }
}
function A { , $args }
function Esc([string] $s) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $c = [int]$ch
        if ($c -eq 10) { [void]$sb.Append('\n') } elseif ($c -eq 13) { [void]$sb.Append('\r') } elseif ($c -eq 9) { [void]$sb.Append('\t') }
        elseif ($c -gt 127) { [void]$sb.AppendFormat('\u{0:x4}', $c) } else { [void]$sb.Append($ch) }
    }
    $sb.ToString()
}
function Prop($obj, [string] $name, [object[]] $index) {
    $obj.GetType().InvokeMember($name, [System.Reflection.BindingFlags]::GetProperty, $null, $obj, $index)
}
function Show($v, [int] $depth = 0) {
    if ($null -eq $v) { return 'null' }
    if ($v -is [System.Reflection.Missing]) { return 'Missing' }
    if ($v -is [System.DBNull]) { return 'DBNull' }
    if ($v -is [array]) {
        $t = $v.GetType()
        $el = $t.GetElementType().Name
        $dims = @()
        for ($d = 0; $d -lt $v.Rank; $d++) { $dims += ('{0}..{1}' -f $v.GetLowerBound($d), $v.GetUpperBound($d)) }
        $parts = @()
        $n = 0
        foreach ($e in $v) {
            if ($n -ge 24) { $parts += '...'; break }
            if ($depth -lt 3) { $parts += (Show $e ($depth + 1)) } else { $parts += '?' }
            $n++
        }
        return ('array[{0}] rank {1} [{2}] {{{3}}}' -f $el, $v.Rank, ($dims -join ','), ($parts -join ', '))
    }
    if ($v -is [string]) { return "string '" + (Esc $v) + "'" }
    if ($v -is [datetime]) { return 'DateTime ' + $v.ToString('yyyy-MM-dd HH:mm:ss.fff', $inv) }
    if ($v -is [bool]) { return "bool $v" }
    if ($v -is [double]) { return 'double ' + $v.ToString('R', $inv) }
    if ($v -is [single]) { return 'single ' + $v.ToString('R', $inv) }
    if ($v -is [decimal]) { return 'decimal ' + $v.ToString($inv) }
    if ($v -is [System.__ComObject]) {
        # A COM object's kind by its member names: a missing property reads as null here, not as an error.
        $members = @()
        try { $members = @($v | Get-Member -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) } catch { }
        if ($members -contains 'Address') { try { $addr = $v.Address($false, $false); return ('Range {0} value={1}' -f $addr, (Show $v.Value2 ($depth + 1))) } catch { } }
        if ($members -contains 'Imag') { try { return ('MWComplex re={0} im={1}' -f (Show $v.Real ($depth + 1)), (Show $v.Imag ($depth + 1))) } catch { } }
        if ($members -contains 'NumRows') { try { return ('MWSparse rows={0} cols={1} ri={2} ci={3} array={4}' -f $v.NumRows, $v.NumColumns, (Show $v.RowIndex ($depth + 1)), (Show $v.ColumnIndex ($depth + 1)), (Show $v.Array ($depth + 1))) } catch { } }
        if ($members -notcontains 'NumberOfFields') {
            if ($members -contains 'Value' -and $members -contains 'MWFlags') { try { return ('MWArg value={0}' -f (Show $v.Value ($depth + 1))) } catch { } }
            return ('COM object [{0}]' -f ($members -join ','))
        }
        try { $nf = $v.NumberOfFields; $names = @($v.FieldNames); $dims = @($v.Dims)
            $fields = @()
            if ($depth -lt 3) {
                foreach ($nm in $names) {
                    try { $f = Prop $v 'Item' @([string]$nm); $fields += ('{0}={1}' -f $nm, (Show $f.Value ($depth + 1))) } catch { $fields += ('{0}=?' -f $nm) }
                }
            }
            return ('MWStruct dims [{0}] fields {1} {{{2}}}' -f ($dims -join ','), $nf, ($fields -join '; '))
        } catch { }
        try { $re = $v.Real; $im = $v.Imag; return ('MWComplex re={0} im={1}' -f (Show $re ($depth + 1)), (Show $im ($depth + 1))) } catch { }
        try { $nr = $v.NumRows; return ('MWSparse rows={0} cols={1} ri={2} ci={3} array={4}' -f $nr, $v.NumColumns, (Show $v.RowIndex ($depth + 1)), (Show $v.ColumnIndex ($depth + 1)), (Show $v.Array ($depth + 1))) } catch { }
        try { $val = $v.Value; return ('MWArg value={0}' -f (Show $val ($depth + 1))) } catch { }
        try { $members = @($v | Get-Member -MemberType Property, Method | ForEach-Object { $_.Name }); return ('COM object [{0}]' -f ($members -join ',')) } catch { return 'COM object' }
    }
    return ('{0} {1}' -f $v.GetType().Name, [string]$v)
}

# A method with nargout, the output slots and the inputs, called in PowerShell's own direct syntax
# ($obj.name(nargout, [ref]slot, input...)) built as text, since that path alone keeps a two-dimensional
# array two-dimensional (Type.InvokeMember flattened one to a row) and surfaces the add-in's error
# text (PSMethod.Invoke buried it). The answer is the slots' contents.
function Call($obj, [string] $name, [int] $nargout, [int] $nout, [object[]] $inputs, [object[]] $slots) {
    $script:__obj = $obj
    $script:__nargout = $nargout
    $parts = @('$script:__nargout')
    for ($i = 0; $i -lt $nout; $i++) {
        Set-Variable -Name "__slot$i" -Scope Script -Value $(if ($slots -and $i -lt $slots.Count) { Raw $slots[$i] } else { $null })
        $parts += "[ref]`$script:__slot$i"
    }
    for ($j = 0; $j -lt $inputs.Count; $j++) {
        Set-Variable -Name "__in$j" -Scope Script -Value (Raw $inputs[$j])
        $parts += "`$script:__in$j"
    }
    Invoke-Expression ('$script:__obj.' + $name + '(' + ($parts -join ', ') + ')') | Out-Null
    $outs = @()
    for ($i = 0; $i -lt $nout; $i++) { $outs += (Show (Get-Variable -Name "__slot$i" -Scope Script -ValueOnly)) }
    $outs -join ' | '
}
# A method with no output parameter and no nargout (a function with no outputs).
function CallNoOut($obj, [string] $name, [object[]] $inputs) {
    $script:__obj = $obj
    $parts = @()
    for ($j = 0; $j -lt $inputs.Count; $j++) {
        Set-Variable -Name "__in$j" -Scope Script -Value (Raw $inputs[$j])
        $parts += "`$script:__in$j"
    }
    Invoke-Expression ('$script:__obj.' + $name + '(' + ($parts -join ', ') + ')') | Out-Null
    'ok'
}
function Echo1($obj, $value) { Call $obj 'k17_echo' 1 1 (A $value) }
function Pass1($obj, $value) { Call $obj 'k17_pass' 1 1 (A $value) }
function Make($obj, [string] $kind) { Call $obj 'k17_make' 1 1 (A $kind) }
function Flags($obj) {
    $f = $obj.MWFlags
    $af = $f.ArrayFormatFlags; $dc = $f.DataConversionFlags
    'InputArrayFormat={0} InputArrayIndFlag={1} OutputArrayFormat={2} OutputArrayIndFlag={3} AutoResizeOutput={4} TransposeOutput={5} CoerceNumericToType={6} InputDateFormat={7} OutputAsDate={8} DateBias={9} ReplaceMissing={10} ReplaceMatlabNaN={11}' -f `
        $af.InputArrayFormat, $af.InputArrayIndFlag, $af.OutputArrayFormat, $af.OutputArrayIndFlag, $af.AutoResizeOutput, $af.TransposeOutput, `
        $dc.CoerceNumericToType, $dc.InputDateFormat, $dc.OutputAsDate, $dc.DateBias, $dc.ReplaceMissing, $dc.ReplaceMatlabNaN
}
function Fresh([string] $id) { New-Object -ComObject $id }
# IMWFlags.Clone([out] IMWFlags**): the slot by reference through InvokeMember, as a DispatchWrapper so
# it goes as VT_BYREF|VT_DISPATCH (PowerShell's [ref] fails on it, a null slot is a type mismatch).
function CloneFlags($f) {
    $a = New-Object object[] 1
    $a[0] = [System.Runtime.InteropServices.DispatchWrapper]::new([object]$null)
    $pm = New-Object System.Reflection.ParameterModifier 1
    $pm[0] = $true
    [void]$f.GetType().InvokeMember('Clone', [System.Reflection.BindingFlags]::InvokeMethod, $null, $f, $a, [System.Reflection.ParameterModifier[]]@($pm), $null, $null)
    $a[0]
}
function ErrorCell([int] $code) { New-Object System.Runtime.InteropServices.ErrorWrapper $code }
function Matrix2([double[]] $values, [int] $rows, [int] $cols) {
    $m = New-Object 'double[,]' $rows, $cols
    $k = 0
    for ($r = 0; $r -lt $rows; $r++) { for ($c = 0; $c -lt $cols; $c++) { $m[$r, $c] = $values[$k]; $k++ } }
    , $m
}
function VarMatrix2([object[]] $values, [int] $rows, [int] $cols) {
    $m = New-Object 'object[,]' $rows, $cols
    $k = 0
    for ($r = 0; $r -lt $rows; $r++) { for ($c = 0; $c -lt $cols; $c++) { $m[$r, $c] = $values[$k]; $k++ } }
    , $m
}
function OneBased([object[]] $values, [int] $rows, [int] $cols) {
    $m = [System.Array]::CreateInstance([object], [int[]]@($rows, $cols), [int[]]@(1, 1))
    $k = 0
    for ($r = 1; $r -le $rows; $r++) { for ($c = 1; $c -le $cols; $c++) { $m.SetValue($values[$k], $r, $c); $k++ } }
    , $m
}

$xl = $null; $wb = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    Say 'host.powershell' $PSVersionTable.PSVersion
    Say 'host.clr' ([System.Environment]::Version)
    Say 'host.is64' ([System.Environment]::Is64BitProcess)

    # --- the utility library before any component -------------------------------------------------------
    $u = $null
    Case 'util.create' { $script:u = Fresh $UtilProgId; 'ok' }
    Case 'util.IsMCRInitialized.before' { $u.IsMCRInitialized() }
    Case 'util.IsMCRJVMEnabled' { $u.IsMCRJVMEnabled() }

    # --- the component ----------------------------------------------------------------------------------
    $o = $null
    $t = [System.Diagnostics.Stopwatch]::StartNew()
    Case 'create' { $script:o = Fresh $ProgId; 'ok' }
    Say 'create.seconds' ('{0:N1}' -f $t.Elapsed.TotalSeconds)
    Case 'util.IsMCRInitialized.after' { $u.IsMCRInitialized() }
    Case 'members' { (@($o | Get-Member -MemberType Property, Method | ForEach-Object { $_.Name }) -join ',') }
    $dict = New-Object -ComObject 'Scripting.Dictionary'

    if ($Phase -eq 'preinit') {
        # Before MWInitApplication, in a process of its own: a call with arguments that failed before the
        # initialization left the main run's first call failing too (the first run of this script), so
        # the pre-initialization rows are recorded apart from everything else.
        Case 'preinit.quiet' { CallNoOut $o 'k17_quiet' (A) }
        Case 'preinit.none' { CallNoOut $o 'k17_none' (A) }
        Case 'preinit.help' { Show (Prop $o 'help' @('k17_one')) }
        Case 'preinit.flags' { Flags $o }
        Case 'preinit.call' { Call $o 'k17_one' 1 1 (A 2.0) }
        Case 'preinit.call.direct' { $y = $null; $o.k17_one(1, [ref]$y, 2.0); Show $y }
        Case 'preinit.init.dictionary' { $u.MWInitApplication($dict); 'ok' }
        Case 'preinit.call.after.dictionary' { Call $o 'k17_one' 1 1 (A 2.0) }
        $xl = New-Object -ComObject 'Excel.Application'
        $xl.Visible = $false
        $xl.DisplayAlerts = $false
        Case 'preinit.init.excel' { $u.MWInitApplication($xl); 'ok' }
        Case 'preinit.call.after.excel' { Call $o 'k17_one' 1 1 (A 2.0) }
        Case 'preinit.call.after.excel.direct' { $y = $null; $o.k17_one(1, [ref]$y, 2.0); Show $y }
        Case 'preinit.fresh.after.excel' { $z = Fresh $ProgId; Call $z 'k17_one' 1 1 (A 2.0) }
        Case 'preinit.noout.after.excel' { CallNoOut $o 'k17_noout' (A 'x') }
        Case 'preinit.terminate' { $u.MWTerminateApplication(); 'ok' }
        try { $xl.Quit() } catch { }
        try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) } catch { }
        $xl = $null
        Say 'preinit.excel.quit' 'ok'
        # Last, since it ends the process: a negative nargout makes CallComFcn _alloca a negative count
        # and the host dies with a stack overflow (the first main run found it). Nothing after it runs.
        Say 'preinit.next' 'preinit.crash.nargoutneg: k17_two with nargout -1; the process ends here if it crashes'
        Case 'preinit.crash.nargoutneg' { Call $o 'k17_two' -1 2 (A 5.0) }
        return
    }

    # --- Excel, invisible, for MWInitApplication and the range rows ----------------------------------------
    $xl = New-Object -ComObject 'Excel.Application'
    $xl.Visible = $false
    $xl.DisplayAlerts = $false
    Say 'excel.version' ('{0} build {1}' -f $xl.Version, $xl.Build)
    $wb = $xl.Workbooks.Add()
    $ws = $wb.Worksheets.Item(1)
    $ws.Range('A1').Value2 = 1.5; $ws.Range('B1').Value2 = 'two'; $ws.Range('A2').Value2 = $true; $ws.Range('B2').Value2 = 4
    $ws.Range('D1').Value2 = 10; $ws.Range('D2').Value2 = 20; $ws.Range('D3').Value2 = 30
    $ws.Range('F1').Formula = '=1/0'
    $ws.Range('G1').Value2 = [datetime]'2024-03-15 10:30:00'
    Case 'init.excel' { $u.MWInitApplication($xl); 'ok' }
    Case 'init.excel.twice' { $u.MWInitApplication($xl); 'ok' }
    Case 'quiet' { CallNoOut $o 'k17_quiet' (A) }
    Case 'none' { CallNoOut $o 'k17_none' (A) }
    Case 'one' { Call $o 'k17_one' 1 1 (A 2.0) }
    Case 'util.IsMCRInitialized.running' { $u.IsMCRInitialized() }

    # --- help ---------------------------------------------------------------------------------------------
    foreach ($name in @('k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', 'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make')) {
        Case "help.$name" { Show (Prop $o 'help' @($name)) }
    }
    Case 'help.unknown' { Show (Prop $o 'help' @('nosuch')) }
    Case 'help.empty' { Show (Prop $o 'help' @('')) }
    Case 'help.case' { Show (Prop $o 'help' @('K17_ONE')) }
    Case 'help.noarg' { Show (Prop $o 'help' @()) }

    # --- the flags' defaults ------------------------------------------------------------------------------
    Case 'flags.defaults' { Flags $o }
    Case 'flags.sameobject' { $f1 = $o.MWFlags; $f2 = $o.MWFlags; [System.Runtime.InteropServices.Marshal]::ReferenceEquals($f1, $f2) }
    Case 'flags.clone' {
        $f = $o.MWFlags; $c = CloneFlags $f
        $c.DataConversionFlags.DateBias = 1
        '{0} / clone {1} / original {2}' -f (Flags $o), $c.DataConversionFlags.DateBias, $o.MWFlags.DataConversionFlags.DateBias
    }
    Case 'flags.put' {
        $f = $o.MWFlags; $c = CloneFlags $f
        $c.DataConversionFlags.DateBias = 7
        $o.MWFlags = $c
        $r = $o.MWFlags.DataConversionFlags.DateBias
        $o.MWFlags = $f
        '{0} then {1}' -f $r, $o.MWFlags.DataConversionFlags.DateBias
    }

    # --- nargout and the output slots ----------------------------------------------------------------------
    Case 'two.nargout0' { Call $o 'k17_two' 0 2 (A 5.0) }
    Case 'two.nargout1' { Call $o 'k17_two' 1 2 (A 5.0) }
    Case 'two.nargout2' { Call $o 'k17_two' 2 2 (A 5.0) }
    Case 'two.nargout3' { Call $o 'k17_two' 3 2 (A 5.0) }
    Case 'one.nargout0' { Call $o 'k17_one' 0 1 (A 5.0) }
    Case 'one.nargout2' { Call $o 'k17_one' 2 1 (A 5.0) }
    Case 'many.nargout3' { Call $o 'k17_many' 3 3 (A 1.0 2.0 3.0) }
    Case 'many.nargout1' { Call $o 'k17_many' 1 3 (A 1.0 2.0 3.0) }
    Case 'many.arrays' { Call $o 'k17_many' 3 3 (A ([double[]]@(1, 2)) ([double[]]@(3, 4)) 2.0) }
    Case 'varout.nargout0' { Call $o 'k17_varout' 0 1 (A 5.0) }
    Case 'varout.nargout1' { Call $o 'k17_varout' 1 1 (A 5.0) }
    Case 'varout.nargout2' { Call $o 'k17_varout' 2 1 (A 5.0) }
    Case 'varout.nargout3' { Call $o 'k17_varout' 3 1 (A 5.0) }
    # k17_both(x, varargin) is two parameters on the interface: x, and varargin as one Variant (an array
    # of Variants is spread, as MWPack packs it).
    Case 'both.nargout1' { Call $o 'k17_both' 1 2 (A 7.0 ([object[]]@('a', 2.0))) }
    Case 'both.nargout2' { Call $o 'k17_both' 2 2 (A 7.0 ([object[]]@('a', 2.0))) }
    Case 'both.nargout3' { Call $o 'k17_both' 3 2 (A 7.0 ([object[]]@('a', 2.0))) }
    Case 'both.nargout4' { Call $o 'k17_both' 4 2 (A 7.0 ([object[]]@('a', 2.0))) }
    Case 'both.packed' { $p = $null; $u.MWPack([ref]$p, 'a', 2.0); Call $o 'k17_both' 3 2 (A 7.0 $p) }
    Case 'both.scalarvarargin' { Call $o 'k17_both' 3 2 (A 7.0 'a') }
    Case 'both.missingvarargin' { Call $o 'k17_both' 2 2 (A 7.0 ([System.Reflection.Missing]::Value)) }
    Case 'both.emptyvarargin' { Call $o 'k17_both' 2 2 (A 7.0 $null) }
    Case 'both.threeargs' { Call $o 'k17_both' 2 2 (A 7.0 'a' 2.0) }
    Case 'noout' { CallNoOut $o 'k17_noout' (A 'x') }
    Case 'noout.array' { CallNoOut $o 'k17_noout' (A ([double[]]@(1, 2, 3))) }
    Case 'noout.missing' { CallNoOut $o 'k17_noout' (A ([System.Reflection.Missing]::Value)) }
    Case 'noout.empty' { CallNoOut $o 'k17_noout' (A $null) }
    Case 'count.1' { Call $o 'k17_count' 1 1 (A) }
    Case 'count.2' { Call $o 'k17_count' 1 1 (A) }
    $o2 = $null
    Case 'count.secondobject' { $script:o2 = Fresh $ProgId; Call $o2 'k17_count' 1 1 (A) }
    Case 'count.firstagain' { Call $o 'k17_count' 1 1 (A) }

    # --- varargin and the missing-argument rules ----------------------------------------------------------
    $missing = [System.Reflection.Missing]::Value
    Case 'varin.none' { Call $o 'k17_varin' 1 1 (A) }
    Case 'varin.one' { Call $o 'k17_varin' 1 1 (A 1.0) }
    Case 'varin.two' { Call $o 'k17_varin' 1 1 (A 1.0 'a') }
    Case 'varin.two.packed' { Call $o 'k17_varin' 1 1 (A ([object[]]@(1.0, 'a'))) }
    Case 'varin.missing' { Call $o 'k17_varin' 1 1 (A $missing) }
    Case 'varin.empty' { Call $o 'k17_varin' 1 1 (A $null) }
    Case 'varin.array' { Call $o 'k17_varin' 1 1 (A ([double[]]@(1, 2, 3))) }
    Case 'varin.vararray' { Call $o 'k17_varin' 1 1 (A ([object[]]@(1.0, 'a', $true))) }
    Case 'varin.vararray2d' { Call $o 'k17_varin' 1 1 (A (VarMatrix2 @(1.0, 2.0, 3.0, 4.0) 2 2)) }
    Case 'varin.packed' { $p = $null; $u.MWPack([ref]$p, 1.0, 'two', ([double[]]@(3, 4))); Call $o 'k17_varin' 1 1 (A $p) }
    Case 'varin.packedarray' { $p = $null; $u.MWPack([ref]$p, ([double[]]@(3, 4))); Call $o 'k17_varin' 1 1 (A $p) }
    Case 'varin.emptyvararray' { Call $o 'k17_varin' 1 1 (A ([object[]]@())) }
    Case 'opt.0' { Call $o 'k17_opt' 1 1 (A $missing $missing $missing) }
    Case 'opt.1' { Call $o 'k17_opt' 1 1 (A 1.0 $missing $missing) }
    Case 'opt.2' { Call $o 'k17_opt' 1 1 (A 1.0 'b' $missing) }
    Case 'opt.3' { Call $o 'k17_opt' 1 1 (A 1.0 'b' $true) }
    Case 'opt.middle' { Call $o 'k17_opt' 1 1 (A 1.0 $missing $true) }
    Case 'opt.emptymiddle' { Call $o 'k17_opt' 1 1 (A 1.0 $null $true) }
    Case 'opt.emptylast' { Call $o 'k17_opt' 1 1 (A 1.0 'b' $null) }
    Case 'opt.dbnull' { Call $o 'k17_opt' 1 1 (A 1.0 ([System.DBNull]::Value) $true) }
    Case 'opt.errorcell' { Call $o 'k17_opt' 1 1 (A 1.0 (ErrorCell 0x800A07FA) $true) }
    Case 'opt.paramnotfound' { Call $o 'k17_opt' 1 1 (A 1.0 (ErrorCell 0x80020004) $true) }
    Case 'args.default' { Call $o 'k17_args' 1 1 (A ([double[]]@(1, 2)) $null) }
    Case 'args.both' { Call $o 'k17_args' 1 1 (A ([double[]]@(1, 2)) 3.0) }
    Case 'args.missing' { Call $o 'k17_args' 1 1 (A ([double[]]@(1, 2)) $missing) }

    # --- values in: what each Variant becomes in MATLAB ----------------------------------------------------
    $inputs = [ordered]@{
        'empty' = $null
        'missing' = $missing
        'dbnull' = [System.DBNull]::Value
        'err_na' = (ErrorCell 0x800A07FA)
        'err_div0' = (ErrorCell 0x800A07D7)
        'err_value' = (ErrorCell 0x800A07DF)
        'err_ref' = (ErrorCell 0x800A07E7)
        'r8' = 3.5
        'r8_int' = 4.0
        'r8_neg' = -2.25
        'r8_big' = 1e300
        'r8_nan' = [double]::NaN
        'r8_inf' = [double]::PositiveInfinity
        'r4' = [single]1.5
        'i4' = [int]7
        'i2' = [int16]-3
        'i8' = [int64]9007199254740993
        'ui1' = [byte]200
        'ui2' = [uint16]60000
        'ui4' = [uint32]4000000000
        'i1' = [sbyte]-5
        'bool_true' = $true
        'bool_false' = $false
        'bstr' = 'hello'
        'bstr_empty' = ''
        'bstr_nonascii' = ("caf" + [char]0x00E9 + " " + [char]0x2014 + " " + [char]0x00B0)
        'bstr_newline' = "one`ntwo"
        'bstr_number' = '42'
        'char' = [char]'c'
        'date' = [datetime]'2024-03-15 10:30:00'
        'date_1900' = [datetime]'1900-01-01'
        'date_timeonly' = [datetime]'1899-12-30 06:00:00'
        'date_early' = [datetime]'1850-06-01'
        'cy' = (New-Object System.Runtime.InteropServices.CurrencyWrapper ([decimal]1.5))
        'decimal' = [decimal]2.25
        'arr_r8' = [double[]]@(1, 2, 3)
        'arr_r8_one' = [double[]]@(9)
        'arr_r8_2d' = (Matrix2 @(1, 2, 3, 4, 5, 6) 2 3)
        'arr_r8_col' = (Matrix2 @(1, 2, 3) 3 1)
        'arr_r8_row' = (Matrix2 @(1, 2, 3) 1 3)
        'arr_r8_empty' = [double[]]@()
        'arr_r8_empty2d' = (New-Object 'double[,]' 0, 0)
        'arr_r8_3d' = (New-Object 'double[,,]' 2, 2, 2)
        'arr_r4' = [single[]]@(1.5, 2.5)
        'arr_i4' = [int[]]@(1, 2)
        'arr_i2' = [int16[]]@(1, 2)
        'arr_ui1' = [byte[]]@(1, 2)
        'arr_bool' = [bool[]]@($true, $false)
        'arr_str' = [string[]]@('a', 'bb')
        'arr_str_2d' = (VarMatrix2 @('a', 'b', 'c', 'd') 2 2)
        'arr_date' = [datetime[]]@([datetime]'2024-03-15', [datetime]'2024-03-16')
        'arr_decimal' = [decimal[]]@(1.5, 2.5)
        'arr_var_numbers' = [object[]]@(1.0, 2.0, 3.0)
        'arr_var_mixed' = [object[]]@(1.0, 'a', $true)
        'arr_var_2d' = (VarMatrix2 @(1.0, 2.0, 3.0, 4.0) 2 2)
        'arr_var_2d_mixed' = (VarMatrix2 @(1.0, 'a', $true, 4.0) 2 2)
        'arr_var_2d_strings' = (VarMatrix2 @('a', 'b', 'c', 'd') 2 2)
        'arr_var_empty' = [object[]]@()
        'arr_var_with_empty' = [object[]]@(1.0, $null, 3.0)
        'arr_var_with_missing' = [object[]]@(1.0, $missing, 3.0)
        # An ErrorWrapper inside an array is left out: through PowerShell's own marshaller it ended the
        # host with an access violation inside the add-in's conversion (the fourth run); a Range holding
        # an error cell (in.range_error below) is the row Excel would produce.
        'arr_var_with_dbnull' = [object[]]@(1.0, [System.DBNull]::Value, 3.0)
        'arr_var_with_date' = [object[]]@(1.0, [datetime]'2024-03-15', 3.0)
        'arr_var_ints' = [object[]]@([int]1, [int16]2, [byte]3)
        'arr_var_bools' = [object[]]@($true, $false)
        'arr_var_strings' = [object[]]@('a', 'bb')
        'arr_var_2d_with_empty' = (VarMatrix2 @(1.0, $null, $null, 4.0) 2 2)
        'arr_jagged' = [object[]]@([object[]]@(1.0, 2.0), [object[]]@(3.0, 4.0))
        'arr_jagged_ragged' = [object[]]@([object[]]@(1.0, 2.0), [object[]]@(3.0))
        'arr_jagged_deep' = [object[]]@([object[]]@([object[]]@(1.0), 'x'), 2.0)
        'arr_jagged_r8' = [object[]]@([double[]]@(1, 2), [double[]]@(3, 4))
        'arr_1based' = (OneBased @(1.0, 2.0, 3.0, 4.0) 2 2)
        'arr_1based_mixed' = (OneBased @(1.0, 'b', $null, 4.0) 2 2)
    }
    foreach ($k in $inputs.Keys) { $v = $inputs[$k]; Case "in.$k" { Echo1 $o $v } }
    Case 'in.dispatch_other' { Echo1 $o $dict }
    Case 'in.range_one' { Echo1 $o ($ws.Range('A1')) }
    Case 'in.range_2x2' { Echo1 $o ($ws.Range('A1:B2')) }
    Case 'in.range_col' { Echo1 $o ($ws.Range('D1:D3')) }
    Case 'in.range_blank' { Echo1 $o ($ws.Range('H1')) }
    Case 'in.range_blanks' { Echo1 $o ($ws.Range('H1:I2')) }
    Case 'in.range_error' { Echo1 $o ($ws.Range('F1')) }
    Case 'in.range_date' { Echo1 $o ($ws.Range('G1')) }
    Case 'in.range_value2' { Echo1 $o ($ws.Range('A1:B2').Value2) }
    Case 'in.range_value' { Echo1 $o ($ws.Range('A1:B2').Value(10)) }
    Case 'in.struct' {
        $s = Fresh 'MWComUtil.MWStruct'
        $s.Initialize([double[]]@(1, 1), [string[]]@('a', 'b'))
        (Prop $s 'Item' @('a')).Value = 1.0
        (Prop $s 'Item' @('b')).Value = 'text'
        Echo1 $o $s
    }
    Case 'in.struct_array' {
        $s = Fresh 'MWComUtil.MWStruct'
        $s.Initialize([double[]]@(1, 2), [string[]]@('a'))
        (Prop $s 'Item' @(1.0, 1.0)).Value = 1.0
        (Prop $s 'Item' @(1.0, 2.0)).Value = 2.0
        Echo1 $o $s
    }
    Case 'in.struct_nested' {
        $inner = Fresh 'MWComUtil.MWStruct'
        $inner.Initialize([double[]]@(1, 1), [string[]]@('x'))
        (Prop $inner 'Item' @('x')).Value = 9.0
        $s = Fresh 'MWComUtil.MWStruct'
        $s.Initialize([double[]]@(1, 1), [string[]]@('in', 'arr'))
        (Prop $s 'Item' @('in')).Value = $inner
        (Prop $s 'Item' @('arr')).Value = [double[]]@(1, 2)
        Echo1 $o $s
    }
    Case 'in.struct_empty' { $s = Fresh 'MWComUtil.MWStruct'; Echo1 $o $s }
    Case 'in.complex' { $c = Fresh 'MWComUtil.MWComplex'; $c.Real = 1.0; $c.Imag = 2.0; Echo1 $o $c }
    Case 'in.complex_array' { $c = Fresh 'MWComUtil.MWComplex'; $c.Real = [double[]]@(1, 2); $c.Imag = [double[]]@(3, 4); Echo1 $o $c }
    Case 'in.complex_noimag' { $c = Fresh 'MWComUtil.MWComplex'; $c.Real = 1.0; Echo1 $o $c }
    Case 'in.sparse' {
        $s = Fresh 'MWComUtil.MWSparse'
        $s.NumRows = 2; $s.NumColumns = 3
        $s.RowIndex = [double[]]@(1, 2); $s.ColumnIndex = [double[]]@(1, 3); $s.Array = [double[]]@(5, 6)
        Echo1 $o $s
    }
    Case 'in.sparse_empty' { $s = Fresh 'MWComUtil.MWSparse'; Echo1 $o $s }
    Case 'in.arg_plain' { $g = Fresh 'MWComUtil.MWArg'; $g.Value = 3.7; Echo1 $o $g }
    Case 'in.arg_int8' { $g = Fresh 'MWComUtil.MWArg'; $g.Value = 3.7; $g.MWFlags.DataConversionFlags.CoerceNumericToType = 8; Echo1 $o $g }
    Case 'in.arg_cell' { $g = Fresh 'MWComUtil.MWArg'; $g.Value = (Matrix2 @(1, 2, 3, 4) 2 2); $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; Echo1 $o $g }
    Case 'in.arg_flagsobject' { $g = Fresh 'MWComUtil.MWArg'; Show $g.MWFlags }
    Case 'in.enumvararg' { $e = Fresh 'MWComUtil.MWEnumVararg'; Show $e }
    Case 'in.field' { $f = Fresh 'MWComUtil.MWField'; Show $f }
    Case 'in.flags_object' { $f = Fresh 'MWComUtil.MWFlags'; Echo1 $o $f }

    # --- values out: what each MATLAB class becomes ---------------------------------------------------------
    foreach ($kind in @('double', 'doublerow', 'doublecol', 'doublemat', 'double3d', 'doubleneg0', 'bigint', 'single', 'singlemat', 'int8', 'uint8', 'uint8mat', 'int16', 'uint16', 'int32', 'uint32', 'int64', 'uint64',
            'logical', 'logicalfalse', 'logicalmat', 'logicalempty', 'char', 'charone', 'charmat', 'charcol', 'charempty', 'charnonascii', 'charnewline', 'string', 'stringarr', 'stringmissing', 'stringempty',
            'cell', 'cellnested', 'cellcol', 'cellmat', 'cellempty', 'cellofempty', 'struct', 'structarr', 'structcol', 'structnested', 'structempty', 'structnofields',
            'complex', 'complexmat', 'complexzero', 'sparse', 'sparselogical', 'sparseempty', 'empty', 'emptyrow', 'emptycol', 'nan', 'nanmat', 'inf', 'neginf',
            'datetime', 'datetimearr', 'datetimenat', 'datenum', 'duration', 'categorical', 'fhandle', 'fhandleanon', 'table', 'map', 'object', 'nosuchkind')) {
        Case "out.$kind" { Make $o $kind }
    }
    Case 'out.struct.members' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); '{0} fields={1} dims={2} names={3} item.a={4} item.b.name={5}' -f $s.GetType().Name, $s.NumberOfFields, (Show $s.Dims), (Show $s.FieldNames), (Show (Prop $s 'Item' @('a')).Value), (Prop $s 'Item' @('b')).Name }
    Case 'out.complex.members' { $c = $null; $o.k17_make(1, [ref]$c, 'complexmat'); '{0} re={1} im={2}' -f $c.GetType().Name, (Show $c.Real), (Show $c.Imag) }
    Case 'out.sparse.members' { $s = $null; $o.k17_make(1, [ref]$s, 'sparse'); '{0} rows={1} cols={2} ri={3} ci={4} array={5}' -f $s.GetType().Name, $s.NumRows, $s.NumColumns, (Show $s.RowIndex), (Show $s.ColumnIndex), (Show $s.Array) }

    # --- round trips --------------------------------------------------------------------------------------
    foreach ($k in @('empty', 'missing', 'r8', 'r4', 'i4', 'i2', 'i8', 'ui1', 'bool_true', 'bstr', 'bstr_empty', 'bstr_nonascii', 'char', 'date', 'cy', 'decimal', 'arr_r8', 'arr_r8_2d', 'arr_r8_col', 'arr_i4', 'arr_bool', 'arr_str', 'arr_var_mixed', 'arr_var_2d', 'arr_var_with_empty', 'arr_jagged', 'arr_1based', 'arr_date', 'err_na', 'dbnull')) {
        $v = $inputs[$k]; Case "pass.$k" { Pass1 $o $v }
    }
    Case 'pass.range' { Pass1 $o ($ws.Range('A1:B2')) }

    # --- outputs into a Range ------------------------------------------------------------------------------
    function RangeOut([string] $addr, [string] $kind, [scriptblock] $setup) {
        $ws.Range('K1:P20').Clear() | Out-Null
        $target = $ws.Range($addr)
        if ($setup) { & $setup }
        $slot = $target
        $o.k17_make(1, [ref]$slot, $kind)
        $used = $ws.Range('K1:P20')
        $cells = @()
        foreach ($cell in $used.Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
        'slot={0} cells {{{1}}}' -f (Show $slot), ($cells -join ', ')
    }
    Case 'rangeout.scalar' { RangeOut 'K1' 'double' }
    Case 'rangeout.matrix.onecell' { RangeOut 'K1' 'doublemat' }
    Case 'rangeout.matrix.fit' { RangeOut 'K1:L2' 'doublemat' }
    Case 'rangeout.matrix.larger' { RangeOut 'K1:M3' 'doublemat' }
    Case 'rangeout.row' { RangeOut 'K1' 'doublerow' }
    Case 'rangeout.col' { RangeOut 'K1' 'doublecol' }
    Case 'rangeout.cell' { RangeOut 'K1' 'cell' }
    Case 'rangeout.char' { RangeOut 'K1' 'char' }
    Case 'rangeout.charmat' { RangeOut 'K1' 'charmat' }
    Case 'rangeout.nan' { RangeOut 'K1' 'nan' }
    Case 'rangeout.nanmat' { RangeOut 'K1' 'nanmat' }
    Case 'rangeout.empty' { RangeOut 'K1' 'empty' }
    Case 'rangeout.struct' { RangeOut 'K1' 'struct' }
    Case 'rangeout.logicalmat' { RangeOut 'K1' 'logicalmat' }
    Case 'rangeout.datetime' { RangeOut 'K1' 'datetime' }
    Case 'rangeout.complex' { RangeOut 'K1' 'complex' }
    Case 'rangeout.noresize' { RangeOut 'K1' 'doublemat' { $o.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
    $o.MWFlags.ArrayFormatFlags.AutoResizeOutput = $true
    Case 'rangeout.transpose' { RangeOut 'K1' 'doublerow' { $o.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
    $o.MWFlags.ArrayFormatFlags.TransposeOutput = $false
    Case 'rangeout.transpose.matrix' { RangeOut 'K1' 'doublemat' { $o.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
    $o.MWFlags.ArrayFormatFlags.TransposeOutput = $false
    Case 'flags.afterrange' { Flags $o }

    # --- the flags, each on a fresh object ------------------------------------------------------------------
    foreach ($v in @(0, 1, 2, 3)) {
        Case "flag.InputArrayFormat.$v.r8_2d" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v; Echo1 $f (Matrix2 @(1, 2, 3, 4) 2 2) }
        Case "flag.InputArrayFormat.$v.var_2d" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v; Echo1 $f (VarMatrix2 @(1.0, 'a', $true, 4.0) 2 2) }
        Case "flag.InputArrayFormat.$v.var_1d" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v; Echo1 $f ([object[]]@(1.0, 2.0)) }
        Case "flag.InputArrayFormat.$v.scalar" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v; Echo1 $f 5.0 }
        Case "flag.InputArrayFormat.$v.range" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v; Echo1 $f ($ws.Range('A1:B2')) }
    }
    foreach ($ind in @(0, 1, 2, 3)) {
        Case "flag.InputArrayIndFlag.$ind" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind; Call $f 'k17_many' 3 3 (A (Matrix2 @(1, 2) 1 2) (Matrix2 @(3, 4) 1 2) (Matrix2 @(5, 6) 1 2)) }
        Case "flag.InputArrayIndFlag.$ind.echo" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind; Echo1 $f (Matrix2 @(1, 2) 1 2) }
    }
    foreach ($v in @(0, 1, 2)) {
        Case "flag.OutputArrayFormat.$v.doublemat" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $v; Make $f 'doublemat' }
        Case "flag.OutputArrayFormat.$v.cell" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $v; Make $f 'cell' }
        Case "flag.OutputArrayFormat.$v.double" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $v; Make $f 'double' }
        Case "flag.OutputArrayFormat.$v.struct" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $v; Make $f 'struct' }
        Case "flag.OutputArrayFormat.$v.char" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $v; Make $f 'char' }
    }
    foreach ($ind in @(0, 1, 2)) {
        Case "flag.OutputArrayIndFlag.$ind" { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind; Call $f 'k17_two' 2 2 (A (Matrix2 @(1, 2) 1 2)) }
    }
    # Only mwDataType's own values: 1, 2, 5 and anything past 13 are undefined, and a value of 1 ended the
    # host in the runtime's conversion (the fifth run); 2 and 5 were refused with E_FAIL in an earlier one.
    foreach ($t in @(0, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13)) {
        Case "flag.CoerceNumericToType.$t.r8" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t; Echo1 $f 3.7 }
        Case "flag.CoerceNumericToType.$t.arr" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t; Echo1 $f ([double[]]@(1.5, -2.5, 300)) }
        Case "flag.CoerceNumericToType.$t.bstr" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t; Echo1 $f 'text' }
        Case "flag.CoerceNumericToType.$t.bool" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t; Echo1 $f $true }
        Case "flag.CoerceNumericToType.$t.i4" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t; Echo1 $f ([int]7) }
    }
    foreach ($v in @(0, 1, 2)) {
        Case "flag.InputDateFormat.$v.date" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.InputDateFormat = $v; Echo1 $f ([datetime]'2024-03-15 10:30:00') }
        Case "flag.InputDateFormat.$v.r8" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.InputDateFormat = $v; Echo1 $f 45366.4375 }
        Case "flag.InputDateFormat.$v.range" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.InputDateFormat = $v; Echo1 $f ($ws.Range('G1')) }
    }
    foreach ($v in @($false, $true)) {
        Case "flag.OutputAsDate.$v.r8" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.OutputAsDate = $v; Pass1 $f 45366.4375 }
        Case "flag.OutputAsDate.$v.datenum" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.OutputAsDate = $v; Make $f 'datenum' }
        Case "flag.OutputAsDate.$v.datetime" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.OutputAsDate = $v; Make $f 'datetime' }
        Case "flag.OutputAsDate.$v.char" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.OutputAsDate = $v; Make $f 'char' }
        Case "flag.OutputAsDate.$v.small" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.OutputAsDate = $v; Pass1 $f 0.5 }
    }
    foreach ($bias in @(0, 693960, 1)) {
        Case "flag.DateBias.$bias.datein" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = $bias; Echo1 $f ([datetime]'2024-03-15 10:30:00') }
        Case "flag.DateBias.$bias.dateout" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = $bias; $f.MWFlags.DataConversionFlags.OutputAsDate = $true; Pass1 $f 45366.4375 }
        Case "flag.DateBias.$bias.datetimeout" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = $bias; Make $f 'datetime' }
    }
    foreach ($v in @(0, 1, 2)) {
        Case "flag.ReplaceMissing.$v.arr" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; Echo1 $f ([object[]]@(1.0, $null, 3.0)) }
        Case "flag.ReplaceMissing.$v.scalar" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; Echo1 $f $null }
        Case "flag.ReplaceMissing.$v.range" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; Echo1 $f ($ws.Range('H1:I2')) }
        Case "flag.ReplaceMissing.$v.rangeerror" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; Echo1 $f ($ws.Range('F1:G1')) }
    }
    foreach ($v in @(0, 1, 2, 3)) {
        Case "flag.ReplaceMatlabNaN.$v.nan" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; Make $f 'nan' }
        Case "flag.ReplaceMatlabNaN.$v.nanmat" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; Make $f 'nanmat' }
        Case "flag.ReplaceMatlabNaN.$v.inf" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; Make $f 'inf' }
        Case "flag.ReplaceMatlabNaN.$v.range" { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; RangeOut 'K1' 'nanmat' { $o.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v } }
    }
    $o.MWFlags.DataConversionFlags.ReplaceMatlabNaN = 0
    Case 'flag.bad.InputArrayFormat' { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 9; $f.MWFlags.ArrayFormatFlags.InputArrayFormat }
    Case 'flag.bad.CoerceNumericToType.set' { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = 99; $f.MWFlags.DataConversionFlags.CoerceNumericToType }
    Case 'flag.bad.DateBias' { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = -5; $f.MWFlags.DataConversionFlags.DateBias }

    # --- the utility library's helpers ------------------------------------------------------------------------
    Case 'util.MWPack.three' { $p = $null; $u.MWPack([ref]$p, 1.0, 'two', ([double[]]@(3, 4))); Show $p }
    Case 'util.MWPack.none' { $p = $null; $u.MWPack([ref]$p); Show $p }
    Case 'util.MWPack.one' { $p = $null; $u.MWPack([ref]$p, 5.0); Show $p }
    Case 'util.MWPack.withempty' { $p = $null; $u.MWPack([ref]$p, 1.0, $null, 3.0); Show $p }
    Case 'util.MWPack.range' { $p = $null; $u.MWPack([ref]$p, ($ws.Range('A1:B2'))); Show $p }
    Case 'util.MWUnpack.plain' { $p = $null; $u.MWPack([ref]$p, 1.0, 'two', 3.0); $a = $null; $b = $null; $c = $null; $u.MWUnpack($p, 0, $false, [ref]$a, [ref]$b, [ref]$c); '{0} | {1} | {2}' -f (Show $a), (Show $b), (Show $c) }
    Case 'util.MWUnpack.start1' { $p = $null; $u.MWPack([ref]$p, 1.0, 'two', 3.0); $a = $null; $b = $null; $u.MWUnpack($p, 1, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
    Case 'util.MWUnpack.fewer' { $p = $null; $u.MWPack([ref]$p, 1.0, 'two', 3.0); $a = $null; $u.MWUnpack($p, 0, $false, [ref]$a); Show $a }
    Case 'util.MWUnpack.more' { $p = $null; $u.MWPack([ref]$p, 1.0); $a = $null; $b = $null; $u.MWUnpack($p, 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
    Case 'util.MWUnpack.notpacked' { $a = $null; $u.MWUnpack(5.0, 0, $false, [ref]$a); Show $a }
    Case 'util.MWUnpack.array' { $a = $null; $b = $null; $u.MWUnpack(([object[]]@(7.0, 'x')), 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
    Case 'util.MWUnpack.range' { $ws.Range('K1:P20').Clear() | Out-Null; $p = $null; $u.MWPack([ref]$p, (Matrix2 @(1, 2, 3, 4) 2 2), 'x'); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); $cells = @(); foreach ($cell in $ws.Range('K1:L2').Cells) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) }; $cells -join ', ' }
    Case 'util.MWUnpack.range.noresize' { $ws.Range('K1:P20').Clear() | Out-Null; $p = $null; $u.MWPack([ref]$p, (Matrix2 @(1, 2, 3, 4) 2 2)); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $false, [ref]$r); $cells = @(); foreach ($cell in $ws.Range('K1:L2').Cells) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) }; $cells -join ', ' }
    Case 'util.MWDate2VariantDate.datenum' { $d = 739326.4375; $u.MWDate2VariantDate([ref]$d); Show $d }
    Case 'util.MWDate2VariantDate.small' { $d = 0.5; $u.MWDate2VariantDate([ref]$d); Show $d }
    Case 'util.MWDate2VariantDate.array' { $d = [double[]]@(739326.4375, 739327.0); $u.MWDate2VariantDate([ref]$d); Show $d }
    Case 'util.MWDate2VariantDate.text' { $d = 'x'; $u.MWDate2VariantDate([ref]$d); Show $d }
    Case 'util.MWDate2VariantDate.date' { $d = [datetime]'2024-03-15'; $u.MWDate2VariantDate([ref]$d); Show $d }
    Case 'util.CreateRemoteObject' { $r = $u.CreateRemoteObject($ProgId); Call $r 'k17_one' 1 1 (A 21.0) }
    Case 'util.CreateRemoteObject.unknown' { $r = $u.CreateRemoteObject('No.Such.ProgId'); Show $r }
    Case 'util.MWInitApplicationWithMCROptions' { $u.MWInitApplicationWithMCROptions($xl, ([object[]]@('-nojvm'))); 'ok' }
    Case 'util.members' { (@($u | Get-Member -MemberType Property, Method | ForEach-Object { $_.Name }) -join ',') }

    # --- errors ---------------------------------------------------------------------------------------------
    foreach ($kind in @('id', 'plain', 'multi', 'nonascii', 'nested', 'warn', 'nargs', 'undefined', 'index', 'none')) {
        Case "err.$kind" { Call $o 'k17_err' 1 1 (A $kind) }
    }
    Case 'err.nooutputs.fail' { CallNoOut $o 'k17_noout' (A (Fresh 'MWComUtil.MWFlags')) }
    Case 'err.badcount.more' { Call $o 'k17_one' 1 1 (A 1.0 2.0) }
    Case 'err.badcount.fewer' { $o.k17_one(1); 'ok' }
    Case 'err.badcount.fewer.dispatch' { $a = New-Object object[] 1; $a[0] = 1; [void]$o.GetType().InvokeMember('k17_one', [System.Reflection.BindingFlags]::InvokeMethod, $null, $o, $a); 'ok' }
    Case 'err.unknownmethod' { CallNoOut $o 'no_such_method' (A) }
    Case 'err.argsblock.negative' { Call $o 'k17_args' 1 1 (A ([double[]]@(1, -2)) $missing) }
    Case 'err.argsblock.text' { Call $o 'k17_args' 1 1 (A 'x' $missing) }
    Case 'err.argsblock.toomany' { Call $o 'k17_args' 1 1 (A ([double[]]@(1, 2)) 3.0) }
    Case 'err.defaultmiddle' { Call $o 'k17_many' 3 3 (A 1.0 $missing 3.0) }
    Case 'err.emptymiddle' { Call $o 'k17_many' 3 3 (A 1.0 $null 3.0) }
    Case 'err.alldefault' { Call $o 'k17_many' 3 3 (A $missing $missing $missing) }
    Case 'err.unconvertible' { Call $o 'k17_one' 1 1 (A $dict) }
    Case 'err.nargouttext' { Call $o 'k17_one' 1 1 (A 'abc') }
    Case 'err.slotnotbyref' { $y = $null; $o.k17_one(1, $y, 2.0); Show $y }
    Case 'err.make.unknown' { Make $o 'nosuchkind' }
    Case 'err.errorinfo.lastwarn' { Call $o 'k17_err' 1 1 (A 'warn') }
    # The text a refusal of CallComFcn's own carries: none of its own, the last MATLAB error's instead
    # (seen by hand: a defaulted middle argument right after k17_err reported k17_err's text).
    Case 'stale.1.error' { Call $o 'k17_err' 1 1 (A 'id') }
    Case 'stale.2.defaultmiddle' { Call $o 'k17_many' 3 3 (A 1.0 $missing 3.0) }
    Case 'stale.3.i8' { Echo1 $o ([int64]5) }
    Case 'stale.4.ok' { Call $o 'k17_one' 1 1 (A 2.0) }
    Case 'stale.5.defaultmiddle.afterok' { Call $o 'k17_many' 3 3 (A 1.0 $missing 3.0) }
    Case 'stale.6.notenough' { CallNoOut $o 'k17_noout' (A $missing) }
    Case 'stale.7.defaultmiddle.afternotenough' { Call $o 'k17_many' 3 3 (A 1.0 $missing 3.0) }

    # --- figures ----------------------------------------------------------------------------------------------
    Case 'plot.invisible' { Call $o 'k17_plot' 1 1 (A $missing) }
    Case 'plot.invisible.again' { Call $o 'k17_plot' 1 1 (A 'off') }

    # --- the other builds' ProgIDs ---------------------------------------------------------------------------------
    foreach ($id in ($OtherProgIds -split ',' | Where-Object { $_ })) {
        Case "progid.$id" { $x = Fresh $id; $m = @($x | Get-Member -MemberType Method | ForEach-Object { $_.Name }); if ($m -contains 'k17_one') { Call $x 'k17_one' 1 1 (A 2.0) } else { "members [$($m -join ',')]" } }
        Case "progid.$id.count" { $x = Fresh $id; $m = @($x | Get-Member -MemberType Method | ForEach-Object { $_.Name }); if ($m -contains 'k17_count') { Call $x 'k17_count' 1 1 (A) } else { 'no k17_count' } }
    }
    Case 'progid.versionless.sameclass' { $x = Fresh 'k17xl.K17Xl'; Call $x 'k17_count' 1 1 (A) }

    # --- termination ------------------------------------------------------------------------------------------------
    Case 'terminate' { $u.MWTerminateApplication(); 'ok' }
    Case 'util.IsMCRInitialized.terminated' { $u.IsMCRInitialized() }
    Case 'terminate.twice' { $u.MWTerminateApplication(); 'ok' }
    Case 'init.after.terminate' { $u.MWInitApplication($xl); 'ok' }
    Case 'util.IsMCRInitialized.reinit' { $u.IsMCRInitialized() }
    # Last, with Excel gone first: a call after MWTerminateApplication ended the host with an access
    # violation in mclFeval (the third main run); nothing after it would run.
    try { $wb.Close($false) } catch { }
    try { $xl.Quit() } catch { }
    try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) } catch { }
    $wb = $null; $xl = $null
    Say 'excel.quit' 'ok'
    Say 'next' 'call.after.terminate: k17_one on the object after MWTerminateApplication; the process ends here if it crashes'
    Case 'call.after.terminate' { Call $o 'k17_one' 1 1 (A 2.0) }
    Case 'create.after.terminate' { $y = Fresh $ProgId; Call $y 'k17_one' 1 1 (A 2.0) }
}
catch {
    Say 'FATAL' (Err $_.Exception)
    Say 'FATAL.where' $_.ScriptStackTrace
}
finally {
    try { if ($wb) { $wb.Close($false) } } catch { }
    try { if ($xl) { $xl.Quit() } } catch { }
    try { if ($xl) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) } } catch { }
    Say 'seconds' ('{0:N1}' -f $watch.Elapsed.TotalSeconds)
    $writer.Close()
}
