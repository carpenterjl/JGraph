namespace JGraph.Scripting.Jgs;

/// <summary>The lexical categories the <see cref="Lexer"/> produces.</summary>
internal enum TokenType
{
    // Literals and identifiers.
    Number,
    ImaginaryNumber, // 2i / 1.5i / 3j — the numeric text without the suffix is in Token.Number
    String,
    Identifier,

    // Keywords.
    Let,
    Fn,
    Return,
    If,
    Else,
    For,
    While,
    In,
    Break,
    Continue,
    True,
    False,
    End,     // MATLAB block terminator, and "last index" inside an index expression
    ElseIf,  // MATLAB 'elseif'

    // MATLAB-only keywords (the JGS lexer never produces these).
    Function,
    Switch,
    Case,
    Otherwise,
    Try,
    Catch,
    Global,

    // Punctuation and operators.
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Comma,
    Assign,       // =
    PlusAssign,   // +=
    MinusAssign,  // -=
    StarAssign,   // *=
    SlashAssign,  // /=
    PercentAssign, // %=
    PlusPlus,     // ++
    MinusMinus,   // --
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Caret,        // ^ (also the target of '.^')
    Colon,        // : (MATLAB range, and 'all' inside an index)
    Bang,         // !
    EqualEqual,   // ==
    BangEqual,    // !=
    Less,         // <
    LessEqual,    // <=
    Greater,      // >
    GreaterEqual, // >=
    AmpAmp,       // &&
    PipePipe,     // ||

    // MATLAB-only punctuation (the JGS lexer never produces these).
    DotStar,      // .* — elementwise multiply, where '*' is matrix multiplication
    DotSlash,     // ./ — elementwise divide
    DotCaret,     // .^ — elementwise power
    Amp,          // & — elementwise logical AND (non-short-circuiting)
    Pipe,         // | — elementwise logical OR
    Transpose,    // ' — complex-conjugate transpose
    DotTranspose, // .' — plain transpose
    Dot,          // . — struct field access
    At,           // @ — function handle / anonymous function

    /// <summary>A statement separator (a significant newline).</summary>
    Newline,

    /// <summary>';' — a statement separator that also suppresses console echo (and separates array rows).</summary>
    Semicolon,

    /// <summary>End of input.</summary>
    Eof,
}

/// <summary>
/// A single lexical token with its source position. <see cref="Text"/> holds the raw lexeme (or the decoded
/// value for strings); <see cref="Number"/> holds the parsed value for numeric tokens.
/// </summary>
/// <param name="Type">The lexical category.</param>
/// <param name="Text">The raw lexeme, or a string literal's decoded value.</param>
/// <param name="Number">The parsed value of a numeric token.</param>
/// <param name="Line">The 1-based source line.</param>
/// <param name="Column">The 1-based source column.</param>
/// <param name="PrecededByWhitespace">Whether whitespace (or a <c>...</c> continuation) separated this
/// token from the one before it. MATLAB needs it to read <c>[1 -2]</c> as two elements and
/// <c>[1 - 2]</c> as one, and to tell <c>a'</c> (transpose) from <c>[a 'b']</c> (a char literal).</param>
internal readonly record struct Token(
    TokenType Type, string Text, double Number, int Line, int Column, bool PrecededByWhitespace = false)
{
    /// <summary>A short human-readable description of the token, for error messages.</summary>
    public string Describe() => Type switch
    {
        TokenType.Eof => "end of input",
        TokenType.Newline => "end of line",
        TokenType.Number => $"number '{Text}'",
        TokenType.ImaginaryNumber => $"number '{Text}'",
        TokenType.String => "string",
        TokenType.Identifier => $"'{Text}'",
        _ => $"'{Text}'",
    };
}
