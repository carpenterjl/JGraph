<#
.SYNOPSIS
    Runs stage K0's probe programs in JGraph's deployed session mode and writes one recording per
    case to recordings-jgraph\<case>.out.txt, beside K0's R2025b recordings, then a list of the
    lines that differ (compiler and SDK plan, stage K1).

.DESCRIPTION
    Until the archive (stage K3) and the launcher (stage K6) exist, a deployed program is run as
    jgraph -batch <main.m> --deployed <folder> [--deployed-numeric] -- <arguments>, with the folder
    standing in for the extracted archive. Each program's folder is laid out as R2025b extracted
    it: the component's own folder holding the main and the probe helpers (and the files mcc
    compiled in beside them), and an assets folder holding the -a data files. The cases, run
    folders and path tokens are run-k0.ps1's, so the two sets of recordings compare line by line.

    jgraph.exe is run through run-quiet.ps1 like every other program here, for its timeout and
    its job object. It makes no beep: that is the MATLAB Runtime's.

.PARAMETER WorkRoot
    A scratch folder outside the repo for the program folders and run folders.

.PARAMETER Jgraph
    The jgraph.exe to run. Defaults to the Debug build.

.PARAMETER Cases
    Case names to run. Omit to run all of them, the two -n refusals included (no beep here).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Jgraph,
    [string[]] $Cases,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
if (-not $Jgraph) { $Jgraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
$quiet = Join-Path $here 'run-quiet.ps1'
$src = Join-Path $here 'src'
$recordings = Join-Path $here 'recordings-jgraph'
New-Item -ItemType Directory -Force $recordings | Out-Null
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)

# What each program's archive held, as build_k0.m built it: its main, the helpers it calls, and
# what -a or a pragma added.
$helpers = @('k0_quiet.m', 'k0_try.m', 'k0_do.m', 'k0_show.m', 'k0_flat.m', 'k0_warned.m')
$programs = @{
    k0_args    = @{ Main = 'k0_args.m' }
    k0_argsn   = @{ Main = 'k0_args.m'; Numeric = $true }
    k0_exit    = @{ Main = 'k0_exit.m' }
    k0_env     = @{ Main = 'k0_env.m'; Assets = @('data.mat') }
    k0_env_sep = @{ Main = 'k0_env.m'; Assets = @('data.mat') }
    k0_path    = @{ Main = 'k0_path.m'; Extra = @('k0_inside_helper.m'); Assets = @('inside.txt') }
    k0_feval   = @{ Main = 'k0_feval.m' }
    k0_fevalp  = @{ Main = 'k0_fevalp.m'; Extra = @('k0_feval.m', 'k0_target.m') }
    k0_stubs   = @{ Main = 'k0_stubs.m' }
    k0_figure  = @{ Main = 'k0_figure.m' }
    k0_ropts   = @{ Main = 'k0_ropts.m' }
    k0_startup = @{ Main = 'k0_startup.m'; From = 'startup_case'; Extra = @('startup.m') }
}

function Case($name, $exe, [string[]] $argv = @(), [hashtable] $envv = @{}, [int] $timeout = 180) {
    [pscustomobject]@{ Name = $name; Exe = $exe; Args = $argv; Env = $envv; Timeout = $timeout }
}

