using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The rest of mcc's command line (compiler and SDK plan, stage K7) against what R2025b's mcc answered
/// (<c>tools/matlab-checklist/compiler-probes/recordings/k7_switches.out.txt</c> and <c>k7_more.out.txt</c>).
/// </summary>
public sealed class MccSwitchesTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k7-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private MccCommandLine Parse(params string[] args) => MccCommandLine.Parse(args, _folder, _folder);

    private MccException Refused(params string[] args) => Assert.Throws<MccException>(() => Parse(args));

    [Fact]
    public void ASwitchOfSeveralLettersIsThatManySwitches()
    {
        MccCommandLine mv = Parse("-mv", "k7_hello.m");
        Assert.True(mv.Verbose);
        Assert.Equal(MccTarget.Console, mv.Target);
        Assert.True(Parse("-mC", "k7_hello.m").SeparateArchive);

        // -ok7ns is -o -k -7 -n -s: -o takes -k, and -7 is no switch (no_space).
        Assert.Equal("Error: Unrecognized option: -7.", Refused("-ok7ns", "-m", "k7_hello.m").Message);

        // -help is -h -e -l -p: -h takes -e, a file that is not there (help_word), checked before anything else.
        MccException help = Refused("-help");
        Assert.Equal("Compiler:compiler:COM_ERR_HELP_FILE_NOT_EXIST", help.Identifier);
        Assert.Equal("The help file \n  '-e'\ndoes not exist.", help.Message);
        Assert.Equal("The help file \n  '-d'\ndoes not exist.", Refused("-m", "k7_hello.m", "-h", "-d", "out").Message);
    }

    [Fact]
    public void TheObsoleteSwitchesAreNotedOrRefused()
    {
        MccCommandLine noted = Parse("-O", "-i", "-z", "-L", "C", "-t", "-m", "k7_hello.m");
        Assert.Equal(5, noted.Notes.Count);
        Assert.Equal("The -O switch is no longer supported; the MATLAB JIT accelerator makes \nCompiler optimizations unnecessary. \n", noted.Notes[0]);
        Assert.Equal("The -z switch is no longer supported. \n", noted.Notes[2]);

        Assert.Equal("Compiler:compiler:COM_MSG_NO_DASH_V", Refused("-V", "-m", "k7_hello.m").Identifier);
        Assert.Equal("The -win32 switch is no longer supported. \n", Refused("-win32", "-m", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_MSG_NO_DASH_WIN64", Refused("-win64", "-m", "k7_hello.m").Identifier);
        Assert.StartsWith("Error: -y is no longer supported. The MATLAB Compiler no longer generates \n  Simulink", Refused("-y", "-m", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_DASH_F_DEPRECATED", Refused("-F", "k7.prj").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_OPTION_F_MUST_BE_ALONE", Refused("-F", "k7.prj", "-m", "k7_hello.m").Identifier);
    }

    [Fact]
    public void ASwitchAStandaloneBuildIgnoresIsTaken()
    {
        // With R2025b's warning, -G named as -g (g, G, M, f, c_m) ...
        Assert.Equal(["g", "g", "M", "f", "c"], Parse("-g", "-G", "-M", "-DK7=1", "-f", "k7_opts.xml", "-c", "-m", "k7_hello.m").IgnoredSwitches);

        // ... and without (b_m, obs_S, obs_u, H_m, P_m, Y, A_all; k7_more: -P takes no switch for its value).
        MccCommandLine quiet = Parse("-b", "-S", "-u", "-H", "-P", "x", "-Y", "license.dat", "-A", "all", "-P", "-m", "k7_hello.m");
        Assert.Empty(quiet.IgnoredSwitches);
        Assert.Equal(["k7_hello.m"], quiet.Files);

        Assert.Equal("Error: \"none\" is not a valid -A option argument.", Refused("-A", "none", "-m", "k7_hello.m").Message);
        Assert.Equal("Error: Unrecognized option: -D.", Refused("-D", "x", "-m", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_MISSING_WRAPPER_FOR_CODEGEN_ONLY", Refused("-c", "k7_hello.m").Identifier);
    }

    [Fact]
    public void TheTargetSaysHowFarTheBuildGoes()
    {
        Assert.Equal(MccLink.Program, Parse("-m", "k7_hello.m").Link);
        Assert.Equal(MccLink.ArchiveOnly, Parse("-m", "-T", "codegen", "k7_hello.m").Link);
        MccCommandLine compiled = Parse("-W", "main:k7tc", "-T", "compile:exe", "k7_hello.m");
        Assert.Equal(MccLink.ProgramBesideArchive, compiled.Link);
        Assert.True(compiled.SeparateArchive);
        Assert.True(Parse("-U", "-e", "k7_hello.m").SeparateArchive); // U_m, U_e
        Assert.Equal(MccLink.Program, Parse("-T", "exe", "-m", "k7_hello.m").Link);
        Assert.Equal(MccLink.Program, Parse("-T", "link:", "-m", "k7_hello.m").Link);
        Assert.Equal(MccLink.Program, Parse("-T", "link:lib", "-T", "link:exe", "-W", "main:k7tt", "k7_hello.m").Link);
        Assert.Equal("Compiler:compiler:COM_ERR_WRAPPER_TARGET_MISMATCH", Refused("-m", "-T", "link:lib", "k7_hello.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MISSING_WRAPPER", Refused("-T", "link:exe", "k7_hello.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MISSING_WRAPPER", Refused("-T", "codegen", "k7_hello.m").Identifier);
    }

    [Fact]
    public void EveryTargetIsCheckedAsR2025bChecksIt()
    {
        Assert.Equal(
            "Illegal output name: '' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')",
            Refused("-W", "lib", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_COMPONENT_NAME_NEEDED_FOR_CPPLIB", Refused("-W", "cpplib:", "k7_hello.m").Identifier);
        Assert.Equal("Error: Option \"api=bogus\" is not valid following \"cpplib:component_name,\".\n", Refused("-W", "cpplib:k7c,api=bogus", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_NAMED_CPPLIB_NOT_SUPPORTED_YET", Refused("-W", "cpplib:k7c,named", "k7_hello.m").Identifier);
        Assert.Equal(
            "Invalid value for \"api=\" specified in -W or -B option: bogus. Value must be a member of this set:\n\t'matlab-data'\n\t'mwarray'\n\t''",
            Refused("-W", "dotnet:k7asm,api=bogus", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_API_PLACEMENT", Refused("-W", "dotnet:k7asm,k7cls,api=matlab-data", "k7_hello.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_NET_VERSION_INVALID", Refused("-W", "dotnet:k7asm,k7cls,3.5,Private", "k7_hello.m").Identifier);
        foreach (string empty in new[] { "python:", "com:", "mpsxl:", "excel:" })
        {
            Assert.Equal("Error: Component name invalid or unspecified (specify with -n or -W).", Refused("-W", empty, "k7_hello.m").Message);
        }

        Assert.Equal("Compiler:compiler:COM_ERROR_MISSING_U_WITH_W_CTF", Refused("-W", "CTF:k7ctf", "k7_hello.m").Identifier);
        Assert.Equal("Error: Missing \"-C\" option to compile Spark component \n", Refused("-W", "spark:k7sp,2", "k7_hello.m").Message);

        // What passes R2025b's checks is a target JGraph builds later, or never; -l and -W lib are stage K11's (ADR 0267).
        Assert.Equal(MccTarget.CLibrary, Parse("-l", "k7_hello.m").Target);
        // .NET's MWArray API is stage K8's (ADR 0264): a class named after the assembly, framework 4.0;
        // the MATLAB Data API is K9's (ADR 0265), and remoting is out of scope.
        Assert.Equal(new DotNetWrapper("k7asm", "k7asm", "4.0", null), Parse("-W", "dotnet:k7asm", "k7_hello.m").DotNet);
        Assert.True(Parse("-W", "dotnet:k7asm,api=matlab-data", "k7_hello.m").DotNet!.DataApi);
        Assert.Equal("JGraph:mcc:targetOutOfScope", Refused("-W", "dotnet:k7asm,k7cls,5.0,Private,remote", "k7_hello.m").Identifier);
        Assert.Equal("JGraph:mcc:targetNotBuilt", Refused("-W", "CTF:k7ctf", "-U", "k7_hello.m").Identifier);
        Assert.Equal("JGraph:mcc:targetOutOfScope", Refused("-W", "java:k7pkg,k7cls", "k7_hello.m").Identifier);
        Assert.Equal("JGraph:mcc:targetOutOfScope", Refused("-W", "hadoop:k7hd", "k7_hello.m").Identifier);

        // A standalone wrapper ignores what it does not know after the name (W_main_extra, W_main_toomany, W_empty_item).
        Assert.Equal("k7me", Parse("-W", "main:k7me,bogus", "-T", "link:exe", "k7_hello.m").ComponentName);
        Assert.Equal("2.0.0.0", Parse("-W", "main:k7vt,version=1,version=2", "k7_hello.m").Version.ToString());
        Assert.Null(Parse("-W", "main:", "-T", "link:exe", "k7_hello.m").ComponentName);
        Assert.Equal(MccTarget.Windowed, Parse("-W", "WinMain", "-T", "link:exe", "k7_hello.m").Target);
        Assert.Equal(
            "Error: Multiple component names. Processing \"-o\" switch. Component name \"k7_first\" already specified via \"-o\".",
            Refused("-o", "k7_first", "-o", "k7_second", "-m", "k7_hello.m").Message);
    }

    [Fact]
    public void TheCommandLinesSyntaxIsR2025bs()
    {
        Assert.Equal(
            "Error: Found illegal whitespace character in command line option: \" \". \nThe strings on the left and right side of the space should be separate arguments \nto MCC.",
            Refused("-m k7_hello.m").Message);
        Assert.Equal("Error: The -o option requires a nonempty argument (e.g. \"-o <output file>\").", Refused("-o", "", "-m", "k7_hello.m").Message);
        Assert.Equal("Error: The -R option requires a nonempty argument (e.g. \"-R -nojvm -nojit\").", Refused("-R", "", "-m", "k7_hello.m").Message);
        Assert.True(MccCommandLine.Parse([]).ShowHelp); // no_args prints the help
        Assert.True(Parse("-m", "-?", "k7_hello.m").ShowHelp);
        Assert.True(Parse("-/?").ShowTraceHelp);
        MccCommandLine traced = Parse("-/t", "-mv", "k7_hello.m");
        Assert.True(traced.Trace);
        Assert.Equal(["-/t", "-m", "-v", "k7_hello.m"], traced.Processed);
        Assert.Equal("Compiler:compiler:COM_DBG_ERR_UNRECOGNIZED_OPTION", Refused("-/q", "-m", "k7_hello.m").Identifier);
    }

    [Fact]
    public void ABundleIsReplacedByItsSwitches()
    {
        File.WriteAllText(Path.Combine(_folder, "k7.bundle"), "-m -o k7_bundled");
        File.WriteAllText(Path.Combine(_folder, "k7p.bundle"), "-W main:%1% -T link:exe");
        File.WriteAllText(Path.Combine(_folder, "k7p2.bundle"), "-W main:%1%,version=%2%\n-T link:exe");
        File.WriteAllText(Path.Combine(_folder, "k7nest.bundle"), "-B k7.bundle -v");

        Assert.Equal("k7_bundled", Parse("-B", "k7.bundle", "k7_hello.m").OutputName);
        Assert.Equal("k7_param", Parse("-B", "k7p.bundle:k7_param", "k7_hello.m").ComponentName);
        MccCommandLine two = Parse("-B", "k7p2.bundle:k7_p2,1.2", "k7_hello.m");
        Assert.Equal(("k7_p2", "1.2.0.0"), (two.ComponentName, two.Version.ToString()));
        Assert.Null(Parse("-B", "k7p.bundle:", "k7_hello.m").ComponentName); // B_param_empty: main: with no name
        Assert.Equal("k7_a", Parse("-B", "k7p.bundle:k7_a,k7_b", "k7_hello.m").ComponentName); // B_too_many
        Assert.Equal("Error: Illegal bundle file replacement argument.", Refused("-B", "k7p.bundle", "k7_hello.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_BUNDLE_REPLACEMENT", Refused("-B", "k7p2.bundle:k7_few", "k7_hello.m").Identifier);
        Assert.Equal("Error: Unable to open file \"k7_nosuch.bundle\": No such file or directory.", Refused("-B", "k7_nosuch.bundle", "k7_hello.m").Message);
        MccCommandLine nested = Parse("-B", "k7nest.bundle", "k7_hello.m");
        Assert.Equal(("k7_bundled", true), (nested.OutputName, nested.Verbose));
        Assert.Equal(["-m", "-o", "k7_bundled", "-v", "k7_hello.m"], nested.Processed);

        File.WriteAllText(Path.Combine(_folder, "self.bundle"), "-B self.bundle");
        Assert.Equal("JGraph:mcc:bundleDepth", Refused("-B", "self.bundle", "k7_hello.m").Identifier);
    }

    [Fact]
    public void JGraphShipsR2025bsBundleNames()
    {
        foreach (string name in new[] { "csharedlib", "cpplib", "dotnet", "cjava", "ccom", "cexcel", "cmpsxl", "macro_option_m", "macro_option_e", "macro_option_g" })
        {
            Assert.True(File.Exists(Path.Combine(MccCommandLine.BundleFolder, name)), name);
        }

        Assert.Equal(MccTarget.Console, MccCommandLine.Parse(["-B", "macro_option_m", "k7_hello.m"], _folder).Target);
        MccCommandLine bundled = MccCommandLine.Parse(["-B", "csharedlib:k7blib", "k7_hello.m"], _folder);
        Assert.Equal((MccTarget.CLibrary, "k7blib", CLibraryStage.Link), (bundled.Target, bundled.ComponentName, bundled.LibraryStage));
        // Stage K12 builds the C++ library's generic interface; the bundle asks for both, as -W cpplib:name does.
        MccCommandLine cpp = MccCommandLine.Parse(["-B", "cpplib:k7bc", "k7_hello.m"], _folder);
        Assert.Equal((MccTarget.CppLibrary, "k7bc", CppInterfaceKind.All), (cpp.Target, cpp.ComponentName, cpp.CppInterface));
    }

    [Fact]
    public void TheWarningSwitchIsR2025bs()
    {
        // k7_switches and k7_more: the table's short names or any identifier with a colon; the words in any case.
        Assert.True(Parse("-w", "list").Warnings.List);
        Assert.True(Parse("-w", "LIST", "-m", "k7_hello.m").PrintsOnly);
        Assert.Equal("Error: 'bogus' is not a valid -w option.", Refused("-w", "bogus", "-m", "k7_hello.m").Message);
        Assert.Equal("Error: '-d' is not a valid -w option.", Refused("-m", "k7_hello.m", "-w", "-d", "out").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_WARN_OPTION", Refused("-w", "disable:COM_WARN_STARTUP_FILE_INCLUDED", "-m", "k7_hello.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_WARN_OPTION", Refused("-w", "disable:k7_nosuch_id", "-m", "k7_hello.m").Identifier);

        const string startup = "Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED";
        const string repeated = "Compiler:compiler:COM_WARN_REPEATED_FILE";
        MccWarnings w = Parse("-w", "disable:repeated_file", "-m", "k7_hello.m").Warnings;
        Assert.False(w.Shows(repeated));
        Assert.True(w.Shows(startup));
        w = Parse("-w", "error:" + repeated, "-m", "k7_hello.m").Warnings;
        Assert.True(w.Shows(repeated) && w.Fails(repeated) && !w.Fails(startup));
        w = Parse("-w", "DISABLE", "-m", "k7_hello.m").Warnings;
        Assert.False(w.Shows(startup));
        w = Parse("-w", "disable", "-w", "enable:" + startup, "-m", "k7_hello.m").Warnings; // w_dis_then_enable
        Assert.True(w.Shows(startup));
        w = Parse("-w", "error", "-w", "disable:" + startup, "-m", "k7_hello.m").Warnings; // w_err_then_dis
        Assert.True(!w.Shows(startup) && w.Fails(startup));
        w = Parse("-w", "off", "-w", "on", "-w", "enable", "-w", "disable:MATLAB:k7:anything", "-m", "k7_hello.m").Warnings;
        Assert.True(w.Shows(startup) && !w.Fails(startup));
        w = Parse("-w", "error:all_warnings", "-m", "k7_hello.m").Warnings;
        Assert.True(w.Fails(startup));
    }

    [Fact]
    public void TheWarningTableIsR2025bs()
    {
        // k7_switches: w_list, line for line; k7_more: w_list_dis_short and w_list_error.
        string table = Parse("-w", "list").Warnings.ListText();
        string[] lines = table.Split('\n');
        Assert.Equal("Warning table (for use with -w enable:/disable:/error:). Format is as follows: ", lines[0]);
        Assert.Equal("\"<WARNING IDENTIFIER>\" is <STATUS>, and corresponds to the message: <TEXT> ", lines[2]);
        Assert.Equal(new string('-', 80), lines[5]);
        Assert.Equal("Identifier \"all_warnings\" is enabled.", lines[7]);
        Assert.Equal("\"specified_file_mismatch\" is enabled, and corresponds to the message:", lines[11]);
        Assert.Equal("On Linux and Mac, the linker expects shared library names to begin with \"lib\".", lines[33]);
        Assert.Equal(new string('-', 80), lines[34]);
        Assert.Equal("  on %s.", lines[40]);
        Assert.Equal(45, lines.Length);

        Assert.Contains("\"repeated_file\" is disabled, and corresponds to the message:", Parse("-w", "disable:repeated_file", "-w", "list").Warnings.ListText());
        Assert.Contains("\"switch_ignored\" is set to throw an error,", Parse("-w", "error:switch_ignored", "-w", "list").Warnings.ListText());
        Assert.Contains("Identifier \"all_warnings\" is set to throw an error.", Parse("-w", "error", "-w", "list").Warnings.ListText());
    }
}
