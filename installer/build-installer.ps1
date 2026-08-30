# Builds the JGraph MSI: publishes both executables into one staging folder (the layout
# GuiLauncher calls "deployed"), then compiles the WiX package against it.
#
#   .\build-installer.ps1                # version from Directory.Build.props
#   .\build-installer.ps1 -Version 0.2.0 # explicit override
#
# The WiX project is deliberately not in JGraph.sln: it needs this staging folder to exist,
# so it must never run on an ordinary solution build.

param(
    [string]$Version,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $PSScriptRoot "staging"

if (-not $Version) {
    # Directory.Build.props is the single source of truth for the product version.
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -notmatch "<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>") {
        throw "Could not read <Version> from Directory.Build.props."
    }
    $Version = $Matches[1]
}
if ($Version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
    throw "MSI ProductVersion must be numeric major.minor.patch; got '$Version'."
}

Write-Host "== JGraph installer $Version ($Configuration) ==" -ForegroundColor Cyan

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}

# Both publishes land in ONE folder: jgraph.exe finds JGraph.Application.exe beside itself.
# Overlapping assemblies are the same net8.0 builds from the same tree, so order is irrelevant.
foreach ($project in "src\JGraph.Application", "src\JGraph.Cli") {
    Write-Host "-- publish $project" -ForegroundColor Cyan
    dotnet publish (Join-Path $root $project) -c $Configuration -r win-x64 --self-contained false -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project." }
}

# Anchors that must be present for the installed product to work; missing means the publish
# layout changed and the installer would silently ship a broken product.
$anchors = @(
    "jgraph.exe",
    "JGraph.Application.exe",
    "python\jgraph_console.py",
    "docs\jgs-scripting-guide.html",
    "examples\example.jgs",
    # The splash animation. It is carried by a Content item with a TargetPath rather than by the
    # publish's own folder rules, so it is exactly the kind of file a layout change drops silently -
    # and the product would still start, just wearing the fallback panel instead of its own face.
    "splash.apng"
)
foreach ($anchor in $anchors) {
    if (-not (Test-Path (Join-Path $staging $anchor))) {
        throw "Staging is missing '$anchor' - the publish layout changed; fix before shipping."
    }
}

Write-Host "-- build MSI" -ForegroundColor Cyan
$wixproj = Join-Path $PSScriptRoot "JGraph.Installer\JGraph.Installer.wixproj"
dotnet build $wixproj -c $Configuration -p:ProductVersion=$Version -p:StagingDir=$staging --nologo
if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

$msi = Join-Path $PSScriptRoot "JGraph.Installer\bin\$Configuration\JGraph-$Version.msi"
if (-not (Test-Path $msi)) { throw "Expected MSI not found at $msi." }
Write-Host "== Done: $msi ($([Math]::Round((Get-Item $msi).Length / 1MB, 1)) MB) ==" -ForegroundColor Green
