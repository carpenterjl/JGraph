using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>A function an Excel add-in exports: its name, inputs, outputs and help line, read from its file.</summary>
public sealed record ExcelFunction(string Name, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, string H1, string File);

/// <summary>What an Excel add-in is built with besides its archive (compiler and SDK plan, stage K17b).</summary>
public sealed record ExcelAddInOptions
{
    /// <summary>The folder the add-in is written to; made when missing, as R2025b's mcc makes <c>-d</c>'s.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The add-in's name: the type library's, the DLL's stem and the first part of its ProgIDs (<c>-W excel:&lt;name&gt;</c>).</summary>
    public required string AddInName { get; init; }

    /// <summary>The class's name: the coclass and the second part of the ProgIDs (<c>-W excel:&lt;name&gt;,&lt;class&gt;</c>); the add-in's name when not given.</summary>
    public required string ClassName { get; init; }

    /// <summary>The functions exported, in the command line's order.</summary>
    public IReadOnlyList<ExcelFunction> Functions { get; init; } = [];

    /// <summary>The add-in's version (<c>version=</c>), naming the DLL and the versioned ProgID.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary>False for <c>mcc -C</c>: the archive is written beside the DLL as <c>&lt;name&gt;.ctf</c>.</summary>
    public bool EmbedArchive { get; init; } = true;

    /// <summary><c>mcc -g</c>: a debug build.</summary>
    public bool Debug { get; init; }

    /// <summary><c>mcc -b</c>: the Visual Basic module (<c>&lt;name&gt;.bas</c>) is asked for (stage K17e writes it).</summary>
    public bool VisualBasicFile { get; init; }

    /// <summary><c>mcc -T codegen</c> (and, in JGraph, <c>-T compile:lib</c>): the archive and the texts, no DLL.</summary>
    public bool ArchiveOnly { get; init; }

    /// <summary>mcc's words, for the readme.</summary>
    public IReadOnlyList<string> MccArguments { get; init; } = [];

    /// <summary>
    /// The prebuilt add-in template the DLL is copied and stamped from (decision KO9, stage K17c:
    /// <c>jgxl.dll</c>). Null writes no DLL: the archive goes beside as <c>&lt;name&gt;.ctf</c>, and
    /// the build says so.
    /// </summary>
    public string? TemplatePath { get; init; }
}

/// <summary>What <see cref="ExcelAddIn.Write"/> wrote: the files <c>compiler.build.Results</c> lists (mcc's own), the manifest, and whether the DLL is among them.</summary>
public sealed record ExcelAddInBuild(IReadOnlyList<string> Files, CtfManifest Manifest, string DllName, bool DllWritten);

/// <summary>
/// An Excel add-in (compiler and SDK plan, stage K17), as R2025b's mcc <c>-W excel:&lt;name&gt;,&lt;class&gt;</c>
/// builds one (<c>recordings/k17_excel.out.txt</c>): the archive of the exported functions (target
/// type 5), embedded in the add-in's DLL or beside it, the DLL named <c>&lt;name&gt;_&lt;major&gt;_&lt;minor&gt;.dll</c>
/// by R2025b's rule, <c>readme.txt</c> in JGraph's words, and for <c>compiler.build.excelAddIn</c>
/// <c>_install.bat</c> line for line as R2025b's <c>writeInstallBat</c> writes it and
/// <c>GettingStarted.html</c> in JGraph's words. Stage K17b writes everything but the DLL, which
/// stage K17c copies from a prebuilt template and stamps (decision KO9); the <c>.bas</c> comes with
/// stage K17e. No C++ source, IDL, module definition or resource script is generated: JGraph needs
/// no compiler on the building machine.
/// </summary>
public static class ExcelAddIn
{
    /// <summary>The registration batch file <c>compiler.build.excelAddIn</c> writes beside the DLL.</summary>
    public const string InstallBatName = "_install.bat";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// The DLL's version suffix by R2025b's postmcc rule: <c>&lt;major&gt;_&lt;minor&gt;</c>, with a
    /// major of 0 made <c>1_0</c> (<c>0</c> and <c>0.5</c> both give <c>_1_0</c>, <c>3.4.5.6</c> gives <c>_3_4</c>).
    /// </summary>
    public static string VersionSuffix(ProgramVersion version) =>
        version.Major == 0 ? "1_0" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{version.Major}_{version.Minor}");

    /// <summary>The DLL's name: <c>&lt;addIn&gt;_&lt;major&gt;_&lt;minor&gt;.dll</c>.</summary>
    public static string DllName(string addIn, ProgramVersion version) => $"{addIn}_{VersionSuffix(version)}.dll";

    /// <summary>The versioned ProgID's suffix: <c>&lt;major&gt;_&lt;minor&gt;</c> as the DLL's.</summary>
    public static string ProgIdSuffix(ProgramVersion version) => VersionSuffix(version);

