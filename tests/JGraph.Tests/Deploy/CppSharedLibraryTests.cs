using JGraph.Deploy;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// A C++ shared library's strongly typed header and command line (compiler and SDK plan, stage K12)
/// against what R2025b's mcc -W cpplib wrote and answered: <c>recordings/k12_out</c> (the probe's
/// main, variadic and one-function builds over stage K9's functions) and
/// <c>recordings/k12_build.out.txt</c> (the command line's forms and refusals). Every line of the
/// header is R2025b's but its version line and the names JGraph passes before name-value arguments,
/// which R2025b passed by position (ADR 0268).
/// </summary>
public sealed class CppSharedLibraryTests
{
    private static readonly string Probes = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes");

    /// <summary>The probe's main build, in its command line's order (probe_k12_build's mainNames, then the package function).</summary>
    private static readonly string[] MainFiles =
    [
        "k9describe.m", "k9make.m", "k9echo.m", "k9multi.m", "k9nv.m", "k9print.m", "k9noout.m", "k9slow.m", "k9err.m", "k9quiet.m",
        "k9t_real.m", "k9t_cplx.m", "k9t_ints.m", "k9t_misc.m", "k9t_untyped.m", "k9t_opt.m", "k9t_ref.m", "k9t_keyword.m", "k9t_multi.m",
        "k9t_none.m", "k9t_objects.m", "k9point.m", "k9counter.m", "k9color.m", "k9opt.m", Path.Combine("+k9pkg", "k9inner.m"),
    ];

    /// <summary>MATLAB's own classes among those the probe's arguments name.</summary>
    private static readonly HashSet<string> MatlabClasses = new(StringComparer.Ordinal)
    {
        "double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "logical", "char", "string",
        "cell", "struct", "dictionary",
    };

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    /// <summary>The header for <paramref name="files"/> of src/k9, its classes the build's own and MATLAB's restricted, as mcc tells them apart.</summary>
    private static CppInterface Generated(IEnumerable<string> files)
    {
        (List<TypedFunction> functions, List<TypedClass> classes) = TypedInterfaceReader.Read(files.Select(f => Path.Combine(Probes, "src", "k9", f)));
        var own = classes.ToDictionary(static c => c.FullName, StringComparer.Ordinal);
        return TypedInterfaceCpp.Generate(functions, classes, @"C:\out\k12libv2.hpp", new DateTime(2026, 10, 25),
            name => own.TryGetValue(name, out TypedClass? cls)
                ? new CppClassFacts(Restricted: false, External: true, cls.Enumeration.Count > 0, cls.IsHandle)
                : new CppClassFacts(MatlabClasses.Contains(name), External: false, Enumeration: false, Handle: false),
            "0.2.3");
    }

