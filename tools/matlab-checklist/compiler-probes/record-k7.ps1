<#
.SYNOPSIS
    Records what R2025b's mcc does with the switches stage K6 left (compiler and SDK plan, stage K7),
    inside MATLAB and from the operating system's command line.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      switches  probe_k7_switches under matlab -batch -noFigureWindows -> recordings\k7_switches.out.txt
      more      probe_k7_more over the same source folder -> recordings\k7_more.out.txt
      cmd       R2025b's bin\mcc.bat run from cmd.exe in the probe's source folder, its standard output,
                standard error, exit code and time per case -> recordings\k7_cmd.out.txt

    Nothing built is run, and no window is shown.

.PARAMETER WorkRoot
    A scratch folder outside the repo. The switches step builds into <WorkRoot>\b, the cmd step into
    <WorkRoot>\cmd; the cmd step needs the source folder the switches step made (<WorkRoot>\src).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 5400
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | ForEach-Object { , @($_[0].Substring(2), ($_[1] -replace '>$', '-NODRIVE>')) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Write-Recording([string] $name, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $name), (Tokens $text), $utf8)
    Write-Host ("{0,-24} written" -f $name)
}

function Run-Process([string] $file, [string] $arguments, [string] $directory, [int] $timeout) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $file -ArgumentList $arguments -WorkingDirectory $directory `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $null = $p.Handle
        $timedOut = -not $p.WaitForExit($timeout * 1000)
        if ($timedOut) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $watch.Stop()
        [pscustomobject]@{
            Out = [System.IO.File]::ReadAllText($out); Err = [System.IO.File]::ReadAllText($err)
            Exit = $p.ExitCode; TimedOut = $timedOut; Seconds = $watch.Elapsed.TotalSeconds
        }
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

if ($skipped -notcontains 'switches') {
    $r = Run-Process (Join-Path $MatlabRoot 'bin\matlab.exe') `
        "-batch `"cd('$here'); probe_k7_switches('$WorkRoot')`" -noFigureWindows" $here $TimeoutSeconds
    $text = $r.Out
    if ($r.Err.Trim()) { $text += "`n" + ((($r.Err.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    if ($r.TimedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
    Write-Recording 'k7_switches.out.txt' $text
    Write-Host ("  matlab exit {0}" -f $r.Exit)
}

if ($skipped -notcontains 'more') {
    $r = Run-Process (Join-Path $MatlabRoot 'bin\matlab.exe') `
        "-batch `"cd('$here'); probe_k7_more('$WorkRoot')`" -noFigureWindows" $here $TimeoutSeconds
    $text = $r.Out
    if ($r.Err.Trim()) { $text += "`n" + ((($r.Err.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    if ($r.TimedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
    Write-Recording 'k7_more.out.txt' $text
    Write-Host ("  matlab exit {0}" -f $r.Exit)
}

if ($skipped -notcontains 'cmd') {
    $src = Join-Path $WorkRoot 'src'
    $cmdOut = Join-Path $WorkRoot 'cmd'
    if (Test-Path -LiteralPath $cmdOut) { Remove-Item -LiteralPath $cmdOut -Recurse -Force }
    New-Item -ItemType Directory -Force $cmdOut | Out-Null
    $mcc = Join-Path $MatlabRoot 'bin\mcc.bat'
    # Each case: name, the arguments as cmd.exe reads them; {D} is -d and the case's own folder.
    $cases = @(
        @('no_args', ''),
        @('question', '-?'),
        @('build', "-m k7_hello.m {D}"),
        @('build_verbose', "-v -m k7_hello.m {D}"),
        @('missing_file', "-m k7_nosuch.m {D}"),
        @('bad_flag', "-Q -m k7_hello.m {D}"),
        @('no_wrapper', "k7_hello.m {D}"),
        @('single_quotes', "-W 'main:k7q' -T link:exe k7_hello.m {D}"),
        @('double_quotes', "-W `"main:k7dq`" -T link:exe k7_hello.m {D}"),
        @('license_Y', "-Y k7_license.dat -m k7_hello.m {D}"),
        @('warn_list', '-w list'),
        @('startup_warn', "-m startup\k7_start.m {D}"),
        @('startup_error', "-w error -m startup\k7_start.m {D}")
    )
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($c in $cases) {
        $dir = Join-Path $cmdOut $c[0]
        $arguments = $c[1].Replace('{D}', "-d `"$dir`"")
        $r = Run-Process $env:ComSpec "/d /c `"`"$mcc`" $arguments`"" $src 900
        $lines.Add("CASE`t$($c[0])")
        $lines.Add("RUN`tmcc $($c[1])")
        $lines.Add(("EXIT`t{0}{1}" -f $r.Exit, $(if ($r.TimedOut) { ' TIMEOUT' } else { '' })))
        $lines.Add(("SECONDS`t{0:0.0}" -f $r.Seconds))
        foreach ($l in (($r.Out -replace "`r`n", "`n").TrimEnd("`n") -split "`n")) { if ($l -ne '') { $lines.Add("  OUT|$l") } }
        foreach ($l in (($r.Err -replace "`r`n", "`n").TrimEnd("`n") -split "`n")) { if ($l -ne '') { $lines.Add("  ERR|$l") } }
        if ($c[1].Contains('{D}')) {
            $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue |
                ForEach-Object { '{0} ({1})' -f $_.FullName.Substring($dir.Length + 1), $_.Length } | Sort-Object)
            $lines.Add("FILES`t" + ($files -join ' | '))
        }
        Write-Host ("  {0,-16} exit {1}  {2:0.0} s" -f $c[0], $r.Exit, $r.Seconds)
    }
    Write-Recording 'k7_cmd.out.txt' ($lines -join "`n")
}
