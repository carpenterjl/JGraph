<#
.SYNOPSIS
    Records what R2025b's C runtime API answers a C program (compiler and SDK plan, stage K10, the
    native bridge): the mxArray functions with no component, the mcl application functions, and
    what crosses the call boundary of a C shared library. Writes recordings\k10_build.out.txt,
    k10_mx_<model>.out.txt, k10_app.out.txt, k10_feval_<model>.out.txt and k10_out\*.

.DESCRIPTION
    probe_k10_build builds src\k10 as the C shared library k10lib with mcc -W lib:k10lib -T link:lib
    under matlab -batch -noFigureWindows. The clients in src\k10client are then compiled with MSVC
    against R2025b's extern\include and mclmcrrt.lib, each twice where the complex model matters:
    'separate' with /DMATLAB_DEFAULT_RELEASE=R2017b (mbuild's default) and 'interleaved' with
    /DMATLAB_MEXCMD_RELEASE=R2018a. Every run goes through run-quiet.ps1; a risky case (a misuse
    R2025b may refuse, ignore or crash on) runs in a process of its own. k10lib's generated text is
    copied into recordings\k10_out with the paths and the date replaced by tokens: run-k10.ps1
    compiles that k10lib.c against the JGraph Runtime's headers. No window is shown, and k10feval
    turns the beep off before its first error.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build', 'mx', 'app' and 'feval' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$models = [ordered]@{ separate = '/DMATLAB_DEFAULT_RELEASE=R2017b'; interleaved = '/DMATLAB_MEXCMD_RELEASE=R2018a' }

function Tokens([string] $text) {
    # The generated header's argument list, cut into lines of 77 with its paths split, replaced whole (stage K11).
    $text = [regex]::Replace($text, '(?m)^(?<p>(?:  \|)?) \* Arguments:(?<nl>\r?\n)(?:\k<p> \* (?!/)[^\r\n]*\r?\n)+', '${p} * Arguments:${nl}${p} * <ARGUMENTS>${nl}')
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    # A handler's text is printed with each backslash doubled (k10common.h's escaping).
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

# MSVC's environment, as mbuild finds it: the newest Visual Studio with the x64 C++ tools.
function Invoke-Msvc([string[]] $commands, [string] $log) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'MSVC was not found (vswhere found no installation with the x64 C++ tools).' }
    $cmd = Join-Path $WorkRoot ('msvc_' + [guid]::NewGuid().ToString('N') + '.cmd')
    $lines = @('@echo off', "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul") +
        @($commands | ForEach-Object { "$_ || exit /b 1" })
    [System.IO.File]::WriteAllLines($cmd, $lines)
    & cmd.exe /c $cmd *> $log
    $code = $LASTEXITCODE
    Remove-Item -LiteralPath $cmd -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "The MSVC build failed; see $log." }
}

# One client run through run-quiet.ps1: its transcript's lines, indented under a header line.
function Run-Client([string] $exe, [string[]] $arguments, [string] $header) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject @($arguments)
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json));
        Environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache') }; MatlabRoot = $MatlabRoot;
        TimeoutSeconds = 600; OutFile = $transcript; WorkingDirectory = (Split-Path $exe) }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $lines = @("== $header")
    foreach ($l in ($out -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
    $lines
}

function Save([string[]] $lines, [string] $target) {
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1} lines" -f $target, $lines.Count)
}

$lib = Join-Path $WorkRoot 'b\lib'
if ($skipped -notcontains 'build') {
    Run-Matlab "cd('$here'); probe_k10_build('$WorkRoot\b')" 'k10_build.out.txt'
    $outCopy = Join-Path $recordings 'k10_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    $texts = Get-ChildItem -LiteralPath $lib -File | Where-Object { $_.Extension -in '.c', '.h', '.def', '.exports', '.txt', '.html', '.json', '.log' }
    foreach ($f in $texts) {
        $text = [System.IO.File]::ReadAllText($f.FullName)
        $text = [regex]::Replace($text, ' \* Date: [^\r\n]*', ' * Date: <DATE>')
        [System.IO.File]::WriteAllText((Join-Path $outCopy $f.Name), (Tokens $text), $utf8)
    }
    Write-Host "k10_out                      $(@($texts).Count) files"
}

$clients = Join-Path $WorkRoot 'clients'
$src = Join-Path $here 'src\k10client'
if (($skipped -notcontains 'mx') -or ($skipped -notcontains 'app') -or ($skipped -notcontains 'feval')) {
    New-Item -ItemType Directory -Force $clients | Out-Null
    $include = "/I`"$MatlabRoot\extern\include`" /I`"$MatlabRoot\extern\include\win64`" /I`"$src`" /I`"$lib`""
    $libpath = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" /LIBPATH:`"$lib`""
    $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS'
    $commands = @("cd /d `"$clients`"")
    foreach ($m in $models.Keys) {
        $commands += "cl $flags $($models[$m]) $include `"$src\k10mx.c`" /Fe:k10mx_$m.exe /Fo:k10mx_$m.obj /link $libpath mclmcrrt.lib"
        $commands += "cl $flags $($models[$m]) $include `"$src\k10feval.c`" /Fe:k10feval_$m.exe /Fo:k10feval_$m.obj /link $libpath k10lib.lib mclmcrrt.lib"
    }
    $commands += "cl $flags $($models['separate']) $include `"$src\k10app.c`" /Fe:k10app.exe /Fo:k10app.obj /link $libpath mclmcrrt.lib"
    Invoke-Msvc $commands (Join-Path $WorkRoot 'clients_build.txt')
    Copy-Item -LiteralPath (Join-Path $lib 'k10lib.dll') -Destination $clients -Force
}

if ($skipped -notcontains 'mx') {
    foreach ($m in $models.Keys) {
        $exe = Join-Path $clients "k10mx_$m.exe"
        $lines = @(Run-Client $exe @() 'safe')
        foreach ($n in 1..25) { $lines += Run-Client $exe @('risky', "$n") "risky $n" }
        Save $lines "k10_mx_$m.out.txt"
    }
}

if ($skipped -notcontains 'app') {
    $exe = Join-Path $clients 'k10app.exe'
    $lines = @()
    foreach ($case in @(@('plain'), @('noproxy'), @('before'), @('options'), @('logfile', "$clients\k10app.log"),
            @('split', "$clients\k10split.log"), @('startmsg'), @('unknown'), @('runmain', 'a', 'b c', ''))) {
        $lines += Run-Client $exe $case ($case -join ' ')
    }
    Save $lines 'k10_app.out.txt'
}

if ($skipped -notcontains 'feval') {
    foreach ($m in $models.Keys) {
        $exe = Join-Path $clients "k10feval_$m.exe"
        $lines = @(Run-Client $exe @() 'safe')
        foreach ($n in 1..12) { $lines += Run-Client $exe @('risky', "$n") "risky $n" }
        Save $lines "k10_feval_$m.out.txt"
    }
}
