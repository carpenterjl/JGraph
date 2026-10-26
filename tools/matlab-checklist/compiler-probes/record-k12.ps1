<#
.SYNOPSIS
    Records how R2025b builds a C++ shared library for the MATLAB Data API (compiler and SDK plan,
    stage K12): what compiler.build.cppSharedLibrary and mcc -W cpplib print, write and refuse, the
    strongly typed header they generate, and a C++ client of the libraries run every way. Writes
    recordings\k12_build.out.txt, k12_client.out.txt and k12_out\*.

.DESCRIPTION
    probe_k12_build runs under matlab -batch -noFigureWindows over a copy of stage K9's functions
    (src\k9). src\k12client is then compiled with MSVC against R2025b's extern\include and the main
    build's k12libv2.hpp, linked as mbuild links a C++ client (libMatlabDataArray.lib delay-loaded,
    libMatlabCppSharedLib.lib, mclmcrrt.lib), and run through run-quiet.ps1 in each of its modes, the
    two archives beside it. The paths mode runs four times, an archive named pick.ctf placed in a
    different set of the three folders R2025b's header searches each time (CPPSHARED_BASE_CTF_PATH's,
    the working folder, the program's). The generated text is copied into recordings\k12_out with the
    paths and the date replaced by tokens. No window is shown; the client turns the beep off (k9quiet)
    before its first error.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build', 'client' and 'paths' to leave out.

.PARAMETER Modes
    The client modes to run (local, calls, typed, twice, outproc, runmain).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $Modes = 'local,calls,typed,twice,outproc,runmain',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
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
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    # The generated header and readme date the build.
    $text = [regex]::Replace($text, 'generated on: \d{2}-[A-Za-z]{3}-\d{4}', 'generated on: <DATE>')
    $text = [regex]::Replace($text, ' \* Date: [^\r\n]*', ' * Date: <DATE>')
    $text = [regex]::Replace($text, 'mex_[0-9]+_[0-9]+', 'mex_<N>')
    $text
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
        -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    try {
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
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
function Run-Client([string] $exe, [string[]] $arguments, [string] $header, [string] $folder, [hashtable] $environment = @{}) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject @($arguments)
    $env2 = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache') }
    foreach ($k in $environment.Keys) { $env2[$k] = $environment[$k] }
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json));
        Environment = $env2; MatlabRoot = $MatlabRoot; TimeoutSeconds = 900; OutFile = $transcript; WorkingDirectory = $folder }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $lines = @("== $header")
    foreach ($l in ($out -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
    Write-Host ("client {0,-24} {1} lines" -f $header, $lines.Count)
    $lines
}

function Save([string[]] $lines, [string] $target) {
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1} lines" -f $target, $lines.Count)
}

