<#
.SYNOPSIS
    Records stage K12's edge probes in both engines (compiler and SDK plan, stage K12): the MATLAB
    Data API read, referenced and compared past the end of an array, where R2025b's library throws
    its own internal exception or ends the process - recordings\k12_edge.out.txt for R2025b's
    library and recordings-jgraph\k12_edge.out.txt for JGraph's headers (ADR 0268's divergences).

.DESCRIPTION
    src\k12edge\kind.cpp and eq.cpp are each compiled twice with MSVC, from the same source: against
    R2025b's extern\include and libMatlabDataArray.lib, and against the repo's native\jgrt\include and
    native\win-x64\jgrt\jgrt.lib. kind runs once; eq runs once per case, each in a process of its own,
    because R2025b's library ends the process for some. Every run goes through run-quiet.ps1, so a
    crash ends the program with its status instead of a dialog, and the transcript keeps what the run
    printed and its EXIT line. No MATLAB session and no JGraph build is needed.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER MatlabRoot
    The R2025b installation whose headers and libMatlabDataArray the R2025b builds use.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$cases = 'num', 'str', 'ref', 'refok', 'st', 'stf', 'streq', 'refeq'

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

# One run through run-quiet.ps1: its transcript's lines but the time, indented under a header line.
function Run-Probe([string] $exe, [string[]] $arguments, [string] $header, [hashtable] $quietOptions) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -Compress -InputObject @($arguments)
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); MatlabRoot = $MatlabRoot;
        TimeoutSeconds = 120; OutFile = $transcript; WorkingDirectory = (Split-Path $exe) } + $quietOptions
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $lines = @("== $header")
    foreach ($l in ($out -split "`r?`n")) { if ($l.Length -gt 0 -and -not $l.StartsWith('SECONDS')) { $lines += "  $l" } }
    $lines
}

$flags = '/nologo /O2 /MD /EHsc /std:c++17 /W3 /Zc:__cplusplus /D_CRT_SECURE_NO_DEPRECATE'
$engines = @(
    @{ Name = 'R2025b'; Folder = (Join-Path $WorkRoot 'mw'); Recordings = 'recordings'; Quiet = @{};
       Include = "/DMSVC /DIBMPC /I`"$MatlabRoot\extern\include`"";
       Link = "/LIBPATH:`"$MatlabRoot\extern\lib\win64\microsoft`" libMatlabDataArray.lib" },
    @{ Name = 'JGraph'; Folder = (Join-Path $WorkRoot 'jg'); Recordings = 'recordings-jgraph'; Quiet = @{ NoMatlabRuntime = $true };
       Include = "/I`"$repoRoot\native\jgrt\include`"";
       Link = "`"$repoRoot\native\win-x64\jgrt\jgrt.lib`"" }
)
foreach ($engine in $engines) {
    $folder = $engine.Folder
    if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory -Force $folder | Out-Null
    $commands = @("cd /d `"$folder`"")
    foreach ($probe in 'kind', 'eq') {
        $commands += "cl $flags $($engine.Include) `"$here\src\k12edge\$probe.cpp`" /Fe:$probe.exe /link $($engine.Link)"
    }
    Invoke-Msvc $commands (Join-Path $WorkRoot "$($engine.Name)_build.txt")
    if ($engine.Name -eq 'JGraph') {
        Copy-Item -LiteralPath (Join-Path $repoRoot 'native\win-x64\jgrt\jgrt.dll') -Destination $folder -Force
    }

    $lines = Run-Probe (Join-Path $folder 'kind.exe') @() 'kind' $engine.Quiet
    foreach ($case in $cases) {
        $lines += Run-Probe (Join-Path $folder 'eq.exe') @($case) "eq $case" $engine.Quiet
    }

    $target = Join-Path (Join-Path $here $engine.Recordings) 'k12_edge.out.txt'
    [System.IO.File]::WriteAllText($target, (($lines -join "`n") + "`n"), $utf8)
    Write-Host ("{0,-8} {1} lines -> {2}" -f $engine.Name, $lines.Count, $target)
}
