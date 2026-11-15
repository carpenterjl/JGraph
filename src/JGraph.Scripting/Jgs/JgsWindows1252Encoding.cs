using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Windows-1252 code page as R2025b reads and writes it (open items 303 and 318, ADR 0352 and ADR 0378):
/// one byte a character, 0x80 to 0x9F as Windows-1252 maps them and its five undefined bytes as the C1
/// controls. .NET has no code page 1252 without a provider, and ISO-8859-1, which this build used for the
/// name, differs in that row. A character it cannot write becomes <c>?</c>.
/// </summary>
internal sealed class JgsWindows1252Encoding : Encoding
{
    /// <summary>The one instance; the encoding keeps no state.</summary>
    internal static readonly JgsWindows1252Encoding Instance = new();

    private static readonly Dictionary<char, byte> Back = BuildBack();

    /// <inheritdoc />
    public override string WebName => "windows-1252";

    /// <inheritdoc />
    public override int GetByteCount(char[] chars, int index, int count) => count;

    /// <inheritdoc />
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        for (int i = 0; i < charCount; i++)
        {
            char c = chars[charIndex + i];
            bytes[byteIndex + i] = c < 0x80 || c is >= (char)0xA0 and <= (char)0xFF ? (byte)c
                : Back.TryGetValue(c, out byte b) ? b : (byte)'?';
        }

        return charCount;
    }

    /// <inheritdoc />
    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    /// <inheritdoc />
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        string text = JgsSourceText.Windows1252Text(bytes[byteIndex..(byteIndex + byteCount)]);
        text.CopyTo(0, chars, charIndex, text.Length);
        return text.Length;
    }

    /// <inheritdoc />
    public override int GetMaxByteCount(int charCount) => charCount;

    /// <inheritdoc />
    public override int GetMaxCharCount(int byteCount) => byteCount;

    private static Dictionary<char, byte> BuildBack()
    {
        var back = new Dictionary<char, byte>();
        string row = JgsSourceText.Windows1252Text([.. Enumerable.Range(0x80, 0x20).Select(static b => (byte)b)]);
        for (int i = 0; i < row.Length; i++)
        {
            back[row[i]] = (byte)(0x80 + i);
        }

        return back;
    }
}
