using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The text of a MATLAB code file as R2025b decodes one (open item 159): UTF-8 when every byte of
/// the file is valid UTF-8, its byte-order mark dropped; otherwise the whole file in Windows-1252,
/// the bytes Windows-1252 leaves undefined (0x81, 0x8D, 0x8F, 0x90, 0x9D) as the control characters
/// of the same value. Measured (the ADR on open items 159 and 161) through App Designer's own reader
/// of a plain-text app (<c>appdesigner.internal.cacheservice.readAppFile</c>, a builtin), through
/// <c>fileread</c>, and through the parser running a function whose literal holds the bytes: all
/// three give U+00E9 U+20AC for both <c>C3 A9 E2 82 AC</c> and <c>E9 80</c>, and a file mixing a
/// valid <c>C3 A9</c> with a lone <c>E9</c> back as U+00C3 U+00A9 U+00E9, every byte its own character.
/// </summary>
/// <remarks>
/// A file that begins with a UTF-16 or UTF-32 byte-order mark is read in that encoding, as this
/// build read every code file before (<see cref="File.ReadAllText(string)"/>); R2025b was not
/// measured on one.
/// </remarks>
public static class JgsSourceText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>What Windows-1252 makes of the bytes 0x80 to 0x9F; the rest of its bytes are their own code points.</summary>
    private static readonly char[] Windows1252High =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];

    /// <summary>The text of the file at <paramref name="path"/>, decoded as R2025b decodes a code file.</summary>
    public static string Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>The text <paramref name="bytes"/> hold, decoded as R2025b decodes a code file.</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
            || span.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        ReadOnlySpan<byte> body = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? span[3..] : span;
        try
        {
            return StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252(body);
        }
    }

    private static string Windows1252(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            text.Append(b is >= 0x80 and <= 0x9F ? Windows1252High[b - 0x80] : (char)b);
        }

        return text.ToString();
    }
}
