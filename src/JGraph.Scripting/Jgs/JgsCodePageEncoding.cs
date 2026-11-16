using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// An encoding R2025b's <c>fopen</c> names (open item 350, ADR 0385): the name its <c>fopen(fid)</c> reports, and
/// whether it opens the file without the <c>MATLAB:iofun:UnsupportedEncoding</c> warning.
/// </summary>
internal abstract class JgsMatlabEncoding : Encoding
{
    protected JgsMatlabEncoding(string matlabName, bool supported)
    {
        MatlabName = matlabName;
        Supported = supported;
    }

    /// <summary>The name R2025b's <c>fopen(fid)</c> gives the encoding (<c>Shift_JIS</c>, <c>iso-8859_11-2001</c>).</summary>
    internal string MatlabName { get; }

    /// <summary>False for a name R2025b opens with its "is not supported" warning (UTF-16, IBM437, ISO-2022-JP).</summary>
    internal bool Supported { get; }

    public override string WebName => MatlabName;

    public override string EncodingName => MatlabName;

    public override byte[] GetPreamble() => [];

    /// <summary>
    /// The encoding R2025b's <c>fopen</c> means by <paramref name="name"/> (ICU's alias rule: case, '-', '_' and
    /// spaces do not count), or null for one it refuses (<c>MATLAB:iofun:InvalidEncoding</c>). Only the names this
    /// table holds; UTF-8, ISO-8859-1, windows-1252, US-ASCII and UTF-16LE/BE are <see cref="JGraphScriptGlobals.EncodingNamed"/>'s.
    /// </summary>
    internal static JgsMatlabEncoding? Named(string name)
    {
        string key = Key(name);
        return Aliases.TryGetValue(key, out string? canonical) ? Make(canonical) : null;
    }

    /// <summary>The alias key of a name: lower case, letters and digits only.</summary>
    internal static string Key(string name)
    {
        var key = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
        }

