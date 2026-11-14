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

    /// <summary>
    /// The text <paramref name="bytes"/> hold as R2025b's <c>fileread</c> reads them: <c>fopen</c>
    /// with no encoding named, which detects one (open item 303, ADR 0352, 49 files measured).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is ICU's UTF-8 recogniser over the whole file. It counts the complete multi-byte
    /// sequences (by their lead and trail bits alone, so an overlong form or a surrogate counts) and
    /// the broken ones: a byte that cannot lead, or a lead whose trail stops early. A sequence the
    /// file's end cuts short is neither. A file starting with the UTF-8 byte-order mark counts the
    /// mark as one sequence. The file is UTF-8 when nothing is broken and something is valid or the
    /// mark is there, or when the mark is there and the valid outnumber the broken more than ten to
    /// one. Every other file, plain ASCII included, is Windows-1252, the system's encoding here.
    /// </para>
    /// <para>
    /// Read as UTF-8, a broken sequence is U+FFFD (as .NET decodes it), the byte-order mark stays as
    /// U+FEFF, and a character past the basic plane is its surrogate pair. R2025b writes U+001A for
    /// one, losing it: a defect of its own (ADR 0338 saw it too), and a divergence here. A UTF-16
    /// byte-order mark reads the file in that order, its mark kept. Measured cases:
    /// <c>C3 A9 E9</c> (the E9 cut short by the end) is U+00E9 U+FFFD; <c>C3 A9 E9 41</c> is four
    /// Windows-1252 characters; eleven <c>C3 A9</c> and one lone <c>80</c> are Windows-1252, but the
    /// mark, ten <c>C3 A9</c> and a lone <c>80</c> are UTF-8.
    /// </para>
    /// <para>
    /// This is not <see cref="Decode"/>'s rule for a code file, which needs every byte valid and
    /// drops the mark (ADR 0338). Whether R2025b's parser follows ICU too was not measured.
    /// </para>
    /// </remarks>
    public static string DecodeDetected(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes);
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        return RecognisedAsUtf8(span)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes)
            : Windows1252(span);
    }

    /// <summary>The bytes as Windows-1252 text, every byte one character (the undefined five as C1 controls).</summary>
    internal static string Windows1252Text(byte[] bytes) => Windows1252(bytes);

    /// <summary>ICU's UTF-8 recogniser (<c>CharsetRecog_UTF8::match</c>) at a confidence of 80 or more.</summary>
    private static bool RecognisedAsUtf8(ReadOnlySpan<byte> bytes)
    {
        bool mark = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
        int valid = 0;
        int broken = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            int b = bytes[i];
            if ((b & 0x80) == 0)
            {
                continue;
            }

            int trail;
            if ((b & 0xE0) == 0xC0)
            {
                trail = 1;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                trail = 2;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                trail = 3;
            }
            else
            {
                broken++;
                continue;
            }

            while (true)
            {
                i++;
                if (i >= bytes.Length)
                {
                    break;
                }

                if ((bytes[i] & 0xC0) != 0x80)
                {
                    broken++; // the byte that broke it is not read as a lead (ICU steps past it)
                    break;
                }

                if (--trail == 0)
                {
                    valid++;
                    break;
                }
            }
        }

        return (mark && broken == 0) || (mark && valid > broken * 10) || (valid > 0 && broken == 0);
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
