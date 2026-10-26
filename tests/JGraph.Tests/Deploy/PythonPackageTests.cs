using System.Text.RegularExpressions;
using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// A Python package's command line and generated files (compiler and SDK plan, stage K13) against what
/// R2025b's mcc -W python and compiler.build.pythonPackage answered and wrote:
/// <c>recordings/k13_build.out.txt</c> (the command line's forms and refusals, the files each build
/// wrote) and <c>recordings/k13_out</c> (the generated text). The folders, the archive's name and
/// the lines a program or pip depends on are R2025b's: the namespace levels, the package's
/// constants and module functions, setup.py's distribution name, version, packages and package
/// data, pyproject.toml's build system, the readme's title and headings. The rest is in JGraph's
/// words, for the JGraph Runtime (ADR 0269).
/// </summary>
public sealed class PythonPackageTests
{
    private static readonly string Probes = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes");

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    private static string Recorded(string file) => File.ReadAllText(Path.Combine(Probes, "recordings", "k13_out", file));

    /// <summary>A text's lines that match <paramref name="pattern"/>, trimmed at the end.</summary>
    private static string[] Matching(string text, string pattern) =>
    [
        .. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => Regex.IsMatch(l, pattern))
            .Select(static l => l.TrimEnd()),
    ];

    /// <summary>A setup.py's value for <paramref name="field"/>: R2025b's writes a dict entry, JGraph's a keyword argument.</summary>
    private static string SetupValue(string text, string field)
    {
        Match found = Regex.Match(text, $@"(?:'{field}'\s*:\s*|\b{field}=)('[^']*'|\[[^\]]*\]|\{{[^}}]*\}})", RegexOptions.Singleline);
        Assert.True(found.Success, $"setup.py gives no {field}");
        return Regex.Replace(found.Groups[1].Value, @"\s+", "");
    }

    private static MccCommandLine Parse(params string[] args) => MccCommandLine.Parse(args);

    private static string Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args)).Identifier;

    [Fact]
    public void TheCommandLineReadsPythonAsR2025bDoes()
    {
        // probe_k13_build's mcc forms, each answered as R2025b answered it (k13_build: mcc_*).
        MccCommandLine plain = Parse("-W", "python:k13m", "-T", "link:lib", "k9echo.m", "k9multi.m");
        Assert.Equal(MccTarget.PythonPackage, plain.Target);
        Assert.Equal("k13m", plain.ComponentName);
        Assert.Null(plain.IgnoredVersion);
        Assert.Equal(MccTarget.PythonPackage, Parse("-W", "python:k13nt", "k9echo.m").Target);
        Assert.Equal("org.k13m", Parse("-W", "python:org.k13m", "-T", "link:lib", "k9echo.m").ComponentName);

        // A version is warned about and ignored; anything else after the name is ignored.
        Assert.Equal("1.0", Parse("-W", "python:k13vr,version=1.0", "-T", "link:lib", "k9echo.m").IgnoredVersion);
        Assert.Equal("k13cm", Parse("-W", "python:k13cm,extra", "-T", "link:lib", "k9echo.m").ComponentName);

        // -C, -T, -g and -v change nothing a package is made of.
        foreach (string[] args in new[]
        {
            new[] { "-W", "python:k13sp", "-T", "link:lib", "-C", "k9echo.m" },
            ["-W", "python:k13cg", "-T", "codegen", "k9echo.m"],
            ["-W", "python:k13cp", "-T", "compile:lib", "k9echo.m"],
            ["-W", "python:k13ex", "-T", "link:exe", "k9echo.m"],
            ["-W", "python:k13db", "-T", "link:lib", "-g", "k9echo.m"],
            ["-W", "python:k13vb", "-T", "link:lib", "-v", "k9echo.m"],
        })
        {
            MccCommandLine line = Parse(args);
            Assert.Equal(MccTarget.PythonPackage, line.Target);
            Assert.False(line.SeparateArchive, string.Join(' ', args));
            Assert.False(line.DebugBuild, string.Join(' ', args));
            Assert.Empty(line.IgnoredSwitches);
        }

        // A name may be nearly anything: its empty dotted parts are dropped, and a second -W python:
        // nests its name inside the first's.
        Assert.Equal("1bad", Parse("-W", "python:1bad", "-T", "link:lib", "k9echo.m").ComponentName);
        Assert.Equal("a-b", Parse("-W", "python:a-b", "-T", "link:lib", "k9echo.m").ComponentName);
        Assert.Equal("class", Parse("-W", "python:class", "-T", "link:lib", "k9echo.m").ComponentName);
        Assert.Equal("a.b", Parse("-W", "python:a..b", "-T", "link:lib", "k9echo.m").ComponentName);
        Assert.Equal("k13t1.k13t2", Parse("-W", "python:k13t1", "-W", "python:k13t2", "-T", "link:lib", "k9echo.m").ComponentName);

        // -c writes a C library's source in place of the package, and a -W lib: after it builds the library.
        MccCommandLine dashC = Parse("-W", "python:k13cc", "-c", "k9echo.m");
        Assert.Equal(MccTarget.CLibrary, dashC.Target);
        Assert.Equal(CLibraryStage.Codegen, dashC.LibraryStage);
        Assert.Equal("k13cc", dashC.ComponentName);
        MccCommandLine withLib = Parse("-W", "python:k13wl", "-W", "lib:k13wl2", "-T", "link:lib", "k9echo.m");
        Assert.Equal(MccTarget.CLibrary, withLib.Target);
        Assert.Equal("k13wl2", withLib.ComponentName);

        // The refusals: no name, and -o beside -W in either order.
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_COMPONENT_NAME", Refused("-W", "python:", "-T", "link:lib", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_COMPONENT_NAME", Refused("-W", "python", "-T", "link:lib", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_COMPONENT_NAME", Refused("-W", "python:..", "k9echo.m"));
        MccException after = Assert.Throws<MccException>(() => Parse("-W", "python:k13on", "-T", "link:lib", "-o", "k13other", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_MULTIPLE_COMPONENT_NAMES", after.Identifier);
        Assert.Equal("Error: Multiple component names. Processing \"-o\" switch. Component name \"k13on\" already specified via \"-W\".", after.Message.TrimEnd());
        MccException first = Assert.Throws<MccException>(() => Parse("-o", "k13of", "-W", "python:k13of2", "-T", "link:lib", "k9echo.m"));
        Assert.Equal("Error: Multiple component names. Processing \"-W\" switch. Component name \"k13of\" already specified via \"-o\".", first.Message.TrimEnd());
    }

    [Fact]
    public void ADottedNameIsAFolderPerPart()
    {
        Assert.Equal(["org", "k13", "deep"], PythonPackage.Parts("org.k13.deep"));
        Assert.Equal(["a", "b"], PythonPackage.Parts("a..b"));
        Assert.Equal("deep", PythonPackage.ShortName("org.k13.deep"));
        Assert.Equal("k13m", PythonPackage.ShortName("k13m"));
        Assert.Equal(Path.Combine(Path.GetFullPath(@"C:\out"), "org", "k13", "deep"), PythonPackage.PackageFolder(@"C:\out", "org.k13.deep"));
    }

    [Fact]
    public void TheBuildWritesR2025bsFoldersInJGraphsWords()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k13-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var written = new List<string>();
            PythonPackageBuild built = PythonPackage.Write(new CtfBuild { ComponentName = "k13m", Files = [] }, folder, "org.k13m", written.Add);

            // What compiler.build's results list first (k13_build: main.Files), and mcc_dotted's files but its logs.
            Assert.Equal([Path.Combine(folder, "org"), Path.Combine(folder, "setup.py")], built.Files);
            Assert.Equal(
                [@"org\__init__.py", @"org\k13m\__init__.py", @"org\k13m\k13m.ctf", "pyproject.toml", "readme.txt", "setup.py"],
                Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).Order(StringComparer.Ordinal));
            Assert.Equal(
                [Path.Combine(folder, "org", "k13m", "__init__.py"), Path.Combine(folder, "setup.py"), Path.Combine(folder, "pyproject.toml"), Path.Combine(folder, "readme.txt")],
                written);
            Assert.Equal(CtfTargetType.PythonPackage, built.Manifest.TargetType);

            // Every text file with CR LF, as R2025b's.
            foreach (string text in new[] { @"org\__init__.py", @"org\k13m\__init__.py", "pyproject.toml", "readme.txt", "setup.py" })
            {
                string content = File.ReadAllText(Path.Combine(folder, text));
                Assert.DoesNotContain("\n", content.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
            }

            // The page compiler.build adds, with line feeds as R2025b's (k13_build: main.file.GettingStarted.html).
            string page = File.ReadAllText(PythonPackage.WriteGettingStarted(folder, "org.k13m"));
            Assert.DoesNotContain("\r", page, StringComparison.Ordinal);
            Assert.Contains("import org.k13m", page, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheLinesAProgramDependsOnAreR2025bs()
    {
        // The package's constants and module functions (mcc_default's k13m\__init__.py).
        const string Constants = "^ *(SUPPORTED_PYTHON_VERSIONS|RUNTIME_VERSION_W_DOTS|PACKAGE_NAME) = |^def ";
        string init = PythonPackage.PackageInit("k13m", "0.2.3");
        Assert.Equal(Matching(Recorded("mcc_default_k13m___init__.py"), Constants), Matching(init, Constants));
        Assert.Contains("JGRAPH_VERSION = '0.2.3'", init, StringComparison.Ordinal);
        Assert.Contains("\"\"\" Package for executing deployed MATLAB functions \"\"\"", init, StringComparison.Ordinal);

        // R2025b's messages for the interpreter's version (k13_client: version).
        Assert.Contains("'Python versions 3.9, 3.10, 3.11, and 3.12 are supported, but your version of Python is %s'", init, StringComparison.Ordinal);
        Assert.Contains("'Python {0} is not supported.'", init, StringComparison.Ordinal);

        // A namespace level's code (dotted's org\__init__.py).
        const string Code = "^(from|__path__)";
        Assert.Equal(Matching(Recorded("dotted_org___init__.py"), Code), Matching(PythonPackage.NamespaceInit("org.k13.deep"), Code));

        // pip's build system.
        const string Toml = @"^(\[|requires|build-backend)";
        Assert.Equal(Matching(Recorded("dotted_pyproject.toml"), Toml), Matching(PythonPackage.Pyproject("org.k13.deep"), Toml));

        // setup.py's distribution, as a dict entry in R2025b's and a keyword argument in JGraph's.
        foreach ((string file, string name) in new[] { ("dotted_setup.py", "org.k13.deep"), ("mcc_default_setup.py", "k13m"), ("main_setup.py", "k13pkg") })
        {
            string theirs = Recorded(file);
            string ours = PythonPackage.Setup(name);
            foreach (string field in new[] { "name", "version", "description", "packages", "package_data" })
            {
                Assert.Equal(SetupValue(theirs, field), SetupValue(ours, field));
            }
        }

        // The readme's title and headings; R2025b's names the short name's folder and import where JGraph's names the package's.
        const string Headings = @"^(\w+ MATLAB Python Package|[123]\. )";
        Assert.Equal(Matching(Recorded("mcc_default_readme.txt"), Headings), Matching(PythonPackage.Readme("k13m"), Headings));
        Assert.Equal(Matching(Recorded("dotted_readme.txt"), Headings), Matching(PythonPackage.Readme("org.k13.deep"), Headings));
        Assert.Contains("    import org.k13.deep\n", PythonPackage.Readme("org.k13.deep").Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
