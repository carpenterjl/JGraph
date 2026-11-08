# The extra rows of stage K17d (compiler and SDK plan), dot-sourced by k17com.ps1 -Phase extra after
# the component $o, the utility object $u and the invisible Excel's worksheet $ws are up: the flags'
# effect on shapes the main oracle did not vary, Range objects in and out in more forms, and the
# utility library's value classes (MWStruct, MWComplex, MWSparse, MWArg, MWField) and MWUtil's methods
# in detail. Each row is one line of k17_com_extra.out.txt; record-k17d.ps1 records R2025b's, run-k17.ps1
# JGraph's. The rows that ended R2025b's host in the main oracle's development (an error cell inside a
# Variant array, CoerceNumericToType 1) are not here; the ones that might (a bad sparse index) are last.

$missing = [System.Reflection.Missing]::Value
$d1 = [datetime]'2024-03-15 10:30:00'
$d2 = [datetime]'2024-03-16 00:00:00'
if ($ws) {
    $ws.Range('G2').Value2 = $d2
    $ws.Range('R1').Value2 = 'a'; $ws.Range('R2').Value2 = 'b'
    $ws.Range('S1').Value2 = $true; $ws.Range('S2').Value2 = $false
    $ws.Range('T1').Value2 = 1; $ws.Range('T3').Value2 = 3
    $ws.Range('U1').Value2 = 'ab'; $ws.Range('U2').Value2 = 'cd'
    $ws.Range('V1').Value2 = 1.5; $ws.Range('V2').Value2 = 'two'
    $ws.Range('W1').Formula = '=1/0'; $ws.Range('W2').Value2 = 5
}
# A fresh component object with its flags set by $set (a script block taking the object).
function FX([scriptblock] $set) { $f = Fresh $ProgId; if ($set) { $null = & $set $f }; return $f }
# An output written into a Range by a fresh object with its flags set by $setup: the slot after the
# call and the cells of K1:P20 that hold something.
function RangeOutX([string] $addr, [string] $kind, [scriptblock] $setup) {
    $ws.Range('K1:P20').Clear() | Out-Null
    $f = FX $setup
    $slot = $ws.Range($addr)
    $f.k17_make(1, [ref]$slot, $kind)
    $cells = @()
    foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot={0} cells {{{1}}}' -f (Show $slot), ($cells -join ', ')
}
function Jagged() { , ([object[]]@([object[]]@(1.0, 2.0), [object[]]@(3.0, 4.0))) }
function Packed([object[]] $items) {
    $p = $null
    $script:__packItems = $items
    $parts = @('[ref]$script:__packed')
    for ($j = 0; $j -lt $items.Count; $j++) { Set-Variable -Name "__pk$j" -Scope Script -Value (Raw $items[$j]); $parts += "`$script:__pk$j" }
    $script:__packed = $null
    Invoke-Expression ('$u.MWPack(' + ($parts -join ', ') + ')') | Out-Null
    , $script:__packed
}

# --- InputArrayFormat over more shapes ----------------------------------------------------------------
foreach ($v in @(0, 1, 2)) {
    Case "x.InputArrayFormat.$v.bstr" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'text' }
    Case "x.InputArrayFormat.$v.bool" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) $true }
    Case "x.InputArrayFormat.$v.date" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) $d1 }
    Case "x.InputArrayFormat.$v.r8_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([double[]]@(1, 2, 3)) }
    Case "x.InputArrayFormat.$v.r8_3d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) (New-Object 'double[,,]' 2, 2, 2) }
    Case "x.InputArrayFormat.$v.i4_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([int[]]@(1, 2)) }
    Case "x.InputArrayFormat.$v.bool_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([bool[]]@($true, $false)) }
    Case "x.InputArrayFormat.$v.str_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([string[]]@('a', 'bb')) }
    Case "x.InputArrayFormat.$v.var_2d_numbers" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) (VarMatrix2 @(1.0, 2.0, 3.0, 4.0) 2 2) }
    Case "x.InputArrayFormat.$v.var_1d_bools" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@($true, $false)) }
    Case "x.InputArrayFormat.$v.var_1d_strings" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@('a', 'bb')) }
    Case "x.InputArrayFormat.$v.var_1d_chars" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@('a', 'b')) }
    Case "x.InputArrayFormat.$v.var_2d_strings" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) (VarMatrix2 @('a', 'b', 'c', 'd') 2 2) }
    Case "x.InputArrayFormat.$v.var_with_empty" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@(1.0, $null, 3.0)) }
    Case "x.InputArrayFormat.$v.var_ints" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@([int]1, [int16]2)) }
    Case "x.InputArrayFormat.$v.var_i4s" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@([int]1, [int]2)) }
    Case "x.InputArrayFormat.$v.jagged" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) (Jagged) }
    Case "x.InputArrayFormat.$v.jagged_r8" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ([object[]]@([double[]]@(1, 2), [double[]]@(3, 4))) }
    Case "x.InputArrayFormat.$v.onebased" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) (OneBased @(1.0, 2.0, 3.0, 4.0) 2 2) }
    Case "x.InputArrayFormat.$v.range_col" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ($ws.Range('D1:D3')) }
    Case "x.InputArrayFormat.$v.range_one" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ($ws.Range('A1')) }
    Case "x.InputArrayFormat.$v.range_text" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ($ws.Range('B1')) }
    Case "x.InputArrayFormat.$v.range_charcol" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) ($ws.Range('R1:R2')) }
    Case "x.InputArrayFormat.$v.varin_vararray" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'k17_varin' 1 1 (A ([object[]]@(1.0, 'a'))) }
    Case "x.InputArrayFormat.$v.varin_r8" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'k17_varin' 1 1 (A ([double[]]@(1, 2))) }
    Case "x.InputArrayFormat.$v.varin_packed_inner" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'k17_varin' 1 1 (A (Packed @(1.0, ([double[]]@(3, 4)), ([object[]]@(5.0, 6.0))))) }
    Case "x.InputArrayFormat.$v.both_packed" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'k17_both' 3 2 (A 7.0 (Packed @('a', 2.0))) }
    Case "x.InputArrayFormat.$v.opt_array_last" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $v }) 'k17_opt' 1 1 (A 1.0 'b' ([object[]]@(1.0, 2.0))) }
}

# --- InputArrayIndFlag under each format ---------------------------------------------------------------
foreach ($ind in @(0, 1, 2)) {
    foreach ($fmt in @(0, 1, 2)) {
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.scalar" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) 5.0 }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.bstr" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) 'text' }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.r8_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) ([double[]]@(1, 2)) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.r8_2d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) (Matrix2 @(1, 2, 3, 4) 2 2) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.var_1d" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) ([object[]]@(1.0, 2.0)) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.var_mixed" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) ([object[]]@(1.0, 'a')) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.jagged" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) (Jagged) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.range" { Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) ($ws.Range('D1:D3')) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.varin" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) 'k17_varin' 1 1 (A ([object[]]@(1.0, 'a'))) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.varin_r8" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) 'k17_varin' 1 1 (A ([double[]]@(1, 2))) }
        Case "x.InputArrayIndFlag.$ind.fmt$fmt.varin_packed" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.InputArrayIndFlag = $ind }) 'k17_varin' 1 1 (A (Packed @(1.0, ([double[]]@(3, 4)), ([object[]]@(5.0, 6.0))))) }
    }
}

# --- OutputArrayFormat and OutputArrayIndFlag over more kinds -------------------------------------------
foreach ($kind in @('doublerow', 'doublecol', 'double3d', 'logical', 'logicalmat', 'logicalempty', 'charone', 'charmat', 'charcol', 'charempty', 'cellnested', 'cellcol', 'cellempty', 'cellofempty',
        'nan', 'nanmat', 'inf', 'empty', 'emptyrow', 'int8', 'uint8mat', 'single', 'bigint', 'int64', 'string', 'structarr', 'structnested', 'structempty', 'structnofields', 'complex', 'complexmat', 'sparse', 'sparselogical', 'datenum')) {
    Case "x.OutputArrayFormat.2.$kind" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }) $kind }
}
foreach ($kind in @('doublemat', 'cell', 'double', 'struct', 'char', 'cellnested')) {
    Case "x.OutputArrayFormat.3.$kind" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 3 }) $kind }
}
foreach ($ind in @(0, 1, 2)) {
    foreach ($fmt in @(0, 1, 2)) {
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.doublemat" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'doublemat' }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.double" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'double' }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.cell" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'cell' }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.cellnested" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'cellnested' }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.char" { Make (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'char' }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.varout" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'k17_varout' 3 1 (A 5.0) }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.varout_arr" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'k17_varout' 2 1 (A ([double[]]@(1, 2))) }
        Case "x.OutputArrayIndFlag.$ind.fmt$fmt.both" { Call (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = $fmt; $f.MWFlags.ArrayFormatFlags.OutputArrayIndFlag = $ind }) 'k17_both' 3 2 (A 7.0 ([object[]]@('a', ([double[]]@(1, 2))))) }
    }
}

