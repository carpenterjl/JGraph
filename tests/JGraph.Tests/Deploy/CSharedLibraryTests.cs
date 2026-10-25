using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// A C shared library's generated text and command line (compiler and SDK plan, stage K11) against
/// what R2025b's mcc -W lib wrote and answered: <c>recordings/k11_out</c> (the probe's main build),
/// <c>recordings/k11_names.out.txt</c> (odd signatures and long names) and
/// <c>recordings/k11_build.out.txt</c> (the command line's forms and refusals). Every line below the
/// header is R2025b's but where ADR 0267 records JGraph renaming an argument so the library compiles.
/// </summary>
public sealed class CSharedLibraryTests
{
    private static readonly string Recordings = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes", "recordings");

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    private static CLibraryFunction F(string name, string[] outputs, string[] inputs) => new(name, inputs, outputs, name + ".m");

    /// <summary>The probe's main build, in its command line's order (src/k11).</summary>
    private static readonly CLibraryFunction[] MainFunctions =
    [
        F("k11plain", ["y"], ["x"]),
        F("k11Mixed", ["a", "b"], ["p", "q"]),
        F("K11upper", ["r"], ["s"]),
        F("k11_under", ["o"], ["i"]),
        F("k11long", ["first_output", "second_output", "third_output"], ["alpha_input", "beta_input", "gamma_input", "delta_input", "epsilon_input"]),
        F("k11wide", ["o1", "o2", "o3", "o4", "varargout"], ["i1", "i2", "i3", "i4", "i5", "i6", "i7", "i8", "varargin"]),
        F("k11_a_function_name_long_enough_to_make_generated_lines_wrap", ["result"], ["value"]),
        F("k11sub", ["y"], ["x"]),
        F("k11noparen", [], []),
        F("k11outonly", ["a", "varargout"], []),
        F("k11varin", [], ["varargin"]),
        F("k11quiet", [], []),
        F("k11fail", [], ["message"]),
    ];

    private static CSharedLibraryOptions Options(string name, IReadOnlyList<CLibraryFunction> functions, bool embed = true) =>
        new() { OutputFolder = ".", LibraryName = name, Functions = functions, EmbedArchive = embed, MccArguments = ["-W", "lib:" + name] };

    /// <summary>A generated file's lines below its header comment, as R2025b's recording of it has them.</summary>
    private static string[] Body(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int end = text.IndexOf(" */\n", StringComparison.Ordinal);
        return text[(end + 4)..].Split('\n');
    }

    [Fact]
    public void TheGeneratedTextIsR2025bsBelowItsHeader()
    {
        CSharedLibraryOptions options = Options("k11lib", MainFunctions);
        foreach ((string file, string text) in new[]
        {
            ("k11lib.h", CSharedLibrary.Header(options)), ("k11lib.c", CSharedLibrary.Source(options)),
        })
        {
            Assert.Equal(Body(File.ReadAllText(Path.Combine(Recordings, "k11_out", file))), Body(text));
        }

        // The export lists have no header: R2025b's bytes, CR LF and the empty last line.
        Assert.Equal(File.ReadAllText(Path.Combine(Recordings, "k11_out", "k11lib.def")).Replace("\r\n", "\n", StringComparison.Ordinal), CSharedLibrary.DefFile(options));
        Assert.Equal(File.ReadAllText(Path.Combine(Recordings, "k11_out", "k11lib.exports")).Replace("\r\n", "\n", StringComparison.Ordinal), CSharedLibrary.Exports(options));
    }

