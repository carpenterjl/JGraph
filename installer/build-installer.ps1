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
# The Production Server (stage K14) lands beside them: framework-dependent on ASP.NET Core's shared
# framework, which the two programs do not need.
foreach ($project in "src\JGraph.Application", "src\JGraph.Cli", "src\JGraph.ProductionServer") {
    Write-Host "-- publish $project" -ForegroundColor Cyan
    dotnet publish (Join-Path $root $project) -c $Configuration -r win-x64 --self-contained false -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project." }
}

# The WiX 6 command line and its extensions (compiler and SDK plan, stage K18, decision KO7): what
# compiler.package.installer and compiler.runtime.customInstaller build installers with, shipped under
# toolbox\compiler\wix where JGraph.Deploy.WixTool looks first, from the NuGet package cache that
# restoring installer\WixTools\WixTools.csproj fills. WiX is MS-RL licensed; its license text goes with it.
Write-Host "-- WiX 6 (installer\WixTools)" -ForegroundColor Cyan
dotnet restore (Join-Path $PSScriptRoot "WixTools\WixTools.csproj") --nologo
if ($LASTEXITCODE -ne 0) { throw "The WiX packages could not be restored." }
$wixVersion = ([xml](Get-Content (Join-Path $PSScriptRoot "WixTools\WixTools.csproj"))).Project.PropertyGroup.WixVersion
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$wixFolder = Join-Path $staging "toolbox\compiler\wix"
New-Item -ItemType Directory -Force (Join-Path $wixFolder "ext") | Out-Null
Copy-Item (Join-Path $packages "wixtoolset.sdk\$wixVersion\tools\net6.0\*") $wixFolder -Recurse -Force
Copy-Item (Join-Path $packages "wixtoolset.sdk\$wixVersion\OSMFEULA.txt") (Join-Path $wixFolder "LICENSE.txt") -Force
foreach ($extension in "WixToolset.UI.wixext", "WixToolset.BootstrapperApplications.wixext") {
    Copy-Item (Join-Path $packages "$($extension.ToLowerInvariant())\$wixVersion\wixext6\$extension.dll") (Join-Path $wixFolder "ext\$extension.dll") -Force
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
    "splash.apng",
    # The bug-report assembly (ADR 0116). Referenced by JGraph.Application, so the publish
    # carries it - this line is here so a removed reference fails the build loudly instead of
    # shipping a product whose Report a Bug button cannot load its types.
    "JGraph.Reporting.dll",
    # The native library host (ADR 0180): loadlibrary and calllib run native code in this child
    # process. JGraph.Scripting references it, which carries the executable and its runtimeconfig
    # into the publish; without them every native call fails on a missing host.
    "JGraph.NativeHost.exe",
    "JGraph.NativeHost.runtimeconfig.json",

    # Bluetooth (ADR 0186): JGraph.Devices loads its Windows backend and the WinRT projection from
    # the application folder at run time; without them bluetooth, ble and their lists fail to load.
    "JGraph.Devices.Bluetooth.dll",
    "Microsoft.Windows.SDK.NET.dll",
    "WinRT.Runtime.dll",

    # mcc from a command prompt and mcc -B's bundles (stage K7, ADR 0263).
    "jgmcc.cmd",
    "toolbox\compiler\bundles\csharedlib",

    # mcc -W dotnet (stage K8, ADR 0264): the MWArray API a client references, and .NET 8's
    # reference assemblies the generated classes compile against where no .NET SDK is installed.
    "JGraph.MATLAB.NET.Arrays.dll",
    "toolbox\dotnetbuilder\ref\net8.0\System.Runtime.dll",

    # mcc -W dotnet:...,api=matlab-data (stage K9, ADR 0265): the MATLAB Data API the
    # strongly typed interface compiles against and a client references.
    "JGraph.MATLAB.Runtime.dll",

    # The native bridge (stage K10, ADR 0266): jgrt.dll beside the engine, where a C program with
    # this folder on its PATH finds it, and the runtimeconfig it starts .NET with.
    "jgrt.dll",
    "jgrt.runtimeconfig.json",

    # mcc -W lib (stage K11, ADR 0267): the headers a C shared library and its clients compile
    # against and the import library they link with, where MATLAB keeps its own.
    "extern\include\mclmcrrt.h",
    "extern\lib\win64\microsoft\jgrt.lib",

    # mcc -W cpplib (stage K12, ADR 0268): the C++ Data API's headers a client compiles against.
    "extern\include\MatlabCppSharedLib.hpp",
    "extern\include\jgraph\cpplib.hpp",

    # mcc -W python (stage K13, ADR 0269): the matlab module and the package runtime beside the
    # engine, where a package finds them with this folder, jgrt.dll's, on the PATH.
    "python\matlab\__init__.py",
    "python\matlab_pysdk\runtime\__init__.py",

    # The Production Server (stage K14, ADR 0270): jgraph-mps new/run/worker, and the runtimeconfig
    # naming the ASP.NET Core shared framework it runs on.
    "jgraph-mps.exe",
    "jgraph-mps.runtimeconfig.json",

    # compiler.package.installer (stage K18, ADR 0273): the WiX command line, the bootstrapper
    # applications extension an installer's bundle is built with, its license, and the default logo
    # the installer's pages show.
    "toolbox\compiler\wix\wix.dll",
    "toolbox\compiler\wix\ext\WixToolset.BootstrapperApplications.wixext.dll",
    "toolbox\compiler\wix\LICENSE.txt",
    "toolbox\compiler\packagingResources\default_logo.png"
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
