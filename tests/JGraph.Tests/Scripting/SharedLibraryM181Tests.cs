using System.Globalization;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 8 of the .NET and shared-library interop plan (ADR 0181): <c>loadlibrary</c> and the prototype
/// model. The header parser is held to the prototype files R2025b wrote — for <c>jgtestlib.h</c> and for
/// <c>probe_shrlib_parse.h</c> — read from the committed preprocessor output, so it needs no compiler.
/// The header route end to end needs one, and those tests are skipped, with the reason, where none is found.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SharedLibraryM181Tests
{
    private static readonly string Interop = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop");
    private static readonly string Library = Path.Combine(Interop, "jgtestlib.dll");
    private static readonly string Header = Path.Combine(Interop, "jgtestlib.h");
    private static readonly string Parse = Path.Combine(Interop, "parse");

    // ------------------------------------------------------------------------------------------
    // The parser against R2025b's prototype files
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TheParserRecordsWhatR2025bRecordsForTheTestLibrary()
    {
        CHeaderParser.Result parsed = CHeaderParser.Parse(File.ReadAllText(Path.Combine(Interop, "jgtestlib.msvc.i")), ["jgtestlib.h"], "jgtestlib.h");
        AssertSameModel(ReadPrototypeText(File.ReadAllText(Path.Combine(Interop, "jgtestlib_proto.m"))), parsed.Model);
    }

    [Fact]
    public void TheParserRecordsWhatR2025bRecordsForTheProbeHeader()
    {
        // Typedef'd tags, anonymous structs, enum expressions, wchar_t, long double, __stdcall, arrays,
        // externs, a struct no function uses, #pragma pack(2): every one as R2025b's own file has it.
        CHeaderParser.Result parsed = CHeaderParser.Parse(
            File.ReadAllText(Path.Combine(Parse, "probe_shrlib_parse.msvc.i")), ["probe_shrlib_parse.h"], "probe_shrlib_parse.h");
        AssertSameModel(ReadPrototypeText(File.ReadAllText(Path.Combine(Parse, "probe_shrlib_parse_proto.m"))), parsed.Model);
    }

    [Fact]
    public void AddheaderRecordsAnIncludedHeaderByItsNameWithOrWithoutItsExtension()
    {
        string text = File.ReadAllText(Path.Combine(Parse, "probe_shrlib_parse.msvc.i"));
        foreach (string added in new[] { "probe_shrlib_parse_inc", "probe_shrlib_parse_inc.h" })
        {
            LibraryModel model = CHeaderParser.Parse(text, ["probe_shrlib_parse.h", added], "probe_shrlib_parse.h").Model;
            Assert.Equal(54, model.Functions.Count); // R2025b: 52 + the included header's two
            Assert.Equal(["q_inc_fn", "q_inc_ptr"], model.Functions.Take(2).Select(f => f.Name));
            Assert.Equal(["INC_S"], model.Functions[0].Rhs);
            Assert.NotNull(model.Struct("INC_S"));
        }

        Assert.DoesNotContain(CHeaderParser.Parse(text, ["probe_shrlib_parse.h"], "x").Model.Functions, f => f.Name == "q_inc_fn");
    }

    [Fact]
    public void TheWarningsTextNamesWhatR2025bNamesInTheNamedHeader()
    {
        CHeaderParser.Result parsed = CHeaderParser.Parse(File.ReadAllText(Path.Combine(Interop, "jgtestlib.msvc.i")), ["jgtestlib.h"], "jgtestlib.h");
        Assert.StartsWith("jgtestlib.h\n", parsed.Warnings, StringComparison.Ordinal);
        Assert.Contains("Failed to parse type 'union jg_union { int i ; float f ; } jg_union' original input 'union jg_union { int i ; float f ; } jg_union'", parsed.Warnings, StringComparison.Ordinal);
        Assert.Contains("Type 'jg_union' was not found.  Defaulting to type error.", parsed.Warnings, StringComparison.Ordinal);
        Assert.Contains("Type 'jg_bitsPtr' was not found.  Defaulting to type voidPtr.", parsed.Warnings, StringComparison.Ordinal);
        Assert.Contains("Error parsing argument for function jg_varsum function may be invalid.", parsed.Warnings, StringComparison.Ordinal);
        Assert.Matches(@"Found on line \d+ of input from line 188 of file .*jgtestlib\.h", parsed.Warnings);
        Assert.DoesNotContain("corecrt", parsed.Warnings, StringComparison.Ordinal); // system headers are not the named header

        CHeaderParser.Result probe = CHeaderParser.Parse(
            File.ReadAllText(Path.Combine(Parse, "probe_shrlib_parse.msvc.i")), ["probe_shrlib_parse.h"], "probe_shrlib_parse.h");
        Assert.Contains("Type 'longdouble' was not found.  Defaulting to type error.", probe.Warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclarationTheParserCannotReadIsSkippedAndTheRestIsKept()
    {
        const string text = "#line 1 \"t.h\"\nint ok_before(int x);\nint broken(;\nstruct { int a : 3; } bits;\n__declspec(dllimport) double ok_after(double y) __attribute__((deprecated));\n";
        LibraryModel model = CHeaderParser.Parse(text, ["t.h"], "t.h").Model;
        Assert.Equal(["ok_before", "ok_after"], model.Functions.Select(f => f.Name));
        Assert.Equal(["double"], model.Functions[1].Rhs);
    }

    // ------------------------------------------------------------------------------------------
    // The model: layout, signatures, the prototype file
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void StructLayoutIsTheCompilersIncludingPacking()
    {
        LibraryModel model = ReadPrototypeText(File.ReadAllText(Path.Combine(Interop, "jgtestlib_proto.m")));
        // jg_layout(which) answers the library's own sizeof and offsetof (jgtestlib.h).
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'lay'); v = zeros(1, 14); for k = 0:13, v(k + 1) = calllib('lay', 'jg_layout', k); end; fprintf('%d ', v); unloadlibrary('lay');";
        long[] witness = Run(code).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();

        (int[] Offsets, int Size, int Align) point = model.StructLayout(model.Struct("jg_point")!)!.Value;
        (int[] Offsets, int Size, int Align) mixed = model.StructLayout(model.Struct("jg_mixed")!)!.Value;
        (int[] Offsets, int Size, int Align) nested = model.StructLayout(model.Struct("jg_nested")!)!.Value;
        (int[] Offsets, int Size, int Align) packed = model.StructLayout(model.Struct("jg_packed")!)!.Value;
        long[] ours =
        [
            point.Size, mixed.Size, .. mixed.Offsets, nested.Size, nested.Offsets[1], nested.Offsets[2], packed.Size, .. packed.Offsets,
        ];
        Assert.Equal(witness, ours);
    }

    [Fact]
    public void ASignatureListsTheReturnThenEveryPointerArgument()
    {
        LibraryModel model = ReadPrototypeText(File.ReadAllText(Path.Combine(Interop, "jgtestlib_proto.m")));
        string Line(string name) => model.Signature(model.Functions.Single(f => f.Name == name));
        Assert.Equal("[double lhs1, doublePtr lhs2] jg_add_ref(double rhs1, doublePtr rhs2, double rhs3)", Line("jg_add_ref"));
        Assert.Equal("lib.pointer lhs1 jg_alloc_ret(int32 rhs1)", Line("jg_alloc_ret"));
        Assert.Equal("lib.jg_color lhs1 jg_color_next(lib.jg_color rhs1)", Line("jg_color_next"));
        Assert.Equal("double lhs1 jg_apply(FcnPtr rhs1, double rhs2)", Line("jg_apply"));
        Assert.Equal("int32 lhs1 jg_triple(doublePtrPtrPtr rhs1)", Line("jg_triple"));
        Assert.Equal("lib.pointer lhs1 jg_exported_value", Line("jg_exported_value"));
        Assert.Equal("jg_crash", Line("jg_crash"));
        Assert.Equal("jg_point lhs1 jg_point_make(double rhs1, double rhs2)", Line("jg_point_make"));
    }

    [Fact]
    public void AWrittenPrototypeFileLoadsBackToTheSameLibrary()
    {
        LibraryModel model = CHeaderParser.Parse(File.ReadAllText(Path.Combine(Interop, "jgtestlib.msvc.i")), ["jgtestlib.h"], "jgtestlib.h").Model;
        string folder = Directory.CreateTempSubdirectory("jg-proto-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "rt_proto.m"), PrototypeFile.Write(model, "rt_proto", "jgtestlib", "jgtestlib", DateTime.Now) + "\n");
            Assert.Equal(model.Functions.Select(f => (f.Name, f.CallType, f.Lhs, string.Join(",", f.Rhs), f.ThunkName)),
                ReadPrototypeText(File.ReadAllText(Path.Combine(folder, "rt_proto.m"))).Functions.Select(f => (f.Name, f.CallType, f.Lhs, string.Join(",", f.Rhs), f.ThunkName)));

            string code = $"addpath('{folder}'); loadlibrary('{Library}', @rt_proto, 'alias', 'rt'); a = libfunctions('rt', '-full'); unloadlibrary rt;"
                + $" loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'orig'); b = libfunctions('orig', '-full'); unloadlibrary orig;"
                + " fprintf('%d %d %d', numel(a), numel(b), isequal(a, b));";
            Assert.Equal("86 86 1", Run(code));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ------------------------------------------------------------------------------------------
    // calllib in stage 8, and the host
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void AStructReturnedByValueComesBackAsAStruct()
    {
        // R2025b lists jg_point_make in notfound; JGraph calls it through the hidden return buffer.
        string code = $"[nf, w] = loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'sv'); p = calllib('sv', 'jg_point_make', 1.5, -2);"
            + " fprintf('%s|%s|%g|%g|%s', strjoin(nf, ','), class(p), p.x, p.y, strjoin(fieldnames(p)', ',')); unloadlibrary sv;";
        Assert.Equal("jg_not_exported|struct|1.5|-2|x,y", Run(code));
    }

    [Fact]
    public void ScalarsStringsAndEnumsCrossACall()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'sc');"
            + " a = calllib('sc', 'jg_double', 2.5); b = calllib('sc', 'jg_int8', 127); c = calllib('sc', 'jg_bool', true);"
            + " [n, s] = calllib('sc', 'jg_strlen', 'hello'); g = calllib('sc', 'jg_greeting'); e = calllib('sc', 'jg_color_next', 'JG_RED');"
            + " v = calllib('sc', 'jg_color_value', 'JG_BLUE'); m = calllib('sc', 'jg_mixed_args', 1, 2, 3, 4, 5);"
            + " fprintf('%g|%g|%s|%d|%g|%s|%s|%s|%g|%g', a, b, class(c), c, n, s, g, e, v, m); unloadlibrary sc;";
        Assert.Equal("3.5|-128|logical|0|5|hello|hello from jgtestlib|JG_GREEN|4|15", Run(code));
    }

    [Fact]
    public void ACrashUnloadsEveryLibraryAndTheNextLoadStartsAnotherHost()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'cr'); loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'cr2');"
            + " try, calllib('cr', 'jg_crash'); catch e, fprintf('%s|', e.identifier); end;"
            + $" fprintf('%d%d|', libisloaded('cr'), libisloaded('cr2')); loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'cr');"
            + " fprintf('%d', calllib('cr', 'jg_version')); unloadlibrary cr;";
        Assert.Equal("JGraph:loadlibrary:HostExited|00|3", Run(code));
    }

    [Fact]
    public void UnloadFreesTheModuleSoAnotherLoadMapsItAgain()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'u1'); unloadlibrary u1; loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'u1');"
            + " fprintf('%d', calllib('u1', 'jg_version')); unloadlibrary('u1');";
        Assert.Equal("3", Run(code));
    }

    // ------------------------------------------------------------------------------------------
    // Compilers
    // ------------------------------------------------------------------------------------------

    private static readonly CCompiler FakeMsvc = new(
        "msvc:C:\\VS", true, "Microsoft Visual C++ 2022 (C)", "MSVC170", "Microsoft", "17.0", "C:\\VS\\", "C:\\VS\\VC\\Auxiliary\\Build\\vcvars64.bat");

    private static readonly CCompiler FakeGcc = new(
        "mingw:C:\\mingw64", false, "MinGW64 Compiler (C)", "mingw64", "GNU", "8.1.0", "C:\\mingw64\\", "C:\\mingw64\\bin\\gcc.exe");

    [Fact]
    public void WithNoCompilerTheHeaderRouteRaisesR2025bsRefusalAndThePrototypeRouteStillLoads()
    {
        CCompilers.SubstituteForTests([]);
        try
        {
            string code = $"try, loadlibrary('{Library}', '{Header}', 'alias', 'nc'); catch e, fprintf('%s|%s|', e.identifier, e.message); end;"
                + $" loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'nc'); fprintf('%d', libisloaded('nc')); unloadlibrary nc;";
            Assert.Equal($"{CCompilers.NoCompilerIdentifier}|{CCompilers.NoCompilerMessage}|1", Run(code));
        }
        finally
        {
            CCompilers.SubstituteForTests(null);
        }
    }

    [Fact]
    public void TheOptionsSettingChoosesAmongTheCompilersFound()
    {
        CCompilers.SubstituteForTests([FakeMsvc, FakeGcc]);
        string? before = CCompilers.Preferred;
        try
        {
            CCompilers.Preferred = null;
            Assert.Same(FakeMsvc, CCompilers.Selected());
            CCompilers.Preferred = FakeGcc.Id;
            Assert.Same(FakeGcc, CCompilers.Selected());
            CCompilers.Preferred = "msvc:C:\\gone";
            Assert.Same(FakeMsvc, CCompilers.Selected()); // a setting naming a compiler no longer found falls back
        }
        finally
        {
            CCompilers.Preferred = before;
            CCompilers.SubstituteForTests(null);
        }
    }

    [Fact]
    public void GetCompilerConfigurationsDescribesTheSelectedCompilerWithR2025bsProperties()
    {
        CCompilers.SubstituteForTests([FakeMsvc]);
        try
        {
            string code = "c = mex.getCompilerConfigurations('C', 'Selected'); fprintf('%s|%s|%s|%s|%s|%s|', class(c), strjoin(fieldnames(c)', ','), c.Name, c.ShortName, c.Version, c.Language);"
                + " a = mex.getCompilerConfigurations(); fprintf('%d|%s|', numel(a), strjoin({a.Language}, ','));"
                + " fprintf('%s|', class(c.Details)); try, mex.getCompilerConfigurations('Cobol'); catch e, fprintf('%s|%s', e.identifier, e.message); end";
            Assert.Equal(
                "mex.CompilerConfiguration|Name,Manufacturer,Language,Version,Location,ShortName,Priority,Details,LinkerName,LinkerVersion,MexOpt|"
                + "Microsoft Visual C++ 2022 (C)|MSVC170|17.0|C|2|C++,C|mex.CompilerConfigurationDetails|MATLAB:mex:UnknownSwitch|Unknown MEX argument 'Cobol'.",
                Run(code));
        }
        finally
        {
            CCompilers.SubstituteForTests(null);
        }
    }

    [CompilerFact]
    public void TheHeaderRouteParsesPreprocessesAndWritesAPrototypeFileWhereItIsTold()
    {
        string folder = Directory.CreateTempSubdirectory("jg-hdr-").FullName;
        try
        {
            string proto = Path.Combine(folder, "sub", "hdr_proto");
            Directory.CreateDirectory(Path.GetDirectoryName(proto)!);
            string code = $"[nf, w] = loadlibrary('{Library}', '{Header}', 'alias', 'hdr', 'mfilename', '{proto}');"
                + " p = calllib('hdr', 'jg_point_make', 3, 4); fprintf('%s|%d|%d|%g|', strjoin(nf, ','), numel(libfunctions('hdr')), contains(w, 'jg_varsum'), p.y);"
                + $" unloadlibrary hdr; addpath('{Path.GetDirectoryName(proto)}');"
                + " loadlibrary('" + Library + "', @hdr_proto, 'alias', 'back'); fprintf('%d', numel(libfunctions('back'))); unloadlibrary back;";
            Assert.Equal("jg_not_exported|86|1|4|86", Run(code));
            Assert.True(File.Exists(proto + ".m"), "the prototype file was not written into the folder mfilename named");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [CompilerFact]
    public void AHeaderIncludingWindowsHRecordsOnlyItsOwnFunctionsAndAddheaderAddsTheIncludedOnes()
    {
        string windows = Path.Combine(Interop, "jgtestlib_win.h");
        string code = $"loadlibrary('{Library}', '{windows}', 'alias', 'w1'); f = libfunctions('w1'); fprintf('%s|%d|', strjoin(f', ','), calllib('w1', 'jg_is_even', 4)); unloadlibrary w1;"
            + $" loadlibrary('{Library}', '{windows}', 'alias', 'w2', 'addheader', 'jgtestlib'); fprintf('%d', numel(libfunctions('w2'))); unloadlibrary w2;";
        Assert.Equal("jg_is_even,jg_tick|1|88", Run(code));
    }

    [CompilerFact]
    public void APreprocessorFailureIsR2025bsCppFailure()
    {
        string folder = Directory.CreateTempSubdirectory("jg-cpp-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "bad_jg.h"), "#include \"no_such_include.h\"\nint jg_version(void);\n");
            string code = $"try, loadlibrary('{Library}', '{Path.Combine(folder, "bad_jg.h")}', 'alias', 'bad'); catch e, fprintf('%s|%s', e.identifier, e.message); end";
            string output = Run(code);
            Assert.StartsWith("MATLAB:loadlibrary:cppfailure|Failed to preprocess the input file.\nOutput from preprocessor is:", output, StringComparison.Ordinal);
            Assert.Contains("no_such_include.h", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [CompilerFact]
    public void ALibraryAloneFindsItsHeaderBesideIt()
    {
        string code = $"[nf, w] = loadlibrary('{Library}'); fprintf('%d', libisloaded('jgtestlib')); unloadlibrary jgtestlib;";
        Assert.Equal("1", Run(code));
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    /// <summary>A fact that is skipped, saying why, where no C compiler is found for the header route.</summary>
    private sealed class CompilerFactAttribute : FactAttribute
    {
        public CompilerFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || CCompilers.Discover().Count == 0)
            {
                Skip = "No C compiler was found: loadlibrary's header route needs MSVC or MinGW-w64 (ADR 0181).";
            }
        }
    }

    private static string Run(string code, bool allowFailure = false)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, Interop, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(allowFailure || result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }

    private static void AssertSameModel(LibraryModel expected, LibraryModel actual)
    {
        Assert.Equal(
            expected.Functions.Select(f => $"{f.Name} {f.CallType} {f.Lhs ?? "[]"} ({string.Join(", ", f.Rhs)}) {f.ThunkName}"),
            actual.Functions.Select(f => $"{f.Name} {f.CallType} {f.Lhs ?? "[]"} ({string.Join(", ", f.Rhs)}) {f.ThunkName}"));
        Assert.Equal(
            expected.Structs.Select(s => $"{s.Name} {s.Packing} {string.Join(", ", s.Members.Select(m => m.Name + ":" + m.Type))}"),
            actual.Structs.Select(s => $"{s.Name} {s.Packing} {string.Join(", ", s.Members.Select(m => m.Name + ":" + m.Type))}"));
        // R2025b's enum order is not the header's (probe_shrlib_parse); the set and each enum's members are.
        Assert.Equal(
            expected.Enums.Select(e => $"{e.Name} {string.Join(", ", e.Members.Select(m => $"{m.Name}={m.Value}"))}").Order(StringComparer.Ordinal),
            actual.Enums.Select(e => $"{e.Name} {string.Join(", ", e.Members.Select(m => $"{m.Name}={m.Value}"))}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A prototype file's model, read from its text by the lines R2025b writes — an oracle independent
    /// of <see cref="PrototypeFile.Read"/>, which the script tests exercise.
    /// </summary>
    private static LibraryModel ReadPrototypeText(string text)
    {
        var model = new LibraryModel();
        static string? Field(string line, string name)
        {
            Match m = Regex.Match(line, @"fcns\." + name + @"\{fcnNum\}=('(?<v>[^']*)'|\[\]|\{(?<c>[^}]*)\})");
            return !m.Success ? null : m.Groups["v"].Success ? m.Groups["v"].Value : m.Groups["c"].Success ? m.Groups["c"].Value : "";
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Contains("fcns.name{fcnNum}", StringComparison.Ordinal))
            {
                string rhs = Field(line, "RHS") ?? "";
                model.Functions.Add(new LibFunction(
                    Field(line, "name")!, Field(line, "calltype")!, Field(line, "LHS") is { Length: > 0 } lhs ? lhs : null,
                    [.. Regex.Matches(rhs, "'([^']*)'").Select(m => m.Groups[1].Value)], null, Field(line, "thunkname") is { Length: > 0 } t ? t : null));
            }
            else if (Regex.Match(line, @"^structs\.(\w+)\.members=struct\((.*)\);$") is { Success: true } members)
            {
                string[] parts = [.. Regex.Matches(members.Groups[2].Value, "'([^']*)'").Select(m => m.Groups[1].Value)];
                var list = new List<LibMember>();
                for (int i = 0; i + 1 < parts.Length; i += 2)
                {
                    list.Add(new LibMember(parts[i], parts[i + 1]));
                }

                int? packing = Regex.Match(text, @"structs\." + members.Groups[1].Value + @"\.packing=(\d+);") is { Success: true } p
                    ? int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture)
                    : null;
                model.Structs.Add(new LibStruct(members.Groups[1].Value, list, packing));
            }
            else if (Regex.Match(line, @"^enuminfo\.(\w+)=struct\((.*)\);$") is { Success: true } values)
            {
                var list = Regex.Matches(values.Groups[2].Value, @"'(\w+)',(-?\d+)")
                    .Select(m => (m.Groups[1].Value, long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();
                model.Enums.Add(new LibEnum(values.Groups[1].Value, list));
            }
        }

        return model;
    }
}
