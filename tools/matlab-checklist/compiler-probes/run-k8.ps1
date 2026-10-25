<#
.SYNOPSIS
    Builds stage K8's .NET assemblies with JGraph and runs the same client against them, recording
    what happened the way record-k8.ps1 recorded R2025b (compiler and SDK plan, stage K8):
    recordings-jgraph\k8_build.out.txt, k8_client.out.txt and k8_differences.txt.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build    probe_k8_build.m, unchanged, under jgraph -batch: the assemblies in <WorkRoot>\b
      client   src\k8client with its using lines turned from MathWorks.MATLAB.NET to
               JGraph.MATLAB.NET, built against <WorkRoot>\b\dn\K8 and JGraph.MATLAB.NET.Arrays.dll,
               and run through run-quiet.ps1 with JGRAPH_RUNTIME_ROOT set
      compare  compare-k8.py over both engines' recordings

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1. Without one, a stand-in is made in
    <WorkRoot>\runtime whose bin\win64 is a junction to the CLI's build folder.

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

if (-not $Runtime) {
    # A stand-in runtime: the marker of the CLI's version, and its build folder as bin\win64.
    $Runtime = Join-Path $WorkRoot 'runtime'
    $bin = Join-Path $Runtime 'bin\win64'
    if (-not (Test-Path $bin)) {
        New-Item -ItemType Directory -Force (Split-Path $bin) | Out-Null
        & cmd.exe /c mklink /J "$bin" "$jgraphFolder" | Out-Null
    }
    $version = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $jgraphFolder 'JGraph.Deploy.dll')).Version.ToString(3)
    [System.IO.File]::WriteAllText((Join-Path $Runtime 'jgraph-runtime.json'), "{`n  `"version`": `"$version`",`n  `"release`": `"25.2`"`n}", $utf8)
}
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path

# The MWArray API alone in its folder, as a client references it: the client's build then copies it
# and no engine assembly, and it finds the engine in the runtime's bin\win64 at run time.
$arrays = Join-Path $Runtime 'toolbox\dotnetbuilder\bin\win64\net8.0\JGraph.MATLAB.NET.Arrays.dll'
if (-not (Test-Path $arrays)) {
    New-Item -ItemType Directory -Force (Split-Path $arrays) | Out-Null
    Copy-Item (Join-Path $Runtime 'bin\win64\JGraph.MATLAB.NET.Arrays.dll') $arrays
}

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
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

if ($skipped -notcontains 'build') {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $statement = "cd('$($here.Replace('\', '/'))'); probe_k8_build('$($WorkRoot.Replace('\', '/'))/b')"
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    $p.WaitForExit()
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText($err)
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Remove-Item -LiteralPath $out, $err
    Write-Recording 'k8_build.out.txt' $text
    Write-Host ("build: exit {0} in {1:F1} s" -f $p.ExitCode, $clock.Elapsed.TotalSeconds)
}

if ($skipped -notcontains 'client') {
    $lines = New-Object System.Collections.Generic.List[string]
    $clientSrc = Join-Path $WorkRoot 'clientsrc'
    if (Test-Path $clientSrc) { Remove-Item -LiteralPath $clientSrc -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here 'src\k8client') -Destination $clientSrc -Recurse
    $program = Join-Path $clientSrc 'Program.cs'
    $source = [System.IO.File]::ReadAllText($program).Replace('using MathWorks.MATLAB.NET.', 'using JGraph.MATLAB.NET.').Replace('= MathWorks.MATLAB.NET.', '= JGraph.MATLAB.NET.')
    [System.IO.File]::WriteAllText($program, $source, $utf8)
    $buildLog = Join-Path $WorkRoot 'client_build.txt'
    $clientOut = Join-Path $WorkRoot 'client'
    if (Test-Path $clientOut) { Remove-Item -LiteralPath $clientOut -Recurse -Force }
    & dotnet build (Join-Path $clientSrc 'k8client.csproj') -c Release -o $clientOut `
        "-p:K8Out=$WorkRoot\b\dn\K8" '-p:JGraph=true' "-p:JGraphArrays=$arrays" -nologo *> $buildLog
    $lines.Add("BUILD`texit $LASTEXITCODE")
    & dotnet build-server shutdown *> $null
    $exe = Join-Path $clientOut 'k8client.exe'
    if (Test-Path $exe) {
        $transcript = [System.IO.Path]::GetTempFileName()
        $quiet = @{ Exe = $exe; Environment = @{ JGRAPH_RUNTIME_ROOT = $Runtime; MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache_client') };
            TimeoutSeconds = 900; OutFile = $transcript; WorkingDirectory = $clientOut }
        & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
        $out = [System.IO.File]::ReadAllText($transcript)
        Remove-Item -LiteralPath $transcript
        foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
        $engine = @(Get-ChildItem -LiteralPath $clientOut -Filter 'JGraph.*.dll' | Where-Object { $_.Name -ne 'JGraph.MATLAB.NET.Arrays.dll' })
        Write-Host ("client: {0} engine assemblies beside it (the runtime's bin\win64 serves the rest)" -f $engine.Count)
    }
    else { $lines.Add("  no program (see $buildLog)") }
    Write-Recording 'k8_client.out.txt' ($lines -join "`n")
}

if ($skipped -notcontains 'compare') {
    & $Python (Join-Path $here 'compare-k8.py')
}
