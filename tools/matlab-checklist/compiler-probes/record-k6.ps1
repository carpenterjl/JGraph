<#
.SYNOPSIS
    Records what R2025b's MATLAB Compiler builds for a standalone program and what those programs do
    (compiler and SDK plan, stage K6): mcc -m and -e, compiler.build.standaloneApplication and
    standaloneWindowsApplication, the files and resources each build writes, the console programs'
    output and exit codes, and - with the user's leave, given 2026-10-24 - the windowed programs'
    splash, windows, output and exit.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build     probe_k6_build under matlab -batch -noFigureWindows -> recordings\k6_build.out.txt
      roots     probe_k6_roots: whether mcc searches its main's own folder -> recordings\k6_roots.out.txt
      pe        pe-resources.py over every program built -> recordings\k6_pe.out.txt
      console   the console programs through run-quiet.ps1 -> recordings\k6_console.out.txt
      windows   the windowed programs (and the console ones that show a window) through
                run-quiet.ps1 -AllowWindowed, each watched by a window timeline -> recordings\k6_windows.out.txt

    The window timeline lists, every 100 ms, the top-level windows of the program and of every
    process under it (R2025b draws its figures in MATLABWindow.exe), and prints each change with
    the seconds since the start. A case's action closes its window only through UI Automation
    patterns (WindowPattern.Close, InvokePattern on a named button), never by synthesized input.

.PARAMETER WorkRoot
    A scratch folder outside the repo. The builds go in <WorkRoot>\b.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $Only = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe',
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$build = Join-Path $WorkRoot 'b'
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$onlyCases = @($Only -split ',' | Where-Object { $_ })

function Tokens([string] $text) {
    $tokens = @(@($build, '<B>'), @($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
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

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $null = $p.Handle
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        Write-Recording $target $text
        Write-Host ("  matlab exit {0}" -f $p.ExitCode)
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

function Run-Quiet([string] $exe, [string[]] $arguments, [hashtable] $environment, [switch] $windowed) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -InputObject @($arguments) -Compress
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); Environment = $environment;
        TimeoutSeconds = 240; OutFile = $transcript }
    if ($windowed) { $quiet.AllowWindowed = $true }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $out
}

if ($skipped -notcontains 'build') { Run-Matlab "cd('$here'); probe_k6_build('$build')" 'k6_build.out.txt' }

if ($skipped -notcontains 'roots') {
    Run-Matlab "cd('$here'); addpath(fullfile('$here', 'src')); probe_k6_roots('$WorkRoot\roots')" 'k6_roots.out.txt'
}

if ($skipped -notcontains 'pe') {
    $exes = @(Get-ChildItem -LiteralPath $build -Recurse -Filter *.exe | Sort-Object FullName | ForEach-Object { $_.FullName })
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($exe in $exes) {
        $lines.Add("CASE`t" + $exe.Substring($build.Length + 1))
        foreach ($l in (& $Python (Join-Path $here 'pe-resources.py') $exe)) { $lines.Add($l) }
    }
    Write-Recording 'k6_pe.out.txt' ($lines -join "`n")
}

$cache = Join-Path $WorkRoot 'cache'
$baseEnvironment = @{ MCR_CACHE_ROOT = $cache }

if ($skipped -notcontains 'console') {
    $cases = @(
        @('hello.args', 'm_plain\k6_hello.exe', @('a', 'b c')),
        @('hello.question', 'm_plain\k6_hello.exe', @('-?')),
        @('hello.dash_h', 'm_plain\k6_hello.exe', @('-h')),
        @('hello.question_then_arg', 'm_plain\k6_hello.exe', @('-?', 'x')),
        @('hello.slash_question_upper', 'm_plain\k6_hello.exe', @('/?')),
        @('named', 'm_named\k6_named.exe', @()),
        @('helped.question', 'm_help\k6_helped.exe', @('-?')),
        @('helped.dash_h', 'm_help\k6_helped.exe', @('-h')),
        @('helped.slash_question', 'm_help\k6_helped.exe', @('/?')),
        @('helped.dash_help', 'm_help\k6_helped.exe', @('-help')),
        @('helped.dashdash_help', 'm_help\k6_helped.exe', @('--help')),
        @('helped.after_arg', 'm_help\k6_helped.exe', @('x', '-?')),
        @('helped.plain', 'm_help\k6_helped.exe', @('x')),
        @('api_m.question', 'api_m\k6_api.exe', @('-?')),
        @('api_m.args', 'api_m\k6_api.exe', @('1')),
        @('api_numeric.args', 'api_m_numeric\k6_hello.exe', @('1', '[2 3]')),
        @('separate', 'm_separate\k6_sep.exe', @('s')),
        @('api_separate', 'api_m_separate\k6_hello.exe', @('s')),
        @('m_err', 'm_err\k6_err.exe', @())
    )
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($c in $cases) {
        if ($onlyCases.Count -and $onlyCases -notcontains $c[0]) { continue }
        $exe = Join-Path $build $c[1]
        $lines.Add("CASE`t$($c[0])")
        $lines.Add("RUN`t$($c[1]) $($c[2] -join ' ')")
        if (-not (Test-Path -LiteralPath $exe)) { $lines.Add("  not built"); continue }
        $out = Run-Quiet $exe $c[2] $baseEnvironment
        foreach ($l in ($out -split "`r?`n")) { if ($l -ne '') { $lines.Add("  $l") } }
        Write-Host ("  {0}" -f $c[0])
    }
    Write-Recording 'k6_console.out.txt' ($lines -join "`n")
}

if ($skipped -notcontains 'windows') {
    . (Join-Path $here 'k6-windows.ps1')

    # Each case: name, program, arguments, action. Actions: none; close:<title> (WindowPattern.Close
    # once the window has been shown 1.5 s); press:<title>:<button> (InvokePattern on the button, or
    # WindowPattern.Close after 8 s without one); dismiss (any window shown 4 s: its texts, then close).
    $cases = @(
        @('e_plain', 'e_plain\k6_hello.exe', @('a', 'b c'), 'none'),
        @('e_wait', 'e_wait\k6_wait.exe', @(), 'none'),
        @('api_e_wait', 'api_e\k6_wait.exe', @(), 'none'),
        @('api_e_default', 'api_e_default\k6_hello.exe', @(), 'none'),
        @('e_fig', 'e_fig\k6_fig.exe', @(), 'close:k6 figure'),
        @('m_fig', 'm_fig\k6_fig.exe', @(), 'close:k6 figure'),
        @('e_err', 'e_err\k6_err.exe', @(), 'dismiss'),
        @('e_app_m', 'e_app_m\k6_app.exe', @(), 'press:k6 app:Close'),
        @('e_app_mlapp', 'e_app_mlapp\k6_app.exe', @(), 'press:k6 app:Close'),
        @('m_app_mlapp', 'm_app_mlapp\k6_app_console.exe', @(), 'close:k6 app'),
        @('api_e_app', 'api_e_app\k6_app.exe', @(), 'press:k6 app:Close')
    )
    $lines = Invoke-K6Windows -Cases $cases -Build $build -WorkRoot $WorkRoot -Cache $cache -Only $onlyCases
    Write-Recording 'k6_windows.out.txt' ($lines -join "`n")
}