# --- CoerceNumericToType over more values --------------------------------------------------------------
foreach ($t in @(0, 3, 4, 6, 7, 8, 9, 10, 12, 13)) {
    Case "x.Coerce.$t.half" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([double[]]@(0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 3.5, 0.49999)) }
    Case "x.Coerce.$t.huge" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([double[]]@(1e10, -1e10, 0, 65535.5)) }
    Case "x.Coerce.$t.naninf" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([double[]]@([double]::NaN, [double]::PositiveInfinity, [double]::NegativeInfinity)) }
    Case "x.Coerce.$t.r8_2d" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) (Matrix2 @(1.5, 2.5, 3.5, 4.5) 2 2) }
    Case "x.Coerce.$t.var_mixed" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([object[]]@(1.0, 'a', $true)) }
    Case "x.Coerce.$t.var_numbers" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([object[]]@(1.5, 2.5)) }
    Case "x.Coerce.$t.var_bools" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([object[]]@($true, $false)) }
    Case "x.Coerce.$t.jagged" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) (Jagged) }
    Case "x.Coerce.$t.date" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) $d1 }
    Case "x.Coerce.$t.range" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ($ws.Range('D1:D3')) }
    Case "x.Coerce.$t.range_mixed" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ($ws.Range('A1:B2')) }
    Case "x.Coerce.$t.range_text" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ($ws.Range('B1')) }
    Case "x.Coerce.$t.err" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) (ErrorCell 0x800A07FA) }
    Case "x.Coerce.$t.i2" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([int16]-3) }
    Case "x.Coerce.$t.ui1" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([byte]200) }
    Case "x.Coerce.$t.r4" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([single]1.5) }
    Case "x.Coerce.$t.decimal" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([decimal]2.25) }
    Case "x.Coerce.$t.cy" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) (New-Object System.Runtime.InteropServices.CurrencyWrapper ([decimal]1.5)) }
    Case "x.Coerce.$t.char" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([char]'c') }
    Case "x.Coerce.$t.bool_false" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) $false }
    Case "x.Coerce.$t.zero" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 0.0 }
    Case "x.Coerce.$t.empty" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 'k17_opt' 1 1 (A 1.0 $null $missing) }
    Case "x.Coerce.$t.dbnull" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) ([System.DBNull]::Value) }
    Case "x.Coerce.$t.bstr_number" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) '42' }
    Case "x.Coerce.$t.pass" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 3.7 }
    Case "x.Coerce.$t.make" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 'double' }
    Case "x.Coerce.$t.make_logical" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 'logical' }
    Case "x.Coerce.$t.varin" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = $t }) 'k17_varin' 1 1 (A ([object[]]@(1.5, 'a'))) }
}

# --- dates ----------------------------------------------------------------------------------------------
foreach ($v in @(0, 1)) {
    Case "x.InputDateFormat.$v.date_arr" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([datetime[]]@($d1, $d2)) }
    Case "x.InputDateFormat.$v.var_with_date" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([object[]]@(1.0, $d1, 3.0)) }
    Case "x.InputDateFormat.$v.var_dates" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([object[]]@($d1, $d2)) }
    Case "x.InputDateFormat.$v.range_two" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ($ws.Range('G1:G2')) }
    Case "x.InputDateFormat.$v.date_early" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([datetime]'1850-06-01') }
    Case "x.InputDateFormat.$v.date_midnight" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([datetime]'2024-03-15') }
    Case "x.InputDateFormat.$v.date_timeonly" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([datetime]'1899-12-30 06:00:00') }
    Case "x.InputDateFormat.$v.date_seconds" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) ([datetime]'2024-03-15 23:59:59') }
    Case "x.InputDateFormat.$v.bias0" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v; $f.MWFlags.DataConversionFlags.DateBias = 0 }) $d1 }
    Case "x.InputDateFormat.$v.coerce12" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v; $f.MWFlags.DataConversionFlags.CoerceNumericToType = 12 }) $d1 }
    Case "x.InputDateFormat.$v.pass" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) $d1 }
    Case "x.InputDateFormat.$v.varin" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = $v }) 'k17_varin' 1 1 (A ([object[]]@($d1, 2.0))) }
}
foreach ($v in @($false, $true)) {
    Case "x.OutputAsDate.$v.doublerow" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) ([double[]]@(739326.4375, 739327.0)) }
    Case "x.OutputAsDate.$v.doublemat" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'doublemat' }
    Case "x.OutputAsDate.$v.int32" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'int32' }
    Case "x.OutputAsDate.$v.single" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'single' }
    Case "x.OutputAsDate.$v.logical" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'logical' }
    Case "x.OutputAsDate.$v.cell" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) ([object[]]@(739326.4375, 'x')) }
    Case "x.OutputAsDate.$v.nan" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'nan' }
    Case "x.OutputAsDate.$v.inf" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'inf' }
    Case "x.OutputAsDate.$v.mixedrange" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) ([double[]]@(739326.4375, 0.5)) }
    Case "x.OutputAsDate.$v.neg" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) -5.0 }
    Case "x.OutputAsDate.$v.zero" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 0.0 }
    Case "x.OutputAsDate.$v.huge" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 1e7 }
    Case "x.OutputAsDate.$v.maxole" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 3652425.0 }
    Case "x.OutputAsDate.$v.pastmax" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 3652426.0 }
    Case "x.OutputAsDate.$v.minole" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 36526.0 }
    Case "x.OutputAsDate.$v.belowmin" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 36525.0 }
    Case "x.OutputAsDate.$v.yearone" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 693961.0 }
    Case "x.OutputAsDate.$v.fraction" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 739326.999999 }
    Case "x.OutputAsDate.$v.varout" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v }) 'k17_varout' 2 1 (A 739326.4375) }
    Case "x.OutputAsDate.$v.range" { RangeOutX 'K1' 'datenum' { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v } }
    Case "x.OutputAsDate.$v.cellformat" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $v; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }) 'datenum' }
}
Case 'x.DateBias.0.datenum_out' { Make (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = 0; $f.MWFlags.DataConversionFlags.OutputAsDate = $true }) 'datenum' }
Case 'x.DateBias.0.range_in' { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = 0 }) ($ws.Range('G1')) }
Case 'x.DateBias.0.date_arr' { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = 0 }) ([datetime[]]@($d1, $d2)) }
Case 'x.DateBias.neg.datein' { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = -5 }) $d1 }
Case 'x.DateBias.neg.dateout' { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = -5; $f.MWFlags.DataConversionFlags.OutputAsDate = $true }) 45366.4375 }
Case 'x.DateBias.big.dateout' { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.DateBias = 1000000; $f.MWFlags.DataConversionFlags.OutputAsDate = $true }) 1045366.5 }

