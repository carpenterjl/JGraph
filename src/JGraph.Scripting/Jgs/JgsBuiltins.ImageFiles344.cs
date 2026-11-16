using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The image file leftovers of open item 344 (ADR 0380), in MATLAB code: GIF writes and reads with
/// writegif's and readgif's indices, BMP reads through every compression readbmp takes, imfinfo's
/// Format, BitDepth and ColorType for those files, and imwrite's signedPixelData warning.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string GifOutOfRangeText =
        "Image data contains values that are out of range.  Out of range values will be given the nearest valid value.";

    /// <summary>
    /// imfinfo's Format in MATLAB code: the format the file's bytes say, in R2025b's spelling (lower
    /// case, <c>jpg</c> and <c>tif</c> for every spelling of the extension, and <c>GIF</c> in capitals
    /// because imgifinfo copies it from the file's signature); a file of no format known here keeps its
    /// extension, lower case.
    /// </summary>
    private static string MatlabImageFormat(string path) => ImageFormatOf(path) switch
    {
        ".gif" => "GIF",
        { } known => known.TrimStart('.'),
        null => Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
    };

    /// <summary>
    /// imfinfo of a BMP or GIF file in MATLAB code, from the file's own header as imbmpinfo and
    /// imgifinfo read it: a BMP's BitDepth is its bits a pixel and its ColorType indexed when it holds a
    /// colour map; a GIF has one entry a frame, indexed, at the depth of the table the frame uses. Null
    /// for any other file, which the general road describes.
    /// </summary>
    private static JgsValue? MatlabImfinfoFromHeader(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var pages = new List<(int Width, int Height, int BitDepth, string ColorType, int Samples)>();
        if (BmpFile.ReadHeader(bytes) is { } bmp)
        {
            string colorType = bmp.ColorType;
            pages.Add((bmp.Width, Math.Abs(bmp.Height), bmp.BitDepth, colorType, colorType == "truecolor" ? 3 : 1));
        }
        else if (GifFile.IsGif(bytes))
        {
            foreach (GifFrame frame in GifFile.Parse(bytes).Frames)
            {
                pages.Add((frame.Width, frame.Height, frame.BitDepth, "indexed", 1));
            }
        }
        else
        {
            return null;
        }

        string full = Path.GetFullPath(path);
        string format = MatlabImageFormat(path);
        var entries = new Dictionary<string, JgsValue>[pages.Count];
        for (int i = 0; i < pages.Count; i++)
        {
            entries[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Filename"] = JgsValue.Str(full),
                ["FileSize"] = JgsValue.Number(bytes.Length),
                ["Format"] = JgsValue.Str(format),
                ["Width"] = JgsValue.Number(pages[i].Width),
                ["Height"] = JgsValue.Number(pages[i].Height),
                ["BitDepth"] = JgsValue.Number(pages[i].BitDepth),
                ["ColorType"] = JgsValue.Str(pages[i].ColorType),
                ["NumberOfSamples"] = JgsValue.Number(pages[i].Samples),
                ["Comment"] = JgsValue.Cell([]),
            };
        }

        return JgsValue.StructArray(new JgsStructArray(entries, [.. entries[0].Keys]), entries.Length, 1);
    }

    /// <summary>
    /// imwrite's warning before a writer sees a signed class (R2025b's imwrite.m): every format but
    /// JPEG (refused there) and TIFF (writetif's own refusal) warns, and the writer then refuses.
    /// </summary>
    private static void WarnMatlabSignedPixels(JGraphScriptGlobals host, JgsValue image, string path)
    {
        if (ClassOf(image, JgsDialect.Matlab) is "int8" or "int16" or "int32" or "int64"
            && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".bmp" or ".gif")
        {
            Warn(host, "MATLAB:imagesci:imwrite:signedPixelData", "Data loss and unexpected results may occur with signed pixel data.");
        }
    }

    /// <summary>
    /// writegif in MATLAB code: the samples are indices (a double one-based, so <c>x - 1</c>; a logical
    /// or uint8 zero-based), clamped to the map after <c>dataOutOfRange</c> and rounded as uint8; without
    /// a map a logical picture gets a black-and-white table and anything else a 256-level grey ramp
    /// (an appended frame without one uses the file's table). A 3-D array is <c>badSize</c>. False for a
    /// 4-D array, which the general writer takes.
    /// </summary>
    private static bool MatlabGifWrite(
        JgsValue data, double[,]? map, string path, bool appendMode, ParsedArgs parsed, JGraphScriptGlobals host, int line, int col)
    {
        ImArray samples = ReadImArray(data);
        int dimensions = samples.Dims.Length;
        if (dimensions == 4 || !samples.HasData)
        {
            return false;
        }

        if (dimensions != 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:imagesci:writegif:badSize",
                $"{dimensions}-D data is not supported for GIF files. Image data must be either 2-D (single-frame) or 4-D "
                + "(multi-frame, with third dimension of 1), and either grayscale or indexed.");
        }

        bool appendExisting = appendMode && File.Exists(path);
        double[,]? table = map;
        if (table is null && !appendExisting)
        {
            int levels = samples.IsLogical ? 2 : 256;
            table = new double[levels, 3];
            for (int i = 0; i < levels; i++)
            {
                double level = i / (double)(levels - 1);
                table[i, 0] = table[i, 1] = table[i, 2] = level;
            }
        }

        int tableLength = table?.GetLength(0) ?? 256;
        bool oneBased = samples.ClassName == "double";
        int rows = samples.Dims[0];
        int cols = samples.Dims[1];
        var indices = new byte[rows * cols];
        bool outside = false;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                double x = samples.Data[(c * rows) + r] - (oneBased ? 1 : 0);
                if (x >= tableLength)
                {
                    x = tableLength - 1;
                    outside = true;
                }
                else if (x < 0)
                {
                    x = 0;
                    outside = true;
                }

                indices[(r * cols) + c] = (byte)JgsNumericClasses.Convert(x, JgsNumericClass.UInt8);
            }
        }

        if (outside)
        {
            Warn(host, "MATLAB:imagesci:writegif:dataOutOfRange", GifOutOfRangeText);
        }

        int delay = parsed.Named("DelayTime") is { } delayValue
            ? (int)Math.Round(NumOf("imwrite: DelayTime", delayValue, line, col) * 100, MidpointRounding.AwayFromZero)
            : 50;
        int loops = -1;
        if (!appendExisting && parsed.Named("LoopCount") is { } loopValue)
        {
            // writegif: 0 plays once (no loop block), Inf loops for ever (a count of 0), n repeats n times.
            double count = Math.Round(NumOf("imwrite: LoopCount", loopValue, line, col), MidpointRounding.AwayFromZero);
            loops = double.IsPositiveInfinity(count) ? 0 : count == 0 ? -1 : (int)Math.Clamp(count, 0, 65535);
        }

        try
        {
            GifEncoder.WriteIndexed(path, indices, cols, rows, table, delay, loops, appendExisting);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"imwrite: cannot write '{path}': {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// A BMP or GIF file's samples as R2025b's readbmp and readgif answer them in MATLAB code, or null
    /// for any other file: a BMP's indices or colours and its map (readbmp's warnings given on the
    /// way), a GIF frame's composed indices and the first frame's table.
    /// </summary>
    private static (ImageBuffer Image, double[,]? Palette, ImageBuffer? Alpha)? MatlabBmpOrGifSamples(
        string path, int frame, JGraphScriptGlobals? host)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            var warnings = new List<ImageFileWarning>();
            if (BmpFile.Read(bytes, warnings) is not { } read)
            {
                return null;
            }

            if (host is not null)
            {
                foreach (ImageFileWarning warning in warnings)
                {
                    Warn(host, warning.Identifier, warning.Text);
                }
            }

            return (read.Image, read.Map, null);
        }

        if (GifFile.IsGif(bytes))
        {
            (ImageBuffer indices, double[,] map) = GifFile.Read(bytes, frame);
            return (indices, map, null);
        }

        return null;
    }
}