# run-k0.ps1's cases, in its order.
$all = @(
    (Case 'args_none' 'k0_args')
    (Case 'args_plain' 'k0_args' @('1', 'two', 'three four', 'a"b', 'c:\x\', '', '-x', '[1 2]'))
    (Case 'argsn_ok' 'k0_argsn' @('1', '2.5', '[1 2;3 4]', '-3', '1e3', '1+2i', '0x1F', "'txt'", '(1)', ' 7'))
    (Case 'argsn_int8' 'k0_argsn' @('1', 'int8(4)'))
    (Case 'argsn_word' 'k0_argsn' @('hello'))
    (Case 'exit_return' 'k0_exit' @('return'))
    (Case 'exit_noargs' 'k0_exit')
    (Case 'exit_error' 'k0_exit' @('error'))
    (Case 'exit_nested' 'k0_exit' @('nested'))
    (Case 'exit_builtin' 'k0_exit' @('builtin'))
    (Case 'exit_cause' 'k0_exit' @('cause'))
    (Case 'exit_exit3' 'k0_exit' @('exit3'))
    (Case 'exit_quit4' 'k0_exit' @('quit4'))
    (Case 'exit_quitforce' 'k0_exit' @('quitforce'))
    (Case 'exit_warning' 'k0_exit' @('warning'))
    (Case 'exit_disp' 'k0_exit' @('disp'))
    (Case 'env_plain' 'k0_env')
    (Case 'env_userdata' 'k0_env' @('-mcruserdata', 'ParallelProfile:k0.mlsettings', 'after'))
    (Case 'env_separate_ctf' 'k0_env_sep')
    (Case 'env_cache_root' 'k0_env' @() @{ MCR_CACHE_ROOT = '<RUN>\cache' })
    (Case 'path' 'k0_path')
    (Case 'feval_nopragma' 'k0_feval')
    (Case 'feval_pragma' 'k0_fevalp')
    (Case 'stubs' 'k0_stubs' @() @{} 90)
    (Case 'figure_invisible' 'k0_figure' @() @{} 180)
    (Case 'startup' 'k0_startup')
)
if ($Cases) {
    $Cases = @($Cases | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    foreach ($c in $Cases) { if (-not ($all | Where-Object Name -eq $c)) { Write-Error "no case named '$c'" } }
    $all = @($all | Where-Object { $Cases -contains $_.Name })
}

# The program folders: <WorkRoot>\<program>\ctf\<program> for the code, ...\ctf\assets for data.
$assets = Join-Path $WorkRoot 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
[System.IO.File]::WriteAllText((Join-Path $assets 'inside.txt'), "inside text`n", $utf8)
$made = Join-Path $assets 'data.mat'
$maker = Join-Path $WorkRoot 'make_k1_data.m'
[System.IO.File]::WriteAllText($maker, "note = 'k0 data'; v = [1 2 3];`nsave('$($made.Replace("'", "''"))', 'note', 'v');`n", $utf8)
& $Jgraph -batch $maker | Out-Null
foreach ($name in $programs.Keys) {
    $p = $programs[$name]
    $ctf = Join-Path $WorkRoot "$name\ctf"
    if (Test-Path -LiteralPath $ctf) { Remove-Item -LiteralPath $ctf -Recurse -Force }
    $component = Join-Path $ctf $name
    New-Item -ItemType Directory -Force $component | Out-Null
    $from = if ($p.From) { Join-Path $src $p.From } else { $src }
    Copy-Item -LiteralPath (Join-Path $from $p.Main) -Destination $component
    foreach ($h in $helpers) { Copy-Item -LiteralPath (Join-Path $src $h) -Destination $component }
    foreach ($e in @($p.Extra | Where-Object { $_ })) { Copy-Item -LiteralPath (Join-Path $from $e) -Destination $component }
    if ($p.Assets) {
        $a = Join-Path $ctf 'assets'
        New-Item -ItemType Directory -Force $a | Out-Null
        foreach ($f in $p.Assets) { Copy-Item -LiteralPath (Join-Path $assets $f) -Destination $a }
    }
}

$runRoot = Join-Path $WorkRoot 'runs'
$cache = Join-Path $env:LOCALAPPDATA 'JGraph\RuntimeCache'
$temp = [System.IO.Path]::GetTempPath().TrimEnd('\')
$jgraphRoot = Split-Path -Parent $Jgraph
foreach ($c in $all) {
    $run = Join-Path $runRoot $c.Name
    if (Test-Path -LiteralPath $run) { Remove-Item -LiteralPath $run -Recurse -Force }
    New-Item -ItemType Directory -Force $run | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $run 'k0_outside.m'), "function r = k0_outside()`nr = 'outside function ran';`nend`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $run 'outside_script.m'), "disp('outside script ran');`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $run 'outside.txt'), "outside text`n", $utf8)

    $p = $programs[$c.Exe]
    $ctf = Join-Path $WorkRoot "$($c.Exe)\ctf"
    $main = Join-Path (Join-Path $ctf $c.Exe) $p.Main
    $argv = @('-batch', $main, '--deployed', $ctf)
    if ($p.Numeric) { $argv += '--deployed-numeric' }
    $argv += '--'
    $argv += $c.Args
    $envv = @{}
    foreach ($k in $c.Env.Keys) { $envv[$k] = $c.Env[$k] -replace '<RUN>', $run }
    $transcript = Join-Path $run 'transcript.txt'
    & $quiet -Exe $Jgraph -Arguments $argv -Environment $envv -WorkingDirectory $run `
        -TimeoutSeconds $c.Timeout -MatlabRoot $MatlabRoot -OutFile $transcript 6>$null | Out-Null
    $text = [System.IO.File]::ReadAllText($transcript)
    $head = "CASE`t$($c.Name)`nRUN`t$($c.Exe).exe $((@($c.Args | ForEach-Object { if ($_ -match '[\s"]' -or $_ -eq '') { '''' + $_ + '''' } else { $_ } })) -join ' ')`n"
    foreach ($k in $c.Env.Keys) { $head += "ENV`t$k=$($c.Env[$k])`n" }
    $text = $head + $text
    # run-k0.ps1's tokens; <BUILD> is the folder of program folders, <CACHE> JGraph's runtime cache
    # and <MATLABROOT> the JGraph build's folder, which is what matlabroot answers here.
    $tokens = @(
        @($run, '<RUN>'), @($WorkRoot, '<BUILD>'), @($cache, '<CACHE>'), @($temp, '<TEMP>'),
        @($jgraphRoot, '<MATLABROOT>'), @($env:USERPROFILE, '<HOME>'), @($repoRoot, '<REPO>')
    )
    $tokens += @($tokens | Where-Object { $_[0] -match '^[A-Za-z]:\\' } | ForEach-Object { , @($_[0].Substring(3), ($_[1] -replace '>$', '-NODRIVE>')) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    [System.IO.File]::WriteAllText((Join-Path $recordings "$($c.Name).out.txt"), $text, $utf8)
    $exitLine = ($text -split "`n" | Where-Object { $_ -like "EXIT`t*" } | Select-Object -Last 1)
    $secLine = ($text -split "`n" | Where-Object { $_ -like "SECONDS`t*" } | Select-Object -Last 1)
    Write-Host ("{0,-20} {1,-40} {2}" -f $c.Name, $exitLine, $secLine)
}

# The lines that differ from R2025b's, case by case: '<' is R2025b's, '>' JGraph's. SECONDS lines
# and the beep line (K0's probes print the beep state; JGraph's beep is never on a speaker) are left out.
$diff = New-Object System.Text.StringBuilder
foreach ($c in $all) {
    $theirs = Join-Path $here "recordings\$($c.Name).out.txt"
    $ours = Join-Path $recordings "$($c.Name).out.txt"
    if (-not (Test-Path -LiteralPath $theirs)) { continue }
    $a = @([System.IO.File]::ReadAllLines($theirs) | Where-Object { $_ -notlike "SECONDS`t*" -and $_ -notlike "beep.default`t*" })
    $b = @([System.IO.File]::ReadAllLines($ours) | Where-Object { $_ -notlike "SECONDS`t*" -and $_ -notlike "beep.default`t*" })
    $d = @(Compare-Object -ReferenceObject $a -DifferenceObject $b -SyncWindow 400 -CaseSensitive)
    [void] $diff.AppendLine("== $($c.Name): $($d.Count) line(s) differ")
    foreach ($line in $d) {
        $mark = if ($line.SideIndicator -eq '<=') { '<' } else { '>' }
        $shown = [string] $line.InputObject
        if ($shown.Length -gt 300) { $shown = $shown.Substring(0, 300) + '...' }
        [void] $diff.AppendLine("$mark $shown")
    }
}
[System.IO.File]::WriteAllText((Join-Path $recordings 'differences.txt'), $diff.ToString().Replace("`r`n", "`n"), $utf8)
