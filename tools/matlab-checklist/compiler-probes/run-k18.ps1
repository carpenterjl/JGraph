<#
.SYNOPSIS
    Runs stage K18's probe against JGraph (compiler and SDK plan): probe_k18_package, unchanged, under
    jgraph -batch, and compares its transcript with R2025b's recording.

.DESCRIPTION
    The probe builds a console program, a windowed one and a Python package, then packages installers
    with compiler.package.installer and compiler.runtime.customInstaller in every form, asks
    compiler.package.InstallerOptions every question, and mcrinstaller and compiler.runtime.version
    theirs. JGraph's installers are WiX bundles built by jgraph's own packager, so the machine needs
    the WiX 6 tool (the NuGet package cache has it after the installer projects restore) and the JGraph
    Runtime MSI of this version (installer\build-runtime-installer.ps1 builds it, which
    RuntimeInstaller.Find looks for in the checkout). No installer is run.

    Writes recordings-jgraph\k18_package.out.txt and k18_differences.txt (compare-k18.py).

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Jgraph
    The jgraph.exe that runs the probe; the CLI's Debug build when not given.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Jgraph,
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
    [switch] $SkipCompare
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

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($jgraphFolder, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

$probe = Join-Path $WorkRoot 'p'
$statement = "cd('$($here.Replace('\', '/'))'); probe_k18_package('$($probe.Replace('\', '/'))')"
$out = Join-Path $WorkRoot 'probe.stdout'
$err = Join-Path $WorkRoot 'probe.stderr'
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Jgraph -ArgumentList @('-batch', "`"$statement`"") -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru -Wait
$text = [System.IO.File]::ReadAllText($out)
$errText = [System.IO.File]::ReadAllText($err)
if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
$text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
$target = Join-Path $recordings 'k18_package.out.txt'
[System.IO.File]::WriteAllText($target, (Tokens $text), $utf8)
Write-Host ("k18_package (jgraph)         exit {0}  {1:F1} s" -f $p.ExitCode, $clock.Elapsed.TotalSeconds)

if (-not $SkipCompare) {
    $differences = Join-Path $recordings 'k18_differences.txt'
    & $Python -I (Join-Path $here 'compare-k18.py') $target *> $differences
    $code = $LASTEXITCODE
    Get-Content $differences | Select-Object -First 3
    Write-Host ("k18_differences.txt          {0} unexplained" -f $code)
}
