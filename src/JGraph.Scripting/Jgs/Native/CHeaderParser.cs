using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// Reads a preprocessed C header (the compiler's <c>/E</c> or <c>-E</c> output, line markers kept) into
/// the prototype model R2025b's <c>loadlibrary</c> builds from it (ADR 0181).
/// </summary>
/// <remarks>
/// <para>
/// It parses the whole translation unit, because <c>windows.h</c> has to go through, but records
/// functions, exported variables, structs and enums only from the named headers — the header given
/// and each <c>addheader</c> — which it knows by the line markers. Types are resolved wherever they
/// were declared. MSVC's and GCC's extensions are skipped: <c>__declspec</c>, <c>__attribute__</c>,
/// <c>__pragma</c>, calling conventions, pointer qualifiers, <c>_Static_assert</c> and function bodies.
/// <c>#pragma pack</c> is followed, so a struct records its packing.
/// </para>
/// <para>
/// The MATLAB names are the ones R2025b's parser writes (the prototype files it wrote for
/// <c>jgtestlib.h</c> and <c>probe_shrlib_parse.h</c>): <c>char*</c> and <c>signed char*</c> are
/// <c>cstring</c> when written so, but <c>int8_t*</c> (the same type through a typedef) and a
/// <c>char s[16]</c> parameter are <c>int8Ptr</c>; <c>char**</c> is
/// <c>stringPtrPtr</c>; a struct is named by its tag, or by its typedef when it has none; a struct or
/// union it cannot use — a union, a struct with bitfields — leaves its typedef unknown, so the type
/// is <c>error</c> by value and <c>voidPtr</c> by pointer, each with R2025b's warning; <c>long
/// double</c> is <c>error</c>; a varargs function ends its parameters with <c>error</c> and has the
/// call type <c>cdecl</c>; a parameter written as a function pointer (rather than through a typedef)
/// drops its function, as R2025b drops it.
/// </para>
/// </remarks>
internal sealed class CHeaderParser
{
    private readonly List<Token> _tokens;
    private readonly List<string> _files;
    private readonly bool[] _named;
    private int _pos;

    private readonly Dictionary<string, CType> _typedefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Aggregate> _structs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Aggregate> _unions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Aggregate> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _constants = new(StringComparer.Ordinal);
    private readonly List<Aggregate> _defined = [];
    private readonly Stack<int> _packStack = new();
    private int _pack = 8;

    private readonly LibraryModel _model = new();
    private readonly List<(string Message, Token At)> _warnings = [];
    private readonly HashSet<string> _seenFunctions = new(StringComparer.Ordinal);

    private CHeaderParser(List<Token> tokens, List<string> files, bool[] named)
    {
        _tokens = tokens;
        _files = files;
        _named = named;
    }

    /// <summary>The result of <see cref="Parse"/>.</summary>
    /// <param name="Model">The functions, structs and enums of the named headers.</param>
    /// <param name="Warnings">The parser's warnings, as <c>[notfound, warnings] = loadlibrary(…)</c> answers them.</param>
    /// <param name="WarningCount">How many warnings the text holds; loadlibrary says so when it is not asked for the text.</param>
    public sealed record Result(LibraryModel Model, string Warnings, int WarningCount);

