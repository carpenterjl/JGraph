using System.Security.Cryptography;
using System.Text;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// JGraph's P-code file (the compiler and SDK plan's stage K3b, ADR 0259): what <c>pcode</c> writes
/// for a <c>.m</c> file and what the path runs in its place. It is the file's tokens as the lexer
/// read them, with the comments gone and every position 0, so a P-file runs as its source did and
/// reports line 0 in its error frames, as R2025b's do. It is JGraph's own format, not MathWorks':
/// a P-file R2025b wrote is refused by name, and nothing here is encrypted (decision of 2026-10-24),
/// so a P-file is a precompiled form, not a protection.
/// </summary>
/// <remarks>
/// <para>Layout: the 16-byte header <c>jgraph-pcode-01\n</c>; a string table (a count, then each
/// string as UTF-8 with its length before it, the lengths and counts 7-bit encoded); the tokens (a
/// count, then for each the index of its type's name in the table, a flags byte - preceded by
/// whitespace, a char literal - the index of its text, and for a number its value as an IEEE
/// double); last the SHA-256 of everything before it. Types are stored by name so a reordered
/// <see cref="TokenType"/> still reads an old file. The bytes depend on the source alone, so the
/// same source gives the same file twice (R2025b's are deterministic too, measured).</para>
/// <para>Measured in R2025b (probe_k3b_pcode): a file shorter than a P-file can be, or one that is
/// not a P-file at all, is <c>MATLAB:shortPfileFormat</c> with its path; one whose bytes were
/// changed is <c>MATLAB:corruptPfile_3</c> without it.</para>
/// </remarks>
internal static class JgsPcode
{
    /// <summary>The extension of a P-code file.</summary>
    public const string Extension = ".p";

    private static readonly byte[] Header = Encoding.ASCII.GetBytes("jgraph-pcode-01\n");

    private const int HashLength = 32;

    private const byte SpaceBefore = 1;
    private const byte CharLiteral = 2;

    /// <summary>Whether a path names a P-code file, by its extension.</summary>
    public static bool IsPcode(string path) =>
        Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The P-code of <paramref name="source"/>: its tokens with every position 0. The source is
    /// parsed first, so a file that does not parse raises its syntax error and nothing is made
    /// (R2025b's pcode checks the file first too).
    /// </summary>
    /// <exception cref="JgsSyntaxException">The source does not lex or parse.</exception>
    public static byte[] Compile(string source, string sourceId)
    {
        IReadOnlyList<Token> tokens = Lexer.Tokenize(source, tolerant: false, JgsDialect.Matlab);
        Parser.Parse(tokens, sourceId, JgsDialect.Matlab);

        var strings = new List<string>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string text)
        {
            if (!indexOf.TryGetValue(text, out int index))
            {
                index = strings.Count;
                strings.Add(text);
                indexOf[text] = index;
            }

            return index;
        }

        var body = new MemoryStream();
        using (var tokenWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            tokenWriter.Write7BitEncodedInt(tokens.Count);
            foreach (Token token in tokens)
            {
                tokenWriter.Write7BitEncodedInt(Intern(token.Type.ToString()));
                tokenWriter.Write((byte)((token.PrecededByWhitespace ? SpaceBefore : 0) | (token.IsCharLiteral ? CharLiteral : 0)));
                tokenWriter.Write7BitEncodedInt(Intern(token.Text));
                if (token.Type is TokenType.Number or TokenType.ImaginaryNumber)
                {
                    tokenWriter.Write(token.Number);
                }
            }
        }