# --- ReplaceMissing and ReplaceMatlabNaN over more shapes ---------------------------------------------------
foreach ($v in @(0, 1)) {
    Case "x.ReplaceMissing.$v.var_2d_empty" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) (VarMatrix2 @(1.0, $null, $null, 4.0) 2 2) }
    Case "x.ReplaceMissing.$v.var_1d_dbnull" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([object[]]@(1.0, [System.DBNull]::Value, 3.0)) }
    Case "x.ReplaceMissing.$v.var_1d_missing" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([object[]]@(1.0, $missing, 3.0)) }
    Case "x.ReplaceMissing.$v.var_all_empty" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([object[]]@($null, $null)) }
    Case "x.ReplaceMissing.$v.var_mixed_empty" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([object[]]@(1.0, $null, 'a')) }
    Case "x.ReplaceMissing.$v.var_bools_empty" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([object[]]@($true, $null)) }
    Case "x.ReplaceMissing.$v.range_num_blank_num" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('T1:T3')) }
    Case "x.ReplaceMissing.$v.range_blank_one" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('H1')) }
    Case "x.ReplaceMissing.$v.range_blank_row" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('H1:I1')) }
    Case "x.ReplaceMissing.$v.range_text_blank" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('B1:C1')) }
    Case "x.ReplaceMissing.$v.range_bool_blank" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('S2:T2')) }
    Case "x.ReplaceMissing.$v.range_cellformat" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2 }) ($ws.Range('T1:T3')) }
    Case "x.ReplaceMissing.$v.range_asis" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 0 }) ($ws.Range('T1:T3')) }
    Case "x.ReplaceMissing.$v.range_value2" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ($ws.Range('T1:T3').Value2) }
    Case "x.ReplaceMissing.$v.empty_middle" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) 'k17_opt' 1 1 (A 1.0 $null $true) }
    Case "x.ReplaceMissing.$v.dbnull_scalar" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v }) ([System.DBNull]::Value) }
    Case "x.ReplaceMissing.$v.coerce8" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = $v; $f.MWFlags.DataConversionFlags.CoerceNumericToType = 8 }) ($ws.Range('T1:T3')) }
}
foreach ($v in @(0, 1, 2)) {
    Case "x.ReplaceMatlabNaN.$v.nanrow" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v }) ([double[]]@(1, [double]::NaN, 3)) }
    Case "x.ReplaceMatlabNaN.$v.nan_in_cell" { Pass1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v }) ([object[]]@(1.0, [double]::NaN, 'x')) }
    Case "x.ReplaceMatlabNaN.$v.nan_scalar_in" { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v }) ([double]::NaN) }
    Case "x.ReplaceMatlabNaN.$v.neginf" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v }) 'neginf' }
    Case "x.ReplaceMatlabNaN.$v.varout" { Call (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v }) 'k17_varout' 2 1 (A ([double]::NaN)) }
    Case "x.ReplaceMatlabNaN.$v.cellformat" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }) 'nanmat' }
    Case "x.ReplaceMatlabNaN.$v.asdate" { Make (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; $f.MWFlags.DataConversionFlags.OutputAsDate = $true }) 'nan' }
    Case "x.ReplaceMatlabNaN.$v.range_scalar" { RangeOutX 'K1' 'nan' { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v } }
    Case "x.ReplaceMatlabNaN.$v.range_row" { RangeOutX 'K1' 'nanmat' { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = $v; $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
}

# --- Range objects in ---------------------------------------------------------------------------------
Case 'x.range.charcol' { Echo1 $o ($ws.Range('R1:R2')) }
Case 'x.range.boolcol' { Echo1 $o ($ws.Range('S1:S2')) }
Case 'x.range.num_blank_num' { Echo1 $o ($ws.Range('T1:T3')) }
Case 'x.range.strings_col' { Echo1 $o ($ws.Range('U1:U2')) }
Case 'x.range.col_mixed' { Echo1 $o ($ws.Range('V1:V2')) }
Case 'x.range.row_mixed' { Echo1 $o ($ws.Range('A1:B1')) }
Case 'x.range.error_and_number' { Echo1 $o ($ws.Range('W1:W2')) }
Case 'x.range.text_one' { Echo1 $o ($ws.Range('B1')) }
Case 'x.range.bool_one' { Echo1 $o ($ws.Range('A2')) }
Case 'x.range.blank_2x1' { Echo1 $o ($ws.Range('H1:H2')) }
Case 'x.range.dates' { Echo1 $o ($ws.Range('G1:G2')) }
Case 'x.range.date_and_number' { Echo1 $o ($ws.Range('G1:H1')) }
Case 'x.range.pass_col' { Pass1 $o ($ws.Range('D1:D3')) }
Case 'x.range.pass_one' { Pass1 $o ($ws.Range('A1')) }
Case 'x.range.pass_text' { Pass1 $o ($ws.Range('B1')) }
Case 'x.range.pass_blank' { Pass1 $o ($ws.Range('H1')) }
Case 'x.range.pass_error' { Pass1 $o ($ws.Range('F1')) }
Case 'x.range.pass_date' { Pass1 $o ($ws.Range('G1')) }
Case 'x.range.opt_trailing_blank' { Call $o 'k17_opt' 1 1 (A 1.0 ($ws.Range('H1')) $missing) }
Case 'x.range.opt_middle_blank' { Call $o 'k17_opt' 1 1 (A 1.0 ($ws.Range('H1')) $true) }
Case 'x.range.cells_property' { Echo1 $o ($ws.Range('A1:B2').Cells) }
Case 'x.range.rows_property' { Echo1 $o ($ws.Range('A1:B2').Rows) }
Case 'x.range.noout' { CallNoOut $o 'k17_noout' (A ($ws.Range('A1:B2'))) }
Case 'x.range.worksheet' { Echo1 $o $ws }
Case 'x.range.application' { Echo1 $o $xl }
Case 'x.range.workbook' { Echo1 $o $wb }

# --- Range objects out --------------------------------------------------------------------------------------
foreach ($kind in @('logical', 'logicalempty', 'cellnested', 'cellmat', 'cellcol', 'cellempty', 'cellofempty', 'int8', 'uint8mat', 'double3d', 'charcol', 'charone', 'charnewline', 'charnonascii', 'emptyrow', 'emptycol',
        'string', 'stringarr', 'int64', 'bigint', 'complexmat', 'complexzero', 'sparse', 'sparselogical', 'sparseempty', 'inf', 'neginf', 'single', 'singlemat', 'structarr', 'structempty', 'datetimearr', 'duration', 'fhandle', 'object')) {
    Case "x.rangeout.$kind" { RangeOutX 'K1' $kind }
}
Case 'x.rangeout.string_into_2x2' { RangeOutX 'K1:L2' 'char' }
Case 'x.rangeout.scalar_into_2x2' { RangeOutX 'K1:L2' 'double' }
Case 'x.rangeout.scalar_into_row' { RangeOutX 'K1:M1' 'double' }
Case 'x.rangeout.row_into_col' { RangeOutX 'K1:K3' 'doublerow' }
Case 'x.rangeout.col_into_row' { RangeOutX 'K1:M1' 'doublecol' }
Case 'x.rangeout.matrix_into_wide' { RangeOutX 'K1:P1' 'doublemat' }
Case 'x.rangeout.empty_into_2x2' { RangeOutX 'K1:L2' 'empty' }
Case 'x.rangeout.nan_into_2x2' { RangeOutX 'K1:L2' 'nan' }
Case 'x.rangeout.cell_into_2x2' { RangeOutX 'K1:L2' 'cell' }
Case 'x.rangeout.datenum_asdate' { RangeOutX 'K1' 'datenum' { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $true } }
Case 'x.rangeout.datenum_plain' { RangeOutX 'K1' 'datenum' }
Case 'x.rangeout.noresize.row_into_2cells' { RangeOutX 'K1:L1' 'doublerow' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.col' { RangeOutX 'K1' 'doublecol' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.fit' { RangeOutX 'K1:L2' 'doublemat' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.larger' { RangeOutX 'K1:M3' 'doublemat' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.scalar_into_2x2' { RangeOutX 'K1:L2' 'double' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.cell' { RangeOutX 'K1' 'cell' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.noresize.char' { RangeOutX 'K1' 'char' { param($f) $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.transpose.col' { RangeOutX 'K1' 'doublecol' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.scalar' { RangeOutX 'K1' 'double' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.cell' { RangeOutX 'K1' 'cell' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.charmat' { RangeOutX 'K1' 'charmat' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.char' { RangeOutX 'K1' 'char' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.3d' { RangeOutX 'K1' 'double3d' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true } }
Case 'x.rangeout.transpose.noresize' { RangeOutX 'K1' 'doublerow' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true; $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.transpose.noresize_fit' { RangeOutX 'K1:K3' 'doublerow' { param($f) $f.MWFlags.ArrayFormatFlags.TransposeOutput = $true; $f.MWFlags.ArrayFormatFlags.AutoResizeOutput = $false } }
Case 'x.rangeout.cellformat.doublemat' { RangeOutX 'K1' 'doublemat' { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 } }
Case 'x.rangeout.cellformat.double' { RangeOutX 'K1' 'double' { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 } }
Case 'x.rangeout.cellformat.struct' { RangeOutX 'K1' 'struct' { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 } }
Case 'x.rangeout.nan_replace2_scalar' { RangeOutX 'K1' 'nan' { param($f) $f.MWFlags.DataConversionFlags.ReplaceMatlabNaN = 2 } }
Case 'x.rangeout.two' {
    $ws.Range('K1:P20').Clear() | Out-Null
    $s1 = $ws.Range('K1'); $s2 = $ws.Range('K5')
    $o.k17_two(2, [ref]$s1, [ref]$s2, (Matrix2 @(1, 2) 1 2))
    $cells = @(); foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot1={0} slot2={1} cells {{{2}}}' -f (Show $s1), (Show $s2), ($cells -join ', ')
}
Case 'x.rangeout.varout' {
    $ws.Range('K1:P20').Clear() | Out-Null
    $s1 = $ws.Range('K1')
    $o.k17_varout(3, [ref]$s1, 5.0)
    $cells = @(); foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot={0} cells {{{1}}}' -f (Show $s1), ($cells -join ', ')
}
Case 'x.rangeout.both' {
    $ws.Range('K1:P20').Clear() | Out-Null
    $s1 = $ws.Range('K1'); $s2 = $ws.Range('K5')
    $o.k17_both(3, [ref]$s1, [ref]$s2, 7.0, ([object[]]@('a', ([double[]]@(1, 2)))))
    $cells = @(); foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot1={0} slot2={1} cells {{{2}}}' -f (Show $s1), (Show $s2), ($cells -join ', ')
}
Case 'x.rangeout.one_slot_scalar_then_matrix' {
    $ws.Range('K1:P20').Clear() | Out-Null
    $s1 = $ws.Range('K1')
    $o.k17_make(1, [ref]$s1, 'double')
    $o.k17_make(1, [ref]$s1, 'doublemat')
    $cells = @(); foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot={0} cells {{{1}}}' -f (Show $s1), ($cells -join ', ')
}
Case 'x.rangeout.prefilled_range_kept_elsewhere' {
    $ws.Range('K1:P20').Clear() | Out-Null
    $ws.Range('K2').Value2 = 99
    $s1 = $ws.Range('K1')
    $o.k17_make(1, [ref]$s1, 'doublecol')
    $cells = @(); foreach ($cell in $ws.Range('K1:P20').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }
    'slot={0} cells {{{1}}}' -f (Show $s1), ($cells -join ', ')
}
Case 'x.rangeout.flags_after' { Flags $o }

# --- MWArg -----------------------------------------------------------------------------------------------
Case 'x.arg.cell_var' { $g = U 'MWArg'; $g.Value = [object[]]@(1.0, 2.0); $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 0; Echo1 $o $g }
Case 'x.arg.cell_var_fmt2' { $g = U 'MWArg'; $g.Value = [object[]]@(1.0, 2.0); $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; Echo1 $o $g }
Case 'x.arg.cell_var2d_fmt2' { $g = U 'MWArg'; $g.Value = (VarMatrix2 @(1.0, 2.0, 3.0, 4.0) 2 2); $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; Echo1 $o $g }
Case 'x.arg.scalar_fmt2' { $g = U 'MWArg'; $g.Value = 5.0; $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; Echo1 $o $g }
Case 'x.arg.coerce_char' { $g = U 'MWArg'; $g.Value = 3.7; $g.MWFlags.DataConversionFlags.CoerceNumericToType = 4; Echo1 $o $g }
Case 'x.arg.coerce_arr' { $g = U 'MWArg'; $g.Value = [double[]]@(1.5, -2.5, 300); $g.MWFlags.DataConversionFlags.CoerceNumericToType = 8; Echo1 $o $g }
Case 'x.arg.date_string' { $g = U 'MWArg'; $g.Value = $d1; $g.MWFlags.DataConversionFlags.InputDateFormat = 1; Echo1 $o $g }
Case 'x.arg.date_bias0' { $g = U 'MWArg'; $g.Value = $d1; $g.MWFlags.DataConversionFlags.DateBias = 0; Echo1 $o $g }
Case 'x.arg.replacemissing' { $g = U 'MWArg'; $g.Value = $ws.Range('T1:T3'); $g.MWFlags.DataConversionFlags.ReplaceMissing = 1; Echo1 $o $g }
Case 'x.arg.range' { $g = U 'MWArg'; $g.Value = $ws.Range('D1:D3'); Echo1 $o $g }
Case 'x.arg.range_fmt2' { $g = U 'MWArg'; $g.Value = $ws.Range('D1:D3'); $g.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; Echo1 $o $g }
Case 'x.arg.struct' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = 1.0; $g = U 'MWArg'; $g.Value = $s; Echo1 $o $g }
Case 'x.arg.unset' { $g = U 'MWArg'; Echo1 $o $g }
Case 'x.arg.unset_middle' { $g = U 'MWArg'; Call $o 'k17_opt' 1 1 (A 1.0 $g $true) }
Case 'x.arg.value_null' { $g = U 'MWArg'; $g.Value = $null; Echo1 $o $g }
Case 'x.arg.value_readback' { $g = U 'MWArg'; $g.Value = [double[]]@(1, 2); Show $g.Value }
Case 'x.arg.value_readback_2d' { $g = U 'MWArg'; $g.Value = (Matrix2 @(1, 2, 3, 4) 2 2); Show $g.Value }
Case 'x.arg.value_readback_unset' { $g = U 'MWArg'; Show $g.Value }
Case 'x.arg.nested' { $i = U 'MWArg'; $i.Value = 3.7; $i.MWFlags.DataConversionFlags.CoerceNumericToType = 8; $g = U 'MWArg'; $g.Value = $i; Echo1 $o $g }
Case 'x.arg.flags_put' { $g = U 'MWArg'; $g.Value = 3.7; $uf = U 'MWFlags'; $uf.DataConversionFlags.CoerceNumericToType = 8; $g.MWFlags = $uf; Echo1 $o $g }
Case 'x.arg.flags_defaults' { $g = U 'MWArg'; Flags $g }
Case 'x.arg.flags_sameobject' { $g = U 'MWArg'; $a = $g.MWFlags; $b = $g.MWFlags; [System.Runtime.InteropServices.Marshal]::ReferenceEquals($a, $b) }
Case 'x.arg.clone' { $g = U 'MWArg'; $g.Value = 3.7; $g.MWFlags.DataConversionFlags.CoerceNumericToType = 8; $c = CloneObj $g; '{0} | {1}' -f (Show $c.Value), $c.MWFlags.DataConversionFlags.CoerceNumericToType }
Case 'x.arg.clone_independent' { $g = U 'MWArg'; $g.Value = 3.7; $c = CloneObj $g; $c.Value = 1.0; Show $g.Value }
Case 'x.arg.component_coerce' { $g = U 'MWArg'; $g.Value = 3.7; Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = 8 }) $g }
Case 'x.arg.component_fmt2' { $g = U 'MWArg'; $g.Value = [double[]]@(1, 2); Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2 }) $g }
Case 'x.arg.pass' { $g = U 'MWArg'; $g.Value = 3.7; Pass1 $o $g }
Case 'x.arg.members' { Members (U 'MWArg') }

# --- MWStruct ------------------------------------------------------------------------------------------
Case 'x.struct.item_name_index' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); (Prop $s 'Item' @('a', 1.0)).Value = 1.0; (Prop $s 'Item' @('a', 2.0)).Value = 2.0; Echo1 $o $s }
Case 'x.struct.item_name_sub2' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); (Prop $s 'Item' @('a', 1.0, 1.0)).Value = 1.0; (Prop $s 'Item' @('a', 1.0, 2.0)).Value = 2.0; Echo1 $o $s }
Case 'x.struct.item_name_sub2_col' { $s = U 'MWStruct'; $s.Initialize([double[]]@(2, 1), [string[]]@('a')); (Prop $s 'Item' @('a', 2.0, 1.0)).Value = 2.0; Echo1 $o $s }
Case 'x.struct.item_index_first' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); (Prop $s 'Item' @(1.0, 'a')).Value = 1.0; Echo1 $o $s }
Case 'x.struct.item_index_only' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Prop $s 'Item' @(1.0)) }
Case 'x.struct.item_unknown' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Prop $s 'Item' @('zz')) }
Case 'x.struct.item_case' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Prop $s 'Item' @('A')) }
Case 'x.struct.item_noargs' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Prop $s 'Item' @()) }
Case 'x.struct.item_out_of_range' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); Show (Prop $s 'Item' @('a', 3.0)) }
Case 'x.struct.item_zero' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); Show (Prop $s 'Item' @('a', 0.0)) }
Case 'x.struct.item_text_index' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); Show (Prop $s 'Item' @('a', '2')) }
Case 'x.struct.item_name_only_on_array' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); Show (Prop $s 'Item' @('a')) }
Case 'x.struct.item_before_init' { $s = U 'MWStruct'; Show (Prop $s 'Item' @('a')) }
Case 'x.struct.item_twice_same' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $f1 = Prop $s 'Item' @('a'); $f2 = Prop $s 'Item' @('a'); $f1.Value = 5.0; '{0} same={1}' -f (Show $f2.Value), [System.Runtime.InteropServices.Marshal]::ReferenceEquals($f1, $f2) }
Case 'x.struct.dims_2d' { $s = U 'MWStruct'; $s.Initialize([double[]]@(2, 2), [string[]]@('a', 'b')); 'nf={0} nd={1} dims={2} names={3}' -f $s.NumberOfFields, $s.NumberOfDims, (Show $s.Dims), (Show $s.FieldNames) }
Case 'x.struct.dims_3' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1, 2), [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims); Echo1 $o $s }
Case 'x.struct.dims_scalar' { $s = U 'MWStruct'; $s.Initialize(2.0, [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.dims_zero' { $s = U 'MWStruct'; $s.Initialize([double[]]@(0, 0), [string[]]@('a')); 'nd={0} dims={1} nf={2}' -f $s.NumberOfDims, (Show $s.Dims), $s.NumberOfFields; Echo1 $o $s }
Case 'x.struct.dims_zero_echo' { $s = U 'MWStruct'; $s.Initialize([double[]]@(0, 0), [string[]]@('a')); Echo1 $o $s }
Case 'x.struct.init_noargs' { $s = U 'MWStruct'; $s.Initialize(); 'nf={0} nd={1} dims={2} names={3}' -f $s.NumberOfFields, $s.NumberOfDims, (Show $s.Dims), (Show $s.FieldNames) }
Case 'x.struct.init_noargs_echo' { $s = U 'MWStruct'; $s.Initialize(); Echo1 $o $s }
Case 'x.struct.init_dims_only' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1)); 'nf={0} nd={1} dims={2} names={3}' -f $s.NumberOfFields, $s.NumberOfDims, (Show $s.Dims), (Show $s.FieldNames) }
Case 'x.struct.init_names_variant' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [object[]]@('a', 'b')); Show $s.FieldNames }
Case 'x.struct.init_names_one_string' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), 'a'); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.init_names_2d' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), (VarMatrix2 @('a', 'b', 'c', 'd') 2 2)); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.init_names_dup' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a', 'a')); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.init_names_bad' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('1bad', 'a b')); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames); Echo1 $o $s }
Case 'x.struct.init_names_number' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [object[]]@(5.0)); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.init_dims_variant' { $s = U 'MWStruct'; $s.Initialize([object[]]@(1.0, 2.0), [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.init_dims_ints' { $s = U 'MWStruct'; $s.Initialize([int[]]@(1, 2), [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.init_dims_text' { $s = U 'MWStruct'; $s.Initialize('x', [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.init_dims_negative' { $s = U 'MWStruct'; $s.Initialize([double[]]@(-1, 1), [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.init_dims_fraction' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1.5, 1), [string[]]@('a')); 'nd={0} dims={1}' -f $s.NumberOfDims, (Show $s.Dims) }
Case 'x.struct.init_twice' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a', 'b')); (Prop $s 'Item' @('a')).Value = 1.0; $s.Initialize([double[]]@(1, 1), [string[]]@('c')); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames); Echo1 $o $s }
Case 'x.struct.field_unset' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a', 'b')); (Prop $s 'Item' @('a')).Value = 1.0; Echo1 $o $s }
Case 'x.struct.field_unset_readback' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Prop $s 'Item' @('a')).Value }
Case 'x.struct.field_name' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Name }
Case 'x.struct.field_flags' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Flags (Prop $s 'Item' @('a')) }
Case 'x.struct.field_value_coerce' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $fl = Prop $s 'Item' @('a'); $fl.MWFlags.DataConversionFlags.CoerceNumericToType = 8; $fl.Value = 3.7; Echo1 $o $s }
Case 'x.struct.field_value_fmt2' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $fl = Prop $s 'Item' @('a'); $fl.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; $fl.Value = [double[]]@(1, 2); Echo1 $o $s }
Case 'x.struct.field_value_range' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = $ws.Range('D1:D3'); Echo1 $o $s }
Case 'x.struct.field_value_complex' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $c = U 'MWComplex'; $c.Real = 1.0; $c.Imag = 2.0; (Prop $s 'Item' @('a')).Value = $c; Echo1 $o $s }
Case 'x.struct.field_value_sparse' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $sp = U 'MWSparse'; $sp.NumRows = 2; $sp.NumColumns = 2; $sp.RowIndex = [double[]]@(1); $sp.ColumnIndex = [double[]]@(2); $sp.Array = [double[]]@(5); (Prop $s 'Item' @('a')).Value = $sp; Echo1 $o $s }
Case 'x.struct.field_value_arg' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $g = U 'MWArg'; $g.Value = 3.7; $g.MWFlags.DataConversionFlags.CoerceNumericToType = 8; (Prop $s 'Item' @('a')).Value = $g; Echo1 $o $s }
Case 'x.struct.field_value_array2d' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = (Matrix2 @(1, 2, 3, 4) 2 2); Echo1 $o $s }
Case 'x.struct.field_value_var' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = [object[]]@(1.0, 'x'); Echo1 $o $s }
Case 'x.struct.field_value_date' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = $d1; Echo1 $o $s }
Case 'x.struct.field_value_bool' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = $true; Echo1 $o $s }
Case 'x.struct.field_value_readback' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = [double[]]@(1, 2); Show (Prop $s 'Item' @('a')).Value }
Case 'x.struct.field_value_struct_array' { $i = U 'MWStruct'; $i.Initialize([double[]]@(1, 2), [string[]]@('x')); (Prop $i 'Item' @('x', 1.0)).Value = 1.0; (Prop $i 'Item' @('x', 2.0)).Value = 2.0; $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = $i; Echo1 $o $s }
Case 'x.struct.clone' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = 1.0; $c = CloneObj $s; (Prop $c 'Item' @('a')).Value = 2.0; '{0} | {1}' -f (Show (Prop $s 'Item' @('a')).Value), (Show (Prop $c 'Item' @('a')).Value) }
Case 'x.struct.pass' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a', 'b')); (Prop $s 'Item' @('a')).Value = 1.0; (Prop $s 'Item' @('b')).Value = 'text'; Pass1 $o $s }
Case 'x.struct.pass_array' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); (Prop $s 'Item' @('a', 1.0)).Value = 1.0; (Prop $s 'Item' @('a', 2.0)).Value = 2.0; Pass1 $o $s }
Case 'x.struct.in_cellformat' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = [double[]]@(1, 2); Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2 }) $s }
Case 'x.struct.in_coerce' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = 3.7; Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = 8 }) $s }
Case 'x.struct.in_datestring' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = $d1; Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.InputDateFormat = 1 }) $s }
Case 'x.struct.members' { Members (U 'MWStruct') }
Case 'x.struct.field_members' { Members (U 'MWField') }
Case 'x.struct.out_item_index' { $s = $null; $o.k17_make(1, [ref]$s, 'structarr'); '{0} | {1} | {2}' -f (Show (Prop $s 'Item' @('a', 1.0)).Value), (Show (Prop $s 'Item' @('a', 2.0)).Value), (Show (Prop $s 'Item' @('b', 1.0, 2.0)).Value) }
Case 'x.struct.out_item_linear_col' { $s = $null; $o.k17_make(1, [ref]$s, 'structcol'); '{0} | {1}' -f (Show (Prop $s 'Item' @('a', 2.0)).Value), (Show (Prop $s 'Item' @('a', 2.0, 1.0)).Value) }
Case 'x.struct.out_item_name_only_on_array' { $s = $null; $o.k17_make(1, [ref]$s, 'structarr'); Show (Prop $s 'Item' @('a')) }
Case 'x.struct.out_numdims' { $s = $null; $o.k17_make(1, [ref]$s, 'structnested'); 'nd={0} dims={1} nf={2} names={3}' -f $s.NumberOfDims, (Show $s.Dims), $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.out_empty_members' { $s = $null; $o.k17_make(1, [ref]$s, 'structempty'); 'nd={0} dims={1} nf={2} names={3}' -f $s.NumberOfDims, (Show $s.Dims), $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.out_nofields_members' { $s = $null; $o.k17_make(1, [ref]$s, 'structnofields'); 'nd={0} dims={1} nf={2} names={3}' -f $s.NumberOfDims, (Show $s.Dims), $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.out_field_flags' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); Flags (Prop $s 'Item' @('a')) }
Case 'x.struct.out_field_name' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); (Prop $s 'Item' @('b')).Name }
Case 'x.struct.out_set_then_pass' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); (Prop $s 'Item' @('a')).Value = 5.0; Pass1 $o $s }
Case 'x.struct.out_init_again' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); $s.Initialize([double[]]@(1, 1), [string[]]@('z')); 'nf={0} names={1}' -f $s.NumberOfFields, (Show $s.FieldNames) }
Case 'x.struct.out_cellformat_arr' { $s = $null; (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }).k17_make(1, [ref]$s, 'structarr'); Show (Prop $s 'Item' @('a', 1.0)).Value }
Case 'x.struct.out_clone' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); $c = CloneObj $s; Show (Prop $c 'Item' @('b')).Value }
Case 'x.struct.out_same_twice' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); $t2 = $null; $o.k17_make(1, [ref]$t2, 'struct'); [System.Runtime.InteropServices.Marshal]::ReferenceEquals($s, $t2) }
Case 'x.struct.out_members' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); Members $s }
Case 'x.struct.out_item_members' { $s = $null; $o.k17_make(1, [ref]$s, 'struct'); Members (Prop $s 'Item' @('a')) }
Case 'x.struct.out_nested_value_type' { $s = $null; $o.k17_make(1, [ref]$s, 'structnested'); $inner = (Prop $s 'Item' @('in')).Value; '{0} nf={1}' -f $inner.GetType().Name, $inner.NumberOfFields }
Case 'x.struct.out_asdate' { $s = $null; (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $true }).k17_make(1, [ref]$s, 'struct'); Show (Prop $s 'Item' @('a')).Value }
Case 'x.struct.out_in_cell' { Make $o 'cellnested' }

