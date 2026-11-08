# Builds JGraph's server configuration add-in for Excel (compiler and SDK plan, stage K17f) into
# native\win-x64\jgxlconfig:
#
#   jgxlconfig.dll   a COM add-in for Excel (IDTExtensibility2 with a ribbon part) whose "Configure
#                    Production Server" button opens the dialog that writes where the Excel clients
#                    for the Production Server find their server; the counterpart of the ServerConfig.dll
#                    R2025b's compiler.package.excelClientForProductionServer carries in every installer
#
# It is checked in, as the other templates are. Rebuild it only when a file under native\jgxlconfig
# changes, and commit the output with SOURCE.md:
#
#   powershell -File native\jgxlconfig\build.ps1
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgxlconfig" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgxlconfig_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE"
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "rc /nologo /fo `"$work\jgxlconfig.res`" jgxlconfig.rc || exit /b 1",
        "cl $flags /c jgxlconfig.cpp /Fo`"$work\jgxlconfig.obj`" || exit /b 1",
        "link /nologo /Brepro /DLL /NOIMPLIB /NOEXP /DEF:jgxlconfig.def /OUT:`"$Output\jgxlconfig.dll`" `"$work\jgxlconfig.obj`" `"$work\jgxlconfig.res`" advapi32.lib ole32.lib oleaut32.lib user32.lib comdlg32.lib shell32.lib || exit /b 1",
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"",
        "exit /b 0")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgxlconfig build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$file = Get-Item (Join-Path $Output "jgxlconfig.dll")
$hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("# jgxlconfig.dll", "",
    "Built by ``native\jgxlconfig\build.ps1`` from ``native\jgxlconfig`` (compiler and SDK plan, stage K17f): JGraph's server configuration add-in for Excel, carried by every Excel client installer.", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro``",
    "- ``jgxlconfig.dll``: $($file.Length) bytes, SHA-256 ``$hash``")
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
