using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

public class WindowBorrowedBufferTests
{
    [Theory]
    [InlineData(BufferMode.Managed, 301)]
    [InlineData(BufferMode.Native, 301)]
    [InlineData(BufferMode.Native, 300007)]
    public void BorrowedStorageMatchesArrayAndRemainsUnchanged(BufferMode mode, int length)
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = mode };
        using NumericBuffer buffer = allocator.Allocate(length);
        double[] input = Enumerable.Range(0, length).Select(i => Math.Sin(i) * (i % 17)).ToArray();
        input[17] = double.NaN;
        input[31] = double.PositiveInfinity;
        input[49] = -0.0;
        input.CopyTo(buffer.AsSpan());
        WindowStat[] stats = length > 1000
            ? [WindowStat.Mean, WindowStat.StandardDeviation]
            : Enum.GetValues<WindowStat>().Where(s => s != WindowStat.Other).ToArray();
        foreach (var stat in stats)
        foreach (var ends in Enum.GetValues<WindowEnds>())
        foreach (bool omit in new[] { false, true })
        {
            double[] expected = WindowKernels.Slide(stat, input, 10, 10, ends, -2, omit, double.NaN);
            double[] actual = WindowKernels.Slide(stat, buffer, 10, 10, ends, -2, omit, double.NaN);
            Assert.Equal(expected.Select(BitConverter.DoubleToInt64Bits), actual.Select(BitConverter.DoubleToInt64Bits));
        }

        Assert.Equal(input.Select(BitConverter.DoubleToInt64Bits), buffer.AsSpan().ToArray().Select(BitConverter.DoubleToInt64Bits));
    }
}
