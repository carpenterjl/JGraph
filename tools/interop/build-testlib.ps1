<#
.SYNOPSIS
    Builds tests/Interop/native/jgtestlib.dll with MSVC and writes its preprocessed header and
    SOURCE.md (interop plan, step 0).

.DESCRIPTION
    Finds a Visual Studio instance that has the x64 C++ tools (vswhere, any version), enters its
    vcvars64 environment, and runs

        cl /nologo /LD /O2 /MT /W4 /DJGTESTLIB_BUILD jgtestlib.c /Fe:jgtestlib.dll

    /MT links the C runtime statically, so the DLL imports nothing but KERNEL32 and runs on a
    machine with no Visual C++ redistributable. The DLL goes to tests/Interop/native/win-x64/,
    where it is committed with a SOURCE.md that records the compiler version, the command line
    and the SHA-256, following the OpenBLAS precedent in native/win-x64/. The test lanes then
    need no compiler to call it.

    It also writes jgtestlib.msvc.i, the header as a user of the library sees it after the
    MSVC preprocessor (cl /P /TC, no JGTESTLIB_BUILD). The header-parser unit tests read that file
    so they run without a compiler.

    Re-run it whenever jgtestlib.c or a header changes, and commit all three outputs together.

.PARAMETER Instance
    Optional vswhere version range, for example "[17.0,18.0)" to build with VS 2022 only.
    The default takes the newest instance that has the C++ tools.

.EXAMPLE
    powershell -File tools/interop/build-testlib.ps1
#>
[CmdletBinding()]
param(
    [string] $Instance
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $repo 'tests\Interop\native'
$out = Join-Path $src 'win-x64'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { Write-Error "vswhere not found at '$vswhere': install Visual Studio Build Tools with the C++ workload." }
$query = @('-products', '*', '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-latest', '-property', 'installationPath')
if ($Instance) { $query = @('-version', $Instance) + $query }
$vs = & $vswhere @query | Select-Object -First 1
if (-not $vs) { Write-Error 'no Visual Studio instance with the x64 C++ tools was found.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) { Write-Error "vcvars64.bat not found under '$vs'." }

New-Item -ItemType Directory -Force -Path $out | Out-Null
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('jgtestlib-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$buildArgs = '/nologo /LD /O2 /MT /W4 /DJGTESTLIB_BUILD'
$pre = Join-Path $src 'jgtestlib.msvc.i'
$cmd = @(
    "call `"$vcvars`" >nul",
    "cd /d `"$work`"",
    "cl $buildArgs `"$src\jgtestlib.c`" /Fe:jgtestlib.dll /link /NOLOGO",
    "cl /nologo /P /TC /Fi:`"$pre`" `"$src\jgtestlib.h`"",
    'cl 2>&1 | findstr /C:"Version"'
) -join ' && '

try {
    # vcvars64 calls vswhere by name, so its folder goes on PATH. stderr is merged inside cmd:
    # in Windows PowerShell 5.1 a native stderr line under ErrorActionPreference=Stop is fatal.
    $env:Path = (Split-Path -Parent $vswhere) + ';' + $env:Path
    $log = cmd /c "($cmd) 2>&1"
    $log | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Write-Error "the build failed (exit $LASTEXITCODE)." }
    Copy-Item -LiteralPath (Join-Path $work 'jgtestlib.dll') -Destination $out -Force
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$compiler = ($log | Where-Object { $_ -match 'Optimizing Compiler Version' } | Select-Object -Last 1)
$vsName = & $vswhere -path $vs -property displayName
$vsVersion = & $vswhere -path $vs -property installationVersion
$dll = Join-Path $out 'jgtestlib.dll'
$hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$srcHash = (Get-FileHash -LiteralPath (Join-Path $src 'jgtestlib.c') -Algorithm SHA256).Hash
$hHash = (Get-FileHash -LiteralPath (Join-Path $src 'jgtestlib.h') -Algorithm SHA256).Hash
$date = Get-Date -Format 'yyyy-MM-dd'

$md = @"
# jgtestlib.dll: provenance

Built from ``tests/Interop/native/jgtestlib.c`` by ``tools/interop/build-testlib.ps1``. Do not edit
this file by hand; re-run the script, which rewrites it.

| | |
| --- | --- |
| Built | $date |
| Visual Studio | $vsName $vsVersion |
| Compiler | $compiler |
| Command | ``cl $buildArgs jgtestlib.c /Fe:jgtestlib.dll`` |
| jgtestlib.c SHA256 | ``$srcHash`` |
| jgtestlib.h SHA256 | ``$hHash`` |
| DLL SHA256 | ``$hash`` |

``/MT`` links the C runtime statically, so the DLL needs no Visual C++ redistributable.
``jgtestlib.msvc.i`` next to the sources is the same compiler's ``cl /P /TC jgtestlib.h``.
"@
[System.IO.File]::WriteAllText((Join-Path $out 'SOURCE.md'), ($md -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "jgtestlib.dll $hash"
