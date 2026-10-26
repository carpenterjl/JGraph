# Lays out the JGraph Runtime (compiler and SDK plan, stage K4): the folder a deployed program or a
# client of a deployed component runs on, found through JGRAPH_RUNTIME_ROOT, the registry key the
# runtime's installer will write (stage K18), or an application's own folder.
#
#   <root>\jgraph-runtime.json   the marker: the JGraph version and the release it follows
#   <root>\bin\win64\            the engine, jgraph.exe and JGraph.Runtime.dll, published ReadyToRun, the
#                                window host, JGraph.Runtime.Desktop.dll (stage K5), and jglaunch.dll, what
#                                a standalone program's launcher starts (stage K6)
#   <root>\runtime\win64\        the native bridge, jgrt.dll, which a C program finds on its PATH (stage K10)
#   <root>\include\              the C headers (stage K10; mclmcrrt.h and what it includes) and the C++ ones (K12)
#   <root>\lib\win64\            jgrt.lib, what a C program links with (stage K10)
#   <root>\python\               the matlab module (stage K13)
#   <root>\launchers\win64\      the launcher templates mcc -m and -e copy (stage K6)
#
#   .\publish-runtime.ps1                         # into installer\runtime-staging
#   .\publish-runtime.ps1 -Output D:\rt -NoReadyToRun
#
# Nothing here installs anything or writes the registry; the MSI is stage K18's.
param(
    [string]$Output,
    [string]$Configuration = "Release",
    [switch]$NoReadyToRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $PSScriptRoot "runtime-staging" }

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch "<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>") {
    throw "Could not read <Version> from Directory.Build.props."
}
$version = $Matches[1]

Write-Host "== JGraph Runtime $version ($Configuration$(if ($NoReadyToRun) { '' } else { ', ReadyToRun' })) ==" -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

$bin = Join-Path $Output "bin\win64"
$readyToRun = if ($NoReadyToRun) { "false" } else { "true" }

# The headless launcher carries the whole engine and, by reference, JGraph.Runtime; no WPF.
dotnet publish (Join-Path $root "src\JGraph.Cli") -c $Configuration -r win-x64 --self-contained false `
    -p:PublishReadyToRun=$readyToRun -o $bin --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# The window host (stage K5), beside the engine: JGraph.Runtime finds it there the first time a
# component shows a figure, in a process that runs on the Windows desktop framework.
dotnet publish (Join-Path $root "src\JGraph.Runtime.Desktop") -c $Configuration -r win-x64 --self-contained false `
    -p:PublishReadyToRun=$readyToRun -o $bin --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the window host failed." }

