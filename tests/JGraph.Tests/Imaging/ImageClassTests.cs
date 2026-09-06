using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46: the class tag on <see cref="ImageBuffer"/> — the native-range arithmetic it implies, the fact
/// that it survives a copy, and that decoding stamps the class the file's bit depth calls for.
/// </summary>
public class ImageClassTests
{
    [Fact]
    public void NewImage_IsDouble_SoNothingComputedChangesMeaning()
    {
        using var image = new ImageBuffer(2, 2, 1);
        Assert.Equal(ImageClass.Double, image.Class);
        Assert.Equal(1.0, image.Class.Scale());
        Assert.False(image.Class.IsInteger());
    }

    [Theory]
    [InlineData(ImageClass.UInt8, 1.0, 255.0)]
    [InlineData(ImageClass.UInt8, 0.0, 0.0)]
    [InlineData(ImageClass.UInt16, 1.0, 65535.0)]
    [InlineData(ImageClass.Int16, 1.0, 32767.0)]
    [InlineData(ImageClass.Int16, 0.0, -32768.0)]
    [InlineData(ImageClass.Double, 0.25, 0.25)]
    [InlineData(ImageClass.Logical, 1.0, 1.0)]
    public void ToNative_MapsTheNormalizedRangeOntoTheClassRange(ImageClass imageClass, double sample, double expected) =>
        Assert.Equal(expected, imageClass.ToNative(sample), 9);

    [Theory]
    [InlineData(ImageClass.UInt8)]
    [InlineData(ImageClass.UInt16)]
    [InlineData(ImageClass.Int16)]
    public void FromNative_UndoesToNative(ImageClass imageClass)
    {
        // Only on the class's own grid: ToNative rounds, so a uint8 image cannot hold 0.125 and the
        // round trip is not meant to pretend otherwise.
        double scale = imageClass.Scale();
        foreach (double level in new[] { 0.0, 1.0, 2.0, Math.Floor(scale / 2), scale - 1, scale })
        {
            double sample = level / scale;
            Assert.Equal(sample, imageClass.FromNative(imageClass.ToNative(sample)), 12);
        }
    }

    [Fact]
    public void Quantize_SnapsAnIntegerClassOntoItsOwnGrid()
    {
        // Half of 255 is 127.5, which uint8 cannot hold; MATLAB rounds, so the sample must land on a
        // whole 1/255 step rather than halfway between two of them.
        using var image = new ImageBuffer(1, 1, 1) { Class = ImageClass.UInt8 };
        image[0, 0, 0] = 0.5;
        ImageClassInfo.Quantize(image);

        double native = image.Class.ToNative(image[0, 0, 0]);
        Assert.Equal(Math.Round(native), native);
        Assert.Equal(128.0, native);
    }

    [Fact]
    public void Quantize_LeavesAFloatingPointImageAlone()
    {
        using var image = new ImageBuffer(1, 1, 1) { Class = ImageClass.Double };
        image[0, 0, 0] = 0.123456789;
        ImageClassInfo.Quantize(image);
        Assert.Equal(0.123456789, image[0, 0, 0], 12);
    }

    [Fact]
    public void Clone_CarriesTheClass()
    {
        using var image = new ImageBuffer(2, 2, 1) { Class = ImageClass.UInt16 };
        using ImageBuffer copy = image.Clone();
        Assert.Equal(ImageClass.UInt16, copy.Class);
    }

    [Fact]
    public void MatlabName_RoundTripsThroughFromMatlabName()
    {
        foreach (ImageClass imageClass in Enum.GetValues<ImageClass>())
        {
            Assert.Equal(imageClass, ImageClassInfo.FromMatlabName(imageClass.MatlabName()));
        }

        Assert.Null(ImageClassInfo.FromMatlabName("table"));
    }

    [Fact]
    public void Read_TagsAnOrdinaryFileUint8()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
        try
        {
            using (var source = new ImageBuffer(2, 2, 1))
            {
                source[0, 0, 0] = 1.0;
                ImageCodec.Write(path, source);
            }

            using ImageBuffer read = ImageCodec.Read(path);
            Assert.Equal(ImageClass.UInt8, read.Class);
            Assert.Equal(255.0, read.Class.ToNative(read[0, 0, 0]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SixteenBitPng_RoundTripsAtFullPrecision()
    {
        // A sample that cannot survive an eight-bit encoder must retain its native value.
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
        try
        {
            const double value = 1000.0 / 65535.0; // not representable in 8 bits
            using (var source = new ImageBuffer(2, 2, 1) { Class = ImageClass.UInt16 })
            {
                source.Pixels.Fill(value);
                ImageCodec.Write(path, source, new CodecWriteOptions(BitDepth: 16));
            }

            using ImageBuffer read = ImageCodec.Read(path);
            double native = read.Class.ToNative(read[0, 0, 0]);
            Assert.Equal(ImageClass.UInt16, read.Class);
            Assert.Equal(1000.0, native, 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SixteenBitPng_PreservesEverySampleAndAlpha(int channels)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
        using var source = new ImageBuffer(256, 256, channels) { Class = ImageClass.UInt16 };
        using var alpha = new ImageBuffer(256, 256, 1) { Class = ImageClass.UInt16 };
        for (int r = 0; r < 256; r++)
        {
            for (int c = 0; c < 256; c++)
            {
                int sample = r * 256 + c;
                alpha[r, c, 0] = sample / 65535.0;
                for (int ch = 0; ch < channels; ch++)
                    source[r, c, ch] = ((sample + ch * 1000) % 65536) / 65535.0;
            }
        }
        try
        {
            ImageCodec.Write(path, source, new CodecWriteOptions(Alpha: alpha));
            var decoded = ImageCodec.ReadWithAlpha(path);
            using var read = decoded.Image;
            using var opacity = decoded.Alpha;
            Assert.Equal(ImageClass.UInt16, read.Class);
            Assert.Equal(channels, read.Channels);
            Assert.NotNull(opacity);
            Assert.True(source.Pixels.SequenceEqual(read.Pixels));
            Assert.True(alpha.Pixels.SequenceEqual(opacity.Pixels));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadWithAlpha_HandsBackTheOpacityPlaneOnlyWhenItIsNotOpaque()
    {
        string opaque = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
        string translucent = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
        try
        {
            using (var source = new ImageBuffer(2, 2, 1))
            {
                source.Pixels.Fill(0.5);
                ImageCodec.Write(opaque, source);

                using var mask = new ImageBuffer(2, 2, 1);
                mask.Pixels.Fill(0.5);
                ImageCodec.Write(translucent, source, new CodecWriteOptions(Alpha: mask));
            }

            (ImageBuffer image, ImageBuffer? alpha) = ImageCodec.ReadWithAlpha(opaque);
            image.Dispose();
            Assert.Null(alpha);

            (ImageBuffer image2, ImageBuffer? alpha2) = ImageCodec.ReadWithAlpha(translucent);
            image2.Dispose();
            Assert.NotNull(alpha2);
            Assert.Equal(0.5, alpha2[0, 0, 0], 2);
            alpha2.Dispose();
        }
        finally
        {
            File.Delete(opaque);
            File.Delete(translucent);
        }
    }
}
