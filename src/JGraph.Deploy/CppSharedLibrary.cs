using System.Text;

namespace JGraph.Deploy;

/// <summary>What a C++ shared library build writes, besides its archive.</summary>
public sealed record CppSharedLibraryOptions
{
    /// <summary>The folder the build writes into.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The library's name: <c>&lt;name&gt;.ctf</c>, <c>&lt;name&gt;v2.hpp</c>.</summary>
    public required string LibraryName { get; init; }

    /// <summary>
    /// The strongly typed header's text (<see cref="TypedInterfaceCpp.Generate"/>), or null when the
    /// build writes none: <c>-W cpplib:name</c> and <c>,all</c> make the generic interface's archive alone,
    /// as R2025b's do (k12_build: mcc_default, mcc_all).
    /// </summary>
    public string? Header { get; init; }

    /// <summary>Whether the build was asked for the legacy mwArray interface too, which JGraph does not build (decision KD7).</summary>
    public bool LegacyAsked { get; init; }
}

/// <summary>A C++ shared library build's files and its archive's manifest.</summary>
/// <param name="Files">What <c>compiler.build.Results.Files</c> lists first: the <c>v2</c> folder.</param>
/// <param name="Manifest">The archive's manifest.</param>
public sealed record CppSharedLibraryBuild(IReadOnlyList<string> Files, CtfManifest Manifest);

/// <summary>
/// A C++ shared library for the MATLAB Data API (compiler and SDK plan, stage K12, ADR 0268), as R2025b's
/// mcc <c>-W cpplib:&lt;name&gt;,generic</c> and <c>compiler.build.cppSharedLibrary</c> build one
/// (<c>record-k12.ps1</c>, <c>recordings\k12_build</c>): no compiled code, only the deployable archive
/// <c>&lt;name&gt;.ctf</c> and the strongly typed header <c>&lt;name&gt;v2.hpp</c> in
/// <c>v2\generic_interface</c> with a readme, and a readme above them. A program includes the header,
/// compiles with the JGraph Runtime's include folder, links with <c>jgrt.lib</c>, and opens the archive
/// with <c>initMATLABLibrary</c>; JGraph's C++ Data API (<c>jgraph/*.hpp</c>) runs it.
/// </summary>
public static class CppSharedLibrary
{
    /// <summary>The generic interface's folder under the output folder.</summary>
    public static string InterfaceFolder(string outputFolder) => Path.Combine(outputFolder, "v2", "generic_interface");