# A standalone program's managed entry (stage K6), which its launcher starts through hostfxr: jglaunch.dll,
# on the Windows desktop framework so a program can show its windows. It publishes no apphost.
dotnet publish (Join-Path $root "src\JGraph.Runtime.Launch") -c $Configuration -r win-x64 --self-contained false `
    -p:PublishReadyToRun=$readyToRun -o $bin --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the program entry failed." }

foreach ($folder in "runtime\win64", "include", "lib\win64", "python", "launchers\win64") {
    New-Item -ItemType Directory -Force (Join-Path $Output $folder) | Out-Null
}

# The native bridge (stage K10; native\jgrt, checked in prebuilt): jgrt.dll in runtime\win64, which a
# C program has on its PATH and which finds the engine in bin\win64 from there, its headers and its
# import library. The copy beside the engine in bin\win64 (JGraph.Runtime's own output) stays: an
# application carrying the engine loads it from there.
Copy-Item (Join-Path $bin "jgrt.dll") (Join-Path $Output "runtime\win64\jgrt.dll")
Copy-Item (Join-Path $root "native\jgrt\include\*.h") (Join-Path $Output "include")
# The C++ shared library API (stage K12): the MathWorks-named headers and the jgraph ones they include.
Copy-Item (Join-Path $root "native\jgrt\include\*.hpp") (Join-Path $Output "include")
foreach ($folder in "jgraph", "MatlabDataArray") {
    Copy-Item (Join-Path $root "native\jgrt\include\$folder") (Join-Path $Output "include") -Recurse -Force
}
Copy-Item (Join-Path $root "native\win-x64\jgrt\jgrt.lib") (Join-Path $Output "lib\win64\jgrt.lib")

# A Python package's runtime (stage K13): the matlab module, mlexceptions and matlab_pysdk, which a
# package's __init__.py puts on the module path and which reach the engine through jgrt.dll.
Copy-Item (Join-Path $root "python\jgraph_runtime\*") (Join-Path $Output "python") -Recurse -Force

# The launcher templates mcc -m and -e copy (stage K6; native\jglaunch, checked in prebuilt).
foreach ($template in "jglaunch-console.exe", "jglaunch-gui.exe") {
    Copy-Item (Join-Path $root "native\win-x64\launchers\$template") (Join-Path $Output "launchers\win64\$template")
}

# The MWArray API a .NET client references (stage K8), alone in a folder as R2025b keeps MWArray.dll
# in toolbox\dotnetbuilder\bin\win64: a client's build then copies it and nothing else, and it loads
# the engine from bin\win64 at run time.
$dotnetbuilder = Join-Path $Output "toolbox\dotnetbuilder\bin\win64\net8.0"
New-Item -ItemType Directory -Force $dotnetbuilder | Out-Null
foreach ($file in "JGraph.MATLAB.NET.Arrays.dll", "JGraph.MATLAB.NET.Arrays.xml") {
    Copy-Item (Join-Path $bin $file) (Join-Path $dotnetbuilder $file)
}

# The MATLAB Data API a .NET client references (stage K9), alone in extern\dotnet\net8.0 as R2025b
# keeps MathWorks.MATLAB.Runtime.dll and MathWorks.MATLAB.Types.dll in extern\dotnet\netstandard2.0.
$dataApi = Join-Path $Output "extern\dotnet\net8.0"
New-Item -ItemType Directory -Force $dataApi | Out-Null
foreach ($file in "JGraph.MATLAB.Runtime.dll", "JGraph.MATLAB.Runtime.xml") {
    Copy-Item (Join-Path $bin $file) (Join-Path $dataApi $file)
}

$marker = @{ version = $version; release = "25.2" } | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $Output "jgraph-runtime.json"), $marker, (New-Object System.Text.UTF8Encoding($false)))

# What the runtime cannot work without; a missing one means the publish layout changed.
foreach ($anchor in "jgraph.exe", "JGraph.Runtime.dll", "JGraph.Runtime.Desktop.dll", "JGraph.Controls.dll", "JGraph.Scripting.dll",
    "JGraph.Deploy.dll", "native\libopenblas.dll", "jglaunch.dll", "jglaunch.runtimeconfig.json",
    "launchers\win64\jglaunch-console.exe", "toolbox\compiler\packagingResources\default_splash.png",
    # The MWArray API a .NET client of a component loads from here (stage K8, ADR 0264).
    "JGraph.MATLAB.NET.Arrays.dll",
    # The MATLAB Data API the same client loads from here (stage K9, ADR 0265).
    "JGraph.MATLAB.Runtime.dll",
    # The native bridge beside the engine, and the runtimeconfig it starts .NET with (stage K10, ADR 0266).
    "jgrt.dll", "jgrt.runtimeconfig.json") {
    if (-not (Test-Path (Join-Path $bin $anchor))) {
        throw "The runtime is missing 'bin\win64\$anchor' - the publish layout changed; fix before shipping."
    }
}
foreach ($template in "jglaunch-console.exe", "jglaunch-gui.exe") {
    if (-not (Test-Path (Join-Path $Output "launchers\win64\$template"))) { throw "The runtime is missing 'launchers\win64\$template'." }
}
if (-not (Test-Path (Join-Path $dotnetbuilder "JGraph.MATLAB.NET.Arrays.dll"))) { throw "The runtime is missing the MWArray API for clients." }
if (-not (Test-Path (Join-Path $dataApi "JGraph.MATLAB.Runtime.dll"))) { throw "The runtime is missing the MATLAB Data API for clients." }
foreach ($native in "runtime\win64\jgrt.dll", "include\mclmcrrt.h", "include\matrix.h", "include\MatlabCppSharedLib.hpp",
    "include\jgraph\cpplib.hpp", "include\MatlabDataArray\TypedArray.hpp", "lib\win64\jgrt.lib") {
    if (-not (Test-Path (Join-Path $Output $native))) { throw "The runtime is missing '$native', the native bridge's." }
}
foreach ($module in "python\matlab\__init__.py", "python\mlexceptions.py", "python\matlab_pysdk\runtime\__init__.py",
    "python\matlab_pysdk\runtime\_jgrt.py") {
    if (-not (Test-Path (Join-Path $Output $module))) { throw "The runtime is missing '$module', a Python package's." }
}

# WPF itself must stay out (decision KO5): it comes from the Windows desktop framework of a process
# that has one, and the headless part of the runtime, which keeps Linux possible, never loads it.
if (Get-ChildItem $bin -Filter "PresentationFramework*.dll") { throw "WPF reached the runtime's bin." }

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "== Done: $Output ($([Math]::Round($size / 1MB, 1)) MB) ==" -ForegroundColor Green