    /// <summary>
    /// Parses <paramref name="preprocessed"/>, recording what the files named <paramref name="headers"/>
    /// declare (by file name, case-insensitively; an entry without an extension also names that
    /// name with <c>.h</c>). <paramref name="label"/> heads the warnings text, as R2025b heads it
    /// with the header's name.
    /// </summary>
    public static Result Parse(string preprocessed, IReadOnlyCollection<string> headers, string label)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string header in headers)
        {
            string file = Path.GetFileName(header);
            names.Add(file);
            if (Path.GetExtension(file).Length == 0)
            {
                names.Add(file + ".h");
            }
        }

        (List<Token> tokens, List<string> files) = Tokenize(preprocessed);
        bool[] named = files.Select(f => names.Contains(Path.GetFileName(f))).ToArray();
        var parser = new CHeaderParser(tokens, files, named);
        parser.ParseUnit();
        parser.RecordAggregates();
        return new Result(parser._model, parser.WarningText(label), parser._warnings.Count);
    }

    // ------------------------------------------------------------------------------------------
    // Tokens
    // ------------------------------------------------------------------------------------------

    private enum TokenKind : byte
    {
        Identifier,
        Number,
        Text,
        Character,
        Punctuator,
        Pack,
        End,
    }

    /// <summary>A token: its text, the file and line it came from, and its line in the preprocessed input.</summary>
    private readonly record struct Token(TokenKind Kind, string Text, int File, int Line, int InputLine);

    private static readonly string[] Punctuators = ["...", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "->", "##", "::"];

    private static (List<Token> Tokens, List<string> Files) Tokenize(string text)
    {
        var tokens = new List<Token>(text.Length / 6);
        var files = new List<string> { "" };
        var fileIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""] = 0 };
        int file = 0;
        int line = 1;
        int inputLine = 1;
        int i = 0;
        bool lineStart = true;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                line++;
                inputLine++;
                lineStart = true;
                i++;
                continue;
            }

            if (c is ' ' or '\t' or '\r' or '\f' or '\v')
            {
                i++;
                continue;
            }

            if (c == '#' && lineStart)
            {
                int end = text.IndexOf('\n', i);
                if (end < 0)
                {
                    end = text.Length;
                }

                string directive = text[(i + 1)..end].Trim();
                if (LineMarker(directive) is { } marker)
                {
                    if (!fileIndex.TryGetValue(marker.File, out file))
                    {
                        file = files.Count;
                        files.Add(marker.File);
                        fileIndex[marker.File] = file;
                    }

                    line = marker.Line - 1; // the newline ending the directive moves to it
                }
                else if (directive.StartsWith("pragma", StringComparison.Ordinal))
                {
                    string body = directive[6..].Trim();
                    if (body.StartsWith("pack", StringComparison.Ordinal))
                    {
                        tokens.Add(new Token(TokenKind.Pack, PackArguments(body[4..]), file, line, inputLine));
                    }
                }

                i = end;
                continue;
            }

            lineStart = false;
            int start = i;
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = close < 0 ? text.Length : close + 2;
                for (int k = i; k < stop; k++)
                {
                    if (text[k] == '\n')
                    {
                        line++;
                        inputLine++;
                    }
                }

                i = stop;
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                {
                    i++;
                }

                string word = text[start..i];
                if (word is "L" or "u8" or "u" or "U" && i < text.Length && text[i] is '"' or '\'')
                {
                    continue; // a wide or UTF prefix: the literal follows as its own token
                }

                tokens.Add(new Token(TokenKind.Identifier, word, file, line, inputLine));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.'
                    || ((text[i] is '+' or '-') && text[i - 1] is 'e' or 'E' or 'p' or 'P' && !text[start..i].StartsWith("0x", StringComparison.OrdinalIgnoreCase))))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], file, line, inputLine));
                continue;
            }

            if (c is '"' or '\'')
            {
                i++;
                while (i < text.Length && text[i] != c && text[i] != '\n')
                {
                    i += text[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(i + 1, text.Length);
                tokens.Add(new Token(c == '"' ? TokenKind.Text : TokenKind.Character, text[start..i], file, line, inputLine));
                continue;
            }

            string? punctuator = null;
            foreach (string p in Punctuators)
            {
                if (string.CompareOrdinal(text, i, p, 0, p.Length) == 0)
                {
                    punctuator = p;
                    break;
                }
            }

            punctuator ??= c.ToString();
            i += punctuator.Length;
            tokens.Add(new Token(TokenKind.Punctuator, punctuator, file, line, inputLine));
        }

        tokens.Add(new Token(TokenKind.End, "", file, line, inputLine));
        return (tokens, files);
    }

    /// <summary><c>line 12 "file"</c> (MSVC) or <c>12 "file" 2</c> (GCC): the line and file it names.</summary>
    private static (int Line, string File)? LineMarker(string directive)
    {
        string rest = directive.StartsWith("line", StringComparison.Ordinal) ? directive[4..].TrimStart() : directive;
        int digits = 0;
        while (digits < rest.Length && char.IsDigit(rest[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(rest.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return null;
        }

        int open = rest.IndexOf('"', digits);
        int close = open < 0 ? -1 : rest.IndexOf('"', open + 1);
        while (close > 0 && rest[close - 1] == '\\' && (close < 2 || rest[close - 2] != '\\'))
        {
            close = rest.IndexOf('"', close + 1);
        }

        if (open < 0 || close < 0)
        {
            return null;
        }

        string name = rest[(open + 1)..close].Replace("\\\\", "\\", StringComparison.Ordinal);
        return (number, name);
    }

    /// <summary>The text inside <c>pack( … )</c>, or empty.</summary>
    private static string PackArguments(string afterPack)
    {
        int open = afterPack.IndexOf('(');
        int close = afterPack.LastIndexOf(')');
        return open >= 0 && close > open ? afterPack[(open + 1)..close].Trim() : "";
    }

    // ------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------

    private enum Kind : byte
    {
        Primitive,
        Struct,
        Union,
        Enum,
        Pointer,
        Array,
        Function,
        Unknown,
    }

    /// <summary>A struct, union or enum: its tag, the typedef that names it when it has none, and what it holds.</summary>
    private sealed class Aggregate(Kind kind, string? tag, Token at)
    {
        public Kind Kind { get; } = kind;
        public string? Tag { get; } = tag;
        public string? TypedefName { get; set; }
        public Token At { get; set; } = at;
        public bool Defined { get; set; }
        public bool Unusable { get; set; }
        public int Packing { get; set; } = 8;
        public List<(string Name, CType Type)> Members { get; } = [];
        public List<(string Name, long Value)> Values { get; } = [];

        public string? Name => Tag ?? TypedefName;
    }

    /// <summary>A C type. <see cref="Spelling"/> is how its use wrote it, which a thunk name repeats.</summary>
    private sealed class CType
    {
        public Kind Kind { get; init; }
        public string Name { get; init; } = "";
        public string Spelling { get; init; } = "";
        public CType? Of { get; init; }
        public long Count { get; init; } = -1;
        public Aggregate? Aggregate { get; init; }
        public List<CType>? Parameters { get; init; }
        public bool Varargs { get; init; }
        public bool Unprototyped { get; init; }

        public static CType Primitive(string name, string? spelling = null) =>
            new() { Kind = Kind.Primitive, Name = name, Spelling = spelling ?? name.Replace(" ", "", StringComparison.Ordinal) };

        public static CType PointerTo(CType target) => new() { Kind = Kind.Pointer, Of = target, Spelling = "voidPtr" };

        public CType WithSpelling(string spelling) => new()
        {
            Kind = Kind,
            Name = Name,
            Spelling = spelling,
            Of = Of,
            Count = Count,
            Aggregate = Aggregate,
            Parameters = Parameters,
            Varargs = Varargs,
            Unprototyped = Unprototyped,
        };

        public bool IsVoid => Kind == Kind.Primitive && Name == "void";
    }

    /// <summary>What a declaration's specifiers said.</summary>
    private sealed record Specifiers(CType Type, bool Typedef, bool Extern, bool Static, bool Inline);

    // ------------------------------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------------------------------

    private Token Peek(int ahead = 0) => _tokens[Math.Min(_pos + ahead, _tokens.Count - 1)];

    private Token Next() => _tokens[Math.Min(_pos++, _tokens.Count - 1)];

    private bool Is(string text, int ahead = 0) => Peek(ahead) is { Kind: TokenKind.Punctuator or TokenKind.Identifier } t && t.Text == text;

    private bool AtEnd => Peek().Kind == TokenKind.End;

    private void Expect(string text)
    {
        if (!Is(text))
        {
            throw new FormatException($"expected '{text}' but found '{Peek().Text}'");
        }

        _pos++;
    }

    private void ParseUnit()
    {
        while (!AtEnd)
        {
            int start = _pos;
            try
            {
                ParseTopLevel();
            }
            catch (FormatException)
            {
                // A declaration this parser cannot read is skipped to its end, as R2025b skips what
                // its own parser cannot read; the rest of the unit still parses.
                _pos = Math.Max(_pos, start + 1);
                SkipToDeclarationEnd();
            }
        }
    }

    private void ParseTopLevel()
    {
        Token first = Peek();
        if (first.Kind == TokenKind.Pack)
        {
            Next();
            ApplyPack(first.Text);
            return;
        }

        if (Is(";"))
        {
            Next();
            return;
        }

        if (first.Kind == TokenKind.Identifier && first.Text is "_Static_assert" or "static_assert" or "__static_assert")
        {
            SkipToDeclarationEnd();
            return;
        }

        if (first.Kind == TokenKind.Identifier && first.Text == "extern" && Peek(1).Kind == TokenKind.Text)
        {
            Next();
            Next();
            if (Is("{"))
            {
                Next(); // extern "C" { … }: its declarations are top-level ones; the closing brace is skipped below
            }

            return;
        }

        if (Is("}"))
        {
            Next();
            return;
        }

        int declarationStart = _pos;
        Specifiers specifiers = ParseSpecifiers();
        if (Is(";"))
        {
            Next();
            NoteTypedefFailure(specifiers, declarationStart, null);
            return;
        }

        while (true)
        {
            Token nameAt = Peek();
            (string? name, CType type, _) = ParseDeclarator(specifiers.Type);
            if (Is("="))
            {
                SkipInitializer();
            }

            if (Is("{"))
            {
                SkipBalanced(); // a function definition (an inline function in a header): not an export
                return;
            }

            if (name is not null)
            {
                Declare(specifiers, name, type, nameAt, declarationStart);
            }

            if (Is(","))
            {
                Next();
                continue;
            }

            break;
        }

        Expect(";");
    }

    private void Declare(Specifiers specifiers, string name, CType type, Token nameAt, int declarationStart)
    {
        if (specifiers.Typedef)
        {
            if (NoteTypedefFailure(specifiers, declarationStart, name))
            {
                return; // the typedef stays unknown, so its uses warn as R2025b's do
            }

            if (type.Kind is Kind.Struct or Kind.Enum && type.Aggregate is { Tag: null, TypedefName: null } anonymous)
            {
                anonymous.TypedefName = name;
            }

            _typedefs[name] = type.WithSpelling(name);
            return;
        }

        if (!_named[nameAt.File] || specifiers.Static)
        {
            return;
        }

        string declaration = DeclarationText(declarationStart);
        if (type.Kind == Kind.Function)
        {
            if (!specifiers.Inline && _seenFunctions.Add(name))
            {
                RecordFunction(name, type, nameAt, declaration);
            }
        }
        else if (specifiers.Extern && _seenFunctions.Add(name))
        {
            _model.Functions.Add(new LibFunction(name, "data", DataType(type, nameAt), [], null, null) { Declaration = declaration });
        }
    }

    /// <summary>
    /// A typedef of a union or of a struct with bitfields is R2025b's "Failed to parse type": the name
    /// stays unknown. Answers whether this typedef is one; the union says so in the warnings.
    /// </summary>
    private bool NoteTypedefFailure(Specifiers specifiers, int declarationStart, string? name)
    {
        if (!specifiers.Typedef || name is null)
        {
            return false;
        }

        CType type = specifiers.Type;
        if (type.Kind == Kind.Union)
        {
            if (_named[_tokens[declarationStart].File])
            {
                string text = DeclarationText(declarationStart + 1).TrimEnd(';', ' ');
                Warn($"Failed to parse type '{text}' original input '{text}'", _tokens[declarationStart]);
            }

            return true;
        }

        return type.Kind == Kind.Struct && type.Aggregate is { Unusable: true };
    }

    private void RecordFunction(string name, CType function, Token at, string declaration)
    {
        if (function.Parameters!.Any(p => p.Kind == Kind.Pointer && p.Of!.Kind == Kind.Function && p.Spelling == "inline"))
        {
            return; // a parameter written as a function pointer: R2025b's parser drops the function
        }

        var rhs = new List<string>();
        var thunk = new StringBuilder();
        CType returns = function.Of!;
        string? lhs = returns.IsVoid ? null : MapType(returns, Context.Return, at);
        thunk.Append(returns.IsVoid ? "void" : ThunkSpelling(returns, lhs!));
        List<CType> parameters = function.Parameters ?? [];
        if (!function.Unprototyped && parameters.Count == 0)
        {
            thunk.Append("void");
        }

        foreach (CType parameter in parameters)
        {
            string type = MapType(parameter, Context.Parameter, at);
            rhs.Add(type);
            thunk.Append(ThunkSpelling(parameter, type));
        }

        string callType = "Thunk";
        string? thunkName = thunk.Append("Thunk").ToString();
        if (function.Varargs)
        {
            Warn("Failed to parse type '...' original input ' ...'", at);
            Warn($"Error parsing argument for function {name} function may be invalid.", at);
            rhs.Add("error");
            callType = "cdecl";
            thunkName = null;
        }

        _model.Functions.Add(new LibFunction(name, callType, lhs, rhs, null, thunkName) { Declaration = declaration });
    }

    /// <summary>The spelling a thunk name uses for a type: <c>voidPtr</c> for any pointer but a C string, a C type's own spelling for a struct or enum by value.</summary>
    private static string ThunkSpelling(CType type, string matlab) => matlab switch
    {
        "cstring" => "cstring",
        "single" => "float",
        "bool" => "_Bool",
        _ when LibTypes.IsPointer(matlab) || type.Kind is Kind.Pointer or Kind.Array => "voidPtr",
        _ when LibTypes.IsScalar(matlab) => matlab,
        _ => type.Spelling,
    };

    private string DeclarationText(int start)
    {
        var text = new StringBuilder();
        int depth = 0;
        for (int k = start; k < _tokens.Count && _tokens[k].Kind != TokenKind.End; k++)
        {
            Token t = _tokens[k];
            if (t.Kind == TokenKind.Identifier && t.Text is "__declspec" or "__attribute__")
            {
                k = SkipBalancedFrom(k + 1) - 1;
                continue;
            }

            if (t.Kind == TokenKind.Pack)
            {
                continue;
            }

            text.Append(t.Text).Append(' ');
            depth += t.Text switch { "{" or "(" => 1, "}" or ")" => -1, _ => 0 };
            if (t.Text == ";" && depth <= 0)
            {
                break;
            }
        }

        return text.ToString().TrimEnd();
    }

    private Specifiers ParseSpecifiers()
    {
        bool typedef = false, isExtern = false, isStatic = false, inline = false;
        bool signed = false, unsigned = false;
        int longs = 0;
        string? basic = null;
        CType? named = null;
        while (true)
        {
            Token t = Peek();
            if (t.Kind == TokenKind.Pack)
            {
                Next();
                ApplyPack(t.Text);
                continue;
            }

            if (t.Kind != TokenKind.Identifier)
            {
                break;
            }

            switch (t.Text)
            {
                case "typedef":
                    typedef = true;
                    Next();
                    continue;
                case "extern":
                    isExtern = true;
                    Next();
                    continue;
                case "static":
                    isStatic = true;
                    Next();
                    continue;
                case "inline" or "__inline" or "__inline__" or "__forceinline" or "_inline":
                    inline = true;
                    Next();
                    continue;
                case "__declspec" or "__attribute__" or "__attribute" or "_Alignas" or "alignas" or "__pragma" or "_declspec" or "__asm" or "__asm__":
                    Next();
                    SkipAttribute(t.Text);
                    continue;
                case "signed" or "__signed" or "__signed__":
                    signed = true;
                    Next();
                    continue;
                case "unsigned":
                    unsigned = true;
                    Next();
                    continue;
                case "long":
                    longs++;
                    Next();
                    continue;
                case "short":
                    basic = "short";
                    Next();
                    continue;
                case "int" or "char" or "float" or "double" or "void" or "_Bool" or "__int8" or "__int16" or "__int32" or "__int64" or "__wchar_t":
                    if (basic is "short" && t.Text == "int")
                    {
                        Next();
                        continue;
                    }

                    basic = t.Text;
                    Next();
                    continue;
                case "struct" or "union" or "enum":
                    named = ParseAggregate();
                    continue;
                default:
                    if (IsQualifier(t.Text))
                    {
                        Next();
                        continue;
                    }

                    if (basic is null && named is null && longs == 0 && !signed && !unsigned)
                    {
                        if (_typedefs.TryGetValue(t.Text, out CType? known))
                        {
                            Next();
                            named = known;
                            continue;
                        }

                        // A name used as a type that no typedef declared (a typedef that failed, or
                        // one from a header the parser could not read): R2025b's "was not found".
                        if (Peek(1) is { Kind: TokenKind.Identifier } or { Text: "*" or "(" or "&" })
                        {
                            Next();
                            named = new CType { Kind = Kind.Unknown, Name = t.Text, Spelling = t.Text };
                            continue;
                        }
                    }

                    break;
            }

            break;
        }

        CType type = named ?? CType.Primitive(Primitive(basic, longs, signed, unsigned));
        return new Specifiers(type, typedef, isExtern, isStatic, inline);
    }

    private static string Primitive(string? basic, int longs, bool signed, bool unsigned) => basic switch
    {
        "char" => unsigned ? "unsigned char" : signed ? "signed char" : "char",
        "__int8" => unsigned ? "unsigned char" : "signed char",
        "short" or "__int16" or "__wchar_t" when basic != "__wchar_t" => unsigned ? "unsigned short" : "short",
        "__wchar_t" => "unsigned short",
        "__int32" => unsigned ? "unsigned int" : "int",
        "__int64" => unsigned ? "unsigned long long" : "long long",
        "float" => "float",
        "double" => longs > 0 ? "long double" : "double",
        "void" => "void",
        "_Bool" => "_Bool",
        _ => longs switch
        {
            0 => unsigned ? "unsigned int" : "int",
            1 => unsigned ? "unsigned long" : "long",
            _ => unsigned ? "unsigned long long" : "long long",
        },
    };

    private static bool IsQualifier(string word) => word is
        "const" or "volatile" or "restrict" or "__restrict" or "__restrict__" or "_Restrict" or "__unaligned"
        or "__ptr32" or "__ptr64" or "__w64" or "__sptr" or "__uptr" or "register" or "auto" or "_Noreturn"
        or "__cdecl" or "_cdecl" or "__stdcall" or "_stdcall" or "__fastcall" or "__vectorcall" or "__clrcall"
        or "__thiscall" or "__extension__" or "_Atomic" or "_Nonnull" or "_Nullable" or "__volatile__" or "__const";

    private static bool IsCallingConvention(string word) => word is
        "__cdecl" or "_cdecl" or "__stdcall" or "_stdcall" or "__fastcall" or "__vectorcall" or "__clrcall" or "__thiscall";

    private void SkipAttribute(string keyword)
    {
        if (Is("("))
        {
            if (keyword == "__pragma")
            {
                // __pragma(pack(push, 8)) is the macro spelling of #pragma pack.
                int open = _pos;
                SkipBalanced();
                string inner = string.Concat(_tokens.Skip(open + 1).Take(_pos - open - 2).Select(t => t.Text + " ")).Trim();
                if (inner.StartsWith("pack", StringComparison.Ordinal))
                {
                    ApplyPack(PackArguments(inner[4..]));
                }

                return;
            }

            SkipBalanced();
        }
    }

    private CType ParseAggregate()
    {
        Token keyword = Next();
        Kind kind = keyword.Text switch { "struct" => Kind.Struct, "union" => Kind.Union, _ => Kind.Enum };
        while (Peek() is { Kind: TokenKind.Identifier } attribute && attribute.Text is "__declspec" or "__attribute__" or "_Alignas")
        {
            Next();
            SkipAttribute(attribute.Text);
        }

        string? tag = Peek().Kind == TokenKind.Identifier && !IsQualifier(Peek().Text) ? Next().Text : null;
        Dictionary<string, Aggregate> table = kind switch { Kind.Struct => _structs, Kind.Union => _unions, _ => _enums };
        Aggregate aggregate;
        if (tag is not null && table.TryGetValue(tag, out Aggregate? existing) && !(Is("{") && existing.Defined))
        {
            aggregate = existing;
        }
        else
        {
            aggregate = new Aggregate(kind, tag, keyword);
            if (tag is not null)
            {
                table[tag] = aggregate;
            }
        }

        if (Is("{"))
        {
            aggregate.At = keyword;
            aggregate.Defined = true;
            aggregate.Packing = _pack;
            if (kind == Kind.Enum)
            {
                ParseEnumBody(aggregate);
            }
            else
            {
                ParseMembers(aggregate);
            }

            _defined.Add(aggregate);
        }

        string spelling = tag is null ? "" : keyword.Text + tag;
        return new CType { Kind = kind, Name = tag ?? "", Spelling = spelling, Aggregate = aggregate };
    }

    private void ParseMembers(Aggregate aggregate)
    {
        Expect("{");
        while (!Is("}") && !AtEnd)
        {
            if (Peek().Kind == TokenKind.Pack)
            {
                ApplyPack(Next().Text);
                continue;
            }

            if (Is(";"))
            {
                Next();
                continue;
            }

            if (Peek() is { Kind: TokenKind.Identifier, Text: "_Static_assert" or "static_assert" })
            {
                SkipToDeclarationEnd();
                continue;
            }

            Specifiers specifiers = ParseSpecifiers();
            if (Is(";"))
            {
                aggregate.Unusable = true; // an anonymous struct or union member (an MSVC extension)
                Next();
                continue;
            }

            while (true)
            {
                if (Is(":"))
                {
                    Next();
                    SkipExpression(",", ";");
                    aggregate.Unusable = true; // an unnamed bitfield
                }
                else
                {
                    (string? name, CType type, _) = ParseDeclarator(specifiers.Type);
                    if (Is(":"))
                    {
                        Next();
                        SkipExpression(",", ";");
                        aggregate.Unusable = true; // bitfields: R2025b records no such struct
                    }

                    aggregate.Members.Add((name ?? "", type));
                }

                if (Is(","))
                {
                    Next();
                    continue;
                }

                break;
            }

            Expect(";");
        }

        Expect("}");
        if (aggregate.Kind == Kind.Union)
        {
            aggregate.Unusable = true;
        }
    }

    private void ParseEnumBody(Aggregate aggregate)
    {
        Expect("{");
        long next = 0;
        while (!Is("}") && !AtEnd)
        {
            string name = Next().Text;
            long value = next;
            if (Is("="))
            {
                Next();
                int start = _pos;
                SkipExpression(",", "}");
                if (Evaluate(start, _pos) is { } evaluated)
                {
                    value = evaluated;
                }
            }

            aggregate.Values.Add((name, value));
            _constants[name] = value;
            next = value + 1;
            if (Is(","))
            {
                Next();
            }
        }

        Expect("}");
    }

    /// <summary>
    /// Parses a declarator over <paramref name="baseType"/>: pointers, then a name (or none, for a
    /// parameter), or a parenthesized declarator, then array and function suffixes, which bind
    /// tighter than the pointers before them — so <c>int *a[3]</c> is three pointers and
    /// <c>int (*f)(int)</c> a pointer to a function. Answers whether the declarator was parenthesized.
    /// </summary>
    private (string? Name, CType Type, bool Nested) ParseDeclarator(CType baseType)
    {
        CType type = baseType;
        while (true)
        {
            Token t = Peek();
            if (t.Kind == TokenKind.Punctuator && t.Text == "*")
            {
                Next();
                type = CType.PointerTo(type);
                continue;
            }

            if (t.Kind == TokenKind.Identifier && (IsQualifier(t.Text) || t.Text is "__declspec" or "__attribute__"))
            {
                Next();
                SkipAttribute(t.Text);
                continue;
            }

            break;
        }

        if (Is("(") && (Peek(1) is { Text: "*" or "^" or "(" } || (Peek(1).Kind == TokenKind.Identifier && (IsCallingConvention(Peek(1).Text) || Peek(1).Text is "__declspec" or "__attribute__"))))
        {
            Next();
            int inner = _pos;
            SkipBalancedInside();
            type = ParseSuffixes(type);
            int after = _pos;
            _pos = inner;
            (string? name, CType nested, _) = ParseDeclarator(type);
            Expect(")");
            _pos = after;
            return (name, nested.Kind == Kind.Pointer && nested.Of!.Kind == Kind.Function ? MarkInline(nested) : nested, true);
        }

        string? declared = null;
        if (Peek() is { Kind: TokenKind.Identifier } word && !IsTypeWord(word.Text) && !IsQualifier(word.Text) && word.Text is not ("__attribute__" or "__declspec"))
        {
            declared = Next().Text;
        }

        SkipTrailingAttributes();
        type = ParseSuffixes(type);
        SkipTrailingAttributes();
        return (declared, type, false);
    }

    /// <summary>Skips what may follow a declarator's name or parameters: <c>__attribute__((…))</c>, <c>__declspec(…)</c>, <c>__asm(…)</c>, qualifiers.</summary>
    private void SkipTrailingAttributes()
    {
        while (Peek() is { Kind: TokenKind.Identifier } trailing
            && (trailing.Text is "__attribute__" or "__declspec" or "__asm" or "__asm__" || IsQualifier(trailing.Text)))
        {
            Next();
            SkipAttribute(trailing.Text);
        }
    }

    private static CType MarkInline(CType pointer) => pointer.WithSpelling("inline");

    private static bool IsTypeWord(string word) => word is
        "int" or "char" or "short" or "long" or "float" or "double" or "void" or "signed" or "unsigned" or "_Bool"
        or "struct" or "union" or "enum" or "const" or "volatile";

    private CType ParseSuffixes(CType type)
    {
        var suffixes = new List<CType>();
        while (true)
        {
            if (Is("["))
            {
                Next();
                int start = _pos;
                SkipExpression("]");
                long count = start == _pos ? -1 : Evaluate(start, _pos) ?? -1;
                Expect("]");
                suffixes.Add(new CType { Kind = Kind.Array, Count = count });
            }
            else if (Is("("))
            {
                suffixes.Add(ParseParameters());
            }
            else
            {
                break;
            }
        }

        for (int k = suffixes.Count - 1; k >= 0; k--)
        {
            CType suffix = suffixes[k];
            type = suffix.Kind == Kind.Array
                ? new CType { Kind = Kind.Array, Of = type, Count = suffix.Count, Spelling = "voidPtr" }
                : new CType { Kind = Kind.Function, Of = type, Parameters = suffix.Parameters, Varargs = suffix.Varargs, Unprototyped = suffix.Unprototyped };
        }

        return type;
    }

    private CType ParseParameters()
    {
        Expect("(");
        var parameters = new List<CType>();
        bool varargs = false;
        if (Is(")"))
        {
            Next();
            return new CType { Kind = Kind.Function, Parameters = parameters, Unprototyped = true };
        }

        if (Is("void") && Is(")", 1))
        {
            Next();
            Next();
            return new CType { Kind = Kind.Function, Parameters = parameters };
        }

        while (!AtEnd)
        {
            if (Is("..."))
            {
                Next();
                varargs = true;
            }
            else
            {
                Specifiers specifiers = ParseSpecifiers();
                (_, CType type, _) = ParseDeclarator(specifiers.Type);
                parameters.Add(type);
            }

            if (Is(","))
            {
                Next();
                continue;
            }

            break;
        }

        Expect(")");
        return new CType { Kind = Kind.Function, Parameters = parameters, Varargs = varargs };
    }

    // ------------------------------------------------------------------------------------------
    // MATLAB's type names
    // ------------------------------------------------------------------------------------------

    private enum Context : byte
    {
        Parameter,
        Return,
        Member,
    }

    private string MapType(CType type, Context context, Token at)
    {
        switch (type.Kind)
        {
            case Kind.Primitive:
                return PrimitiveName(type.Name) ?? NotFound(type.Name.Replace(" ", "", StringComparison.Ordinal), "error", at);
            case Kind.Struct:
                return type.Aggregate?.Name ?? "error";
            case Kind.Enum:
                return type.Aggregate?.Name ?? "error";
            case Kind.Union:
                return "error";
            case Kind.Unknown:
                return NotFound(type.Name, "error", at);
            case Kind.Pointer:
                return MapPointer(type.Of!, 1, fromArray: false, at);
            case Kind.Array when context == Context.Member:
                long count = 1;
                CType element = type;
                while (element.Kind == Kind.Array)
                {
                    count *= Math.Max(element.Count, 0);
                    element = element.Of!;
                }

                return $"{MapType(element, Context.Member, at)}#{count}";
            case Kind.Array:
                return MapPointer(type.Of!, 1, fromArray: true, at);
            default:
                return "error";
        }
    }

    private string MapPointer(CType target, int level, bool fromArray, Token at)
    {
        bool directArray = fromArray;
        while (target.Kind is Kind.Pointer or Kind.Array)
        {
            if (target.Kind == Kind.Pointer)
            {
                level++;
                directArray = false;
            }

            target = target.Of!;
        }

        string ptrs = string.Concat(Enumerable.Repeat("Ptr", level));
        switch (target.Kind)
        {
            case Kind.Function:
                return level == 1 ? "FcnPtr" : "voidPtr";
            case Kind.Unknown:
                return NotFound(target.Name + "Ptr", "voidPtr", at);
            case Kind.Union:
                return "voidPtr";
            case Kind.Primitive when target.Name == "void":
                return "void" + ptrs;
            case Kind.Primitive when target.Spelling is "char" or "signedchar" && !(directArray && level == 1):
                return level switch { 1 => "cstring", 2 => "stringPtrPtr", _ => "int8" + ptrs };
        }

        string name = MapType(target, Context.Parameter, at);
        return name == "error" ? "voidPtr" : name + ptrs;
    }

    private string DataType(CType type, Token at) =>
        type.Kind == Kind.Array ? MapPointer(type.Of!, 1, fromArray: true, at) : MapType(type, Context.Member, at) + "Ptr";

    private static string? PrimitiveName(string c) => c switch
    {
        "char" or "signed char" => "int8",
        "unsigned char" => "uint8",
        "short" => "int16",
        "unsigned short" => "uint16",
        "int" => "int32",
        "unsigned int" => "uint32",
        "long" => "long",
        "unsigned long" => "ulong",
        "long long" => "int64",
        "unsigned long long" => "uint64",
        "float" => "single",
        "double" => "double",
        "_Bool" => "bool",
        "void" => "void",
        _ => null,
    };

    private string NotFound(string name, string defaulted, Token at)
    {
        Warn($"Type '{name}' was not found.  Defaulting to type {defaulted}.", at);
        return defaulted;
    }

    private void Warn(string message, Token at)
    {
        if (_named[at.File])
        {
            _warnings.Add((message, at));
        }
    }

    /// <summary>
    /// The warnings as R2025b lays them out (probe_shrlib_readback): the header's name, then each
    /// warning and where it was found — a blank line after a missing type, none after a failed
    /// parse, and no place at all for the "Error parsing argument" line that follows a varargs one.
    /// </summary>
    private string WarningText(string label)
    {
        if (_warnings.Count == 0)
        {
            return "";
        }

        var text = new StringBuilder(label).Append("\n\n");
        foreach ((string message, Token at) in _warnings)
        {
            if (message.StartsWith("Error parsing argument", StringComparison.Ordinal))
            {
                text.Append(message).Append('\n');
                continue;
            }

            text.Append(message).Append(message.StartsWith("Type '", StringComparison.Ordinal) ? "\n\n" : "\n")
                .Append(CultureInfo.InvariantCulture, $"Found on line {at.InputLine} of input from line {at.Line} of file {_files[at.File].Replace("\\", "\\\\", StringComparison.Ordinal)}")
                .Append("\n\n");
        }

        return text.ToString();
    }

    /// <summary>The structs and enums the named headers define, in their order, with their MATLAB member types.</summary>
    private void RecordAggregates()
    {
        foreach (Aggregate aggregate in _defined)
        {
            if (!_named[aggregate.At.File] || aggregate.Name is not { } name)
            {
                continue;
            }

            if (aggregate.Kind == Kind.Enum)
            {
                if (_model.Enum(name) is null)
                {
                    _model.Enums.Add(new LibEnum(name, aggregate.Values));
                }
            }
            else if (aggregate.Kind == Kind.Struct && !aggregate.Unusable && _model.Struct(name) is null)
            {
                var members = aggregate.Members.Select(m => new LibMember(m.Name, MapType(m.Type, Context.Member, aggregate.At))).ToList();
                _model.Structs.Add(new LibStruct(name, members, aggregate.Packing == 8 ? null : aggregate.Packing));
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // #pragma pack, constant expressions, skipping
    // ------------------------------------------------------------------------------------------

    private void ApplyPack(string arguments)
    {
        string[] parts = arguments.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            _pack = 8;
            return;
        }

        switch (parts[0])
        {
            case "push":
                _packStack.Push(_pack);
                if (parts.Length > 1 && PackValue(parts[^1]) is { } pushed)
                {
                    _pack = pushed;
                }

                return;
            case "pop":
                if (_packStack.Count > 0)
                {
                    _pack = _packStack.Pop();
                }

                if (parts.Length > 1 && PackValue(parts[^1]) is { } popped)
                {
                    _pack = popped;
                }

                return;
            case "show":
                return;
            default:
                if (PackValue(parts[0]) is { } value)
                {
                    _pack = value;
                }

                return;
        }
    }

    private static int? PackValue(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is 1 or 2 or 4 or 8 or 16
            ? value
            : text.StartsWith('_') ? 8 : null; // _CRT_PACKING and its kind, left unexpanded in a pragma, are 8

    /// <summary>The value of the constant expression in tokens [<paramref name="start"/>, <paramref name="end"/>), or null.</summary>
    private long? Evaluate(int start, int end)
    {
        var evaluator = new ConstantEvaluator(_tokens, start, end, _constants, _typedefs.ContainsKey);
        return evaluator.Run();
    }

    private void SkipExpression(params string[] stops)
    {
        int depth = 0;
        while (!AtEnd)
        {
            Token t = Peek();
            if (depth == 0 && t.Kind == TokenKind.Punctuator && stops.Contains(t.Text))
            {
                return;
            }

            depth += t.Text switch { "(" or "[" or "{" => 1, ")" or "]" or "}" => -1, _ => 0 };
            if (depth < 0)
            {
                return;
            }

            Next();
        }
    }

    private void SkipInitializer()
    {
        Next(); // '='
        SkipExpression(",", ";");
    }

    /// <summary>Skips a balanced group starting at the current token, which must open it.</summary>
    private void SkipBalanced() => _pos = SkipBalancedFrom(_pos);

    private int SkipBalancedFrom(int from)
    {
        if (from >= _tokens.Count || _tokens[from].Text is not ("(" or "[" or "{"))
        {
            return from;
        }

        int depth = 0;
        for (int k = from; k < _tokens.Count; k++)
        {
            string text = _tokens[k].Kind == TokenKind.Punctuator ? _tokens[k].Text : "";
            depth += text switch { "(" or "[" or "{" => 1, ")" or "]" or "}" => -1, _ => 0 };
            if (depth == 0)
            {
                return k + 1;
            }
        }

        return _tokens.Count - 1;
    }

    /// <summary>Having just passed an opening parenthesis, moves past its closing one.</summary>
    private void SkipBalancedInside()
    {
        int depth = 1;
        while (!AtEnd)
        {
            Token t = Next();
            if (t.Kind != TokenKind.Punctuator)
            {
                continue;
            }

            depth += t.Text switch { "(" or "[" or "{" => 1, ")" or "]" or "}" => -1, _ => 0 };
            if (depth == 0)
            {
                return;
            }
        }
    }

    private void SkipToDeclarationEnd()
    {
        int depth = 0;
        while (!AtEnd)
        {
            Token t = Next();
            if (t.Kind != TokenKind.Punctuator)
            {
                continue;
            }

            if (t.Text is "(" or "[" or "{")
            {
                depth++;
            }
            else if (t.Text is ")" or "]" or "}")
            {
                depth--;
                if (depth <= 0 && t.Text == "}" && !Is(";") && Peek().Kind != TokenKind.Identifier)
                {
                    return;
                }
            }
            else if (t.Text == ";" && depth <= 0)
            {
                return;
            }
        }
    }

    /// <summary>A C constant expression over integers: what an enum value or an array size is written as.</summary>
    private sealed class ConstantEvaluator(List<Token> tokens, int start, int end, Dictionary<string, long> constants, Func<string, bool> isTypedef)
    {
        private int _at = start;

        public long? Run()
        {
            try
            {
                long value = Conditional();
                return _at == end ? value : null;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (DivideByZeroException)
            {
                return null;
            }
        }

        private string Current => _at < end ? tokens[_at].Text : "";

        private bool Accept(string text)
        {
            if (_at < end && tokens[_at].Kind == TokenKind.Punctuator && tokens[_at].Text == text)
            {
                _at++;
                return true;
            }

            return false;
        }

        private long Conditional()
        {
            long condition = Binary(0);
            if (Accept("?"))
            {
                long yes = Conditional();
                if (!Accept(":"))
                {
                    throw new FormatException();
                }

                long no = Conditional();
                return condition != 0 ? yes : no;
            }

            return condition;
        }

        private static readonly string[][] Levels =
        [
            ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"],
        ];

        private long Binary(int level)
        {
            if (level == Levels.Length)
            {
                return Unary();
            }

            long left = Binary(level + 1);
            while (_at < end && tokens[_at].Kind == TokenKind.Punctuator && Levels[level].Contains(tokens[_at].Text))
            {
                string op = tokens[_at++].Text;
                long right = Binary(level + 1);
                left = op switch
                {
                    "||" => left != 0 || right != 0 ? 1 : 0,
                    "&&" => left != 0 && right != 0 ? 1 : 0,
                    "|" => left | right,
                    "^" => left ^ right,
                    "&" => left & right,
                    "==" => left == right ? 1 : 0,
                    "!=" => left != right ? 1 : 0,
                    "<" => left < right ? 1 : 0,
                    ">" => left > right ? 1 : 0,
                    "<=" => left <= right ? 1 : 0,
                    ">=" => left >= right ? 1 : 0,
                    "<<" => left << (int)right,
                    ">>" => left >> (int)right,
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => left / right,
                    _ => left % right,
                };
            }

            return left;
        }

        private long Unary()
        {
            if (Accept("-"))
            {
                return -Unary();
            }

            if (Accept("+"))
            {
                return Unary();
            }

            if (Accept("~"))
            {
                return ~Unary();
            }

            if (Accept("!"))
            {
                return Unary() == 0 ? 1 : 0;
            }

            if (Current == "sizeof")
            {
                _at++;
                return SizeOf();
            }

            if (Current == "(" && _at + 1 < end && IsTypeStart(tokens[_at + 1]))
            {
                // A cast: the type is skipped, the value kept.
                while (_at < end && !Accept(")"))
                {
                    _at++;
                }

                return Unary();
            }

            return Primary();
        }

        private bool IsTypeStart(Token token) =>
            token.Kind == TokenKind.Identifier && (IsTypeWord(token.Text) || isTypedef(token.Text) || token.Text.StartsWith("__int", StringComparison.Ordinal));

        private long SizeOf()
        {
            bool parenthesized = Accept("(");
            var words = new List<string>();
            int pointers = 0;
            while (_at < end && !(parenthesized && Current == ")"))
            {
                if (Current == "*")
                {
                    pointers++;
                }
                else
                {
                    words.Add(Current);
                }

                _at++;
                if (!parenthesized)
                {
                    break;
                }
            }

            if (parenthesized && !Accept(")"))
            {
                throw new FormatException();
            }

            if (pointers > 0)
            {
                return 8;
            }

            words.RemoveAll(w => w is "const" or "volatile" or "signed" or "unsigned");
            return string.Join(' ', words) switch
            {
                "char" or "__int8" or "_Bool" => 1,
                "short" or "short int" or "__int16" or "wchar_t" => 2,
                "int" or "long" or "long int" or "float" or "__int32" or "" => 4,
                "long long" or "__int64" or "double" or "size_t" or "long double" => 8,
                _ => throw new FormatException(),
            };
        }

        private long Primary()
        {
            if (Accept("("))
            {
                long value = Conditional();
                if (!Accept(")"))
                {
                    throw new FormatException();
                }

                return value;
            }

            if (_at >= end)
            {
                throw new FormatException();
            }

            Token token = tokens[_at++];
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return IntegerLiteral(token.Text);
                case TokenKind.Character:
                    return CharacterLiteral(token.Text);
                case TokenKind.Identifier when constants.TryGetValue(token.Text, out long constant):
                    return constant;
                default:
                    throw new FormatException();
            }
        }

        private static long IntegerLiteral(string text)
        {
            string digits = text.TrimEnd('u', 'U', 'l', 'L');
            if (digits.EndsWith("i64", StringComparison.OrdinalIgnoreCase) || digits.EndsWith("i32", StringComparison.OrdinalIgnoreCase))
            {
                digits = digits[..^3].TrimEnd('u', 'U');
            }

            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return (long)ulong.Parse(digits.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            if (digits.Length > 1 && digits[0] == '0' && digits.All(char.IsDigit))
            {
                return Convert.ToInt64(digits, 8);
            }

            if (ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            {
                return (long)value;
            }

            throw new FormatException();
        }

        private static long CharacterLiteral(string text)
        {
            string body = text.Length >= 2 ? text[1..^1] : "";
            if (body.Length == 1)
            {
                return body[0];
            }

            if (body.Length >= 2 && body[0] == '\\')
            {
                return body[1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' when body.Length == 2 => 0,
                    'a' => 7,
                    'b' => 8,
                    'f' => 12,
                    'v' => 11,
                    'x' => Convert.ToInt64(body[2..], 16),
                    >= '0' and <= '7' => Convert.ToInt64(body[1..], 8),
                    _ => body[1],
                };
            }

            throw new FormatException();
        }
    }
}
