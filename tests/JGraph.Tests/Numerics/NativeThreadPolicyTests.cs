using JGraph.Numerics.LinearAlgebra.Native;
using Xunit;

namespace JGraph.Tests.Numerics;

public class NativeThreadPolicyTests
{
    [Fact]
    public void ConcurrentScopesKeepTheirRequestedCountUntilDisposed()
    {
        if (!OpenBlasLoader.Status.Loaded) return;
        Parallel.For(0, 64, i =>
        {
            var work = i % 2 == 0 ? NativeThreads.Work.Spectral : NativeThreads.Work.Factor;
            long size = i % 2 == 0 ? 400 : 1200;
            using var scope = NativeThreads.Use(work, size);
            int expected = NativeThreads.CountFor(work, size);
            Assert.Equal(expected, OpenBlasNative.GetNumThreads());
            Thread.Yield();
            Assert.Equal(expected, OpenBlasNative.GetNumThreads());
        });
    }

    [Fact]
    public void TraceReportsEachCallByRoutineThreadsSizeAndSeconds()
    {
        if (!OpenBlasLoader.Status.Loaded) return;
        var lines = new List<string>();
        Action<string>? before = NativeThreads.Trace;
        NativeThreads.Trace = line => { lock (lines) { lines.Add(line); } };
        try
        {
            using (NativeThreads.Use(NativeThreads.Work.Factor, 300))
            {
                Thread.Sleep(2);
            }
        }
        finally
        {
            NativeThreads.Trace = before;
        }

        string line = Assert.Single(lines);
        string[] parts = line.Split('|');
        Assert.Equal("native", parts[0]);
        Assert.Equal(nameof(TraceReportsEachCallByRoutineThreadsSizeAndSeconds), parts[1]);
        Assert.Equal("threads=" + NativeThreads.CountFor(NativeThreads.Work.Factor, 300), parts[2]);
        Assert.Equal("size=300", parts[3]);
        Assert.True(double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) >= 0.001, line);
    }

    [Fact]
    public void NoTraceMeansNoWork()
    {
        if (!OpenBlasLoader.Status.Loaded) return;
        Action<string>? before = NativeThreads.Trace;
        NativeThreads.Trace = null;
        try
        {
            using NativeThreads.Scope scope = NativeThreads.Use(NativeThreads.Work.Level3, 10);
            Assert.Equal(default, scope);
        }
        finally
        {
            NativeThreads.Trace = before;
        }
    }
}
