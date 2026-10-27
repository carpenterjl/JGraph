# Builds the JGraph Runtime MSI (compiler and SDK plan, stage K18): lays the runtime out with
# publish-runtime.ps1 into installer\runtime-staging, then compiles the WiX package against it.
#
#   .\build-runtime-installer.ps1                    # version from Directory.Build.props
#   .\build-runtime-installer.ps1 -Version 0.2.0     # explicit override
#   .\build-runtime-installer.ps1 -NoPublish         # the staging folder as it is
#
# The MSI is JGraph-Runtime-<version>.msi in JGraph.Runtime.Installer\bin\<Configuration>. It is
# what compiler.runtime.download fetches from a release and what compiler.package.installer embeds
# (RuntimeDelivery 'installer') or has the user's installer download (RuntimeDelivery 'web'). It is
# never run here: a built installer is inspected, not installed (the plan's house rule).
#
# The WiX project is deliberately not in JGraph.sln: it needs the staging folder to exist, so it
# must never run on an ordinary solution build.

param(
    [string]$Version,
    [string]$Configuration = "Release",
    [switch]$NoPublish,
    [switch]$NoReadyToRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $PSScriptRoot "runtime-staging"

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

# Each version of the runtime is a product of its own, so its upgrade code is derived from the
# version: the name-based (version 5, SHA-1) UUID of "JGraph Runtime <version>" in a namespace of
# JGraph's own. JGraph.Deploy's RuntimeInstaller.UpgradeCode derives the same value, which is how
# compiler.package.installer knows a runtime MSI for what it is.
function Get-RuntimeUpgradeCode([string]$version) {
    $namespace = [Guid]::Parse("7C0E6B2A-4F1D-4E3B-9A7E-2D5F8C1B6A90").ToByteArray()
    [Array]::Reverse($namespace, 0, 4)
    [Array]::Reverse($namespace, 4, 2)
    [Array]::Reverse($namespace, 6, 2)
    $name = [System.Text.Encoding]::UTF8.GetBytes("JGraph Runtime $version")
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    $hash = $sha1.ComputeHash($namespace + $name)
    $bytes = $hash[0..15]
    $bytes[6] = ($bytes[6] -band 0x0F) -bor 0x50
    $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
    [Array]::Reverse($bytes, 0, 4)
    [Array]::Reverse($bytes, 4, 2)
    [Array]::Reverse($bytes, 6, 2)
    return [Guid]::new([byte[]]$bytes).ToString().ToUpperInvariant()
}
$upgradeCode = Get-RuntimeUpgradeCode $Version

Write-Host "== JGraph Runtime installer $Version ($Configuration; upgrade code $upgradeCode) ==" -ForegroundColor Cyan

if (-not $NoPublish) {
    $publish = @{ Output = $staging; Configuration = $Configuration }
    if ($NoReadyToRun) { $publish.NoReadyToRun = $true }
    & (Join-Path $PSScriptRoot "publish-runtime.ps1") @publish
    if ($LASTEXITCODE -ne 0) { throw "publish-runtime.ps1 failed." }
}
if (-not (Test-Path (Join-Path $staging "jgraph-runtime.json"))) {
    throw "No runtime layout at $staging - run publish-runtime.ps1 first or leave -NoPublish off."
}

Write-Host "-- build MSI" -ForegroundColor Cyan
$wixproj = Join-Path $PSScriptRoot "JGraph.Runtime.Installer\JGraph.Runtime.Installer.wixproj"
dotnet build $wixproj -c $Configuration -p:ProductVersion=$Version -p:StagingDir=$staging -p:UpgradeCode=$upgradeCode --nologo
if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

$msi = Join-Path $PSScriptRoot "JGraph.Runtime.Installer\bin\$Configuration\JGraph-Runtime-$Version.msi"
if (-not (Test-Path $msi)) { throw "Expected MSI not found at $msi." }
Write-Host "== Done: $msi ($([Math]::Round((Get-Item $msi).Length / 1MB, 1)) MB) ==" -ForegroundColor Green
