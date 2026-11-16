using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// Open item 344 (ADR 0380): the BMP reader's RLE and bitfields files built byte by byte, the GIF
/// writer's indices read back through the GIF reader, and the 1-bit logical TIFF.
/// </summary>
public sealed class BmpGifFileTests : IDisposable
{
    private readonly string _directory;

    public BmpGifFileTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
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

    [Fact]
    public void Rle8_DecodesRunsAbsoluteRunsAndEndCodes()
    {
        // 4-by-3, bottom row first: two runs, an end of line; four absolute; a run, three absolute
        // (padded), the end of the bitmap.
        byte[] file = Bmp(4, 3, 8, 1, Palette(16), [2, 5, 2, 7, 0, 0, 0, 4, 1, 2, 3, 4, 0, 0, 1, 9, 0, 3, 10, 11, 12, 0, 0, 1]);
        var warnings = new List<ImageFileWarning>();
        (ImageBuffer image, double[,]? map, BmpHeader header) = BmpFile.Read(file, warnings)!.Value;
        using (image)
        {
            Assert.Empty(warnings);
            Assert.Equal(ImageClass.UInt8, image.Class);
            Assert.Equal(new double[] { 9, 10, 11, 12 }, Row(image, 0));
            Assert.Equal(new double[] { 1, 2, 3, 4 }, Row(image, 1));
            Assert.Equal(new double[] { 5, 5, 7, 7 }, Row(image, 2));
            Assert.Equal(16, map!.GetLength(0));
            Assert.Equal("indexed", header.ColorType);
        }
    }

    [Fact]
    public void Rle4_AlternatesNibblesAndWarnsWithoutAnEndCode()
    {
        byte[] file = Bmp(5, 1, 4, 2, Palette(16), [5, 0x12, 0, 0]);
        var warnings = new List<ImageFileWarning>();
        (ImageBuffer image, _, _) = BmpFile.Read(file, warnings)!.Value;
        using (image)
        {
            Assert.Equal(new double[] { 1, 2, 1, 2, 1 }, Row(image, 0));
            Assert.Equal("MATLAB:imagesci:bmpdrle:missingEndCode", Assert.Single(warnings).Identifier);
        }
    }

    [Fact]
    public void Rle8_DeltaMovesFromTheCursor()
    {
        // A run of three, a delta of two to the right, a run of one: the BMP format's answer.
        byte[] file = Bmp(8, 1, 8, 1, Palette(16), [3, 5, 0, 2, 2, 0, 1, 6, 0, 1]);
        (ImageBuffer image, _, _) = BmpFile.Read(file, [])!.Value;
        using (image)
        {
            Assert.Equal(new double[] { 5, 5, 5, 0, 0, 6, 0, 0 }, Row(image, 0));
        }
    }

    [Fact]
    public void Bitfields565_ScalesEachMaskToEightBits()
    {
        byte[] masks = [.. Le(0xF800), .. Le(0x07E0), .. Le(0x001F)];
        byte[] pixels = [0x00, 0x00, 0x10, 0x84]; // black, then 0x8410 (16, 32, 16 of 31, 63, 31)
        byte[] file = Bmp(2, 1, 16, 3, null, pixels, masks);
        (ImageBuffer image, double[,]? map, BmpHeader header) = BmpFile.Read(file, [])!.Value;
        using (image)
        {
            Assert.Null(map);
            Assert.Equal("truecolor", header.ColorType);
            Assert.Equal(0, image[0, 0, 0] * 255, 9);
            Assert.Equal(Math.Round(16 * 255 / 31.0), image[0, 1, 0] * 255, 9);
            Assert.Equal(Math.Round(32 * 255 / 63.0), image[0, 1, 1] * 255, 9);
        }
    }

    [Fact]
    public void Version4Header_At16BitsWithoutBitfieldsIsRefused()
    {
        byte[] file = Bmp(1, 1, 16, 0, null, [0, 0, 0, 0], new byte[68], headerSize: 108);
        ImageFileRefusal refusal = Assert.Throws<ImageFileRefusal>(() => BmpFile.Read(file, []));
        Assert.Equal("MATLAB:imagesci:imbmpinfo:invalidBMPmetadata", refusal.Identifier);
    }

