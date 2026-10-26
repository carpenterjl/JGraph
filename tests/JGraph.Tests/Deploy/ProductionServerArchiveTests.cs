using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// A Production Server archive's command line, discovery and routes files, and generated files
/// (compiler and SDK plan, stage K14) against what R2025b's mcc -W CTF and
/// compiler.build.productionServerArchive answered and wrote (<c>recordings/k14_build.out.txt</c>,
/// <c>probe_k14_routes</c>, the fixture <c>k14_production_server_archive</c>): the forms and refusals,
/// the discovery file's checks in R2025b's words, the routes file's checks in R2025b's words, and the
/// archive's manifest and entries. The readme is in JGraph's words (ADR 0270).
/// </summary>
public sealed class ProductionServerArchiveTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k14-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp folder clears it.
        }
    }

    private string Put(string name, string text)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private MccCommandLine Parse(params string[] args) => MccCommandLine.Parse(args, _root);

    private MccException Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args, _root));

    private const string Signatures =
        """{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a","type":["double","size=1,1"],"purpose":"The first addend."},{"name":"b","type":["double"],"purpose":"The second addend."}],"outputs":[{"name":"c","type":["double"],"purpose":"Their sum."}],"purpose":"Add two values."}}""";

    [Fact]
    public void TheCommandLineReadsCtfAsR2025bDoes()
    {
        Put("k14add.m", "function c = k14add(a, b)\nc = a + b;\nend\n");
        string sig = Put("k14sig.json", Signatures);
        string routes = Put("routes.json", """{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"component":"k14m","function":"k14add"}}]}""");

        // k14_build: mcc_default, mcc_lower, mcc_discovery, mcc_routes (with the archive's own name), mcc_twice, mcc_ofirst.
        MccCommandLine plain = Parse("-W", "CTF:k14m", "-U", "k14add.m");
        Assert.Equal(MccTarget.ProductionServerArchive, plain.Target);
        Assert.Equal("k14m", plain.ComponentName);
        Assert.True(plain.SeparateArchive);
        Assert.Null(plain.Signatures);
        Assert.Null(plain.Routes);
        Assert.Equal("k14lc", Parse("-W", "ctf:k14lc", "-U", "k14add.m").ComponentName);
        MccCommandLine described = Parse("-W", "CTF:k14m,DISCOVERY:k14sig.json,ROUTES:routes.json", "-U", "k14add.m");
        Assert.Equal(sig, described.FunctionSignaturesFile);
        Assert.Equal(routes, described.RoutesFilePath);
        Assert.Equal(["k14add"], described.Signatures!.Functions.Keys);
        Assert.Equal("^/k14/add$", described.Routes!.Routes.Single().Match);
        Assert.Equal("k14t2", Parse("-W", "CTF:k14t1", "-W", "CTF:k14t2", "-U", "k14add.m").ComponentName);
        Assert.Equal("k14of2", Parse("-o", "k14of", "-W", "CTF:k14of2", "-U", "k14add.m").ComponentName);
        Assert.Equal("1bad", Parse("-W", "CTF:1bad", "-U", "k14add.m").ComponentName);

        // -T, -C, -g, -v, -X, -n and -j change nothing an archive is made of (mcc_codegen, mcc_separate, mcc_debug, mcc_nodata, mcc_numeric, mcc_pcode).
        foreach (string[] args in new[]
        {
            new[] { "-W", "CTF:k14cg", "-U", "-T", "codegen", "k14add.m" },
            ["-W", "CTF:k14sp", "-U", "-C", "k14add.m"],
            ["-W", "CTF:k14db", "-U", "-g", "k14add.m"],
            ["-W", "CTF:k14vb", "-U", "-v", "k14add.m"],
            ["-W", "CTF:k14te", "-U", "-T", "link:exe", "k14add.m"],
            ["-W", "CTF:k14tl", "-U", "-T", "link:lib", "k14add.m"],
        })
        {
            MccCommandLine line = Parse(args);
            Assert.Equal(MccTarget.ProductionServerArchive, line.Target);
            Assert.DoesNotContain("g", line.IgnoredSwitches);
        }

        Assert.True(Parse("-W", "CTF:k14nm", "-U", "-n", "k14add.m").NumericArguments);
        Assert.True(Parse("-W", "CTF:k14pc", "-U", "-j", "k14add.m").PCode);

        // -U alone: R2025b writes a nameless .ctf; JGraph names the archive after the first file (ADR 0270).
        MccCommandLine alone = Parse("-U", "k14add.m");
        Assert.Equal(MccTarget.ProductionServerArchive, alone.Target);
        Assert.Equal("k14add", alone.ComponentName);

        // The refusals (mcc_nou, mcc_noname, mcc_nocolon, mcc_dash, mcc_dot, mcc_space, mcc_bogus_part, mcc_extra_part,
        // mcc_version, mcc_disc_*, mcc_routes_*, mcc_u_m, mcc_u_lib, mcc_withpython, mcc_runtime, mcc_o).
        Assert.Equal("Compiler:compiler:COM_ERROR_MISSING_U_WITH_W_CTF", Refused("-W", "CTF:k14nu", "k14add.m").Identifier);
        foreach (string bad in new[] { "CTF:", "CTF", "CTF:a-b", "CTF:a.b", "CTF:a b" })
        {
            MccException refused = Refused("-W", bad, "-U", "k14add.m");
            Assert.Equal("Compiler:compiler:COM_ERR_BAD_WRAPPER_SYNTAX", refused.Identifier);
            Assert.Equal($"The -W option (\"{bad}\") is not correctly formed.", refused.Message);
        }

        Assert.Equal("The -W option (\"BOGUS:x\") is not correctly formed.", Refused("-W", "CTF:k14bp,BOGUS:x", "-U", "k14add.m").Message);
        Assert.Equal("The -W option (\"extra\") is not correctly formed.", Refused("-W", "CTF:k14ep,extra", "-U", "k14add.m").Message);
        Assert.Equal("The -W option (\"version=1.0\") is not correctly formed.", Refused("-W", "CTF:k14vr,version=1.0", "-U", "k14add.m").Message);
        Assert.Equal("The -W option (\"DISCOVERY\") is not correctly formed.", Refused("-W", "CTF:k14dn,DISCOVERY", "-U", "k14add.m").Message);
        MccException missing = Refused("-W", "CTF:k14dm,DISCOVERY:nosuch.json", "-U", "k14add.m");
        Assert.Equal("Compiler:compiler:COM_ERR_MPS_DISCOVERY_FILE_NOT_FOUND", missing.Identifier);
        Assert.Equal("Could not find the MATLAB Production Server discovery file 'nosuch.json'.", missing.Message);
        Assert.Equal("Compiler:compiler:COM_ERR_MPS_DISCOVERY_FILE_NOT_SPECIFIED", Refused("-W", "CTF:k14de,DISCOVERY:", "-U", "k14add.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MPS_MULTIPLE_DISCOVERY", Refused("-W", "CTF:k14d2,DISCOVERY:k14sig.json,DISCOVERY:k14sig.json", "-U", "k14add.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MPS_ROUTES_FILE_NOT_FOUND", Refused("-W", "CTF:k14rm,ROUTES:nosuch.json", "-U", "k14add.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MPS_ROUTES_FILE_NOT_SPECIFIED", Refused("-W", "CTF:k14re,ROUTES:", "-U", "k14add.m").Identifier);
        foreach (string[] args in new[]
        {
            new[] { "-m", "-U", "k14add.m" },
            ["-W", "lib:k14ul", "-U", "k14add.m"],
            ["-W", "CTF:k14wp", "-W", "python:k14wp2", "-U", "k14add.m"],
        })
        {
            MccException refused = Refused(args);
            Assert.Equal("Compiler:compiler:COM_ERR_INVALID_SWITCH_LIB_OR_STANDALONE", refused.Identifier);
            Assert.Equal("Error: The option -U is invalid in library or standalone application mode (specify -? for help).", refused.Message);
        }

        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_SWITCH_LIB_OR_COMP", Refused("-W", "CTF:k14ro", "-U", "-R", "-nojvm", "k14add.m").Identifier);
        MccException named = Refused("-W", "CTF:k14on", "-U", "-o", "k14other", "k14add.m");
        Assert.Equal("Compiler:compiler:COM_ERR_MULTIPLE_COMPONENT_NAMES", named.Identifier);
        Assert.Equal("Error: Multiple component names. Processing \"-o\" switch. Component name \"k14on\" already specified via \"-W\".", named.Message);

        // A discovery file that is not a discovery file is refused at the -W (mcc_disc_badtype); its missing
        // schema version is a warning (mcc_disc_noschema); a routes file's fault is a warning before the refusal (mcc_routes_nomatch).
        Put("badtype.json", """{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a","type":["nosuchtype"]},{"name":"b"}],"outputs":[{"name":"c"}]}}""");
        MccException badType = Refused("-W", "CTF:k14dt,DISCOVERY:badtype.json", "-U", "k14add.m");
        Assert.Equal("Compiler:compiler:COM_ERROR_SIGNATURE_PARSE", badType.Identifier);
        Assert.Equal("Error: Function signature file \"badtype.json\" could not be parsed. In function signature \"k14add\":\nProperty \"type\" has an invalid value: \"nosuchtype\".\n", badType.Message);
        Put("noschema.json", """{"k14add":{"inputs":[{"name":"a"},{"name":"b"}],"outputs":[{"name":"c"}]}}""");
        MccCommandLine warned = Parse("-W", "CTF:k14dh,DISCOVERY:noschema.json", "-U", "k14add.m");
        Assert.Equal(
            [("Compiler:compiler:COM_MESG_SIGNATURE_PARSE", "While parsing function signature file \"noschema.json\": Schema version property '_schemaVersion' was not found in the function signatures file.")],
            warned.ParseWarnings);
        Put("nomatch.json", """{"version":"1.0.0","pathmap":[{"webhandler":{"function":"k14add"}}]}""");
        MccException noMatch = Refused("-W", "CTF:k14ra,ROUTES:nomatch.json", "-U", "k14add.m");
        Assert.Equal("Compiler:compiler:COM_ERROR_ROUTES_PARSE", noMatch.Identifier);
        Assert.Equal("Error: Routes file \"nomatch.json\" could not be parsed. Please fix errors and run again.", noMatch.Message);
        Assert.Equal(("Compiler:compiler:COM_MESG_ROUTES_PARSE", "While parsing routes file \"nomatch.json\": Required property \"match\" not found. (at .pathmap[0].match)"), noMatch.WarningFirst);
    }

    [Fact]
    public void ADiscoveryFileIsCheckedInR2025bsWords()
    {
        // The fixture k14_production_server_archive's sig_* rows: the file's shape, then its functions against the archive's.
        CtfFunction[] functions = [new CtfFunction("k14f", "k14f.m", ["x", "n"], ["a", "b"])];
        string Fault(string json, bool check = true)
        {
            try
            {
                FunctionSignatures read = FunctionSignatures.Parse(json);
                if (check)
                {
                    read.Check(functions);
                }

                return "ok" + (read.Warnings.Count > 0 ? " / warn " + string.Join(" | ", read.Warnings) : "");
            }
            catch (SignatureException fault)
            {
                return fault.Message;
            }
        }

        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double"],"purpose":"x"},{"name":"n","type":[],"purpose":""}],"outputs":[{"name":"a"},{"name":"b"}],"purpose":"A probe function."}}"""));
        Assert.Equal("ok / warn Schema version property '_schemaVersion' was not found in the function signatures file.", Fault("""{"k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.0.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("MATLAB Production Server discovery schema version '9.0.0' is not supported.\n", Fault("""{"_schemaVersion":"9.0.0","k14f":{"inputs":[],"outputs":[]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"colour\" is not recognized.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","colour":"red"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"type\" has an invalid value: \"nosuchtype\".\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["nosuchtype"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nAttribute \"size\" requires at least 2 values.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","size=x"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"inputs\" has an invalid number of arguments: 1. Specify 2.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("Function signature \"k14nosuch\" was defined but is not contained in the deployed archive.\n", Fault("""{"_schemaVersion":"1.1.0","k14nosuch":{"inputs":[{"name":"x"}],"outputs":[{"name":"a"}]}}"""));
        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.1.0","_typedefs":{"pt":{"purpose":"a point","type":"struct","fields":[{"name":"x","type":"double"},{"name":"y","type":"double"}]}},"k14f":{"inputs":[{"name":"x","type":"struct:pt"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nstruct \"nosuch\" was referenced but not defined.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":"struct:nosuch"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("invalid conversion: JSON array to JSON object", Fault("[1,2]"));
        Assert.Equal("ok / warn Schema version property '_schemaVersion' was not found in the function signatures file.", Fault("{}"));
        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.1.0"}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"name\" has an invalid MATLAB variable name: \"1x\".\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"1x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"name\" is missing.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"type":["double"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"outputs\" is missing.\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}]}}"""));
        Assert.Equal("In function signature \"k14f\":\nProperty \"inputs\" must be of type \"JSON array\".\n", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":{"name":"x"},"outputs":[]}}"""));
        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","size=1,1"]},{"name":"n","type":["double","size=1,:"]}],"outputs":[{"name":"a","type":["double","size=:,:"]},{"name":"b"}]}}"""));
        Assert.Equal("ok", Fault("""{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","single","int8","uint8","int16","uint16","int32","uint32","int64","uint64","logical","char","string","cell","struct"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}"""));
        Assert.Equal("Function signature \"pathmap\" was defined but is not contained in the deployed archive.\n", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/k14/f$","webhandler":{"component":"k14f","function":"k14f"}}]}"""));

        // The JSON fault the fixture met, in R2025b's parser's words (sig_notjson).
        Assert.StartsWith("JSON ParserException (E0008): Expected leading '\"' character while parsing value of type string (Line 1, Column 12)\n{\"this is\": not json\n", Fault("{\"this is\": not json\n"), StringComparison.Ordinal);

        // What the manifest keeps: the described functions' help and typed parameters.
        FunctionSignatures described = FunctionSignatures.Parse(Signatures);
        CtfBuild build = described.ApplyTo(new CtfBuild
        {
            ComponentName = "k14m",
            Files = [],
            PublicFunctions = [new CtfFunction("k14add", "k14m/k14add.m", ["a", "b"], ["c"])],
        });
        CtfFunction add = build.PublicFunctions.Single();
        Assert.Equal("Add two values.", add.Help);
        Assert.Equal([new CtfSignatureParameter("a", "double", "[1,1]", "The first addend."), new CtfSignatureParameter("b", "double", "", "The second addend.")], add.Signature!.Inputs);
        Assert.Equal([new CtfSignatureParameter("c", "double", "", "Their sum.")], add.Signature.Outputs);
    }

    [Fact]
    public void ARoutesFileIsCheckedInR2025bsWords()
    {
        // probe_k14_routes: the component named must be the archive's; version is not checked; a function need not exist.
        string Fault(string json)
        {
            try
            {
                return "ok " + RoutesFile.Parse(json, "k14r").Routes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (RoutesException fault)
            {
                return fault.Message;
            }
        }

        Assert.Equal("ok 1", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"component":"k14r","function":"k14add"}}]}"""));
        Assert.Equal("ok 1", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"function":"k14add"}}]}"""));
        Assert.Equal("ok 2", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"function":"k14add"}},{"match":"^/k14/multi/.*","webhandler":{"component":"k14r","function":"k14multi"}}]}"""));
        Assert.Equal("ok 1", Fault("""{"pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}}]}"""));
        Assert.Equal("ok 1", Fault("""{"version":"2.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}}]}"""));
        Assert.Equal("ok 1", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14nosuch"}}]}"""));
        Assert.Equal("ok 0", Fault("""{"version":"1.0.0","pathmap":[]}"""));
        Assert.Equal("Required property \"match\" not found. (at .pathmap[0].match)", Fault("""{"version":"1.0.0","pathmap":[{"webhandler":{"function":"k14add"}}]}"""));
        Assert.StartsWith("\"match\" entry has invalid regular expression. (at .pathmap[0].match). reason: ", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a[$","webhandler":{"function":"k14add"}}]}"""), StringComparison.Ordinal);
        Assert.Equal("Unknown key(s) detected: [ \"extra\" ]. Must be one of: [ \"match\", \"webhandler\" ]. (at .pathmap[0])", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"},"extra":1}],"more":2}"""));
        Assert.Equal("Unknown key(s) detected: [ \"other\" ]. Must be one of: [ \"component\", \"function\" ]. (at .pathmap[0])", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add","other":"x"}}]}"""));
        Assert.Equal("Required property \"match\" must be a String. (at .pathmap[0].match)", Fault("""{"version":"1.0.0","pathmap":[{"match":5,"webhandler":{"function":"k14add"}}]}"""));
        Assert.Equal("Required property \"function\" must be a String. (at .pathmap[0].function)", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":5}}]}"""));
        Assert.Equal("webhandler entry is not an Object. (at .pathmap[0])", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":"k14add"}]}"""));
        Assert.Equal("Required property \"webhandler\" not found. (at .pathmap[0].webhandler)", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$"}]}"""));
        Assert.Equal("Required property \"function\" not found. (at .pathmap[0].function)", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"k14r"}}]}"""));
        Assert.Equal("Optional property \"component\" if present must be the same value as \"k14r\" . (at .pathmap[0])", Fault("""{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"other","function":"k14add"}}]}"""));
        Assert.Equal("The top-level JSON value must be an Object.", Fault("[1,2]"));
        Assert.Equal("Required property \"pathmap\" not found. (at .pathmap)", Fault("{}"));
        Assert.Equal("Top-level property \"pathmap\" must be an array. (at .pathmap).", Fault("""{"version":"1.0.0","pathmap":{"match":"^/a$","webhandler":{"function":"k14add"}}}"""));
        Assert.Equal("JSON syntax error: status=Unexpected end of input (line: 2, column: 0)", Fault("{\"version\":\n"));
    }

    [Fact]
    public void TheBuildWritesTheArchiveAndAReadme()
    {
        string source = Put("k14add.m", "function c = k14add(a, b)\nc = a + b;\nend\n");
        var build = new CtfBuild
        {
            ComponentName = "k14r",
            Files = [(source, "k14r/k14add.m")],
            MatlabPath = ["k14r"],
            PublicFunctions = [new CtfFunction("k14add", "k14r/k14add.m", ["a", "b"], ["c"])],
        };
        build = FunctionSignatures.Parse(Signatures).ApplyTo(build);
        RoutesFile routes = RoutesFile.Parse("""{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"component":"k14r","function":"k14add"}}]}""" + "\n", "k14r");
        string folder = Path.Combine(_root, "out");
        var written = new List<string>();
        ProductionServerArchiveBuild built = ProductionServerArchive.Write(build, folder, "k14r", routes, written.Add);

        // R2025b's results list the archive alone (k14_build: main.Files), and -v names the readme after it.
        Assert.Equal([Path.Combine(folder, "k14r.ctf")], built.Files);
        Assert.Equal([Path.Combine(folder, "readme.txt")], written);
        Assert.Equal(CtfTargetType.ProductionServerArchive, built.Manifest.TargetType);
        Assert.Null(built.Manifest.MainFunction);
        string readme = File.ReadAllText(Path.Combine(folder, "readme.txt"));
        Assert.StartsWith("k14r for the JGraph Production Server\r\n", readme, StringComparison.Ordinal);
        Assert.Contains("auto_deploy", readme, StringComparison.Ordinal);

        // The archive carries the routes file where R2025b's does and the signatures in its manifest (probe_k14_routes: r_ok; k14_out: mcc_discovery).
        using CtfArchiveReader archive = CtfArchiveReader.Open(built.Files[0]);
        Assert.Contains(archive.Manifest.Files, static f => f.Path == RoutesFile.ArchivePath);
        CtfFunction add = archive.Manifest.PublicFunctions.Single();
        Assert.Equal("Add two values.", add.Help);
        Assert.Equal("[1,1]", add.Signature!.Inputs[0].Size);
        Assert.Equal("The second addend.", add.Signature.Inputs[1].Help);
    }
}
