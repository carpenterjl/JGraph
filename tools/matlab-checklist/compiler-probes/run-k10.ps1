<#
.SYNOPSIS
    Runs stage K10's C clients against the JGraph Runtime's native bridge, as record-k10.ps1 ran
    them against R2025b's runtime (compiler and SDK plan, stage K10), recording the transcripts in
    recordings-jgraph\k10_mx_<model>.out.txt, k10_app.out.txt and k10_feval_<model>.out.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build    build_k10_jgraph.m under jgraph -batch over a copy of src\k10: k10lib.ctf, the
               archive R2025b's k10lib carries, made by JGraph
      clients  the same client sources, and R2025b's generated k10lib.c from recordings\k10_out,
               compiled with MSVC against the runtime's include and jgrt.lib (k10lib.dll with
               JGraph's archive appended, as R2025b's mcc appends its own)
      mx, app, feval   the runs, each through run-quiet.ps1 with the runtime's runtime\win64 first
               on the PATH and R2025b's left off it
      compare  compare-k10.py over both engines' recordings

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1. Without one, a stand-in is made in
    <WorkRoot>\runtime: bin\win64 a junction to the CLI's build folder, runtime\win64 its jgrt.dll,
    and the headers and jgrt.lib from the repo's native folder.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runtime,
    [string] $Jgraph,
    [string] $Skip = '',
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
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$models = [ordered]@{ separate = '/DMATLAB_DEFAULT_RELEASE=R2017b'; interleaved = '/DMATLAB_MEXCMD_RELEASE=R2018a' }

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
    Copy-Item (Join-Path $repoRoot 'native\jgrt\include\*.h') (Join-Path $Runtime 'include') -Force
    Copy-Item (Join-Path $repoRoot 'native\win-x64\jgrt\jgrt.lib') (Join-Path $Runtime 'lib\win64\jgrt.lib') -Force
}
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    # A handler's text is printed with each backslash doubled (k10common.h's escaping).
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
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

function Run-Client([string] $exe, [string[]] $arguments, [string] $header) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject @($arguments)
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json));
        Environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache') }; PathFirst = @(Join-Path $Runtime 'runtime\win64');
        NoMatlabRuntime = $true; TimeoutSeconds = 600; OutFile = $transcript; WorkingDirectory = (Split-Path $exe) }
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
$lib = Join-Path $build 'lib'
if ($skipped -notcontains 'build') {
    if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $build 'src') | Out-Null
    Copy-Item (Join-Path $here 'src\k10\*') (Join-Path $build 'src')
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $env:K10_OUT = $build
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath (Join-Path $Runtime 'bin\win64\jgraph.exe') -ArgumentList @('-batch', ('"{0}"' -f (Join-Path $here 'build_k10_jgraph.m'))) `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    $p.WaitForExit()
    Write-Host ("build: {0} {1} in {2:F1} s" -f ([System.IO.File]::ReadAllText($out).Trim()), ([System.IO.File]::ReadAllText($err).Trim()), $clock.Elapsed.TotalSeconds)
    Remove-Item -LiteralPath $out, $err
    if (-not (Test-Path (Join-Path $lib 'k10lib.ctf'))) { throw "The build made no k10lib.ctf." }
}

$clients = Join-Path $WorkRoot 'clients'
$src = Join-Path $here 'src\k10client'
$generated = Join-Path $here 'recordings\k10_out'
if ($skipped -notcontains 'clients') {
    if (Test-Path $clients) { Remove-Item -LiteralPath $clients -Recurse -Force }
    New-Item -ItemType Directory -Force $clients | Out-Null
    Copy-Item (Join-Path $generated 'k10lib.c'), (Join-Path $generated 'k10lib.h'), (Join-Path $generated 'k10lib.def') $clients
    $include = "/I`"$Runtime\include`" /I`"$src`" /I`"$clients`""
    $libpath = "/LIBPATH:`"$Runtime\lib\win64`" /LIBPATH:`"$clients`""
    $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS'
    $commands = @("cd /d `"$clients`"",
        # R2025b's generated library, built as its mbuild builds it (the separate model), against JGraph's headers.
        "cl $flags $($models['separate']) $include /LD k10lib.c /Fe:k10lib.dll /link /DEF:k10lib.def $libpath jgrt.lib")
    foreach ($m in $models.Keys) {
        $commands += "cl $flags $($models[$m]) $include `"$src\k10mx.c`" /Fe:k10mx_$m.exe /Fo:k10mx_$m.obj /link $libpath jgrt.lib"
        $commands += "cl $flags $($models[$m]) $include `"$src\k10feval.c`" /Fe:k10feval_$m.exe /Fo:k10feval_$m.obj /link $libpath k10lib.lib jgrt.lib"
    }
    $commands += "cl $flags $($models['separate']) $include `"$src\k10app.c`" /Fe:k10app.exe /Fo:k10app.obj /link $libpath jgrt.lib"
    Invoke-Msvc $commands (Join-Path $WorkRoot 'clients_build.txt')

    # The archive appended after the library's last byte, where mclGetEmbeddedCtfStream finds it.
    $dll = Join-Path $clients 'k10lib.dll'
    $bytes = [System.IO.File]::ReadAllBytes($dll) + [System.IO.File]::ReadAllBytes((Join-Path $lib 'k10lib.ctf'))
    [System.IO.File]::WriteAllBytes($dll, $bytes)
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

if ($skipped -notcontains 'compare') {
    & $Python (Join-Path $here 'compare-k10.py')
}
