# Builds the Excel client template for the Production Server (compiler and SDK plan, stage K17f;
# decision KO9) into native\win-x64\jgxlmps:
#
#   jgxlmps.dll   the DLL mcc -W mpsxl copies to <addin>.dll and stamps with the client's type library,
#                 its configuration (the functions' marshalling switches) and version; a COM
#                 automation add-in whose worksheet functions post MATLAB Production Server's
#                 protobuf requests over WinHTTP and convert the replies for Excel's cells
#
# It is checked in, as the add-in template and the launchers are, so neither building JGraph nor
# building a client with mcc needs a C++ compiler. Rebuild it only when a file under native\jgxlmps
# changes, and commit the output with SOURCE.md:
#
#   powershell -File native\jgxlmps\build.ps1
#
# Needs MSVC (Visual Studio or its Build Tools, found through vswhere). The C runtime is linked
# statically, so a client needs no Visual C++ redistributable; /Brepro makes a rebuild of the same
# sources the same bytes.
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgxlmps" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgxlmps_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE"
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "rc /nologo /fo `"$work\jgxlmps.res`" jgxlmps.rc || exit /b 1",
        "cl $flags /c jgxlmps.cpp /Fo`"$work\jgxlmps.obj`" || exit /b 1",
        "link /nologo /Brepro /DLL /NOIMPLIB /NOEXP /DEF:jgxlmps.def /OUT:`"$Output\jgxlmps.dll`" `"$work\jgxlmps.obj`" `"$work\jgxlmps.res`" advapi32.lib ole32.lib oleaut32.lib winhttp.lib crypt32.lib shell32.lib uuid.lib || exit /b 1",
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"",
        "exit /b 0")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgxlmps build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$file = Get-Item (Join-Path $Output "jgxlmps.dll")
$hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("# jgxlmps.dll", "",
    "Built by ``native\jgxlmps\build.ps1`` from ``native\jgxlmps`` (compiler and SDK plan, stage K17f): the Excel client template mcc -W mpsxl copies and stamps, a native automation add-in that calls a Production Server over HTTP.", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro``",
    "- ``jgxlmps.dll``: $($file.Length) bytes, SHA-256 ``$hash``")
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
