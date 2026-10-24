# Builds the two launcher templates (compiler and SDK plan, stage K6) into native\win-x64\launchers:
#
#   jglaunch-console.exe   the console program mcc -m copies
#   jglaunch-gui.exe       the windowed program mcc -e copies (it shows splash.png)
#
# They are checked in, as libopenblas.dll is, so neither building JGraph nor building a program with
# mcc needs a C++ compiler. Rebuild them only when jglaunch.cpp, jglaunch.rc or the manifest changes:
#
#   powershell -File native\jglaunch\build.ps1
#
# Needs MSVC (Visual Studio or its Build Tools, found through vswhere) and the .NET 8 apphost pack
# (Microsoft.NETCore.App.Host.win-x64, installed with the .NET 8 SDK) for nethost's static library
# and the hosting headers. The C runtime is linked statically, so a program needs no Visual C++
# redistributable; /Brepro makes a rebuild of the same sources the same bytes.
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\launchers" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$packs = Join-Path $env:ProgramFiles "dotnet\packs\Microsoft.NETCore.App.Host.win-x64"
$pack = Get-ChildItem $packs -Directory | Where-Object { $_.Name -like "8.0.*" } |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $pack) { throw "The .NET 8 apphost pack was not found under $packs." }
$native = Join-Path $pack.FullName "runtimes\win-x64\native"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan
Write-Host "== nethost: $native" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jglaunch_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $common = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE /DNETHOST_USE_AS_STATIC /I`"$native`""
    $libs = "`"$native\libnethost.lib`" advapi32.lib user32.lib shell32.lib"
    $variants = @(
        @{ Name = "jglaunch-console.exe"; Defines = ""; Subsystem = "CONSOLE"; Libs = "" },
        @{ Name = "jglaunch-gui.exe"; Defines = "/DJGLAUNCH_GUI"; Subsystem = "WINDOWS"; Libs = "gdiplus.lib gdi32.lib ole32.lib" }
    )
    # vcvars64.bat runs vswhere itself and expects it on the PATH.
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "rc /nologo /fo `"$work\jglaunch.res`" jglaunch.rc || exit /b 1")
    foreach ($v in $variants) {
        $obj = Join-Path $work ($v.Name + ".obj")
        $script += "cl $common $($v.Defines) /c jglaunch.cpp /Fo`"$obj`" || exit /b 1"
        # libnethost's objects export get_hostfxr_path; the program needs no import library for it.
        $script += "link /nologo /Brepro /NOIMPLIB /NOEXP /SUBSYSTEM:$($v.Subsystem) /OUT:`"$(Join-Path $Output $v.Name)`" `"$obj`" `"$work\jglaunch.res`" $libs $($v.Libs) || exit /b 1"
    }
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The launcher build failed." }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

foreach ($v in $variants) {
    $file = Get-Item (Join-Path $Output $v.Name)
    Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
}
