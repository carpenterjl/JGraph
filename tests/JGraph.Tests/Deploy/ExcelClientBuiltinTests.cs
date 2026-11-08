using JGraph.Api;
using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The Excel client's builtins (compiler and SDK plan, stage K17f) against what R2025b's answered
/// (<c>recordings/k17_excel.out.txt</c>: copts.*, cset.*, cbuild.*, the client builds, mpsxl_*,
/// popts.*, pset.*, pkg.*): <c>compiler.build.ExcelClientForProductionServerOptions</c> and
/// <c>compiler.build.excelClientForProductionServer</c> from a server archive's results and from files,
/// <c>mcc -W mpsxl</c> with its <c>class{}</c> group written as the switches mean,
/// <c>compiler.package.ExcelClientForProductionServerOptions</c> and the installer.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ExcelClientBuiltinTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k17fb-" + Guid.NewGuid().ToString("N"))).FullName;

    public ExcelClientBuiltinTests()
    {
        JG.Reset();
        File.WriteAllText(Path.Combine(_root, "k17_one.m"), "function y = k17_one(x)\n%K17_ONE Doubles its one input (compiler and SDK plan, stage K17).\ny = 2 * x;\nend\n");
        File.WriteAllText(Path.Combine(_root, "k17_none.m"), "function k17_none()\n%K17_NONE Takes and gives nothing.\nend\n");
        File.WriteAllText(Path.Combine(_root, "k17_many.m"), "function [s, d, p] = k17_many(a, b, c)\ns = a + b + c; d = a - b - c; p = a * b * c;\nend\n");
        File.WriteAllText(Path.Combine(_root, "k17data.txt"), "data");
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
            // A DLL still mapped; the temp folder clears it.
        }
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

    private string Slash(string relative) => Path.Combine(_root, relative).Replace("\\", "/", StringComparison.Ordinal);

    /// <summary>A server archive built first, as the probe builds k17arch: its results in <c>rArch</c> and the archive's path in <c>archive</c>.</summary>
    private string Archive => $"rArch = compiler.build.productionServerArchive({{'k17_one.m', 'k17_none.m', 'k17_many.m'}}, 'ArchiveName', 'k17arch', 'OutputDir', '{Slash("arch")}'); archive = '{Slash("arch/k17arch.ctf")}'; ";

    [Fact]
    public async Task TheOptionsDeriveAndCheckAsR2025bsDo()
    {
        // copts.results, copts.files, copts.derived, copts.noargs, copts.onefile, copts.addinresults, copts.missingarchive, copts.txtarchive, copts.noext.
        Assert.Equal("k17_one | k17_oneClass | .\\k17_oneexcelClientForProductionServer | on",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); disp([o.AddInName ' | ' o.ClassName ' | ' o.OutputDir ' | ' char(o.GenerateVisualBasicFile)])"));
        Assert.Equal("k17_one | 3",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions(rArch); disp([o.AddInName ' | ' num2str(numel(o.FunctionFiles))])"));
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said("compiler.build.ExcelClientForProductionServerOptions();"));
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said("compiler.build.ExcelClientForProductionServerOptions('k17_one.m');"));
        Assert.Equal("ERR CompilerSDK:ExcelClientForProductionServerOptions:notAProductionServerResult | The result object passed as input is not from the function compiler.build.productionServerArchive.",
            await Said($"r = compiler.build.excelAddIn('k17_one.m', 'AddInName', 'k17ra', 'OutputDir', '{Slash("ra")}'); compiler.build.ExcelClientForProductionServerOptions(r);"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  / nosuch.ctf",
            await Said("compiler.build.ExcelClientForProductionServerOptions('k17_one.m', 'nosuch.ctf');"));
        Assert.Equal("ERR CompilerSDK:ExcelClientForProductionServerOptions:notACTFFile | The specified input is not a CTF file: k17data.txt /  /",
            await Said("compiler.build.ExcelClientForProductionServerOptions('k17_one.m', 'k17data.txt');"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  / " + Path.Combine(_root, "arch", "k17arch"),
            await Said(Archive + $"compiler.build.ExcelClientForProductionServerOptions('k17_one.m', '{Path.Combine(_root, "arch", "k17arch")}');"));
        Assert.Equal("k17nv", await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions(rArch, 'AddInName', 'k17nv', 'ReplaceExcelBlankWithNaN', 'on'); disp(o.AddInName)"));
        Assert.Equal("ERR MATLAB:InputParser:ParamMissingValue | No value was given for 'AddInName'. Name-value pair arguments require a name followed by a value.",
            await Said(Archive + "compiler.build.ExcelClientForProductionServerOptions(rArch, 'AddInName');"));
        Assert.Equal("ERR MATLAB:InputParser:UnmatchedParameter | 'Bogus' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.",
            await Said(Archive + "compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive, 'Bogus', 1);"));
        Assert.Equal("ERR MATLAB:narginchk:tooManyInputs | Too many input arguments.",
            await Said(Archive + "many = repmat({'Verbose', 'on'}, 1, 20); compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive, many{:});"));

        // cset.*: the switches, the names, the version, the files.
        Assert.Equal("ERR MATLAB:validation:UnableToConvert | Error setting property 'ReplaceExcelBlankWithNaN' of class 'compiler.build.ExcelClientForProductionServerOptions'. 'x' is invalid. Value must be 'off' or 'on'.",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.ReplaceExcelBlankWithNaN = 'x';"));
        Assert.Equal("on | off", await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.ReplaceNaNToZeroInExcel = 1; o.GenerateVisualBasicFile = 'off'; disp([char(o.ReplaceNaNToZeroInExcel) ' | ' char(o.GenerateVisualBasicFile)])"));
        Assert.Equal("My Client | a b | abc", await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.AddInName = 'My Client'; o.ClassName = 'a b'; o.AddInVersion = 'abc'; disp([o.AddInName ' | ' o.ClassName ' | ' o.AddInVersion])"));
        Assert.Equal("ERR MATLAB:validators:mustBeNonzeroLengthText | Value must be text with one or more characters.",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.ServerArchive = '';"));
        Assert.Equal("ERR MATLAB:invalidType | Expected ServerArchive to be one of these types: /  / char, string /  / Instead its type was double.",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.ServerArchive = 5;"));
        Assert.Equal("ERR MATLAB:expectedNonempty | Expected FunctionFiles to be nonempty.",
            await Said(Archive + "o = compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive); o.FunctionFiles = {};"));

        // cbuild.*: the build's own refusals.
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said("compiler.build.excelClientForProductionServer('k17_one.m');"));
        Assert.Equal("ERR MATLAB:narginchk:tooManyInputs | Too many input arguments.",
            await Said(Archive + "compiler.build.excelClientForProductionServer(compiler.build.ExcelClientForProductionServerOptions('k17_one.m', archive), 'Verbose', 'on');"));
    }

    [Fact]
    public async Task ABuildWritesTheClientAndListsItsFiles()
    {
        // fromresults.Files, fromresults.BuildType: the DLL and the module; nobas.Files: the DLL alone, the module written all the same.
        string c = Slash("c");
        // (The tests register nothing, so R2025b's per-user registration warning, which a build outside them raises, is not here.)
        Assert.Equal("excelClientForProductionServer | k17mps | K17Mps | 3\n<OUT>/k17mps.dll | <OUT>/k17mps.bas | <OUT>/k17mps.xlam",
            await Said(Archive + $"r = compiler.build.excelClientForProductionServer(rArch, 'AddInName', 'k17mps', 'ClassName', 'K17Mps', 'OutputDir', '{c}'); "
                + $"disp([r.BuildType ' | ' r.Options.AddInName ' | ' r.Options.ClassName ' | ' num2str(numel(r.Files))]); "
                + $"disp(strjoin(strrep(strrep(r.Files(:)', '{c.Replace("/", "\\", StringComparison.Ordinal)}', '<OUT>'), '\\', '/'), ' | '))"));
        foreach (string file in new[] { "k17mps.dll", "k17mps.bas", "k17mps.xlam", "readme.txt", "buildresult.json", "includedSupportPackages.txt" })
        {
            Assert.True(File.Exists(Path.Combine(_root, "c", file)), file + " was not written");
        }

        Assert.Equal("{\r\n\t\"RuntimeDependencies\": []\r\n}\r\n", File.ReadAllText(Path.Combine(_root, "c", "buildresult.json")));
        Assert.StartsWith("k17mps Deployable Archive with Excel Integration\r\n", File.ReadAllText(Path.Combine(_root, "c", "readme.txt")), StringComparison.Ordinal);
        string nb = Slash("nb");
        Assert.Equal("1\n<OUT>/k17cnb.dll",
            await Said(Archive + $"r = compiler.build.excelClientForProductionServer({{'k17_none.m', 'k17_one.m'}}, archive, 'AddInName', 'k17cnb', 'ClassName', 'K17Cnb', 'GenerateVisualBasicFile', 'off', 'OutputDir', '{nb}'); "
                + $"disp(num2str(numel(r.Files))); disp(strjoin(strrep(strrep(r.Files(:)', '{nb.Replace("/", "\\", StringComparison.Ordinal)}', '<OUT>'), '\\', '/'), ' | '))"));
        Assert.True(File.Exists(Path.Combine(_root, "nb", "k17cnb.bas")));

        // The switches reach the DLL's configuration, and the mcc line carries the class{} group as R2025b's writer means it (its three slips not copied).
        string sw = Slash("sw");
        _output.Mark();
        ScriptRunResult verbose = await Run(Archive + $"compiler.build.excelClientForProductionServer({{'k17_none.m', 'k17_one.m'}}, archive, 'AddInName', 'k17all', 'ClassName', 'K17All', "
            + $"'ReplaceExcelBlankWithNaN', 'on', 'ConvertExcelDateToString', 'on', 'ReplaceNaNToZeroInExcel', 'on', 'ConvertNumericOutToDateInExcel', 'on', 'Verbose', 'on', 'OutputDir', '{sw}');");
        Assert.True(verbose.Success, verbose.Message + _output.ErrorText);
        string printed = _output.TextSinceMark;
        string none = Path.Combine(_root, "k17_none.m");
        string one = Path.Combine(_root, "k17_one.m");
        Assert.Contains($"mcc -W 'mpsxl:k17all,K17All,version=1.0.0.0' -T link:lib -d {Path.Combine(_root, "sw")} -v -b {none} {one} 'class{{K17All:{none} -replaceBlankWithNaN -convertDateToString -replaceNaNWithZero -convertNumericToDate,{one} -replaceBlankWithNaN -convertDateToString -replaceNaNWithZero -convertNumericToDate}}'",
            printed, StringComparison.Ordinal);
        string configuration = System.Text.Encoding.UTF8.GetString(PeResources.Read(Path.Combine(_root, "sw", "k17all.dll"), 10, ExcelClient.ConfigurationResource)!);
        Assert.Contains("Function=k17_none:blank,date,nan,numdate\nFunction=k17_one:blank,date,nan,numdate\n", configuration, StringComparison.Ordinal);

        // cvtext: the version is mcc's to refuse.
        Assert.Equal("ERR Compiler:compiler:COM_ERR_INVALID_VERSION | Version string \"abc\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.",
            await Said(Archive + $"compiler.build.excelClientForProductionServer('k17_one.m', archive, 'AddInName', 'k17cvt', 'AddInVersion', 'abc', 'OutputDir', '{Slash("vt")}');"));
    }

    [Fact]
    public async Task MccReadsMpsxlAsR2025bDoes()
    {
        // mpsxl_default, mpsxl_noversion, mpsxl_noclass, mpsxl_bundle, mpsxl_debug, mpsxl_notarget: each builds the client.
        string x = Slash("x");
        Assert.Equal("", await Said($"mcc('-W', 'mpsxl:k17x,K17X,version=1.0', '-T', 'link:lib', '-b', '-d', '{x}', 'k17_one.m');"));
        Assert.Equal(new[] { "buildresult.json", "includedSupportPackages.txt", "k17x.bas", "k17x.dll", "k17x.xlam", "readme.txt" }, Directory.GetFiles(Path.Combine(_root, "x")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(string.Join("\r\n", ExcelClientTests.Recorded("k17_excel.out.txt", "mpsxl_default.file.k17x.bas")) + "\r\n", File.ReadAllText(Path.Combine(_root, "x", "k17x.bas")));
        MccCommandLine noClass = MccCommandLine.Parse(["-W", "mpsxl:k17xnc", "-T", "link:lib", "k17_one.m"], _root);
        Assert.Equal((MccTarget.ExcelClient, "k17xnc", "k17xnc"), (noClass.Target, noClass.ComponentName, noClass.ExcelClassName));
        MccCommandLine bundled = MccCommandLine.Parse(["-B", "cmpsxl:k17xb,K17Xb,1.0", "k17_one.m"], _root);
        Assert.Equal((MccTarget.ExcelClient, "k17xb", "K17Xb", new ProgramVersion(1, 0, 0, 0)), (bundled.Target, bundled.ComponentName, bundled.ExcelClassName, bundled.Version));
        Assert.True(MccCommandLine.Parse(["-W", "mpsxl:k17xd,K17Xd,version=1.0", "-T", "link:lib", "-g", "k17_one.m"], _root).DebugBuild);
        Assert.Equal(MccTarget.ExcelClient, MccCommandLine.Parse(["-W", "mpsxl:k17xnt,K17Xnt,version=1.0", "k17_one.m"], _root).Target);

        // mpsxl_empty, and the class{} group (mpsxl_group: the files named go among the files, each with its switches; mpsxl_badswitch: an unknown switch passed over).
        MccException empty = Assert.Throws<MccException>(() => MccCommandLine.Parse(["-W", "mpsxl:", "-T", "link:lib", "k17_one.m"], _root));
        Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_COMPONENT_NAME", empty.Identifier);
        MccCommandLine grouped = MccCommandLine.Parse(["-W", "mpsxl:k17xg,K17Xg,version=1.0", "-T", "link:lib", "-b", "k17_none.m", "k17_one.m",
            "class{K17Xg:k17_none.m -replaceBlankWithNaN -convertDateToString,k17_one.m -replaceNaNWithZero -convertNumericToDate}"], _root);
        Assert.Equal(["k17_none.m", "k17_one.m", "k17_none.m", "k17_one.m"], grouped.Files);
        Assert.Equal(new ExcelClientSwitches(true, true, false, false), grouped.ExcelClientSwitches["k17_none.m"]);
        Assert.Equal(new ExcelClientSwitches(false, false, true, true), grouped.ExcelClientSwitches["k17_one.m"]);
        Assert.Equal(ExcelClientSwitches.None, MccCommandLine.Parse(["-W", "mpsxl:k17xbs,K17Xbs,version=1.0", "-T", "link:lib", "k17_one.m", "class{K17Xbs:k17_one.m -noSuchSwitch}"], _root).ExcelClientSwitches["k17_one.m"]);
        string g = Slash("g");
        Assert.Equal("warn Compiler:compiler:COM_WARN_REPEATED_FILE", await Said($"mcc('-W', 'mpsxl:k17xg,K17Xg,version=1.0', '-T', 'link:lib', '-b', '-d', '{g}', 'k17_none.m', 'k17_one.m', 'class{{K17Xg:k17_none.m -replaceBlankWithNaN -convertDateToString,k17_one.m -replaceNaNWithZero -convertNumericToDate}}');"));
        Assert.Equal(string.Join("\r\n", ExcelClientTests.Recorded("k17_excel.out.txt", "mpsxl_group.file.k17xg.bas")) + "\r\n", File.ReadAllText(Path.Combine(_root, "g", "k17xg.bas")));
        string configuration = System.Text.Encoding.UTF8.GetString(PeResources.Read(Path.Combine(_root, "g", "k17xg.dll"), 10, ExcelClient.ConfigurationResource)!);
        Assert.Contains("Function=k17_none:blank,date\nFunction=k17_one:nan,numdate\n", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePackagingOptionsAndTheInstallerFollowR2025b()
    {
        string c = Slash("pc");
        string build = Archive + $"rClient = compiler.build.excelClientForProductionServer(rArch, 'AddInName', 'k17mps', 'ClassName', 'K17Mps', 'OutputDir', '{c}'); "
            + $"clientDll = '{Slash("pc/k17mps.dll")}'; clientBas = '{Slash("pc/k17mps.bas")}'; ";

        // popts.results, popts.files, popts.properties, popts.noargs, popts.onearg, popts.addinresults, popts.archresults, popts.missingdll, popts.txtdll, popts.txtarchive.
        Assert.Equal("k17mpsExcelClientInstaller | .\\k17mpsExcelClientInstaller | 750 | 60 | 1.0 | k17mps.dll",
            await Said(build + "p = compiler.package.ExcelClientForProductionServerOptions(rClient); [~, n, e] = fileparts(p.ClientLibrary); disp([char(p.InstallerName) ' | ' p.OutputDir ' | ' num2str(p.MaxResponseSize) ' | ' num2str(p.ServerTimeOut) ' | ' p.Version ' | ' n e])"));
        Assert.Equal("ServerArchive | ClientLibrary | InstallerName | InstallerIcon | MaxResponseSize | ServerTimeOut | ServerURL | SSLCertificate | Version | OutputDir",
            await Said(build + "disp(strjoin(properties(compiler.package.ExcelClientForProductionServerOptions(clientDll, archive))', ' | '))"));
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said("compiler.package.ExcelClientForProductionServerOptions();"));
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said(build + "compiler.package.ExcelClientForProductionServerOptions(clientDll);"));
        Assert.Equal("ERR CompilerSDK:ExcelClientForProductionServerOptions:notAnExcelClientForProductionServerResult | The result object passed as input is not from the function compiler.build.excelClientForProductionServer.",
            await Said(build + "compiler.package.ExcelClientForProductionServerOptions(rArch);"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  / nosuch.dll", await Said(build + "compiler.package.ExcelClientForProductionServerOptions('nosuch.dll', archive);"));
        Assert.Equal("ERR Compiler:clishared:badProperty | Invalid 'ClientLibrary': '" + Path.Combine(_root, "k17data.txt") + "'",
            await Said(build + "compiler.package.ExcelClientForProductionServerOptions('k17data.txt', archive);"));
        Assert.Equal("ERR Compiler:clishared:badProperty | Invalid 'ServerArchive': '" + Path.Combine(_root, "k17data.txt") + "'",
            await Said(build + "compiler.package.ExcelClientForProductionServerOptions(clientDll, 'k17data.txt');"));

        // pset.*: Version, ServerTimeOut, MaxResponseSize, ServerURL, SSLCertificate, InstallerName, InstallerIcon, OutputDir.
        string owner = "compiler.package.ExcelClientForProductionServerOptions";
        string po = build + "po = compiler.package.ExcelClientForProductionServerOptions(clientDll, archive); ";
        Assert.Equal("2.1.3 |  1.0", await Said(po + "po.Version = '2.1.3'; a = po.Version; po.Version = ' 1.0'; disp([a ' | ' po.Version])"));
        Assert.Equal("ERR Compiler:clishared:badProperty | Invalid 'Version': 'a.b'", await Said(po + "po.Version = 'a.b';"));
        Assert.Equal("ERR Compiler:clishared:badProperty | Invalid 'Version': '1.0-beta'", await Said(po + "po.Version = '1.0-beta';"));
        Assert.Equal($"ERR MATLAB:validators:mustBeNonempty | Error setting property 'Version' of class '{owner}'. Value must not be empty.", await Said(po + "po.Version = '';"));
        Assert.Equal("ERR MATLAB:invalidType | Expected Version to be one of these types: /  / char, string /  / Instead its type was double.", await Said(po + "po.Version = 3;"));
        Assert.Equal("5", await Said(po + "po.ServerTimeOut = 5; disp(num2str(po.ServerTimeOut))"));
        Assert.Equal($"ERR MATLAB:validators:mustBePositive | Error setting property 'ServerTimeOut' of class '{owner}'. Value must be positive.", await Said(po + "po.ServerTimeOut = 0;"));
        Assert.Equal($"ERR MATLAB:validators:mustBeInteger | Error setting property 'ServerTimeOut' of class '{owner}'. Value must be integer.", await Said(po + "po.ServerTimeOut = 1.5;"));
        Assert.Equal($"ERR MATLAB:validators:mustBeNumericOrLogical | Error setting property 'ServerTimeOut' of class '{owner}'. Value must be numeric or logical.", await Said(po + "po.ServerTimeOut = '5';"));
        Assert.Equal($"ERR MATLAB:validators:mustBeInteger | Error setting property 'ServerTimeOut' of class '{owner}'. Value must be integer.", await Said(po + "po.ServerTimeOut = Inf;"));
        Assert.Equal("0 | -1 | Inf | NaN", await Said(po + "po.MaxResponseSize = 0; a = po.MaxResponseSize; po.MaxResponseSize = -1; b = po.MaxResponseSize; po.MaxResponseSize = Inf; c = po.MaxResponseSize; po.MaxResponseSize = NaN; disp([num2str(a) ' | ' num2str(b) ' | ' num2str(c) ' | ' num2str(po.MaxResponseSize)])"));
        Assert.Equal($"ERR MATLAB:validators:mustBeNumeric | Error setting property 'MaxResponseSize' of class '{owner}'. Value must be numeric.", await Said(po + "po.MaxResponseSize = '10';"));
        Assert.Equal("nonsense | https://h:1", await Said(po + "po.ServerURL = 'nonsense'; a = po.ServerURL; po.ServerURL = \"https://h:1\"; disp([a ' | ' po.ServerURL])"));
        Assert.Equal($"ERR MATLAB:validators:mustBeTextScalar | Error setting property 'ServerURL' of class '{owner}'. Value must be a character vector or string scalar.", await Said(po + "po.ServerURL = 5;"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  / nosuch.pem", await Said(po + "po.SSLCertificate = 'nosuch.pem';"));
        Assert.Equal("0 | 0", await Said(po + "po.SSLCertificate = ''; a = numel(po.SSLCertificate); po.SSLCertificate = \"\"; disp([num2str(a) ' | ' num2str(numel(po.SSLCertificate))])"));
        Assert.Equal($"ERR MATLAB:validators:mustBeTextScalar | Error setting property 'SSLCertificate' of class '{owner}'. Value must be a character vector or string scalar.", await Said(po + "po.SSLCertificate = 5;"));
        Assert.Equal("a b | 1abc", await Said(po + "po.InstallerName = 'a b'; a = po.InstallerName; po.InstallerName = '1abc'; disp([a ' | ' po.InstallerName])"));
        Assert.Equal("ERR MATLAB:validators:mustBeNonzeroLengthText | Value must be text with one or more characters.", await Said(po + "po.InstallerName = '';"));
        Assert.Equal("ERR MATLAB:invalidType | Expected InstallerName to be one of these types: /  / char, string /  / Instead its type was double.", await Said(po + "po.InstallerName = 5;"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  / nosuch.png", await Said(po + "po.InstallerIcon = 'nosuch.png';"));
        Assert.Equal("ERR Compiler:utils:shared:cannotFindFile | Cannot find file: /  /", await Said(po + "po.InstallerIcon = '';"));
        Assert.Equal(Path.Combine(_root, "rel"), await Said(po + "po.OutputDir = 'rel'; disp(po.OutputDir)"));
        Assert.Equal($"ERR MATLAB:validators:mustBeNonempty | Error setting property 'OutputDir' of class '{owner}'. Value must not be empty.", await Said(po + "po.OutputDir = '';"));

        // pkg.default, pkg.named, pkg.files, pkg.nolibrary (its warning), pkg.optionsplus, pkg.addinresults, pkg.noargs, pkg.nooutdir.
        if (ExcelClientInstaller.DefaultTemplatePath is null)
        {
            return;
        }

        Assert.Equal("", await Said(build + $"compiler.package.excelClientForProductionServer(rClient, 'OutputDir', '{Slash("p/default")}');"));
        Assert.True(File.Exists(Path.Combine(_root, "p", "default", "k17mpsExcelClientInstaller.exe")));
        string installer = Path.Combine(_root, "p", "default", "k17mpsExcelClientInstaller.exe");
        string configuration = System.Text.Encoding.UTF8.GetString(PeResources.Read(installer, 10, ExcelClientInstaller.ConfigurationResource)!);
        Assert.Equal("CTFFile=k17arch.ctf\nCTFArchive=k17arch.ctf\nAutomationAddinDll=k17mps.dll\nresponseSizeLimit=750\ntimeoutSeconds=60\nAutomationAddin=k17arch\n", configuration);
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(PeResources.Read(installer, 10, ExcelClientInstaller.BundleResource)!)))
        {
            Assert.Equal(["k17mps.dll", "k17mps.bas", "k17mps.xlam", "jgxlconfig.dll"], zip.Entries.Select(static e => e.FullName));
        }

        Assert.Equal("", await Said(build + $"compiler.package.excelClientForProductionServer(rClient, 'ServerURL', 'http://localhost:9910', 'MaxResponseSize', 10, 'ServerTimeOut', 5, 'Version', '2.1', 'InstallerName', 'K17Setup', 'OutputDir', '{Slash("p/named")}');"));
        string named = Path.Combine(_root, "p", "named", "K17Setup.exe");
        Assert.Contains("responseSizeLimit=10\ntimeoutSeconds=5\nAutomationAddin=k17arch\nServerInfo=http://localhost:9910\n",
            System.Text.Encoding.UTF8.GetString(PeResources.Read(named, 10, ExcelClientInstaller.ConfigurationResource)!), StringComparison.Ordinal);
        Assert.Equal("2.1.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(named).FileVersion);
        Assert.Equal("warn CompilerSDK:ExcelClientForProductionServerOptions:missingClientLibraryInPackagedFiles",
            await Said(build + $"compiler.package.excelClientForProductionServer({{clientBas}}, clientDll, archive, 'OutputDir', '{Slash("p/nolibrary")}');"));
        Assert.Equal("", await Said(build + $"compiler.package.excelClientForProductionServer({{clientDll, clientBas}}, clientDll, archive, 'OutputDir', '{Slash("p/files")}');"));
        Assert.Equal("ERR Compiler:package:shared:onlyOptions | No other name-value arguments can be used with the 'Options' parameter.",
            await Said(build + "compiler.package.excelClientForProductionServer(rClient, 'Options', compiler.package.ExcelClientForProductionServerOptions(rClient), 'Version', '2');"));
        Assert.Equal("ERR CompilerSDK:ExcelClientForProductionServerOptions:notAnExcelClientForProductionServerResult | The result object passed as input is not from the function compiler.build.excelClientForProductionServer.",
            await Said(build + $"compiler.package.excelClientForProductionServer(rArch, 'OutputDir', '{Slash("p/addin")}');"));
        Assert.Equal("ERR MATLAB:narginchk:notEnoughInputs | Not enough input arguments.", await Said("compiler.package.excelClientForProductionServer();"));
        Assert.Equal("", await Said(build + "compiler.package.excelClientForProductionServer(rClient);"));
        Assert.True(File.Exists(Path.Combine(_root, "k17mpsExcelClientInstaller", "k17mpsExcelClientInstaller.exe")));
    }
}
