# Lays out the JGraph Runtime (compiler and SDK plan, stage K4): the folder a deployed program or a
# client of a deployed component runs on, found through JGRAPH_RUNTIME_ROOT, the registry key the
# runtime's installer will write (stage K18), or an application's own folder.
#
#   <root>\jgraph-runtime.json   the marker: the JGraph version and the release it follows
#   <root>\bin\win64\            the engine, jgraph.exe and JGraph.Runtime.dll, published ReadyToRun
#   <root>\runtime\win64\        the native bridge (stage K10)
#   <root>\include\              the C and C++ headers (stages K11, K12)
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

foreach ($folder in "runtime\win64", "include", "python", "launchers\win64") {
    New-Item -ItemType Directory -Force (Join-Path $Output $folder) | Out-Null
}

$marker = @{ version = $version; release = "25.2" } | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $Output "jgraph-runtime.json"), $marker, (New-Object System.Text.UTF8Encoding($false)))

# What the runtime cannot work without; a missing one means the publish layout changed.
foreach ($anchor in "jgraph.exe", "JGraph.Runtime.dll", "JGraph.Scripting.dll", "JGraph.Deploy.dll", "native\libopenblas.dll") {
    if (-not (Test-Path (Join-Path $bin $anchor))) {
        throw "The runtime is missing 'bin\win64\$anchor' - the publish layout changed; fix before shipping."
    }
}

# WPF must stay out (decision KO5): a headless runtime is what keeps Linux possible.
if (Get-ChildItem $bin -Filter "PresentationFramework*.dll") { throw "WPF reached the runtime's bin." }

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "== Done: $Output ($([Math]::Round($size / 1MB, 1)) MB) ==" -ForegroundColor Green
