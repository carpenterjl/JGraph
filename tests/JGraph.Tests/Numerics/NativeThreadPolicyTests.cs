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
}
