# Builds JGraph's COM utility library for Excel add-ins (compiler and SDK plan, stage K17d; decision
# KO10) into native\win-x64\jgcomutil:
#
#   jgcomutil.dll   the in-process server of JGraphComUtil.MWUtil, MWFlags, MWArrayFormatFlags,
#                   MWDataConversionFlags, MWStruct, MWField, MWComplex, MWSparse and MWArg, the
#                   counterpart of MATLAB Runtime's mwcomutil.dll: it registers itself from its type
#                   library (jgcomutil.idl, compiled by midl into its TYPELIB resource) and hands its
#                   objects to the JGraph Runtime's native bridge, jgrt.dll, beside it
#
# It is the add-in template's source (native\jgxl\jgxl.cpp) built with JGRAPH_COMUTIL defined: the
# same finder of jgrt.dll, the same registration from the type library, the ProgIDs in MathWorks'
# form for a utility library (<library>.<class> and <library>.<class>25.2). It is checked in, as the
# template and jgrt.dll are. Rebuild it only when a file under native\jgcomutil or native\jgxl
# changes, and commit the output with SOURCE.md:
#
#   powershell -File native\jgcomutil\build.ps1
#
# Needs MSVC and the Windows SDK (midl.exe and rc.exe, found through vcvars64.bat). The C runtime is
# linked statically; /Brepro makes a rebuild of the same sources the same bytes.
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
$template = Join-Path $root "native\jgxl"
if (-not $Output) { $Output = Join-Path $root "native\win-x64\jgcomutil" }
New-Item -ItemType Directory -Force $Output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC was not found (vswhere found no installation with the x64 C++ tools)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "== MSVC: $vs" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("jgcomutil_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $flags = "/nologo /O2 /MT /EHsc /W4 /WX /permissive- /std:c++17 /Brepro /DUNICODE /D_UNICODE /DJGRAPH_COMUTIL"
    $script = @("@echo off", "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`"", "call `"$vcvars`" >nul", "cd /d `"$here`"",
        "midl /nologo /W1 /win64 /tlb `"$work\jgcomutil.tlb`" /h `"$work\jgcomutil.h`" /iid `"$work\jgcomutil_i.c`" /proxy `"$work\jgcomutil_p.c`" /dlldata `"$work\dlldata.c`" jgcomutil.idl || exit /b 1",
        "rc /nologo /I `"$work`" /fo `"$work\jgcomutil.res`" jgcomutil.rc || exit /b 1",
        "cl $flags /c `"$template\jgxl.cpp`" /Fo`"$work\jgcomutil.obj`" || exit /b 1",
        "link /nologo /Brepro /DLL /NOIMPLIB /NOEXP /DEF:jgcomutil.def /OUT:`"$Output\jgcomutil.dll`" `"$work\jgcomutil.obj`" `"$work\jgcomutil.res`" advapi32.lib ole32.lib oleaut32.lib || exit /b 1",
        "cl 2>&1 | findstr /C:`"Compiler Version`" > `"$work\version.txt`"",
        "midl /confirm 2>&1 | findstr /C:`"Compiler Version`" > `"$work\midl.txt`"",
        "exit /b 0")
    $cmd = Join-Path $work "build.cmd"
    [System.IO.File]::WriteAllLines($cmd, $script)
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "The jgcomutil build failed." }
    $compiler = (Get-Content (Join-Path $work "version.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
    $midl = (Get-Content (Join-Path $work "midl.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$file = Get-Item (Join-Path $Output "jgcomutil.dll")
$hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("# jgcomutil.dll", "",
    "Built by ``native\jgcomutil\build.ps1`` from ``native\jgcomutil\jgcomutil.idl`` and ``native\jgxl\jgxl.cpp`` with ``JGRAPH_COMUTIL`` (compiler and SDK plan, stage K17d): JGraph's COM utility library for Excel add-ins, the counterpart of MATLAB Runtime's mwcomutil.dll.", "",
    "- Compiler: $(if ($compiler) { $compiler.Trim() } else { 'MSVC' }) (x64), ``/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro /DJGRAPH_COMUTIL``",
    "- Type library: $(if ($midl) { $midl.Trim() } else { 'midl' }), ``/win64``",
    "- ``jgcomutil.dll``: $($file.Length) bytes, SHA-256 ``$hash``")
[System.IO.File]::WriteAllLines((Join-Path $Output "SOURCE.md"), $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("== {0}: {1:N0} bytes" -f $file.Name, $file.Length) -ForegroundColor Green