        var file = new MemoryStream();
        file.Write(Header);
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(strings.Count);
            foreach (string text in strings)
            {
                writer.Write(text);
            }
        }

        body.Position = 0;
        body.CopyTo(file);
        file.Write(SHA256.HashData(file.GetBuffer().AsSpan(0, (int)file.Length)));
        return file.ToArray();
    }

    /// <summary>
    /// The program a P-code file holds, every statement stamped with <paramref name="path"/>.
    /// </summary>
    /// <exception cref="JgsRuntimeException">The file is not one of JGraph's P-files, or is damaged,
    /// in R2025b's words; or it is one MATLAB's pcode wrote, which JGraph cannot read.</exception>
    public static IReadOnlyList<Stmt> Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (IsMatlabPfile(bytes))
        {
            throw new JgsRuntimeException(0, 0, "JGraph:pcode:MatlabPfile",
                $"Unable to run P-code file '{path}'. It was written by MATLAB's pcode, and JGraph runs only the P-code files its own pcode writes.");
        }

        if (bytes.Length < Header.Length + HashLength || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
        {
            throw Short(path);
        }

        int bodyEnd = bytes.Length - HashLength;
        if (!SHA256.HashData(bytes.AsSpan(0, bodyEnd)).AsSpan().SequenceEqual(bytes.AsSpan(bodyEnd)))
        {
            throw Corrupt();
        }

        List<Token> tokens;
        try
        {
            tokens = ReadTokens(bytes, bodyEnd);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException
            or ArgumentException or InvalidDataException or DecoderFallbackException)
        {
            throw Corrupt();
        }

        try
        {
            return Parser.Parse(tokens, path, JgsDialect.Matlab);
        }
        catch (JgsSyntaxException)
        {
            // Every P-file pcode wrote parsed when it was written: one that no longer does was not
            // written by this pcode, whatever its hash says.
            throw Corrupt();
        }
    }

    private static List<Token> ReadTokens(byte[] bytes, int bodyEnd)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes, Header.Length, bodyEnd - Header.Length), Encoding.UTF8);
        int stringCount = reader.Read7BitEncodedInt();
        if (stringCount < 0 || stringCount > bodyEnd)
        {
            throw new InvalidDataException("string count");
        }

        var strings = new string[stringCount];
        for (int i = 0; i < stringCount; i++)
        {
            strings[i] = reader.ReadString();
        }

        string At(int index) => index >= 0 && index < strings.Length ? strings[index] : throw new InvalidDataException("string index");

        int tokenCount = reader.Read7BitEncodedInt();
        if (tokenCount <= 0 || tokenCount > bodyEnd)
        {
            throw new InvalidDataException("token count");
        }

        var tokens = new List<Token>(tokenCount);
        for (int i = 0; i < tokenCount; i++)
        {
            TokenType type = Enum.Parse<TokenType>(At(reader.Read7BitEncodedInt()), ignoreCase: false);
            byte flags = reader.ReadByte();
            string text = At(reader.Read7BitEncodedInt());
            double number = type is TokenType.Number or TokenType.ImaginaryNumber ? reader.ReadDouble() : 0;
            tokens.Add(new Token(type, text, number, 0, 0, (flags & SpaceBefore) != 0, (flags & CharLiteral) != 0));
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length || tokens[^1].Type != TokenType.Eof)
        {
            throw new InvalidDataException("trailing bytes");
        }

        return tokens;
    }

    /// <summary>
    /// <paramref name="build"/> as <c>mcc -j</c> makes it: each <c>.m</c> file stored as its P-file
    /// beside where it was (the main and every public function's file too), and the manifest's
    /// protection <see cref="CtfProtection.PCode"/>. A file with no code in it, such as a
    /// <c>Contents.m</c> of comments, stays as it is, as R2025b leaves one (k3b_run).
    /// </summary>
    /// <exception cref="JgsSyntaxException">A code file does not parse; the build stops there.</exception>
    internal static CtfBuild PcodeBuild(CtfBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var files = new List<(string Source, string ArchivePath)>();
        var made = new List<(byte[] Contents, string ArchivePath)>(build.MadeFiles);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string source, string archivePath) in build.Files)
        {
            string text;
            if (!archivePath.EndsWith(".m", StringComparison.OrdinalIgnoreCase)
                || !HasCode(text = File.ReadAllText(source)))
            {
                files.Add((source, archivePath));
                continue;
            }

            string pfile = archivePath[..^2] + Extension;
            made.Add((Compile(text, source), pfile));
            renamed[archivePath] = pfile;
        }

        return new CtfBuild
        {
            ComponentName = build.ComponentName,
            TargetType = build.TargetType,
            Uuid = build.Uuid,
            Files = files,
            MadeFiles = made,
            MatlabPath = build.MatlabPath,
            PublicFunctions = [.. build.PublicFunctions.Select(f => renamed.TryGetValue(f.File, out string? p) ? f with { File = p } : f)],
            MainFunction = build.MainFunction,
            NumericArguments = build.NumericArguments,
            RuntimeOptions = build.RuntimeOptions,
            MetaFiles = build.MetaFiles,
            Created = build.Created,
            Protection = CtfProtection.PCode,
        };
    }

    /// <summary>Whether a source holds any statement at all, rather than comments and blank lines.</summary>
    private static bool HasCode(string source) =>
        Lexer.Tokenize(source, tolerant: true, JgsDialect.Matlab)
            .Any(t => t.Type is not (TokenType.Newline or TokenType.Semicolon or TokenType.Comma or TokenType.Eof));

    /// <summary>A P-file R2025b's pcode wrote: its header is <c>vNN.NNvNN.NN</c>.</summary>
    private static bool IsMatlabPfile(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        for (int i = 0; i < 12; i++)
        {
            char c = (char)bytes[i];
            bool ok = i is 0 or 6 ? c == 'v' : i is 3 or 9 ? c == '.' : char.IsAsciiDigit(c);
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static JgsRuntimeException Short(string path) =>
        new(0, 0, "MATLAB:shortPfileFormat", $"Unable to run P-code file '{path}'. The file is damaged.");

    private static JgsRuntimeException Corrupt() =>
        new(0, 0, "MATLAB:corruptPfile_3", "Unable to run P-code file. The file is damaged.");
}
