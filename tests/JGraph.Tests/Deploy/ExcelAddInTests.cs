using JGraph.Api;
using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// An Excel add-in's command line, options and output folder (compiler and SDK plan, stage K17b)
/// against what R2025b's mcc -W excel and compiler.build.excelAddIn answered and wrote
/// (<c>recordings/k17_excel.out.txt</c>): the forms and refusals, the DLL's name by R2025b's version
/// rule, <c>_install.bat</c> line for line, the options' derived names and checks, and the files a
/// build leaves: the DLL from the template (stage K17c, <c>ExcelAddInServerTests</c>), the archive
/// in it or beside it, the texts, and a warning for the <c>.bas</c> (stage K17e) while it is
/// pending. The readme and GettingStarted.html are in JGraph's words (ADR 0315).
/// </summary>
[Collection("JG facade")]
public sealed class ExcelAddInTests : IDisposable
{
    private static readonly string Recordings = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes", "recordings");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k17b-" + Guid.NewGuid().ToString("N"))).FullName;

    public ExcelAddInTests()
    {
        JG.Reset();
        File.WriteAllText(Path.Combine(_root, "k17_one.m"), "function y = k17_one(x)\n%K17_ONE Doubles its one input (compiler and SDK plan, stage K17).\ny = 2 * x;\nend\n");
        File.WriteAllText(Path.Combine(_root, "k17_none.m"), "function y = k17_none()\ny = 1;\nend\n");
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp folder clears it.
        }
    }

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    private MccCommandLine Parse(params string[] args) => MccCommandLine.Parse(args, _root);

    private MccException Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args, _root));

    /// <summary>A file's lines as the recording prints them under <paramref name="key"/> (its <c>  |</c> prefix dropped).</summary>
    private static string[] Recorded(string key)
    {
        string[] all = File.ReadAllLines(Path.Combine(Recordings, "k17_excel.out.txt"));
        int at = Array.FindIndex(all, l => l.StartsWith(key + "\t", StringComparison.Ordinal));
        Assert.True(at >= 0, key + " is not in the recording");
        return [.. all.Skip(at + 1).TakeWhile(static l => l.StartsWith("  |", StringComparison.Ordinal)).Select(static l => l[3..])];
    }

    [Fact]
    public void TheCommandLineReadsExcelAsR2025bDoes()
    {
        // k17_excel: mcc_default, mcc_noversion, mcc_noclass, mcc_version2, mcc_bundle, mcc_notarget, mcc_separate, mcc_debug, mcc_codegen.
        MccCommandLine plain = Parse("-W", "excel:k17m,K17M,version=1.0", "-T", "link:lib", "-b", "k17_one.m");
        Assert.Equal(MccTarget.ExcelAddIn, plain.Target);
        Assert.Equal("k17m", plain.ComponentName);
        Assert.Equal("K17M", plain.ExcelClassName);
        Assert.Equal(new ProgramVersion(1, 0, 0, 0), plain.Version);
        Assert.True(plain.VisualBasicFile);
        Assert.False(plain.SeparateArchive);
        Assert.Equal(MccLink.Program, plain.Link);
        Assert.False(Parse("-W", "excel:k17mnv,K17Mnv", "-T", "link:lib", "k17_one.m").VisualBasicFile);
        Assert.Equal("k17mnc", Parse("-W", "excel:k17mnc", "-T", "link:lib", "k17_one.m").ExcelClassName);
        Assert.Equal(new ProgramVersion(2, 0, 0, 0), Parse("-W", "excel:k17mv2,K17Mv2,version=2", "-T", "link:lib", "k17_one.m").Version);
        MccCommandLine bundled = Parse("-B", "cexcel:k17mb,K17Mb,1.0", "k17_one.m");
        Assert.Equal(MccTarget.ExcelAddIn, bundled.Target);
        Assert.Equal(("k17mb", "K17Mb"), (bundled.ComponentName, bundled.ExcelClassName));
        Assert.True(bundled.VisualBasicFile);
        Assert.Equal(MccTarget.ExcelAddIn, Parse("-W", "excel:k17mnt,K17Mnt,version=1.0", "k17_one.m").Target);
        Assert.True(Parse("-W", "excel:k17ms,K17Ms,version=1.0", "-T", "link:lib", "-C", "k17_one.m").SeparateArchive);
        MccCommandLine debug = Parse("-W", "excel:k17md,K17Md,version=1.0", "-T", "link:lib", "-g", "k17_one.m");
        Assert.True(debug.DebugBuild);
        Assert.Empty(debug.IgnoredSwitches);
        Assert.Equal(MccLink.ArchiveOnly, Parse("-W", "excel:k17mcg,K17Mcg,version=1.0", "-T", "codegen", "k17_one.m").Link);
        Assert.Equal(MccLink.ProgramBesideArchive, Parse("-W", "excel:k17mcp,K17Mcp,version=1.0", "-T", "compile:lib", "k17_one.m").Link);

        // mcc_empty, mcc_nocolon, mcc_badversion, mcc_extra, mcc_dotted, mcc_o, and a version past the fourth part (vtext, v5parts, vbig through compiler.build).
        foreach (string[] args in new[] { new[] { "-W", "excel:", "-T", "link:lib", "k17_one.m" }, ["-W", "excel", "-T", "link:lib", "k17_one.m"] })
        {
            MccException empty = Refused(args);
            Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_COMPONENT_NAME", empty.Identifier);
            Assert.Equal("Error: Component name invalid or unspecified (specify with -n or -W).", empty.Message);
        }

        MccException bad = Refused("-W", "excel:k17mbv,K17Mbv,version=x.y", "-T", "link:lib", "k17_one.m");
        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_VERSION", bad.Identifier);
        Assert.Equal("Version string \"x.y\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.", bad.Message);
        Assert.Equal("Version string \"version=1.0\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.",
            Refused("-W", "excel:k17mx,K17Mx,version=1.0,other=1", "-T", "link:lib", "k17_one.m").Message);
        foreach (string version in new[] { "abc", "1.2.3.4.5", "70000" })
        {
            Assert.Equal("Compiler:compiler:COM_ERR_INVALID_VERSION", Refused("-W", $"excel:k17v,K17V,version={version}", "-T", "link:lib", "k17_one.m").Identifier);
        }

        MccException dotted = Refused("-W", "excel:a.b,K17Dot,version=1.0", "-T", "link:lib", "k17_one.m");
        Assert.Equal("Compiler:compiler:COM_COM_EXCEL_NAMESPACE_ERROR", dotted.Identifier);
        Assert.Equal("\"Package \"a\" not supported for COM components and Excel addins\".", dotted.Message);
        MccException named = Refused("-W", "excel:k17mo,K17Mo,version=1.0", "-T", "link:lib", "-o", "k17other", "k17_one.m");
        Assert.Equal("Compiler:compiler:COM_ERR_MULTIPLE_COMPONENT_NAMES", named.Identifier);
        Assert.Equal("Error: Multiple component names. Processing \"-o\" switch. Component name \"k17mo\" already specified via \"-W\".", named.Message);
    }

    [Fact]
    public void TheDllIsNamedByR2025bsVersionRule()
    {
        // k17_excel: plain, v2, v05, v3456, vzero (k17xl_1_0, k17v2_2_0, k17v05_1_0, k17v3456_3_4, k17vz_1_0) and the postmcc rule itself.
        foreach ((string version, string suffix) in new[]
        {
            ("1.0.0.0", "1_0"), ("2", "2_0"), ("0.5", "1_0"), ("3.4.5.6", "3_4"), ("0", "1_0"), ("1.2.3.4", "1_2"), ("12.34", "12_34"),
        })
        {
            Assert.Equal(suffix, ExcelAddIn.VersionSuffix(ProgramVersion.Parse(version)!.Value));
        }

        Assert.Equal("k17xl_1_0.dll", ExcelAddIn.DllName("k17xl", ProgramVersion.Default));
        Assert.True(ExcelAddIn.IsComName("k17_ok"));
        Assert.True(ExcelAddIn.IsComName("_x"));
        foreach (string bad in new[] { "1bad", "a-b", "a b", "a.b", "" })
        {
            Assert.False(ExcelAddIn.IsComName(bad));
        }
    }

    [Fact]
    public void TheInstallBatIsR2025bsLineForLine()
    {
        // k17_excel: plain.file._install.bat (880 chars, line feeds, no line end after the last line).
        string bat = ExcelAddIn.InstallBat("k17xl_1_0.dll");
        Assert.Equal(string.Join('\n', Recorded("plain.file._install.bat")), bat);
        Assert.Equal(880, bat.Length);
        Assert.DoesNotContain("\r", bat, StringComparison.Ordinal);
        Assert.Equal(string.Join('\n', Recorded("defaults.file._install.bat")), ExcelAddIn.InstallBat("k17_one_1_0.dll"));
    }

    private async Task<ScriptRunResult> Run(string code)
    {
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, _root));
        return await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
    }

    /// <summary>What a statement answers: its printed text, or <c>ERR identifier | message</c>, and the warning it raised.</summary>
    private async Task<string> Said(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await Run(
            $"lastwarn(''); try, {statement}, catch e, disp(['ERR ' e.identifier ' | ' strrep(e.message, newline, ' / ')]), end; [m, id] = lastwarn; if ~isempty(id), disp(['warn ' id]), end");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    [Fact]
    public async Task TheOptionsDeriveAndCheckAsR2025bsDo()
    {
        // k17_excel: opts.derived.*, derived.afterfiles, derived.userkept, set.AddInName.*, set.ClassName.*, set.AddInVersion.*, opts.unknown, opts.toomany.
        Assert.Equal("k17_one | k17_oneClass | .\\k17_oneexcelAddIn",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); disp([o.AddInName ' | ' o.ClassName ' | ' o.OutputDir])"));
        Assert.Equal("k17_none | k17_noneClass",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.FunctionFiles = 'k17_none.m'; disp([o.AddInName ' | ' o.ClassName])"));
        Assert.Equal("kept | k17_noneClass",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m', 'AddInName', 'kept'); o.FunctionFiles = 'k17_none.m'; disp([o.AddInName ' | ' o.ClassName])"));
        Assert.Equal("a b", await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.AddInName = 'a b'; disp(o.AddInName)"));
        Assert.Equal("ERR Compiler:clishared:noWhiteSpaceInProperty | 'AddInName' cannot contain any white space characters.",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.AddInName = sprintf('a\\tb');"));
        Assert.Equal("ERR MATLAB:validators:mustBeNonzeroLengthText | Value must be text with one or more characters.",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.ClassName = '';"));
        Assert.Equal("ERR MATLAB:invalidType | Expected AddInVersion to be one of these types: /  / char, string /  / Instead its type was double.",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.AddInVersion = 5;"));
        Assert.Equal("abc", await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.AddInVersion = 'abc'; disp(o.AddInVersion)"));
        Assert.Equal("ERR MATLAB:validation:UnableToConvert | Error setting property 'GenerateVisualBasicFile' of class 'compiler.build.ExcelAddInOptions'. 'x' is invalid. Value must be 'off' or 'on'.",
            await Said("o = compiler.build.ExcelAddInOptions('k17_one.m'); o.GenerateVisualBasicFile = 'x';"));
        Assert.Equal("ERR MATLAB:InputParser:UnmatchedParameter | 'Bogus' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.",
            await Said("compiler.build.ExcelAddInOptions('k17_one.m', 'Bogus', 1);"));
        Assert.Equal("ERR MATLAB:narginchk:tooManyInputs | Too many input arguments.",
            await Said("many = repmat({'Verbose', 'on'}, 1, 20); compiler.build.ExcelAddInOptions('k17_one.m', many{:});"));

        // vtext: the version is mcc's to refuse, when the build runs.
        Assert.Equal("ERR Compiler:compiler:COM_ERR_INVALID_VERSION | Version string \"abc\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.",
            await Said("compiler.build.excelAddIn('k17_one.m', 'AddInName', 'k17vt', 'AddInVersion', 'abc', 'OutputDir', 'vt');"));
    }

    [Fact]
    public async Task ABuildWritesTheDllAndSaysWhatIsPending()
    {
        // mcc -W excel (k17_excel: mcc_default): the DLL with the archive in it, the readme and the logs; the .bas pending until K17e, said once.
        string m = Path.Combine(_root, "m");
        string said = await Said($"mcc('-W', 'excel:k17m,K17M,version=1.0', '-T', 'link:lib', '-b', '-d', '{m.Replace("\\", "/", StringComparison.Ordinal)}', 'k17_one.m');");
        Assert.Equal("warn JGraph:mcc:excelAddInPending", said);
        foreach (string file in new[] { "k17m_1_0.dll", "readme.txt", "buildresult.json", "mccExcludedFiles.log", "requiredMCRProducts.txt", "unresolvedSymbols.txt", "includedSupportPackages.txt" })
        {
            Assert.True(File.Exists(Path.Combine(m, file)), file + " was not written");
        }

        Assert.False(File.Exists(Path.Combine(m, "k17m.ctf")), "the archive is embedded in the DLL");
        Assert.False(File.Exists(Path.Combine(m, "_install.bat")), "mcc writes no _install.bat; compiler.build does");
        string readme = File.ReadAllText(Path.Combine(m, "readme.txt"));
        Assert.StartsWith("k17m JGraph Excel Add-in \r\n\r\n1. Prerequisites for Deployment \r\n", readme, StringComparison.Ordinal);
        Assert.Contains("-k17m_1_0.dll (output from mcc)\r\n-k17m.bas (VBA module file created by mcc)\r\n", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("k17m.ctf", readme, StringComparison.Ordinal);

        // Without -b nothing is pending; -C leaves the archive beside the DLL (mcc_separate).
        string sep = Path.Combine(_root, "sep");
        Assert.Equal("", await Said($"mcc('-W', 'excel:k17ms,K17Ms,version=1.0', '-T', 'link:lib', '-C', '-d', '{sep.Replace("\\", "/", StringComparison.Ordinal)}', 'k17_one.m');"));
        Assert.True(File.Exists(Path.Combine(sep, "k17ms_1_0.dll")));
        Assert.True(File.Exists(Path.Combine(sep, "k17ms.ctf")));
        Assert.Contains("-k17ms.ctf (component technology file)\r\n-k17ms_1_0.dll (output from mcc)\r\n", File.ReadAllText(Path.Combine(sep, "readme.txt")), StringComparison.Ordinal);

        // -T codegen writes the same without the warning (k17_excel: mcc_codegen has no DLL either).
        string cg = Path.Combine(_root, "cg");
        Assert.Equal("", await Said($"mcc('-W', 'excel:k17mcg,K17Mcg,version=1.0', '-T', 'codegen', '-d', '{cg.Replace("\\", "/", StringComparison.Ordinal)}', 'k17_one.m');"));
        Assert.True(File.Exists(Path.Combine(cg, "k17mcg.ctf")));

        // A name that is not an identifier is refused before anything is written (mcc_badname, mcc_badclass: R2025b fails in mbuild).
        string bad = Path.Combine(_root, "bad");
        Assert.StartsWith("ERR JGraph:mcc:excelAddInName | ", await Said($"mcc('-W', 'excel:1bad,K17Bad,version=1.0', '-T', 'link:lib', '-d', '{bad.Replace("\\", "/", StringComparison.Ordinal)}', 'k17_one.m');"), StringComparison.Ordinal);
        Assert.StartsWith("ERR JGraph:mcc:excelClassName | ", await Said($"mcc('-W', 'excel:k17mbc,1Bad,version=1.0', '-T', 'link:lib', '-d', '{bad.Replace("\\", "/", StringComparison.Ordinal)}', 'k17_one.m');"), StringComparison.Ordinal);
        Assert.False(Directory.Exists(bad) && Directory.EnumerateFileSystemEntries(bad).Any());

        // compiler.build.excelAddIn (k17_excel: plain.Files, noembed.Files, plain.BuildType): the DLL, _install.bat, the separate archive and the page in R2025b's order.
        string b = Path.Combine(_root, "b").Replace("\\", "/", StringComparison.Ordinal);
        Assert.Equal("excelAddIn | k17b | K17B | 3\n<OUT>/k17b_1_0.dll | <OUT>/_install.bat | <OUT>/GettingStarted.html",
            await Said($"r = compiler.build.excelAddIn('k17_one.m', 'AddInName', 'k17b', 'ClassName', 'K17B', 'OutputDir', '{b}'); "
                + $"disp([r.BuildType ' | ' r.Options.AddInName ' | ' r.Options.ClassName ' | ' num2str(numel(r.Files))]); "
                + $"disp(strjoin(strrep(strrep(r.Files(:)', '{b.Replace("/", "\\", StringComparison.Ordinal)}', '<OUT>'), '\\', '/'), ' | '))"));
        string ne = Path.Combine(_root, "ne").Replace("\\", "/", StringComparison.Ordinal);
        Assert.Equal("4\n<OUT>/k17ne_1_0.dll | <OUT>/_install.bat | <OUT>/k17ne.ctf | <OUT>/GettingStarted.html",
            await Said($"r = compiler.build.excelAddIn('k17_one.m', 'AddInName', 'k17ne', 'ClassName', 'K17Ne', 'EmbedArchive', 'off', 'OutputDir', '{ne}'); "
                + $"disp(num2str(numel(r.Files))); disp(strjoin(strrep(strrep(r.Files(:)', '{ne.Replace("/", "\\", StringComparison.Ordinal)}', '<OUT>'), '\\', '/'), ' | '))"));
        Assert.Equal(string.Join('\n', Recorded("plain.file._install.bat")).Replace("k17xl_1_0.dll", "k17b_1_0.dll", StringComparison.Ordinal),
            File.ReadAllText(Path.Combine(_root, "b", "_install.bat")));
        Assert.Contains("<title>Getting Started with the k17b Excel Add-In</title>", File.ReadAllText(Path.Combine(_root, "b", "GettingStarted.html")), StringComparison.Ordinal);

        // An options object alone, and the installer takes the add-in's results by its name (compiler.package.InstallerOptions(results)).
        Assert.Equal("k17b",
            await Said($"o = compiler.build.ExcelAddInOptions('k17_one.m', 'AddInName', 'k17b', 'OutputDir', '{b}'); r = compiler.build.excelAddIn(o); "
                + "p = compiler.package.InstallerOptions(r); disp(p.ApplicationName)"));
    }
}