    [Fact]
    public void TheHeaderNamesJGraphsCompilerAndWrapsMccsWordsAsR2025bDoes()
    {
        CSharedLibraryOptions options = Options("k11lib", MainFunctions) with
        {
            MccArguments = ["-W", "lib:k11lib", "-T", "link:lib", "-d", @"C:\a\folder\long\enough\to\need\a\second\line\of\the\header", "k11plain.m"],
            Built = new DateTime(2026, 10, 5, 9, 3, 7),
        };
        string[] lines = CSharedLibrary.Header(options).Split('\n');
        Assert.Equal("/*", lines[0]);
        Assert.Equal($" * JGraph Compiler: {RuntimeLayout.CurrentVersion} (MATLAB Compiler 25.2, R2025b)", lines[1]);
        Assert.Equal(" * Date: Mon Oct  5 09:03:07 2026", lines[2]);
        Assert.Equal(" * Arguments:", lines[3]);

        // mcc's words, each quoted, cut into pieces of 77 after " * " as R2025b cuts them (k11_out).
        string[] arguments = [.. lines.Skip(4).TakeWhile(static l => l != " */")];
        Assert.Equal(2, arguments.Length);
        Assert.Equal(80, arguments[0].Length);
        Assert.Equal("\"-B\"\"macro_default\"" + string.Concat(options.MccArguments.Select(static w => "\"" + w + "\"")),
            string.Concat(arguments.Select(static l => l[3..])));
        Assert.Equal(" */", lines[4 + arguments.Length]);
    }

    /// <summary>A section of the names recording: the lines printed under <paramref name="key"/>, the trailing empty ones dropped.</summary>
    private static string[] Recorded(string key)
    {
        string[] all = File.ReadAllLines(Path.Combine(Recordings, "k11_names.out.txt"));
        int at = Array.FindIndex(all, l => l.StartsWith(key + "\t", StringComparison.Ordinal));
        Assert.True(at >= 0, key + " is not in the recording");
        var lines = all.Skip(at + 1).TakeWhile(static l => l.StartsWith("  |", StringComparison.Ordinal)).Select(static l => l[3..]).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return [.. lines];
    }

    /// <summary>A generated file's lines from <paramref name="start"/> (or the line before the first wrapper's) on, as probe_k11_names prints them.</summary>
    private static string[] Printed(string text, string? start = null)
    {
        var lines = text.Split('\n').ToList();
        int first = start is null
            ? lines.FindIndex(static l => l.Contains("MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START", StringComparison.Ordinal)
                || l.Contains("MW_CALL_CONV mlx", StringComparison.Ordinal))
            : lines.FindIndex(l => l.Contains(start, StringComparison.Ordinal));
        if (start is null && lines[first].Contains("MW_CALL_CONV mlx", StringComparison.Ordinal))
        {
            first--;
        }

        lines = lines.Skip(first).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return [.. lines];
    }

    [Fact]
    public void LongNamesBreakTheirLinesWhereR2025bsBreak()
    {
        // k11_names: long, names of 48 to 60 letters, which break the wrappers' declarations and the
        // mlx wrapper's call at each place the rule allows.
        CLibraryFunction[] functions =
        [
            .. Enumerable.Range(48, 13).Select(n => F($"k11w{n:D2}" + new string('x', n - 6), ["out"], ["in"])),
        ];
        CSharedLibraryOptions options = Options("k11long", functions);
        Assert.Equal(Recorded("long.k11long.c"), Printed(CSharedLibrary.Source(options)));
        Assert.Equal(Recorded("long.k11long.h"), Printed(CSharedLibrary.Header(options)));

        // longlib: a library's long name breaks its Initialize's call, and nothing else of its own.
        CSharedLibraryOptions longLibrary = Options("k11_a_library_name_long_enough_to_break_the_general_functions",
            [F("k11t2", ["y"], ["~", "~", "c"])]);
        string source = "k11_a_library_name_long_enough_to_break_the_general_functions";
        Assert.Equal(Recorded($"longlib.{source}.c"), Printed(CSharedLibrary.Source(longLibrary), "#define EXPORTING_"));
        Assert.Equal(Recorded($"longlib.{source}.h"), Printed(CSharedLibrary.Header(longLibrary), "GENERAL LIBRARY FUNCTIONS -- START"));
    }

