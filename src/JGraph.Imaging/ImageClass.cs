namespace JGraph.Imaging;

/// <summary>
/// The MATLAB numeric class an image's samples stand for. Storage is always normalized doubles in
/// [0, 1] (see <see cref="ImageBuffer"/>); this tag records what those samples <em>mean</em>, so
/// <c>imread</c> can hand back a <c>uint8</c> image whose pixels a MATLAB script reads as 0–255 while
/// the algorithms underneath keep working in one uniform range.
/// </summary>
/// <remarks>
/// Deliberately not a storage format. Making the buffer genuinely hold bytes would fork every
/// algorithm in this project by element type for no numerical gain; making the tag a lie would break
/// <c>class(I)</c>, saturation, and the <c>im2*</c> family. Recording the class beside normalized
/// storage keeps one algorithm layer and still answers every question MATLAB asks about class.
/// </remarks>
public enum ImageClass : byte
{
    /// <summary>Samples are their own value: [0, 1] doubles. The default for computed images.</summary>
    Double,

    /// <summary>As <see cref="Double"/>, but the script sees the class name <c>single</c>.</summary>
    Single,

    /// <summary>Samples stand for 0–255. What <c>imread</c> produces for an 8-bit file.</summary>
    UInt8,

    /// <summary>Samples stand for 0–65535 (16-bit PNG).</summary>
    UInt16,

    /// <summary>Samples stand for −32768–32767; the one class whose native range is offset from zero.</summary>
    Int16,

    /// <summary>Samples are 0 or 1 and the script sees <c>logical</c> — masks, edges, thresholds.</summary>
    Logical,
}

/// <summary>Native-range facts about an <see cref="ImageClass"/>: how [0, 1] maps to what a script sees.</summary>
public static class ImageClassInfo
{
    /// <summary>The width of the class's native range: 255 for uint8, 65535 for the 16-bit classes, 1 otherwise.</summary>
    public static double Scale(this ImageClass imageClass) => imageClass switch
    {
        ImageClass.UInt8 => 255.0,
        ImageClass.UInt16 or ImageClass.Int16 => 65535.0,
        _ => 1.0,
    };

    /// <summary>The class's native minimum. Only <see cref="ImageClass.Int16"/> starts below zero.</summary>
    public static double Offset(this ImageClass imageClass) =>
        imageClass == ImageClass.Int16 ? -32768.0 : 0.0;

    /// <summary>Whether the class stores whole numbers, so results must land on its sample grid.</summary>
    public static bool IsInteger(this ImageClass imageClass) =>
        imageClass is ImageClass.UInt8 or ImageClass.UInt16 or ImageClass.Int16;

    /// <summary>The name MATLAB's <c>class</c> reports for this tag.</summary>
    public static string MatlabName(this ImageClass imageClass) => imageClass switch
    {
        ImageClass.UInt8 => "uint8",
        ImageClass.UInt16 => "uint16",
        ImageClass.Int16 => "int16",
        ImageClass.Single => "single",
        ImageClass.Logical => "logical",
        _ => "double",
    };

    /// <summary>The tag a MATLAB class name selects, or null when the name is not an image class.</summary>
    public static ImageClass? FromMatlabName(string name) => name switch
    {
        "uint8" => ImageClass.UInt8,
        "uint16" => ImageClass.UInt16,
        "int16" => ImageClass.Int16,
        "single" => ImageClass.Single,
        "logical" => ImageClass.Logical,
        "double" => ImageClass.Double,
        _ => null,
    };

    /// <summary>Converts a normalized [0, 1] sample to the value a script sees for this class.</summary>
    public static double ToNative(this ImageClass imageClass, double sample) =>
        imageClass.IsInteger()
            ? Math.Round((sample * imageClass.Scale()) + imageClass.Offset(), MidpointRounding.AwayFromZero)
            : sample;

    /// <summary>Converts a script-visible value for this class back to a normalized [0, 1] sample.</summary>
    public static double FromNative(this ImageClass imageClass, double native) =>
        imageClass.IsInteger() ? (native - imageClass.Offset()) / imageClass.Scale() : native;

    /// <summary>
    /// Snaps every sample onto the class's representable grid, in place. An integer-class result in
    /// MATLAB is rounded to whole units, so <c>immultiply(uint8Image, 0.5)</c> lands on multiples of
    /// 1/255 rather than somewhere between them; without this the tag would claim a precision the
    /// samples do not have.
    /// </summary>
    public static void Quantize(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.Class.IsInteger())
        {
            return;
        }

        double scale = image.Class.Scale();
        Span<double> pixels = image.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Math.Round(pixels[i] * scale, MidpointRounding.AwayFromZero) / scale;
        }

        GC.KeepAlive(image);
    }
}
