using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 9 of the .NET and shared-library interop plan (ADR 0182): <c>calllib</c>'s arguments,
/// <c>lib.pointer</c>, <c>libpointer</c> and <c>libstruct</c>. What R2025b does is held by the
/// <c>shrlib_types</c>, <c>shrlib_pointers</c> and <c>shrlib_structs</c> fixtures; these tests hold what
/// only JGraph does: memory a pointer owns is freed after its last holder, a pointer into a dead host
/// refuses, and where the ADR departs from R2025b.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class LibPointerM182Tests
{
    private static readonly string Interop = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop");
    private static readonly string Library = Path.Combine(Interop, "jgtestlib.dll");

    [Fact]
    public void MemoryABlockOwnsIsFreedByTheNextCallAfterItsLastHolderGoes()
    {
        using NativeHostProcess host = NativeHostProcess.Start();
        WeakReference dropped = AllocateAndDrop(host);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(dropped.IsAlive);
        Assert.Equal(1, host.PendingReleases);

        host.Release(host.Alloc(8)); // the next request makes the queued free first
        Assert.Equal(0, host.PendingReleases);
    }

    private static WeakReference AllocateAndDrop(NativeHostProcess host) =>
        new(NativeBlock.Allocate(host, new byte[64]));

    [Fact]
    public void APointerMadeByPlusKeepsTheMemoryOfThePointerItCameFrom()
    {
        string code = "lp = libpointer('doublePtr', [1 2 3]); lq = lp + 1; clear lp; lq.Value = lq.Value;"
            + " fprintf('%g ', lq.Value);";
        Assert.Equal("2 3 ", Run(code));
    }

    [Fact]
    public void AValuePastTheEndOfOwnedMemoryIsRefused()
    {
        // R2025b reads past the allocation (a 4x4 of a 6-element pointer); JGraph refuses.
        string code = "lp = libpointer('doublePtr', 1:6); reshape(lp, 4, 4);"
            + " try, v = lp.Value; catch e, fprintf('%s|', e.identifier); end; reshape(lp, 2, 3); v = lp.Value; fprintf('%g', sum(v(:)));";
        Assert.Equal("JGraph:libpointer:PastEnd|21", Run(code));
    }

    [Fact]
    public void IntegerPointerMemoryRoundsAndSaturatesAsMatlabsClassesDo()
    {
        // R2025b holds [2 -2 -2147483648] (truncation and wrap); JGraph holds what int32() gives.
        string code = "v = libpointer('int32Ptr', [2.5 -2.5 1e10]).Value; fprintf('%s:%d %d %d', class(v), v);";
        Assert.Equal("int32:3 -3 2147483647", Run(code));
    }

    [Fact]
    public void ALibstructNothingNamesNoLongerHoldsItsLibrary()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'ls1'); s = libstruct('jg_point');"
            + " try, unloadlibrary ls1; catch e, fprintf('%s|', e.identifier); end;"
            + " clear s; unloadlibrary ls1; fprintf('%d', libisloaded('ls1'));";
        Assert.Equal("MATLAB:unloadlibrary:IsInUse|0", Run(code));
    }

    [Fact]
    public void APointerIntoAHostThatCrashedRefusesAndTheNextOneWorks()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'pc'); lp = libpointer('doublePtr', [1 2]);"
            + " try, calllib('pc', 'jg_crash'); catch, end;"
            + " try, v = lp.Value; catch e, fprintf('%s|', e.identifier); end;"
            + " q = libpointer('doublePtr', [3 4]); fprintf('%g', sum(q.Value));";
        Assert.Equal("JGraph:libpointer:HostExited|7", Run(code));
    }

    [Fact]
    public void UInt64MaxComesBackAsItsValueWithThePrecisionWarning()
    {
        // R2025b answers -1 (ADR 0182).
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'u64'); lastwarn('');"
            + " x = calllib('u64', 'jg_uint64_max'); [m, id] = lastwarn; fprintf('%.17g|%s', x, id); unloadlibrary u64;";
        Assert.Equal("1.8446744073709552e+19|JGraph:interop:int64Precision", Run(code));
    }

    [Fact]
    public void ALibstructWithArrayFieldsIsPassedAsItsMemory()
    {
        // R2025b passes NULL for a libstruct whose type has an array field or packing (ADR 0182).
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'mx'); m = libstruct('jg_mixed');"
            + " calllib('mx', 'jg_mixed_fill', m); fprintf('%g %g %g|%s:%s|', m.a, m.b, m.c, class(m.d), mat2str(m.d));"
            + " p = libstruct('jg_packed'); calllib('mx', 'jg_packed_fill', p); fprintf('%g', calllib('mx', 'jg_packed_sum', p));";
        Assert.Matches(@"^-1 2\.5 300\|int32:\[4 5 6\]\|24\.5$", Run(code));
    }

    [Fact]
    public void AStructPassedByPointerComesBackAndALibstructSeesTheCallsWrites()
    {
        string code = $"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'sp');"
            + " r = calllib('sp', 'jg_point_scale', struct('x', 1, 'y', 2), 10); s = libstruct('jg_point', struct('x', 3));"
            + " calllib('sp', 'jg_point_scale', s, 2); fprintf('%g %g|%g %g|%d', r.x, r.y, s.x, s.y, s.structsize);";
        Assert.Equal("10 20|6 0|16", Run(code));
    }

    [Fact]
    public void AMissingFieldIsMatlabsErrorInTheMatlabDialect()
    {
        string code = "s = struct('x', 1); try, s.y; catch e, fprintf('%s:%s|', e.identifier, e.message); end;"
            + " t = struct('x', {}); try, t.y; catch e, fprintf('%s', e.identifier); end;";
        Assert.Equal("MATLAB:nonExistentField:Unrecognized field name \"y\".|MATLAB:nonExistentField", Run(code));
    }

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, Interop, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