    [Fact]
    public void ArgumentsAreNamedAsR2025bNamesThem()
    {
        // k11_build and k11_names: an ignored input in turn _U4b, _U4c; an input named as an output
        // with _in and the first number no other argument has; a word C reserves with a 0.
        Assert.Equal("y | _U4b _U4c c", Names(F("k11t2", ["y"], ["~", "~", "c"])));
        Assert.Equal("a b | b_in1 a_in1", Names(F("k11same2", ["a", "b"], ["b", "a"])));
        Assert.Equal("x | x_in2 x_in1", Names(F("k11same4", ["x"], ["x", "x_in1"])));
        Assert.Equal("int0 long0 | char0 double0 struct0", Names(F("k11cwords", ["int", "long"], ["char", "double", "struct"])));
        Assert.Equal("default0 inline0 | NULL0 size_t main", Names(F("k11cw3", ["default", "inline"], ["NULL", "size_t", "main"])));
        Assert.Equal(" | class0 bool0 true0 nlhs nargin", Names(F("k11cpp", [], ["class", "bool", "true", "nlhs", "nargin"])));

        // ADR 0267: what R2025b leaves to break the library's build or a C++ client's is renamed too.
        Assert.Equal("plhs | nlhs nargout0", Names(F("k11clash", ["plhs"], ["nlhs", "nargout"])));
        Assert.Equal(" | nargout", Names(F("k11quiet", [], ["nargout"]))); // no wrapper nargout to clash with
        Assert.Equal("new0 this0 | delete0 restrict0 mxArray0 MW_CALL_CONV0 mclMlfFeval0",
            Names(F("k11cpp", ["new", "this"], ["delete", "restrict", "mxArray", "MW_CALL_CONV", "mclMlfFeval"])));
        Assert.Equal("int00 | int_in1 int0", Names(F("k11taken", ["int"], ["int", "int0"]))); // a renamed one names no other
    }

    /// <summary>A wrapper's C names: the outputs, a bar, the inputs.</summary>
    private static string Names(CLibraryFunction f)
    {
        (IReadOnlyList<string> outputs, IReadOnlyList<string> inputs) = CSharedLibrary.ArgumentNames(f, "k11lib");
        return string.Join(" ", outputs) + " | " + string.Join(" ", inputs);
    }