    [Fact]
    public void GifIndices_RoundTripWithTheirTable()
    {
        string path = Path.Combine(_directory, "g.gif");
        byte[] indices = [0, 1, 1, 1, 0, 1];
        GifEncoder.WriteIndexed(path, indices, 3, 2, new double[,] { { 0, 0, 0 }, { 1, 1, 1 } }, 50, -1, append: false);
        byte[] bytes = File.ReadAllBytes(path);
        (IReadOnlyList<GifFrame> frames, _) = GifFile.Parse(bytes);
        Assert.Equal(1, Assert.Single(frames).BitDepth);
        (ImageBuffer read, double[,] map) = GifFile.Read(bytes, 0);
        using (read)
        {
            Assert.Equal(2, map.GetLength(0));
            Assert.Equal(new double[] { 0, 1, 1 }, Row(read, 0));
            Assert.Equal(new double[] { 1, 0, 1 }, Row(read, 1));
        }
    }

    [Fact]
    public void GifIndices_SurviveLzwPastTheDictionaryLimit()
    {
        string path = Path.Combine(_directory, "big.gif");
        var random = new Random(344);
        byte[] indices = new byte[200 * 150];
        random.NextBytes(indices);
        var ramp = new double[256, 3];
        for (int i = 0; i < 256; i++)
        {
            ramp[i, 0] = ramp[i, 1] = ramp[i, 2] = i / 255.0;
        }

        GifEncoder.WriteIndexed(path, indices, 200, 150, ramp, 50, -1, append: false);
        (ImageBuffer read, _) = GifFile.Read(File.ReadAllBytes(path), 0);
        using (read)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                Assert.Equal(indices[i], (int)Math.Round(read.Pixels[i] * 255));
            }
        }
    }

    [Fact]
    public void LogicalTiff_IsOneBitAndReadsBackLogical()
    {
        string path = Path.Combine(_directory, "t.tif");
        using var image = new ImageBuffer(3, 11, 1) { Class = ImageClass.Logical };
        for (int i = 0; i < 33; i++)
        {
            image.Pixels[i] = i % 3 == 0 ? 1 : 0;
        }

        TiffCodec.Write(path, image);
        (ImageBuffer read, double[,]? map, ImageBuffer? alpha) = TiffCodec.Read(path);
        using (read)
        {
            Assert.Null(map);
            Assert.Null(alpha);
            Assert.Equal(ImageClass.Logical, read.Class);
            Assert.Equal(image.Pixels.ToArray(), read.Pixels.ToArray());
        }
    }

    private static double[] Row(ImageBuffer image, int row) =>
        [.. Enumerable.Range(0, image.Width).Select(x => Math.Round(image[row, x, 0] * (image.Class == ImageClass.Logical ? 1 : 255)))];

    private static byte[] Le(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static byte[][] Palette(int entries) =>
        [.. Enumerable.Range(0, entries).Select(i => new byte[] { (byte)(255 - (16 * i)), (byte)(16 * i), (byte)i, 0 })];

    /// <summary>A BMP file from its parts; extra goes right after the first 40 header bytes.</summary>
    private static byte[] Bmp(int width, int height, int bits, int compression, byte[][]? palette, byte[] pixels, byte[]? extra = null, int headerSize = 40)
    {
        var info = new List<byte>();
        info.AddRange(Le(headerSize));
        info.AddRange(Le(width));
        info.AddRange(Le(height));
        info.AddRange([1, 0, (byte)bits, 0]);
        info.AddRange(Le(compression));
        info.AddRange(Le(pixels.Length));
        info.AddRange(new byte[8]);
        info.AddRange(Le(palette?.Length ?? 0));
        info.AddRange(new byte[4]);
        if (headerSize > 40)
        {
            byte[] tail = new byte[headerSize - 40];
            extra?.AsSpan(0, Math.Min(extra.Length, tail.Length)).CopyTo(tail);
            info.AddRange(tail);
            extra = null;
        }

        byte[] paletteBytes = palette is null ? [] : [.. palette.SelectMany(static p => p)];
        int offset = 14 + info.Count + (extra?.Length ?? 0) + paletteBytes.Length;
        var file = new List<byte> { (byte)'B', (byte)'M' };
        file.AddRange(Le(offset + pixels.Length));
        file.AddRange(new byte[4]);
        file.AddRange(Le(offset));
        file.AddRange(info);
        file.AddRange(extra ?? []);
        file.AddRange(paletteBytes);
        file.AddRange(pixels);
        return [.. file];
    }
}