        return key.ToString();
    }

    private static readonly Dictionary<string, JgsMatlabEncoding> Made = new(StringComparer.Ordinal);

    private static JgsMatlabEncoding? Make(string canonical)
    {
        lock (Made)
        {
            if (Made.TryGetValue(canonical, out JgsMatlabEncoding? held))
            {
                return held;
            }

            JgsMatlabEncoding? made = JgsCodePageTables.Pages.TryGetValue(canonical, out (string Decoded, byte Substitute) page)
                ? new JgsSingleByteEncoding(canonical, page.Decoded, page.Substitute, supported: canonical is not ("IBM437" or "IBM850"))
                : canonical switch
                {
                    // The double-byte pages, from .NET's code page provider, with the bytes R2025b writes for a
                    // character a page does not hold (unicode2native, measured): sub1 for one up to U+00FF.
                    "Shift_JIS" => Dbcs(canonical, 932, [0xFC, 0xFC]),
                    "GBK" => Dbcs(canonical, 936, [(byte)'?']),
                    "GB2312" => Dbcs(canonical, 20936, [0xA1, 0xA1]),
                    "GB18030" => Dbcs(canonical, 54936, [0x1A]),
                    "Big5" => Dbcs(canonical, 950, [(byte)'?']),
                    "Big5-HKSCS" => Dbcs(canonical, 950, [0xA1, 0x48]),
                    "KSC_5601" => Dbcs(canonical, 949, [(byte)'?']),
                    "EUC-KR" => Dbcs(canonical, 51949, [0xAF, 0xFE], sub1: [0x1A], unmapped: '€'),
                    "ibm-949_P110-1999" => Dbcs(canonical, 949, [0xAF, 0xFE], sub1: [0x7F], unmapped: '€'),
                    "EUC-JP" => Dbcs(canonical, 51932, [0xF4, 0xFE]),
                    "ibm-950_P110-1999" => Dbcs(canonical, 950, [0x7F], supported: false),
                    "ISO-2022-JP" => Dbcs(canonical, 50220, [0x1A], supported: false),
                    "UTF-16" => new JgsCodePageEncoding(canonical, new UnicodeEncoding(false, false, true), [0xFD, 0xFF], null, null, supported: false),
                    "UTF-32" => new JgsCodePageEncoding(canonical, new UTF32Encoding(false, false, true), [0xFD, 0xFF, 0, 0], null, null, supported: false),
                    "UTF-32LE" => new JgsCodePageEncoding(canonical, new UTF32Encoding(false, false, true), [0xFD, 0xFF, 0, 0], null, null, supported: true),
                    "UTF-32BE" => new JgsCodePageEncoding(canonical, new UTF32Encoding(true, false, true), [0, 0, 0xFF, 0xFD], null, null, supported: true),
                    _ => null,
                };
            if (made is not null)
            {
                Made[canonical] = made;
            }

            return made;
        }

        static JgsMatlabEncoding Dbcs(string canonical, int page, byte[] sub, byte[]? sub1 = null, char? unmapped = null, bool supported = true)
        {
            Encoding strict = CodePagesEncodingProvider.Instance.GetEncoding(page, EncoderFallback.ExceptionFallback, new DecoderReplacementFallback("�"))
                ?? throw new InvalidOperationException($"The code page {page} is not available.");
            return new JgsCodePageEncoding(canonical, strict, sub, sub1, unmapped, supported);
        }
    }

    /// <summary>R2025b's names (the aliases measured, and the canonical spellings) by alias key, to the name <c>fopen(fid)</c> reports.</summary>
    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string canonical, params string[] names)
        {
            aliases[Key(canonical)] = canonical;
            foreach (string name in names)
            {
                aliases[Key(name)] = canonical;
            }
        }

        foreach (string page in JgsCodePageTables.Pages.Keys)
        {
            Add(page);
        }

        foreach (int n in (int[])[2, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14, 15])
        {
            Add($"ISO-8859-{n}", $"ISO8859-{n}");
        }

        foreach (int n in (int[])[1250, 1251, 1253, 1254, 1255, 1256, 1257, 1258])
        {
            Add($"windows-{n}", $"cp{n}");
        }

        Add("iso-8859_11-2001", "ISO-8859-11", "ISO8859-11");
        Add("windows-874", "TIS-620");
        Add("macintosh", "x-mac-roman", "macroman");
        Add("IBM437", "cp437");
        Add("IBM850");
        Add("IBM866");
        Add("KOI8-R");
        Add("KOI8-U");
        Add("Shift_JIS", "sjis", "cp932", "ms932", "windows-31j", "windows-932");
        Add("GBK", "cp936", "windows-936");
        Add("GB2312", "EUC-CN");
        Add("GB18030");
        Add("Big5", "windows-950");
        Add("Big5-HKSCS");
        Add("KSC_5601", "windows-949");
        Add("EUC-KR");
        Add("ibm-949_P110-1999", "CP949");
        Add("EUC-JP");
        Add("ibm-950_P110-1999", "cp950");
        Add("ISO-2022-JP");
        Add("UTF-16");
        Add("UTF-32");
        Add("UTF-32LE");
        Add("UTF-32BE");
        return aliases;
    }
}

/// <summary>
/// A single-byte code page as R2025b's ICU converter reads and writes it (open item 350, ADR 0385): each byte its
/// measured character (U+FFFD for one the page leaves undefined), each character its byte, and the page's
/// substitution byte (0x1A, '?' or 0x7F) for a character it does not hold.
/// </summary>
internal sealed class JgsSingleByteEncoding : JgsMatlabEncoding
{
    private readonly string _decoded;
    private readonly byte _substitute;
    private readonly Dictionary<char, byte> _bytes = [];

    internal JgsSingleByteEncoding(string matlabName, string decoded, byte substitute, bool supported)
        : base(matlabName, supported)
    {
        _decoded = decoded;
        _substitute = substitute;
        for (int b = 0; b < decoded.Length; b++)
        {
            if (decoded[b] != '�')
            {
                _bytes.TryAdd(decoded[b], (byte)b);
            }
        }
    }