$build = Join-Path $WorkRoot 'b'
$main = Join-Path $build 'cpp\k12lib'
$variadic = Join-Path $build 'cpp\k12v'
if ($skipped -notcontains 'build') {
    Run-Matlab "cd('$here'); probe_k12_build('$build')" 'k12_build.out.txt'
    $outCopy = Join-Path $recordings 'k12_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    $copied = 0
    foreach ($pair in @(@($main, 'main'), @($variadic, 'variadic'), @((Join-Path $build 'defaults'), 'defaults'))) {
        if (-not (Test-Path $pair[0])) { continue }
        foreach ($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File | Where-Object { $_.Extension -in '.hpp', '.txt', '.json', '.log', '.html' }) {
            $rel = $f.FullName.Substring($pair[0].Length + 1).Replace('\', '_')
            [System.IO.File]::WriteAllText((Join-Path $outCopy ($pair[1] + '_' + $rel)), (Tokens ([System.IO.File]::ReadAllText($f.FullName))), $utf8)
            $copied++
        }
    }
    Write-Host "k12_out                      $copied files"
}

$clients = Join-Path $WorkRoot 'clients'
$exe = Join-Path $clients 'k12client.exe'
if ($skipped -notcontains 'client') {
    if (Test-Path $clients) { Remove-Item -LiteralPath $clients -Recurse -Force }
    New-Item -ItemType Directory -Force $clients | Out-Null
    $generic = Join-Path $main 'v2\generic_interface'
    $include = "/I`"$MatlabRoot\extern\include`" /I`"$generic`""
    $libs = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" libMatlabDataArray.lib libMatlabCppSharedLib.lib mclmcrrt.lib delayimp.lib /DELAYLOAD:libMatlabDataArray.dll"
    $flags = '/nologo /O2 /MD /EHsc /std:c++17 /utf-8 /W3 /Zc:__cplusplus /bigobj /DMSVC /DIBMPC /D_CRT_SECURE_NO_DEPRECATE'
    Invoke-Msvc @("cd /d `"$clients`"", "cl $flags $include `"$here\src\k12client\k12client.cpp`" /Fe:k12client.exe /link $libs") `
        (Join-Path $WorkRoot 'clients_build.txt')
    Copy-Item -LiteralPath (Join-Path $generic 'k12lib.ctf') -Destination $clients -Force
    Copy-Item -LiteralPath (Join-Path $variadic 'v2\generic_interface\k12v.ctf') -Destination $clients -Force
    $lines = @()
    foreach ($mode in ($Modes -split ',')) {
        $arguments = @($mode)
        if ($mode -eq 'runmain') { $arguments += @('first', 'second') }
        $lines += Run-Client $exe $arguments $mode $clients
    }
    Save $lines 'k12_client.out.txt'
}

if ($skipped -notcontains 'paths') {
    # Three folders, each holding a different archive named pick.ctf: the main library (k9make),
    # the variadic one (k9nargs) and the one-function default build (k9echo alone).
    $p = Join-Path $WorkRoot 'paths'
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    $envDir = Join-Path $p 'env'; $cwdDir = Join-Path $p 'cwd'; $exeDir = Join-Path $p 'exe'
    foreach ($d in $envDir, $cwdDir, $exeDir, (Join-Path $cwdDir 'sub')) { New-Item -ItemType Directory -Force $d | Out-Null }
    $pexe = Join-Path $exeDir 'k12client.exe'
    Copy-Item -LiteralPath $exe -Destination $pexe
    $echoCtf = Join-Path $build 'defaults\v2\generic_interface\k9echo.ctf'
    $lines = @()
    $setups = @(
        @{ Name = 'all three'; Env = $true; Cwd = $true; Exe = $true },
        @{ Name = 'cwd and exe'; Env = $false; Cwd = $true; Exe = $true },
        @{ Name = 'exe only'; Env = $false; Cwd = $false; Exe = $true },
        @{ Name = 'env names a missing folder'; Env = $false; Cwd = $true; Exe = $false; Missing = $true })
    foreach ($s in $setups) {
        Remove-Item -LiteralPath (Join-Path $envDir 'pick.ctf'), (Join-Path $cwdDir 'pick.ctf'), (Join-Path $exeDir 'pick.ctf') -ErrorAction SilentlyContinue
        if ($s.Env) { Copy-Item -LiteralPath (Join-Path $clients 'k12v.ctf') -Destination (Join-Path $envDir 'pick.ctf') }
        if ($s.Cwd) { Copy-Item -LiteralPath (Join-Path $clients 'k12lib.ctf') -Destination (Join-Path $cwdDir 'pick.ctf') }
        if ($s.Exe) { Copy-Item -LiteralPath $echoCtf -Destination (Join-Path $exeDir 'pick.ctf') }
        Copy-Item -LiteralPath (Join-Path $clients 'k12lib.ctf') -Destination (Join-Path $cwdDir 'sub\inner.ctf') -Force
        $environment = @{}
        if ($s.Env) { $environment['CPPSHARED_BASE_CTF_PATH'] = $envDir }
        if ($s.Missing) { $environment['CPPSHARED_BASE_CTF_PATH'] = (Join-Path $p 'nowhere') }
        $names = @('pick.ctf', 'missing.ctf', 'sub\inner.ctf', 'sub/inner.ctf', (Join-Path $cwdDir 'sub\inner.ctf'), (Join-Path $cwdDir 'nothere.ctf'), '.\pick.ctf')
        $lines += Run-Client $pexe (@('paths') + $names) ("paths " + $s.Name) $cwdDir $environment
    }
    Save $lines 'k12_paths.out.txt'
}
