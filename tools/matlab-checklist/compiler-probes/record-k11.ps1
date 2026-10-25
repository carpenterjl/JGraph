<#
.SYNOPSIS
    Records how R2025b builds a C shared library (compiler and SDK plan, stage K11): what mcc -W lib
    and compiler.build.cSharedLibrary print, write and refuse, the generated text, the DLL's
    resources, the refusal when no C compiler is found, and a client of the library run. Writes
    recordings\k11_build.out.txt, k11_pe.out.txt, k11_names.out.txt, k11_nocompiler.out.txt,
    k11_client.out.txt and k11_out\*.

.DESCRIPTION
    probe_k11_build runs under matlab -batch -noFigureWindows over a copy of src\k11. Each DLL it
    built is then read by pe-resources.py (its version block, imports and the archive appended).
    probe_k11_names writes functions with odd signatures and long names and prints what -T codegen
    generates for each. probe_k11_nocompiler runs in a second MATLAB with ProgramFiles(x86) naming an empty folder, so
    mbuild's search finds no Visual Studio (no other compiler is installed here). Last, src\k11client
    is compiled with MSVC against the main build's k11lib.h and k11lib.lib and R2025b's mclmcrrt.lib
    (the separate complex model, mbuild's default), and run through run-quiet.ps1 in each of its
    modes. The main build's generated text is copied into recordings\k11_out with the paths and the
    date replaced by tokens. No window is shown; the client turns the beep off before its first error.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build', 'pe', 'names', 'nocompiler' and 'client' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe',
    [int] $TimeoutSeconds = 2400
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
    # A generated file's header lists mcc's arguments cut into lines of 77, paths split where no token
    # below can find them (the user's folder showed through), so the list is replaced whole.
    $text = [regex]::Replace($text, '(?m)^(?<p>(?:  \|)?) \* Arguments:(?<nl>\r?\n)(?:\k<p> \* (?!/)[^\r\n]*\r?\n)+', '${p} * Arguments:${nl}${p} * <ARGUMENTS>${nl}')
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    # A handler's text is printed with each backslash doubled (k10common.h's escaping).
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    # The generated files' header dates the build; mbuild's scratch folder is named for the moment.
    $text = [regex]::Replace($text, ' \* Date: [^\r\n]*', ' * Date: <DATE>')
    $text = [regex]::Replace($text, 'mex_[0-9]+_[0-9]+', 'mex_<N>')
    $text
}

function Run-Matlab([string] $statement, [string] $target, [hashtable] $environment = @{}) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $saved = @{}
    foreach ($k in $environment.Keys) {
        $saved[$k] = [Environment]::GetEnvironmentVariable($k)
        [Environment]::SetEnvironmentVariable($k, $environment[$k])
    }
    try {
        $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    }
    finally {
        foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) }
    }
    try {
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

$build = Join-Path $WorkRoot 'b'
$main = Join-Path $build 'main'
if ($skipped -notcontains 'build') {
    Run-Matlab "cd('$here'); probe_k11_build('$build')" 'k11_build.out.txt'
    $outCopy = Join-Path $recordings 'k11_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    $texts = Get-ChildItem -LiteralPath $main -File | Where-Object { $_.Extension -in '.c', '.h', '.def', '.exports', '.txt', '.json', '.log' }
    foreach ($f in $texts) {
        [System.IO.File]::WriteAllText((Join-Path $outCopy $f.Name), (Tokens ([System.IO.File]::ReadAllText($f.FullName))), $utf8)
    }
    $gs = Join-Path $build 'api\k11plaincSharedLibrary\GettingStarted.html'
    if (Test-Path $gs) { [System.IO.File]::WriteAllText((Join-Path $outCopy 'GettingStarted.html'), (Tokens ([System.IO.File]::ReadAllText($gs))), $utf8) }
    Write-Host "k11_out                      $(@($texts).Count) files"
}

if ($skipped -notcontains 'pe') {
    # Every DLL and import library the builds made: the version block, the imports, what is appended.
    $lines = @()
    foreach ($dll in Get-ChildItem -LiteralPath $build -Recurse -File | Where-Object { $_.Extension -eq '.dll' } | Sort-Object FullName) {
        $rel = $dll.FullName.Substring($build.Length + 1)
        $lines += "== $rel"
        $text = & $Python (Join-Path $here 'pe-resources.py') $dll.FullName 2>&1
        foreach ($l in $text) { if ("$l" -notmatch '^(file|bytes|image\.bytes|overlay\.bytes)\t') { $lines += "  $l" } }
    }
    Save $lines 'k11_pe.out.txt'
}

if ($skipped -notcontains 'names') {
    Run-Matlab "cd('$here'); probe_k11_names('$WorkRoot\nm')" 'k11_names.out.txt'
}

if ($skipped -notcontains 'nocompiler') {
    $empty = Join-Path $WorkRoot 'nothing'
    New-Item -ItemType Directory -Force $empty | Out-Null
    Run-Matlab "cd('$here'); probe_k11_nocompiler('$WorkRoot\nc')" 'k11_nocompiler.out.txt' @{ 'ProgramFiles(x86)' = $empty }
}

$clients = Join-Path $WorkRoot 'clients'
if ($skipped -notcontains 'client') {
    if (Test-Path $clients) { Remove-Item -LiteralPath $clients -Recurse -Force }
    New-Item -ItemType Directory -Force $clients | Out-Null
    $include = "/I`"$MatlabRoot\extern\include`" /I`"$here\src\k10client`" /I`"$main`""
    $libpath = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" /LIBPATH:`"$main`""
    $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /DMATLAB_DEFAULT_RELEASE=R2017b'
    Invoke-Msvc @("cd /d `"$clients`"", "cl $flags $include `"$here\src\k11client\k11client.c`" /Fe:k11client.exe /link $libpath k11lib.lib mclmcrrt.lib") `
        (Join-Path $WorkRoot 'clients_build.txt')
    Copy-Item -LiteralPath (Join-Path $main 'k11lib.dll') -Destination $clients -Force
    $exe = Join-Path $clients 'k11client.exe'
    $lines = @()
    foreach ($case in @(@(), @('plain'), @('twice'))) {
        $lines += Run-Client $exe $case $(if ($case.Count) { $case -join ' ' } else { 'handlers' })
    }
    Save $lines 'k11_client.out.txt'
}