# --- MWComplex -------------------------------------------------------------------------------------------
Case 'x.complex.imag_only' { $c = U 'MWComplex'; $c.Imag = 2.0; Echo1 $o $c }
Case 'x.complex.unset' { $c = U 'MWComplex'; Echo1 $o $c }
Case 'x.complex.unset_middle' { $c = U 'MWComplex'; Call $o 'k17_opt' 1 1 (A 1.0 $c $true) }
Case 'x.complex.2d' { $c = U 'MWComplex'; $c.Real = (Matrix2 @(1, 2, 3, 4) 2 2); $c.Imag = (Matrix2 @(5, 6, 7, 8) 2 2); Echo1 $o $c }
Case 'x.complex.ints' { $c = U 'MWComplex'; $c.Real = [int]1; $c.Imag = [int]2; Echo1 $o $c }
Case 'x.complex.singles' { $c = U 'MWComplex'; $c.Real = [single]1.5; $c.Imag = [single]2.5; Echo1 $o $c }
Case 'x.complex.bool' { $c = U 'MWComplex'; $c.Real = $true; $c.Imag = $false; Echo1 $o $c }
Case 'x.complex.text' { $c = U 'MWComplex'; $c.Real = 'x'; $c.Imag = 2.0; Echo1 $o $c }
Case 'x.complex.var_arrays' { $c = U 'MWComplex'; $c.Real = [object[]]@(1.0, 2.0); $c.Imag = [object[]]@(3.0, 4.0); Echo1 $o $c }
Case 'x.complex.range' { $c = U 'MWComplex'; $c.Real = $ws.Range('D1:D3'); $c.Imag = $ws.Range('D1:D3'); Echo1 $o $c }
Case 'x.complex.flags_coerce' { $c = U 'MWComplex'; $c.MWFlags.DataConversionFlags.CoerceNumericToType = 7; $c.Real = 1.5; $c.Imag = 2.5; Echo1 $o $c }
Case 'x.complex.flags_defaults' { $c = U 'MWComplex'; Flags $c }
Case 'x.complex.readback' { $c = U 'MWComplex'; $c.Real = [double[]]@(1, 2); '{0} | {1}' -f (Show $c.Real), (Show $c.Imag) }
Case 'x.complex.clone' { $c = U 'MWComplex'; $c.Real = 1.0; $c.Imag = 2.0; $d = CloneObj $c; $d.Imag = 9.0; '{0} | {1}' -f (Show $c.Imag), (Show $d.Imag) }
Case 'x.complex.pass' { $c = U 'MWComplex'; $c.Real = 1.0; $c.Imag = 2.0; Pass1 $o $c }
Case 'x.complex.pass_array' { $c = U 'MWComplex'; $c.Real = [double[]]@(1, 2); $c.Imag = [double[]]@(3, 4); Pass1 $o $c }
Case 'x.complex.in_cellformat' { $c = U 'MWComplex'; $c.Real = [double[]]@(1, 2); $c.Imag = [double[]]@(3, 4); Echo1 (FX { param($f) $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2 }) $c }
Case 'x.complex.in_component_coerce' { $c = U 'MWComplex'; $c.Real = 1.5; $c.Imag = 2.5; Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = 7 }) $c }
Case 'x.complex.members' { Members (U 'MWComplex') }
Case 'x.complex.out_types' { $c = $null; $o.k17_make(1, [ref]$c, 'complex'); '{0} | {1} | {2}' -f $c.GetType().Name, (Show $c.Real), (Show $c.Imag) }
Case 'x.complex.out_flags' { $c = $null; $o.k17_make(1, [ref]$c, 'complex'); Flags $c }
Case 'x.complex.out_cellformat' { $c = $null; (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }).k17_make(1, [ref]$c, 'complexmat'); '{0} | {1}' -f (Show $c.Real), (Show $c.Imag) }
Case 'x.complex.out_asdate' { $c = $null; (FX { param($f) $f.MWFlags.DataConversionFlags.OutputAsDate = $true }).k17_make(1, [ref]$c, 'complex'); '{0} | {1}' -f (Show $c.Real), (Show $c.Imag) }
Case 'x.complex.out_set_then_pass' { $c = $null; $o.k17_make(1, [ref]$c, 'complex'); $c.Imag = 9.0; Pass1 $o $c }
Case 'x.complex.out_members' { $c = $null; $o.k17_make(1, [ref]$c, 'complex'); Members $c }
Case 'x.complex.out_in_cell' { $s = $null; $o.k17_make(1, [ref]$s, 'cellnested'); Show $s }
Case 'x.complex.out_in_struct' { $s = $null; $o.k17_make(1, [ref]$s, 'structnested'); Show $s }

