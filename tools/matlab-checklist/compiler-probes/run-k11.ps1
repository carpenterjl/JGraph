<#
.SYNOPSIS
    Builds stage K11's C shared libraries with JGraph and runs the same clients against them, recording
    what happened the way record-k11.ps1 recorded R2025b (compiler and SDK plan, stage K11):
    recordings-jgraph\k11_build.out.txt, k11_names.out.txt, k11_pe.out.txt, k11_client.out.txt,
    k11_feval_<model>.out.txt and k11_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build    probe_k11_build.m, unchanged, under jgraph -batch: the libraries in <WorkRoot>\b
      names    probe_k11_names.m, unchanged, under jgraph -batch
      pe       pe-resources.py over every DLL the builds made
      client   src\k11client compiled with MSVC against the main build's k11lib.h and k11lib.lib and
               the runtime's jgrt.lib, run in each of its modes through run-quiet.ps1 with the
               runtime's runtime\win64 first on the PATH and R2025b's left off it
      feval    stage K10's k10feval in both complex models against k10lib built from src\k10 by
               JGraph's mcc -W lib (where run-k10.ps1 compiled R2025b's generated k10lib.c)
      compare  compare-k11.py over both engines' recordings

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1. Without one, a stand-in is made in
    <WorkRoot>\runtime: bin\win64 a junction to the CLI's build folder, runtime\win64 its jgrt.dll,
    and the headers and jgrt.lib from the CLI's extern folder.

.PARAMETER Jgraph
    The jgraph.exe that builds; the CLI's Debug build when not given.
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
    Copy-Item (Join-Path $jgraphFolder 'extern\include\*.h') (Join-Path $Runtime 'include') -Force
    Copy-Item (Join-Path $jgraphFolder 'extern\lib\win64\microsoft\jgrt.lib') (Join-Path $Runtime 'lib\win64\jgrt.lib') -Force
}
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path

