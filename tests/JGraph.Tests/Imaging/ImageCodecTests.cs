using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24: <see cref="ImageCodec"/> round-trips through real PNG/JPEG/BMP files in a temp dir.</summary>
public sealed class ImageCodecTests : IDisposable
{
    private readonly string _directory;

    public ImageCodecTests()
    {
        _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "jgraph-tests", System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathFor(string name) => System.IO.Path.Combine(_directory, name);

    [Fact]
    public void Png_RoundTripsRgbPixelsExactly()
    {
        using var image = new ImageBuffer(2, 2, 3);
        // Byte-quantized values survive PNG (lossless) exactly: 0, 64, 128, 255 → /255.
        image[0, 0, 0] = 0.0;   image[0, 0, 1] = 64 / 255.0;  image[0, 0, 2] = 128 / 255.0;
        image[0, 1, 0] = 1.0;   image[0, 1, 1] = 0.0;         image[0, 1, 2] = 0.0;
        image[1, 0, 0] = 0.0;   image[1, 0, 1] = 1.0;         image[1, 0, 2] = 0.0;
        image[1, 1, 0] = 0.0;   image[1, 1, 1] = 0.0;         image[1, 1, 2] = 1.0;

        string path = PathFor("rgb.png");
        ImageCodec.Write(path, image);
        using ImageBuffer read = ImageCodec.Read(path);

        Assert.Equal(3, read.Channels);
        Assert.Equal(2, read.Height);
        Assert.Equal(2, read.Width);
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    Assert.Equal(image[r, c, ch], read[r, c, ch], 6);
                }
            }
        }
    }

    [Fact]
    public void Png_NeutralGrayCollapsesToOneChannel()
    {
        using var image = new ImageBuffer(2, 2, 3);
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 2; c++)
            {
                double v = (((r * 2) + c) * 64) / 255.0; // byte-exact levels 0, 64, 128, 192
                image[r, c, 0] = image[r, c, 1] = image[r, c, 2] = v;
            }
        }

        string path = PathFor("gray.png");
        ImageCodec.Write(path, image);
        using ImageBuffer read = ImageCodec.Read(path);

        Assert.Equal(1, read.Channels); // R==G==B everywhere → decoded as grayscale
        Assert.Equal(128 / 255.0, read[1, 0, 0], 6);
    }

    [Fact]
    public void Jpeg_RoundTripsWithinTolerance()
    {
        using var image = new ImageBuffer(8, 8, 3);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                image[r, c, 0] = 0.75;
                image[r, c, 1] = 0.25;
                image[r, c, 2] = 0.50;
            }
        }

        string path = PathFor("flat.jpg");
        ImageCodec.Write(path, image, jpegQuality: 100);
        using ImageBuffer read = ImageCodec.Read(path);

        Assert.Equal(3, read.Channels);
        Assert.True(Math.Abs(read[4, 4, 0] - 0.75) < 0.05); // lossy: a flat block stays close at quality 100
        Assert.True(Math.Abs(read[4, 4, 1] - 0.25) < 0.05);
        Assert.True(Math.Abs(read[4, 4, 2] - 0.50) < 0.05);
    }

    [Fact]
    public void Read_MissingFile_ThrowsIoException()
    {
        Assert.ThrowsAny<IOException>(() => ImageCodec.Read(PathFor("nope.png")));
    }

    [Fact]
    public void Read_UndecodableBytes_ThrowsInvalidData()
    {
        string path = PathFor("garbage.png");
        File.WriteAllText(path, "this is not an image");
        Assert.Throws<InvalidDataException>(() => ImageCodec.Read(path));
    }

    [Fact]
    public void Write_UnsupportedExtension_Throws()
    {
        using var image = new ImageBuffer(2, 2, 1);
        Assert.Throws<ArgumentException>(() => ImageCodec.Write(PathFor("x.gif"), image));
    }
}