# --- MWSparse ---------------------------------------------------------------------------------------------
Case 'x.sparse.logical' { $s = U 'MWSparse'; $s.NumRows = 1; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = [bool[]]@($true); Echo1 $o $s }
Case 'x.sparse.int_indices' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 3; $s.RowIndex = [int[]]@(1, 2); $s.ColumnIndex = [int[]]@(1, 3); $s.Array = [double[]]@(5, 6); Echo1 $o $s }
Case 'x.sparse.variant_indices' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 3; $s.RowIndex = [object[]]@(1.0, 2.0); $s.ColumnIndex = [object[]]@(1.0, 3.0); $s.Array = [object[]]@(5.0, 6.0); Echo1 $o $s }
Case 'x.sparse.scalar_triplet' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = 2.0; $s.ColumnIndex = 1.0; $s.Array = 7.0; Echo1 $o $s }
Case 'x.sparse.unsorted' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(2, 1); $s.ColumnIndex = [double[]]@(1, 1); $s.Array = [double[]]@(5, 6); Echo1 $o $s }
Case 'x.sparse.col_major_order' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1, 1); $s.ColumnIndex = [double[]]@(2, 1); $s.Array = [double[]]@(5, 6); Echo1 $o $s }
Case 'x.sparse.int_values' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = [int[]]@(5); Echo1 $o $s }
Case 'x.sparse.flags_defaults' { $s = U 'MWSparse'; Flags $s }
Case 'x.sparse.readback' { $s = U 'MWSparse'; $s.NumRows = 2; $s.RowIndex = [double[]]@(1, 2); '{0} | {1} | {2} | {3}' -f $s.NumRows, $s.NumColumns, (Show $s.RowIndex), (Show $s.Array) }
Case 'x.sparse.clone' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = [double[]]@(5); $c = CloneObj $s; $c.NumRows = 9; '{0} | {1}' -f $s.NumRows, $c.NumRows }
Case 'x.sparse.pass' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 3; $s.RowIndex = [double[]]@(1, 2); $s.ColumnIndex = [double[]]@(1, 3); $s.Array = [double[]]@(5, 6); Pass1 $o $s }
Case 'x.sparse.in_component_coerce' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = [double[]]@(5.5); Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.CoerceNumericToType = 8 }) $s }
Case 'x.sparse.members' { Members (U 'MWSparse') }
Case 'x.sparse.out_types' { $s = $null; $o.k17_make(1, [ref]$s, 'sparse'); '{0} | {1} | {2}' -f $s.GetType().Name, $s.NumRows.GetType().Name, (Show $s.RowIndex) }
Case 'x.sparse.out_flags' { $s = $null; $o.k17_make(1, [ref]$s, 'sparse'); Flags $s }
Case 'x.sparse.out_cellformat' { $s = $null; (FX { param($f) $f.MWFlags.ArrayFormatFlags.OutputArrayFormat = 2 }).k17_make(1, [ref]$s, 'sparse'); '{0} | {1} | {2}' -f (Show $s.RowIndex), (Show $s.ColumnIndex), (Show $s.Array) }
Case 'x.sparse.out_set_then_pass' { $s = $null; $o.k17_make(1, [ref]$s, 'sparse'); $s.NumRows = 5; Pass1 $o $s }
Case 'x.sparse.out_members' { $s = $null; $o.k17_make(1, [ref]$s, 'sparse'); Members $s }

