using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The JGraph Runtime's native bridge, jgrt.dll (compiler and SDK plan, stage K10). Its mx functions
/// are called in this process, where the engine never starts, against R2025b's answers to the same
/// questions (<c>recordings/k10_mx_*.out.txt</c>); its mcl functions and the conversions at the call
/// boundary are run by a C program, <c>Runtime/native/win-x64/jgrtclient.exe</c> (built by
/// <c>native/jgrt/build.ps1</c>), which starts .NET in a process of its own as any C client does, on
/// an archive of the probe's sources JGraph's mcc builds. <c>run-k10.ps1</c> runs the whole probe;
/// these are its landmarks, and the divergences ADR 0266 records.
/// </summary>
[Collection("JG facade")]
public sealed class NativeBridgeTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k10-" + Guid.NewGuid().ToString("N"))).FullName;

    public NativeBridgeTests()
    {
        JG.Reset();
        string from = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "src", "k10");
        Directory.CreateDirectory(Src);
        foreach (string file in Directory.GetFiles(from, "*.m"))
        {
            File.Copy(file, Path.Combine(Src, Path.GetFileName(file)));
        }
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
            // A cache file the client's process still held; the temp folder clears it.
        }
    }

    private string Src => Path.Combine(_root, "src");

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    [Fact]
    public void ArraysAreShapedAsR2025bShapesThem()
    {
        // create.nd2311, create.nd1, create.nd0, create.nd031: fewer than two dimensions made two,
        // trailing singletons dropped.
        Assert.Equal([2UL, 3UL], Dims(Mx.mxCreateNumericArray(4, [2, 3, 1, 1], DoubleClass, 0)));
        Assert.Equal([4UL, 1UL], Dims(Mx.mxCreateNumericArray(1, [4], DoubleClass, 0)));
        Assert.Equal([0UL, 0UL], Dims(Mx.mxCreateNumericArray(0, [4], DoubleClass, 0)));
        Assert.Equal([0UL, 3UL], Dims(Mx.mxCreateNumericArray(3, [0, 3, 1], DoubleClass, 0)));

        // query.N, query.sub3: N is every dimension past the first; a subscript counts down the columns.
        nint nd = Mx.mxCreateNumericArray(3, [2, 3, 4], DoubleClass, 0);
        Assert.Equal(12UL, (ulong)Mx.mxGetN(nd));
        Assert.Equal(23UL, (ulong)Mx.mxCalcSingleSubscript(nd, 3, [1, 2, 3]));
        Mx.mxDestroyArray(nd);

        // risky 8 to 10: mxCreateNumericMatrix of a cell, char or logical class makes one.
        nint cell = Mx.mxCreateNumericMatrix(2, 2, 1, 0);
        Assert.Equal(1, Mx.mxGetClassID(cell));
        Assert.Equal("cell", Marshal.PtrToStringAnsi(Mx.mxGetClassName(cell)));
        Mx.mxDestroyArray(cell);

        // create.sparse0, scalar.sparse: room for at least one nonzero; the scalar is the first stored value.
        nint sparse = Mx.mxCreateSparse(2, 2, 0, 0);
        Assert.Equal(1UL, (ulong)Mx.mxGetNzmax(sparse));
        Mx.mxDestroyArray(sparse);
    }

    [Fact]
    public void TextIsR2025bs()
    {
        // create.charmatrix: rows padded with NUL, not blanks; string.matrix.tostring stops at the first.
        nint matrix = Mx.mxCreateCharMatrixFromStrings(3, ["a", "bcd", "ef"]);
        Assert.Equal([3UL, 3UL], Dims(matrix, destroy: false));
        nint text = Mx.mxArrayToString(matrix);
        Assert.Equal("abe", Marshal.PtrToStringAnsi(text));
        Mx.mxFree(text);
        Mx.mxDestroyArray(matrix);

        // string.get3, string.get5, string.get6: 1 when cut short, the buffer always ended.
        nint hello = Mx.mxCreateString("hello");
        var buffer = new byte[16];
        Assert.Equal(1, Mx.mxGetString(hello, buffer, 3));
        Assert.Equal("he", Encoding.ASCII.GetString(buffer, 0, 2));
        Assert.Equal(0, buffer[2]);
        Assert.Equal(1, Mx.mxGetString(hello, buffer, 5));
        Assert.Equal(0, Mx.mxGetString(hello, buffer, 6));
        Assert.Equal("hello", Encoding.ASCII.GetString(buffer, 0, 5));
        Mx.mxDestroyArray(hello);

        // create.utf8, create.latin1: UTF-8 when the bytes are UTF-8, else the ANSI code page's.
        nint utf8 = Mx.mxCreateStringBytes([0x63, 0x61, 0x66, 0xC3, 0xA9, 0]);
        Assert.Equal(4UL, (ulong)Mx.mxGetN(utf8));
        Assert.Equal('é', (char)Marshal.ReadInt16(Mx.mxGetChars(utf8), 6));
        Mx.mxDestroyArray(utf8);
    }

    [Fact]
    public void AComplexArrayIsReadInEitherModel()
    {
        // An array made in the interleaved model and read in the separate one is rearranged for it:
        // what the engine's outputs are to a client built with mbuild's default (R2017b).
        nint z = Mx.mxCreateDoubleMatrix(1, 2, 1);
        Assert.Equal(16UL, (ulong)Mx.mxGetElementSize(z));
        Assert.Equal(8UL, (ulong)Mx.mxGetElementSize_730(z));
        Marshal.Copy(new double[] { 1, 2, 3, -4 }, 0, Mx.mxGetComplexDoubles(z), 4);
        var real = new double[2];
        var imaginary = new double[2];
        Marshal.Copy(Mx.mxGetPr_730(z), real, 0, 2);
        Marshal.Copy(Mx.mxGetPi_730(z), imaginary, 0, 2);
        Assert.Equal([1.0, 3.0], real);
        Assert.Equal([2.0, -4.0], imaginary);

        // And back: the interleaved view of a separate array.
        var both = new double[4];
        Marshal.Copy(Mx.mxGetComplexDoubles(z), both, 0, 4);
        Assert.Equal([1.0, 2.0, 3.0, -4.0], both);

        // risky 12 (interleaved): R2025b's mxGetPr of a complex array throws and ends the process; JGraph's answers NULL.
        Assert.Equal(0, Mx.mxGetPr(z));
        Assert.Equal(1, Mx.mxMakeArrayReal(z));
        Assert.Equal(8UL, (ulong)Mx.mxGetElementSize(z));
        Mx.mxDestroyArray(z);
    }

    [Fact]
    public void StructFieldsAreR2025bs()
    {
        nint s = Mx.mxCreateStructMatrix(1, 2, 2, ["a", "b"]);
        Assert.Equal(1, Mx.mxGetFieldNumber(s, "b"));
        Assert.Equal(-1, Mx.mxGetFieldNumber(s, "B"));
        Assert.Equal(0, Mx.mxGetField(s, 0, "b")); // struct.get.unset
        Assert.Equal(2, Mx.mxAddField(s, "c"));
        Assert.Equal(0, Mx.mxAddField(s, "a")); // struct.add.a: the number it has
        Assert.Equal(3, Mx.mxAddField(s, "1bad")); // risky 7: any name, as R2025b takes it
        Mx.mxRemoveField(s, 0);
        Assert.Equal("b", Marshal.PtrToStringAnsi(Mx.mxGetFieldNameByNumber(s, 0)));
        Assert.Equal(0, Mx.mxGetFieldNameByNumber(s, 9)); // risky 15
        Mx.mxDestroyArray(s);
    }

    [Fact]
    public async Task ACProgramCallsAComponentAsR2025bsDoes()
    {
        string exe = Path.Combine(Repo, "tests", "JGraph.Tests", "Runtime", "native", "win-x64", "jgrtclient.exe");
        Assert.True(File.Exists(exe), $"{exe} is missing: run native\\jgrt\\build.ps1.");
        string archive = await BuildArchive();
        string[] lines = Run(exe, archive);
        string[] recorded = File.ReadAllLines(Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "recordings", "k10_feval_separate.out.txt"))
            .Select(static l => l.StartsWith("  ", StringComparison.Ordinal) ? l[2..] : l)
            .ToArray();

        // What JGraph answers as R2025b did, line for line from its recording.
        foreach (string label in new[]
        {
            "echo.matrix", "info.matrix", "info.cell.null", "echo.complex", "make.string", "make.string.info", "object",
            "object.get.Value", "two.nlhs3.ok", "two.nlhs0.ok", "two.nlhs0.out0", "err.nested.ok", "err.nested.depth",
            "err.nested.lastmessage", "print.ok", "mlf.mixed.3.ok", "mlf.mixed.3.a", "mlf.mixed.3.vout",
        })
        {
            Assert.Equal(recorded.First(l => l.StartsWith(label + ": ", StringComparison.Ordinal)), lines.First(l => l.StartsWith(label + ": ", StringComparison.Ordinal)));
        }

        Assert.Contains("initialize: 1", lines);
        Assert.Contains("initialize.again: 0", lines);
        Assert.Contains("ERR<<Error using k10two\\nToo many output arguments.\\n\\n>>", lines);
        Assert.Contains("ERR<<Error using k10err>inner (line 27)\\nRaised in a local function.\\n\\nError in k10err (line 11)\\n\\n>>", lines);
        Assert.Contains("err.nested.frame0: k10err.m, name inner, line 27.", lines);
        Assert.Contains("OUT<<no newline>>", lines);
        Assert.Contains("ERR<<to standard error\\n>>", lines);

        // ADR 0266's divergences: mxIsClass knows an object's own class, a missing property is NULL
        // where R2025b crashes, a duplicate of a value object is a copy, a NULL input is refused.
        Assert.Contains("object.isclass.k10obj: 1", lines);
        Assert.Contains("object.get.Nope: NULL", lines);
        Assert.Contains(lines, static l => l.StartsWith("value.dup.set.original: double(6) [1x1] n=1 es=8 is=numeric,scalar,double {3}", StringComparison.Ordinal));
        Assert.Contains(lines, static l => l.StartsWith("value.dup.set.dup: double(6) [1x1] n=1 es=8 is=numeric,scalar,double {10}", StringComparison.Ordinal));
        Assert.Contains("ERR<<Error using k10info\\nInput argument 1 is NULL; pass an mxArray, an empty one for [].\\n\\n>>", lines);
        Assert.Contains("risky.nullinput.ok: 0", lines);
        Assert.Contains("initialized.after: 1", lines);
        Assert.Equal("done", lines[^1]);
    }

    /// <summary>The probe's sources as an archive, through the .NET Data API target, which writes it beside its assembly.</summary>
    private async Task<string> BuildArchive()
    {
        string folder = Path.Combine(_root, "lib");
        IEnumerable<string> files = Directory.GetFiles(Src, "*.m").Order(StringComparer.Ordinal).Select(static f => $"'{f}'");
        string code = $"mcc('-W', 'dotnet:k10lib,api=matlab-data', '-T', 'link:lib', '-d', '{folder}', {string.Join(", ", files)});";
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return Path.Combine(folder, "k10lib.ctf");
    }

    /// <summary>Runs the C program with the test's own folder, where jgrt.dll sits beside the engine, first on its PATH.</summary>
    private string[] Run(string exe, string archive)
    {
        var start = new ProcessStartInfo(exe, $"\"{archive}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = _root,
        };
        start.Environment["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH");
        start.Environment["MCR_CACHE_ROOT"] = Path.Combine(_root, "cache");
        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("jgrtclient did not finish in three minutes.");
        }

        Assert.True(process.ExitCode == 0, $"jgrtclient ended with {process.ExitCode}:\n{output}\n{error.Result}");
        return output.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
    }

    private const int DoubleClass = 6;

    /// <summary>An array's size, destroying the array after unless told not to.</summary>
    private static ulong[] Dims(nint array, bool destroy = true)
    {
        int n = (int)Mx.mxGetNumberOfDimensions(array);
        var dims = new long[n];
        Marshal.Copy(Mx.mxGetDimensions(array), dims, 0, n);
        if (destroy)
        {
            Mx.mxDestroyArray(array);
        }

        return [.. dims.Select(static d => (ulong)d)];
    }

    /// <summary>jgrt.dll's mx functions, from beside the engine in the test's folder.</summary>
    private static class Mx
    {
        private const string Jgrt = "jgrt";

        [DllImport(Jgrt)] public static extern nint mxCreateNumericArray(nuint ndim, nuint[] dims, int classId, int complexity);
        [DllImport(Jgrt)] public static extern nint mxCreateNumericMatrix(nuint m, nuint n, int classId, int complexity);
        [DllImport(Jgrt)] public static extern nint mxCreateDoubleMatrix(nuint m, nuint n, int complexity);
        [DllImport(Jgrt)] public static extern nint mxCreateSparse(nuint m, nuint n, nuint nzmax, int complexity);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern nint mxCreateString(string text);
        [DllImport(Jgrt, EntryPoint = "mxCreateString")] public static extern nint mxCreateStringBytes(byte[] text);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern nint mxCreateCharMatrixFromStrings(nuint m, string[] text);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern nint mxCreateStructMatrix(nuint m, nuint n, int nfields, string[] names);
        [DllImport(Jgrt)] public static extern void mxDestroyArray(nint array);
        [DllImport(Jgrt)] public static extern void mxFree(nint memory);
        [DllImport(Jgrt)] public static extern int mxGetClassID(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetClassName(nint array);
        [DllImport(Jgrt)] public static extern nuint mxGetNumberOfDimensions(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetDimensions(nint array);
        [DllImport(Jgrt)] public static extern nuint mxGetN(nint array);
        [DllImport(Jgrt)] public static extern nuint mxCalcSingleSubscript(nint array, nuint nsubs, nuint[] subs);
        [DllImport(Jgrt)] public static extern nuint mxGetNzmax(nint array);
        [DllImport(Jgrt)] public static extern nuint mxGetElementSize(nint array);
        [DllImport(Jgrt)] public static extern nuint mxGetElementSize_730(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetPr(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetPr_730(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetPi_730(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetComplexDoubles(nint array);
        [DllImport(Jgrt)] public static extern int mxMakeArrayReal(nint array);
        [DllImport(Jgrt)] public static extern nint mxGetChars(nint array);
        [DllImport(Jgrt)] public static extern nint mxArrayToString(nint array);
        [DllImport(Jgrt)] public static extern int mxGetString(nint array, byte[] buffer, nuint length);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern int mxGetFieldNumber(nint array, string name);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern nint mxGetField(nint array, nuint index, string name);
        [DllImport(Jgrt, CharSet = CharSet.Ansi)] public static extern int mxAddField(nint array, string name);
        [DllImport(Jgrt)] public static extern void mxRemoveField(nint array, int field);
        [DllImport(Jgrt)] public static extern nint mxGetFieldNameByNumber(nint array, int field);
    }
}
