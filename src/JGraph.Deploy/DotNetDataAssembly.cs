using System.Text;

namespace JGraph.Deploy;

/// <summary>What a MATLAB Data API build writes, besides its archive.</summary>
public sealed record DotNetDataAssemblyOptions
{
    /// <summary>The folder the build writes into.</summary>
    public required string OutputFolder { get; init; }

    /// <summary><c>-W dotnet:</c>'s parts: the assembly's name is the interface's namespace.</summary>
    public required DotNetWrapper Wrapper { get; init; }

    /// <summary>
    /// The strongly typed interface's files, file name and text (<see cref="TypedInterface.Generate"/>),
    /// or null when none was generated: the archive is then the build's one file.
    /// </summary>
    public IReadOnlyList<(string File, string Text)>? Interface { get; init; }

    /// <summary>The assembly's version.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary><c>-g</c>: a debug build with symbols.</summary>
    public bool Debug { get; init; }

    /// <summary>JGraph.MATLAB.Runtime.dll, which the interface is compiled against; beside JGraph when not given.</summary>
    public string? RuntimeAssembly { get; init; }
}

/// <summary>A MATLAB Data API build's files, and the note to print when it made the archive alone.</summary>
/// <param name="Files">The files in the order <c>compiler.build.Results.Files</c> lists them.</param>
/// <param name="Manifest">The archive's manifest.</param>
/// <param name="Note">R2025b's note when the interface's assembly could not be built, or null.</param>
public sealed record DotNetDataBuild(IReadOnlyList<string> Files, CtfManifest Manifest, string? Note);

/// <summary>
/// A deployable archive for the MATLAB Data API for .NET (compiler and SDK plan, stage K9, ADR 0265),
/// as R2025b's mcc <c>-W dotnet:&lt;name&gt;,api=matlab-data</c> builds one (<c>record-k9.ps1</c>,
/// <c>recordings/k9_out</c>): <c>&lt;name&gt;.ctf</c> beside the assembly whatever <c>-C</c> says, the
/// strongly typed interface in <c>strongly_typed_interface\*.cs</c>, a small <c>&lt;name&gt;.dll</c>
/// compiled from it (with Roslyn, where R2025b runs the .NET SDK) against JGraph.MATLAB.Runtime.dll,
/// the <c>.csproj</c> and <c>.deps.json</c> R2025b leaves beside it in their .NET 8 form, and the readme
/// in JGraph's own words. A client calls the archive through <c>JGraph.MATLAB.Runtime.MATLABRuntime</c>.
/// </summary>
public static class DotNetDataAssembly
{
    /// <summary>The assembly a client and the interface use, where R2025b's are MathWorks.MATLAB.Runtime.dll and .Types.dll.</summary>
    public const string RuntimeAssemblyName = "JGraph.MATLAB.Runtime";

    /// <summary>The folder the interface's files go in.</summary>
    public const string InterfaceFolder = "strongly_typed_interface";

    /// <summary>R2025b's note when no strongly typed interface could be generated (k9_build: variadic).</summary>
    public const string NotGeneratedNote =
        "Note: A strongly typed interface for the MATLAB code could not be generated, resulting in the creation of only a .ctf file. "
        + "You can still integrate the .ctf file with your application without strongly typed support. If a strongly typed interface is "
        + "not required for your MATLAB code, enable the \"STRONGLY_TYPED_DISABLED\" environment variable by setting its value to \"1\" "
        + "using the setenv function. However, if a strongly typed interface is necessary, please examine and resolve the issues mentioned earlier.";

    /// <summary>R2025b's note when the interface did not compile (k9_build: optmethod).</summary>
    public const string NotCompiledNote =
        "Note: A .NET assembly for strongly typed interface to MATLAB code could not be generated. If a strongly typed interface is not "
        + "required for your MATLAB code, enable the \"STRONGLY_TYPED_DISABLED\" environment variable by setting its value to \"1\" using the setenv function.";

    /// <summary>Whether <c>STRONGLY_TYPED_DISABLED</c> is 1, so a build makes the archive alone without a note.</summary>
    public static bool StronglyTypedDisabled => Environment.GetEnvironmentVariable("STRONGLY_TYPED_DISABLED") == "1";

