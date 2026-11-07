# Builds the Excel add-in template (compiler and SDK plan, stage K17c) into native\win-x64\jgxl:
#
#   jgxl.dll   the DLL mcc -W excel copies to <addin>_<major>_<minor>.dll and stamps with the add-in's
#              type library, archive and version; it registers itself from the type library and hands
#              its objects to the JGraph Runtime's native bridge, jgrt.dll (native\jgrt\src\com.cpp)
#
# It is checked in, as the launcher templates and jgrt.dll are, so neither building JGraph nor
# building an add-in with mcc needs a C++ compiler. Rebuild it only when a file under native\jgxl
# changes, and commit the output with SOURCE.md:
#
#   powershell -File native\jgxl\build.ps1
#
# Needs MSVC (Visual Studio or its Build Tools, found through vswhere). The C runtime is linked
# statically, so an add-in needs no Visual C++ redistributable; /Brepro makes a rebuild of the same
# sources the same bytes.
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgxl" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgxl_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE"
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "rc /nologo /fo `"$work\jgxl.res`" jgxl.rc || exit /b 1",
        "cl $flags /c jgxl.cpp /Fo`"$work\jgxl.obj`" || exit /b 1",
        "link /nologo /Brepro /DLL /NOIMPLIB /NOEXP /DEF:jgxl.def /OUT:`"$Output\jgxl.dll`" `"$work\jgxl.obj`" `"$work\jgxl.res`" advapi32.lib ole32.lib oleaut32.lib || exit /b 1",
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgxl build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$file = Get-Item (Join-Path $Output "jgxl.dll")
$hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("# jgxl.dll", "",
    "Built by ``native\jgxl\build.ps1`` from ``native\jgxl`` (compiler and SDK plan, stage K17c): the Excel add-in template mcc -W excel copies and stamps.", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro``",
    "- ``jgxl.dll``: $($file.Length) bytes, SHA-256 ``$hash``")
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