    /// <summary>A header's lines below its five-line comment; JGraph's names before name-value arguments left out with <paramref name="named"/>.</summary>
    private static string[] Body(string text, bool named = false)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[5..];
        return named ? [.. lines.Where(static l => !l.Contains("_arrayFactory.createCharArray(\"", StringComparison.Ordinal))] : lines;
    }

    private static string Recorded(string file) => File.ReadAllText(Path.Combine(Probes, "recordings", "k12_out", file));

    [Fact]
    public void TheMainHeaderIsR2025bsBelowItsComment()
    {
        CppInterface generated = Generated(MainFiles);
        Assert.Equal(4, generated.Classes);
        Assert.Equal(22, generated.Functions);
        Assert.Empty(generated.Warnings);
        Assert.Equal(Body(Recorded("main_v2_generic_interface_k12libv2.hpp")), Body(generated.Text, named: true));
        Assert.False(generated.Text.EndsWith('\n'));
    }

    [Fact]
    public void TheVariadicAndOneFunctionHeadersAreR2025bs()
    {
        Assert.Equal(Body(Recorded("variadic_v2_generic_interface_k12vv2.hpp")), Body(Generated(["k9nargs.m", "k9nout.m", "k9describe.m"]).Text));
        Assert.Equal(Body(Recorded("defaults_v2_generic_interface_k9echov2.hpp")), Body(Generated(["k9echo.m"]).Text));
    }

    [Fact]
    public void TheCommentNamesTheFileAndJGraphsVersion()
    {
        string[] lines = Generated(["k9echo.m"]).Text.Split('\n');
        Assert.Equal(
            ["/* File: C:\\out\\k12libv2.hpp", "*", "* JGraph Strongly Typed Interface Version: 0.2.3 (R2025b)", "* C++ source code generated on: 25-Oct-2026", "*/",
                "#include \"MatlabTypesInterface.hpp\"", "#include <map>"],
            lines[..7]);
    }

    [Fact]
    public void ANameValueArgumentIsPassedWithItsName()
    {
        // k12_client: t.nv - R2025b's header passed Scale and Label at positions 2 and 3, which k9nv refuses.
        string[] lines = Generated(["k9nv.m"]).Text.Split('\n');
        int scale = Array.FindIndex(lines, static l => l.Trim() == "_arrayFactory.createCharArray(\"Scale\"),");
        Assert.True(scale > 0);
        Assert.Equal("_arrayFactory.createArray<double>({1,1}, {Scale}),", lines[scale + 1].Trim());
        Assert.Equal("_arrayFactory.createCharArray(\"Label\"),", lines[scale + 2].Trim());
    }

    [Fact]
    public void TheCommandLineReadsCpplibAsR2025bDoes()
    {
        MccCommandLine generic = MccCommandLine.Parse(["-W", "cpplib:k12gn,generic", "-T", "link:lib", "k9echo.m"]);
        Assert.Equal(MccTarget.CppLibrary, generic.Target);
        Assert.Equal(CppInterfaceKind.Generic, generic.CppInterface);
        Assert.Equal("k12gn", generic.ComponentName);
        Assert.Null(generic.IgnoredVersion);

        Assert.Equal(CppInterfaceKind.All, MccCommandLine.Parse(["-W", "cpplib:k12m", "k9echo.m"]).CppInterface);
        Assert.Equal(CppInterfaceKind.All, MccCommandLine.Parse(["-W", "cpplib:k12al,all", "k9echo.m"]).CppInterface);
        Assert.Equal("2.0", MccCommandLine.Parse(["-W", "cpplib:k12vr,generic,version=2.0", "k9echo.m"]).IgnoredVersion);
        Assert.Null(MccCommandLine.Parse(["-W", "cpplib:k12vf,version=2.0", "k9echo.m"]).IgnoredVersion);
        Assert.Equal(MccTarget.CppLibrary, MccCommandLine.Parse(["-W", "cpplib:k12ex,generic", "-T", "link:exe", "k9echo.m"]).Target);
        Assert.Equal("k12t2", MccCommandLine.Parse(["-W", "cpplib:k12t1,generic", "-W", "cpplib:k12t2,generic", "k9echo.m"]).ComponentName);
        Assert.True(MccCommandLine.Parse(["-W", "cpplib:k12db,generic", "-g", "k9echo.m"]).DebugBuild);

        // -c writes a C library's source, as R2025b's does (k12_build: mcc_c).
        MccCommandLine dashC = MccCommandLine.Parse(["-W", "cpplib:k12cc,generic", "-c", "k9echo.m"]);
        Assert.Equal(MccTarget.CLibrary, dashC.Target);
        Assert.Equal(CLibraryStage.Codegen, dashC.LibraryStage);
        Assert.Equal("k12cc", dashC.ComponentName);

        static string Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args)).Identifier;
        Assert.Equal("Compiler:compiler:COM_ERR_MULTIPLE_COMPONENT_NAMES", Refused("-W", "cpplib:k12on,generic", "-o", "k12other", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME", Refused("-W", "cpplib:1bad,generic", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME", Refused("-W", "cpplib:a-b,generic", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_COMPONENT_NAME_NEEDED_FOR_CPPLIB", Refused("-W", "cpplib:", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_NAMED_CPPLIB_NOT_SUPPORTED_YET", Refused("-W", "cpplib:k12nm,named", "k9echo.m"));
        Assert.Equal("Compiler:compiler:COM_ERR_BAD_ARG_FOR_CPPLIB", Refused("-W", "cpplib:k12ba,bogus", "k9echo.m"));

        // The legacy mwArray interface is not built (decision KD7).
        Assert.Equal("JGraph:mcc:targetOutOfScope", Refused("-W", "cpplib:k12lg,legacy", "-T", "link:lib", "k9echo.m"));
    }

    [Fact]
    public void TheBuildWritesR2025bsFilesInJGraphsWords()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k12-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = new CtfBuild { ComponentName = "k12x", Files = [] };
            CppSharedLibraryBuild built = CppSharedLibrary.Write(build, new CppSharedLibraryOptions { OutputFolder = folder, LibraryName = "k12x", Header = "/* x */" });
            Assert.Equal([Path.Combine(folder, "v2")], built.Files);
            string generic = Path.Combine(folder, "v2", "generic_interface");
            Assert.Equal(["k12x.ctf", "k12xv2.hpp", "readme.txt"], Directory.GetFiles(generic).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal("/* x */", File.ReadAllText(Path.Combine(generic, "k12xv2.hpp")));

            // The readmes keep R2025b's title and sections and its CR LF (k12_build: main.readme, main.generic.readme).
            string readme = File.ReadAllText(Path.Combine(folder, "readme.txt"));
            Assert.StartsWith("k12x C++ Shared Library\r\n\r\n1. Prerequisites for Deployment \r\n", readme, StringComparison.Ordinal);
            Assert.Contains("2. Files to Deploy and Package", readme, StringComparison.Ordinal);
            Assert.Contains("-k12xv2.hpp", readme, StringComparison.Ordinal);
            Assert.StartsWith("\r\n1. Prerequisites for Deployment \r\n", File.ReadAllText(Path.Combine(generic, "readme.txt")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