# --- MWField standalone ----------------------------------------------------------------------------------------
Case 'x.field.name_default' { $fd = U 'MWField'; Show $fd.Name }
Case 'x.field.value_roundtrip' { $fd = U 'MWField'; $fd.Value = 3.5; Show $fd.Value }
Case 'x.field.value_array_roundtrip' { $fd = U 'MWField'; $fd.Value = [double[]]@(1, 2); Show $fd.Value }
Case 'x.field.value_unset' { $fd = U 'MWField'; Show $fd.Value }
Case 'x.field.in' { $fd = U 'MWField'; $fd.Value = 3.5; Echo1 $o $fd }
Case 'x.field.flags' { $fd = U 'MWField'; Flags $fd }
Case 'x.field.clone' { $fd = U 'MWField'; $fd.Value = 3.5; $c = CloneObj $fd; Show $c.Value }

# --- the utility library's flags objects, standalone and put into a component --------------------------------------
Case 'x.utilflags.defaults' { FlagsOf (U 'MWFlags') }
Case 'x.utilflags.put_into_component' { $uf = U 'MWFlags'; $uf.DataConversionFlags.CoerceNumericToType = 8; $f = Fresh $ProgId; $f.MWFlags = $uf; Echo1 $f 3.7 }
Case 'x.utilflags.put_shares_object' { $uf = U 'MWFlags'; $f = Fresh $ProgId; $f.MWFlags = $uf; $f.MWFlags.DataConversionFlags.DateBias = 7; $uf.DataConversionFlags.DateBias }
Case 'x.utilflags.put_then_get_same' { $uf = U 'MWFlags'; $f = Fresh $ProgId; $f.MWFlags = $uf; $g1 = $f.MWFlags; [System.Runtime.InteropServices.Marshal]::ReferenceEquals($g1, $uf) }
Case 'x.utilflags.arrayformat_put' { $af = U 'MWArrayFormatFlags'; $af.InputArrayFormat = 2; $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags = $af; Echo1 $f 5.0 }
Case 'x.utilflags.arrayformat_put_shares' { $af = U 'MWArrayFormatFlags'; $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags = $af; $f.MWFlags.ArrayFormatFlags.InputArrayFormat = 2; $af.InputArrayFormat }
Case 'x.utilflags.dataconv_put' { $dc = U 'MWDataConversionFlags'; $dc.CoerceNumericToType = 8; $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags = $dc; Echo1 $f 3.7 }
Case 'x.utilflags.dataconv_defaults' { $dc = U 'MWDataConversionFlags'; '{0} {1} {2} {3} {4} {5}' -f $dc.CoerceNumericToType, $dc.InputDateFormat, $dc.OutputAsDate, $dc.DateBias, $dc.ReplaceMissing, $dc.ReplaceMatlabNaN }
Case 'x.utilflags.arrayformat_defaults' { $af = U 'MWArrayFormatFlags'; '{0} {1} {2} {3} {4} {5}' -f $af.InputArrayFormat, $af.InputArrayIndFlag, $af.OutputArrayFormat, $af.OutputArrayIndFlag, $af.AutoResizeOutput, $af.TransposeOutput }
Case 'x.utilflags.clone' { $uf = U 'MWFlags'; $uf.DataConversionFlags.DateBias = 7; $c = CloneFlags $uf; $c.DataConversionFlags.DateBias = 8; '{0} | {1}' -f $uf.DataConversionFlags.DateBias, $c.DataConversionFlags.DateBias }
Case 'x.utilflags.put_wrong_type' { $af = U 'MWArrayFormatFlags'; $f = Fresh $ProgId; $f.MWFlags = $af; 'ok' }
Case 'x.utilflags.put_null' { $f = Fresh $ProgId; $f.MWFlags = $null; Flags $f }
Case 'x.utilflags.put_dictionary' { $f = Fresh $ProgId; $f.MWFlags = $dict; 'ok' }
Case 'x.utilflags.put_number' { $f = Fresh $ProgId; $f.MWFlags = 5; 'ok' }
Case 'x.utilflags.arrayformat_put_null' { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags = $null; Flags $f }
Case 'x.utilflags.set_bool_as_number' { $f = Fresh $ProgId; $f.MWFlags.ArrayFormatFlags.TransposeOutput = 1; $f.MWFlags.ArrayFormatFlags.TransposeOutput }
Case 'x.utilflags.set_number_as_text' { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = '12'; $f.MWFlags.DataConversionFlags.DateBias }
Case 'x.utilflags.set_number_as_badtext' { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = 'x'; $f.MWFlags.DataConversionFlags.DateBias }
Case 'x.utilflags.set_fraction' { $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.DateBias = 2.7; $f.MWFlags.DataConversionFlags.DateBias }
Case 'x.utilflags.members.flags' { Members (U 'MWFlags') }
Case 'x.utilflags.members.af' { Members (U 'MWArrayFormatFlags') }
Case 'x.utilflags.members.dc' { Members (U 'MWDataConversionFlags') }
Case 'x.utilflags.members.util' { Members $u }
Case 'x.utilflags.flags_from_component_into_arg' { $g = U 'MWArg'; $g.Value = 3.7; $f = Fresh $ProgId; $f.MWFlags.DataConversionFlags.CoerceNumericToType = 8; $g.MWFlags = $f.MWFlags; Echo1 $o $g }

