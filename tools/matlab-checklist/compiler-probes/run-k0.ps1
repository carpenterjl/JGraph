<#
.SYNOPSIS
    Runs the stage K0 probe standalones (built by build_k0.m) through run-quiet.ps1 and writes
    one recording per case to recordings\<case>.out.txt (compiler and SDK plan, stage K0).

.DESCRIPTION
    Each case names a built program, its arguments, extra environment and timeout. A case runs
    in a fresh run folder holding the files a deployed program must not see as code
    (k0_outside.m, outside_script.m) and a data file (outside.txt). Machine paths in the
    recordings are replaced by tokens so they compare across machines:
    <CACHE> (the MATLAB Runtime cache), <BUILD>, <RUN>, <TEMP>, <MATLABROOT>, <HOME>, <REPO>,
    and each of them without its drive as <...-NODRIVE>.

    No case is a windowed program, and every program runs under run-quiet.ps1, so none can
    raise a Windows error dialog. The MATLAB Runtime beeps (MessageBeep) on every error, caught
    or not, so each probe main calls k0_quiet first to turn the beep off. The cases that fail
    before main can run (mcc -n argument refusals) are left out unless named in -Cases: tell
    the user before running one.

.PARAMETER BuildRoot
    The folder build_k0.m built into.

.PARAMETER Cases
    Case names to run. Omit to run all of them.

.EXAMPLE
    powershell -File run-k0.ps1 -BuildRoot C:\scratch\k0_build -Cases env_plain,path
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BuildRoot,
    [string[]] $Cases,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$quiet = Join-Path $here 'run-quiet.ps1'
$recordings = Join-Path $here 'recordings'
New-Item -ItemType Directory -Force $recordings | Out-Null
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$runRoot = Join-Path $BuildRoot 'runs'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path

function Case($name, $exe, [string[]] $argv = @(), [hashtable] $envv = @{}, [int] $timeout = 180, [string] $after, [bool] $beeps = $false) {
    [pscustomobject]@{ Name = $name; Exe = $exe; Args = $argv; Env = $envv; Timeout = $timeout; After = $after; Beeps = $beeps }
}

$all = @(
    (Case 'args_none' 'k0_args')
    (Case 'args_plain' 'k0_args' @('1', 'two', 'three four', 'a"b', 'c:\x\', '', '-x', '[1 2]'))
    (Case 'argsn_ok' 'k0_argsn' @('1', '2.5', '[1 2;3 4]', '-3', '1e3', '1+2i', '0x1F', "'txt'", '(1)', ' 7'))
    (Case 'argsn_int8' 'k0_argsn' @('1', 'int8(4)') @{} 180 $null $true)
    (Case 'argsn_word' 'k0_argsn' @('hello') @{} 180 $null $true)
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
    (Case 'ropts' 'k0_ropts' @() @{} 180 'k0_ropts.log')
    (Case 'startup' 'k0_startup')
)
if ($Cases) {
    $Cases = @($Cases | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    foreach ($c in $Cases) { if (-not ($all | Where-Object Name -eq $c)) { Write-Error "no case named '$c'" } }
    $all = @($all | Where-Object { $Cases -contains $_.Name })
}
else {
    # A case marked Beeps fails before main runs, so k0_quiet never turns the beep off and the
    # MATLAB Runtime beeps on the user's speakers. Such a case runs only when named.
    $all = @($all | Where-Object { -not $_.Beeps })
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$cache = Join-Path $env:LOCALAPPDATA 'MathWorks\MatlabRuntimeCache'
$temp = [System.IO.Path]::GetTempPath().TrimEnd('\')
foreach ($c in $all) {
    $run = Join-Path $runRoot $c.Name
    if (Test-Path -LiteralPath $run) { Remove-Item -LiteralPath $run -Recurse -Force }
    New-Item -ItemType Directory -Force $run | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $run 'k0_outside.m'), "function r = k0_outside()`nr = 'outside function ran';`nend`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $run 'outside_script.m'), "disp('outside script ran');`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $run 'outside.txt'), "outside text`n", $utf8)

    $exeDir = Join-Path $BuildRoot $c.Exe
    $exe = Join-Path $exeDir ($c.Exe + '.exe')
    $envv = @{}
    foreach ($k in $c.Env.Keys) { $envv[$k] = $c.Env[$k] -replace '<RUN>', $run }
    $transcript = Join-Path $run 'transcript.txt'
    & $quiet -Exe $exe -Arguments $c.Args -Environment $envv -WorkingDirectory $run `
        -TimeoutSeconds $c.Timeout -MatlabRoot $MatlabRoot -OutFile $transcript 6>$null | Out-Null
    $text = [System.IO.File]::ReadAllText($transcript)
    if ($c.After) {
        $f = Join-Path $run $c.After
        if (Test-Path -LiteralPath $f) {
            $body = ([System.IO.File]::ReadAllText($f) -replace "`r`n", "`n").TrimEnd("`n")
            $indented = @($body -split "`n" | ForEach-Object { "  $_" })
            $text += "FILE`t$($c.After)`n" + ($indented -join "`n") + "`n"
        }
        else { $text += "FILE`t$($c.After) not written`n" }
    }
    $head = "CASE`t$($c.Name)`nRUN`t$($c.Exe).exe $((@($c.Args | ForEach-Object { if ($_ -match '[\s"]' -or $_ -eq '') { '''' + $_ + '''' } else { $_ } })) -join ' ')`n"
    foreach ($k in $c.Env.Keys) { $head += "ENV`t$k=$($c.Env[$k])`n" }
    $text = $head + $text
    # Longest paths first, so a run folder inside the temp folder becomes <RUN>, not <TEMP>\...
    $tokens = @(
        @($run, '<RUN>'), @($BuildRoot, '<BUILD>'), @($cache, '<CACHE>'), @($temp, '<TEMP>'),
        @($MatlabRoot, '<MATLABROOT>'), @($env:USERPROFILE, '<HOME>'), @($repoRoot, '<REPO>')
    )
    # An archive keeps a file added from outside the main's folder under its full path minus the
    # drive (ctfroot\Users\...), so each path is also replaced in that form.
    $tokens += @($tokens | Where-Object { $_[0] -match '^[A-Za-z]:\\' } | ForEach-Object { , @($_[0].Substring(3), ($_[1] -replace '>$', '-NODRIVE>')) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    [System.IO.File]::WriteAllText((Join-Path $recordings "$($c.Name).out.txt"), $text, $utf8)
    $exitLine = ($text -split "`n" | Where-Object { $_ -like "EXIT`t*" } | Select-Object -Last 1)
    $secLine = ($text -split "`n" | Where-Object { $_ -like "SECONDS`t*" } | Select-Object -Last 1)
    Write-Host ("{0,-20} {1,-40} {2}" -f $c.Name, $exitLine, $secLine)
}
