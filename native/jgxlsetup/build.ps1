# Builds the Excel client installer template (compiler and SDK plan, stage K17f) into
# native\win-x64\jgxlsetup:
#
#   jgxlsetup.exe   the program compiler.package.excelClientForProductionServer copies to
#                   <InstallerName>.exe and stamps with the client's files (a stored zip), its
#                   configuration, icon and version; run, it unpacks the files, registers the client
#                   and JGraph's server configuration add-in, writes the server's address and leaves
#                   an entry in Apps & Features (/uninstall undoes it, /quiet takes the defaults)
#
# It is checked in, as the other templates are. Rebuild it only when a file under native\jgxlsetup
# changes, and commit the output with SOURCE.md:
#
#   powershell -File native\jgxlsetup\build.ps1
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgxlsetup" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgxlsetup_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE"
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "rc /nologo /fo `"$work\jgxlsetup.res`" jgxlsetup.rc || exit /b 1",
        "cl $flags /c jgxlsetup.cpp /Fo`"$work\jgxlsetup.obj`" || exit /b 1",
        "link /nologo /Brepro /NOIMPLIB /NOEXP /SUBSYSTEM:WINDOWS /OUT:`"$Output\jgxlsetup.exe`" `"$work\jgxlsetup.obj`" `"$work\jgxlsetup.res`" advapi32.lib ole32.lib user32.lib shell32.lib version.lib || exit /b 1",
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"",
        "exit /b 0")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgxlsetup build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$file = Get-Item (Join-Path $Output "jgxlsetup.exe")
$hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("# jgxlsetup.exe", "",
    "Built by ``native\jgxlsetup\build.ps1`` from ``native\jgxlsetup`` (compiler and SDK plan, stage K17f): the Excel client installer template compiler.package.excelClientForProductionServer copies and stamps.", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro``",
    "- ``jgxlsetup.exe``: $($file.Length) bytes, SHA-256 ``$hash``")
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