    [Fact]
    public void ALibraryWithItsArchiveBesideItStartsFromThatArchive()
    {
        // k11_build: separate (-C): R2025b's own Initialize, naming the library and LibTarget.
        string source = CSharedLibrary.Source(Options("k11sep", [F("k11plain", ["y"], ["x"])], embed: false)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "    if (!GetModuleFileName(GetModuleHandle(\"k11sep\"), path_to_dll, _MAX_PATH))\n        return false;\n"
            + "    bResult = mclInitializeComponentInstanceNonEmbeddedStandalone(&_mcr_inst,\n        path_to_dll,\n        \"k11sep\",\n"
            + "        LibTarget,\n        error_handler, \n        print_handler,\n        NULL);\n    if (!bResult)\n    return false;\n",
            source, StringComparison.Ordinal);
        Assert.DoesNotContain("mclGetEmbeddedCtfStream", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandLineReadsALibraryAsR2025bsMccReadsIt()
    {
        static MccCommandLine Parse(params string[] args) => MccCommandLine.Parse(args);
        static MccException Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args));

        MccCommandLine plain = Parse("-W", "lib:k11lib,version=2.5", "-T", "link:lib", "a.m", "b.m");
        Assert.Equal((MccTarget.CLibrary, "k11lib", "2.5.0.0", CLibraryStage.Link), (plain.Target, plain.ComponentName, plain.Version.ToString(), plain.LibraryStage));
        Assert.Equal(["a.m", "b.m"], plain.Files);
        Assert.Equal(CLibraryStage.Link, Parse("-W", "lib:k11noT", "a.m").LibraryStage); // noT: link:lib is the default

        // ell, ell_o, W_W, l_W, W_l, m_W, W_extra: the names a library takes.
        Assert.Equal((MccTarget.CLibrary, (string?)null), (Parse("-l", "a.m").Target, Parse("-l", "a.m").ComponentName));
        Assert.Equal("k11named", Parse("-l", "-o", "k11named", "a.m").OutputName);
        Assert.Equal("k11b", Parse("-W", "lib:k11a", "-W", "lib:k11b", "-T", "codegen", "a.m").ComponentName);
        Assert.Null(Parse("-l", "-W", "lib:k11lw", "-T", "codegen", "a.m").ComponentName);
        Assert.Null(Parse("-W", "lib:k11wl", "-l", "-T", "codegen", "a.m").ComponentName);
        Assert.Equal(MccTarget.CLibrary, Parse("-m", "-W", "lib:k11mw", "-T", "codegen", "a.m").Target);
        Assert.Null(Parse("-m", "-W", "lib:k11mw", "-T", "codegen", "a.m").ComponentName);
        Assert.Equal("k11x", Parse("-W", "lib:k11x,bogus", "a.m").ComponentName);

        // codegen, dashc, compile, debug, separate: how far a build goes, and its switches.
        Assert.Equal(CLibraryStage.Codegen, Parse("-W", "lib:k11cg", "-T", "codegen", "a.m").LibraryStage);
        MccCommandLine dashC = Parse("-W", "lib:k11c", "-c", "a.m");
        Assert.Equal((CLibraryStage.Codegen, 0), (dashC.LibraryStage, dashC.IgnoredSwitches.Count));
        Assert.Equal(CLibraryStage.Compile, Parse("-W", "lib:k11cmp", "-T", "compile:lib", "a.m").LibraryStage);
        MccCommandLine debug = Parse("-W", "lib:k11dbg", "-g", "a.m");
        Assert.Equal((true, 0), (debug.DebugBuild, debug.IgnoredSwitches.Count));
        Assert.True(Parse("-W", "lib:k11sep", "-C", "a.m").SeparateArchive);

        // badname, digitname, badversion, lib_o, o_W, exeT, nofiles: R2025b's refusals in its words.
        Assert.Equal(
            "Illegal output name: 'k11-bad' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')",
            Refused("-W", "lib:k11-bad", "a.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME", Refused("-W", "lib:1k11", "a.m").Identifier);
        Assert.Equal(
            "Version string \"x.y\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.",
            Refused("-W", "lib:k11bad,version=x.y", "a.m").Message);
        Assert.Equal(
            "Error: Multiple component names. Processing \"-o\" switch. Component name \"k11w\" already specified via \"-W\".",
            Refused("-W", "lib:k11w", "-o", "k11other", "-T", "link:lib", "a.m").Message);
        Assert.Equal(
            "Error: Multiple component names. Processing \"-W\" switch. Component name \"k11o\" already specified via \"-o\".",
            Refused("-o", "k11o", "-W", "lib:k11w", "-T", "codegen", "a.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_WRAPPER_TARGET_MISMATCH", Refused("-W", "lib:k11exe", "-T", "link:exe", "a.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_NO_FILES_SPECIFIED", Refused("-W", "lib:k11nf", "-T", "link:lib").Identifier);
    }

    [Fact]
    public void BreakingKeepsTheBlankAndLeavesTheLastCharacterUncounted()
    {
        // k11_names: a definition's closing parenthesis may reach column 90 (long: mxArray* in)),
        // a statement's semicolon too (k11_build: K11Named's Initialize), and nothing else.
        Assert.Equal("ab cd", CSharedLibrary.Broken("ab cd", "", 2));
        string line = new string('w', 80) + " " + "12345678)";
        Assert.Equal(line, CSharedLibrary.Broken(line, "", 4)); // 90 columns, the last uncounted
        string longer = new string('w', 80) + " " + "123456789)";
        Assert.Equal(new string('w', 80) + " \n    123456789)", CSharedLibrary.Broken(longer, "", 4));
        string within = new string('w', 80) + " " + "12345678 x";
        Assert.Equal(new string('w', 80) + " 12345678 \n    x", CSharedLibrary.Broken(within, "", 4)); // a word before the last counts whole
    }
}