    public override bool IsSingleByte => true;

    public override int GetByteCount(char[] chars, int index, int count)
    {
        int bytes = 0;
        for (int i = index; i < index + count; i++)
        {
            if (char.IsHighSurrogate(chars[i]) && i + 1 < index + count && char.IsLowSurrogate(chars[i + 1]))
            {
                i++; // a pair is one character the page does not hold
            }

            bytes++;
        }

        return bytes;
    }

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        int at = byteIndex;
        for (int i = charIndex; i < charIndex + charCount; i++)
        {
            char c = chars[i];
            if (char.IsHighSurrogate(c) && i + 1 < charIndex + charCount && char.IsLowSurrogate(chars[i + 1]))
            {
                i++;
                bytes[at++] = _substitute;
                continue;
            }

            bytes[at++] = _bytes.TryGetValue(c, out byte b) ? b : _substitute;
        }

        return at - byteIndex;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        for (int i = 0; i < byteCount; i++)
        {
            chars[charIndex + i] = _decoded[bytes[byteIndex + i]];
        }

        return byteCount;
    }

    public override int GetMaxByteCount(int charCount) => charCount;

    public override int GetMaxCharCount(int byteCount) => byteCount;
}

/// <summary>
/// A multi-byte code page (open item 350, ADR 0385): .NET's page decodes (an invalid sequence U+FFFD) and encodes
/// what it holds; a character it does not hold - or that R2025b's page does not (<c>€</c> in EUC-KR) - is written
/// as R2025b's substitution bytes, <c>sub1</c> for one up to U+00FF where the page has one.
/// </summary>
internal sealed class JgsCodePageEncoding : JgsMatlabEncoding
{
    private readonly Encoding _strict;
    private readonly byte[] _sub;
    private readonly byte[]? _sub1;
    private readonly char? _unmapped;

    internal JgsCodePageEncoding(string matlabName, Encoding strict, byte[] sub, byte[]? sub1, char? unmapped, bool supported)
        : base(matlabName, supported)
    {
        _strict = strict;
        _sub = sub;
        _sub1 = sub1;
        _unmapped = unmapped;
    }

    public override bool IsSingleByte => false;

    private byte[] Encode(char[] chars, int index, int count)
    {
        if (_unmapped is not { } never || Array.IndexOf(chars, never, index, count) < 0)
        {
            try
            {
                return _strict.GetBytes(chars, index, count);
            }
            catch (EncoderFallbackException)
            {
                // a character the page does not hold: written run by run below
            }
        }

        var written = new List<byte>(count * 2);
        int run = index;
        int end = index + count;
        for (int i = index; i < end;)
        {
            int width = char.IsHighSurrogate(chars[i]) && i + 1 < end && char.IsLowSurrogate(chars[i + 1]) ? 2 : 1;
            if (chars[i] == _unmapped || !Holds(chars, i, width))
            {
                if (i > run)
                {
                    written.AddRange(_strict.GetBytes(chars, run, i - run));
                }

                written.AddRange(width == 1 && chars[i] <= 'ÿ' && _sub1 is { } single ? single : _sub);
                run = i + width;
            }

            i += width;
        }

        if (end > run)
        {
            written.AddRange(_strict.GetBytes(chars, run, end - run));
        }

        return [.. written];
    }

    private bool Holds(char[] chars, int index, int width)
    {
        try
        {
            _strict.GetByteCount(chars, index, width);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public override int GetByteCount(char[] chars, int index, int count) => Encode(chars, index, count).Length;

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        byte[] encoded = Encode(chars, charIndex, charCount);
        encoded.CopyTo(bytes, byteIndex);
        return encoded.Length;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => _strict.GetCharCount(bytes, index, count);

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
        _strict.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

    public override Decoder GetDecoder() => _strict.GetDecoder();

    public override int GetMaxByteCount(int charCount) => _strict.GetMaxByteCount(charCount) + (charCount * _sub.Length);

    public override int GetMaxCharCount(int byteCount) => _strict.GetMaxCharCount(byteCount);
}
