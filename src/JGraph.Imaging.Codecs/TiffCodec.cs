using BitMiracle.LibTiff.Classic;

namespace JGraph.Imaging.Codecs;

/// <summary>TIFF pages with native grayscale/indexed samples and palettes.</summary>
public static class TiffCodec
{
    public static bool IsTiff(string path) => Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff";

    public static (ImageBuffer Image, double[,]? Map, ImageBuffer? Alpha) Read(string path, int page = 0)
    {
        using Tiff tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException("Cannot open TIFF.");
        if (page < 0 || page > short.MaxValue || !tiff.SetDirectory((short)page))
            throw new ArgumentOutOfRangeException(nameof(page));
        int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        int bits = tiff.GetFieldDefaulted(TiffTag.BITSPERSAMPLE)[0].ToInt();
        int samples = tiff.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
        int photo = tiff.GetField(TiffTag.PHOTOMETRIC)[0].ToInt();
        bool palette = photo == (int)Photometric.PALETTE;
        double[,]? map = null;
        if (palette)
        {
            FieldValue[] fields = tiff.GetField(TiffTag.COLORMAP);
            map = new double[1 << bits, 3];
            for (int c = 0; c < 3; c++)
            {
                short[] channel = fields[c].ToShortArray();
                for (int i = 0; i < map.GetLength(0); i++)
                    map[i, c] = (ushort)channel[i] / 65535.0;
            }
        }
        bool direct = !tiff.IsTiled() && bits is 1 or 2 or 4 or 8 or 16 && (palette || photo is 0 or 1 or 2);
        int channels = palette || photo is 0 or 1 ? 1 : 3;
        var image = new ImageBuffer(height, width, channels) { Class = bits == 16 ? ImageClass.UInt16 : bits == 1 && !palette ? ImageClass.Logical : ImageClass.UInt8 };
        ImageBuffer? alpha = samples > channels && !palette ? new ImageBuffer(height, width, 1) { Class = image.Class } : null;
        try
        {
            if (!direct)
            {
                if (palette || bits > 8)
                    throw new InvalidDataException("This TIFF layout cannot preserve its native samples.");
                int[] raster = new int[checked(width * height)];
                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    throw new InvalidDataException("Cannot decode TIFF pixels.");
                for (int p = 0; p < raster.Length; p++)
                {
                    image.Pixels[p * channels] = Tiff.GetR(raster[p]) / 255.0;
                    if (channels == 3)
                    {
                        image.Pixels[p * 3 + 1] = Tiff.GetG(raster[p]) / 255.0;
                        image.Pixels[p * 3 + 2] = Tiff.GetB(raster[p]) / 255.0;
                    }
                    if (alpha is not null)
                        alpha.Pixels[p] = Tiff.GetA(raster[p]) / 255.0;
                }
            }
            else
            {
                bool separate = tiff.GetFieldDefaulted(TiffTag.PLANARCONFIG)[0].ToInt() == (int)PlanarConfig.SEPARATE;
                byte[] scan = new byte[tiff.ScanlineSize()];
                for (int r = 0; r < height; r++)
                for (short plane = 0; plane < (separate ? samples : 1); plane++)
                {
                    if (!tiff.ReadScanline(scan, r, plane))
                        throw new InvalidDataException("Truncated TIFF scanline.");
                    for (int x = 0; x < width; x++)
                    for (int channel = 0; channel < (separate ? 1 : samples); channel++)
                    {
                        int c = separate ? plane : channel, sample = separate ? x : x * samples + channel;
                        int raw = bits == 16 ? BitConverter.ToUInt16(scan, sample * 2) : bits == 8 ? scan[sample] : (scan[sample * bits / 8] >> (8 - bits - (sample * bits % 8))) & ((1 << bits) - 1);
                        double normalized = raw / (palette ? bits == 16 ? 65535.0 : 255.0 : (1 << bits) - 1.0);
                        if (c < channels)
                            image.Pixels[(r * width + x) * channels + c] = photo == 0 ? 1 - normalized : normalized;
                        else if (c == channels && alpha is not null)
                            alpha.Pixels[r * width + x] = normalized;
                    }
                }
            }
            return (image, map, alpha);
        }
        catch { image.Dispose(); alpha?.Dispose(); throw; }
    }

    public static int FrameCount(string path)
    {
        using Tiff tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException("Cannot open TIFF.");
        return tiff.NumberOfDirectories();
    }

    public static void Write(string path, ImageBuffer image, bool append = false, double[,]? map = null)
    {
        using Tiff tiff = Tiff.Open(path, append && File.Exists(path) ? "a" : "w") ?? throw new IOException("Cannot create TIFF.");
        int bits = image.Class == ImageClass.UInt16 ? 16 : 8, samples = map is null ? image.Channels : 1;
        tiff.SetField(TiffTag.IMAGEWIDTH, image.Width);
        tiff.SetField(TiffTag.IMAGELENGTH, image.Height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, samples);
        tiff.SetField(TiffTag.BITSPERSAMPLE, bits);
        tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.PHOTOMETRIC, map is not null ? Photometric.PALETTE : samples == 1 ? Photometric.MINISBLACK : Photometric.RGB);
        tiff.SetField(TiffTag.COMPRESSION, Compression.LZW);
        tiff.SetField(TiffTag.ROWSPERSTRIP, tiff.DefaultStripSize(0));
        if (map is not null)
        {
            short[][] colors = [new short[1 << bits], new short[1 << bits], new short[1 << bits]];
            for (int i = 0; i < map.GetLength(0); i++)
            for (int c = 0; c < 3; c++)
                colors[c][i] = unchecked((short)(ushort)Math.Round(Math.Clamp(map[i, c], 0, 1) * 65535));
            tiff.SetField(TiffTag.COLORMAP, colors[0], colors[1], colors[2]);
        }
        byte[] row = new byte[image.Width * samples * (bits / 8)];
        for (int r = 0; r < image.Height; r++)
        {
            for (int x = 0; x < image.Width * samples; x++)
            {
                double value = image.Pixels[r * image.Width * image.Channels + x];
                int raw = map is not null && image.Class == ImageClass.Double ? (int)value - 1 : (int)Math.Round(Math.Clamp(value, 0, 1) * ((1 << bits) - 1));
                if (bits == 8)
                    row[x] = (byte)raw;
                else
                {
                    row[x * 2] = (byte)raw;
                    row[x * 2 + 1] = (byte)(raw >> 8);
                }
            }
            if (!tiff.WriteScanline(row, r))
                throw new IOException("Cannot write TIFF scanline.");
        }
        if (!tiff.WriteDirectory())
            throw new IOException("Cannot write TIFF directory.");
    }
}