    /// <summary>JGraph.MATLAB.Runtime.dll beside JGraph, or null when it is not there.</summary>
    public static string? RuntimeAssemblyBesideJGraph()
    {
        foreach (string? folder in new[] { Path.GetDirectoryName(typeof(DotNetDataAssembly).Assembly.Location), AppContext.BaseDirectory })
        {
            if (folder is { Length: > 0 } && Path.Combine(folder, RuntimeAssemblyName + ".dll") is var candidate && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the archive and, when there is an interface, its files and assembly; answers the files
    /// in the order R2025b's <c>Results.Files</c> lists them (k9_build: main.Files: the archive, the
    /// interface's folder, the assembly), the manifest, and a note when the assembly was not made.
    /// </summary>
    /// <exception cref="FileNotFoundException">JGraph.MATLAB.Runtime.dll is missing.</exception>
    public static DotNetDataBuild Write(CtfBuild build, DotNetDataAssemblyOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string name = options.Wrapper.ShortName;
        string ctf = Path.Combine(folder, name + ".ctf");
        var utf8 = new UTF8Encoding(false);

        CtfManifest manifest;
        using (FileStream stream = File.Create(ctf))
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.DotNetAssembly });
        }

        File.WriteAllText(Path.Combine(folder, "readme.txt"), Crlf(Readme(name, options.Interface is not null)), utf8);
        if (options.Interface is not { } sources)
        {
            return new DotNetDataBuild([ctf], manifest, null);
        }

        string runtime = options.RuntimeAssembly ?? RuntimeAssemblyBesideJGraph()
            ?? throw new FileNotFoundException($"{RuntimeAssemblyName}.dll was not found beside JGraph.");
        string interfaceFolder = Path.Combine(folder, InterfaceFolder);
        Directory.CreateDirectory(interfaceFolder);
        foreach ((string file, string text) in sources)
        {
            // R2025b writes the interface with bare line feeds (k9_build: CRLF 0).
            File.WriteAllText(Path.Combine(interfaceFolder, file), text, utf8);
        }

        string version = options.Version.ToString();
        File.WriteAllText(Path.Combine(folder, name + ".csproj"), Crlf(Project(runtime, version, options.Debug)), utf8);

        string assembly = Path.Combine(folder, name + ".dll");
        try
        {
            DotNetAssembly.Compile(name, [.. sources.Select(s => (Path.Combine(InterfaceFolder, s.File), s.Text))], version, [runtime], assembly,
                options.Debug, options.Wrapper.KeyFile, []);
        }
        catch (MccException)
        {
            // R2025b keeps the archive, the interface and the project, and says so (k9_build: optmethod).
            return new DotNetDataBuild([ctf, interfaceFolder], manifest, NotCompiledNote);
        }
        finally
        {
            File.Delete(Path.ChangeExtension(assembly, ".xml"));
        }

        File.WriteAllText(Path.Combine(folder, name + ".deps.json"), Crlf(Dependencies(name, version)).TrimEnd('\r', '\n'), utf8);
        return new DotNetDataBuild([ctf, interfaceFolder, assembly], manifest, null);
    }

    /// <summary>R2025b's <c>.csproj</c> for the interface's assembly, for .NET 8 and naming JGraph's runtime assembly.</summary>
    private static string Project(string runtime, string version, bool debug) =>
        $$"""
<Project Sdk = "Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net8.0</TargetFramework>
		<PlatformTarget>AnyCPU</PlatformTarget>
		<OutputType>library</OutputType>
		<DebugType>{{(debug ? "portable" : "None")}}</DebugType>
		<Optimize>{{(debug ? "false" : "true")}}</Optimize>
		<ProduceReferenceAssembly>false</ProduceReferenceAssembly>
		<CopyReferenceAssemblies>false</CopyReferenceAssemblies>
		<DefineConstants>$(DefineConstants); __IN_MCC</DefineConstants>
		<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
		<Version>{{version}}</Version>
	</PropertyGroup>
	<ItemGroup>
		<Compile Include = "{{InterfaceFolder}}\*.cs" />
		<Reference Include = "{{runtime}}">
			<Private>False</Private>
		</Reference>
	</ItemGroup>
</Project>

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The <c>.deps.json</c> the .NET SDK writes beside the assembly, for .NET 8; R2025b's ends without a newline.</summary>
    private static string Dependencies(string name, string version) =>
        $$"""
{
  "runtimeTarget": {
    "name": ".NETCoreApp,Version=v8.0",
    "signature": ""
  },
  "compilationOptions": {},
  "targets": {
    ".NETCoreApp,Version=v8.0": {
      "{{name}}/{{version}}": {
        "runtime": {
          "{{name}}.dll": {}
        }
      }
    }
  },
  "libraries": {
    "{{name}}/{{version}}": {
      "type": "project",
      "serviceable": false,
      "sha512": ""
    }
  }
}
""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>readme.txt: JGraph's own text under R2025b's title line and three numbered sections (k9_build: main.readme).</summary>
    public static string Readme(string name, bool typed)
    {
        var s = new StringBuilder();
        s.Append(name).Append(" MATLAB Archive (.NET Component)\n\n");
        s.Append("1. Prerequisites for Deployment \n\n");
        s.Append("The JGraph Runtime ").Append(RuntimeLayout.CurrentVersion).Append(" must be installed on the computer the archive runs on,\n")
            .Append("or its folder named by the JGRAPH_RUNTIME_ROOT environment variable. Clients run on .NET 8 or later.\n\n");
        s.Append("2. Files to Deploy and Package\n\n");
        s.Append("-").Append(name).Append(".ctf\n   -the deployable archive a client opens with MATLABRuntime.StartMATLAB. \n");
        if (typed)
        {
            s.Append("-").Append(InterfaceFolder).Append(" folder\n   -the C# files of the strongly typed interface, which ").Append(name)
                .Append(".dll is compiled from. \n");
            s.Append("-").Append(name).Append(".dll\n   -the strongly typed interface, for a client to reference. \n");
        }

        s.Append("-").Append(RuntimeAssemblyName).Append(".dll\n   -the MATLAB Data API, from the JGraph Runtime's extern\\dotnet\\net8.0 folder.\n");
        s.Append("-This readme file\n\n");
        s.Append("3. Definitions\n\n");
        s.Append("The JGraph Runtime is the engine a built component runs on, installed once per JGraph version.\n");
        s.Append("The MATLAB Data API keeps MathWorks' names under the JGraph.MATLAB namespaces: a client written for\n")
            .Append("MATLAB's runtime changes its using lines from MathWorks.MATLAB to JGraph.MATLAB.\n");
        return s.ToString();
    }

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
