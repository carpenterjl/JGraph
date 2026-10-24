using JGraph.Api;
using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Standalone programs built from a session (compiler and SDK plan, stage K6): <c>mcc</c> and
/// <c>compiler.build</c> as R2025b's answer (<c>tools/matlab-checklist/compiler-probes/recordings/k6_*</c>),
/// and the programs they build run as the JGraph Runtime runs them, in process.
/// </summary>
[Collection("JG facade")]
public sealed class StandaloneBuildTests : IDisposable
{
    private static readonly IScriptEngine[] Engines = [new JgsScriptEngine(), new MatlabScriptEngine()];

    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k6-" + Guid.NewGuid().ToString("N"))).FullName;

    public StandaloneBuildTests() => JG.Reset();

    private string Src => Path.Combine(_root, "src");

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

    private string Put(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private void Hello() => Put(Src, "k6_hello.m", """
        function k6_hello(varargin)
        fprintf('nargin\t%d\n', nargin);
        for k = 1:nargin
            fprintf('arg%d\t%s\n', k, varargin{k});
        end
        fprintf('isdeployed\t%d\n', isdeployed);
        fprintf('end\n');
        end
        """);

    /// <summary>Runs MATLAB code in an ordinary session whose current folder is the sources', and fails with its errors if it fails.</summary>
    private async Task Session(string code)
    {
        Directory.CreateDirectory(Src);
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>Runs a built program as <c>jgraph --deployed program -- args</c> does, from a run folder, with a cache of its own.</summary>
    private async Task<int> Run(string program, params string[] arguments)
    {
        string run = Directory.CreateDirectory(Path.Combine(_root, "run")).FullName;
        string? before = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        try
        {
            JG.Reset();
            StartupOptions options = StartupCommandLine.Parse(["--deployed", program, "-sd", run, "--", .. arguments]);
            Assert.False(options.HasUsageError, options.UsageError);
            return await BatchRunner.RunAsync(options, Engines, _output, static (_, _) => { });
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", before);
        }
    }

    [Fact]
    public async Task MccBuildsAConsoleProgramThatRunsItsMain()
    {
        Hello();
        await Session("mcc -m k6_hello.m -d out");
        string folder = Path.Combine(Src, "out");
        Assert.Equal(
            ["buildresult.json", "includedSupportPackages.txt", "k6_hello.exe", "mccExcludedFiles.log", "readme.txt", "requiredMCRProducts.txt", "unresolvedSymbols.txt"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        using (CtfArchiveReader archive = CtfArchiveReader.Open(Path.Combine(folder, "k6_hello.exe")))
        {
            Assert.Equal(CtfTargetType.StandaloneApplication, archive.Manifest.TargetType);
            Assert.Equal(["k6_hello"], archive.Manifest.MatlabPath); // the file is under the folder mcc ran in
        }

        // k6_console: hello.args.
        _output.Normal.Clear();
        Assert.Equal(0, await Run(Path.Combine(folder, "k6_hello.exe"), "a", "b c"));
        Assert.Equal(["nargin\t2", "arg1\ta", "arg2\tb c", "isdeployed\t1", "end"], _output.NormalLines);
    }

    [Fact]
    public async Task MccSearchesTheMainsOwnFolderAndPlacesFilesByWhereItRuns()
    {
        // k6_roots: built from elsewhere, the helper and the data beside the main come along, under their full path.
        string app = Path.Combine(_root, "app");
        Put(app, "k6r_main.m", "function k6r_main\nk6r_helper();\ndisp(fileread('k6r_data.txt'));\nend\n");
        Put(app, "k6r_helper.m", "function k6r_helper\ndisp('helper');\nend\n");
        Put(app, "k6r_data.txt", "data");
        await Session($"mcc('-m', '{Path.Combine(app, "k6r_main.m")}', '-d', 'out')");
        string program = Path.Combine(Src, "out", "k6r_main.exe");
        using (CtfArchiveReader archive = CtfArchiveReader.Open(program))
        {
            string place = app[(Path.GetPathRoot(app)!.Length)..].Replace('\\', '/');
            Assert.Equal(
                [place + "/k6r_data.txt", place + "/k6r_helper.m", place + "/k6r_main.m"],
                archive.Manifest.Files.Select(f => f.Path).Where(p => !p.StartsWith('.')).Order(StringComparer.Ordinal));
            Assert.Equal(place, archive.Manifest.MatlabPath[0]);
        }

        _output.Normal.Clear();
        Assert.Equal(0, await Run(program));
        Assert.Equal(["helper", "data"], _output.NormalLines);
    }

    [Fact]
    public async Task MccRefusesInR2025bsWords()
    {
        Hello();
        Put(Src, "k6_icon.png", "not really a png");
        await Session("""
            function check(f, id, message)
                try
                    f();
                    error('k6:none', 'no error');
                catch e
                    assert(strcmp(e.identifier, id), e.identifier);
                    assert(strcmp(e.message, message), e.message);
                end
            end

            check(@() mcc('-m', 'k0_nosuchfile.m'), 'Compiler:compiler:COM_ERR_M_FILE_NOT_FOUND', ...
                'Could not find the MATLAB file ''k0_nosuchfile.m'' on the MATLAB path.');
            check(@() mcc('-m', 'k6_hello.m', '-h', 'k6_nosuch.txt'), 'Compiler:compiler:COM_ERR_HELP_FILE_NOT_EXIST', ...
                sprintf('The help file \n  ''k6_nosuch.txt''\ndoes not exist.'));
            check(@() mcc('-m', 'k6_hello.m', '-r', 'k6_icon.png'), 'Compiler:compiler:EMBED_ICON_FAILED', ...
                'Failed to embed icon file "k6_icon.png" - Error reading icon file.');
            check(@() mcc('-m', 'k6_hello.m', '-r', 'k6_nosuch.ico'), 'Compiler:compiler:EMBED_ICON_FAILED', ...
                'Failed to embed icon file "k6_nosuch.ico" - Failed to open file ReadIconFromICOFile with error File not found..');
            check(@() mcc('-m', 'k6_hello.m', '-o', 'k6 spaced'), 'Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME', ...
                sprintf('Illegal output name: ''k6 spaced'' \n (standalone and shared library target output names must begin with a letter or ''_'' and contain only alpha-numeric characters or ''_'')'));
            check(@() mcc('-t', 'k6_hello.m'), 'Compiler:compiler:COM_ERR_MISSING_WRAPPER', ...
                sprintf('Unable to determine application type, since no wrapper function was specified. \nPlease use the -W switch or specify application type via -m or -l. \nType ''mcc -?'' for further assistance.'));
            """);
    }

    [Fact]
    public async Task TheOptionsAreR2025bs()
    {
        Hello();
        await Session("""
            o = compiler.build.StandaloneApplicationOptions('k6_hello.m');
            assert(strcmp(class(o), 'compiler.build.StandaloneApplicationOptions'));
            assert(strcmp(o.ExecutableName, 'k6_hello'));
            assert(strcmp(o.OutputDir, '.\k6_hellostandaloneApplication'));
            assert(strcmp(o.ExecutableVersion, '1.0.0.0'));
            assert(strcmp(strrep(o.ExecutableIcon, matlabroot, '<M>'), '<M>\toolbox\compiler\packagingResources\default_icon_48.png'));
            assert(strcmp(strrep(o.ExecutableSplashScreen, matlabroot, '<M>'), '<M>\toolbox\compiler\packagingResources\default_splash.png'));
            assert(isfile(o.ExecutableIcon) && isfile(o.ExecutableSplashScreen));
            assert(strcmp(o.AppFile, fullfile(pwd, 'k6_hello.m')));
            assert(o.EmbedArchive == 'on' && o.TreatInputsAsNumeric == 'off' && o.Verbose == 'off');
            assert(isequal(o.SupportPackages, {'autodetect'}) && isempty(o.AdditionalFiles));
            assert(isequal(size(o.ExternalEncryptionKey), [0 0]) && isstruct(o.ExternalEncryptionKey));

            o.ExecutableVersion = '70000';   % not checked until the build (build_side)
            assert(strcmp(o.ExecutableVersion, '70000'));
            o.ExecutableName = 'two words';  % a blank is allowed, mcc refuses it later
            assert(strcmp(o.ExecutableName, 'two words'));
            assert(strcmp(o.OutputDir, '.\two_wordsstandaloneApplication'));
            o.TreatInputsAsNumeric = true;
            assert(o.TreatInputsAsNumeric == 'on');
            o.EmbedArchive = 0;
            assert(o.EmbedArchive == 'off');
            o.SupportPackages = {};
            assert(isequal(o.SupportPackages, {'none'}));
            o.AdditionalFiles = {'k6_hello.m'};
            assert(isequal(o.AdditionalFiles, {fullfile(pwd, 'k6_hello.m')}));
            o2 = compiler.build.StandaloneApplicationOptions('k6_hello.m', 'ExecutableName', 'named', 'outputdir', 'built');
            assert(strcmp(o2.ExecutableName, 'named') && strcmp(o2.OutputDir, fullfile(pwd, 'built')));

            function check(f, id, message)
                try
                    f();
                    error('k6:none', 'no error');
                catch e
                    assert(strcmp(e.identifier, id), e.identifier);
                    assert(strcmp(e.message, message), e.message);
                end
            end

            function assign(name, value)
                o = compiler.build.StandaloneApplicationOptions('k6_hello.m');
                o.(name) = value;
            end

            check(@() assign('ExecutableName', 'a/b'), 'Compiler:clishared:badProperty', 'Invalid ''ExecutableName'': ''a/b''');
            check(@() assign('ExecutableName', ''), 'MATLAB:validators:mustBeNonzeroLengthText', 'Value must be text with one or more characters.');
            check(@() assign('OutputDir', 'a|b'), 'Compiler:clishared:badProperty', 'Invalid ''OutputDir'': ''a|b''');
            check(@() assign('TreatInputsAsNumeric', 'maybe'), 'MATLAB:validation:UnableToConvert', ...
                'Error setting property ''TreatInputsAsNumeric'' of class ''compiler.build.StandaloneApplicationOptions''. ''maybe'' is invalid. Value must be ''off'' or ''on''.');
            check(@() assign('Verbose', 'loud'), 'MATLAB:validation:UnableToConvert', ...
                'Error setting property ''Verbose'' of class ''compiler.internal.build.AllOptions''. ''loud'' is invalid. Value must be ''off'' or ''on''.');
            check(@() assign('AdditionalFiles', 'k0_nosuch_data.mat'), 'Compiler:utils:shared:cannotFindFileOrFolder', ...
                sprintf('Cannot find file or folder:\n\nk0_nosuch_data.mat'));
            check(@() assign('ExternalEncryptionKey', struct('EncryptionKeyFile', 'x')), 'Compiler:build:externalencryptionkey:badFields', ...
                '"ExternalEncryptionKey" must be a struct with exactly two fields named "EncryptionKeyFile" and "RuntimeKeyLoaderFile".');
            check(@() assign('ExecutableIcon', 'k0_nosuch.png'), 'Compiler:utils:shared:cannotFindFile', sprintf('Cannot find file:\n\nk0_nosuch.png'));
            check(@() assign('ExecutableSplashScreen', 'k6_hello.m'), 'Compiler:utils:shared:notAnImage', ...
                sprintf('The file:\n\n%s\n\nis not an image file.', fullfile(pwd, 'k6_hello.m')));
            check(@() compiler.build.StandaloneApplicationOptions('k0_nosuch.m'), 'Compiler:utils:shared:cannotFindFile', sprintf('Cannot find file:\n\nk0_nosuch.m'));
            check(@() compiler.build.StandaloneApplicationOptions('k6_hello.m', 'NoSuchOption', 1), 'MATLAB:InputParser:UnmatchedParameter', ...
                '''NoSuchOption'' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.');
            check(@() compiler.build.standaloneApplication(compiler.build.StandaloneApplicationOptions('k6_hello.m'), 'Verbose', 'on'), ...
                'MATLAB:narginchk:tooManyInputs', 'Too many input arguments.');
            check(@() compiler.build.standaloneApplication(), 'MATLAB:narginchk:notEnoughInputs', 'Not enough input arguments.');
            """);
    }

    [Fact]
    public async Task ABuildAnswersItsResultsAndWritesItsCommandWhenVerbose()
    {
        Hello();
        await Session("""
            o = compiler.build.StandaloneApplicationOptions('k6_hello.m', 'OutputDir', 'api_m', 'ExecutableName', 'k6_api', ...
                'ExecutableVersion', '3.4', 'Verbose', 'on');
            r = compiler.build.standaloneApplication(o);
            assert(strcmp(r.BuildType, 'standaloneApplication'));
            assert(isequal(r.Files, {fullfile(pwd, 'api_m', 'k6_api.exe'); fullfile(pwd, 'api_m', 'readme.txt')}));
            assert(isempty(r.IncludedSupportPackages) && iscell(r.IncludedSupportPackages));
            assert(strcmp(class(r.Options), 'compiler.build.StandaloneApplicationOptions'));
            assert(strcmp(class(r.RuntimeDependencies), 'compiler.runtime.Dependencies'));
            assert(isequal(r.RuntimeDependencies.Required.ID, 35010));
            assert(isequal(size(r.RuntimeDependencies.Optional), [0 0]));
            assert(isstring(r.BuildID) && isa(r.BuildDuration, 'double'));
            """);

        // k6_build, api_m.printed: the command line first, in R2025b's writers' order, then mcc's own lines.
        string command = _output.NormalLines[0];
        Assert.StartsWith($"mcc -W 'main:k6_api,version=3.4' -T link:exe -d {Path.Combine(Src, "api_m")} -v -Z autodetect -o k6_api -r ", command);
        Assert.EndsWith($"\\icon.ico {Path.Combine(Src, "k6_hello.m")}", command);
        Assert.Equal("Analyzing file dependencies. ", _output.NormalLines[2]);
        Assert.Equal(3, System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(Src, "api_m", "k6_api.exe")).FileMajorPart);
    }

    [Fact]
    public async Task AWindowedBuildWritesItsSplashBesideTheProgram()
    {
        Hello();
        await Session("""
            r = compiler.build.standaloneWindowsApplication('k6_hello.m', 'OutputDir', 'api_e');
            assert(strcmp(r.BuildType, 'standaloneWindowsApplication'));
            d = fullfile(pwd, 'api_e');
            assert(isequal(r.Files, {fullfile(d, 'k6_hello.exe'); fullfile(d, 'splash.png'); fullfile(d, 'readme.txt')}));
            info = imfinfo(fullfile(d, 'splash.png'));
            assert(info.Width == 400 && info.Height == 400);
            """);
        using CtfArchiveReader archive = CtfArchiveReader.Open(Path.Combine(Src, "api_e", "k6_hello.exe"));
        Assert.Equal(CtfTargetType.WindowsStandaloneApplication, archive.Manifest.TargetType);
    }

    [Fact]
    public async Task ASeparateArchiveIsExtractedBesideItsProgram()
    {
        // K0, env_separate_ctf: ctfroot is <exedir>\<name>_mcr.
        Put(Src, "k6_sep.m", "function k6_sep\nfprintf('%s\\n', ctfroot);\nend\n");
        await Session("mcc -m k6_sep.m -C -d out");
        string folder = Path.Combine(Src, "out");
        Assert.True(File.Exists(Path.Combine(folder, "k6_sep.ctf")));
        _output.Normal.Clear();
        Assert.Equal(0, await Run(Path.Combine(folder, "k6_sep.exe")));
        Assert.Equal([Path.Combine(folder, "k6_sep_mcr")], _output.NormalLines);
        Assert.True(File.Exists(Path.Combine(folder, "k6_sep_mcr", "k6_sep", "k6_sep.m")));
    }

    [Fact]
    public async Task ADeployedSessionHasNoCompiler()
    {
        // R2025b's runtime ships neither mcc nor compiler.build.
        Put(Src, "k6_nomcc.m", "function k6_nomcc\nfprintf('%d %d\\n', exist('mcc'), exist('compiler.build.standaloneApplication'));\nend\n");
        await Session("mcc -m k6_nomcc.m -d out");
        _output.Normal.Clear();
        Assert.Equal(0, await Run(Path.Combine(Src, "out", "k6_nomcc.exe")));
        Assert.Equal(["0 0"], _output.NormalLines);
    }
}
