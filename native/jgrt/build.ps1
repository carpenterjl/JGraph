# Builds jgrt.dll, the JGraph Runtime's native bridge (compiler and SDK plan, stage K10), and its
# import library jgrt.lib into native\win-x64\jgrt, with SOURCE.md recording how:
#
#   jgrt.dll   the C API (mcl*, mx*) a C program, a C shared library built by mcc -W lib, a Python
#              package and the Excel add-in call, and the entry points (jgrtCpp*) the C++ shared
#              library headers call (stage K12); it hosts .NET to run the engine
#   jgrt.lib   what such a program links with
#
# and NativeBridgeTests' C program, tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe.
#
# Both are checked in, as the launcher templates and libopenblas.dll are, so neither building JGraph
# nor publishing the runtime needs a C++ compiler. Rebuild them only when a file under native\jgrt
# changes, and commit the three outputs together:
#
#   powershell -File native\jgrt\build.ps1
#
# Needs MSVC (Visual Studio or its Build Tools, found through vswhere) and the .NET 8 apphost pack
# (Microsoft.NETCore.App.Host.win-x64, installed with the .NET 8 SDK) for nethost's static library
# and the hosting headers. The C runtime is linked statically, so a program needs no Visual C++
# redistributable for it; /Brepro makes a rebuild of the same sources the same bytes.
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgrt" }
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

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgrt_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$client = Join-Path $root "tests\JGraph.Tests\Runtime\native"
$probes = Join-Path $root "tools\matlab-checklist\compiler-probes\src\k10client"
New-Item -ItemType Directory -Force (Join-Path $client "win-x64") | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE /DNETHOST_USE_AS_STATIC " +
        "/I`"$native`" /I`"$here\include`""
    $sources = @("mxarray.cpp", "runtime.cpp", "cpplib.cpp")
    $objects = $sources | ForEach-Object { "`"$work\$([System.IO.Path]::ChangeExtension($_, '.obj'))`"" }
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here\src`"",
        "cl $flags /c $($sources -join ' ') /Fo`"$work\\`" || exit /b 1",
        # libnethost's objects export get_hostfxr_path; /NOEXP keeps that one out of jgrt's own exports.
        "link /nologo /Brepro /DLL /OUT:`"$Output\jgrt.dll`" /IMPLIB:`"$Output\jgrt.lib`" $($objects -join ' ') `"$native\libnethost.lib`" advapi32.lib ole32.lib || exit /b 1",
        # NativeBridgeTests' C program, in mbuild's default (separate) complex model, checked in beside its source.
        ("cl /nologo /O2 /MT /W3 /Brepro /D_CRT_SECURE_NO_WARNINGS /DMATLAB_DEFAULT_RELEASE=R2017b /I`"$here\include`" /I`"$probes`" " +
            "`"$client\jgrtclient.c`" /Fo`"$work\jgrtclient.obj`" /Fe`"$client\win-x64\jgrtclient.exe`" /link /Brepro `"$Output\jgrt.lib`" || exit /b 1"),
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgrt build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

# The import library's export file is the linker's by-product; nothing uses it.
Remove-Item -LiteralPath (Join-Path $Output "jgrt.exp") -ErrorAction SilentlyContinue

$lines = @("# jgrt.dll and jgrt.lib", "",
    "Built by ``native\jgrt\build.ps1`` from ``native\jgrt\src`` and ``native\jgrt\include`` (compiler and SDK plan, stages K10 and K12).", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro``",
    "- nethost: ``$($pack.Name)`` apphost pack, linked statically", "")
foreach ($file in (Get-Item (Join-Path $Output "jgrt.dll"), (Join-Path $Output "jgrt.lib"), (Join-Path $client "win-x64\jgrtclient.exe"))) {
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $where = if ($file.Name -eq "jgrtclient.exe") { "``tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe`` (NativeBridgeTests' C program)" } else { "``$($file.Name)``" }
    $lines += "- $($where): $($file.Length) bytes, SHA-256 ``$hash``"
    Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
}
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
