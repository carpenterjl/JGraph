using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// The rest of mcc's switches in a build (compiler and SDK plan, stage K7), as R2025b's mcc answered
/// them (<c>tools/matlab-checklist/compiler-probes/recordings/k7_switches.out.txt</c>, <c>k7_more.out.txt</c>),
/// and <c>jgraph mcc</c> from the command line as R2025b's <c>mcc.bat</c> (<c>k7_cmd.out.txt</c>).
/// </summary>
[Collection("JG facade")]
public sealed class MccSwitchBuildTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k7b-" + Guid.NewGuid().ToString("N"))).FullName;

    public MccSwitchBuildTests()
    {
        JG.Reset();
        Put(Src, "k7_hello.m", "function k7_hello(varargin)\nfprintf('nargin\\t%d\\n', nargin);\nend\n");
        Put(Path.Combine(Src, "startup"), "k7_start.m", "function k7_start()\ndisp('start');\nend\n");
        Put(Path.Combine(Src, "startup"), "startup.m", "disp('startup');\n");
        Put(Src, "k7_data.txt", "data");
        Put(Src, "k7_bad.ico", "not an icon");
    }

    private string Src => Path.Combine(_root, "src");

    private string Out(string name) => Path.Combine(_root, "b", name);

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private static void Put(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private string[] Files(string folder) =>
        Directory.Exists(folder) ? [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).Order(StringComparer.Ordinal)] : [];

    /// <summary>Runs MATLAB code in a session over the sources' folder; answers what it printed and its last warning's identifier.</summary>
    private async Task<(string Printed, string Warning)> Session(string code)
    {
        _output.Normal.Clear();
        _output.Errors.Clear();
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(
            "lastwarn('', '');\n" + code + "\n[~, k7id] = lastwarn; fprintf('WARN=%s\\n', k7id);", sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        string text = _output.NormalText;
        int at = text.LastIndexOf("WARN=", StringComparison.Ordinal);
        return (text[..at], text[(at + 5)..].TrimEnd('\n'));
    }

    private static string Mcc(string folder, params string[] args) =>
        "try\n mcc(" + string.Join(", ", args.Append("-d").Append(folder).Select(static a => "'" + a.Replace("'", "''") + "'")) + ");\n disp('ok');\n"
        + "catch k7e\n disp(['ERR ' k7e.identifier]);\nend";

    [Fact]
    public async Task CodegenWritesTheArchiveAloneAndCompileWritesItBeside()
    {
        Assert.Contains("ok", (await Session(Mcc(Out("codegen"), "-m", "-T", "codegen", "k7_hello.m"))).Printed);
        Assert.Equal(
            ["buildresult.json", "includedSupportPackages.txt", "k7_hello.ctf", "mccExcludedFiles.log", "readme.txt", "requiredMCRProducts.txt", "unresolvedSymbols.txt"],
            Files(Out("codegen")));
        await Session(Mcc(Out("compile"), "-W", "main:k7tc", "-T", "compile:exe", "k7_hello.m"));
        Assert.Contains("k7_hello.ctf", Files(Out("compile")));
        Assert.Contains("k7_hello.exe", Files(Out("compile")));
    }

    [Fact]
    public async Task AFailedIconLeavesTheArchiveOnlyUnderK()
    {
        Assert.Contains("ERR Compiler:compiler:EMBED_ICON_FAILED", (await Session(Mcc(Out("noK"), "-m", "k7_hello.m", "-r", "k7_bad.ico"))).Printed);
        Assert.Empty(Files(Out("noK")));
        Assert.Contains("ERR Compiler:compiler:EMBED_ICON_FAILED", (await Session(Mcc(Out("K"), "-K", "-m", "k7_hello.m", "-r", "k7_bad.ico"))).Printed);
        Assert.Equal(
            ["buildresult.json", "includedSupportPackages.txt", "k7_hello.ctf", "mccExcludedFiles.log", "readme.txt", "requiredMCRProducts.txt", "unresolvedSymbols.txt"],
            Files(Out("K")));
    }

    [Fact]
    public async Task TheWarningsGoThroughTheWarningSwitch()
    {
        (string printed, string warning) = await Session(Mcc(Out("repeated"), "-m", "k7_hello.m", "k7_hello.m"));
        Assert.Equal("Compiler:compiler:COM_WARN_REPEATED_FILE", warning);
        Assert.Contains($"Warning: The file \"{Path.Combine(Src, "k7_hello.m")}\" was repeated on the command line.", _output.ErrorText);
        Assert.Contains("ok", printed);

        _ = await Session(Mcc(Out("silenced"), "-w", "disable:repeated_file", "-m", "k7_hello.m", "k7_hello.m"));
        Assert.DoesNotContain("repeated", _output.ErrorText);

        // A startup.m beside the main is compiled in and warned about (w_start); -w error fails the build after it (w_error).
        (_, warning) = await Session(Mcc(Out("startup"), "-m", Path.Combine(Src, "startup", "k7_start.m")));
        Assert.Equal("Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED", warning);
        (printed, _) = await Session(Mcc(Out("starterr"), "-w", "error", "-m", Path.Combine(Src, "startup", "k7_start.m")));
        Assert.Contains("ERR Compiler:compiler:COM_ERR_WARNING_TREATED_AS_ERROR", printed);
        Assert.Contains("Warning: Your deployed application may fail", _output.ErrorText);
        Assert.Empty(Files(Out("starterr")));

        (_, warning) = await Session(Mcc(Out("ignored"), "-g", "-m", "k7_hello.m"));
        Assert.Equal("Compiler:compiler:COM_WARN_SWITCH_IGNORED", warning);
        Assert.Contains("Warning: The option -g is ignored in standalone target mode (specify -? for help).", _output.ErrorText);
    }

    [Fact]
    public async Task WhatIsNotCodeAndAMissingFolderAreRefusedAndNotesPrinted()
    {
        Assert.Contains("ERR Compiler:compiler:COM_ERROR_INCLUDE_FILE_WITHOUT_DASH_A", (await Session(Mcc(Out("nonm"), "-m", "k7_hello.m", "k7_data.txt"))).Printed);
        Assert.Contains("ERR Compiler:compiler:DEPFUN_ERR_INVALID_DIR_NAME", (await Session(Mcc(Out("inc"), "-I", "k7_nosuchdir", "-m", "k7_hello.m"))).Printed);
        Assert.Contains("ERR Compiler:compiler:COM_ERR_M_FILE_NOT_FOUND", (await Session(Mcc(Out("end"), "-m", "k7_hello.m", "-M"))).Printed);
        (string printed, _) = await Session(Mcc(Out("noted"), "-O", "-m", "k7_hello.m"));
        Assert.StartsWith("The -O switch is no longer supported; the MATLAB JIT accelerator makes \nCompiler optimizations unnecessary. \n\n", printed);
        Assert.Contains("Warning table (for use with -w enable:/disable:/error:).", (await Session("mcc -w list")).Printed);
    }

    [Fact]
    public void TheCommandLineIsMccBats()
    {
        // k7_cmd: a build, a refusal on standard error between R2025b's markers, and single quotes taken off.
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, MccCommand.Run(["-W", "'main:k7q'", "-T", "link:exe", "k7_hello.m", "-d", Out("cmd")], Src, output, error));
        Assert.Contains("k7_hello.exe", Files(Out("cmd")));
        Assert.Equal("", error.ToString());

        error = new StringWriter();
        Assert.Equal(1, MccCommand.Run(["k7_hello.m"], Src, output, error));
        Assert.Equal(
            "{\bUnable to determine application type, since no wrapper function was specified.\nPlease use the -W switch or specify application type via -m or -l.\n"
            + "Type 'mcc -?' for further assistance.\n}\b \nERROR: JGraph error Exit Status: 0x00000001\n",
            error.ToString());

        output = new StringWriter();
        Assert.Equal(0, MccCommand.Run(["-m", Path.Combine(Src, "startup", "k7_start.m"), "-d", Out("cmdstart")], Src, output, new StringWriter()));
        Assert.StartsWith("[\bWarning: Your deployed application may fail because file or folder paths\n", output.ToString());
        Assert.EndsWith("paths, and recompile your application.\n]\b \n", output.ToString());

        output = new StringWriter();
        Assert.Equal(0, MccCommand.Run([], Src, output, new StringWriter()));
        Assert.StartsWith("mcc  Build MATLAB code into a program that runs on the JGraph Runtime.", output.ToString());
        Assert.Equal(StartupMode.Mcc, StartupCommandLine.Parse(["mcc", "-m", "x.m"]).Mode);
        Assert.Equal(["-m", "x.m"], StartupCommandLine.Parse(["mcc", "-m", "x.m"]).ProgramArguments);
    }

    [Fact]
    public async Task CopyfileTakesFoldersAndWildcards()
    {
        // probe_k7_switches copies its sources so; R2025b's copyfile takes both.
        _ = await Session("copyfile('*.m', 'copied'); copyfile('startup', fullfile('copied', 'startup'));");
        Assert.Equal(["k7_hello.m", Path.Combine("startup", "k7_start.m"), Path.Combine("startup", "startup.m")], Files(Path.Combine(Src, "copied")));
    }
}
