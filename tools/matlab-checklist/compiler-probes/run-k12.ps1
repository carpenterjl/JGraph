<#
.SYNOPSIS
    Builds stage K12's C++ shared libraries with JGraph and runs the same client against them,
    recording what happened the way record-k12.ps1 recorded R2025b (compiler and SDK plan, stage
    K12): recordings-jgraph\k12_build.out.txt, k12_client.out.txt, k12_paths.out.txt, k12_out\* and
    k12_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build    probe_k12_build.m, unchanged, under jgraph -batch: the libraries in <WorkRoot>\b, the
               generated text copied into recordings-jgraph\k12_out as record-k12.ps1 copies R2025b's
      client   src\k12client, unchanged, compiled with MSVC against the runtime's include folder
               and the header JGraph's main build generated, linked with the runtime's jgrt.lib, and
               run in each of its modes through run-quiet.ps1 with the runtime's runtime\win64 first
               on the PATH and R2025b's left off it, JGraph's two archives beside it
      paths    the client's paths mode in record-k12.ps1's four setups, JGraph's archives as pick.ctf
      compare  compare-k12.py over both engines' recordings

    The work root should be as long as record-k12.ps1's (scratch\k12 and scratch\j12), so lines that
    name a path break where R2025b's did.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1. Without one, a stand-in is made in
    <WorkRoot>\runtime: bin\win64 a junction to the CLI's build folder, runtime\win64 its jgrt.dll,
    and the headers and jgrt.lib from the CLI's extern folder.

.PARAMETER Jgraph
    The jgraph.exe that builds; the CLI's Debug build when not given.

.PARAMETER Modes
    The client modes to run (local, calls, typed, twice, outproc, runmain).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $Skip = '',
    [string] $Modes = 'local,calls,typed,twice,outproc,runmain',
    [string] $Python = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
if (-not $Jgraph) { $Jgraph = Join-Path $repoRoot 'src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe' }
$Jgraph = (Resolve-Path -LiteralPath $Jgraph).Path
$jgraphFolder = Split-Path $Jgraph
$recordings = Join-Path $here 'recordings-jgraph'
New-Item -ItemType Directory -Force $recordings | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','

if (-not $Runtime) {
    $Runtime = Join-Path $WorkRoot 'runtime'
    $bin = Join-Path $Runtime 'bin\win64'
    if (-not (Test-Path $bin)) {
        New-Item -ItemType Directory -Force (Split-Path $bin) | Out-Null
        & cmd.exe /c mklink /J "$bin" "$jgraphFolder" | Out-Null
    }
    $version = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $jgraphFolder 'JGraph.Deploy.dll')).Version.ToString(3)
    [System.IO.File]::WriteAllText((Join-Path $Runtime 'jgraph-runtime.json'), "{`n  `"version`": `"$version`",`n  `"release`": `"25.2`"`n}", $utf8)
    New-Item -ItemType Directory -Force (Join-Path $Runtime 'runtime\win64'), (Join-Path $Runtime 'include'), (Join-Path $Runtime 'lib\win64') | Out-Null
    Copy-Item (Join-Path $jgraphFolder 'jgrt.dll') (Join-Path $Runtime 'runtime\win64\jgrt.dll') -Force
    Copy-Item (Join-Path $jgraphFolder 'extern\include\*') (Join-Path $Runtime 'include') -Recurse -Force
    Copy-Item (Join-Path $jgraphFolder 'extern\lib\win64\microsoft\jgrt.lib') (Join-Path $Runtime 'lib\win64\jgrt.lib') -Force
}
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($jgraphFolder, '<JGRAPH>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text = [regex]::Replace($text, 'generated on: \d{2}-[A-Za-z]{3}-\d{4}', 'generated on: <DATE>')
    $text
}

function Run-Jgraph([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    $p.WaitForExit()
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Remove-Item -LiteralPath $out, $err
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1:F1} s" -f $target, $clock.Elapsed.TotalSeconds)
}

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
        Environment = $env2; PathFirst = @(Join-Path $Runtime 'runtime\win64'); NoMatlabRuntime = $true;
        TimeoutSeconds = 900; OutFile = $transcript; WorkingDirectory = $folder }
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
$slash = { param($p) $p.Replace('\', '/') }
if ($skipped -notcontains 'build') {
    Run-Jgraph "cd('$(& $slash $here)'); probe_k12_build('$(& $slash $build)')" 'k12_build.out.txt'
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
    $include = "/I`"$Runtime\include`" /I`"$generic`""
    $flags = '/nologo /O2 /MD /EHsc /std:c++17 /utf-8 /W3 /Zc:__cplusplus /bigobj /D_CRT_SECURE_NO_DEPRECATE'
    Invoke-Msvc @("cd /d `"$clients`"", "cl $flags $include `"$here\src\k12client\k12client.cpp`" /Fe:k12client.exe /link `"$Runtime\lib\win64\jgrt.lib`"") `
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
    # record-k12.ps1's three folders, each holding a different archive named pick.ctf.
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

if ($skipped -notcontains 'compare') {
    & $Python (Join-Path $here 'compare-k12.py')
}
