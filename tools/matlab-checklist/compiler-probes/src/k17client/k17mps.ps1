<#
.SYNOPSIS
    R2025b's Excel client for the Production Server, driven outside Excel (compiler and SDK plan,
    stage K17a): the .NET Framework automation add-in compiler.build.excelClientForProductionServer
    built is reflected (its types, members, attributes, references and resources), its string heap
    read, its server configuration written where ServerConfig.dll keeps it (HKCU\Software\MathWorks\
    MATLAB Builder EX\2.5, removed by record-k17.ps1's cleanup), and each worksheet function called
    with sample arguments against a URL, which record-k17.ps1 points at a logging proxy in front of
    jgraph-mps, so the requests appear on the wire log. Client-side logging is switched on in the
    same key and the log file's text recorded.

    Under -Native (stage K17f) the same rows are written for JGraph's client: a native COM automation
    add-in (jgxlmps.dll stamped by mcc -W mpsxl), created by its ProgID after the build registered it
    for the current user, its configuration written where it reads it (HKCU\Software\JGraph\Excel
    Client\<add-in>, removed by run-k17.ps1's cleanup), every call made through PowerShell's direct
    COM syntax with the same samples; no .NET assembly to reflect, so the asm.*, type.*, deps.* and
    string-heap rows are not written.

.PARAMETER Dll
    The client DLL (k17mps.dll).

.PARAMETER Url
    The server URL the configuration names.

.PARAMETER Archive
    The archive's name on the server (k17arch).

.PARAMETER Native
    JGraph's native client in place of R2025b's .NET one.

.PARAMETER ProgId
    Under -Native: the client's ProgID (<add-in>.<Class>), k17mps.K17Mps by default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Dll,
    [Parameter(Mandatory = $true)] [string] $Url,
    [string] $Archive = 'k17arch',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [switch] $Native,
    [string] $ProgId = 'k17mps.K17Mps',
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Continue'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lines = New-Object System.Collections.Generic.List[string]
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$here = $PSScriptRoot

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
function Attrs($member) {
    $parts = @()
    foreach ($a in $member.GetCustomAttributes($false)) {
        $t = $a.GetType().Name
        $vals = @()
        foreach ($p in $a.GetType().GetProperties()) {
            if ($p.Name -eq 'TypeId') { continue }
            try { $vals += ('{0}={1}' -f $p.Name, $p.GetValue($a, $null)) } catch { }
        }
        $parts += ('{0}({1})' -f $t, ($vals -join ' '))
    }
    $parts -join ' '
}

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$configRoot = if ($Native) { 'HKCU:\Software\JGraph\Excel Client' } else { 'HKCU:\Software\MathWorks\MATLAB Builder EX\2.5' }

# The generated class shows a MessageBox for every failure (a MATLAB error, a web error, a missing
# configuration), which would block this process until someone clicks it. A runspace of this same
# process watches for dialogs owned by this process through UI Automation, records each one's text
# and presses its OK button through the Invoke pattern; nothing of the user's is touched.
$boxLog = Join-Path (Split-Path $Out) 'k17mps_boxes.txt'
if (Test-Path $boxLog) { Remove-Item $boxLog }
$stopFile = $boxLog + '.stop'
if (Test-Path $stopFile) { Remove-Item $stopFile }
$closer = [powershell]::Create()
[void]$closer.AddScript({
    param($ownerPid, $logPath, $stopPath)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ownerPid)
    $textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $buttonCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $n = 0
    while (-not (Test-Path $stopPath)) {
        try {
            $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
            foreach ($w in $wins) {
                if ($w.Current.ClassName -ne '#32770') { continue }
                $texts = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond) | ForEach-Object { $_.Current.Name })
                $buttons = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCond))
                $n++
                Add-Content -Path $logPath -Value ("{0}`t{1} | {2} | buttons [{3}]" -f $n, $w.Current.Name, ($texts -join ' / '), (($buttons | ForEach-Object { $_.Current.Name }) -join ','))
                $ok = $buttons | Where-Object { $_.Current.Name -eq 'OK' } | Select-Object -First 1
                if (-not $ok) { $ok = $buttons | Select-Object -First 1 }
                if ($ok) { $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
            }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
})
[void]$closer.AddArgument($PID).AddArgument($boxLog).AddArgument($stopFile)
$closerHandle = $closer.BeginInvoke()
try {
    Say 'dll' (Split-Path $Dll -Leaf)
    Say 'dll.exists' (Test-Path -LiteralPath $Dll)
    Say 'dll.folder' ((Get-ChildItem (Split-Path $Dll) | ForEach-Object { '{0} ({1})' -f $_.Name, $_.Length }) -join ' | ')

    $asm = $null
    $types = @()
    if (-not $Native) {
        # --- the string heap -------------------------------------------------------------------------------
        $strings = & $Python -I (Join-Path $here 'k17strings.py') 'client' $Dll 2>&1 | Out-String
        foreach ($l in ($strings -split "`r?`n")) { if ($l.Length -gt 0) { $lines.Add($l) } }

        # --- reflection -------------------------------------------------------------------------------------
        # The class resolves MathWorks' client assembly from its own resource at run time (AssemblyResolve in its
        # static constructor); reflection before that needs the same assembly loadable, so R2025b's copy is loaded
        # first, with Excel's interop assembly from the GAC.
        Case 'deps.mpsclient' { [System.Reflection.Assembly]::LoadFrom((Join-Path $MatlabRoot 'toolbox\compiler_sdk\mps_clients\dotnet\MathWorks.MATLAB.ProductionServer.Client.dll')).FullName }
        Case 'deps.excelinterop' { [System.Reflection.Assembly]::Load('Microsoft.Office.Interop.Excel, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c').FullName }
        Case 'deps.visualbasic' { [System.Reflection.Assembly]::Load('Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a').FullName }
        Case 'asm.load' { $script:asm = [System.Reflection.Assembly]::LoadFrom($Dll); $asm.FullName }
        if ($asm) {
            Case 'asm.runtime' { $asm.ImageRuntimeVersion }
            Case 'asm.attributes' { Attrs $asm }
            Case 'asm.references' { ($asm.GetReferencedAssemblies() | ForEach-Object { $_.FullName }) -join ' | ' }
            Case 'asm.resources' { ($asm.GetManifestResourceNames()) -join ' | ' }
            foreach ($rn in $asm.GetManifestResourceNames()) {
                Case "asm.resource.$rn" {
                    $s = $asm.GetManifestResourceStream($rn); $b = New-Object byte[] $s.Length; [void]$s.Read($b, 0, $b.Length)
                    $text = [System.Text.Encoding]::UTF8.GetString($b)
                    if ($text -match '^[\x09\x0A\x0D\x20-\x7E -￿]*$') { '{0} bytes: {1}' -f $b.Length, $text } else { '{0} bytes: {1}' -f $b.Length, ([System.BitConverter]::ToString($b, 0, [Math]::Min(64, $b.Length))) }
                }
            }
            try { $types = $asm.GetTypes() } catch {
                $e = $_.Exception; while ($e.InnerException -and -not ($e -is [System.Reflection.ReflectionTypeLoadException])) { $e = $e.InnerException }
                if ($e -is [System.Reflection.ReflectionTypeLoadException]) {
                    $types = @($e.Types | Where-Object { $_ })
                    Say 'asm.types.partial' (($e.LoaderExceptions | ForEach-Object { $_.Message }) -join ' | ')
                } else { Say 'asm.types.partial' (Err $_.Exception) }
            }
            foreach ($t in $types) {
                $label = 'type.' + $t.FullName
                Say $label ('{0}{1} : {2} [{3}] {4}' -f $(if ($t.IsPublic) { 'public ' } else { '' }), $(if ($t.IsInterface) { 'interface' } elseif ($t.IsEnum) { 'enum' } else { 'class' }), $(if ($t.BaseType) { $t.BaseType.FullName } else { '-' }), (($t.GetInterfaces() | ForEach-Object { $_.Name }) -join ','), (Attrs $t))
                foreach ($f in $t.GetFields([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly')) {
                    $lit = ''
                    if ($f.IsLiteral) { try { $lit = ' = ' + $f.GetRawConstantValue() } catch { } }
                    Say "$label.field.$($f.Name)" ('{0} {1}{2} {3}' -f $(if ($f.IsPublic) { 'public' } else { 'private' }), $f.FieldType.Name, $lit, (Attrs $f))
                }
                foreach ($c in $t.GetConstructors([System.Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly')) {
                    Say "$label.ctor" (($c.GetParameters() | ForEach-Object { '{0} {1}' -f $_.ParameterType.Name, $_.Name }) -join ', ')
                }
                foreach ($p in $t.GetProperties([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly')) {
                    Say "$label.property.$($p.Name)" ('{0} {1}' -f $p.PropertyType.Name, (Attrs $p))
                }
                foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly')) {
                    if ($m.IsSpecialName) { continue }
                    $ps = $m.GetParameters() | ForEach-Object { '{0}{1} {2}{3}' -f $(if ($_.IsOptional) { '[opt] ' } else { '' }), $_.ParameterType.Name, $_.Name, $(if ($_.HasDefaultValue -and $null -ne $_.DefaultValue) { ' = ' + $_.DefaultValue } else { '' }) }
                    Say "$label.method.$($m.Name)" ('{0}{1} {2}({3}) {4}' -f $(if ($m.IsPublic) { 'public ' } else { 'private ' }), $(if ($m.IsStatic) { 'static ' } else { '' }), $m.ReturnType.Name, ($ps -join ', '), (Attrs $m))
                }
            }
        }
    }

    # --- the server configuration, where the client keeps it -----------------------------------------------
    $addin = [System.IO.Path]::GetFileNameWithoutExtension($Dll)
    $logFolder = Join-Path (Split-Path $Out) 'clientlog'
    New-Item -ItemType Directory -Force $logFolder | Out-Null
    if ($Native) {
        # JGraph's client (jgxlmps.cpp): under Software\JGraph\Excel Client\<add-in>, ServerURL is a text value
        # (http://host:port/<archive>), timeoutSeconds and responseSizeLimit DWORDs beside it; Logging\Enabled,
        # LoggingLevel and LogFileFolder switch on its own log.
        Case 'config.write' {
            $key = Join-Path $configRoot $addin
            New-Item -Path $key -Force | Out-Null
            Set-ItemProperty -Path $key -Name 'ServerURL' -Value $Url
            Set-ItemProperty -Path $key -Name 'responseSizeLimit' -Value 64 -Type DWord
            Set-ItemProperty -Path $key -Name 'timeoutSeconds' -Value 120 -Type DWord
            $logKey = Join-Path $configRoot 'Logging'
            New-Item -Path $logKey -Force | Out-Null
            Set-ItemProperty -Path $logKey -Name 'Enabled' -Value 1 -Type DWord
            Set-ItemProperty -Path $logKey -Name 'LoggingLevel' -Value 2 -Type DWord
            Set-ItemProperty -Path $logKey -Name 'LogFileFolder' -Value $logFolder
            'written under ' + $configRoot + ': ' + $addin + ' [ServerURL=' + $Url + ', timeoutSeconds=120, responseSizeLimit=64], Logging [Enabled=1, LoggingLevel=2, LogFileFolder]'
        }
    } else {
        # As the generated class reads it (getServerConfig): under MPS Server Config\<add-in name>, the server
        # URL is a value NAME whose DWORD data is 1 (a ".ctf" suffix dropped, "http://" prefixed when absent);
        # timeoutSeconds and responseSizeLimit are DWORD values beside it. Logging\Enabled, LoggingLevel (an
        # integer) and LogFileFolder switch on the client's own log.
        Case 'config.write' {
            $key = Join-Path $configRoot ('MPS Server Config\' + $addin)
            New-Item -Path $key -Force | Out-Null
            Set-ItemProperty -Path $key -Name $Url -Value 1 -Type DWord
            Set-ItemProperty -Path $key -Name 'responseSizeLimit' -Value 64 -Type DWord
            Set-ItemProperty -Path $key -Name 'timeoutSeconds' -Value 120 -Type DWord
            $logKey = Join-Path $configRoot 'Logging'
            New-Item -Path $logKey -Force | Out-Null
            Set-ItemProperty -Path $logKey -Name 'Enabled' -Value 1 -Type DWord
            Set-ItemProperty -Path $logKey -Name 'LoggingLevel' -Value 2 -Type DWord
            Set-ItemProperty -Path $logKey -Name 'LogFileFolder' -Value $logFolder
            'written under ' + $configRoot + ': MPS Server Config\' + $addin + ' [' + $Url + '=1, timeoutSeconds=120, responseSizeLimit=64], Logging [Enabled=1, LoggingLevel=2, LogFileFolder]'
        }
    }

    # --- the calls --------------------------------------------------------------------------------------------
    # The samples, and the shapes that matter: a range-like 2-D array, a blank, an error value, a date.
    $samples = @{
        'k17_quiet' = @(); 'k17_none' = @(); 'k17_count' = @(); 'k17_h1' = @()
        'k17_one' = @(2.0); 'k17_two' = @(5.0); 'k17_many' = @(1.0, 2.0, 3.0); 'k17_opt' = @(1.0)
        'k17_varin' = @(1.0, 'a'); 'k17_varout' = @(2.0); 'k17_both' = @(7.0, 'a'); 'k17_args' = @(2.0)
        'k17_noout' = @('x'); 'k17_plot' = @(); 'k17_echo' = @('text'); 'k17_pass' = @(2.5); 'k17_make' = @('doublemat')
    }
    $order = @('k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', 'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make')
    $m2 = New-Object 'object[,]' 2, 2; $m2[0, 0] = 1.0; $m2[0, 1] = 'a'; $m2[1, 0] = $true; $m2[1, 1] = 4.0
    $r8 = New-Object 'double[,]' 2, 2; $r8[0, 0] = 1; $r8[0, 1] = 2; $r8[1, 0] = 3; $r8[1, 1] = 4
    $echoes = @(@('var2d', $m2), @('r8_2d', $r8), @('empty', $null), @('missing', [System.Reflection.Missing]::Value), @('err', (New-Object System.Runtime.InteropServices.ErrorWrapper 0x800A07FA)), @('date', [datetime]'2024-03-15 10:30:00'), @('bool', $true), @('i4', [int]7), @('arr_var_with_empty', [object[]]@(1.0, $null, 3.0)), @('arr_r8', [double[]]@(1, 2, 3)), @('str', 'text'))
    $kinds = @('double', 'doublemat', 'doublerow', 'char', 'charmat', 'cell', 'struct', 'nan', 'nanmat', 'logical', 'logicalmat', 'datetime', 'datenum', 'empty', 'int8', 'complex', 'string', 'stringarr')

    if ($Native) {
        # JGraph's client, created by its ProgID (the build registered it for the current user) and called
        # through PowerShell's direct COM syntax, which passes a 2-D array as a 2-D SAFEARRAY and a [ref] as a
        # byref VARIANT (K17a's k17com.ps1 found the direct syntax the one that works whole).
        $class = $ProgId.Split('.')[-1]
        $outputs = @{ 'k17_quiet' = 0; 'k17_none' = 0; 'k17_one' = 1; 'k17_many' = 3; 'k17_two' = 2; 'k17_opt' = 1; 'k17_varin' = 1; 'k17_varout' = 1; 'k17_both' = 2; 'k17_args' = 1
            'k17_noout' = 0; 'k17_err' = 1; 'k17_plot' = 1; 'k17_h1' = 1; 'k17_echo' = 1; 'k17_pass' = 1; 'k17_count' = 1; 'k17_make' = 1 }
        # -NoEnumerate, as k17com.ps1's Raw: written plainly, a two-dimensional array would leave the function as its elements.
        function Raw($v) {
            if ($v -is [psobject] -and $null -ne $v.psobject.BaseObject) { Write-Output -NoEnumerate $v.psobject.BaseObject } else { Write-Output -NoEnumerate $v }
        }
        function Invoke-Native([string] $name, [object[]] $inputs, [int] $nargout = -1) {
            $nout = $outputs[$name]
            if ($nargout -lt 0) { $nargout = $nout }
            $parts = @()
            if ($nout -gt 0) {
                $script:__nargout = $nargout
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
        Case "create.$class" { $script:__obj = New-Object -ComObject $ProgId; 'ok' }
        if ($script:__obj) {
            foreach ($name in $order) {
                if (-not $samples.ContainsKey($name)) { continue }
                $given = $samples[$name]
                Case "call.$class.$name" { Invoke-Native $name $given }
            }
            foreach ($pair in $echoes) { $label, $value = $pair; Case "echo.$class.$label" { Invoke-Native 'k17_echo' @(, $value) } }
            foreach ($kind in $kinds) { Case "make.$class.$kind" { Invoke-Native 'k17_make' @($kind) } }
            Case "two.$class.array" { Invoke-Native 'k17_two' @(, [double[]]@(1, 2)) }
            Case "two.$class.nargout1" { Invoke-Native 'k17_two' @(5.0) 1 }
            Case "two.$class.nargout0" { Invoke-Native 'k17_two' @(5.0) 0 }
            Case "varout.$class.nargout3" { Invoke-Native 'k17_varout' @(2.0) 3 }
            Case "opt.$class.none" { Invoke-Native 'k17_opt' @() }
            Case "opt.$class.three" { Invoke-Native 'k17_opt' @(1.0, 'b', $true) }
            Case "opt.$class.nullmiddle" { Invoke-Native 'k17_opt' @(1.0, $null, $true) }
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:__obj)
            $script:__obj = $null
        }
    } elseif ($asm) {
        $classes = @($types | Where-Object { $_.IsClass -and $_.IsPublic -and -not $_.IsAbstract -and $null -ne $_.GetConstructor([Type]::EmptyTypes) })
        foreach ($t in $classes) {
            $obj = $null
            Case "create.$($t.Name)" { $script:obj = [System.Activator]::CreateInstance($t); 'ok' }
            if (-not $obj) { continue }
            # Each worksheet function takes `params Object[] inputs`, so reflection gets the array as its one
            # argument. A failing call shows a MessageBox (the generated code's way), which nobody here would
            # close, so only calls that succeed on the server are made; the errors are the add-in's rows.
            # The generated signatures mirror the add-in's: void f(int nargout, ref object out1..., params object[]
            # inputs), or void f(params object[] inputs) for a function with no outputs. nargout defaults to the
            # number of output slots; the answer is the slots' contents.
            function Invoke-Udf($method, [object[]] $inputs, [int] $nargout = -1) {
                # Typed array constructors, not New-Object: an array that passed through New-Object or a
                # subexpression arrives wrapped or unrolled (the fourth and fifth runs).
                $ps = $method.GetParameters()
                $a = [object[]]::new($ps.Count)
                $refs = @()
                $nout = @($ps | Where-Object { $_.ParameterType.IsByRef }).Count
                if ($nargout -lt 0) { $nargout = $nout }
                $raw = [object[]]::new($inputs.Count)
                for ($j = 0; $j -lt $inputs.Count; $j++) {
                    $v = $inputs[$j]
                    if ($v -is [psobject] -and $null -ne $v.psobject.BaseObject) { $v = $v.psobject.BaseObject }
                    $raw[$j] = $v
                }
                for ($i = 0; $i -lt $ps.Count; $i++) {
                    $p = $ps[$i]
                    if ($p.ParameterType.IsByRef) { $a[$i] = $null; $refs += $i }
                    elseif ($p.Name -eq 'nargout' -and $p.ParameterType -eq [int]) { $a[$i] = [int]$nargout }
                    elseif ($p.ParameterType -eq [object[]]) { $a[$i] = $raw }
                    else { $a[$i] = $null }
                }
                $r = $method.Invoke($obj, $a)
                if ($refs.Count -eq 0) { return (Show $r) }
                (@($refs | ForEach-Object { Show $a[$_] })) -join ' | '
            }
            foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]'Public,Instance,DeclaredOnly')) {
                if ($m.IsSpecialName -or $m.DeclaringType -ne $t -or -not $samples.ContainsKey($m.Name)) { continue }
                $method = $m
                $given = $samples[$m.Name]
                Case "call.$($t.Name).$($m.Name)" { Invoke-Udf $method $given }
            }
            $echo = $t.GetMethod('k17_echo')
            if ($echo) {
                foreach ($pair in $echoes) {
                    $label, $value = $pair
                    Case "echo.$($t.Name).$label" { Invoke-Udf $echo @(, $value) }
                }
            }
            $make = $t.GetMethod('k17_make')
            if ($make) {
                foreach ($kind in $kinds) {
                    Case "make.$($t.Name).$kind" { Invoke-Udf $make @($kind) }
                }
            }
            $two = $t.GetMethod('k17_two')
            if ($two) {
                Case "two.$($t.Name).array" { Invoke-Udf $two @([double[]]@(1, 2)) }
                Case "two.$($t.Name).nargout1" { Invoke-Udf $two @(5.0) 1 }
                Case "two.$($t.Name).nargout0" { Invoke-Udf $two @(5.0) 0 }
            }
            $varout = $t.GetMethod('k17_varout')
            if ($varout) { Case "varout.$($t.Name).nargout3" { Invoke-Udf $varout @(2.0) 3 } }
            $opt = $t.GetMethod('k17_opt')
            if ($opt) {
                Case "opt.$($t.Name).none" { Invoke-Udf $opt @() }
                Case "opt.$($t.Name).three" { Invoke-Udf $opt @(1.0, 'b', $true) }
                Case "opt.$($t.Name).nullmiddle" { Invoke-Udf $opt @(1.0, $null, $true) }
            }
        }
    }

    # --- the client's own log -----------------------------------------------------------------------------------
    # The trace listener flushes when the class's finalizer closes it, so the objects are let go first.
    $obj = $null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    Start-Sleep -Milliseconds 500
    $logs = @(Get-ChildItem $logFolder -File -ErrorAction SilentlyContinue)
    Say 'clientlog.files' (($logs | ForEach-Object { '{0} ({1})' -f $_.Name, $_.Length }) -join ' | ')
    foreach ($f in $logs) {
        $lines.Add('clientlog.text.' + $f.Name)
        # The trace listener still holds the file open.
        $stream = [System.IO.File]::Open($f.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $reader = New-Object System.IO.StreamReader($stream)
        $text = $reader.ReadToEnd(); $reader.Close(); $stream.Close()
        foreach ($l in ($text -split "`r?`n")) { if ($l.Length -gt 0) { $lines.Add('  |' + $l) } }
    }
    Case 'config.keys' { (Get-ChildItem $configRoot -Recurse | ForEach-Object { $_.Name }) -join ' | ' }
}
catch {
    Say 'FATAL' (Err $_.Exception)
    Say 'FATAL.where' $_.ScriptStackTrace
}
finally {
    Start-Sleep -Milliseconds 600
    New-Item -ItemType File -Path $stopFile -Force | Out-Null
    try { [void]$closer.EndInvoke($closerHandle) } catch { }
    $closer.Dispose()
    if (Test-Path $boxLog) {
        foreach ($l in (Get-Content $boxLog)) { $num, $rest = $l -split "`t", 2; $lines.Add("messagebox.$num`t$rest") }
    } else { $lines.Add("messagebox.count`t0") }
    Say 'seconds' ('{0:N1}' -f $watch.Elapsed.TotalSeconds)
    [System.IO.File]::WriteAllLines($Out, $lines, $utf8)
}
