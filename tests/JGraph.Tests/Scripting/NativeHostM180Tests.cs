using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Api;
using JGraph.NativeHost;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 7 of the .NET and shared-library interop plan (ADR 0180): the native host. There is no MATLAB
/// surface yet, so these drive the host through its client directly, against the committed test
/// library: every slot width, structs by value, memory and the arena, the library search, crash and
/// cancel, environment and folder sync, the job object, and the session's lifetime.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NativeHostM180Tests(ITestOutputHelper log) : IDisposable
{
    private static readonly string Library =
        Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "jgtestlib.dll");

    private readonly NativeHostProcess _host = NativeHostProcess.Start();
    private long _module;

    public void Dispose() => _host.Dispose();

    private long Export(string name)
    {
        if (_module == 0)
        {
            _module = _host.Load(Library);
        }

        return _host.Symbol(_module, name);
    }

    private static byte[] Cells(params long[] values)
    {
        byte[] bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
        }

        return bytes;
    }

    private long CallInteger(string name, SlotKind kind, long argument)
    {
        byte[] answer = _host.Call(Export(name), new Slot(kind), [new Slot(kind)], Cells(argument), default);
        return BinaryPrimitives.ReadInt64LittleEndian(answer);
    }

    [Theory]
    [InlineData("jg_int8", SlotKind.Int8, -5, -4)]
    [InlineData("jg_int8", SlotKind.Int8, 127, -128)]
    [InlineData("jg_uint8", SlotKind.UInt8, 255, 0)]
    [InlineData("jg_int16", SlotKind.Int16, -300, -299)]
    [InlineData("jg_uint16", SlotKind.UInt16, 65535, 0)]
    [InlineData("jg_int32", SlotKind.Int32, -7, -6)]
    [InlineData("jg_uint32", SlotKind.UInt32, 4294967295, 0)]
    [InlineData("jg_int64", SlotKind.Int64, 9007199254740992, 9007199254740993)]
    [InlineData("jg_bool", SlotKind.UInt8, 1, 0)]
    [InlineData("jg_bool", SlotKind.UInt8, 0, 1)]
    public void EveryIntegerWidthIsPassedAndReturnedWithItsSign(string name, SlotKind kind, long argument, long expected)
    {
        Assert.Equal(expected, CallInteger(name, kind, argument));
    }

    [Fact]
    public void AnUnsigned64BitReturnKeepsEveryBit()
    {
        byte[] answer = _host.Call(Export("jg_uint64_max"), new Slot(SlotKind.UInt64), [], [], default);
        Assert.Equal(ulong.MaxValue, BinaryPrimitives.ReadUInt64LittleEndian(answer));
    }

    [Fact]
    public void FloatsTravelInTheirOwnRegistersAmongIntegers()
    {
        byte[] single = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(single, 1.5f);
        Assert.Equal(2.5f, BinaryPrimitives.ReadSingleLittleEndian(
            _host.Call(Export("jg_float"), new Slot(SlotKind.Single), [new Slot(SlotKind.Single)], single, default)));

        // short, int, double, float, int64: the x64 convention interleaves general and XMM registers.
        byte[] mixed = new byte[40];
        BinaryPrimitives.WriteInt64LittleEndian(mixed, 1);
        BinaryPrimitives.WriteInt64LittleEndian(mixed.AsSpan(8), 2);
        BinaryPrimitives.WriteDoubleLittleEndian(mixed.AsSpan(16), 3.5);
        BinaryPrimitives.WriteSingleLittleEndian(mixed.AsSpan(24), 4.25f);
        BinaryPrimitives.WriteInt64LittleEndian(mixed.AsSpan(32), 5);
        Slot[] parameters = [new(SlotKind.Int16), new(SlotKind.Int32), new(SlotKind.Double), new(SlotKind.Single), new(SlotKind.Int64)];
        Assert.Equal(15.75, BinaryPrimitives.ReadDoubleLittleEndian(
            _host.Call(Export("jg_mixed_args"), new Slot(SlotKind.Double), parameters, mixed, default)));
    }

    [Fact]
    public void StructsByValueFollowTheX64Rule()
    {
        // 16 bytes: passed by a pointer to a copy.
        byte[] point = new byte[16];
        BinaryPrimitives.WriteDoubleLittleEndian(point, 3);
        BinaryPrimitives.WriteDoubleLittleEndian(point.AsSpan(8), 4);
        Assert.Equal(5.0, BinaryPrimitives.ReadDoubleLittleEndian(
            _host.Call(Export("jg_point_len"), new Slot(SlotKind.Double), [new Slot(SlotKind.Struct, 16)], point, default)));

        // A 16-byte return: written through a hidden first argument.
        byte[] xy = new byte[16];
        BinaryPrimitives.WriteDoubleLittleEndian(xy, 1.5);
        BinaryPrimitives.WriteDoubleLittleEndian(xy.AsSpan(8), -2);
        byte[] made = _host.Call(Export("jg_point_make"), new Slot(SlotKind.Struct, 16),
            [new Slot(SlotKind.Double), new Slot(SlotKind.Double)], xy, default);
        Assert.Equal(1.5, BinaryPrimitives.ReadDoubleLittleEndian(made));
        Assert.Equal(-2.0, BinaryPrimitives.ReadDoubleLittleEndian(made.AsSpan(8)));

        // 4 bytes (a union): in a register.
        byte[] union = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(union, 1234567);
        Assert.Equal(1234567, BinaryPrimitives.ReadInt32LittleEndian(
            _host.Call(Export("jg_union_in"), new Slot(SlotKind.Int32), [new Slot(SlotKind.Struct, 4)], union, default)));
    }

    [Fact]
    public void MemoryIsAllocatedWrittenPassedAndReadBack()
    {
        long buffer = _host.Alloc(3 * 8);
        byte[] values = new byte[24];
        for (int i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(values.AsSpan(i * 8), i + 1);
        }

        _host.Write(buffer, values);
        byte[] scale = Cells(buffer, 3, 0);
        BinaryPrimitives.WriteDoubleLittleEndian(scale.AsSpan(16), 10);
        _host.Call(Export("jg_scale_double"), new Slot(SlotKind.Void),
            [new Slot(SlotKind.Pointer), new Slot(SlotKind.Int32), new Slot(SlotKind.Double)], scale, default);

        byte[] back = _host.Read(buffer, 24);
        Assert.Equal([10.0, 20.0, 30.0], Enumerable.Range(0, 3).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(back.AsSpan(i * 8))));
        _host.Release(buffer);
    }

    [Fact]
    public void ALargeBufferTravelsThroughTheArena()
    {
        const int n = 1_000_000;
        long buffer = _host.Alloc(n * 8L);
        double[] values = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        _host.Write(buffer, MemoryMarshal.AsBytes(values.AsSpan()));

        byte[] sum = Cells(buffer, n);
        Assert.Equal(n * (n - 1.0) / 2, BinaryPrimitives.ReadDoubleLittleEndian(
            _host.Call(Export("jg_sum"), new Slot(SlotKind.Double), [new Slot(SlotKind.Pointer), new Slot(SlotKind.Int32)], sum, default)));

        byte[] scale = Cells(buffer, n, 0);
        BinaryPrimitives.WriteDoubleLittleEndian(scale.AsSpan(16), 2);
        _host.Call(Export("jg_scale_double"), new Slot(SlotKind.Void),
            [new Slot(SlotKind.Pointer), new Slot(SlotKind.Int32), new Slot(SlotKind.Double)], scale, default);
        double[] back = MemoryMarshal.Cast<byte, double>(_host.Read(buffer, n * 8)).ToArray();
        Assert.Equal(2.0 * (n - 1), back[^1]);
        Assert.Equal(2.0 * 12345, back[12345]);
        _host.Release(buffer);
    }

    [Fact]
    public void AStringReturnedIntoStaticStorageIsMeasuredAndRead()
    {
        long address = BinaryPrimitives.ReadInt64LittleEndian(
            _host.Call(Export("jg_greeting"), new Slot(SlotKind.Pointer), [], [], default));
        long length = _host.StringLength(address);
        Assert.Equal("hello from jgtestlib", Encoding.ASCII.GetString(_host.Read(address, (int)length)));
    }

    [Fact]
    public void TheHostReportsWhatTheLoaderAndGetProcAddressSay()
    {
        NativeHostFailure missing = Assert.Throws<NativeHostFailure>(() => _host.Load(Path.Combine(Path.GetTempPath(), "jg_no_such_library.dll")));
        Assert.Equal(126, missing.Code);

        string notADll = Path.Combine(Path.GetTempPath(), $"jg_not_a_dll_{Guid.NewGuid():N}.dll");
        File.WriteAllText(notADll, "not a library");
        try
        {
            NativeHostFailure bad = Assert.Throws<NativeHostFailure>(() => _host.Load(notADll));
            Assert.Equal(193, bad.Code);
            Assert.Contains("is not a valid Win32 application", bad.Message);
        }
        finally
        {
            File.Delete(notADll);
        }

        Export("jg_int32");
        Assert.Equal(127, Assert.Throws<NativeHostFailure>(() => _host.Symbol(_module, "jg_not_exported")).Code);
        Assert.True(_host.IsAlive);
    }

    [Fact]
    public void ACrashInNativeCodeEndsTheHostAndSaysHow()
    {
        int pid = _host.ProcessId;
        NativeHostExitedException crash = Assert.Throws<NativeHostExitedException>(
            () => _host.Call(Export("jg_crash"), new Slot(SlotKind.Void), [], [], default));

        Assert.False(crash.Cancelled);
        Assert.Equal(unchecked((int)0xC0000005), crash.ExitCode);
        Assert.Equal(
            "The native library host stopped (exception 0xC0000005, access violation) during calllib('jgtestlib', 'jg_crash'). "
            + "Every library it had loaded is unloaded, and every lib.pointer into it is invalid.",
            crash.Sentence("calllib('jgtestlib', 'jg_crash')"));
        Assert.False(_host.IsAlive);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        Assert.Same(crash, Assert.Throws<NativeHostExitedException>(() => _host.Alloc(8))); // dead for good
    }

    [Fact]
    public void ACancelDuringALongCallKillsTheHostPromptly()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        NativeHostExitedException stopped = Assert.Throws<NativeHostExitedException>(
            () => _host.Call(Export("jg_sleep"), new Slot(SlotKind.Void), [new Slot(SlotKind.Int32)], Cells(60_000), cancel.Token));

        Assert.True(stopped.Cancelled);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"the cancel took {clock.Elapsed}");
        Assert.StartsWith("The native library host was stopped during calllib('jgtestlib', 'jg_sleep').",
            stopped.Sentence("calllib('jgtestlib', 'jg_sleep')"));
        Assert.False(_host.IsAlive);
    }

    [Fact]
    public void PrintedOutputGoesNowhereAndNeverBlocksTheHost()
    {
        // With stdout an unread pipe, a few hundred kilobytes would fill it and hang the host.
        long text = _host.Alloc(4097);
        _host.Write(text, Encoding.ASCII.GetBytes(new string('x', 4096)));
        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(4097, BinaryPrimitives.ReadInt32LittleEndian(
                _host.Call(Export("jg_printf"), new Slot(SlotKind.Int32), [new Slot(SlotKind.Pointer)], Cells(text), default)));
        }
    }

    [Fact]
    public void TheEnvironmentAndTheFolderAreBroughtUpToDateBeforeACall()
    {
        string name = $"JG_M180_{Guid.NewGuid():N}";
        string folder = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
        long nameText = _host.Alloc(name.Length + 1);
        _host.Write(nameText, Encoding.ASCII.GetBytes(name));
        try
        {
            Environment.SetEnvironmentVariable(name, "first");
            EnvironmentBlock.NoteChange(); // as setenv and every .NET member do
            _host.Sync(folder);
            Assert.Equal("first", ReadString(CallInteger("jg_getenv", SlotKind.Pointer, nameText)));
            Assert.Equal(folder, ReadString(_host.Call(Export("jg_getcwd"), new Slot(SlotKind.Pointer), [], [], default)));

            Environment.SetEnvironmentVariable(name, null);
            EnvironmentBlock.NoteChange();
            _host.Sync(folder);
            Assert.Equal("", ReadString(CallInteger("jg_getenv", SlotKind.Pointer, nameText)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private string ReadString(byte[] pointer) => ReadString(BinaryPrimitives.ReadInt64LittleEndian(pointer));

    private string ReadString(long address) => Encoding.ASCII.GetString(_host.Read(address, (int)_host.StringLength(address)));

    [Fact]
    public void TheHostBelongsToAJobThatEndsItWithJGraph()
    {
        using Process process = Process.GetProcessById(_host.ProcessId);
        Assert.True(IsProcessInJob(process.Handle, 0, out bool inJob));
        Assert.True(inJob);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [Fact]
    public void ASessionStartsAHostOnDemandAndAnotherAfterACrash()
    {
        var session = new NativeSession();
        Assert.Null(session.Current);
        NativeHostProcess first = session.Host;
        Assert.Same(first, session.Host);

        long module = first.Load(Library);
        Assert.Throws<NativeHostExitedException>(
            () => first.Call(first.Symbol(module, "jg_crash"), new Slot(SlotKind.Void), [], [], default));
        Assert.Null(session.Current);

        NativeHostProcess second = session.Host;
        Assert.NotSame(first, second);
        Assert.True(second.Generation > first.Generation);
        int pid = second.ProcessId;
        session.Stop();
        Assert.Null(session.Current);
        Assert.True(Gone(pid), "a stopped host is still running");
    }

    [Fact]
    public void TheLibrarySearchFollowsR2025b()
    {
        string top = Path.Combine(Path.GetTempPath(), $"jg_m180_search_{Guid.NewGuid():N}");
        string a = Directory.CreateDirectory(Path.Combine(top, "A")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(top, "B")).FullName;
        Directory.CreateDirectory(Path.Combine(a, "sub"));
        try
        {
            foreach (string file in new[] { "A/s1.dll", "B/s1.dll", "B/s2.dll", "A/s3.mexw64", "A/s4.dll", "A/s4.mexw64", "A/s12.lib", "A/sub/s11.dll", "B/s9.dll" })
            {
                File.WriteAllText(Path.Combine(top, file), "");
            }

            string? Find(string name) => NativeSession.FindLibrary(name, a, [b]);
            Assert.Equal(Path.Combine(a, "s1.dll"), Find("s1"));           // the current folder first
            Assert.Equal(Path.Combine(b, "s2.dll"), Find("s2"));           // then the path
            Assert.Equal(Path.Combine(a, "s3.mexw64"), Find("s3"));        // the MEX extension
            Assert.Equal(Path.Combine(a, "s4.mexw64"), Find("s4"));        // before .dll
            Assert.Equal(Path.Combine(a, "s12.lib"), Find("s12.lib"));     // any extension given is kept
            Assert.Equal(Path.Combine(a, "sub", "s11.dll"), Find(Path.Combine("sub", "s11")));
            Assert.Equal(Path.Combine(b, "s9.dll"), Find(Path.Combine(b, "s9"))); // a path without its extension
            Assert.Null(Find("kernel32"));                                 // left to the system search
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }

    [Fact]
    public void ARunEndsItsHost()
    {
        string code = $"lib = '{Library}'; nh = jgraph.internal.nativehost; nh('call', lib, 'int32 jg_int32(int32)', 1); fprintf('%d', nh('pid'));";
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);

        int pid = int.Parse(output.NormalText.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(pid > 0);
        Assert.True(Gone(pid), "the run's native host outlived the run");
    }

    private static bool Gone(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Fact]
    public void StopDuringALongNativeCallCancelsTheRunAndSaysWhatWasLost()
    {
        string code = $"nh = jgraph.internal.nativehost; nh('call', '{Library}', 'void jg_sleep(int32)', 60000); fprintf('not reached');";
        var output = new RecordingScriptOutput();
        // Late enough that the run is inside the call even on a loaded machine; a stop before it loses nothing.
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var clock = Stopwatch.StartNew();
        ScriptRunResult result = JgsRunner.Run(code, new ScriptContext(output, (_, _) => { }, null), cancel.Token, sourceId: "", hook: null, JgsDialect.Matlab);

        Assert.False(result.Success);
        Assert.Equal("Script run was cancelled.", result.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"the stop took {clock.Elapsed}");
        Assert.Contains("The native library host was stopped during calllib(", output.ErrorText + "|" + output.NormalText);
        Assert.DoesNotContain("not reached", output.NormalText);
    }

    [Fact]
    public void ReportTheRoundTripCost()
    {
        long function = Export("jg_void");
        long int32 = Export("jg_int32");
        for (int i = 0; i < 200; i++)
        {
            _host.Call(function, new Slot(SlotKind.Void), [], [], default);
        }

        const int n = 5000;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            _host.Call(function, new Slot(SlotKind.Void), [], [], default);
        }

        double voidCall = clock.Elapsed.TotalMicroseconds / n;
        clock.Restart();
        for (int i = 0; i < n; i++)
        {
            _host.Call(int32, new Slot(SlotKind.Int32), [new Slot(SlotKind.Int32)], Cells(i), default);
        }

        double intCall = clock.Elapsed.TotalMicroseconds / n;
        clock.Restart();
        for (int i = 0; i < n; i++)
        {
            _host.Sync(Path.GetTempPath());
        }

        double sync = clock.Elapsed.TotalMicroseconds / n;
        log.WriteLine($"void call {voidCall:0.0} us, int32 call {intCall:0.0} us, unchanged sync {sync:0.00} us");
    }
}