# --- MWUtil ---------------------------------------------------------------------------------------------------
Case 'x.util.pack_2d' { Show (Packed @((Matrix2 @(1, 2, 3, 4) 2 2))) }
Case 'x.util.pack_nested' { $p1 = Packed @(1.0, 'a'); Show (Packed @($p1, 2.0)) }
Case 'x.util.pack_missing_middle' { $p = $null; $u.MWPack([ref]$p, 1.0, $missing, 3.0); Show $p }
Case 'x.util.pack_dbnull' { Show (Packed @(1.0, [System.DBNull]::Value, 3.0)) }
Case 'x.util.pack_empty_first' { Show (Packed @($null, 2.0)) }
Case 'x.util.pack_empty_last' { Show (Packed @(1.0, $null)) }
Case 'x.util.pack_bool_date' { Show (Packed @($true, $d1)) }
Case 'x.util.pack_struct' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); Show (Packed @($s)) }
Case 'x.util.pack_prefilled' { $p = 'old'; $u.MWPack([ref]$p, 1.0); Show $p }
Case 'x.util.pack_32' { $items = @(); for ($k = 1; $k -le 32; $k++) { $items += [double]$k }; $p = Packed $items; Show $p }
Case 'x.util.pack_onebased_2d' { Show (Packed @((OneBased @(1.0, 2.0, 3.0, 4.0) 2 2))) }
Case 'x.util.unpack_start_beyond' { $p = Packed @(1.0, 2.0, 3.0); $a = 'old'; $u.MWUnpack($p, 5, $false, [ref]$a); Show $a }
Case 'x.util.unpack_start_last' { $p = Packed @(1.0, 2.0, 3.0); $a = 'old'; $b = 'old2'; $u.MWUnpack($p, 2, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_negative_start' { $p = Packed @(1.0, 2.0, 3.0); $a = 'old'; $u.MWUnpack($p, -1, $false, [ref]$a); Show $a }
Case 'x.util.unpack_2d' { $a = $null; $b = $null; $u.MWUnpack((Matrix2 @(1, 2, 3, 4) 2 2), 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_var2d' { $a = $null; $b = $null; $u.MWUnpack((VarMatrix2 @(1.0, 2.0, 3.0, 4.0) 2 2), 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_1based' { $a = $null; $b = $null; $u.MWUnpack((OneBased @(1.0, 2.0, 3.0, 4.0) 2 2), 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_null' { $a = 'old'; $u.MWUnpack($null, 0, $false, [ref]$a); Show $a }
Case 'x.util.unpack_string' { $a = $null; $b = 'old'; $u.MWUnpack('abc', 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_r8array' { $a = $null; $b = $null; $u.MWUnpack(([double[]]@(7, 8)), 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_prefilled_untouched' { $p = Packed @(1.0); $a = 'old'; $b = 'old2'; $u.MWUnpack($p, 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_nested' { $p1 = Packed @(1.0, 'a'); $p = Packed @($p1, 2.0); $a = $null; $b = $null; $u.MWUnpack($p, 0, $false, [ref]$a, [ref]$b); '{0} | {1}' -f (Show $a), (Show $b) }
Case 'x.util.unpack_struct' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); $p = Packed @($s); $a = $null; $u.MWUnpack($p, 0, $false, [ref]$a); Show $a }
Case 'x.util.unpack_range_scalar' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @(5.0); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); '{0} | K1={1}' -f (Show $r), (Show $ws.Range('K1').Value2) }
Case 'x.util.unpack_range_two_slots' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @((Matrix2 @(1, 2, 3, 4) 2 2), 'x'); $r1 = $ws.Range('K1'); $r2 = $ws.Range('K5'); $u.MWUnpack($p, 0, $true, [ref]$r1, [ref]$r2); $cells = @(); foreach ($cell in $ws.Range('K1:L5').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }; '{0} | {1} | {2}' -f (Show $r1), (Show $r2), ($cells -join ', ') }
Case 'x.util.unpack_range_noresize_row' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @(([double[]]@(1, 2, 3))); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $false, [ref]$r); $cells = @(); foreach ($cell in $ws.Range('K1:M1').Cells) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) }; '{0} | {1}' -f (Show $r), ($cells -join ', ') }
Case 'x.util.unpack_range_row_resize' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @(([double[]]@(1, 2, 3))); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); $cells = @(); foreach ($cell in $ws.Range('K1:M3').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }; '{0} | {1}' -f (Show $r), ($cells -join ', ') }
Case 'x.util.unpack_range_larger_target' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @((Matrix2 @(1, 2, 3, 4) 2 2)); $r = $ws.Range('K1:M3'); $u.MWUnpack($p, 0, $true, [ref]$r); $cells = @(); foreach ($cell in $ws.Range('K1:M3').Cells) { if ($null -ne $cell.Value2) { $cells += ('{0}={1}' -f $cell.Address($false, $false), (Show $cell.Value2)) } }; '{0} | {1}' -f (Show $r), ($cells -join ', ') }
Case 'x.util.unpack_range_null_element' { $ws.Range('K1:P20').Clear() | Out-Null; $ws.Range('K1').Value2 = 99; $p = [object[]]@($null); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); '{0} | K1={1}' -f (Show $r), (Show $ws.Range('K1').Value2) }
Case 'x.util.unpack_range_nan' { $ws.Range('K1:P20').Clear() | Out-Null; $ws.Range('K1').Value2 = 99; $p = Packed @([double]::NaN); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); '{0} | K1={1}' -f (Show $r), (Show $ws.Range('K1').Value2) }
Case 'x.util.unpack_range_date' { $ws.Range('K1:P20').Clear() | Out-Null; $p = Packed @($d1); $r = $ws.Range('K1'); $u.MWUnpack($p, 0, $true, [ref]$r); '{0} | K1={1}' -f (Show $r), (Show $ws.Range('K1').Value2) }
Case 'x.util.date2variant.int' { $d = [int]739326; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.single' { $d = [single]739326.5; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.negative' { $d = -5.0; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.huge' { $d = 1e9; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.zero' { $d = 0.0; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.2d' { $d = (Matrix2 @(739326.5, 739327.5) 1 2); $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.var' { $d = [object[]]@(739326.5, 'x'); $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.null' { $d = $null; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.bool' { $d = $true; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.decimal' { $d = [decimal]739326.5; $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.date2variant.twice' { $d = 739326.4375; $u.MWDate2VariantDate([ref]$d); $u.MWDate2VariantDate([ref]$d); Show $d }
Case 'x.util.init_null' { $u.MWInitApplication($null); 'ok' }
Case 'x.util.init_worksheet' { $u.MWInitApplication($ws); 'ok' }
Case 'x.util.init_workbook' { $u.MWInitApplication($wb); 'ok' }
Case 'x.util.init_string' { $u.MWInitApplication('x'); 'ok' }
Case 'x.util.init_number' { $u.MWInitApplication(5.0); 'ok' }
Case 'x.util.init_excel_again' { $u.MWInitApplication($xl); 'ok' }
Case 'x.util.call_after_inits' { Call $o 'k17_one' 1 1 (A 2.0) }
# MWInitApplicationWithMCROptions is not asked again here: a second initialization with options, a list that is no
# array and a missing list each ended R2025b's host within a call or two (the fourth and fifth runs); the main
# oracle's util.MWInitApplicationWithMCROptions row records its refusal of a list that is no string array.
Case 'x.util.isjvm_type' { $r = $u.IsMCRJVMEnabled(); '{0} {1}' -f $r.GetType().Name, $r }
Case 'x.util.isinit_type' { $r = $u.IsMCRInitialized(); '{0} {1}' -f $r.GetType().Name, $r }
Case 'x.util.create_remote_util' { $r = $u.CreateRemoteObject("$UtilPrefix.MWStruct"); Show $r }
Case 'x.util.create_remote_dictionary' { $r = $u.CreateRemoteObject('Scripting.Dictionary'); Show $r }
Case 'x.util.create_remote_empty' { $r = $u.CreateRemoteObject(''); Show $r }
Case 'x.util.create_remote_versionless' { $r = $u.CreateRemoteObject('k17xl.K17Xl'); Show $r }
Case 'x.util.second_util' { $u2 = Fresh $UtilProgId; '{0} {1}' -f $u2.IsMCRInitialized(), [System.Runtime.InteropServices.Marshal]::ReferenceEquals($u, $u2) }

# --- last: the rows that might end the host (R2025b's conversion took a bad sparse index as a crash risk) -------
# empty array meets a flag before the end.
# The rows that may end R2025b's host, least likely first: an empty Variant array under InputArrayFormat 0, a zero
# index into an MWSparse and an MWSparse with no NumRows did (an access violation in its conversion), and so did an
# MWArg inside an array of Variants; the recording stops where the host does.
Say 'x.next' 'x.complex.mismatch and after: the rows that may end the host; the recording stops where it does'
Case 'x.complex.mismatch' { $c = U 'MWComplex'; $c.Real = [double[]]@(1, 2); $c.Imag = 3.0; Echo1 $o $c }
Case 'x.complex.mismatch_lengths' { $c = U 'MWComplex'; $c.Real = [double[]]@(1, 2); $c.Imag = [double[]]@(3, 4, 5); Echo1 $o $c }
Case 'x.complex.mismatch_shape' { $c = U 'MWComplex'; $c.Real = (Matrix2 @(1, 2, 3, 4) 2 2); $c.Imag = [double[]]@(5, 6, 7, 8); Echo1 $o $c }
Case 'x.struct.dims_mismatch_sub' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 2), [string[]]@('a')); Show (Prop $s 'Item' @('a', 1.0, 1.0, 1.0)) }
Case 'x.sparse.rows_only' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; Echo1 $o $s }
Case 'x.sparse.rows_only_middle' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; Call $o 'k17_opt' 1 1 (A 1.0 $s $true) }
Case 'x.sparse.dup' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1, 1); $s.ColumnIndex = [double[]]@(1, 1); $s.Array = [double[]]@(5, 6); Echo1 $o $s }
Case 'x.sparse.length_mismatch' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1, 2); $s.ColumnIndex = [double[]]@(1); $s.Array = [double[]]@(5, 6); Echo1 $o $s }
Case 'x.sparse.text_values' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = [string[]]@('x'); Echo1 $o $s }
Case 'x.sparse.fraction_index' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1.5); $s.ColumnIndex = [double[]]@(1); $s.Array = [double[]]@(5); Echo1 $o $s }
Case 'x.sparse.2d_array' { $s = U 'MWSparse'; $s.NumRows = 2; $s.NumColumns = 2; $s.RowIndex = [double[]]@(1); $s.ColumnIndex = [double[]]@(1); $s.Array = (Matrix2 @(1, 2, 3, 4) 2 2); Echo1 $o $s }
Case 'x.ReplaceMissing.1.var_empty' { Echo1 (FX { param($f) $f.MWFlags.DataConversionFlags.ReplaceMissing = 1 }) ([object[]]@()) }
Case 'x.arg.in_packed' { $g = U 'MWArg'; $g.Value = 3.7; $g.MWFlags.DataConversionFlags.CoerceNumericToType = 8; Call $o 'k17_varin' 1 1 (A (Packed @($g, 2.0))) }
Case 'x.struct.in_packed' { $s = U 'MWStruct'; $s.Initialize([double[]]@(1, 1), [string[]]@('a')); (Prop $s 'Item' @('a')).Value = 1.0; Call $o 'k17_varin' 1 1 (A (Packed @($s, 2.0))) }
Case 'x.range.in_packed' { Call $o 'k17_varin' 1 1 (A (Packed @(1.0, $ws.Range('D1:D3')))) }
Case 'x.after' { Call $o 'k17_one' 1 1 (A 2.0) }
