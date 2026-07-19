using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24: the <see cref="ImageBuffer"/> carrier — dimensions, interleaved indexing, Clone, Dispose.</summary>
public class ImageBufferTests
{
    [Fact]
    public void Constructor_SetsDimensionsAndZeroFills()
    {
        using var image = new ImageBuffer(4, 6, 3);

        Assert.Equal(4, image.Height);
        Assert.Equal(6, image.Width);
        Assert.Equal(3, image.Channels);
        Assert.Equal(72, image.SampleCount);
        foreach (double sample in image.Pixels)
        {
            Assert.Equal(0.0, sample);
        }
    }

    [Fact]
    public void Indexer_IsRowMajorInterleaved()
    {
        using var image = new ImageBuffer(2, 2, 3);
        image[1, 0, 2] = 0.5; // row 1, col 0, blue → flat index ((1*2)+0)*3 + 2 = 8

        Assert.Equal(0.5, image.Pixels[8]);
        Assert.Equal(0.5, image[1, 0, 2]);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 0, 3)]
    public void Indexer_ThrowsOutOfRange(int r, int c, int ch)
    {
        using var image = new ImageBuffer(2, 2, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => image[r, c, ch]);
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(4, 0, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(2, 2, 4)]
    public void Constructor_RejectsInvalidShape(int h, int w, int ch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(h, w, ch));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        using var original = new ImageBuffer(3, 3, 1);
        original[1, 1, 0] = 0.75;

        using ImageBuffer copy = original.Clone();
        Assert.Equal(0.75, copy[1, 1, 0]);

        copy[1, 1, 0] = 0.25;
        Assert.Equal(0.75, original[1, 1, 0]); // mutating the copy leaves the original untouched
    }

    [Fact]
    public void IsBinary_TrueOnlyForZeroOneSamples()
    {
        using var binary = new ImageBuffer(1, 3, 1);
        binary[0, 0, 0] = 0.0;
        binary[0, 1, 0] = 1.0;
        binary[0, 2, 0] = 0.0;
        Assert.True(binary.IsBinary);

        binary[0, 1, 0] = 0.5;
        Assert.False(binary.IsBinary);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var image = new ImageBuffer(2, 2, 1);
        image.Dispose();
        image.Dispose(); // must not throw
    }
}
