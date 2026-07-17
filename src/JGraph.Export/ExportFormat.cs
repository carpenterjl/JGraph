namespace JGraph.Export;

/// <summary>A file format a figure can be exported to.</summary>
public enum ExportFormat
{
    /// <summary>Raster PNG (lossless, supports transparency).</summary>
    Png,

    /// <summary>Raster JPEG (lossy; see <see cref="ExportOptions.JpegQuality"/>).</summary>
    Jpeg,

    /// <summary>Raster Windows bitmap (32-bit, uncompressed).</summary>
    Bmp,

    /// <summary>Raster baseline TIFF (RGB, uncompressed).</summary>
    Tiff,

    /// <summary>Scalable Vector Graphics — true vector output for the web and editing tools.</summary>
    Svg,

    /// <summary>PDF — true vector output for publications; page size matches the figure's physical size.</summary>
    Pdf,
}

/// <summary>Helpers for <see cref="ExportFormat"/>.</summary>
public static class ExportFormats
{
    /// <summary>True for formats rendered to pixels (where <see cref="ExportOptions.Scale"/> applies).</summary>
    public static bool IsRaster(this ExportFormat format) => format is
        ExportFormat.Png or ExportFormat.Jpeg or ExportFormat.Bmp or ExportFormat.Tiff;

    /// <summary>Maps a file extension (with or without the leading dot, any case) to a format.</summary>
    public static ExportFormat FromExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ExportFormat.Png,
            "jpg" or "jpeg" => ExportFormat.Jpeg,
            "bmp" => ExportFormat.Bmp,
            "tif" or "tiff" => ExportFormat.Tiff,
            "svg" => ExportFormat.Svg,
            "pdf" => ExportFormat.Pdf,
            var other => throw new NotSupportedException(
                $"'{other}' is not a supported export format (png, jpg, bmp, tif, svg, pdf)."),
        };
    }

    /// <summary>Maps a file path's extension to a format.</summary>
    public static ExportFormat FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            throw new NotSupportedException($"'{path}' has no file extension to infer an export format from.");
        }

        return FromExtension(extension);
    }
}