function Tokens([string] $text) {
    # The generated header's argument list, cut into lines of 77 with its paths split, replaced whole (record-k11.ps1).
    $text = [regex]::Replace($text, '(?m)^(?<p>(?:  \|)?) \* Arguments:(?<nl>\r?\n)(?:\k<p> \* (?!/)[^\r\n]*\r?\n)+', '${p} * Arguments:${nl}${p} * <ARGUMENTS>${nl}')
    $tokens = @(@($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($jgraphFolder, '<JGRAPH>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    # A handler's text is printed with each backslash doubled (k10common.h's escaping).
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text = [regex]::Replace($text, ' \* Date: [^\r\n]*', ' * Date: <DATE>')
    $text = [regex]::Replace($text, 'jgcl_[0-9a-f]{32}', 'jgcl_<N>')
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
$main = Join-Path $build 'main'
$slash = { param($p) $p.Replace('\', '/') }
if ($skipped -notcontains 'build') {
    Run-Jgraph "cd('$(& $slash $here)'); probe_k11_build('$(& $slash $build)')" 'k11_build.out.txt'
}

if ($skipped -notcontains 'names') {
    Run-Jgraph "cd('$(& $slash $here)'); probe_k11_names('$(& $slash (Join-Path $WorkRoot 'nm'))')" 'k11_names.out.txt'
}

if ($skipped -notcontains 'pe') {
    $lines = @()
    foreach ($dll in Get-ChildItem -LiteralPath $build -Recurse -File | Where-Object { $_.Extension -eq '.dll' } | Sort-Object FullName) {
        $rel = $dll.FullName.Substring($build.Length + 1)
        $lines += "== $rel"
        $text = & $Python (Join-Path $here 'pe-resources.py') $dll.FullName 2>&1
        foreach ($l in $text) { if ("$l" -notmatch '^(file|bytes|image\.bytes|overlay\.bytes)\t') { $lines += "  $l" } }
    }
    Save $lines 'k11_pe.out.txt'
}

$clients = Join-Path $WorkRoot 'clients'
if ($skipped -notcontains 'client') {
    if (Test-Path $clients) { Remove-Item -LiteralPath $clients -Recurse -Force }
    New-Item -ItemType Directory -Force $clients | Out-Null
    $include = "/I`"$Runtime\include`" /I`"$here\src\k10client`" /I`"$main`""
    $libpath = "/LIBPATH:`"$Runtime\lib\win64`" /LIBPATH:`"$main`""
    $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /DMATLAB_DEFAULT_RELEASE=R2017b'
    Invoke-Msvc @("cd /d `"$clients`"", "cl $flags $include `"$here\src\k11client\k11client.c`" /Fe:k11client.exe /link $libpath k11lib.lib jgrt.lib") `
        (Join-Path $WorkRoot 'clients_build.txt')
    Copy-Item -LiteralPath (Join-Path $main 'k11lib.dll') -Destination $clients -Force
    $exe = Join-Path $clients 'k11client.exe'
    $lines = @()
    foreach ($case in @(@(), @('plain'), @('twice'))) {
        $lines += Run-Client $exe $case $(if ($case.Count) { $case -join ' ' } else { 'handlers' })
    }
    Save $lines 'k11_client.out.txt'
}

$fevalBuild = Join-Path $WorkRoot 'k10'
$fevalClients = Join-Path $WorkRoot 'k10clients'
if ($skipped -notcontains 'feval') {
    # Stage K10's library, built by JGraph's mcc -W lib from the same files R2025b's was.
    if (Test-Path $fevalBuild) { Remove-Item -LiteralPath $fevalBuild -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $fevalBuild 'src') | Out-Null
    Copy-Item (Join-Path $here 'src\k10\*') (Join-Path $fevalBuild 'src')
    $driver = Join-Path $fevalBuild 'build.m'
    $names = (Get-ChildItem -LiteralPath (Join-Path $fevalBuild 'src') -Filter '*.m' | Sort-Object Name | ForEach-Object { "'$($_.Name)'" }) -join ', '
    [System.IO.File]::WriteAllText($driver, "cd('$(& $slash (Join-Path $fevalBuild 'src'))');`nmcc('-W', 'lib:k10lib', '-T', 'link:lib', '-d', '$(& $slash (Join-Path $fevalBuild 'lib'))', $names);`n", $utf8)
    $out = & $Jgraph -batch $driver 2>&1
    Write-Host "k10lib: $(($out | Out-String).Trim())"
    $lib = Join-Path $fevalBuild 'lib'
    if (-not (Test-Path (Join-Path $lib 'k10lib.dll'))) { throw 'JGraph built no k10lib.dll.' }
    if (Test-Path $fevalClients) { Remove-Item -LiteralPath $fevalClients -Recurse -Force }
    New-Item -ItemType Directory -Force $fevalClients | Out-Null
    $include = "/I`"$Runtime\include`" /I`"$here\src\k10client`" /I`"$lib`""
    $libpath = "/LIBPATH:`"$Runtime\lib\win64`" /LIBPATH:`"$lib`""
    $flags = '/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS'
    $commands = @("cd /d `"$fevalClients`"")
    foreach ($m in $models.Keys) {
        $commands += "cl $flags $($models[$m]) $include `"$here\src\k10client\k10feval.c`" /Fe:k10feval_$m.exe /Fo:k10feval_$m.obj /link $libpath k10lib.lib jgrt.lib"
    }
    Invoke-Msvc $commands (Join-Path $WorkRoot 'k10clients_build.txt')
    Copy-Item -LiteralPath (Join-Path $lib 'k10lib.dll') -Destination $fevalClients -Force
    foreach ($m in $models.Keys) {
        $exe = Join-Path $fevalClients "k10feval_$m.exe"
        $lines = @(Run-Client $exe @() 'safe')
        foreach ($n in 1..12) { $lines += Run-Client $exe @('risky', "$n") "risky $n" }
        Save $lines "k11_feval_$m.out.txt"
    }
}

if ($skipped -notcontains 'compare') {
    & $Python (Join-Path $here 'compare-k11.py')
}