    /// <summary>
    /// Writes the archive, the header when there is one and the two readmes; answers the <c>v2</c>
    /// folder (k12_build: main.Files) and the manifest.
    /// </summary>
    public static CppSharedLibraryBuild Write(CtfBuild build, CppSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        string folder = Path.GetFullPath(options.OutputFolder);
        string generic = InterfaceFolder(folder);
        Directory.CreateDirectory(generic);
        string name = options.LibraryName;
        var utf8 = new UTF8Encoding(false);

        CtfManifest manifest;
        using (FileStream stream = File.Create(Path.Combine(generic, name + ".ctf")))
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.CppSharedLibrary });
        }

        if (options.Header is { } header)
        {
            // R2025b writes the header with line feeds and no last one (k12_build: main.hpp, CRLF 0).
            File.WriteAllText(Path.Combine(generic, name + "v2.hpp"), header, utf8);
        }

        File.WriteAllText(Path.Combine(folder, "readme.txt"), Crlf(Readme(options)), utf8);
        File.WriteAllText(Path.Combine(generic, "readme.txt"), Crlf(GenericReadme(options)), utf8);
        return new CppSharedLibraryBuild([Path.Combine(folder, "v2")], manifest);
    }

    /// <summary>The header's own name for a library: <c>&lt;name&gt;v2.hpp</c>.</summary>
    public static string HeaderName(string libraryName) => libraryName + "v2.hpp";

    /// <summary>readme.txt above the interface: JGraph's own text under R2025b's title line and three numbered sections (k12_build: main.readme).</summary>
    public static string Readme(CppSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string name = options.LibraryName;
        var s = new StringBuilder();
        s.Append(name).Append(" C++ Shared Library\n\n");
        s.Append("1. Prerequisites for Deployment \n\n");
        AppendPrerequisites(s);
        s.Append("2. Files to Deploy and Package\n\n");
        s.Append("A C++ shared library built by JGraph has the generic interface, which uses the MATLAB Data API.\n");
        if (options.LegacyAsked)
        {
            s.Append("The legacy interface, which uses mwArray, was asked for and JGraph does not build it.\n");
        }

        s.Append("Files for the generic interface can be found in the v2\\generic_interface subdirectory.\n\n\n");
        s.Append("Files to Package for the Generic Interface\n(in the v2\\generic_interface subdirectory)\n==========================================\n");
        AppendFiles(s, options, "-readme.txt\n");
        s.Append('\n');
        AppendDefinitions(s, name);
        return s.ToString();
    }

    /// <summary>readme.txt in <c>v2\generic_interface</c> (k12_build: main.generic.readme).</summary>
    public static string GenericReadme(CppSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var s = new StringBuilder("\n1. Prerequisites for Deployment \n\n");
        AppendPrerequisites(s);
        s.Append("2. Files to Deploy and Package\n\n");
        s.Append("Files to Package for the Generic Interface\n==========================================\n");
        AppendFiles(s, options, "-This readme file\n");
        s.Append('\n');
        AppendDefinitions(s, options.LibraryName);
        return s.ToString();
    }

    private static void AppendPrerequisites(StringBuilder s) =>
        s.Append("Verify that the JGraph Runtime (").Append(RuntimeLayout.CurrentVersion).Append(") is installed, with its runtime\\win64 folder\n")
            .Append("on the PATH, where a program finds jgrt.dll, which runs the library's archive.\n")
            .Append("A program compiles with the runtime's include folder, where MatlabCppSharedLib.hpp and\n")
            .Append("the jgraph headers are, and links with the runtime's lib\\win64\\jgrt.lib (on the computer\n")
            .Append("that builds it, JGraph's extern\\include and extern\\lib\\win64\\microsoft hold the same).\n\n");

    private static void AppendFiles(StringBuilder s, CppSharedLibraryOptions options, string readme)
    {
        s.Append('-').Append(options.LibraryName).Append(".ctf (the deployable archive, which initMATLABLibrary opens) \n");
        if (options.Header is not null)
        {
            s.Append('-').Append(HeaderName(options.LibraryName)).Append(" (the strongly typed interface)\n");
        }

        s.Append(readme);
    }

    private static void AppendDefinitions(StringBuilder s, string name) =>
        s.Append("3. Definitions\n\n")
            .Append("The generic interface keeps MathWorks' class and function names in the jgraph namespace, which\n")
            .Append("the runtime's MathWorks-named headers also call matlab, so a program written for MATLAB's runtime\n")
            .Append("compiles against JGraph's as it is. The strongly typed interface calls the functions and classes\n")
            .Append("in ").Append(name).Append(".ctf by their MATLAB names.\n\n\n");

    /// <summary>Writes <c>GettingStarted.html</c> into <paramref name="folder"/> for the library <paramref name="name"/>, and answers its path.</summary>
    public static string WriteGettingStarted(string folder, string name)
    {
        string path = Path.Combine(folder, "GettingStarted.html");
        File.WriteAllText(path, GettingStarted(name), new UTF8Encoding(false));
        return path;
    }

    /// <summary><c>GettingStarted.html</c>, in JGraph's words (R2025b's has line feeds, k12_build: defaults.GettingStarted).</summary>
    private static string GettingStarted(string name) =>
        $"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Getting started with the {name} C++ shared library</title>
</head>
<body>
<h1>Getting started with the {name} C++ shared library</h1>
<ol>
<li>Install the JGraph Runtime {RuntimeLayout.CurrentVersion}, or set JGRAPH_RUNTIME_ROOT to its folder, and put its runtime\win64 folder on the PATH.</li>
<li>Include {name}v2.hpp (in v2\generic_interface) in one source file of the program, compile with the runtime's include folder and C++17, and link with jgrt.lib:
<pre>cl /std:c++17 /EHsc /I "%JGRAPH_RUNTIME_ROOT%\include" /I v2\generic_interface driverApplication.cpp "%JGRAPH_RUNTIME_ROOT%\lib\win64\jgrt.lib"</pre></li>
<li>In the program, start the runtime with <code>matlab::cpplib::initMATLABApplication</code>, open {name}.ctf with <code>initMATLABLibrary</code>,
and call its functions through the header or with <code>feval</code>. A program written for MATLAB's runtime compiles as it is: the runtime's headers name their <code>jgraph</code> namespace <code>matlab</code> too.</li>
<li>Keep {name}.ctf beside the program, in the folder it runs in, or in the folder CPPSHARED_BASE_CTF_PATH names.</li>
</ol>
</body>
</html>

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