    /// <summary>
    /// Whether a name can be an add-in's or a class's: a letter or <c>_</c>, then letters, digits and
    /// <c>_</c>, what a type library, a coclass and a ProgID take. R2025b's options accept more
    /// (<c>1abc</c>, <c>a-b</c>, <c>a b</c>) and its mbuild then fails on the generated source
    /// (k17_excel: mcc_badname, mcc_badclass); JGraph refuses the name before it writes anything.
    /// </summary>
    public static bool IsComName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>
    /// Writes what mcc writes: the archive (in the DLL when <see cref="ExcelAddInOptions.EmbedArchive"/>
    /// and a template is given, else as <c>&lt;name&gt;.ctf</c>) and <c>readme.txt</c>. The files
    /// answered are the DLL and the separate archive, each where written.
    /// </summary>
    public static ExcelAddInBuild Write(CtfBuild build, ExcelAddInOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string name = options.AddInName;
        string dllName = DllName(name, options.Version);
        string dll = Path.Combine(folder, dllName);
        string ctf = Path.Combine(folder, name + ".ctf");

        byte[] archive;
        CtfManifest manifest;
        using (var stream = new MemoryStream())
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.ExcelAddIn });
            archive = stream.ToArray();
        }

        var files = new List<string>();
        bool dllWritten = false;
        if (!options.ArchiveOnly && options.TemplatePath is { } template)
        {
            // Stage K17c: the template copied, stamped with its type library, the version block and,
            // when embedded, the archive as resource 2.
            File.Copy(template, dll, overwrite: true);
            files.Add(dll);
            dllWritten = true;
        }
        else
        {
            File.Delete(dll);
        }

        if (options.EmbedArchive && dllWritten)
        {
            File.Delete(ctf);
        }
        else
        {
            File.WriteAllBytes(ctf, archive);
            files.Add(ctf);
        }

        File.WriteAllText(Path.Combine(folder, "readme.txt"), Readme(options, dllWritten), Utf8);
        return new ExcelAddInBuild(files, manifest, dllName, dllWritten);
    }

    /// <summary>Writes <c>_install.bat</c> into <paramref name="folder"/> for <paramref name="dllName"/>, and answers its path.</summary>
    public static string WriteInstallBat(string folder, string dllName)
    {
        string path = Path.Combine(folder, InstallBatName);
        File.WriteAllText(path, InstallBat(dllName), Utf8);
        return path;
    }

    /// <summary>
    /// <c>_install.bat</c> as R2025b's <c>writeInstallBat.m</c> writes it, line for line (line feeds,
    /// no line end after the last line): MWRegsvr where it is on the path, else regsvr32, with the
    /// per-user fallback asked for. JGraph's add-in registers through its own <c>DllRegisterServer</c>,
    /// which either tool calls.
    /// </summary>
    public static string InstallBat(string dllName)
    {
        ArgumentNullException.ThrowIfNull(dllName);
        var s = new StringBuilder();
        s.Append("@ECHO OFF\n\n");
        s.Append(":START\n");
        s.Append("SETLOCAL\n");
        s.Append("set FILE=").Append(dllName).Append('\n');
        s.Append("ECHO Registering %FILE%...\n");
        s.Append("where MWRegsvr 1>NUL 2>&1\n");
        s.Append("if ERRORLEVEL 1 GOTO USEREGSVR\n\n");
        s.Append("MWRegsvr %FILE%\n");
        s.Append("IF ERRORLEVEL 1 GOTO :USERONLYREG\n");
        s.Append("GOTO :SUCCESS\n\n");
        s.Append(":USERONLYREG\n");
        s.Append("ECHO.\n");
        s.Append("SET YOURINPUT=\n");
        s.Append("Set /P YOURINPUT=Do you want to register the component for YOU only? [Y]es or [N]o:\n");
        s.Append("IF NOT \"%YOURINPUT%\"==\"\" SET YOURINPUT=%YOURINPUT:~0,1%\n");
        s.Append("IF /I \"%YOURINPUT%\" == \"Y\" (\n");
        s.Append("  ECHO Registering the component for you only...\n");
        s.Append("    MWRegsvr /useronly %FILE%\n");
        s.Append("  if ERRORLEVEL 1 GOTO FAILURE\n");
        s.Append("  GOTO SUCCESS\n");
        s.Append(")\n");
        s.Append("ECHO Did not register the component.\n");
        s.Append("GOTO END\n\n");
        s.Append(":USEREGSVR\n");
        s.Append("REM if mwregsvr is not available use regsvr32\n");
        s.Append("ECHO Using regsvr32 to register the component...\n");
        s.Append("regsvr32 %FILE%\n");
        s.Append("GOTO SUCCESS\n\n");
        s.Append(":FAILURE\n");
        s.Append("ECHO Failed to register the component.\n");
        s.Append("GOTO END\n\n");
        s.Append(":SUCCESS\n");
        s.Append("ECHO Please refer to the documentation for any additional setup steps.\n");
        s.Append("GOTO END\n\n");
        s.Append(":END\n");
        s.Append("ENDLOCAL\n");
        s.Append("ECHO.");
        return s.ToString();
    }

    /// <summary>
    /// readme.txt: JGraph's own text under R2025b's title line and three numbered sections
    /// (k17_excel: plain.file.readme.txt), with CR LF line ends as R2025b writes them.
    /// </summary>
    public static string Readme(ExcelAddInOptions options, bool dllWritten = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        string name = options.AddInName;
        string dll = DllName(name, options.Version);
        string version = RuntimeLayout.CurrentVersion;
        var s = new StringBuilder();
        s.Append(name).Append(" JGraph Excel Add-in \n\n");
        s.Append("1. Prerequisites for Deployment \n\n");
        s.Append("Verify that the JGraph Runtime (").Append(version).Append(") is installed on the computer that\n")
            .Append("runs Excel. If not, install it from its installer, or set JGRAPH_RUNTIME_ROOT to the folder\n")
            .Append("of a JGraph Runtime carried along.\n\n")
            .Append("The add-in is a COM server: register ").Append(dll).Append(" once on that computer by running\n")
            .Append("_install.bat beside it (for every user when run as an administrator, else for the current\n")
            .Append("user alone), then open the add-in workbook in Excel or call the component from VBA.\n\n");
        s.Append("2. Files to Deploy and Package\n\n");
        if (!options.EmbedArchive || !dllWritten)
        {
            s.Append('-').Append(name).Append(".ctf (component technology file)\n");
        }

        s.Append('-').Append(dll).Append(" (output from mcc)\n");
        if (options.VisualBasicFile)
        {
            s.Append('-').Append(name).Append(".bas (VBA module file created by mcc)\n");
        }

        s.Append("-The JGraph Runtime installer, when the computer has no JGraph Runtime\n");
        s.Append("-This readme file\n\n");
        s.Append("NOTE: Verify that ").Append(dll).Append(" is registered on the target machine. If registration fails,\n")
            .Append("      run _install.bat from a command prompt and read what it prints.\n\n");
        s.Append("3. Definitions\n\n");
        s.Append("An Excel add-in is a COM DLL whose class exposes the MATLAB functions in its deployable\n")
            .Append("archive (embedded in the DLL, or the .ctf beside it) as methods Excel's VBA calls on the\n")
            .Append("JGraph Runtime; the .bas module declares a worksheet function for each, and the add-in\n")
            .Append("workbook carries that module into Excel.\n");
        return s.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>Writes <c>GettingStarted.html</c> into <paramref name="folder"/> for the add-in <paramref name="name"/>, and answers its path.</summary>
    public static string WriteGettingStarted(string folder, string name)
    {
        string path = Path.Combine(folder, "GettingStarted.html");
        File.WriteAllText(path, GettingStarted(name), Utf8);
        return path;
    }

    /// <summary><c>GettingStarted.html</c>, in JGraph's words over R2025b's sections (k17_excel: plain.file.GettingStarted.html; line feeds).</summary>
    private static string GettingStarted(string name) =>
        $"""
<!DOCTYPE html>
<html>
    <head>
        <meta charset="utf-8">
        <title>Getting Started with the {name} Excel Add-In</title>
    </head>
    <body>
        <h1>Getting Started with the {name} Excel Add-In</h1>
        <p>JGraph's compiler creates Excel add-ins that can be integrated with Excel applications. You can use this guide to set up your environment.</p>
        <h2>Location of Installed Excel Component Files</h2>
        <p><a href=".">View {name} Files</a></p>
        <h2>Prerequisites</h2>
        <p>Install the JGraph Runtime {RuntimeLayout.CurrentVersion} on the computer that runs Excel, or set JGRAPH_RUNTIME_ROOT to the folder of a runtime carried along.</p>
        <p>Complete this step only if you have not installed the JGraph Runtime while installing the add-in.</p>
        <h2>Register the Add-In</h2>
        <p>Run _install.bat beside the add-in's DLL once: as an administrator it registers the component for every user, otherwise for the current user alone.</p>
        <h2>Additional Resources</h2>
        <p>The deployment chapter of the JGraph scripting guide (docs/jgs-scripting-guide.html) says how the add-in's functions are called from worksheet cells and from VBA, and how the add-in is distributed.</p>
    </body>
</html>

""".Replace("\r\n", "\n", StringComparison.Ordinal);
}
