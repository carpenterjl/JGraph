# Compiles k16c.c, the C client of a Production Server's protobuf interface (compiler and SDK plan,
# stage K16), with MSVC against MathWorks' C client library (R2025b's
# toolbox\compiler_sdk\mps_clients\c: include\mps and win64\lib\mpsclient.lib), and copies the DLLs
# the library needs beside the program.
#
#   powershell -File build-k16c.ps1 -Out <folder>\k16c.exe -MpsClients <toolbox\compiler_sdk\mps_clients>
param(
    [Parameter(Mandatory = $true)] [string] $Out,
    [Parameter(Mandatory = $true)] [string] $MpsClients
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$folder = Split-Path $Out
New-Item -ItemType Directory -Force $folder | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC was not found (vswhere found no installation with the x64 C++ tools).' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$include = Join-Path $MpsClients 'c\include'
$lib = Join-Path $MpsClients 'c\win64\lib'
$script = @('@echo off', "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$folder`"",
    "cl /nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /I`"$include`" `"$here\k16c.c`" /Fo`"$folder\k16c.obj`" /Fe`"$Out`" /link `"$lib\mpsclient.lib`" || exit /b 1")
$cmd = Join-Path $folder 'build.cmd'
[System.IO.File]::WriteAllLines($cmd, $script)
& cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) { throw 'The k16c build failed.' }
Get-ChildItem (Join-Path $lib '*.dll') | Copy-Item -Destination $folder -Force
