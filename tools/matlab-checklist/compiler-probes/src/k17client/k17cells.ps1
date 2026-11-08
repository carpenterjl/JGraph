<#
.SYNOPSIS
    Excel's own cells over an Excel add-in (compiler and SDK plan, stages K17a and K17e): an
    invisible Excel, the add-in workbook opened (-Xlam) or the add-in's .bas imported into a scratch
    workbook through the VBA project object model where Excel trusts it (-Bas), formulas written
    into a second workbook and calculated, each cell's Value2 and Text recorded, the workbooks closed
    unsaved and Excel quit (decision KO13). Where Excel refuses the import, the refusal is the
    recording (K17a: it did, so the cells are recorded through the workbook).

.PARAMETER Bas
    The .bas R2025b wrote with -b (k17bas.bas, whose InitModule creates MWComUtil.MWUtil25.2 and
    the class k17bas.K17Bas.1_0), imported through Excel's VBA project object model.

.PARAMETER Xlam
    An add-in workbook holding that module (stage K17e: the .xlam JGraph writes, or the one
    k17xlam.py writes around R2025b's .bas for the recording), opened as an add-in.
#>
[CmdletBinding()]
param(
    [string] $Bas = '',
    [string] $Xlam = '',
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
function Show($v) {
    if ($null -eq $v) { return 'null' }
    if ($v -is [array]) {
        $parts = @(); foreach ($e in $v) { $parts += (Show $e) }
        return ('array[{0}] rank {1} {{{2}}}' -f $v.GetType().GetElementType().Name, $v.Rank, ($parts -join ', '))
    }
    if ($v -is [string]) { return "string '" + $v + "'" }
    if ($v -is [datetime]) { return 'DateTime ' + $v.ToString('yyyy-MM-dd HH:mm:ss.fff', $inv) }
    if ($v -is [bool]) { return "bool $v" }
    if ($v -is [double]) { return 'double ' + $v.ToString('R', $inv) }
    if ($v -is [int]) { return 'int ' + $v }
    return ('{0} {1}' -f $v.GetType().Name, [string]$v)
}

$xl = $null; $wb = $null; $addin = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $xl = New-Object -ComObject 'Excel.Application'
    $xl.Visible = $false
    $xl.DisplayAlerts = $false
    Say 'excel.version' ('{0} build {1} {2}' -f $xl.Version, $xl.Build, $xl.OperatingSystem)
    Say 'excel.visible' $xl.Visible
    Say 'excel.automationsecurity' $xl.AutomationSecurity
    $imported = $false
    if ($Xlam) {
        # The add-in workbook opened as an add-in; its module's functions then serve any workbook's cells.
        Say 'xlam.exists' (Test-Path -LiteralPath $Xlam)
        Case 'xlam.open' { $script:addin = $xl.Workbooks.Open($Xlam); 'opened {0}: IsAddin {1}, HasVBProject {2}, sheets {3}' -f $script:addin.Name, $script:addin.IsAddin, $script:addin.HasVBProject, $script:addin.Worksheets.Count }
        $imported = $null -ne $addin
        $wb = $xl.Workbooks.Add()
        $ws = $wb.Worksheets.Item(1)
        Say 'xlam.opened' $imported
    } else {
        $wb = $xl.Workbooks.Add()
        $ws = $wb.Worksheets.Item(1)
        Say 'bas.exists' (Test-Path -LiteralPath $Bas)
        Case 'security.AccessVBOM' { $v = (Get-ItemProperty 'HKCU:\Software\Microsoft\Office\16.0\Excel\Security' -ErrorAction SilentlyContinue).AccessVBOM; if ($null -eq $v) { 'absent (programmatic access to the VBA project is not trusted)' } else { $v } }
        Case 'vbe.access' { $vbe = $xl.VBE; 'VBE ok, projects ' + $vbe.VBProjects.Count }
        Case 'vbproject.access' { $proj = $wb.VBProject; if ($null -eq $proj) { 'VBProject is Nothing: Excel does not trust programmatic access to the VBA project object model' } else { 'project ' + $proj.Name + ', components ' + $proj.VBComponents.Count } }
        Case 'bas.import' {
            $proj = $wb.VBProject
            if ($null -eq $proj) { 'refused: VBProject is Nothing (AccessVBOM unset), so the .bas cannot be imported; the cells wait for the add-in workbook JGraph writes (K17e)' }
            else {
                $comp = $proj.VBComponents.Import($Bas)
                $script:imported = $true
                'imported ' + $comp.Name + ' type ' + $comp.Type + ', lines ' + $comp.CodeModule.CountOfLines
            }
        }
        Say 'bas.imported' $imported
    }
    if ($imported) {
        # Inputs for the formulas.
        $ws.Range('A1').Value2 = 4
        $ws.Range('B1').Value2 = 1.5; $ws.Range('C1').Value2 = 'two'; $ws.Range('B2').Value2 = $true; $ws.Range('C2').Value2 = 4
        $ws.Range('D1').Value2 = [datetime]'2024-03-15 10:30:00'
        $ws.Range('F1').Formula = '=1/0'
        $formulas = [ordered]@{
            'one.literal' = '=k17_one(3)'
            'one.cell' = '=k17_one(A1)'
            'one.text' = '=k17_one("abc")'
            'one.blank' = '=k17_one(E1)'
            'one.error' = '=k17_one(F1)'
            'one.none' = '=k17_one()'
            'many.three' = '=k17_many(1,2,3)'
            'many.two' = '=k17_many(1,2)'
            'two' = '=k17_two(5)'
            'echo.number' = '=k17_echo(3.5)'
            'echo.text' = '=k17_echo("text")'
            'echo.bool' = '=k17_echo(TRUE)'
            'echo.range' = '=k17_echo(B1:C2)'
            'echo.rangecol' = '=k17_echo(A1:A3)'
            'echo.date' = '=k17_echo(D1)'
            'echo.blank' = '=k17_echo(E1)'
            'echo.blankrange' = '=k17_echo(E1:E3)'
            'echo.error' = '=k17_echo(F1)'
            'echo.arrayconst' = '=k17_echo({1,2;3,4})'
            'echo.textarray' = '=k17_echo({"a","b"})'
            'echo.none' = '=k17_echo()'
            'opt.1' = '=k17_opt(1)'
            'opt.3' = '=k17_opt(1,"b",TRUE)'
            'varin.0' = '=k17_varin()'
            'varin.2' = '=k17_varin(1,"a")'
            'varin.range' = '=k17_varin(B1:C2)'
            'varout' = '=k17_varout(2)'
            'both' = '=k17_both(7,"a")'
            'args' = '=k17_args(A1)'
            'args.bad' = '=k17_args(-1)'
            'h1' = '=k17_h1()'
            'count.1' = '=k17_count()'
            'count.2' = '=k17_count()+0'
            'pass.number' = '=k17_pass(2.5)'
            'pass.date' = '=k17_pass(D1)'
            'make.double' = '=k17_make("double")'
            'make.doublemat' = '=k17_make("doublemat")'
            'make.doublerow' = '=k17_make("doublerow")'
            'make.char' = '=k17_make("char")'
            'make.charmat' = '=k17_make("charmat")'
            'make.logical' = '=k17_make("logical")'
            'make.cell' = '=k17_make("cell")'
            'make.struct' = '=k17_make("struct")'
            'make.nan' = '=k17_make("nan")'
            'make.inf' = '=k17_make("inf")'
            'make.empty' = '=k17_make("empty")'
            'make.datetime' = '=k17_make("datetime")'
            'make.datenum' = '=k17_make("datenum")'
            'make.string' = '=k17_make("string")'
            'make.int8' = '=k17_make("int8")'
            'make.complex' = '=k17_make("complex")'
            'make.sparse' = '=k17_make("sparse")'
            'make.charnonascii' = '=k17_make("charnonascii")'
            'err.id' = '=k17_err("id")'
            'err.plain' = '=k17_err("plain")'
            'err.multi' = '=k17_err("multi")'
            'err.nonascii' = '=k17_err("nonascii")'
            'err.none' = '=k17_err("none")'
            'noout' = '=k17_noout(1)'
            'none' = '=k17_none()'
            'quiet' = '=k17_quiet()'
            'plot' = '=k17_plot()'
        }
        $row = 1
        $cells = @{}
        foreach ($k in $formulas.Keys) {
            $addr = 'J' + $row
            try { $ws.Range($addr).Formula = $formulas[$k]; $cells[$k] = $addr } catch { Say "formula.$k" (Err $_.Exception) }
            $row++
        }
        Case 'calculate' { $xl.Calculate(); 'ok' }
        foreach ($k in $formulas.Keys) {
            if (-not $cells.ContainsKey($k)) { continue }
            $c = $ws.Range($cells[$k])
            Case "cell.$k" { '{0} | text {1} | formula {2}' -f (Show $c.Value2), $c.Text, $c.Formula }
        }
        # Array formulas over a block.
        Case 'array.doublemat' { $ws.Range('L1:M2').FormulaArray = '=k17_make("doublemat")'; $xl.Calculate(); Show $ws.Range('L1:M2').Value2 }
        Case 'array.doublemat.larger' { $ws.Range('L5:N7').FormulaArray = '=k17_make("doublemat")'; $xl.Calculate(); Show $ws.Range('L5:N7').Value2 }
        Case 'array.cell' { $ws.Range('L10:N10').FormulaArray = '=k17_make("cell")'; $xl.Calculate(); Show $ws.Range('L10:N10').Value2 }
        Case 'array.charmat' { $ws.Range('L12:L13').FormulaArray = '=k17_make("charmat")'; $xl.Calculate(); Show $ws.Range('L12:L13').Value2 }
        Case 'array.two' { $ws.Range('L15:M15').FormulaArray = '=k17_two(5)'; $xl.Calculate(); Show $ws.Range('L15:M15').Value2 }
        Case 'array.varout' { $ws.Range('L17:N17').FormulaArray = '=k17_varout(2)'; $xl.Calculate(); Show $ws.Range('L17:N17').Value2 }
        Case 'array.nanmat' { $ws.Range('L19:M20').FormulaArray = '=k17_make("nanmat")'; $xl.Calculate(); Show $ws.Range('L19:M20').Value2 }
        # A second call to a function whose add-in object already exists, and the module's own text as Excel
        # holds it (refused like the import where the VBA project is not trusted).
        Case 'again.one' { $ws.Range('P1').Formula = '=k17_one(21)'; $xl.Calculate(); Show $ws.Range('P1').Value2 }
        Case 'again.count' { $ws.Range('P2').Formula = '=k17_count()'; $xl.Calculate(); Show $ws.Range('P2').Value2 }
        Case 'workbooks' { '{0} open, add-ins among them: {1}' -f $xl.Workbooks.Count, (@($xl.Workbooks | Where-Object { $_.IsAddin }).Count) }
        $holder = if ($addin) { $addin } else { $wb }
        Case 'module.lines' { $comp = $holder.VBProject.VBComponents.Item($holder.VBProject.VBComponents.Count); $comp.CodeModule.CountOfLines }
        Case 'module.procedures' {
            $comp = $holder.VBProject.VBComponents.Item($holder.VBProject.VBComponents.Count)
            $cm = $comp.CodeModule
            $names = @(); $line = $cm.CountOfDeclarationLines + 1
            while ($line -le $cm.CountOfLines) { $kind = 0; $name = $cm.ProcOfLine($line, [ref]$kind); if ($names -notcontains $name) { $names += $name }; $line = $cm.ProcStartLine($name, $kind) + $cm.ProcCountLines($name, $kind) }
            $names -join ','
        }
    }
}
catch {
    Say 'FATAL' (Err $_.Exception)
    Say 'FATAL.where' $_.ScriptStackTrace
}
finally {
    try { if ($wb) { $wb.Close($false) } } catch { }
    try { if ($addin) { $addin.Close($false) } } catch { }
    try { if ($xl) { $xl.Quit() } } catch { }
    try { if ($xl) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) } } catch { }
    Say 'seconds' ('{0:N1}' -f $watch.Elapsed.TotalSeconds)
    [System.IO.File]::WriteAllLines($Out, $lines, $utf8)
}
